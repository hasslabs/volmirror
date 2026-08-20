using System.Globalization;

namespace VolMirror;

/// Maps a Windows endpoint volume reading onto an Equalizer APO preamp line.
public static class PreampMapper
{
    /// Gain emitted when muted, and the floor for very low volumes.
    public const double SilenceDb = -100.0;

    /// Gain at the bottom of the slider. Below roughly -60 dB nothing is audible
    /// anyway, so spending slider travel there only makes the rest coarser.
    public const double DefaultMinDb = -60.0;

    /// Maps the slider position to gain, linearly in dB.
    ///
    /// Deliberately NOT the dB value Windows reports. Windows derives that from the
    /// device's own volume range, and the UCA202 declares -128 dB as its minimum,
    /// so the curve gets stretched over an absurd span: the top tenth of the slider
    /// covers 1.6 dB while the bottom tenth covers 10.5 dB. At Windows' 2% step that
    /// is 0.32 dB per keypress near the top - below the threshold of audibility, so
    /// the volume appears to do nothing until the third or fourth press.
    ///
    /// Linear-in-dB gives the same step everywhere: with minDb -60 and a 2% step,
    /// every press is 1.2 dB, which is audible across the whole range.
    public static double ScalarToDb(double scalar, double minDb = DefaultMinDb)
    {
        if (double.IsNaN(scalar))
            return SilenceDb;

        scalar = Math.Clamp(scalar, 0.0, 1.0);

        return scalar <= 0.0 ? SilenceDb : minDb * (1.0 - scalar);
    }

    /// Builds the config line from a slider position (0.0-1.0) and the mute flag.
    public static string ToPreampLine(double scalar, bool muted, double minDb = DefaultMinDb)
    {
        // NaN is folded in with mute rather than clamped: "Preamp: NaN dB" is a line
        // Equalizer APO cannot parse, and silence is the safe reading of an unknown
        // volume.
        double gain = muted || double.IsNaN(scalar)
            ? SilenceDb
            : Math.Clamp(ScalarToDb(scalar, minDb), SilenceDb, 0.0);

        // minDb * (1 - 1.0) yields negative zero, which formats as "-0.0 dB".
        if (gain == 0.0)
            gain = 0.0;

        return string.Format(CultureInfo.InvariantCulture, "Preamp: {0:F1} dB", gain);
    }
}
