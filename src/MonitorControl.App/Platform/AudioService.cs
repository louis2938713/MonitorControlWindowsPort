using System.Runtime.InteropServices;
using MonitorControl.Core.Platform;
using NAudio.CoreAudioApi;

namespace MonitorControl.App.Platform;

/// <summary>
/// Default audio output through Core Audio (replacement for SimplyCoreAudio). On Windows every endpoint has a
/// software volume, so the equivalent of "the device cannot set its own volume" is "the output is a display"
/// (HDMI/DisplayPort audio, form factor DigitalAudioDisplayDevice). Must be created on the UI thread.
/// </summary>
internal sealed class AudioService : IAudioService, IDisposable
{
    private const uint DigitalAudioDisplayDevice = 9;
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly MMDeviceNotificationClient? _notifications;

    public AudioService()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            // Events are marshalled to the UI thread (captured synchronization context).
            _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: true);
            _notifications.DefaultDeviceChanged += (_, _) =>
            {
                var previous = (DefaultOutputName, DefaultOutputIsDisplay);
                Refresh();
                if (previous != (DefaultOutputName, DefaultOutputIsDisplay))
                {
                    Core.Log.Info($"Default output device changed to \"{DefaultOutputName}\" (display audio: {DefaultOutputIsDisplay})");
                    DefaultOutputChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            Refresh();
        }
        catch (COMException ex)
        {
            Core.Log.Warning("Core Audio unavailable", ex);
        }
    }

    public event EventHandler? DefaultOutputChanged;

    public string? DefaultOutputName { get; private set; }

    public bool DefaultOutputIsDisplay { get; private set; }

    public void Dispose()
    {
        _notifications?.Dispose();
        _enumerator?.Dispose();
    }

    private void Refresh()
    {
        DefaultOutputName = null;
        DefaultOutputIsDisplay = false;
        try
        {
            if (_enumerator is null || !_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device) || device is null)
            {
                return;
            }

            using (device)
            {
                DefaultOutputName = device.FriendlyName;
                DefaultOutputIsDisplay = device.Properties.TryGetValue<uint>(PropertyKeys.PKEY_AudioEndpoint_FormFactor, out var formFactor) && formFactor == DigitalAudioDisplayDevice;
            }
        }
        catch (COMException ex)
        {
            Core.Log.Warning("Unable to read the default audio device", ex);
        }
    }
}
