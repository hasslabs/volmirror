using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

/// TrayApp's constructor shows a tray icon, reads HKCU and enumerates real audio
/// devices, so these tests build the object without running it
/// (GetUninitializedObject) and plant the collaborators it would have created:
/// a writer and config dir under a temp folder, a watcher holding a fake
/// endpoint, and a NotifyIcon that is never made visible.
public class TrayAppTests : IDisposable
{
    private readonly string _dir;
    private readonly Settings _settings;
    private readonly EndpointWatcher _watcher;
    private readonly NotifyIcon _icon = new();
    private readonly TrayApp _app;

    public TrayAppTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "volmirror-tray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = new Settings { ConfigDir = _dir, MinDb = -60.0 };
        _watcher = new EndpointWatcher(_settings.DeviceNameContains, null, 50);

        _app = (TrayApp)RuntimeHelpers.GetUninitializedObject(typeof(TrayApp));
        GC.SuppressFinalize(_app);

        Private.Set(_app, "_settings", _settings);
        Private.Set(_app, "_writer", new PreampWriter(_settings.VolumeFilePath));
        Private.Set(_app, "_watcher", _watcher);
        Private.Set(_app, "_icon", _icon);
        Private.Set(_app, "_pauseItem", new ToolStripMenuItem("Pause mirroring"));
        Private.Set(_app, "_includeEnsured", true);
        Attach();
    }

    public void Dispose()
    {
        _icon.Dispose();
        _watcher.Dispose();
        foreach (var file in Directory.GetFiles(_dir))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private void Attach() => Private.Set(_watcher, "_endpoint", new FakeEndpoint());
    private void Detach() => Private.Set(_watcher, "_endpoint", null);

    private void VolumeChanged(double scalar, bool muted = false) =>
        Private.Call(_app, "OnVolumeChanged", new VolumeReading(scalar, muted));

    private void WriteCurrent() => Private.Call(_app, "WriteCurrent");
    private void TogglePause() => Private.Call(_app, "TogglePause");
    private void UpdateTooltip() => Private.Call(_app, "UpdateTooltip");
    private bool TryEnsureInclude() => (bool)Private.Call(_app, "TryEnsureInclude")!;
    private string VolumeFile() => File.ReadAllText(_settings.VolumeFilePath).TrimEnd();

    private void BreakTheWriter() =>
        Private.Set(_app, "_writer", new PreampWriter(Path.Combine(_dir, "missing", "volume.txt")));

    // ---- mirroring ----

    [Fact]
    public void VolumeChange_WritesTheMappedPreamp_AndShowsItInTheTooltip()
    {
        VolumeChanged(0.5);

        Assert.Equal("Preamp: -30.0 dB", VolumeFile());
        Assert.Equal("VolMirror - -30.0 dB", _icon.Text);
    }

    [Fact]
    public void VolumeChange_UsesTheConfiguredMinDb()
    {
        _settings.MinDb = -40.0;

        VolumeChanged(0.5);

        Assert.Equal("Preamp: -20.0 dB", VolumeFile());
    }

    [Fact]
    public void Mute_WritesSilence_AndSaysMuted()
    {
        VolumeChanged(0.8, muted: true);

        Assert.Equal("Preamp: -100.0 dB", VolumeFile());
        Assert.Equal("VolMirror - muted", _icon.Text);
    }

    [Fact]
    public void WriteCurrent_BeforeAnyReading_TouchesNothing()
    {
        // The retry timer calls this before the watcher has produced a reading;
        // writing a default would blast the output to 0 dB on startup.
        WriteCurrent();

        Assert.False(File.Exists(_settings.VolumeFilePath));
    }

    // ---- write failures ----

    [Fact]
    public void WriteFailures_WarnOnlyFromTheFourthInARow()
    {
        // A collision with Equalizer APO's own reload is normal and self-corrects;
        // the tray must not cry wolf on the first few.
        BreakTheWriter();
        VolumeChanged(0.5);
        WriteCurrent();
        WriteCurrent();

        Assert.Equal(3, Private.Get<int>(_app, "_consecutiveWriteFailures"));
        Assert.False(Private.Get<bool>(_app, "_writeErrorReported"));
        Assert.Equal("VolMirror - -30.0 dB", _icon.Text);

        WriteCurrent();

        Assert.True(Private.Get<bool>(_app, "_writeErrorReported"));
        Assert.Equal("VolMirror - write failing", _icon.Text);

        // Further failures keep counting and stay reported; the balloon is not
        // re-raised on every 500 ms retry.
        WriteCurrent();
        Assert.Equal(5, Private.Get<int>(_app, "_consecutiveWriteFailures"));
        Assert.True(Private.Get<bool>(_app, "_writeErrorReported"));
    }

    [Fact]
    public void SuccessfulWrite_ClearsTheFailureState()
    {
        BreakTheWriter();
        VolumeChanged(0.5);
        for (int i = 0; i < 3; i++) WriteCurrent();

        Private.Set(_app, "_writer", new PreampWriter(_settings.VolumeFilePath));
        WriteCurrent();

        Assert.Equal(0, Private.Get<int>(_app, "_consecutiveWriteFailures"));
        Assert.False(Private.Get<bool>(_app, "_writeErrorReported"));
        Assert.Equal("Preamp: -30.0 dB", VolumeFile());
        Assert.Equal("VolMirror - -30.0 dB", _icon.Text);
    }

    // ---- pause / resume ----

    [Fact]
    public void WhilePaused_ChangesAreNotWritten_AndTheFileIsLeftAlone()
    {
        // Resetting to 0 dB on pause would turn pausing at low volume into a jump.
        VolumeChanged(0.5);
        TogglePause();

        VolumeChanged(0.9);

        Assert.Equal("Preamp: -30.0 dB", VolumeFile());
        Assert.Equal("VolMirror - paused", _icon.Text);
        Assert.Equal("Resume mirroring", Private.Get<ToolStripMenuItem>(_app, "_pauseItem").Text);
    }

    [Fact]
    public void Resume_WritesTheLatestReading()
    {
        VolumeChanged(0.5);
        TogglePause();
        VolumeChanged(0.9);

        TogglePause();

        Assert.Equal("Preamp: -6.0 dB", VolumeFile());
        Assert.Equal("Pause mirroring", Private.Get<ToolStripMenuItem>(_app, "_pauseItem").Text);
        Assert.False(Private.Get<ToolStripMenuItem>(_app, "_pauseItem").Checked);
    }

    [Fact]
    public void Resume_RewritesEvenAnUnchangedValue_WhenTheFileDrifted()
    {
        // Without Invalidate the writer's cache would swallow the write and leave
        // whatever someone put in the file while mirroring was paused.
        VolumeChanged(0.5);
        TogglePause();
        File.WriteAllText(_settings.VolumeFilePath, "Preamp: 0.0 dB\r\n");

        TogglePause();

        Assert.Equal("Preamp: -30.0 dB", VolumeFile());
    }

    // ---- tooltip precedence ----

    [Fact]
    public void Tooltip_BeforeAnyReading_SaysWaiting()
    {
        UpdateTooltip();

        Assert.Equal("VolMirror - waiting", _icon.Text);
    }

    [Fact]
    public void Tooltip_WithoutTheDevice_SaysDeviceNotPresent()
    {
        VolumeChanged(0.5);
        Detach();

        UpdateTooltip();

        Assert.Equal("VolMirror - device not present", _icon.Text);
    }

    [Fact]
    public void Tooltip_PausedBeatsDeviceNotPresent()
    {
        Detach();
        TogglePause();

        Assert.Equal("VolMirror - paused", _icon.Text);
    }

    [Fact]
    public void Tooltip_WaitingForApoBeatsPaused()
    {
        TogglePause();
        Private.Set(_app, "_includeEnsured", false);

        UpdateTooltip();

        Assert.Equal("VolMirror - waiting for Equalizer APO", _icon.Text);
    }

    [Fact]
    public void Tooltip_WriteFailingBeatsEverything()
    {
        TogglePause();
        Detach();
        Private.Set(_app, "_includeEnsured", false);
        Private.Set(_app, "_consecutiveWriteFailures", 4);

        UpdateTooltip();

        Assert.Equal("VolMirror - write failing", _icon.Text);
    }

    // ---- Include line ----

    [Fact]
    public void TryEnsureInclude_WithoutTheApoFolder_ReturnsFalse()
    {
        // Equalizer APO not installed yet; the retry timer keeps asking.
        Private.Set(_app, "_includeEnsured", false);
        _settings.ConfigDir = Path.Combine(_dir, "not-installed");

        Assert.False(TryEnsureInclude());
        Assert.False(Directory.Exists(_settings.ConfigDir));
    }

    [Fact]
    public void TryEnsureInclude_WritesTheIncludeOnce_AndRemembersIt()
    {
        Private.Set(_app, "_includeEnsured", false);

        Assert.True(TryEnsureInclude());
        Assert.StartsWith("Include: volume.txt", File.ReadAllText(_settings.ConfigFilePath));

        // Once ensured it must not keep rewriting config.txt, which APO watches.
        File.Delete(_settings.ConfigFilePath);
        Assert.True(TryEnsureInclude());
        Assert.False(File.Exists(_settings.ConfigFilePath));
    }

    [Fact]
    public void TryEnsureInclude_OnAnUnwritableConfig_ReturnsFalseInsteadOfThrowing()
    {
        // This runs from the constructor; an escaping exception would kill the
        // process before the message loop exists.
        Private.Set(_app, "_includeEnsured", false);
        File.WriteAllText(_settings.ConfigFilePath, "Preamp: -3 dB\r\n");
        File.SetAttributes(_settings.ConfigFilePath, FileAttributes.ReadOnly);

        Assert.False(TryEnsureInclude());
        Assert.False(Private.Get<bool>(_app, "_includeEnsured"));
    }

    // ---- quitting ----

    [Fact]
    public void RestoreUnityGain_LeavesTheOutputAtZeroDb()
    {
        // The Include line stays live after exit and the DAC's own volume is
        // inert, so whatever gain is left in volume.txt is what the user hears.
        VolumeChanged(0.5, muted: true);

        Private.Call(_app, "RestoreUnityGain");

        Assert.Equal("Preamp: 0.0 dB", VolumeFile());
    }

    [Fact]
    public void RestoreUnityGain_FromAQuietUnmutedVolume_StillRaisesToZeroDb()
    {
        // Not muted, just low: the exit gain must not depend on where the slider was.
        VolumeChanged(0.1, muted: false);

        Private.Call(_app, "RestoreUnityGain");

        Assert.Equal("Preamp: 0.0 dB", VolumeFile());
    }

    [Fact]
    public void RestoreUnityGain_OnAnUnwritableFolder_DoesNotThrow()
    {
        // Runs on ProcessExit during logout; nothing useful can be done there.
        BreakTheWriter();

        Private.Call(_app, "RestoreUnityGain");
    }
}
