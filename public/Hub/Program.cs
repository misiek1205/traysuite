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
            var window = new HubWindow(local, showTray);
            var restart = new ToolStripMenuItem(ExplorerRestart.ActionText);
            restart.Click += async (_, _) =>
            {
                restart.Enabled = false;
                try { await ExplorerRestart.Run(); }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException)
                {
                    MessageBox.Show(Texts.Get("Could not restart Explorer.", "Nie udało się ponownie uruchomić Eksploratora.")
                        + Environment.NewLine + error.Message, window.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { restart.Enabled = true; }
            };
            var context = new TrayContext("Hub", window.Text, window, trayVisible: window.ShowTray, extraAction: restart);
            window.TrayChanged += visible => context.TrayVisible = visible;
            window.ReadyToExit += context.ExitThread;
            return context;
        });
    }
}
