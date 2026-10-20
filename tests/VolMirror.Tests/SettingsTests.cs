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

    [Theory]
    [InlineData(0.0)]
    [InlineData(12.0)]
    public void NonNegativeMinDb_FallsBackToTheDefault(double value)
    {
        // A MinDb of 0 would make the whole slider 0 dB (no attenuation at all),
        // and a positive one would boost; both are reset rather than clamped.
        File.WriteAllText(_path, "{ \"MinDb\": " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " }");

        Assert.Equal(PreampMapper.DefaultMinDb, Settings.Load(_path).MinDb);
    }

    [Theory]
    [InlineData(-3.0, -6.0)]      // too narrow: clamped to the -6 dB ceiling
    [InlineData(-250.0, -100.0)]  // wider than silence: clamped to SilenceDb
    [InlineData(-40.0, -40.0)]    // in range: kept as written
    public void MinDb_IsClampedIntoTheUsableRange(double written, double expected)
    {
        File.WriteAllText(_path, "{ \"MinDb\": " + written.ToString(System.Globalization.CultureInfo.InvariantCulture) + " }");

        Assert.Equal(expected, Settings.Load(_path).MinDb);
    }

    [Fact]
    public void WhitespaceStrings_FallBackToDefaults()
    {
        File.WriteAllText(_path, "{ \"ConfigDir\": \"  \", \"DeviceNameContains\": \"\t\" }");

        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultConfigDir, settings.ConfigDir);
        Assert.Equal(Settings.DefaultDeviceName, settings.DeviceNameContains);
    }

    [Fact]
    public void JsonNullLiteral_FallsBackToDefaults()
    {
        // Deserialize returns null for a bare "null" document.
        File.WriteAllText(_path, "null");

        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultDeviceName, settings.DeviceNameContains);
        Assert.Equal(Settings.DefaultPollIntervalMs, settings.PollIntervalMs);
    }

    [Fact]
    public void UnreadablePath_FallsBackToDefaults()
    {
        // A directory where the file should be: File.Exists is false, so Load tries
        // to save defaults there, which throws UnauthorizedAccessException on Windows.
        Directory.CreateDirectory(_path);

        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultDeviceName, settings.DeviceNameContains);
        Assert.Equal(Settings.DefaultConfigDir, settings.ConfigDir);
    }

    [Fact]
    public void Save_CreatesMissingParentDirectories()
    {
        string nested = Path.Combine(_dir, "a", "b", "settings.json");

        new Settings { MinDb = -45.0, PinnedDeviceId = "{x}" }.Save(nested);
        var read = Settings.Load(nested);

        Assert.Equal(-45.0, read.MinDb);
        Assert.Equal("{x}", read.PinnedDeviceId);
    }

    [Fact]
    public void Load_RepairsInMemory_ButDoesNotRewriteTheFile()
    {
        // Normalize fixes the loaded object only; the user's file is left as written.
        const string json = "{ \"PollIntervalMs\": 0 }";
        File.WriteAllText(_path, json);

        Assert.Equal(Settings.MinPollIntervalMs, Settings.Load(_path).PollIntervalMs);
        Assert.Equal(json, File.ReadAllText(_path));
    }

    [Fact]
    public void UnknownProperties_AreIgnored()
    {
        File.WriteAllText(_path, "{ \"Obsolete\": 1, \"DeviceNameContains\": \"Scarlett\" }");

        Assert.Equal("Scarlett", Settings.Load(_path).DeviceNameContains);
    }
}
