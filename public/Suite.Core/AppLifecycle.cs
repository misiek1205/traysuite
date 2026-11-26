using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Suite.Core;

public static class AppLifecycle
{
    public static string MutexName(string id) => id == "Dimmli" ? @"Local\Brightness.Tray" : $@"Local\TraySuite.{id}";

    public static bool IsRunning(string id)
    {
        if (!Mutex.TryOpenExisting(MutexName(id), out var instance))
            return false;
        using (instance)
            return true;
    }

    public static Task<CommandReply> Send(string id, string command, CancellationToken token) =>
        CommandPipe<string>.Send(id + ".lifecycle", command, token, allowForeground: command == "--open");

    public static int Forward(string id, string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            return Send(id, command, timeout.Token).GetAwaiter().GetResult().Success ? 0 : 1;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    public static CommandPipe<string> Listen(string id, Control window, Action open, Action exit) =>
        new(id + ".lifecycle", async (command, token) =>
        {
            if (command == "--open")
                await window.InvokeAsync(open, token);
            return new CommandReply(command is "--open" or "--exit", command);
        }, command =>
        {
            if (command == "--exit" && !window.IsDisposed)
                try { window.BeginInvoke(exit); }
                catch (InvalidOperationException) { }
        });

    public static int Run(string id, string[] args, Action initialize, Func<TrayContext> create, bool pro = false)
    {
        if (args.Length > 1 || args.Length == 1 && args[0] is not ("--open" or "--start" or "--exit"))
            return 2;
        string command = args.FirstOrDefault() ?? "--open";
        using var instance = new Mutex(true, MutexName(id), out bool first);
        if (!first)
            return command == "--start" ? 0 : Forward(id, command);
        if (command == "--exit")
            return 0;
        initialize();
        if (pro && !LicenseManager.IsPro)
        {
            if (command != "--start")
                MessageBox.Show(Texts.Get("Activate Pro in TraySuite Hub to use this tool.",
                    "Aktywuj wersję Pro w TraySuite Hub, aby użyć tego narzędzia."), id);
            return 1;
        }
        using var app = create();
        if (pro)
            app.RequirePro();
        app.Run(command == "--open");
        return 0;
    }

    internal static void AllowForeground(SafePipeHandle pipe)
    {
        if (GetNamedPipeServerProcessId(pipe, out uint processId))
            AllowSetForegroundWindow(processId);
    }

    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
}

public sealed class TrayContext : ApplicationContext
{
    private readonly PopupWindow popup;
    private readonly TrayIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly CommandPipe<string> listener;
    private LicenseMonitor? licenseMonitor;
    private bool disposed;

    public TrayContext(string id, string title, PopupWindow popup, Icon icon, bool trayVisible = true,
        ToolStripMenuItem? extraAction = null)
    {
        this.popup = popup;
        _ = popup.Handle;
        var open = menu.Items.Add(Texts.Get("Open", "Otwórz"));
        open.Click += (_, _) => Open();
        var startup = (ToolStripMenuItem)menu.Items.Add(Texts.Get("Start with Windows", "Uruchamiaj z Windows"));
        string executable = Application.ExecutablePath;
        startup.Click += (_, _) =>
        {
            try { Startup.Set(id, executable, !startup.Checked); }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                MessageBox.Show(error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        menu.Opening += (_, _) => startup.Checked = Startup.IsEnabled(id, executable);
        if (extraAction is not null)
            menu.Items.Add(extraAction);
        menu.Items.Add(Texts.Get("Exit", "Zakończ")).Click += (_, _) => ExitThread();
        tray = new TrayIcon(icon, menu, title) { Visible = trayVisible };
        tray.Selected += popup.Toggle;
        listener = AppLifecycle.Listen(id, popup, Open, ExitThread);
    }

    public bool TrayVisible { get => tray.Visible; set => tray.Visible = value; }

    public void Open()
    {
        var bounds = tray.IconBounds();
        var anchor = bounds.IsEmpty ? PopupWindow.ClockAnchor()
            : new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        popup.Open(anchor);
    }

    internal void RequirePro() => licenseMonitor = new LicenseMonitor(popup, () =>
    {
        if (!LicenseManager.IsPro)
            ExitThread();
    });

    internal void Run(bool show)
    {
        if (show)
            Application.Idle += OpenOnce;
        Application.Run(this);
    }

    private void OpenOnce(object? sender, EventArgs args)
    {
        Application.Idle -= OpenOnce;
        Open();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            Application.Idle -= OpenOnce;
            listener.Dispose();
            licenseMonitor?.Dispose();
            tray.Dispose();
            menu.Dispose();
            if (!popup.IsDisposed)
                popup.Dispose();
        }
        base.Dispose(disposing);
    }
}
