using Microsoft.Win32;

namespace VolMirror;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// Task Manager's Startup tab does not delete the Run value when you disable an
    /// entry - it writes a flag here. Reading only the Run key would report enabled
    /// for something Windows will never launch.
    private const string ApprovedKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "VolMirror";

    public static string CurrentCommand => Quote(Environment.ProcessPath);

    /// True only when the Run value points at *this* executable and Windows has not
    /// disabled the entry. Checking merely that a value exists would report enabled
    /// after the exe moved - e.g. once it is published somewhere else - while
    /// Windows launches a path that no longer exists.
    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);

            return key?.GetValue(ValueName) is string command
                && PointsAt(command, Environment.ProcessPath)
                && !IsDisabledByWindows();
        }
    }

    public static void SetEnabled(bool enabled)
    {
        // CreateSubKey rather than OpenSubKey(writable: true), which returns null
        // when the key is missing.
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);

        if (enabled)
            key.SetValue(ValueName, CurrentCommand);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static string Quote(string? path) => $"\"{path}\"";

    /// Compares the executable inside a Run command line against a path.
    public static bool PointsAt(string command, string? exePath)
    {
        if (exePath is null)
            return false;

        string fromCommand = ExtractExecutable(command);
        if (fromCommand.Length == 0)
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(fromCommand), Path.GetFullPath(exePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// Pulls the executable out of a Run command line, which may be quoted and may
    /// carry arguments.
    public static string ExtractExecutable(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int closing = command.IndexOf('"', 1);
            return closing > 0 ? command[1..closing] : command[1..];
        }

        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static bool IsDisabledByWindows()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ApprovedKey);

        // First byte is 0x02 when enabled and 0x03 when disabled: bit 0 is the flag.
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } flag && (flag[0] & 1) != 0;
    }
}
