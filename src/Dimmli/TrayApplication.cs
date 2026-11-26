using Microsoft.Win32;
using System.Text;
using System.Text.Json;

namespace Dimmli;

internal sealed class TrayApplication : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Keep the original Run value so the installer can adopt an existing portable setup.
    private const string StartupValueName = "Brightness";
    private const string PreferencesKey = @"Software\Dimmli";
    private const string OffsetsKey = PreferencesKey + @"\Offsets";
    private readonly TrayIcon tray;
    private readonly TrayWheel wheel;
    private readonly ContextMenuStrip menu = new();
    private readonly Icon icon;
    private readonly BrightnessPanel panel;
    private readonly GlobalShortcuts globalShortcuts;
    private CommandPipe? commands;
    private readonly ProfileSchedule schedule;
    private readonly FullscreenWatcher fullscreen;
    private readonly MonitorService service = new();
    private readonly MonitorNames monitorNames = new(PreferencesKey + @"\MonitorNames");
    private readonly Dictionary<string, string> aliases;
    private readonly Dictionary<string, int> offsets;
    private readonly Dictionary<string, int> restoreLevels = new(StringComparer.OrdinalIgnoreCase);
    private AutomationSettings automation;
    private Profile? scheduledProfile;
    private string? fullscreenDevice, boostedId;
    private bool fullscreenSuppressed, automationWrite;
    private TaskCompletionSource displayReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private bool diagnosticsRequested;
    private bool exiting;
    private bool displaysReady;
    private bool canAdjustDisplays;
    private bool dialogOpen;
    private long lastDismissal = -1000;
    private Point dismissalPoint;
    private string readyStatus = "";

    public TrayApplication()
    {
        using var resource = typeof(TrayApplication).Assembly.GetManifestResourceStream("Dimmli.Sun.ico")!;
        using var sourceIcon = new Icon(resource);
        icon = (Icon)sourceIcon.Clone();
        panel = new BrightnessPanel(RequestRefresh, profile => ApplyProfile(profile), SaveProfile, ToggleShortcuts);
        try
        {
            aliases = monitorNames.Load();
        }
        catch (Exception error) when (IsPreferenceError(error))
        {
            aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
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
        using var preferences = Registry.CurrentUser.OpenSubKey(PreferencesKey);
        automation = LoadAutomation(preferences?.GetValue("Automation") as string);
        offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var storedOffsets = Registry.CurrentUser.OpenSubKey(OffsetsKey))
            if (storedOffsets is not null)
                foreach (string id in storedOffsets.GetValueNames())
                    if (storedOffsets.GetValue(id) is int value && value is >= -20 and <= 20)
                        offsets[id] = value;
        schedule = new ProfileSchedule(profile =>
        {
            if (displaysReady)
            {
                scheduledProfile = null;
                ApplyProfile(profile, fromSchedule: true);
            }
            else
                scheduledProfile = profile;
        });
        fullscreen = new FullscreenWatcher(device =>
        {
            fullscreenDevice = device;
            fullscreenSuppressed = false;
            UpdateFullscreen();
        });
        globalShortcuts.SetEnabled(preferences?.GetValue("GlobalShortcuts") is not 0);
        panel.UpdateShortcuts(globalShortcuts);

        var open = menu.Items.Add(Texts.Get("Open brightness panel", "Otwórz panel jasności"));
        open.Click += (_, _) => panel.Open(Cursor.Position);
        menu.Items.Add(Texts.Get("Refresh displays", "Odśwież monitory")).Click += (_, _) => RequestRefresh();
        var diagnostics = menu.Items.Add(Texts.Get("Check monitors…", "Sprawdź monitory…"));
        diagnostics.Click += (_, _) => RequestDiagnostics();
        var automationItem = menu.Items.Add(Texts.Get("Automation…", "Automatyzacja…"));
        automationItem.Click += (_, _) => EditAutomation();
        var scroll = (ToolStripMenuItem)menu.Items.Add(Texts.Get("Scroll over tray icon to adjust brightness",
            "Reguluj jasność rolką nad ikoną"));
        scroll.Click += (_, _) => ToggleWheel();
        menu.Items.Add(Texts.Get("Keyboard shortcuts", "Skróty klawiaturowe")).Click += (_, _) =>
        {
            panel.Open(Cursor.Position);
            panel.ShowShortcutGuide(true);
        };
        var start = (ToolStripMenuItem)menu.Items.Add(Texts.Get("Start with Windows", "Uruchamiaj z Windows"));
        tray = new TrayIcon(icon, menu);
        wheel = new TrayWheel(tray);
        menu.Opening += (_, _) =>
        {
            start.Checked = StartsWithWindows();
            scroll.Checked = wheel.Enabled;
            diagnostics.Enabled = !dialogOpen;
            automationItem.Enabled = !dialogOpen;
        };
        start.Click += (_, _) => ToggleStartup(start.Checked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Texts.Get("Exit", "Zakończ")).Click += (_, _) => Exit();

        tray.Selected += position =>
        {
            if (dialogOpen)
                return;
            if (panel.Visible)
                panel.Hide();
            // Explorer can deactivate the flyout before delivering the click that dismissed it.
            else if (Environment.TickCount64 - lastDismissal > 250
                || Math.Abs(position.X - dismissalPoint.X) > 4 || Math.Abs(position.Y - dismissalPoint.Y) > 4)
                panel.Open(position);
            lastDismissal = -1000;
        };
        tray.WheelStep += steps =>
        {
            if (wheel.Available && wheel.Enabled && !exiting)
                panel.ShiftBy(steps * 5);
        };
        tray.Visible = true;
        if (preferences?.GetValue("TrayWheel") is 1)
            wheel.SetEnabled(true);

        writeTimer.Tick += (_, _) => { writeTimer.Stop(); ProcessWork(); };
        topologyTimer.Tick += (_, _) => { topologyTimer.Stop(); RequestRefresh(); };
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.TimeChanged += TimeChanged;
        Application.Idle += Start;
    }

    private void Start(object? sender, EventArgs args)
    {
        Application.Idle -= Start;
        RequestRefresh();
        ConfigureAutomation();
        commands = new CommandPipe(DispatchCommand);
    }

    private void DisplayChanged(object? sender, EventArgs args) => ScheduleTopologyRefresh();

    private void PowerChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume)
            ScheduleTopologyRefresh();
    }

    private void TimeChanged(object? sender, EventArgs args) => PostToPanel(() =>
    {
        TimeZoneInfo.ClearCachedData();
        schedule.Reevaluate();
    });

    private void ScheduleTopologyRefresh()
    {
        PostToPanel(() =>
        {
            topologyTimer.Stop();
            topologyTimer.Start();
        });
    }

    private void PostToPanel(Action action)
    {
        if (!panel.IsHandleCreated || panel.IsDisposed)
            return;

        try
        {
            panel.BeginInvoke(() =>
            {
                if (!exiting)
                    action();
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
        BeginDetection();
        UpdateWheelAvailability();
        writeTimer.Stop();
        panel.SetControlsEnabled(false);
        panel.SetStatus(Texts.Get("Detecting displays…", "Wykrywanie monitorów…"));
        ProcessWork();
    }

    private void RequestDiagnostics()
    {
        if (exiting || dialogOpen)
            return;
        diagnosticsRequested = true;
        writeTimer.Stop();
        ProcessWork();
    }

    private void QueueBrightness(BrightnessRow row, int value)
    {
        if ((exiting || refreshRequested) && !automationWrite)
            return;

        if (!automationWrite && row.Display.Id is { } id)
        {
            restoreLevels.Remove(id);
            if (string.Equals(boostedId, id, StringComparison.OrdinalIgnoreCase))
            {
                boostedId = null;
                fullscreenSuppressed = true;
            }
        }
        pending[row] = value;
        if (!writeTimer.Enabled)
            writeTimer.Start();
    }

    private async void ProcessWork()
    {
        if (busy)
            return;
        if (exiting && pending.Count == 0)
        {
            ExitThread();
            return;
        }
        busy = true;
        try
        {
            if (!exiting && pending.Count == 0 && (refreshRequested || diagnosticsRequested))
            {
                bool showReport = diagnosticsRequested;
                diagnosticsRequested = false;
                refreshRequested = false;
                BeginDetection();
                UpdateWheelAvailability();
                panel.SetControlsEnabled(false);
                panel.SetStatus(Texts.Get("Detecting displays…", "Wykrywanie monitorów…"));
                var displays = await Task.Run(service.Discover);
                if (!exiting && refreshRequested)
                {
                    diagnosticsRequested |= showReport;
                    return;
                }

                panel.SetDisplays(displays, QueueBrightness, aliases, RenameMonitor, offsets, CalibrateMonitor);
                displaysReady = true;
                displayReady.TrySetResult();
                if (exiting)
                {
                    UpdateFullscreen();
                    return;
                }
                int controllable = displays.Count(display => display.SetBrightness is not null);
                canAdjustDisplays = controllable > 0;
                UpdateWheelAvailability();
                UpdateProfileAvailability();
                panel.SetControlsEnabled(true);
                readyStatus = controllable > 0
                    ? Texts.Get($"Displays with brightness control: {controllable}/{displays.Count}",
                        $"Monitory z regulacją jasności: {controllable}/{displays.Count}")
                    : Texts.Get("Check DDC/CI in the monitor menu.", "Sprawdź DDC/CI w menu monitora.");
                panel.SetStatus(profileReadFailed && controllable > 0
                    ? Texts.Get("Could not read brightness profiles.", "Nie udało się odczytać profili jasności.")
                    : readyStatus);
                schedule.Reevaluate();
                if (scheduledProfile is { } profile)
                {
                    scheduledProfile = null;
                    ApplyProfile(profile, fromSchedule: true);
                }
                fullscreen.Reevaluate();
                UpdateFullscreen();
                if (showReport)
                {
                    string report = MonitorDiagnostics.CreateReport(displays);
                    panel.BeginInvoke(() => ShowDiagnostics(report));
                }
            }
            else if (pending.Count > 0)
            {
                var batch = pending.ToArray();
                pending.Clear();
                var results = await Task.Run(() => batch.Select(item =>
                    item.Key.Display.SetBrightness!(item.Value)).ToArray());

                for (int i = 0; i < batch.Length; i++)
                {
                    BrightnessRow row = batch[i].Key;
                    if (row.Display.Id is { } id && !string.Equals(id, boostedId, StringComparison.OrdinalIgnoreCase)
                        && restoreLevels.TryGetValue(id, out int original) && original == batch[i].Value && results[i] is not null)
                        restoreLevels.Remove(id);
                    if (!exiting && !refreshRequested)
                        row.ShowResult(results[i], pending.ContainsKey(row));
                }
                if (!exiting && !refreshRequested)
                    panel.SetStatus(results.Any(result => result is null)
                    ? Texts.Get("Monitor did not accept the change. Try Refresh.",
                        "Monitor odrzucił zmianę. Spróbuj odświeżyć listę.")
                    : readyStatus);
            }
        }
        finally
        {
            busy = false;
            if (exiting || (pending.Count == 0 && (refreshRequested || diagnosticsRequested)))
                panel.BeginInvoke(ProcessWork);
            else if (pending.Count > 0)
                writeTimer.Start();
        }
    }

    private void BeginDetection()
    {
        if (displaysReady)
            displayReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        displaysReady = false;
    }

    private void ShowDiagnostics(string report)
    {
        if (exiting || dialogOpen)
            return;
        dialogOpen = true;
        UpdateWheelAvailability();
        panel.Hide();
        try
        {
            MonitorDialogs.ShowReport(report);
        }
        finally
        {
            dialogOpen = false;
            UpdateWheelAvailability();
        }
    }

    private void RenameMonitor(Display display)
    {
        if (exiting || dialogOpen || display.Id is not { } id)
            return;

        dialogOpen = true;
        UpdateWheelAvailability();
        panel.Hide();
        try
        {
            string? alias = MonitorDialogs.Rename(display.Name, aliases.GetValueOrDefault(id, ""));
            if (alias is null)
                return;

            monitorNames.Save(id, alias);
            if (alias.Length == 0)
                aliases.Remove(id);
            else
                aliases[id] = alias;
            panel.UpdateMonitorName(id, alias);
        }
        catch (Exception error) when (IsPreferenceError(error))
        {
            panel.SetStatus(Texts.Get("Could not save the monitor name.", "Nie udało się zapisać nazwy monitora."));
        }
        finally
        {
            dialogOpen = false;
            UpdateWheelAvailability();
            if (!exiting)
                panel.Reopen();
        }
    }

    private static AutomationSettings LoadAutomation(string? json)
    {
        if (json is not null)
            try
            {
                if (JsonSerializer.Deserialize<AutomationSettings>(json) is { } value && value.IsValid())
                    return value;
            }
            catch (JsonException) { }
        return new();
    }

    private void EditAutomation()
    {
        if (dialogOpen || exiting)
            return;
        dialogOpen = true;
        UpdateWheelAvailability();
        panel.Hide();
        try
        {
            if (MonitorDialogs.EditAutomation(automation) is { } updated)
            {
                using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKey);
                preferences.SetValue("Automation", JsonSerializer.Serialize(updated), RegistryValueKind.String);
                automation = updated;
                ConfigureAutomation();
            }
        }
        catch (Exception error) when (IsPreferenceError(error))
        {
            MessageBox.Show(Texts.Get("Could not save automation settings.", "Nie udało się zapisać automatyzacji."),
                "Dimmli", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            dialogOpen = false;
            UpdateWheelAvailability();
        }
    }

    private void ConfigureAutomation()
    {
        if (automation.Schedule == ScheduleMode.Off)
            scheduledProfile = null;
        schedule.Configure(automation);
        if (!fullscreen.Configure(automation.Fullscreen))
            panel.SetStatus(Texts.Get("Could not watch for fullscreen windows.", "Nie udało się włączyć wykrywania pełnego ekranu."));
        UpdateFullscreen();
    }

    private void CalibrateMonitor(Display display)
    {
        if (exiting || dialogOpen || display.Id is not { } id)
            return;
        dialogOpen = true;
        UpdateWheelAvailability();
        panel.Hide();
        try
        {
            if (MonitorDialogs.EditOffset(aliases.GetValueOrDefault(id, display.Name), offsets.GetValueOrDefault(id)) is int offset)
            {
                using var settings = Registry.CurrentUser.CreateSubKey(OffsetsKey);
                if (offset == 0)
                {
                    settings.DeleteValue(id, throwOnMissingValue: false);
                    offsets.Remove(id);
                }
                else
                {
                    settings.SetValue(id, offset, RegistryValueKind.DWord);
                    offsets[id] = offset;
                }
                panel.UpdateMonitorOffset(id, offset);
            }
        }
        catch (Exception error) when (IsPreferenceError(error))
        {
            panel.SetStatus(Texts.Get("Could not save calibration.", "Nie udało się zapisać kalibracji."));
        }
        finally
        {
            dialogOpen = false;
            UpdateWheelAvailability();
            if (!exiting)
                panel.Reopen();
        }
    }

    private void UpdateFullscreen()
    {
        if (!displaysReady || (restoreLevels.Count == 0 && fullscreenDevice is null))
            return;

        var identified = panel.ProfileRows();
        BrightnessRow? target = null;
        if (!exiting && automation.Fullscreen && !fullscreenSuppressed && fullscreenDevice is not null)
        {
            var candidates = panel.DisplayRows.Where(row =>
                string.Equals(row.Display.ScreenDevice, fullscreenDevice, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (candidates.Length == 1 && candidates[0].Display.Id is { } id && identified.ContainsKey(id))
                target = candidates[0];
        }
        boostedId = target?.Display.Id;

        automationWrite = true;
        try
        {
            if (target is not null && boostedId is { } targetId)
            {
                restoreLevels.TryAdd(targetId, target.Target);
                target.SetTarget(automation.FullscreenLevel);
            }
            foreach (var (id, original) in restoreLevels.ToArray())
            {
                if (string.Equals(id, boostedId, StringComparison.OrdinalIgnoreCase)
                    || !identified.TryGetValue(id, out var row))
                    continue;
                if (row.Confirmed == original && row.Target == original && !pending.ContainsKey(row))
                    restoreLevels.Remove(id);
                else
                    row.SetTarget(original);
            }
        }
        finally
        {
            automationWrite = false;
        }
    }

    private async Task<CommandReply> DispatchCommand(BrightnessCommand command, CancellationToken cancellation)
    {
        try
        {
            return await panel.InvokeAsync(async token =>
            {
                while (!displaysReady && !exiting)
                    await displayReady.Task.WaitAsync(token);
                token.ThrowIfCancellationRequested();
                return exiting ? new CommandReply(false, "Dimmli is closing.") : ExecuteCommand(command);
            }, cancellation);
        }
        catch (InvalidOperationException)
        {
            return new CommandReply(false, "Dimmli is closing.");
        }
    }

    private CommandReply ExecuteCommand(BrightnessCommand command)
    {
        var rows = panel.DisplayRows.ToArray();
        if (command.List)
        {
            var result = new StringBuilder();
            for (int index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                string label = row.Display.SetBrightness is null ? "N/A" : $"{row.Confirmed}%";
                result.AppendLine($"{index + 1}. {row.DisplayName} — {label}");
            }
            return new(true, rows.Length == 0 ? "No monitors detected." : result.ToString().TrimEnd());
        }
        if (command.Profile is { } profile)
        {
            int count = ApplyProfile(profile);
            return count > 0 ? new(true, $"Profile accepted for {count} monitor(s).")
                : new(false, "This profile has no saved levels for the connected monitors.");
        }
        if (command.Monitor is int monitor)
        {
            if (monitor > rows.Length || rows[monitor - 1].Display.SetBrightness is null)
                return new(false, "Monitor does not have brightness control. Use --list to check its number.");
            rows = [rows[monitor - 1]];
        }
        int changed = 0;
        foreach (var row in rows.Where(row => row.Display.SetBrightness is not null))
        {
            row.SetTarget(command.Level!.Value);
            changed++;
        }
        return changed > 0 ? new(true, $"Brightness accepted for {changed} monitor(s).")
            : new(false, "No brightness controls available.");
    }

    private void UpdateWheelAvailability() =>
        wheel.Available = displaysReady && canAdjustDisplays && !dialogOpen && !exiting;

    private void ToggleWheel()
    {
        bool wasEnabled = wheel.Enabled;
        try
        {
            if (!wheel.SetEnabled(!wasEnabled))
                throw new InvalidOperationException();
            using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKey);
            preferences.SetValue("TrayWheel", wheel.Enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception error) when (IsPreferenceError(error) || error is InvalidOperationException)
        {
            wheel.SetEnabled(wasEnabled);
            MessageBox.Show(Texts.Get("Could not change tray wheel control.", "Nie udało się zmienić ustawienia rolki."),
                "Dimmli", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void HandleShortcut(ShortcutAction action)
    {
        if (exiting || dialogOpen)
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
            case ShortcutAction.BrighterFine:
                panel.ShiftBy(1);
                break;
            case ShortcutAction.DimmerFine:
                panel.ShiftBy(-1);
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

    private int ApplyProfile(Profile profile, bool fromSchedule = false)
    {
        if (!savedProfiles.TryGetValue(profile, out var targets))
            return 0;

        int count = 0;
        automationWrite = fromSchedule;
        try
        {
            foreach (var (id, row) in panel.ProfileRows())
                if (targets.TryGetValue(id, out int level))
                {
                    if (fromSchedule && restoreLevels.ContainsKey(id))
                        restoreLevels[id] = level;
                    if (!fromSchedule || !string.Equals(id, boostedId, StringComparison.OrdinalIgnoreCase))
                        row.SetTarget(level);
                    count++;
                }
        }
        finally
        {
            automationWrite = false;
        }
        return count;
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

    private static bool IsPreferenceError(Exception error) =>
        error is IOException or UnauthorizedAccessException or System.Security.SecurityException;

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
        displayReady.TrySetResult();
        fullscreen.Configure(false);
        UpdateFullscreen();
        wheel.SetEnabled(false);
        panel.Hide();
        tray.Visible = false;
        writeTimer.Stop();
        ProcessWork();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.TimeChanged -= TimeChanged;
            Application.Idle -= Start;
            topologyTimer.Dispose();
            writeTimer.Dispose();
            commands?.Dispose();
            schedule.Dispose();
            fullscreen.Dispose();
            globalShortcuts.Dispose();
            wheel.Dispose();
            tray.Dispose();
            menu.Dispose();
            panel.Dispose();
            icon.Dispose();
            service.Dispose();
        }
        base.Dispose(disposing);
    }
}
