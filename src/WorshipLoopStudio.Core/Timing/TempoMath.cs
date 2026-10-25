namespace WorshipLoopStudio.Core.Timing;

/// <summary>
/// Pure tempo and timing calculations. Step positions are always computed from the step index
/// (never accumulated) so that long performances do not drift.
/// </summary>
public static class TempoMath
{
    public const double MinBpm = 30;
    public const double MaxBpm = 300;
    public const double DefaultBpm = 80;

    public static double ClampBpm(double bpm)
    {
        if (double.IsNaN(bpm) || double.IsInfinity(bpm))
        {
            return DefaultBpm;
        }

        return Math.Clamp(Math.Round(bpm, 2), MinBpm, MaxBpm);
    }

    public static double SecondsPerBeat(double bpm) => 60.0 / ValidateBpm(bpm);

    public static double FramesPerBeat(double bpm, int sampleRate) => sampleRate * 60.0 / ValidateBpm(bpm);

    public static double FramesPerStep(double bpm, int sampleRate, int stepsPerBeat)
    {
        if (stepsPerBeat < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stepsPerBeat));
        }

        return FramesPerBeat(bpm, sampleRate) / stepsPerBeat;
    }

    public static int StepsPerBar(TimeSignature signature, int stepsPerBeat) => signature.Numerator * stepsPerBeat;

    public static int TotalSteps(TimeSignature signature, int stepsPerBeat, int bars) =>
        StepsPerBar(signature, stepsPerBeat) * Math.Max(1, bars);

    /// <summary>Exact start frame of a step, rounded once from the ideal position.</summary>
    public static long StepStartFrame(int stepIndex, double framesPerStep) =>
        (long)Math.Round(stepIndex * framesPerStep, MidpointRounding.AwayFromZero);

    public static long LoopLengthFrames(int totalSteps, double framesPerStep) => StepStartFrame(totalSteps, framesPerStep);

    public static double BarDurationSeconds(double bpm, TimeSignature signature) => signature.Numerator * SecondsPerBeat(bpm);

    public static double LoopDurationSeconds(double bpm, TimeSignature signature, int bars) =>
        BarDurationSeconds(bpm, signature) * Math.Max(1, bars);

    /// <summary>Returns the first boundary (a multiple of <paramref name="boundaryFrames"/>) at or after the position.</summary>
    public static long NextBoundary(long position, long boundaryFrames)
    {
        if (boundaryFrames <= 0)
        {
            return position;
        }

        var remainder = position % boundaryFrames;
        return remainder == 0 ? position : position + boundaryFrames - remainder;
    }

    /// <summary>Tempo from a list of tap times in seconds. Needs at least two taps.</summary>
    public static double? TapTempo(IReadOnlyList<double> tapTimesSeconds)
    {
        if (tapTimesSeconds.Count < 2)
        {
            return null;
        }

        var intervals = new List<double>();
        for (var i = 1; i < tapTimesSeconds.Count; i++)
        {
            var interval = tapTimesSeconds[i] - tapTimesSeconds[i - 1];
            if (interval > 0.15 && interval < 2.5)
            {
                intervals.Add(interval);
            }
        }

        if (intervals.Count == 0)
        {
            return null;
        }

        return ClampBpm(60.0 / intervals.Average());
    }

    private static double ValidateBpm(double bpm)
    {
        if (double.IsNaN(bpm) || bpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "BPM must be a positive number.");
        }

        return bpm;
    }
}
