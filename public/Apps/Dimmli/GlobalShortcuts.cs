namespace Dimmli;

internal enum ShortcutAction { Open = 1, Brighter, Dimmer, Day, Evening, BrighterFine, DimmerFine }

internal sealed record Shortcut(ShortcutAction Action, Keys Key, string KeyLabel, string English, string Polish)
{
    public string Label => Texts.Get(English, Polish);
}

internal sealed class GlobalShortcuts(Action<ShortcutAction> dispatch) : IDisposable
{
    private const HotkeyModifiers Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    private readonly Hotkeys hotkeys = new();

    public static IReadOnlyList<Shortcut> Bindings { get; } =
    [
        new(ShortcutAction.Open, Keys.B, "B", "Open or close the panel", "Otwórz lub zamknij panel"),
        new(ShortcutAction.Brighter, Keys.PageUp, "PgUp", "Brighten by 5 points", "Rozjaśnij o 5 p.p."),
        new(ShortcutAction.Dimmer, Keys.PageDown, "PgDn", "Dim by 5 points", "Przyciemnij o 5 p.p."),
        new(ShortcutAction.Day, Keys.D1, "1", "Apply Day profile", "Włącz profil Dzień"),
        new(ShortcutAction.Evening, Keys.D2, "2", "Apply Evening profile", "Włącz profil Wieczór")
    ];

    public bool Enabled { get; private set; }
    public bool IsRegistered(ShortcutAction action) => hotkeys.IsRegistered((int)action);

    public void SetEnabled(bool enabled)
    {
        hotkeys.Clear();
        Enabled = enabled;
        if (!enabled)
            return;
        foreach (var shortcut in Bindings)
            Register(shortcut.Action, shortcut.Key, Modifiers);
        Register(ShortcutAction.BrighterFine, Keys.PageUp, Modifiers | HotkeyModifiers.Shift);
        Register(ShortcutAction.DimmerFine, Keys.PageDown, Modifiers | HotkeyModifiers.Shift);
    }

    private void Register(ShortcutAction action, Keys key, HotkeyModifiers modifiers) =>
        hotkeys.Register((int)action, key, modifiers, () => dispatch(action));

    public void Dispose() => hotkeys.Dispose();
}
