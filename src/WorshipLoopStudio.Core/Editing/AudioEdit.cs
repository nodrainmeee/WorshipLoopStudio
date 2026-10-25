namespace WorshipLoopStudio.Core.Editing;

public readonly record struct LoopRegion(double StartSeconds, double EndSeconds)
{
    public const double MinimumLengthSeconds = 0.01;

    public double LengthSeconds => EndSeconds - StartSeconds;

    public static LoopRegion Clamp(double start, double end, double durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            return new LoopRegion(0, 0);
        }

        if (double.IsNaN(start)) start = 0;
        if (double.IsNaN(end)) end = durationSeconds;
        start = Math.Clamp(start, 0, durationSeconds);
        end = Math.Clamp(end, 0, durationSeconds);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (durationSeconds < MinimumLengthSeconds)
        {
            return new LoopRegion(0, durationSeconds);
        }

        if (end - start < MinimumLengthSeconds)
        {
            if (start + MinimumLengthSeconds <= durationSeconds)
            {
                end = start + MinimumLengthSeconds;
            }
            else
            {
                start = Math.Max(0, end - MinimumLengthSeconds);
            }
        }

        return new LoopRegion(start, end);
    }

    public (int StartFrame, int EndFrame) ToFrames(int sampleRate, int totalFrames)
    {
        var s = (int)Math.Clamp(Math.Round(StartSeconds * sampleRate), 0, totalFrames);
        var e = (int)Math.Clamp(Math.Round(EndSeconds * sampleRate), s, totalFrames);
        return (s, e);
    }
}

public static class AudioEdit
{
    /// <summary>Cuts an interleaved buffer to a region and applies short fades so the loop point does not click.</summary>
    public static float[] Trim(float[] interleaved, int channels, int sampleRate, LoopRegion region, double fadeMs = 5)
    {
        ArgumentNullException.ThrowIfNull(interleaved);
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        var totalFrames = interleaved.Length / channels;
        var (start, end) = region.ToFrames(sampleRate, totalFrames);
        var frames = end - start;
        var result = new float[frames * channels];
        Array.Copy(interleaved, start * channels, result, 0, frames * channels);

        var fadeFrames = Math.Min(frames / 2, (int)(fadeMs / 1000.0 * sampleRate));
        for (var i = 0; i < fadeFrames; i++)
        {
            var gain = i / (float)fadeFrames;
            for (var c = 0; c < channels; c++)
            {
                result[i * channels + c] *= gain;
                result[(frames - 1 - i) * channels + c] *= gain;
            }
        }

        return result;
    }
}

public static class WaveformPeaks
{
    /// <summary>Absolute peak (0..1) per bucket, used to draw the waveform.</summary>
    public static float[] Compute(float[] interleaved, int channels, int points)
    {
        if (points <= 0 || interleaved.Length == 0 || channels < 1)
        {
            return Array.Empty<float>();
        }

        var frames = interleaved.Length / channels;
        points = Math.Min(points, Math.Max(1, frames));
        var peaks = new float[points];
        for (var p = 0; p < points; p++)
        {
            var start = (int)((long)p * frames / points);
            var end = (int)((long)(p + 1) * frames / points);
            var max = 0f;
            for (var f = start; f < end; f++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var v = Math.Abs(interleaved[f * channels + c]);
                    if (v > max) max = v;
                }
            }

            peaks[p] = Math.Min(1f, max);
        }

        return peaks;
    }
}
