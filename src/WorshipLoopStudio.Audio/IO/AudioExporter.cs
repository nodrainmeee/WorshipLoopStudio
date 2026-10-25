using NAudio.MediaFoundation;
using NAudio.Wave;
using WorshipLoopStudio.Audio.Engine;
using WorshipLoopStudio.Core;
using WorshipLoopStudio.Core.Editing;

namespace WorshipLoopStudio.Audio.IO;

public sealed class Mp3UnavailableException : Exception
{
    public Mp3UnavailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Offline rendering and file export. Runs on background threads.</summary>
public static class AudioExporter
{
    /// <summary>Renders the arrangement offline (faster than real time) to a 16-bit WAV file.</summary>
    public static TimeSpan RenderToWav(EngineArrangement arrangement, int loops, double tailSeconds, string wavPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        loops = Math.Clamp(loops, 1, 256);
        var exportArrangement = arrangement;
        var engine = new LoopEngine();
        engine.SetArrangement(exportArrangement);
        engine.Start();

        var framesPerStep = Core.Timing.TempoMath.FramesPerStep(exportArrangement.Bpm, EngineFormat.SampleRate, exportArrangement.StepsPerBeat);
        var loopFrames = Core.Timing.TempoMath.LoopLengthFrames(exportArrangement.TotalSteps, framesPerStep);
        var bodyFrames = loopFrames * loops;
        var tailFrames = (long)(Math.Max(0, tailSeconds) * EngineFormat.SampleRate);
        var totalFrames = bodyFrames + tailFrames;

        var dir = Path.GetDirectoryName(Path.GetFullPath(wavPath))!;
        Directory.CreateDirectory(dir);
        DiskSpaceGuard.EnsureAvailable(dir, totalFrames * 4 + 10L * 1024 * 1024);

        var temp = wavPath + ".part";
        var buffer = new float[4096 * 2];
        using (var writer = new WaveFileWriter(temp, new WaveFormat(EngineFormat.SampleRate, 16, 2)))
        {
            long written = 0;
            var stopped = false;
            while (written < totalFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!stopped && written >= bodyFrames)
                {
                    engine.Stop(StopMode.Release);
                    stopped = true;
                }

                var limit = stopped ? totalFrames : bodyFrames;
                var frames = (int)Math.Min(buffer.Length / 2, limit - written);
                engine.Read(buffer, 0, frames * 2);
                writer.WriteSamples(buffer, 0, frames * 2);
                written += frames;
                progress?.Report(written / (double)totalFrames);
            }
        }

        if (File.Exists(wavPath))
        {
            File.Delete(wavPath);
        }

        File.Move(temp, wavPath);
        return TimeSpan.FromSeconds(totalFrames / (double)EngineFormat.SampleRate);
    }

    /// <summary>Encodes any readable audio file to MP3 with the Windows Media Foundation encoder.</summary>
    public static void EncodeMp3(string sourcePath, string mp3Path, int bitRate = 192000)
    {
        try
        {
            MediaFoundationApi.Startup();
        }
        catch (Exception ex)
        {
            throw new Mp3UnavailableException("Windows Media Foundation is not available (on Windows N editions install the Media Feature Pack). Export as WAV instead.", ex);
        }

        var mediaType = MediaFoundationEncoder.SelectMediaType(AudioSubtypes.MFAudioFormat_MP3, new WaveFormat(EngineFormat.SampleRate, 2), bitRate);
        if (mediaType is null)
        {
            throw new Mp3UnavailableException("No MP3 encoder is installed on this Windows system. Export as WAV instead.");
        }

        var temp = mp3Path + ".part.mp3";
        try
        {
            using (var reader = new AudioFileReader(sourcePath))
            {
                var pcm = new FloatToPcm16Provider(SampleLoader.ToEngineFormat(reader));
                MediaFoundationEncoder.EncodeToMp3(pcm, temp, bitRate);
            }

            if (File.Exists(mp3Path))
            {
                File.Delete(mp3Path);
            }

            File.Move(temp, mp3Path);
        }
        catch (Exception ex) when (ex is not Mp3UnavailableException)
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw new Mp3UnavailableException($"MP3 encoding failed: {ex.Message}", ex);
        }
    }

    /// <summary>Writes a trimmed copy of a clip as a 24-bit WAV at the engine rate.</summary>
    public static void SaveTrimmed(CachedSample sample, LoopRegion region, string path)
    {
        var trimmed = AudioEdit.Trim(sample.Data, EngineFormat.Channels, EngineFormat.SampleRate, region);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        DiskSpaceGuard.EnsureAvailable(dir, trimmed.LongLength * 3 + 1024 * 1024);
        var temp = path + ".part";
        using (var writer = new WaveFileWriter(temp, new WaveFormat(EngineFormat.SampleRate, 24, 2)))
        {
            writer.WriteSamples(trimmed, 0, trimmed.Length);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temp, path);
    }

    /// <summary>Converts any supported file to a 44.1 kHz 16-bit stereo WAV (used before stem separation).</summary>
    public static void ConvertToWav(string sourcePath, string wavPath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(wavPath))!;
        Directory.CreateDirectory(dir);
        using var reader = new AudioFileReader(sourcePath);
        var estimate = (long)(reader.TotalTime.TotalSeconds * EngineFormat.SampleRate * 4) + 10L * 1024 * 1024;
        DiskSpaceGuard.EnsureAvailable(dir, estimate);
        var provider = SampleLoader.ToEngineFormat(reader);
        var buffer = new float[EngineFormat.SampleRate * 2];
        using var writer = new WaveFileWriter(wavPath, new WaveFormat(EngineFormat.SampleRate, 16, 2));
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteSamples(buffer, 0, read);
        }
    }

    /// <summary>Converts float samples to 16-bit PCM bytes (needed by the Media Foundation MP3 encoder).</summary>
    private sealed class FloatToPcm16Provider : IWaveProvider
    {
        private readonly ISampleProvider _source;
        private float[] _buffer = Array.Empty<float>();

        public FloatToPcm16Provider(ISampleProvider source)
        {
            _source = source;
            WaveFormat = new WaveFormat(source.WaveFormat.SampleRate, 16, source.WaveFormat.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var samplesRequested = count / 2;
            if (_buffer.Length < samplesRequested)
            {
                _buffer = new float[samplesRequested];
            }

            var read = _source.Read(_buffer, 0, samplesRequested);
            for (var i = 0; i < read; i++)
            {
                var clamped = Math.Clamp(_buffer[i], -1f, 1f);
                var value = (short)(clamped * 32767f);
                buffer[offset + i * 2] = (byte)(value & 0xFF);
                buffer[offset + i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return read * 2;
        }
    }
}
