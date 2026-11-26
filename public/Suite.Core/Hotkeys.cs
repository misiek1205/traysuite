using System.Runtime.InteropServices;

namespace Suite.Core;

[Flags]
public enum HotkeyModifiers : uint { Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public sealed class Hotkeys : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> registered = [];

    public Hotkeys() => CreateHandle(new CreateParams { Parent = new nint(-3) });

    public bool Register(int id, Keys key, HotkeyModifiers modifiers, Action action)
    {
        if (registered.ContainsKey(id) || !RegisterHotKey(Handle, id, (uint)modifiers | 0x4000, (uint)key))
            return false;
        registered.Add(id, action);
        return true;
    }

    public bool IsRegistered(int id) => registered.ContainsKey(id);

    public void Unregister(int id)
    {
        if (registered.Remove(id))
            UnregisterHotKey(Handle, id);
    }

    public void Clear()
    {
        foreach (int id in registered.Keys)
            UnregisterHotKey(Handle, id);
        registered.Clear();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && registered.TryGetValue((int)message.WParam, out var action))
            action();
        else
            base.WndProc(ref message);
    }

    public void Dispose()
    {
        Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
