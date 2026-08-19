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

        Assert.Equal(Settings.DefaultDeviceId, settings.DeviceId);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void ExistingFile_IsRoundTripped()
    {
        var written = new Settings { DeviceId = "{0.0.0.00000000}.{deadbeef}", PollIntervalMs = 100 };
        written.Save(_path);

        var read = Settings.Load(_path);

        Assert.Equal("{0.0.0.00000000}.{deadbeef}", read.DeviceId);
        Assert.Equal(100, read.PollIntervalMs);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        // A hand-edited settings file must not brick the app on startup.
        File.WriteAllText(_path, "{ not json");

        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultDeviceId, settings.DeviceId);
    }

    [Fact]
    public void DerivedPaths_SitInsideTheConfigDir()
    {
        var settings = new Settings { ConfigDir = @"C:\somewhere\config" };

        Assert.Equal(@"C:\somewhere\config\volume.txt", settings.VolumeFilePath);
        Assert.Equal(@"C:\somewhere\config\config.txt", settings.ConfigFilePath);
    }
}
