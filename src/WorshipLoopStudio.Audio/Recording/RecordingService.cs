using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WorshipLoopStudio.Audio.Devices;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Core;

namespace WorshipLoopStudio.Audio.Recording;

public sealed class RecordingCompletedEventArgs : EventArgs
{
    public RecordingCompletedEventArgs(string path, TimeSpan duration, Exception? error)
    {
        Path = path;
        Duration = duration;
        Error = error;
    }

    public string Path { get; }
    public TimeSpan Duration { get; }
    public Exception? Error { get; }
}

public sealed class MicrophoneAccessException : Exception
{
    public MicrophoneAccessException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Microphone / audio-interface capture through WASAPI. Capture callbacks only convert, meter and queue audio;
/// a background writer task does all disk work.
/// </summary>
public sealed class RecordingService : IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private WaveFormat? _captureFormat;
    private BufferedWaveProvider? _monitorBuffer;
    private ISampleProvider? _monitorProvider;
    private BlockingCollection<float[]>? _writeQueue;
    private Task? _writerTask;
    private string? _recordingPath;
    private long _recordedFrames;
    private long _maxFrames;
    private float _peak;
    private volatile bool _recording;
    private volatile bool _monitorEnabled;
    private volatile float _inputGain = 1f;
    private float[] _convertBuffer = Array.Empty<float>();

    public event EventHandler<RecordingCompletedEventArgs>? RecordingCompleted;

    public event EventHandler<string>? InputFailed;

    public bool IsInputOpen => _capture is not null;
    public bool IsRecording => _recording;
    public string InputName { get; private set; } = "(none)";
    public WaveFormat? CaptureFormat => _captureFormat;

    public float InputGain
    {
        get => _inputGain;
        set => _inputGain = Math.Clamp(value, 0f, 16f);
    }

    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set
        {
            _monitorEnabled = value;
            if (!value)
            {
                _monitorBuffer?.ClearBuffer();
            }
        }
    }

    /// <summary>The live input converted to engine format, for routing into the output mixer when monitoring.</summary>
    public ISampleProvider? MonitorProvider => _monitorProvider;

    public TimeSpan Elapsed => _captureFormat is null
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Interlocked.Read(ref _recordedFrames) / (double)_captureFormat.SampleRate);

    public float ReadAndResetPeak() => Interlocked.Exchange(ref _peak, 0f);

    public static IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var list = new List<AudioDeviceInfo> { new(AudioDeviceInfo.DefaultId, "Windows default input") };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            }
        }
        catch (Exception)
        {
            // Leave only the default entry.
        }

        return list;
    }

    /// <summary>Opens the input device for metering and monitoring (recording starts separately).</summary>
    public void OpenInput(string? deviceId)
    {
        lock (_gate)
        {
            CloseInputInternal();
            MMDevice device;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                device = string.IsNullOrEmpty(deviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                    : enumerator.GetDevice(deviceId);
            }
            catch (COMException ex)
            {
                throw new MicrophoneAccessException("No recording device was found. Connect a microphone or audio interface and check Windows Sound settings.", ex);
            }

            try
            {
                var capture = new WasapiCapture(device, true, 20);
                _captureFormat = capture.WaveFormat;
                if (!IsSupported(_captureFormat))
                {
                    capture.Dispose();
                    throw new MicrophoneAccessException($"The input format {_captureFormat} is not supported. Change the device format in Windows Sound settings (16-bit or 24-bit, 44.1/48 kHz).");
                }

                var monitorFormat = WaveFormat.CreateIeeeFloatWaveFormat(_captureFormat.SampleRate, _captureFormat.Channels);
                _monitorBuffer = new BufferedWaveProvider(monitorFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(300),
                    ReadFully = true,
                };
                _monitorProvider = SampleLoader.ToEngineFormat(new WaveToSampleProvider(_monitorBuffer));

                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnCaptureStopped;
                capture.StartRecording();
                _capture = capture;
                InputName = device.FriendlyName;
            }
            catch (MicrophoneAccessException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw MicBlocked(ex);
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80070005)
            {
                throw MicBlocked(ex);
            }
            catch (Exception ex)
            {
                throw new MicrophoneAccessException($"The recording device '{device.FriendlyName}' could not be opened: {ex.Message}", ex);
            }
        }
    }

    public void CloseInput()
    {
        lock (_gate)
        {
            CloseInputInternal();
        }
    }

    /// <summary>Starts writing the input to a WAV file. maxDuration null = until StopRecording().</summary>
    public void StartRecording(string path, TimeSpan? maxDuration)
    {
        lock (_gate)
        {
            if (_capture is null || _captureFormat is null)
            {
                throw new InvalidOperationException("Open an input device before recording.");
            }

            if (_recording)
            {
                return;
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var bytesPerSecond = _captureFormat.SampleRate * _captureFormat.Channels * 4L;
            var expected = maxDuration is { } d ? (long)(d.TotalSeconds * bytesPerSecond) : bytesPerSecond * 600;
            DiskSpaceGuard.EnsureAvailable(dir, expected + 50L * 1024 * 1024);

            var fileFormat = WaveFormat.CreateIeeeFloatWaveFormat(_captureFormat.SampleRate, _captureFormat.Channels);
            var writer = new WaveFileWriter(path, fileFormat);
            var queue = new BlockingCollection<float[]>(new ConcurrentQueue<float[]>());
            _writeQueue = queue;
            _recordingPath = path;
            Interlocked.Exchange(ref _recordedFrames, 0);
            _maxFrames = maxDuration is { } m ? (long)(m.TotalSeconds * _captureFormat.SampleRate) : long.MaxValue;
            _writerTask = Task.Factory.StartNew(() => WriterLoop(writer, queue, path), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _recording = true;
        }
    }

    public void StopRecording()
    {
        lock (_gate)
        {
            if (!_recording)
            {
                return;
            }

            _recording = false;
            _writeQueue?.CompleteAdding();
        }
    }

    public void Dispose()
    {
        StopRecording();
        CloseInput();
    }

    private static MicrophoneAccessException MicBlocked(Exception ex) => new(
        "Windows blocked access to the microphone. Open Settings > Privacy & security > Microphone, turn on 'Microphone access' and 'Let desktop apps access your microphone', then try again.",
        ex);

    private static bool IsSupported(WaveFormat format) =>
        (format.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible && format.BitsPerSample == 32)
        || (format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible && format.BitsPerSample is 16 or 24);

    private void CloseInputInternal()
    {
        if (_recording)
        {
            _recording = false;
            _writeQueue?.CompleteAdding();
        }

        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnCaptureStopped;
            try
            {
                capture.StopRecording();
            }
            catch (Exception)
            {
                // Device may already be gone.
            }

            capture.Dispose();
        }

        _monitorBuffer = null;
        _monitorProvider = null;
        InputName = "(none)";
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = _captureFormat;
        if (format is null || e.BytesRecorded == 0)
        {
            return;
        }

        var samples = Convert(e.Buffer, e.BytesRecorded, format);
        var gain = _inputGain;
        var peak = 0f;
        for (var i = 0; i < samples; i++)
        {
            var v = _convertBuffer[i] * gain;
            _convertBuffer[i] = v;
            var a = Math.Abs(v);
            if (a > peak) peak = a;
        }

        if (peak > Volatile.Read(ref _peak))
        {
            Volatile.Write(ref _peak, peak);
        }

        if (_monitorEnabled && _monitorBuffer is { } monitor)
        {
            var bytes = MemoryMarshal.AsBytes(_convertBuffer.AsSpan(0, samples)).ToArray();
            monitor.AddSamples(bytes, 0, bytes.Length);
        }

        if (_recording && _writeQueue is { IsAddingCompleted: false } queue)
        {
            var channels = format.Channels;
            var frames = samples / channels;
            var remaining = _maxFrames - Interlocked.Read(ref _recordedFrames);
            var take = (int)Math.Min(frames, Math.Max(0, remaining));
            if (take > 0)
            {
                var chunk = new float[take * channels];
                Array.Copy(_convertBuffer, chunk, chunk.Length);
                try
                {
                    queue.Add(chunk);
                }
                catch (InvalidOperationException)
                {
                    // Recording was stopped between the check and the add.
                }

                Interlocked.Add(ref _recordedFrames, take);
            }

            if (take < frames || Interlocked.Read(ref _recordedFrames) >= _maxFrames)
            {
                _recording = false;
                queue.CompleteAdding();
            }
        }
    }

    private int Convert(byte[] buffer, int bytes, WaveFormat format)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        var count = bytes / bytesPerSample;
        if (_convertBuffer.Length < count)
        {
            _convertBuffer = new float[count];
        }

        var isFloat = format.BitsPerSample == 32 && format.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible;
        if (isFloat)
        {
            MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, count * 4)).CopyTo(_convertBuffer);
        }
        else if (format.BitsPerSample == 16)
        {
            for (var i = 0; i < count; i++)
            {
                _convertBuffer[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var o = i * 3;
                var v = buffer[o] | (buffer[o + 1] << 8) | ((sbyte)buffer[o + 2] << 16);
                _convertBuffer[i] = v / 8388608f;
            }
        }

        return count;
    }

    private void WriterLoop(WaveFileWriter writer, BlockingCollection<float[]> queue, string path)
    {
        Exception? error = null;
        try
        {
            foreach (var chunk in queue.GetConsumingEnumerable())
            {
                writer.WriteSamples(chunk, 0, chunk.Length);
            }
        }
        catch (Exception ex)
        {
            error = ex is IOException io && (io.HResult & 0xFFFF) == 112
                ? new InsufficientDiskSpaceException(0, 0, path)
                : ex;
            _recording = false;
            if (!queue.IsAddingCompleted)
            {
                queue.CompleteAdding();
            }
        }
        finally
        {
            try
            {
                writer.Dispose();
            }
            catch (Exception ex)
            {
                error ??= ex;
            }
        }

        var format = writer.WaveFormat;
        var duration = TimeSpan.FromSeconds(Interlocked.Read(ref _recordedFrames) / (double)Math.Max(1, format.SampleRate));
        RecordingCompleted?.Invoke(this, new RecordingCompletedEventArgs(path, duration, error));
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            if (_recording)
            {
                _recording = false;
                _writeQueue?.CompleteAdding();
            }

            InputFailed?.Invoke(this, $"The recording device stopped: {e.Exception.Message}. It may have been unplugged.");
        }
    }
}
