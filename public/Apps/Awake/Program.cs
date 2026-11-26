using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Awake;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppLifecycle.Run("Awake", args, () => ApplicationConfiguration.Initialize(),
        () => new TrayContext("Awake", "Awake", new AwakeWindow()));
}

internal sealed class AwakeWindow : ModuleWindow
{
    private const uint Continuous = 0x80000000, RequireSystemAndDisplay = 0x00000003;
    private readonly ComboBox processes = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Label mode;
    private DateTimeOffset? until;
    private Process? watched;

    public AwakeWindow() : base("Awake", Texts.Get("Keep the PC and screen awake", "Blokada uśpienia komputera i ekranu"), 306)
    {
        mode = Label("", font: PopupAppearance.ValueFont);
        Add(Card(mode, featured: true), 55);
        Add(Columns(Button(Texts.Get("Off", "Wyłączone"), () => Set(null, null, false)),
            Button(Texts.Get("30 min", "30 min"), () => Set(TimeSpan.FromMinutes(30), null)),
            Button(Texts.Get("1 hour", "1 godz."), () => Set(TimeSpan.FromHours(1), null)),
            Button(Texts.Get("Unlimited", "Bez limitu"), () => Set(null, null))), 35);
        processes.AccessibleName = Texts.Get("Running process", "Uruchomiony proces");
        processes.BackColor = Theme.Card;
        processes.ForeColor = Theme.Text;
        processes.DropDown += (_, _) => RefreshProcesses();
        Add(PopupField.Wrap(processes), 38);
        Add(Button(Texts.Get("While selected process runs", "Dopóki działa wybrany proces"), SelectProcess,
            PopupGlyph.Cup, PopupButtonStyle.Primary), 36);
        timer.Tick += (_, _) =>
        {
            if (until <= DateTimeOffset.UtcNow || watched is { HasExited: true })
                Set(null, null, false);
            else
                UpdateMode(true);
        };
        UpdateMode(false);
    }

    private void RefreshProcesses()
    {
        var selected = processes.SelectedItem as ProcessChoice;
        var choices = new List<ProcessChoice>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
                try
                {
                    if (process.SessionId == Session && process.Id != Environment.ProcessId)
                        choices.Add(new(process.Id, process.StartTime, process.ProcessName));
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        processes.Items.Clear();
        foreach (var choice in choices.OrderBy(choice => choice.Name).ThenBy(choice => choice.Id))
            processes.Items.Add(choice);
        if (selected is not null && processes.Items.Contains(selected))
            processes.SelectedItem = selected;
    }

    private static readonly int Session = CurrentSession();

    private static int CurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private void SelectProcess()
    {
        if (processes.SelectedItem is not ProcessChoice choice)
        {
            SetStatus(Texts.Get("Choose a running process.", "Wybierz uruchomiony proces."));
            return;
        }

        Process? process = null;
        try
        {
            process = Process.GetProcessById(choice.Id);
            if (process.StartTime != choice.Started || process.HasExited)
                throw new InvalidOperationException();
            Set(null, process);
            process = null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus(Texts.Get("The process has already exited or is inaccessible.", "Proces zakończył działanie lub jest niedostępny."));
        }
        finally { process?.Dispose(); }
    }

    private void Set(TimeSpan? duration, Process? process, bool enable = true)
    {
        if (SetThreadExecutionState(Continuous | (enable ? RequireSystemAndDisplay : 0)) == 0)
        {
            process?.Dispose();
            SetStatus(Texts.Get("Windows rejected the power request.", "Windows odrzucił żądanie zasilania."));
            return;
        }
        watched?.Dispose();
        watched = process;
        until = duration is { } period ? DateTimeOffset.UtcNow + period : null;
        timer.Enabled = enable && (until.HasValue || watched is not null);
        SetStatus("");
        UpdateMode(enable);
    }

    private void UpdateMode(bool enabled)
    {
        mode.Text = !enabled ? Texts.Get("Off", "Wyłączone") : watched is not null ? watched.ProcessName
            : until is { } end ? $"{end - DateTimeOffset.UtcNow:hh\\:mm\\:ss}" : Texts.Get("Unlimited", "Bez limitu");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SetThreadExecutionState(Continuous);
            timer.Dispose();
            watched?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record ProcessChoice(int Id, DateTime Started, string Name)
    {
        public override string ToString() => $"{Name} ({Id})";
    }

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint executionState);
}
