using System.Text;
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
        File.WriteAllText(_configPath, "Include: volume.txt" + Environment.NewLine);

        Assert.False(ApoConfig.EnsureInclude(_configPath));
        Assert.Single(File.ReadAllLines(_configPath), l => l.Contains("Include: volume.txt"));
    }

    [Fact]
    public void IncludeWithSurroundingWhitespace_IsRecognised()
    {
        File.WriteAllText(_configPath, "   Include: volume.txt   " + Environment.NewLine);

        Assert.False(ApoConfig.EnsureInclude(_configPath));
    }

    [Fact]
    public void UserFiltersArePreserved()
    {
        // The whole reason we own a separate file rather than config.txt.
        File.WriteAllText(_configPath,
            "Filter 1: ON PK Fc 1000 Hz Gain -3 dB Q 1" + Environment.NewLine +
            "Filter 2: ON LS Fc 100 Hz Gain 2 dB" + Environment.NewLine);

        ApoConfig.EnsureInclude(_configPath);

        string text = File.ReadAllText(_configPath);
        Assert.Contains("Filter 1: ON PK Fc 1000 Hz Gain -3 dB Q 1", text);
        Assert.Contains("Filter 2: ON LS Fc 100 Hz Gain 2 dB", text);
        Assert.Contains("Include: volume.txt", text);
    }

    [Fact]
    public void IncludeIsPrepended_NotAppended()
    {
        // An appended Include would land inside a trailing Device:/If: block, and
        // Equalizer APO would skip it entirely while VolMirror reported success.
        File.WriteAllText(_configPath,
            "Device: Benq Monitor" + Environment.NewLine +
            "Filter 1: ON PK Fc 1000 Hz Gain -3 dB Q 1" + Environment.NewLine);

        ApoConfig.EnsureInclude(_configPath);

        Assert.Equal("Include: volume.txt", File.ReadAllLines(_configPath)[0]);
    }

    [Fact]
    public void NonUtf8Bytes_SurviveUntouched()
    {
        // APO accepts ANSI configs. Decoding this as UTF-8 and writing it back would
        // turn the 0xF6 into U+FFFD permanently, killing the user's Device: match.
        byte[] ansi = Encoding.Latin1.GetBytes(
            "Device: Högtalare (USB Audio CODEC)" + Environment.NewLine + "Preamp: -3 dB" + Environment.NewLine);
        File.WriteAllBytes(_configPath, ansi);

        ApoConfig.EnsureInclude(_configPath);

        byte[] after = File.ReadAllBytes(_configPath);
        Assert.Contains((byte)0xF6, after);
        Assert.Equal(ansi, after[^ansi.Length..]);
    }

    [Fact]
    public void Utf8Bom_StaysFirst()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        File.WriteAllBytes(_configPath, [.. bom, .. Encoding.UTF8.GetBytes("Preamp: 0 dB" + Environment.NewLine)]);

        ApoConfig.EnsureInclude(_configPath);

        byte[] after = File.ReadAllBytes(_configPath);
        Assert.Equal(bom, after[..3]);
        Assert.StartsWith("Include: volume.txt", Encoding.UTF8.GetString(after[3..]));
    }

    [Theory]
    [InlineData("Include:volume.txt")]
    [InlineData("Include:  volume.txt")]
    [InlineData("Include :volume.txt")]
    [InlineData("include: VOLUME.TXT")]
    public void SpacingVariants_CountAsAlreadyPresent(string variant)
    {
        // APO splits on the first colon and trims, so these are all the same
        // directive. Appending a second would double the gain: preamps are
        // additive, so two -20 dB includes give -40 dB.
        File.WriteAllText(_configPath, variant + Environment.NewLine);

        Assert.False(ApoConfig.EnsureInclude(_configPath));
    }

    [Fact]
    public void NoTempFileIsLeftBehind()
    {
        ApoConfig.EnsureInclude(_configPath);

        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.Contains("tmp"));
    }
}
