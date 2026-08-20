using System.Text.Json;

namespace VolMirror;

public sealed class Settings
{
    /// Matched against the endpoint's friendly name, case-insensitively.
    /// Deliberately a name and not an ID: endpoint IDs change when Windows
    /// re-enumerates a device (installing Equalizer APO did exactly that to
    /// the UCA202), which would strand a hard-coded ID.
    public const string DefaultDeviceName = "USB Audio CODEC";
    public const string DefaultConfigDir = @"C:\Program Files\EqualizerAPO\config";

    public const int MinPollIntervalMs = 10;
    public const int MaxPollIntervalMs = 5000;
    public const int DefaultPollIntervalMs = 50;

    public string DeviceNameContains { get; set; } = DefaultDeviceName;

    /// Optional exact endpoint ID, for the case where two identical devices
    /// share a name. Ignored if that endpoint is not present.
    public string? PinnedDeviceId { get; set; }

    public string ConfigDir { get; set; } = DefaultConfigDir;
    public int PollIntervalMs { get; set; } = DefaultPollIntervalMs;

    /// Gain at the bottom of the slider. A wider range makes each step coarser;
    /// a narrower one limits how quiet it can go.
    public double MinDb { get; set; } = PreampMapper.DefaultMinDb;

    public string VolumeFilePath => Path.Combine(ConfigDir, ApoConfig.VolumeFileName);
    public string ConfigFilePath => Path.Combine(ConfigDir, "config.txt");

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VolMirror", "settings.json");

    public static Settings Load(string path)
    {
        Settings settings;

        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings()
                : Save(new Settings(), path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fall through to defaults rather than refusing to start.
            settings = new Settings();
        }

        settings.Normalize();
        return settings;
    }

    /// Repairs values that are well-formed JSON but unusable. Without this a
    /// hand-edited file can kill the app before the message loop starts, where
    /// there is no tray icon and no dialog to explain why - and Load would never
    /// rewrite the bad value, so every relaunch fails identically.
    ///
    /// Nulls are possible despite the non-nullable annotations: System.Text.Json
    /// does not honour them by default (RespectNullableAnnotations is false).
    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(DeviceNameContains))
            DeviceNameContains = DefaultDeviceName;

        if (string.IsNullOrWhiteSpace(ConfigDir))
            ConfigDir = DefaultConfigDir;

        if (string.IsNullOrWhiteSpace(PinnedDeviceId))
            PinnedDeviceId = null;

        // Timer.Interval throws below 1, which would brick startup permanently.
        PollIntervalMs = Math.Clamp(PollIntervalMs, MinPollIntervalMs, MaxPollIntervalMs);

        if (double.IsNaN(MinDb) || MinDb >= 0.0)
            MinDb = PreampMapper.DefaultMinDb;

        MinDb = Math.Clamp(MinDb, PreampMapper.SilenceDb, -6.0);
    }

    private static Settings Save(Settings settings, string path)
    {
        settings.Save(path);
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
