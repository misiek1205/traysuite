namespace Suite.Core;

public sealed class LicenseMonitor : IDisposable
{
    private readonly FileSystemWatcher? watcher;
    private readonly System.Windows.Forms.Timer expiry = new();
    private readonly Control window;
    private readonly Action changed;

    public LicenseMonitor(Control window, Action changed)
    {
        this.window = window;
        this.changed = changed;
        try
        {
            Directory.CreateDirectory(LicenseManager.DataDirectory);
            watcher = new FileSystemWatcher(LicenseManager.DataDirectory, "license.dat")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += Updated;
            watcher.Changed += Updated;
            watcher.Deleted += Updated;
            watcher.Renamed += Updated;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            watcher?.Dispose();
            watcher = null;
        }
        expiry.Tick += (_, _) => Check();
        SetDeadline();
    }

    private void Updated(object sender, FileSystemEventArgs args)
    {
        try { window.BeginInvoke(Check); }
        catch (InvalidOperationException) { }
    }

    private void Check()
    {
        SetDeadline();
        changed();
    }

    private void SetDeadline()
    {
        expiry.Stop();
        if (LicenseManager.Read()?.ExpiresAt is { } until)
        {
            expiry.Interval = (int)Math.Clamp((until - DateTimeOffset.UtcNow).TotalMilliseconds + 100, 250, 86_400_000);
            expiry.Start();
        }
    }

    public void Dispose()
    {
        watcher?.Dispose();
        expiry.Dispose();
    }
}
