using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;
using Fusion360.Mcp.Server;
using ModelContextProtocol.Protocol;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>Unit tests for the reference-faithful tool-result rendering.</summary>
public class ResultFormatterTests
{
    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    [Fact]
    public void FormatResult_Success_RendersOkHeaderAndFields()
    {
        var result = new JsonObject
        {
            ["ok"] = true,
            ["body_name"] = "Body1",
            ["height"] = 5.0,
            ["mode"] = "mock",
        };

        var formatted = ResultFormatter.FormatResult("create_box", result);

        Assert.False(formatted.IsError);
        var text = Text(formatted);
        Assert.StartsWith("**create_box** OK", text);
        Assert.Contains("  body_name: Body1", text);
        Assert.Contains("  height: 5", text);
        // "ok" is a special key (suppressed); "mode" is NOT special, so it renders.
        Assert.DoesNotContain("  ok: ", text);
        Assert.Contains("  mode: mock", text);
    }

    [Fact]
    public void FormatResult_Error_RendersClassifiedFailure()
    {
        var result = new JsonObject
        {
            ["ok"] = false,
            ["error_kind"] = "PROFILE_NOT_CLOSED",
            ["error_message"] = "sketch has no closed profile",
            ["hints"] = new JsonArray("close the sketch", "use a closed profile"),
            ["traceback"] = "at some line",
            ["mode"] = "mock",
        };

        var formatted = ResultFormatter.FormatResult("extrude", result);

        Assert.True(formatted.IsError);
        var text = Text(formatted);
        Assert.Contains("**extrude** ERROR (PROFILE_NOT_CLOSED)", text);
        Assert.Contains("sketch has no closed profile", text);
        Assert.Contains("hints:", text);
        Assert.Contains("- close the sketch", text);
        Assert.Contains("traceback:", text);
        Assert.Contains("at some line", text);
    }

    [Fact]
    public void FormatResult_Mutation_RendersDeltas()
    {
        var result = new JsonObject
        {
            ["ok"] = true,
            ["body_name"] = "Box1",
            ["mode"] = "mock",
            ["deltas"] = new JsonObject
            {
                ["mass_g_before"] = 0.0,
                ["mass_g_after"] = 10.0,
                ["mass_g_delta"] = 10.0,
                ["bbox_after"] = new JsonObject
                {
                    ["min"] = new JsonArray(0.0, 0.0, 0.0),
                    ["max"] = new JsonArray(1.0, 1.0, 1.0),
                },
            },
        };

        var formatted = ResultFormatter.FormatResult("extrude", result);

        var text = Text(formatted);
        Assert.Contains("deltas:", text);
        Assert.Contains("mass_g:", text);
        // bbox_after must render as a compact (single-line) JSON object.
        var bboxLine = text.Split('\n').First(l => l.Contains("bbox_after:"));
        Assert.DoesNotContain("\n", bboxLine);
        Assert.Contains("\"min\"", bboxLine);
    }

    [Fact]
    public void FusionErrorResult_RendersIsErrorWithKindAndHints()
    {
        var err = new FusionError("no closed profile", ErrorKind.ProfileNotClosed,
            new[] { "close the sketch", "profile must be closed" });

        var formatted = ResultFormatter.FusionErrorResult("extrude", err);

        Assert.True(formatted.IsError);
        var text = Text(formatted);
        Assert.Contains("**extrude** ERROR (PROFILE_NOT_CLOSED)", text);
        Assert.Contains("no closed profile", text);
        Assert.Contains("- close the sketch", text);
    }

    [Fact]
    public void FusionErrorResult_RendersUnknownTimeoutWithoutClaimingNoEffects()
    {
        var err = new FusionError("bridge timeout", ErrorKind.Timeout, outcomeUnknown: true);

        var text = Text(ResultFormatter.FusionErrorResult("extrude", err));

        Assert.Contains("outcome_unknown: true", text);
        Assert.DoesNotContain("partial_effects: false", text);
    }

    [Fact]
    public void FormatResult_RendersUnknownOutcomeFromAddIn()
    {
        var text = Text(ResultFormatter.FormatResult("create_sketch", new JsonObject
        {
            ["ok"] = false,
            ["error_kind"] = "UNKNOWN",
            ["error_message"] = "handler failed",
            ["outcome_unknown"] = true,
        }));

        Assert.Contains("outcome_unknown: true", text);
        Assert.DoesNotContain("partial_effects:", text);
    }

    [Fact]
    public void FormatResult_RevisionConflictShowsCurrentRevision()
    {
        var text = Text(ResultFormatter.FormatResult("create_box", new JsonObject
        {
            ["ok"] = false,
            ["error_kind"] = "REVISION_CONFLICT",
            ["error_message"] = "stale revision",
            ["revision_after"] = 7,
            ["partial_effects"] = false,
        }));

        Assert.Contains("revision_after: 7", text);
        Assert.Contains("partial_effects: false", text);
    }

    [Fact]
    public void ConnectionErrorResult_SuggestsAddInIsRunning()
    {
        var formatted = ResultFormatter.ConnectionErrorResult("ping", new Exception("connection refused"));

        Assert.True(formatted.IsError);
        var text = Text(formatted);
        Assert.Contains("ping", text);
        Assert.Contains("Make sure Fusion 360 is running", text);
    }

    // ------------------------------------------------------------------
    // P1 (ADR-0009): the optional verification block renders in the text
    // ------------------------------------------------------------------

    [Fact]
    public void FormatResult_Mutation_WithVerification_RendersLevelAndCheckTally()
    {
        var result = new JsonObject
        {
            ["ok"] = true,
            ["body_name"] = "Box1",
            ["deltas"] = new JsonObject { ["body_count_delta"] = 1 },
            ["verification"] = Verification.ToJson(
                VerificationLevel.GeometryChecked,
                new[]
                {
                    VerificationCheck.Count("body_exists", 1, 1),
                    VerificationCheck.Tolerance("volume", 4.0, 4.0, VerificationTolerances.Volume(4.0)),
                },
                Array.Empty<UnverifiedAspect>()),
        };

        var text = Text(ResultFormatter.FormatResult("create_box", result));
        // The agent reads the text block: the level + check tally must land there.
        Assert.Contains("verified: geometry_checked (2/2 checks)", text);
        Assert.DoesNotContain("unverified:", text);
    }

    [Fact]
    public void FormatResult_Mutation_CalledOutUnverifiedAspects()
    {
        // Nothing is silent: every unverified aspect is named in the text.
        var result = new JsonObject
        {
            ["ok"] = true,
            ["verification"] = Verification.ToJson(
                VerificationLevel.CallReturned,
                Array.Empty<VerificationCheck>(),
                new[]
                {
                    new UnverifiedAspect("geometry", "no analytic expected geometry for this command"),
                    new UnverifiedAspect("scene", "the scene snapshot is unavailable — no deltas were recorded"),
                }),
        };

        var text = Text(ResultFormatter.FormatResult("mirror", result));
        Assert.Contains("verified: call_returned (no checks)", text);
        Assert.Contains("unverified:", text);
        Assert.Contains("geometry — no analytic expected geometry for this command", text);
        Assert.Contains("scene — the scene snapshot is unavailable", text);
    }

    [Fact]
    public void FormatResult_Mutation_NoBlock_SaysNothingAboutVerification()
    {
        // Baseline-only results (old add-in) must render exactly as before.
        var result = new JsonObject
        {
            ["ok"] = true,
            ["body_name"] = "Body1",
            ["deltas"] = new JsonObject { ["body_count_delta"] = 1 },
        };

        var text = Text(ResultFormatter.FormatResult("extrude", result));
        Assert.DoesNotContain("verified:", text);
        Assert.DoesNotContain("verification", text);
    }
}
