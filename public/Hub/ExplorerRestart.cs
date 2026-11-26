using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TraySuite.Hub;

internal static class ExplorerRestart
{
    public static async Task Run()
    {
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        nint shell = GetShellWindow();
        if (shell != 0)
        {
            GetWindowThreadProcessId(shell, out uint pid);
            Process? process;
            try { process = Process.GetProcessById((int)pid); }
            catch (ArgumentException) { process = null; }
            using (process)
            {
                if (process is not null && !process.HasExited)
                {
                    if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(Texts.Get("The active shell is not Windows Explorer.",
                            "Aktywna powłoka nie jest Eksploratorem Windows."));
                    // Target the shell process only, even when Explorer folder windows use separate processes.
                    try { process.Kill(); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
        }
        if (GetShellWindow() == 0)
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })?.Dispose();
    }

    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}
