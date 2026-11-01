using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.Core.Tests;

/// <summary>Roundtrip tests for the newline-delimited JSON protocol codec.</summary>
public class ProtocolCodecTests
{
    [Fact]
    public void Command_Roundtrip()
    {
        var envelope = CommandEnvelope.Create("extrude", new JsonObject
        {
            ["height"] = 3.0,
            ["operation"] = "new_body",
        });

        var line = envelope.Encode();

        // Wire form must match the reference protocol: {"type": ..., "params": ...}
        Assert.Contains("\"type\":\"extrude\"", line);
        Assert.Contains("\"params\"", line);
        Assert.DoesNotContain('\n', line);

        var decoded = ProtocolCodec.ParseCommand(line);
        Assert.Equal("extrude", decoded.Command);
        Assert.Equal(3.0, decoded.Params!["height"]!.GetValue<double>());
        Assert.Equal("new_body", decoded.Params!["operation"]!.GetValue<string>());
    }

    [Fact]
    public void Command_WithoutParams_UsesEmptyObject()
    {
        var line = CommandEnvelope.Create("ping").Encode();

        var decoded = ProtocolCodec.ParseCommand(line);
        Assert.Equal("ping", decoded.Command);
        Assert.NotNull(decoded.Params);
        Assert.Empty((System.Text.Json.Nodes.JsonObject)decoded.Params!);
    }

    [Fact]
    public void Command_MissingType_Throws()
    {
        Assert.Throws<FormatException>(() => ProtocolCodec.ParseCommand("{\"params\":{}}"));
    }

    [Fact]
    public void Command_MalformedJson_Throws()
    {
        // Malformed frames throw a JsonException subclass (JsonReaderException
        // on the STJ version in play) — ThrowsAny accepts the subclass.
        var ex = Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => ProtocolCodec.ParseCommand("{not json"));

        Assert.Contains("invalid start of a property name", ex.Message);
    }

    [Fact]
    public void Success_Response_Roundtrip()
    {
        var payload = new JsonObject { ["body_name"] = "Body1", ["deltas"] = new JsonObject { ["body_count_delta"] = 1 } };
        var result = CommandResult.Success(payload);

        var line = ProtocolCodec.EncodeSuccess(result);
        var frame = ProtocolCodec.ParseResponse(line);

        Assert.True(frame.IsSuccess);
        Assert.NotNull(frame.Result);
        Assert.True(frame.Result!.Ok);
        Assert.Equal("Body1", frame.Result.Field("body_name")?.GetValue<string>());
        Assert.NotNull(frame.Result.Deltas);
        Assert.Equal(ErrorKind.Unknown, ErrorKindExtensions.FromWireString(null!));
    }

    [Fact]
    public void Error_Response_WithKindAndHints()
    {
        var line = ProtocolCodec.EncodeError(
            "Boolean subtract failed — empty result",
            ErrorKind.BooleanNoOp,
            new[] { "Verify the tool body overlaps the target.", "Use check_interference." });

        var frame = ProtocolCodec.ParseResponse(line);

        Assert.True(frame.IsError);
        Assert.Equal("Boolean subtract failed — empty result", frame.Message);
        Assert.Equal(ErrorKind.BooleanNoOp, frame.ErrorKind);
        Assert.Equal(2, frame.Hints.Length);
        Assert.Equal("Verify the tool body overlaps the target.", frame.Hints[0]);
    }

    [Fact]
    public void UnknownCommand_Response_IsClassified()
    {
        var line = ProtocolCodec.EncodeError("Unknown command: frobnicate", ErrorKind.UnknownCommand);
        var frame = ProtocolCodec.ParseResponse(line);

        Assert.Equal(ErrorKind.UnknownCommand, frame.ErrorKind);
    }

    [Fact]
    public void Failure_Result_CarriesReferenceShape()
    {
        var failure = CommandResult.Failure(
            ErrorKind.ProfileNotClosed,
            "No profiles in sketch",
            new[] { "The sketch has no closed profile to extrude/revolve." },
            "Traceback (most recent call last): ...");

        var json = failure.ToJson();

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("\"error_kind\":\"PROFILE_NOT_CLOSED\"", json);
        Assert.Contains("\"error_message\":\"No profiles in sketch\"", json);
        Assert.Contains("traceback", json);

        var parsed = CommandResult.FromJson(json);
        Assert.False(parsed.Ok);
        Assert.Equal(ErrorKind.ProfileNotClosed, parsed.ErrorKind);
        Assert.Equal("No profiles in sketch", parsed.ErrorMessage);
        Assert.Single(parsed.Hints);
        Assert.Equal("Traceback (most recent call last): ...", parsed.Traceback);
    }

    [Fact]
    public void Success_Result_AlwaysHasOkMarker()
    {
        var result = CommandResult.Success(new JsonObject { ["status"] = "pong" });

        Assert.True(result.Ok);
        Assert.Equal("pong", result.Field("status")?.GetValue<string>());
    }
}

/// <summary>Retry/timeout semantics ported from the reference connection.py.</summary>
public class Fusion360ConnectionTests
{
    /// <summary>A fake add-in: replies to one command on a localhost TCP socket.</summary>
    private sealed class FakeAddIn : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Thread _thread;

        public int Port { get; }
        public List<string> ReceivedCommands { get; } = new();

        public FakeAddIn(string? response = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _thread = new Thread(() =>
            {
                try
                {
                    using var client = _listener.AcceptTcpClient();
                    using var reader = new StreamReader(client.GetStream());
                    var line = reader.ReadLine();
                    if (line is not null)
                    {
                        ReceivedCommands.Add(line);
                    }

                    if (response is not null)
                    {
                        var stream = client.GetStream();
                        stream.Write(System.Text.Encoding.UTF8.GetBytes(response + "\n"));
                        stream.Flush();
                    }
                }
                catch
                {
                    // The connection may be dropped by the test — not an error.
                }
            })
            { IsBackground = true };
            _thread.Start();
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    [Fact]
    public void SendCommand_Success_ReadsResult()
    {
        const string response = "{\"status\":\"success\",\"result\":{\"ok\":true,\"status\":\"pong\"}}";
        using var addIn = new FakeAddIn(response);
        using var connection = new Fusion360Connection(IPAddress.Loopback.ToString(), addIn.Port);

        var result = connection.SendCommand("ping", timeoutSeconds: 5);

        Assert.True(result.Ok);
        Assert.Equal("pong", result.Field("status")?.GetValue<string>());
        Assert.Contains("\"type\":\"ping\"", addIn.ReceivedCommands[0]);
    }

    [Fact]
    public async Task ConcurrentCalls_KeepEachResponseWithItsRequest()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var reader = new StreamReader(client.GetStream());
            using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
            for (var i = 0; i < 2; i++)
            {
                var request = System.Text.Json.JsonDocument.Parse(reader.ReadLine()!).RootElement;
                var command = request.GetProperty("type").GetString();
                writer.WriteLine($"{{\"status\":\"success\",\"result\":{{\"ok\":true,\"command\":\"{command}\"}}}}");
            }
        });
        using var connection = new Fusion360Connection(IPAddress.Loopback.ToString(), port);

        var first = Task.Run(() => connection.SendCommand("get_scene_info", timeoutSeconds: 5));
        var second = Task.Run(() => connection.SendCommand("get_design_type", timeoutSeconds: 5));

        var results = await Task.WhenAll(first, second);
        await server;
        Assert.Equal("get_scene_info", results[0].Field("command")!.GetValue<string>());
        Assert.Equal("get_design_type", results[1].Field("command")!.GetValue<string>());
    }

    [Fact]
    public void ApplicationError_ReturnsResultWithoutThrowing()
    {
        // status "success" + ok:false is the reference's application-error
        // shape (command_handler catches handler exceptions): the client
        // receives the structured result and the MCP server formats it —
        // no exception is raised (unlike the status:"error" frames).
        const string response =
            "{\"status\":\"success\",\"result\":{\"ok\":false,\"error_kind\":\"SKETCH_NOT_FOUND\"," +
            "\"error_message\":\"Sketch 'Nope' not found\",\"hints\":[\"List sketches via get_scene_info\"],\"traceback\":\"tb\"}}";
        using var addIn = new FakeAddIn(response);
        using var connection = new Fusion360Connection(IPAddress.Loopback.ToString(), addIn.Port);

        var result = connection.SendCommand("extrude", new JsonObject { ["sketch_name"] = "Nope" }, 0, 5);

        Assert.False(result.Ok);
        Assert.Equal(ErrorKind.SketchNotFound, result.ErrorKind);
        Assert.Equal("Sketch 'Nope' not found", result.ErrorMessage);
        Assert.Equal("List sketches via get_scene_info", result.Hints[0]);
        Assert.Equal("tb", result.Traceback);
    }

    [Fact]
    public void SendCommand_BareErrorMessage_FallsBackToClassifier()
    {
        const string response = "{\"status\":\"error\",\"message\":\"timeout while running cam toolpath\"}";
        using var addIn = new FakeAddIn(response);
        using var connection = new Fusion360Connection(IPAddress.Loopback.ToString(), addIn.Port);

        var ex = Assert.Throws<FusionError>(() => connection.SendCommand("cam_generate_toolpath", null, 0, 5));

        Assert.Equal(ErrorKind.Timeout, ex.Kind);
        Assert.NotEmpty(ex.Hints);
    }

    [Fact]
    public void Ping_WithoutServer_IsFalse()
    {
        // A port with no listener: pick a closed one by opening and closing a listener.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var connection = new Fusion360Connection(IPAddress.Loopback.ToString(), port);

        Assert.False(connection.Ping());
    }

    [Fact]
    public void EnsureConnected_Reconnects()
    {
        var connection = new Fusion360Connection(IPAddress.Loopback.ToString(), 1); // port 1: nothing there
        Assert.False(connection.Connected);
        Assert.False(connection.EnsureConnected());
        connection.Dispose();
    }

    [Fact]
    public void MutationCommands_MatchReferenceSet()
    {
        // The reference mock set includes execute_code, which this port excludes.
        Assert.Equal(40, Fusion360.Mcp.Core.Mock.MockResponses.MutationCommands.Count);
        Assert.Contains("extrude", Fusion360.Mcp.Core.Mock.MockResponses.MutationCommands);
        Assert.Contains("delete_all", Fusion360.Mcp.Core.Mock.MockResponses.MutationCommands);
        Assert.DoesNotContain("ping", Fusion360.Mcp.Core.Mock.MockResponses.MutationCommands);
        Assert.DoesNotContain("get_scene_info", Fusion360.Mcp.Core.Mock.MockResponses.MutationCommands);
    }
}
