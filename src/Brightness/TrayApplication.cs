using Microsoft.Win32;

namespace Brightness;

internal sealed class TrayApplication : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly Icon icon;
    private readonly BrightnessPanel panel;
    private readonly MonitorService service = new();
    private readonly System.Windows.Forms.Timer writeTimer = new() { Interval = 90 };
    private readonly System.Windows.Forms.Timer topologyTimer = new() { Interval = 800 };
    private readonly Dictionary<BrightnessRow, int> pending = [];
    private bool busy;
    private bool refreshRequested;
    private bool exiting;
    private long lastDismissal = -1000;
    private Point dismissalPoint;
    private string readyStatus = "";

    public TrayApplication()
    {
        using var resource = typeof(TrayApplication).Assembly.GetManifestResourceStream("Brightness.Sun.ico")!;
        using var sourceIcon = new Icon(resource);
        icon = (Icon)sourceIcon.Clone();
        panel = new BrightnessPanel(RequestRefresh);
        panel.Deactivate += (_, _) =>
        {
            if (!panel.Visible)
                return;
            lastDismissal = Environment.TickCount64;
            dismissalPoint = Cursor.Position;
            panel.Hide();
        };
        // Create a window handle so notifications can marshal onto this message loop.
        _ = panel.Handle;

        var open = menu.Items.Add(Texts.Get("Open brightness panel", "Otwórz panel jasności"));
        open.Click += (_, _) => panel.Open(Cursor.Position);
        menu.Items.Add(Texts.Get("Refresh displays", "Odśwież monitory")).Click += (_, _) => RequestRefresh();
        var start = (ToolStripMenuItem)menu.Items.Add(Texts.Get("Start with Windows", "Uruchamiaj z Windows"));
        menu.Opening += (_, _) => start.Checked = StartsWithWindows();
        start.Click += (_, _) => ToggleStartup(start.Checked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Texts.Get("Exit", "Zakończ")).Click += (_, _) => Exit();

        tray = new NotifyIcon { Icon = icon, Text = "Brightness", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button != MouseButtons.Left)
                return;

            Point position = Cursor.Position;
            if (panel.Visible)
                panel.Hide();
            // Explorer can deactivate the flyout before delivering the click that dismissed it.
            else if (Environment.TickCount64 - lastDismissal > 250
                || Math.Abs(position.X - dismissalPoint.X) > 4 || Math.Abs(position.Y - dismissalPoint.Y) > 4)
                panel.Open(position);
            lastDismissal = -1000;
        };

        writeTimer.Tick += (_, _) => { writeTimer.Stop(); ProcessWork(); };
        topologyTimer.Tick += (_, _) => { topologyTimer.Stop(); RequestRefresh(); };
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        panel.BeginInvoke(RequestRefresh);
    }

    private void DisplayChanged(object? sender, EventArgs args) => ScheduleTopologyRefresh();

    private void PowerChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume)
            ScheduleTopologyRefresh();
    }

    private void ScheduleTopologyRefresh()
    {
        if (!panel.IsHandleCreated || panel.IsDisposed)
            return;

        try
        {
            panel.BeginInvoke(() =>
            {
                if (exiting)
                    return;
                topologyTimer.Stop();
                topologyTimer.Start();
            });
        }
        catch (InvalidOperationException)
        {
            // The window can close while a display notification is being delivered.
        }
    }

    private void RequestRefresh()
    {
        if (exiting)
            return;

        refreshRequested = true;
        writeTimer.Stop();
        pending.Clear();
        panel.SetControlsEnabled(false);
        panel.SetStatus(Texts.Get("Detecting displays…", "Wykrywanie monitorów…"));
        ProcessWork();
    }

    private void QueueBrightness(BrightnessRow row, int value)
    {
        if (exiting || refreshRequested)
            return;

        pending[row] = value;
        if (!writeTimer.Enabled)
            writeTimer.Start();
    }

    private async void ProcessWork()
    {
        if (busy || exiting)
            return;
        busy = true;
        try
        {
            if (refreshRequested)
            {
                refreshRequested = false;
                var displays = await Task.Run(service.Discover);
                if (exiting || refreshRequested)
                    return;

                panel.SetDisplays(displays, QueueBrightness);
                panel.SetControlsEnabled(true);
                int controllable = displays.Count(display => display.SetBrightness is not null);
                readyStatus = controllable > 0
                    ? Texts.Get($"Displays with brightness control: {controllable}/{displays.Count}",
                        $"Monitory z regulacją jasności: {controllable}/{displays.Count}")
                    : Texts.Get("Check DDC/CI in the monitor menu.", "Sprawdź DDC/CI w menu monitora.");
                panel.SetStatus(readyStatus);
            }
            else if (pending.Count > 0)
            {
                var batch = pending.ToArray();
                pending.Clear();
                var results = await Task.Run(() => batch.Select(item =>
                    item.Key.Display.SetBrightness!(item.Value)).ToArray());

                if (exiting || refreshRequested)
                    return;

                for (int i = 0; i < batch.Length; i++)
                {
                    BrightnessRow row = batch[i].Key;
                    row.ShowResult(results[i], pending.ContainsKey(row));
                }
                panel.SetStatus(results.Any(result => result is null)
                    ? Texts.Get("Monitor did not accept the change. Try Refresh.",
                        "Monitor odrzucił zmianę. Spróbuj odświeżyć listę.")
                    : readyStatus);
            }
        }
        finally
        {
            busy = false;
            if (exiting)
                ExitThread();
            else if (refreshRequested)
                panel.BeginInvoke(ProcessWork);
            else if (pending.Count > 0)
                writeTimer.Start();
        }
    }

    private static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Brightness") is string;
    }

    private static void ToggleStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.DeleteValue("Brightness", throwOnMissingValue: false);
            else
                key.SetValue("Brightness", $"\"{Application.ExecutablePath}\"");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(Texts.Get("Could not update Windows startup settings.",
                "Nie udało się zmienić ustawień autostartu."), "Brightness", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Exit()
    {
        exiting = true;
        panel.Hide();
        tray.Visible = false;
        if (!busy)
            ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            topologyTimer.Dispose();
            writeTimer.Dispose();
            tray.Dispose();
            menu.Dispose();
            panel.Dispose();
            icon.Dispose();
            service.Dispose();
        }
        base.Dispose(disposing);
    }
}
