using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "volmirror-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingFile_WritesDefaults()
    {
        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultDeviceName, settings.DeviceNameContains);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void ExistingFile_IsRoundTripped()
    {
        var written = new Settings { DeviceNameContains = "Focusrite", PollIntervalMs = 100 };
        written.Save(_path);

        var read = Settings.Load(_path);

        Assert.Equal("Focusrite", read.DeviceNameContains);
        Assert.Equal(100, read.PollIntervalMs);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        // A hand-edited settings file must not brick the app on startup.
        File.WriteAllText(_path, "{ not json");

        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultDeviceName, settings.DeviceNameContains);
    }

    [Fact]
    public void DerivedPaths_SitInsideTheConfigDir()
    {
        var settings = new Settings { ConfigDir = @"C:\somewhere\config" };

        Assert.Equal(@"C:\somewhere\config\volume.txt", settings.VolumeFilePath);
        Assert.Equal(@"C:\somewhere\config\config.txt", settings.ConfigFilePath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public void OutOfRangePollInterval_IsClamped(int value)
    {
        // Timer.Interval throws below 1. Well-formed JSON never hits Load's catch,
        // so an unvalidated 0 would kill startup before the message loop exists -
        // no tray icon, no dialog - and would never be rewritten, so every relaunch
        // would fail identically.
        File.WriteAllText(_path, "{ \"PollIntervalMs\": " + value + " }");

        var settings = Settings.Load(_path);

        Assert.InRange(settings.PollIntervalMs, Settings.MinPollIntervalMs, Settings.MaxPollIntervalMs);
    }

    [Fact]
    public void NullStrings_FallBackToDefaults()
    {
        // System.Text.Json ignores non-nullable annotations by default, so a JSON
        // null overwrites the property initializer and is then dereferenced.
        File.WriteAllText(_path, "{ \"ConfigDir\": null, \"DeviceNameContains\": null }");

        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultConfigDir, settings.ConfigDir);
        Assert.Equal(Settings.DefaultDeviceName, settings.DeviceNameContains);
        Assert.NotNull(settings.VolumeFilePath);
    }

    [Fact]
    public void BlankPinnedId_BecomesNull()
    {
        File.WriteAllText(_path, "{ \"PinnedDeviceId\": \"   \" }");

        Assert.Null(Settings.Load(_path).PinnedDeviceId);
    }
}
