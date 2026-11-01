namespace Fusion360.Mcp.Core.Units;

/// <summary>
/// Unit handling for user parameters.
///
/// Fusion reads a bare real in its internal units — centimetres for a
/// length, radians for an angle — so a value the caller meant as
/// millimetres has to be handed over as a unit-bearing expression string
/// instead. Kept free of adsk imports so the rules can be unit-tested
/// without a running Fusion instance. Port of reference
/// addon/server/parameter_units.py.
/// </summary>
public static class ParameterUnits
{
    /// <summary>Trim a unit to its canonical form; blank means unitless.</summary>
    public static string NormalizeUnit(string? unit) => (unit ?? string.Empty).Trim();

    /// <summary>True when the value must go through a string rather than a real.</summary>
    public static bool NeedsExpression(string? unit) => NormalizeUnit(unit).Length > 0;

    /// <summary>
    /// The expression text that stores *value* as *unit*.
    /// A unitless parameter has nothing to interpolate, so it keeps the
    /// bare number — Fusion then reads it as internal units, which is what
    /// unitless means.
    /// </summary>
    public static string ExpressionFor(double value, string? unit)
    {
        var normalized = NormalizeUnit(unit);
        // Invariant formatting: Python's str(float) is locale-free, and a
        // comma decimal separator would be a syntax error in a Fusion
        // expression.
        var number = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return normalized.Length > 0
            ? $"{number} {normalized}"
            : number;
    }

    /// <summary>
    /// The ValueInput expression or bare value: when *unit* is non-empty
    /// the value must go through <c>createByString(expression)</c>,
    /// otherwise <c>createByReal(value)</c>. Returns the expression text;
    /// an empty result means use the real form.
    /// </summary>
    public static string? ValueInputFor(double value, string? unit)
        => NeedsExpression(unit) ? ExpressionFor(value, unit) : null;
}
