using Fusion360.Mcp.Core.Errors;

namespace Fusion360.Mcp.Core.Tests;

/// <summary>
/// Classifier table cases: one representative message per rule (port of
/// the reference hints.py rules, first match wins) plus the UNKNOWN
/// fallback and the first-match-wins ordering guarantee.
/// </summary>
public class ErrorClassifierTests
{
    [Theory]
    [InlineData("no active design", ErrorKind.NoActiveDesign, 2)]
    [InlineData("No active design found in the workspace", ErrorKind.NoActiveDesign, 2)]
    [InlineData("No profiles in sketch", ErrorKind.ProfileNotClosed, 3)]
    [InlineData("No profile in sketch", ErrorKind.ProfileNotClosed, 3)]
    [InlineData("Sketch 'A' not found", ErrorKind.SketchNotFound, 2)]
    [InlineData("no sketch named B", ErrorKind.SketchNotFound, 2)]
    [InlineData("Body 'C' not found", ErrorKind.BodyNotFound, 2)]
    [InlineData("no body named D", ErrorKind.BodyNotFound, 2)]
    [InlineData("self intersection detected", ErrorKind.SelfIntersection, 4)]
    [InlineData("Self-Intersection in resulting geometry", ErrorKind.SelfIntersection, 4)]
    [InlineData("Regeneration failed for Extrude1", ErrorKind.RegenFailed, 3)]
    [InlineData("Feature failure during rebuild", ErrorKind.RegenFailed, 3)]
    [InlineData("Boolean subtract failed — empty result", ErrorKind.BooleanNoOp, 4)]
    [InlineData("boolean operation no op", ErrorKind.BooleanNoOp, 4)]
    [InlineData("invalid argument: height", ErrorKind.InvalidInput, 2)]
    [InlineData("bad value for plane", ErrorKind.InvalidInput, 2)]
    [InlineData("Unknown command: frobnicate", ErrorKind.UnknownCommand, 2)]
    [InlineData("profile is open, cannot extrude", ErrorKind.ProfileNotClosed, 2)]
    [InlineData("The bridge timeout was exceeded", ErrorKind.Timeout, 2)]
    [InlineData("requires parametric design type", ErrorKind.DesignTypeMismatch, 2)]
    [InlineData("not supported in direct-edit", ErrorKind.DesignTypeMismatch, 2)]
    [InlineData("switch to parametric to continue", ErrorKind.DesignTypeMismatch, 2)]
    [InlineData("Stale reference: body 'X' was reported by a previous scene read but no longer exists", ErrorKind.StaleReference, 2)]
    [InlineData("Stale reference: sketch 'S' was reported by a previous scene read but no longer exists", ErrorKind.StaleReference, 2)]
    public static void Classify_EachRuleMatches(string message, ErrorKind expectedKind, int expectedHints)
    {
        var (kind, hints) = ErrorClassifier.Classify(message);

        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedHints, hints.Length);
        Assert.All(hints, hint => Assert.False(string.IsNullOrWhiteSpace(hint)));
    }

    [Fact]
    public void Classify_NoMatch_FallsBackToUnknown()
    {
        var (kind, hints) = ErrorClassifier.Classify("something entirely unexpected happened");

        Assert.Equal(ErrorKind.Unknown, kind);
        Assert.Empty(hints);
    }

    [Fact]
    public void Classify_IsCaseInsensitive()
    {
        var (kind, _) = ErrorClassifier.Classify("TIMEOUT OCCURRED");

        Assert.Equal(ErrorKind.Timeout, kind);
    }

    [Fact]
    public void Classify_FirstMatchWins_OrderingIsStable()
    {
        // "No profiles in sketch" matches rule 2 (profile) before any
        // later rule that could also match — table order is the contract.
        var (kind, _) = ErrorClassifier.Classify("no profiles in sketch, extrude aborted");

        Assert.Equal(ErrorKind.ProfileNotClosed, kind);
    }

    [Fact]
    public void Classify_Exception_UsesMessage()
    {
        var ex = new InvalidOperationException("No active design");
        var (kind, _) = ErrorClassifier.Classify(ex);

        Assert.Equal(ErrorKind.NoActiveDesign, kind);
    }

    [Fact]
    public void Classify_Exception_WithEmptyMessage_UsesTypeName()
    {
        var ex = new Exception("");
        var (kind, _) = ErrorClassifier.Classify(ex);

        // "Exception" matches no rule → UNKNOWN
        Assert.Equal(ErrorKind.Unknown, kind);
    }

    [Fact]
    public void AllErrorKinds_RoundtripThroughWireForm()
    {
        foreach (var kind in Enum.GetValues<ErrorKind>())
        {
            var wire = kind.ToWireString();
            Assert.Equal(kind, ErrorKindExtensions.FromWireString(wire));
        }
    }

    [Fact]
    public void WireForm_IsScreamingSnake()
    {
        Assert.Equal("PROFILE_NOT_CLOSED", ErrorKind.ProfileNotClosed.ToWireString());
        Assert.Equal("DESIGN_TYPE_MISMATCH", ErrorKind.DesignTypeMismatch.ToWireString());
        Assert.Equal("UNKNOWN_COMMAND", ErrorKind.UnknownCommand.ToWireString());
        Assert.Equal("UNKNOWN", ErrorKind.Unknown.ToWireString());
        Assert.Equal("REVISION_CONFLICT", ErrorKind.RevisionConflict.ToWireString());
        Assert.Equal("STALE_REFERENCE", ErrorKind.StaleReference.ToWireString());
        Assert.Equal("OPERATION_ID_CONFLICT", ErrorKind.OperationIdConflict.ToWireString());
        Assert.Equal(ErrorKind.RevisionConflict, ErrorKindExtensions.FromWireString("REVISION_CONFLICT"));
        Assert.Equal(ErrorKind.StaleReference, ErrorKindExtensions.FromWireString("STALE_REFERENCE"));
        Assert.Equal(ErrorKind.OperationIdConflict, ErrorKindExtensions.FromWireString("OPERATION_ID_CONFLICT"));
    }
}
