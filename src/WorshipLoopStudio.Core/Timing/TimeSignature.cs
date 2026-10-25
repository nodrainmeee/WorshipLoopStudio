using System.Globalization;

namespace WorshipLoopStudio.Core.Timing;

/// <summary>
/// A musical time signature. The tempo (BPM) always counts the lower note value:
/// quarter notes in 4/4 and 3/4, eighth notes in 6/8 and 12/8.
/// </summary>
public readonly record struct TimeSignature(int Numerator, int Denominator)
{
    public static readonly TimeSignature FourFour = new(4, 4);

    public static IReadOnlyList<TimeSignature> Common { get; } = new[]
    {
        new TimeSignature(4, 4),
        new TimeSignature(3, 4),
        new TimeSignature(2, 4),
        new TimeSignature(5, 4),
        new TimeSignature(6, 8),
        new TimeSignature(7, 8),
        new TimeSignature(12, 8),
    };

    public bool IsValid => Numerator is >= 1 and <= 16 && Denominator is 2 or 4 or 8 or 16;

    public override string ToString() => $"{Numerator}/{Denominator}";

    public static TimeSignature Parse(string text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"'{text}' is not a valid time signature. Use a format such as 4/4 or 6/8.");
        }

        return value;
    }

    public static bool TryParse(string? text, out TimeSignature value)
    {
        value = FourFour;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('/');
        if (parts.Length != 2
            || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
        {
            return false;
        }

        var candidate = new TimeSignature(n, d);
        if (!candidate.IsValid)
        {
            return false;
        }

        value = candidate;
        return true;
    }
}
