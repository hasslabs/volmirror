using System.Runtime.InteropServices;
using VolMirror.Interop;

namespace VolMirror;

public readonly record struct VolumeReading(double LevelDb, bool Muted);

/// Polls one audio endpoint, resolved by device ID, and raises an event when its
/// volume or mute state changes. Re-attaches by itself if the device goes away.
public sealed class EndpointWatcher : IDisposable
{
    private const uint ClsCtxInprocServer = 1;

    private readonly string _deviceId;
    private readonly System.Windows.Forms.Timer _timer;

    private IAudioEndpointVolume? _endpoint;
    private VolumeReading? _last;

    /// Raised on every observed change, and once on first successful attach.
    public event Action<VolumeReading>? Changed;

    /// Raised when the device becomes available or unavailable.
    public event Action<bool>? AvailabilityChanged;

    public bool IsAttached => _endpoint is not null;

    public EndpointWatcher(string deviceId, int pollIntervalMs)
    {
        _deviceId = deviceId;
        _timer = new System.Windows.Forms.Timer { Interval = pollIntervalMs };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        TryAttach();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void TryAttach()
    {
        try
        {
            Type? comType = Type.GetTypeFromCLSID(CoreAudioGuids.MMDeviceEnumerator);
            if (comType is null || Activator.CreateInstance(comType) is not IMMDeviceEnumerator enumerator)
                return;

            if (enumerator.GetDevice(_deviceId, out IMMDevice device) != 0)
                return;

            Guid iid = CoreAudioGuids.IAudioEndpointVolume;
            if (device.Activate(ref iid, ClsCtxInprocServer, IntPtr.Zero, out object raw) != 0)
                return;

            _endpoint = (IAudioEndpointVolume)raw;
            _last = null;                       // force a Changed on the next poll
            AvailabilityChanged?.Invoke(true);
        }
        catch (COMException)
        {
            _endpoint = null;
        }
    }

    private void Detach()
    {
        _endpoint = null;
        _last = null;
        AvailabilityChanged?.Invoke(false);
    }

    private void Poll()
    {
        if (_endpoint is null)
        {
            TryAttach();
            return;
        }

        try
        {
            if (_endpoint.GetMasterVolumeLevel(out float db) != 0) { Detach(); return; }
            if (_endpoint.GetMute(out bool muted) != 0) { Detach(); return; }

            var reading = new VolumeReading(db, muted);
            if (_last is { } previous && previous == reading)
                return;

            _last = reading;
            Changed?.Invoke(reading);
        }
        catch (COMException)
        {
            // Device unplugged or driver reloaded mid-poll.
            Detach();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        if (_endpoint is not null)
            Marshal.ReleaseComObject(_endpoint);
    }
}
