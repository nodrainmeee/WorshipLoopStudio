using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WorshipLoopStudio.Audio.IO;

public sealed class AudioFileException : Exception
{
    public AudioFileException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Decodes audio files into engine format. Runs on background threads only.</summary>
public static class SampleLoader
{
    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromMinutes(15);

    public static CachedSample Load(string path, TimeSpan? maxDuration = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new AudioFileException($"The audio file '{path}' was not found.");
        }

        var limit = maxDuration ?? DefaultMaxDuration;
        try
        {
            using var reader = new AudioFileReader(path);
            if (reader.TotalTime > limit + TimeSpan.FromSeconds(1))
            {
                throw new AudioFileException($"'{System.IO.Path.GetFileName(path)}' is longer than {limit.TotalMinutes:0} minutes, which is not supported here.");
            }

            var provider = ToEngineFormat(reader);
            var maxSamples = (long)(limit.TotalSeconds * EngineFormat.SampleRate * EngineFormat.Channels) + EngineFormat.SampleRate * 4L;
            var data = ReadAll(provider, maxSamples, cancellationToken);
            return new CachedSample(path, data);
        }
        catch (AudioFileException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AudioFileException($"'{System.IO.Path.GetFileName(path)}' could not be read as audio ({ex.Message}). Use WAV, MP3, AIFF, FLAC or M4A files.", ex);
        }
    }

    /// <summary>Converts any sample provider to 44.1 kHz stereo float.</summary>
    public static ISampleProvider ToEngineFormat(ISampleProvider source)
    {
        var provider = source;
        if (provider.WaveFormat.Channels == 1)
        {
            provider = new MonoToStereoSampleProvider(provider);
        }
        else if (provider.WaveFormat.Channels > 2)
        {
            provider = new DownmixToStereoSampleProvider(provider);
        }

        if (provider.WaveFormat.SampleRate != EngineFormat.SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, EngineFormat.SampleRate);
        }

        return provider;
    }

    private static float[] ReadAll(ISampleProvider provider, long maxSamples, CancellationToken cancellationToken)
    {
        var buffer = new float[EngineFormat.SampleRate * EngineFormat.Channels];
        var result = new float[EngineFormat.SampleRate * EngineFormat.Channels * 2];
        long count = 0;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count + read > maxSamples)
            {
                read = (int)(maxSamples - count);
            }

            if (count + read > result.Length)
            {
                var newSize = Math.Max(result.Length * 2L, count + read);
                Array.Resize(ref result, (int)Math.Min(newSize, int.MaxValue - 64));
            }

            Array.Copy(buffer, 0, result, count, read);
            count += read;
            if (count >= maxSamples)
            {
                break;
            }
        }

        count -= count % EngineFormat.Channels;
        Array.Resize(ref result, (int)count);
        return result;
    }
}

/// <summary>Mixes surround or multichannel input down to stereo.</summary>
public sealed class DownmixToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _inChannels;
    private float[] _sourceBuffer = Array.Empty<float>();

    public DownmixToStereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _inChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        var needed = frames * _inChannels;
        if (_sourceBuffer.Length < needed)
        {
            _sourceBuffer = new float[needed];
        }

        var read = _source.Read(_sourceBuffer, 0, needed);
        var framesRead = read / _inChannels;
        var extraScale = _inChannels > 2 ? 0.5f / (_inChannels - 2) : 0f;
        for (var f = 0; f < framesRead; f++)
        {
            var baseIndex = f * _inChannels;
            var l = _sourceBuffer[baseIndex];
            var r = _sourceBuffer[baseIndex + 1];
            float extra = 0;
            for (var c = 2; c < _inChannels; c++)
            {
                extra += _sourceBuffer[baseIndex + c];
            }

            buffer[offset + f * 2] = (l + extra * extraScale) * 0.8f;
            buffer[offset + f * 2 + 1] = (r + extra * extraScale) * 0.8f;
        }

        return framesRead * 2;
    }
}
