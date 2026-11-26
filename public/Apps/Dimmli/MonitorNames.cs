using Microsoft.Win32;

namespace Dimmli;

internal sealed class MonitorNames(string keyPath)
{
    public Dictionary<string, string> Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (key is not null)
            foreach (var id in key.GetValueNames())
                if (key.GetValue(id) is string { Length: > 0 } name)
                    names[id] = name;
        return names;
    }

    public void Save(string id, string alias)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (alias.Length == 0)
            key.DeleteValue(id, throwOnMissingValue: false);
        else
            key.SetValue(id, alias, RegistryValueKind.String);
    }
}
