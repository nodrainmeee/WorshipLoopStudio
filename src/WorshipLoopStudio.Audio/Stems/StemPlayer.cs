using NAudio.Wave;
using WorshipLoopStudio.Audio.IO;

namespace WorshipLoopStudio.Audio.Stems;

/// <summary>One separated stem held in memory as 16-bit interleaved stereo (half the memory of float).</summary>
public sealed class StemTrack
{
    public StemTrack(string name, string path, short[] data)
    {
        Name = name;
        Path = path;
        Data = data;
    }

    public string Name { get; }
    public string Path { get; }
    public short[] Data { get; }
    public int Frames => Data.Length / 2;

    public volatile float Volume = 1f;
    public volatile bool Mute;
    public volatile bool Solo;
    internal float CurrentGain = -1f;

    public static StemTrack Load(string name, string path, CancellationToken cancellationToken = default)
    {
        var sample = SampleLoader.Load(path, TimeSpan.FromMinutes(20), cancellationToken);
        var data = new short[sample.Data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var v = sample.Data[i];
            v = v > 1f ? 1f : v < -1f ? -1f : v;
            data[i] = (short)(v * 32767f);
        }

        return new StemTrack(name, path, data);
    }
}

/// <summary>Plays all stems from one shared position so they always stay in sync.</summary>
public sealed class StemPlayer : ISampleProvider
{
    private StemTrack[] _stems = Array.Empty<StemTrack>();
    private long _position;
    private long _seekRequest = -1;
    private int _playing;

    public WaveFormat WaveFormat { get; } = EngineFormat.WaveFormat;

    public bool IsPlaying => Volatile.Read(ref _playing) == 1;

    public double PositionSeconds => Volatile.Read(ref _position) / (double)EngineFormat.SampleRate;

    public double DurationSeconds
    {
        get
        {
            var stems = Volatile.Read(ref _stems);
            return stems.Length == 0 ? 0 : stems.Max(s => s.Frames) / (double)EngineFormat.SampleRate;
        }
    }

    public IReadOnlyList<StemTrack> Stems => Volatile.Read(ref _stems);

    public void Load(IEnumerable<StemTrack> stems)
    {
        Volatile.Write(ref _playing, 0);
        Volatile.Write(ref _stems, stems.ToArray());
        Interlocked.Exchange(ref _seekRequest, 0);
    }

    public void Clear() => Load(Array.Empty<StemTrack>());

    public void Play()
    {
        if (Volatile.Read(ref _stems).Length > 0)
        {
            if (PositionSeconds >= DurationSeconds - 0.01)
            {
                Seek(0);
            }

            Volatile.Write(ref _playing, 1);
        }
    }

    public void Pause() => Volatile.Write(ref _playing, 0);

    public void Stop()
    {
        Volatile.Write(ref _playing, 0);
        Seek(0);
    }

    public void Seek(double seconds)
    {
        var frame = (long)(Math.Max(0, seconds) * EngineFormat.SampleRate);
        Interlocked.Exchange(ref _seekRequest, frame);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        Array.Clear(buffer, offset, frames * 2);
        var stems = Volatile.Read(ref _stems);

        var seek = Interlocked.Exchange(ref _seekRequest, -1);
        if (seek >= 0)
        {
            _position = seek;
            foreach (var s in stems)
            {
                s.CurrentGain = -1f;
            }
        }

        if (Volatile.Read(ref _playing) == 0 || stems.Length == 0)
        {
            Volatile.Write(ref _position, _position);
            return frames * 2;
        }

        var anySolo = false;
        foreach (var s in stems)
        {
            if (s.Solo)
            {
                anySolo = true;
            }
        }

        const float scale = 1f / 32768f;
        long maxFrames = 0;
        foreach (var stem in stems)
        {
            maxFrames = Math.Max(maxFrames, stem.Frames);
            var target = stem.Mute || (anySolo && !stem.Solo) ? 0f : Math.Clamp(stem.Volume, 0f, 2f);
            var start = stem.CurrentGain < 0 ? target : stem.CurrentGain;
            var available = (int)Math.Max(0, Math.Min(frames, stem.Frames - _position));
            var data = stem.Data;
            var si = (int)(_position * 2);
            var bi = offset;
            for (var f = 0; f < available; f++)
            {
                var g = start + (target - start) * ((f + 1) / (float)frames);
                buffer[bi] += data[si] * scale * g;
                buffer[bi + 1] += data[si + 1] * scale * g;
                bi += 2;
                si += 2;
            }

            stem.CurrentGain = target;
        }

        _position += frames;
        if (_position >= maxFrames)
        {
            _position = maxFrames;
            Volatile.Write(ref _playing, 0);
        }

        for (var i = offset; i < offset + frames * 2; i++)
        {
            var v = buffer[i];
            buffer[i] = v > 1f ? 1f : v < -1f ? -1f : v;
        }

        Volatile.Write(ref _position, _position);
        return frames * 2;
    }
}
