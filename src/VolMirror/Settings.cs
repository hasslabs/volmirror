using System.Text.Json;

namespace VolMirror;

public sealed class Settings
{
    /// The Behringer UCA202 on this machine, measured via EndpointVolumeProbe.
    /// Endpoint IDs are stable across reboots.
    public const string DefaultDeviceId = "{0.0.0.00000000}.{953bc6ad-4278-495a-83c9-22367cb2a16b}";
    public const string DefaultConfigDir = @"C:\Program Files\EqualizerAPO\config";

    public string DeviceId { get; set; } = DefaultDeviceId;
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
