using System.Runtime.InteropServices;
using System.Text;

namespace TraySuite.Hub;

internal sealed class ToolHotkeys(Action<string, string[]?> launch) : IDisposable
{
    private Hotkeys? hotkeys;

    public bool Apply(bool enabled)
    {
        hotkeys?.Dispose();
        hotkeys = null;
        if (!enabled) return true;

        hotkeys = new Hotkeys();
        const HotkeyModifiers modifier = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        bool preview = hotkeys.Register(1, Keys.Space, modifier, Preview);
        bool focus = hotkeys.Register(2, Keys.F, modifier, () => launch("FocusDim", null));
        bool shelf = hotkeys.Register(3, Keys.D, modifier, () => launch("DropShelf", null));
        return preview && focus && shelf;
    }

    private void Preview()
    {
        try
        {
            string[] paths = ExplorerSelection();
            if (paths.Length > 0)
                launch("QuickView", paths);
        }
        catch (Exception error) when (error is COMException or InvalidCastException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
    }

    private static string[] ExplorerSelection()
    {
        nint foreground = GetForegroundWindow();
        var className = new StringBuilder(64);
        if (GetClassName(foreground, className, className.Capacity) == 0 || className.ToString() != "CabinetWClass")
            return [];

        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return [];
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic windows = shell.Windows();
            try
            {
                for (int index = 0; index < (int)windows.Count; index++)
                {
                    dynamic? window = windows.Item(index);
                    if (window is null) continue;
                    try
                    {
                        if ((long)window.HWND != foreground.ToInt64()) continue;
                        return ReadSelection(window);
                    }
                    catch (COMException) { }
                    finally { Release(window); }
                }
            }
            finally { Release(windows); }
        }
        finally { Release(shell); }
        return [];
    }

    private static string[] ReadSelection(dynamic window)
    {
        dynamic folder = window.Document;
        try
        {
            dynamic items = folder.SelectedItems();
            try
            {
                var paths = new List<string>();
                for (int index = 0; index < Math.Min(200, (int)items.Count); index++)
                {
                    dynamic item = items.Item(index);
                    try
                    {
                        string path = item.Path;
                        if (File.Exists(path)) paths.Add(path);
                    }
                    finally { Release(item); }
                }
                return paths.ToArray();
            }
            finally { Release(items); }
        }
        finally { Release(folder); }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    public void Dispose() => hotkeys?.Dispose();

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int max);
}
