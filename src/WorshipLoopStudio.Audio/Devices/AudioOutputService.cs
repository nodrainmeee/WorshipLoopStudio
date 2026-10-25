using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WorshipLoopStudio.Audio.IO;

namespace WorshipLoopStudio.Audio.Devices;

public enum OutputDriver
{
    WasapiShared,
    WasapiExclusive,
    WaveOut,
}

public sealed record AudioDeviceInfo(string Id, string Name)
{
    public const string DefaultId = "";

    public override string ToString() => Name;
}

public sealed class AudioDeviceEventArgs : EventArgs
{
    public AudioDeviceEventArgs(string message, Exception? exception)
    {
        Message = message;
        Exception = exception;
    }

    public string Message { get; }
    public Exception? Exception { get; }
}

/// <summary>
/// Owns the Windows output device. The output stays open for the whole session and plays the master mixer,
/// which contains the loop engine and the stem player; transport start/stop never re-opens the device.
/// </summary>
public sealed class AudioOutputService : IDisposable
{
    private readonly object _gate = new();
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly DeviceNotifier? _notifier;
    private IWavePlayer? _player;

    public AudioOutputService()
    {
        Mixer = new MixingSampleProvider(EngineFormat.WaveFormat) { ReadFully = true };
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _notifier = new DeviceNotifier(_ => DevicesChanged?.Invoke(this, EventArgs.Empty));
            _enumerator.RegisterEndpointNotificationCallback(_notifier);
        }
        catch (Exception)
        {
            _enumerator = null;
            _notifier = null;
        }
    }

    /// <summary>Raised on a background thread when playback fails (device unplugged, driver error...).</summary>
    public event EventHandler<AudioDeviceEventArgs>? PlaybackFailed;

    /// <summary>Raised on a COM thread when Windows reports an audio device change.</summary>
    public event EventHandler? DevicesChanged;

    public MixingSampleProvider Mixer { get; }

    public bool IsRunning { get; private set; }
    public OutputDriver Driver { get; private set; }
    public string? DeviceId { get; private set; }
    public string DeviceName { get; private set; } = "(none)";
    public int LatencyMs { get; private set; }

    public IReadOnlyList<AudioDeviceInfo> GetDevices(OutputDriver driver)
    {
        var list = new List<AudioDeviceInfo> { new(AudioDeviceInfo.DefaultId, "Windows default device") };
        try
        {
            if (driver == OutputDriver.WaveOut)
            {
                for (var i = 0; i < WaveOut.DeviceCount; i++)
                {
                    list.Add(new AudioDeviceInfo(i.ToString(System.Globalization.CultureInfo.InvariantCulture), WaveOut.GetCapabilities(i).ProductName));
                }
            }
            else if (_enumerator is not null)
            {
                foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                }
            }
        }
        catch (Exception)
        {
            // Return what we have; the UI will show the default entry.
        }

        return list;
    }

    /// <summary>Opens the output. Throws a descriptive exception when the device cannot be used.</summary>
    public void Start(OutputDriver driver, string? deviceId, int latencyMs)
    {
        lock (_gate)
        {
            StopInternal();
            latencyMs = Math.Clamp(latencyMs, 5, 500);
            IWavePlayer player;
            string name;
            try
            {
                if (driver == OutputDriver.WaveOut)
                {
                    var number = -1;
                    if (!string.IsNullOrEmpty(deviceId) && int.TryParse(deviceId, out var parsed) && parsed < WaveOut.DeviceCount)
                    {
                        number = parsed;
                    }

                    player = new WaveOutEvent { DeviceNumber = number, DesiredLatency = Math.Max(latencyMs, 50), NumberOfBuffers = 3 };
                    name = number < 0 ? "Windows default (MME)" : WaveOut.GetCapabilities(number).ProductName + " (MME)";
                }
                else
                {
                    if (_enumerator is null)
                    {
                        throw new InvalidOperationException("Windows audio services (WASAPI) are not available on this system.");
                    }

                    var device = string.IsNullOrEmpty(deviceId)
                        ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                        : _enumerator.GetDevice(deviceId);
                    var mode = driver == OutputDriver.WasapiExclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared;
                    player = new WasapiOut(device, mode, true, latencyMs);
                    name = device.FriendlyName + (mode == AudioClientShareMode.Exclusive ? " (WASAPI exclusive)" : " (WASAPI)");
                }

                player.PlaybackStopped += OnPlaybackStopped;
                player.Init(new SampleToWaveProvider(Mixer));
                player.Play();
            }
            catch (Exception ex)
            {
                IsRunning = false;
                throw new InvalidOperationException(DescribeStartError(driver, ex), ex);
            }

            _player = player;
            IsRunning = true;
            Driver = driver;
            DeviceId = deviceId;
            DeviceName = name;
            LatencyMs = latencyMs;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopInternal();
        }
    }

    public bool IsDeviceStillPresent()
    {
        if (string.IsNullOrEmpty(DeviceId) || Driver == OutputDriver.WaveOut || _enumerator is null)
        {
            return true;
        }

        try
        {
            using var device = _enumerator.GetDevice(DeviceId);
            return device.State == DeviceState.Active;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        if (_enumerator is not null && _notifier is not null)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notifier);
            }
            catch (Exception)
            {
                // Ignore during shutdown.
            }
        }

        _enumerator?.Dispose();
    }

    private void StopInternal()
    {
        var player = _player;
        _player = null;
        IsRunning = false;
        if (player is null)
        {
            return;
        }

        player.PlaybackStopped -= OnPlaybackStopped;
        try
        {
            player.Stop();
        }
        catch (Exception)
        {
            // The device may already be gone.
        }

        player.Dispose();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        IsRunning = false;
        var message = e.Exception is null
            ? "Audio output stopped unexpectedly."
            : $"Audio output failed: {e.Exception.Message}. The device may have been unplugged or taken by another application.";
        PlaybackFailed?.Invoke(this, new AudioDeviceEventArgs(message, e.Exception));
    }

    private static string DescribeStartError(OutputDriver driver, Exception ex)
    {
        var baseMessage = driver switch
        {
            OutputDriver.WasapiExclusive => "The device refused exclusive mode (another app may be using it, or the format is not supported). Try WASAPI shared mode.",
            OutputDriver.WaveOut => "The MME output device could not be opened.",
            _ => "The audio output device could not be opened.",
        };
        return $"{baseMessage} ({ex.Message})";
    }
}
