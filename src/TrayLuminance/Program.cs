namespace TrayLuminance;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Share the mutex with the previous portable release.
        using var instance = new Mutex(true, @"Local\Brightness.Tray", out bool firstInstance);
        if (!firstInstance)
            return;

        ApplicationConfiguration.Initialize();
        using var app = new TrayApplication();
        Application.Run(app);
    }
}
