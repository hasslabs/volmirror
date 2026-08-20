using System.Globalization;
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class PreampMapperTests
{
    [Fact]
    public void FullVolume_IsZeroGain()
    {
        Assert.Equal("Preamp: 0.0 dB", PreampMapper.ToPreampLine(1.0, muted: false));
    }

    [Fact]
    public void HalfSlider_IsHalfTheRange()
    {
        Assert.Equal("Preamp: -30.0 dB", PreampMapper.ToPreampLine(0.5, muted: false));
    }

    [Fact]
    public void EveryStepIsTheSameSize()
    {
        // The point of the whole curve. Windows' own taper gave 0.32 dB per 2% step
        // near the top and 2.1 dB near the bottom, so the top of the slider felt dead.
        double top    = PreampMapper.ScalarToDb(1.00) - PreampMapper.ScalarToDb(0.98);
        double middle = PreampMapper.ScalarToDb(0.52) - PreampMapper.ScalarToDb(0.50);
        double bottom = PreampMapper.ScalarToDb(0.12) - PreampMapper.ScalarToDb(0.10);

        Assert.Equal(1.2, top, precision: 6);
        Assert.Equal(1.2, middle, precision: 6);
        Assert.Equal(1.2, bottom, precision: 6);
    }

    [Fact]
    public void EveryTwoPercentStepChangesTheLine()
    {
        // The symptom Viktor reported: most presses produced no change at all.
        var seen = new HashSet<string>();
        for (int i = 0; i <= 50; i++)
            seen.Add(PreampMapper.ToPreampLine(i / 50.0, muted: false));

        Assert.Equal(51, seen.Count);
    }

    [Fact]
    public void Muted_IsSilence()
    {
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(0.7, muted: true));
    }

    [Fact]
    public void Muted_WinsOverFullVolume()
    {
        // Windows reports mute independently of level; mute must not be inferred
        // from level == 0, and level must not override mute.
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(1.0, muted: true));
    }

    [Fact]
    public void BottomOfSlider_IsSilence()
    {
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(0.0, muted: false));
    }

    [Fact]
    public void OutOfRangeScalar_IsClamped()
    {
        Assert.Equal("Preamp: 0.0 dB", PreampMapper.ToPreampLine(1.5, muted: false));
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(-0.5, muted: false));
    }

    [Fact]
    public void NaN_IsTreatedAsSilence()
    {
        // Math.Clamp propagates NaN, which would emit "Preamp: NaN dB" - a line
        // Equalizer APO cannot parse. A failed COM read can produce this.
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(double.NaN, muted: false));
    }

    [Fact]
    public void CustomMinDb_ChangesTheRange()
    {
        Assert.Equal("Preamp: -20.0 dB", PreampMapper.ToPreampLine(0.5, muted: false, minDb: -40.0));
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
            Assert.Equal("Preamp: -27.0 dB", PreampMapper.ToPreampLine(0.55, muted: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
