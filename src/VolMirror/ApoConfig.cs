namespace VolMirror;

/// Touches Equalizer APO's own config.txt as little as possible: one Include line,
/// added once. Everything else in that file belongs to the user.
public static class ApoConfig
{
    public const string VolumeFileName = "volume.txt";
    public const string IncludeLine = "Include: " + VolumeFileName;

    /// Returns true if the line was added.
    public static bool EnsureInclude(string configPath)
    {
        var lines = File.Exists(configPath)
            ? File.ReadAllLines(configPath).ToList()
            : new List<string>();

        if (lines.Any(l => l.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase)))
            return false;

        lines.Add(IncludeLine);
        File.WriteAllLines(configPath, lines);
        return true;
    }
}
