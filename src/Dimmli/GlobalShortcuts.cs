using System.Runtime.InteropServices;

namespace Dimmli;

internal enum ShortcutAction { Open = 1, Brighter, Dimmer, Day, Evening, BrighterFine, DimmerFine }

internal sealed record Shortcut(ShortcutAction Action, Keys Key, string KeyLabel, string English, string Polish)
{
    public string Label => Texts.Get(English, Polish);
}

internal sealed class GlobalShortcuts : NativeWindow, IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const uint ControlAltNoRepeat = 0x4003;
    private readonly HashSet<ShortcutAction> registered = [];
    private readonly Action<ShortcutAction> dispatch;

    public static IReadOnlyList<Shortcut> Bindings { get; } =
    [
        new(ShortcutAction.Open, Keys.B, "B", "Open or close the panel", "Otwórz lub zamknij panel"),
        new(ShortcutAction.Brighter, Keys.PageUp, "PgUp", "Brighten by 5 points", "Rozjaśnij o 5 p.p."),
        new(ShortcutAction.Dimmer, Keys.PageDown, "PgDn", "Dim by 5 points", "Przyciemnij o 5 p.p."),
        new(ShortcutAction.Day, Keys.D1, "1", "Apply Day profile", "Włącz profil Dzień"),
        new(ShortcutAction.Evening, Keys.D2, "2", "Apply Evening profile", "Włącz profil Wieczór")
    ];

    public GlobalShortcuts(Action<ShortcutAction> dispatch)
    {
        this.dispatch = dispatch;
        CreateHandle(new CreateParams { Parent = new nint(-3) }); // HWND_MESSAGE
    }

    public bool Enabled { get; private set; }
    public bool IsRegistered(ShortcutAction action) => registered.Contains(action);

    public void SetEnabled(bool enabled)
    {
        foreach (var action in registered)
            UnregisterHotKey(Handle, (int)action);
        registered.Clear();
        Enabled = enabled;

        if (enabled)
        {
            foreach (var shortcut in Bindings)
                if (RegisterHotKey(Handle, (int)shortcut.Action, ControlAltNoRepeat, (uint)shortcut.Key))
                    registered.Add(shortcut.Action);
            RegisterFine(ShortcutAction.BrighterFine, Keys.PageUp);
            RegisterFine(ShortcutAction.DimmerFine, Keys.PageDown);
        }
    }

    private void RegisterFine(ShortcutAction action, Keys key)
    {
        const uint shift = 0x0004;
        if (RegisterHotKey(Handle, (int)action, ControlAltNoRepeat | shift, (uint)key))
            registered.Add(action);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage && registered.Contains((ShortcutAction)(int)message.WParam))
        {
            dispatch((ShortcutAction)(int)message.WParam);
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        SetEnabled(false);
        DestroyHandle();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
