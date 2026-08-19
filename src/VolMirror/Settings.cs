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

    public string DeviceNameContains { get; set; } = DefaultDeviceName;

    /// Optional exact endpoint ID, for the case where two identical devices
    /// share a name. Ignored if that endpoint is not present.
    public string? PinnedDeviceId { get; set; }

    public string ConfigDir { get; set; } = DefaultConfigDir;
    public int PollIntervalMs { get; set; } = 50;

    public string VolumeFilePath => Path.Combine(ConfigDir, ApoConfig.VolumeFileName);
    public string ConfigFilePath => Path.Combine(ConfigDir, "config.txt");

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VolMirror", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Fall through to defaults rather than refusing to start.
        }

        var settings = new Settings();
        settings.Save(path);
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
