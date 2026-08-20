using System.Windows.Forms;

namespace VolMirror;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "VolMirror.SingleInstance", out bool isFirst);
        if (!isFirst)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(Settings.Load(Settings.DefaultPath)));
    }
}
