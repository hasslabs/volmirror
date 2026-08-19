using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class ApoConfigTests : IDisposable
{
    private readonly string _dir;
    private readonly string _configPath;

    public ApoConfigTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "volmirror-apo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "config.txt");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingConfig_GetsTheIncludeLine()
    {
        Assert.True(ApoConfig.EnsureInclude(_configPath));
        Assert.Contains("Include: volume.txt", File.ReadAllText(_configPath));
    }

    [Fact]
    public void ExistingInclude_IsNotDuplicated()
    {
        File.WriteAllText(_configPath, "Include: volume.txt\n");

        Assert.False(ApoConfig.EnsureInclude(_configPath));
        Assert.Single(File.ReadAllLines(_configPath), l => l.Contains("Include: volume.txt"));
    }

    [Fact]
    public void IncludeWithSurroundingWhitespace_IsRecognised()
    {
        File.WriteAllText(_configPath, "   Include: volume.txt   \n");

        Assert.False(ApoConfig.EnsureInclude(_configPath));
    }

    [Fact]
    public void UserFiltersArePreserved()
    {
        // The whole reason we own a separate file rather than config.txt.
        File.WriteAllText(_configPath, "Filter 1: ON PK Fc 1000 Hz Gain -3 dB Q 1\nFilter 2: ON LS Fc 100 Hz Gain 2 dB\n");

        ApoConfig.EnsureInclude(_configPath);

        string text = File.ReadAllText(_configPath);
        Assert.Contains("Filter 1: ON PK Fc 1000 Hz Gain -3 dB Q 1", text);
        Assert.Contains("Filter 2: ON LS Fc 100 Hz Gain 2 dB", text);
        Assert.Contains("Include: volume.txt", text);
    }
}
