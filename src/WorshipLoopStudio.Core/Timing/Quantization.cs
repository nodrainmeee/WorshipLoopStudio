namespace WorshipLoopStudio.Core.Timing;

/// <summary>Grid resolution of the step sequencer, expressed as subdivisions of one beat.</summary>
public enum Quantization
{
    Beat,
    Eighth,
    EighthTriplet,
    Sixteenth,
    SixteenthTriplet,
    ThirtySecond,
}

public static class QuantizationExtensions
{
    public static int StepsPerBeat(this Quantization quantization) => quantization switch
    {
        Quantization.Beat => 1,
        Quantization.Eighth => 2,
        Quantization.EighthTriplet => 3,
        Quantization.Sixteenth => 4,
        Quantization.SixteenthTriplet => 6,
        Quantization.ThirtySecond => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(quantization), quantization, "Unknown quantization."),
    };

    public static string DisplayName(this Quantization quantization) => quantization switch
    {
        Quantization.Beat => "1 step per beat",
        Quantization.Eighth => "1/2 beat (8ths in 4/4)",
        Quantization.EighthTriplet => "1/3 beat (8th triplets)",
        Quantization.Sixteenth => "1/4 beat (16ths in 4/4)",
        Quantization.SixteenthTriplet => "1/6 beat (16th triplets)",
        Quantization.ThirtySecond => "1/8 beat (32nds in 4/4)",
        _ => quantization.ToString(),
    };
}
