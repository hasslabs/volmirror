using System.Runtime.InteropServices;
using VolMirror.Interop;

namespace VolMirror;

/// Scalar is the slider position, 0.0-1.0. Deliberately not the device's own dB:
/// Windows derives that from the device's volume range, and the UCA202 declares
/// -128 dB, which stretches the curve so the top of the slider barely moves.
public readonly record struct VolumeReading(double Scalar, bool Muted);

/// Polls one audio endpoint, resolved by device ID, and raises an event when its
/// volume or mute state changes. Re-attaches by itself if the device goes away.
public sealed class EndpointWatcher : IDisposable
{
    private const uint ClsCtxInprocServer = 1;

    private readonly string _nameContains;
    private readonly string? _pinnedId;
    private readonly System.Windows.Forms.Timer _timer;

    private IAudioEndpointVolume? _endpoint;
    private VolumeReading? _last;
    private int _ticksUntilRetry;

    /// Raised on every observed change, and once on first successful attach.
    public event Action<VolumeReading>? Changed;

    /// Raised when the device becomes available or unavailable.
    public event Action<bool>? AvailabilityChanged;

    public bool IsAttached => _endpoint is not null;

    public EndpointWatcher(string nameContains, string? pinnedId, int pollIntervalMs)
    {
        _nameContains = nameContains;
        _pinnedId = pinnedId;
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
            // Resolved every attempt, not cached: the ID changes underneath us
            // when Windows re-enumerates the device.
            string? deviceId = DeviceResolver.PickBestMatch(
                DeviceResolver.ListActiveRenderEndpoints(), _nameContains, _pinnedId);
            if (deviceId is null)
                return;

            Type? comType = Type.GetTypeFromCLSID(CoreAudioGuids.MMDeviceEnumerator);
            if (comType is null || Activator.CreateInstance(comType) is not IMMDeviceEnumerator enumerator)
                return;

            if (enumerator.GetDevice(deviceId, out IMMDevice device) != 0)
                return;

            Guid iid = CoreAudioGuids.IAudioEndpointVolume;
            if (device.Activate(ref iid, ClsCtxInprocServer, IntPtr.Zero, out object raw) != 0)
                return;

            _endpoint = (IAudioEndpointVolume)raw;
            _last = null;                       // force a Changed on the next poll
            AvailabilityChanged?.Invoke(true);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _endpoint = null;
        }
    }

    /// Everything the attach/read path can realistically throw. Deliberately wider
    /// than COMException: unplugging the device makes Windows delete the registry
    /// key mid-read, which raises IOException, and a failed QueryInterface raises
    /// InvalidCastException. Either one escaping into Timer.Tick would stack
    /// exception dialogs 20 times a second, since the app installs no
    /// ThreadException handler.
    private static bool IsTransient(Exception ex) =>
        ex is COMException or InvalidCastException or IOException
           or UnauthorizedAccessException or ArgumentException or NullReferenceException;

    private void Detach()
    {
        if (_endpoint is not null)
            Release(_endpoint);

        _endpoint = null;
        _last = null;
        AvailabilityChanged?.Invoke(false);
    }

    internal static void Release(object comObject)
    {
        try { Marshal.ReleaseComObject(comObject); }
        catch (ArgumentException) { /* not an RCW, or already released */ }
    }

    private void Poll()
    {
        if (_endpoint is null)
        {
            // Re-enumerating every 50 ms while the device is off would run ~576k
            // full device scans over a night. Once a second is plenty.
            if (_ticksUntilRetry-- > 0)
                return;

            _ticksUntilRetry = Math.Max(1, 1000 / Math.Max(1, _timer.Interval));
            try { TryAttach(); }
            catch (Exception ex) when (IsTransient(ex)) { _endpoint = null; }
            return;
        }

        try
        {
            if (_endpoint.GetMasterVolumeLevelScalar(out float scalar) != 0) { Detach(); return; }
            if (_endpoint.GetMute(out bool muted) != 0) { Detach(); return; }

            var reading = new VolumeReading(scalar, muted);
            if (_last is { } previous && previous == reading)
                return;

            _last = reading;
            Changed?.Invoke(reading);
        }
        catch (Exception ex) when (IsTransient(ex))
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
            Release(_endpoint);
        _endpoint = null;
    }
}
