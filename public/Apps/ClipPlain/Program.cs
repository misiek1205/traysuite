using System.Runtime.InteropServices;
using System.Text;

namespace ClipPlain;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppLifecycle.Run("ClipPlain", args, () => ApplicationConfiguration.Initialize(),
        () => new TrayContext("ClipPlain", "ClipPlain", new ClipboardWindow(), SystemIcons.Application));
}

internal sealed class ClipboardWindow : ModuleWindow
{
    private const int ClipboardUpdate = 0x031D;
    private readonly Hotkeys hotkeys = new();
    private readonly List<string> history = [];
    private readonly System.Windows.Forms.Timer retry = new() { Interval = 65 };
    private readonly System.Windows.Forms.Timer paste = new() { Interval = 25 };
    private nint previousWindow, pasteTarget;
    private int retries, pasteChecks;
    private readonly WinEvent foregroundChanged;
    private readonly nint foregroundHook;

    public ClipboardWindow() : base("ClipPlain", Texts.Get("Clipboard history", "Historia schowka"), 385)
    {
        foregroundChanged = (_, _, window, _, _, _, _) => Remember(window);
        foregroundHook = SetWinEventHook(3, 3, 0, foregroundChanged, 0, 0, 2);
        Remember(GetForegroundWindow());
        bool hotkeyRegistered = hotkeys.Register(1, Keys.V, HotkeyModifiers.Control | HotkeyModifiers.Shift,
            () => CopyPlain(null, GetForegroundWindow()));
        retry.Tick += (_, _) => CaptureClipboard();
        paste.Tick += (_, _) => PasteToTarget();
        Render();
        SetStatus(hotkeyRegistered ? "Ctrl+Shift+V"
            : Texts.Get("Ctrl+Shift+V is already in use.", "Skrót Ctrl+Shift+V jest już zajęty."));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (AddClipboardFormatListener(Handle))
            BeginInvoke(CaptureClipboard);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        RemoveClipboardFormatListener(Handle);
        base.OnHandleDestroyed(e);
    }

    private void Remember(nint candidate)
    {
        if (candidate == 0 || IsHandleCreated && candidate == Handle || candidate == GetShellWindow())
            return;
        var name = new StringBuilder(128);
        if (GetClassName(candidate, name, name.Capacity) == 0 || name.ToString() is
            "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland"
            or "Progman" or "WorkerW")
            return;
        previousWindow = candidate;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == ClipboardUpdate)
        {
            retries = 4;
            CaptureClipboard();
        }
        base.WndProc(ref message);
    }

    private void CaptureClipboard()
    {
        retry.Stop();
        try
        {
            string text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (text.Length is 0 or > 1_048_576 || history.FirstOrDefault() == text)
                return;
            history.Remove(text);
            history.Insert(0, text);
            if (history.Count > 10)
                history.RemoveAt(10);
            Render();
        }
        catch (ExternalException)
        {
            if (retries-- > 0)
                retry.Start();
        }
    }

    private void Render()
    {
        Body.SuspendLayout();
        try
        {
            ClearBody();
            if (history.Count == 0)
                Add(Label(Texts.Get("Copy some text to see it here.", "Skopiuj tekst, aby zobaczyć go tutaj."), muted: true), 32);
            foreach (var text in history)
            {
                string preview = text[..Math.Min(text.Length, 100)].Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
                Add(Button(preview.Length == 0 ? Texts.Get("Whitespace", "Białe znaki") : preview,
                    () => CopyPlain(text, previousWindow)), 33);
            }
        }
        finally { Body.ResumeLayout(); }
    }

    private void CopyPlain(string? text, nint target)
    {
        try
        {
            text ??= Clipboard.GetText(TextDataFormat.UnicodeText);
            if (text.Length == 0)
                return;
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }
        catch (ExternalException)
        {
            SetStatus(Texts.Get("The clipboard is busy.", "Schowek jest zajęty."));
            return;
        }
        if (target == 0 || target == Handle)
            return;
        if (Visible)
            Hide();
        if (GetForegroundWindow() != target && !SetForegroundWindow(target))
            return;
        pasteTarget = target;
        pasteChecks = 20;
        paste.Start();
    }

    private void PasteToTarget()
    {
        if (GetForegroundWindow() != pasteTarget || --pasteChecks == 0)
        {
            paste.Stop();
            return;
        }
        if (Control.ModifierKeys != Keys.None || (GetAsyncKeyState((int)Keys.V) & 0x8000) != 0)
            return;
        paste.Stop();
        Input[] events = [Key(Keys.ControlKey, false), Key(Keys.V, false), Key(Keys.V, true), Key(Keys.ControlKey, true)];
        SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
    }

    private static Input Key(Keys key, bool up) => new()
    {
        Type = 1, Data = new() { Keyboard = new() { Key = (ushort)key, Flags = up ? 2u : 0u } }
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (foregroundHook != 0)
                UnhookWinEvent(foregroundHook);
            retry.Dispose();
            paste.Dispose();
            hotkeys.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public nint Extra;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nint Extra;
    }

    private delegate void WinEvent(nint hook, uint type, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module,
        WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(nint window);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
