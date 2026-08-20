using System.Globalization;
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class PreampMapperTests
{
    [Fact]
    public void FullVolume_IsZeroGain()
    {
        Assert.Equal("Preamp: 0.0 dB", PreampMapper.ToPreampLine(0.0, muted: false));
    }

    [Fact]
    public void MidVolume_PassesWindowsTaperThrough()
    {
        // Measured on the real device: scalar 0.49 reported -10.8 dB.
        Assert.Equal("Preamp: -10.8 dB", PreampMapper.ToPreampLine(-10.8, muted: false));
    }

    [Fact]
    public void Muted_IsSilence()
    {
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(-4.5, muted: true));
    }

    [Fact]
    public void Muted_WinsOverFullVolume()
    {
        // Windows reports mute independently of level; mute must not be inferred
        // from level == 0, and level must not override mute.
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(0.0, muted: true));
    }

    [Fact]
    public void BottomOfSlider_IsClampedToFloor()
    {
        // Windows reports -128 dB at scalar 0. Never emit that.
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(-128.0, muted: false));
    }

    [Fact]
    public void PositiveGain_IsClampedToZero()
    {
        // Defensive: never boost, which would clip.
        Assert.Equal("Preamp: 0.0 dB", PreampMapper.ToPreampLine(3.0, muted: false));
    }

    [Fact]
    public void UsesInvariantCulture_EvenUnderSwedishLocale()
    {
        // The machine runs sv-SE, where the default decimal separator is a comma.
        // Equalizer APO would not parse "Preamp: -10,8 dB".
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("Preamp: -10.8 dB", PreampMapper.ToPreampLine(-10.8, muted: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NaN_IsTreatedAsSilence()
    {
        // Math.Clamp propagates NaN, which would emit "Preamp: NaN dB" — a line
        // Equalizer APO cannot parse, leaving the gain at whatever it was.
        // A failed COM read on a device-unplug path can produce this.
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(double.NaN, muted: false));
    }
}
