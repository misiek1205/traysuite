using Microsoft.Win32;

namespace Suite.Core;

public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(string name, string? executable = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) is string command && (executable is null
            || string.Equals(command, Command(executable), StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, $"\"{executable}\"", StringComparison.OrdinalIgnoreCase));
    }

    public static void Set(string name, string executable, bool enabled, bool onlyOwnEntry = false)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(name, Command(executable));
        else if (!onlyOwnEntry || IsEnabled(name, executable))
            key.DeleteValue(name, throwOnMissingValue: false);
    }

    private static string Command(string executable) => $"\"{executable}\" --start";
}
