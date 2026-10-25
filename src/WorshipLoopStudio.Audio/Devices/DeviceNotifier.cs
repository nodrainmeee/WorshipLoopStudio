using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WorshipLoopStudio.Audio.Devices;

/// <summary>Receives Windows audio endpoint notifications (device unplugged, default device changed...).</summary>
internal sealed class DeviceNotifier : IMMNotificationClient
{
    private readonly Action<string> _onChange;

    public DeviceNotifier(Action<string> onChange)
    {
        _onChange = onChange;
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Raise($"state:{deviceId}:{newState}");

    public void OnDeviceAdded(string pwstrDeviceId) => Raise($"added:{pwstrDeviceId}");

    public void OnDeviceRemoved(string deviceId) => Raise($"removed:{deviceId}");

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (role == Role.Multimedia)
        {
            Raise($"default:{flow}:{defaultDeviceId}");
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Property changes are too frequent to be useful here.
    }

    private void Raise(string what)
    {
        try
        {
            _onChange(what);
        }
        catch (Exception)
        {
            // Never throw back into COM.
        }
    }
}
