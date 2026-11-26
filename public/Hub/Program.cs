using Microsoft.Win32;

namespace TraySuite.Hub;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? local = Environment.GetEnvironmentVariable("TRAYSUITE_LOCAL_PACKAGES");
        if (args is ["--local", var directory])
        {
            local = directory;
            args = ["--open"];
        }

        using var preferences = Registry.CurrentUser.OpenSubKey(HubWindow.PreferencesKey);
        bool showTray = preferences?.GetValue("Tray") is not 0;
        if (!showTray && args is ["--start"])
            return 0;

        return AppLifecycle.Run("Hub", args, () => ApplicationConfiguration.Initialize(), () =>
        {
            using var resource = typeof(Program).Assembly.GetManifestResourceStream("TraySuite.Icon")!;
            using var source = new Icon(resource);
            var icon = (Icon)source.Clone();
            var window = new HubWindow(local, showTray);
            window.Disposed += (_, _) => icon.Dispose();
            var context = new TrayContext("Hub", window.Text, window, icon, window.ShowTray);
            window.TrayChanged += visible => context.TrayVisible = visible;
            window.ReadyToExit += context.ExitThread;
            return context;
        });
    }
}
