using NAudio.Wave;

namespace WorshipLoopStudio.Audio.IO;

public static class EngineFormat
{
    public const int SampleRate = 44100;
    public const int Channels = 2;

    public static WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
}

/// <summary>Audio fully decoded into memory as interleaved stereo float at the engine rate. Immutable once built.</summary>
public sealed class CachedSample
{
    public static readonly CachedSample Empty = new(string.Empty, Array.Empty<float>());

    public CachedSample(string path, float[] data)
    {
        Path = path;
        Data = data;
    }

    public string Path { get; }
    public float[] Data { get; }
    public int Frames => Data.Length / EngineFormat.Channels;
    public double DurationSeconds => Frames / (double)EngineFormat.SampleRate;
    public bool IsEmpty => Data.Length == 0;
}
