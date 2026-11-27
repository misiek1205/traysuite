using System.Runtime.InteropServices;

namespace TraySuite.Hub;

internal static class ToolShortcuts
{
    public static void Install(CatalogApp app, string executable)
    {
        dynamic shell = CreateShell();
        var created = new List<string>();
        try
        {
            foreach (string path in Paths(app))
            {
                if (File.Exists(path))
                {
                    if (!Owned(shell, path, executable))
                        throw new IOException($"A different shortcut already exists at {path}.");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".traysuite-{Guid.NewGuid():N}.lnk");
                try
                {
                    dynamic shortcut = shell.CreateShortcut(temporary);
                    try
                    {
                        shortcut.TargetPath = executable;
                        shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
                        shortcut.Description = app.DescriptionEn;
                        shortcut.Save();
                    }
                    finally { Marshal.FinalReleaseComObject(shortcut); }
                    File.Move(temporary, path);
                    created.Add(path);
                }
                finally { File.Delete(temporary); }
            }
        }
        catch
        {
            foreach (string path in created)
                File.Delete(path);
            throw;
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    public static void Remove(CatalogApp app, string executable)
    {
        dynamic shell = CreateShell();
        try
        {
            foreach (string path in Paths(app))
                if (File.Exists(path) && Owned(shell, path, executable))
                    File.Delete(path);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static object CreateShell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")
        ?? throw new InvalidOperationException("Windows shortcuts are unavailable."))!;

    private static bool Owned(dynamic shell, string path, string executable)
    {
        dynamic shortcut = shell.CreateShortcut(path);
        try { return string.Equals((string)shortcut.TargetPath, executable, StringComparison.OrdinalIgnoreCase); }
        finally { Marshal.FinalReleaseComObject(shortcut); }
    }

    private static IEnumerable<string> Paths(CatalogApp app)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (programs.Length > 0)
            yield return Path.Combine(programs, "TraySuite", app.Id + ".lnk");
        string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        if (app.AcceptsFiles && sendTo.Length > 0)
            yield return Path.Combine(sendTo, "TraySuite " + app.Id + ".lnk");
    }
}
