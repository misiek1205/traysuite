using Microsoft.Win32;
using System.Text.Json;

namespace Dimmli;

internal sealed class TrayApplication : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Keep the original Run value so the installer can adopt an existing portable setup.
    private const string StartupValueName = "Brightness";
    private const string PreferencesKey = @"Software\Dimmli";
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly Icon icon;
    private readonly BrightnessPanel panel;
    private readonly GlobalShortcuts globalShortcuts;
    private readonly MonitorService service = new();
    // Keep profiles created by earlier releases under the previous app name.
    private readonly ProfileStore profileStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayLuminance", "profiles.json"));
    private Dictionary<Profile, Dictionary<string, int>> savedProfiles = [];
    private bool profileReadFailed;
    private readonly System.Windows.Forms.Timer writeTimer = new() { Interval = 90 };
    private readonly System.Windows.Forms.Timer topologyTimer = new() { Interval = 800 };
    private readonly Dictionary<BrightnessRow, int> pending = [];
    private bool busy;
    private bool refreshRequested;
    private bool exiting;
    private bool displaysReady;
    private long lastDismissal = -1000;
    private Point dismissalPoint;
    private string readyStatus = "";

    public TrayApplication()
    {
        using var resource = typeof(TrayApplication).Assembly.GetManifestResourceStream("Dimmli.Sun.ico")!;
        using var sourceIcon = new Icon(resource);
        icon = (Icon)sourceIcon.Clone();
        panel = new BrightnessPanel(RequestRefresh, ApplyProfile, SaveProfile, ToggleShortcuts);
        try
        {
            savedProfiles = profileStore.Load();
        }
        catch (Exception error) when (IsProfileError(error))
        {
            profileReadFailed = true;
        }
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
        globalShortcuts = new GlobalShortcuts(HandleShortcut);
        using (var preferences = Registry.CurrentUser.OpenSubKey(PreferencesKey))
            globalShortcuts.SetEnabled(preferences?.GetValue("GlobalShortcuts") is not 0);
        panel.UpdateShortcuts(globalShortcuts);

        var open = menu.Items.Add(Texts.Get("Open brightness panel", "Otwórz panel jasności"));
        open.Click += (_, _) => panel.Open(Cursor.Position);
        menu.Items.Add(Texts.Get("Refresh displays", "Odśwież monitory")).Click += (_, _) => RequestRefresh();
        menu.Items.Add(Texts.Get("Keyboard shortcuts", "Skróty klawiaturowe")).Click += (_, _) =>
        {
            panel.Open(Cursor.Position);
            panel.ShowShortcutGuide(true);
        };
        var start = (ToolStripMenuItem)menu.Items.Add(Texts.Get("Start with Windows", "Uruchamiaj z Windows"));
        menu.Opening += (_, _) => start.Checked = StartsWithWindows();
        start.Click += (_, _) => ToggleStartup(start.Checked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Texts.Get("Exit", "Zakończ")).Click += (_, _) => Exit();

        tray = new NotifyIcon { Icon = icon, Text = "Dimmli", ContextMenuStrip = menu, Visible = true };
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
        displaysReady = false;
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
                displaysReady = true;
                UpdateProfileAvailability();
                panel.SetControlsEnabled(true);
                int controllable = displays.Count(display => display.SetBrightness is not null);
                readyStatus = controllable > 0
                    ? Texts.Get($"Displays with brightness control: {controllable}/{displays.Count}",
                        $"Monitory z regulacją jasności: {controllable}/{displays.Count}")
                    : Texts.Get("Check DDC/CI in the monitor menu.", "Sprawdź DDC/CI w menu monitora.");
                panel.SetStatus(profileReadFailed && controllable > 0
                    ? Texts.Get("Could not read brightness profiles.", "Nie udało się odczytać profili jasności.")
                    : readyStatus);
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

    private void HandleShortcut(ShortcutAction action)
    {
        if (exiting)
            return;

        if (action == ShortcutAction.Open)
        {
            if (panel.Visible)
                panel.Hide();
            else
            {
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                panel.Open(new Point(area.Right - 1, area.Bottom - 1));
            }
            return;
        }

        if (!displaysReady)
            return;

        switch (action)
        {
            case ShortcutAction.Brighter:
                panel.ShiftBy(5);
                break;
            case ShortcutAction.Dimmer:
                panel.ShiftBy(-5);
                break;
            case ShortcutAction.Day:
                ApplyProfile(Profile.Day);
                break;
            case ShortcutAction.Evening:
                ApplyProfile(Profile.Evening);
                break;
        }
    }

    private void ToggleShortcuts()
    {
        bool enable = !globalShortcuts.Enabled;
        try
        {
            using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKey);
            preferences.SetValue("GlobalShortcuts", enable ? 1 : 0, RegistryValueKind.DWord);
            globalShortcuts.SetEnabled(enable);
            panel.UpdateShortcuts(globalShortcuts);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            panel.SetStatus(Texts.Get("Could not save the shortcut setting.",
                "Nie udało się zapisać ustawienia skrótów."));
        }
    }

    private void UpdateProfileAvailability()
    {
        var connected = panel.ProfileRows();
        panel.SetProfileAvailability(connected.Count > 0, HasMatch(Profile.Day), HasMatch(Profile.Evening));

        bool HasMatch(Profile profile) => savedProfiles.TryGetValue(profile, out var targets)
            && connected.Keys.Any(targets.ContainsKey);
    }

    private void ApplyProfile(Profile profile)
    {
        if (!savedProfiles.TryGetValue(profile, out var targets))
            return;

        foreach (var (id, row) in panel.ProfileRows())
            if (targets.TryGetValue(id, out int level))
                row.SetTarget(level);
    }

    private void SaveProfile(Profile profile)
    {
        var rows = panel.ProfileRows();
        if (rows.Count == 0)
            return;

        try
        {
            savedProfiles = profileStore.Save(profile, rows.ToDictionary(pair => pair.Key, pair => pair.Value.Target));
            profileReadFailed = false;
            UpdateProfileAvailability();
            panel.SetStatus(profile == Profile.Day
                ? Texts.Get("Day profile saved.", "Profil Dzień zapisany.")
                : Texts.Get("Evening profile saved.", "Profil Wieczór zapisany."));
        }
        catch (Exception error) when (IsProfileError(error))
        {
            panel.SetStatus(Texts.Get("Could not save the brightness profile.", "Nie udało się zapisać profilu jasności."));
        }
    }

    private static bool IsProfileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException;

    private static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(StartupValueName) is string;
    }

    private static void ToggleStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            else
                key.SetValue(StartupValueName, $"\"{Application.ExecutablePath}\"");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(Texts.Get("Could not update Windows startup settings.",
                "Nie udało się zmienić ustawień autostartu."), "Dimmli", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            globalShortcuts.Dispose();
            tray.Dispose();
            menu.Dispose();
            panel.Dispose();
            icon.Dispose();
            service.Dispose();
        }
        base.Dispose(disposing);
    }
}
