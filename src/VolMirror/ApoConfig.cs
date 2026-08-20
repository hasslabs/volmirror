using System.Text;

namespace VolMirror;

/// Touches Equalizer APO's own config.txt as little as possible: one Include line,
/// prepended once. Everything else in that file belongs to the user.
public static class ApoConfig
{
    public const string VolumeFileName = "volume.txt";
    public const string IncludeLine = "Include: " + VolumeFileName;

    /// Adds the Include line if it is missing. Returns true if the file was changed.
    ///
    /// Deliberately byte-level rather than ReadAllLines/WriteAllLines: Equalizer APO
    /// also accepts ANSI configs (it falls back to CP_ACP when a UTF-8 decode yields
    /// U+FFFD), and round-tripping one of those through a UTF-8 decode would replace
    /// every non-ASCII byte with U+FFFD permanently, breaking the user's Device:
    /// patterns. We never decode the existing bytes; we only prepend ASCII ones.
    public static bool EnsureInclude(string configPath)
    {
        byte[] existing = File.Exists(configPath) ? File.ReadAllBytes(configPath) : [];

        if (HasInclude(existing))
            return false;

        // Prepended, not appended: if the file ends inside a Device: or If: block,
        // an appended Include would be scoped to that block and APO would skip it
        // entirely. Line 1 is the only position guaranteed to be global.
        byte[] prefix = Encoding.ASCII.GetBytes(IncludeLine + "\r\n");
        byte[] combined = new byte[prefix.Length + existing.Length];

        int offset = 0;
        // A UTF-8 BOM has to stay first, or APO reads it as part of the first key.
        if (existing.Length >= 3 && existing[0] == 0xEF && existing[1] == 0xBB && existing[2] == 0xBF)
        {
            Array.Copy(existing, 0, combined, 0, 3);
            offset = 3;
        }

        Array.Copy(prefix, 0, combined, offset, prefix.Length);
        Array.Copy(existing, offset, combined, offset + prefix.Length, existing.Length - offset);

        AtomicWrite(configPath, combined);
        return true;
    }

    /// True if any line is an Include of our file. APO splits on the first colon and
    /// trims, so "Include:volume.txt" and "Include :  volume.txt" are the same
    /// directive - matching only the canonical spelling would append a duplicate,
    /// and preamps are additive, so the gain would be applied twice.
    private static bool HasInclude(byte[] content)
    {
        // Latin1 never fails and never substitutes: every byte maps to one char.
        // We only look for ASCII, so a mis-guessed encoding cannot cause a false hit.
        foreach (string line in Encoding.Latin1.GetString(content).Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
                continue;

            if (line[..colon].Trim().Equals("Include", StringComparison.OrdinalIgnoreCase) &&
                line[(colon + 1)..].Trim().Equals(VolumeFileName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void AtomicWrite(string path, byte[] content)
    {
        // The user's filter chain is not regenerable; an interrupted in-place write
        // would truncate it.
        string tmp = path + ".volmirror-tmp";
        try
        {
            File.WriteAllBytes(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
