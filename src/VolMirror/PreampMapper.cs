using System.Globalization;

namespace VolMirror;

/// Maps a Windows endpoint volume reading onto an Equalizer APO preamp line.
public static class PreampMapper
{
    /// Gain emitted when muted, and the floor for very low volumes.
    /// Windows reports -128 dB at the bottom of the slider; that is not worth passing on.
    public const double SilenceDb = -100.0;

    public static string ToPreampLine(double levelDb, bool muted)
    {
        // NaN is folded in with mute rather than clamped: Math.Clamp propagates it,
        // and "Preamp: NaN dB" is a line Equalizer APO cannot parse. Silence is the
        // safe reading of "we do not know what the volume is".
        double gain = muted || double.IsNaN(levelDb)
            ? SilenceDb
            : Math.Clamp(levelDb, SilenceDb, 0.0);

        return string.Format(CultureInfo.InvariantCulture, "Preamp: {0:F1} dB", gain);
    }
}
