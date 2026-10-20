using System.Runtime.InteropServices;
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

/// Drives EndpointWatcher.Poll against a fake endpoint. Start() is never called:
/// it would enumerate the real audio devices through COM.
public class EndpointWatcherTests : IDisposable
{
    private const int DeviceInvalidated = unchecked((int)0x88890004); // AUDCLNT_E_DEVICE_INVALIDATED

    private readonly EndpointWatcher _watcher = new("USB Audio CODEC", pinnedId: null, pollIntervalMs: 50);
    private readonly FakeEndpoint _endpoint = new();
    private readonly List<VolumeReading> _changes = [];
    private readonly List<bool> _availability = [];

    public EndpointWatcherTests()
    {
        _watcher.Changed += _changes.Add;
        _watcher.AvailabilityChanged += _availability.Add;
        Private.Set(_watcher, "_endpoint", _endpoint);
    }

    public void Dispose() => _watcher.Dispose();

    private void Poll() => Private.Call(_watcher, "Poll");

    [Fact]
    public void FirstPoll_RaisesChanged_WithTheReading()
    {
        _endpoint.Scalar = 0.25f;
        _endpoint.Muted = true;

        Poll();

        Assert.Equal([new VolumeReading(0.25f, true)], _changes);
        Assert.True(_watcher.IsAttached);
    }

    [Fact]
    public void UnchangedReading_IsNotRaisedAgain()
    {
        // Polling runs 20 times a second; re-raising an identical reading would
        // redo the write path and tooltip on every tick.
        Poll();
        Poll();
        Poll();

        Assert.Single(_changes);
        Assert.Equal(3, _endpoint.Reads);
    }

    [Fact]
    public void VolumeOrMuteChange_IsRaised()
    {
        Poll();
        _endpoint.Scalar = 0.75f;
        Poll();
        _endpoint.Muted = true;
        Poll();

        Assert.Equal(
            [new VolumeReading(0.5f, false), new VolumeReading(0.75f, false), new VolumeReading(0.75f, true)],
            _changes);
    }

    [Theory]
    [InlineData(DeviceInvalidated, 0)]   // failing volume read
    [InlineData(0, DeviceInvalidated)]   // failing mute read
    public void FailingHResult_Detaches_AndReportsUnavailable(int scalarHr, int muteHr)
    {
        _endpoint.ScalarHr = scalarHr;
        _endpoint.MuteHr = muteHr;

        Poll();

        Assert.False(_watcher.IsAttached);
        Assert.Equal([false], _availability);
        Assert.Empty(_changes);
    }

    public static TheoryData<Exception> TransientExceptions =>
    [
        new COMException("device gone"),
        new InvalidCastException(),
        new IOException("registry key deleted mid-read"),
        new UnauthorizedAccessException(),
        new ArgumentException(),
        new NullReferenceException(),
    ];

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void TransientException_Detaches_InsteadOfEscapingIntoTheTimer(Exception ex)
    {
        // Escaping into Timer.Tick would stack an exception dialog per tick.
        _endpoint.Throw = ex;

        Poll();

        Assert.False(_watcher.IsAttached);
        Assert.Equal([false], _availability);
    }

    [Fact]
    public void UnexpectedException_IsNotSwallowed()
    {
        // The filter is a list, not a catch-all: a genuine bug should surface.
        _endpoint.Throw = new InvalidOperationException("bug");

        Assert.Throws<InvalidOperationException>(Poll);
        Assert.True(_watcher.IsAttached);
    }

    [Fact]
    public void AfterReattach_TheSameReadingIsRaisedAgain()
    {
        // Detach clears the last reading, so the tray re-mirrors the volume once
        // the device returns, even if the slider never moved while it was gone.
        Poll();
        _endpoint.ScalarHr = DeviceInvalidated;
        Poll();

        _endpoint.ScalarHr = 0;
        Private.Set(_watcher, "_endpoint", _endpoint);  // what a successful TryAttach does
        Poll();

        Assert.Equal([new VolumeReading(0.5f, false), new VolumeReading(0.5f, false)], _changes);
    }

    [Fact]
    public void WhileDetached_ReattachIsThrottled()
    {
        // Each skipped tick only counts down; it must not touch the endpoint or
        // raise anything. The tick that reaches zero would enumerate real
        // devices, so it is not exercised here.
        Private.Set(_watcher, "_endpoint", null);
        Private.Set(_watcher, "_ticksUntilRetry", 3);

        Poll();
        Poll();
        Poll();

        Assert.Equal(0, Private.Get<int>(_watcher, "_ticksUntilRetry"));
        Assert.False(_watcher.IsAttached);
        Assert.Empty(_changes);
        Assert.Empty(_availability);
        Assert.Equal(0, _endpoint.Reads);
    }

    [Fact]
    public void Dispose_DropsTheEndpoint()
    {
        // Release() on the fake goes through Marshal.ReleaseComObject's "not an
        // RCW" ArgumentException, which must be swallowed here as in Detach.
        _watcher.Dispose();

        Assert.False(_watcher.IsAttached);
    }
}
