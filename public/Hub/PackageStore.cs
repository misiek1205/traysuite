using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace TraySuite.Hub;

internal sealed class PackageStore(PackageCatalog catalog, string root)
{
    private const string VersionFile = ".traysuite-version";

    public string Executable(string id) => Path.Combine(root, id, id + ".exe");
    public string? InstalledVersion(string id)
    {
        if (!File.Exists(Executable(id)))
            return null;
        try { return File.ReadAllText(Marker(id)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return "0.0"; }
    }
    public bool StartsWithWindows(string id) => Startup.IsEnabled(StartupName(id), Executable(id));
    public void SetStartup(string id, bool enabled) => Startup.Set(StartupName(id), Executable(id), enabled, onlyOwnEntry: true);
    private string Marker(string id) => Path.Combine(root, id, VersionFile);
    private static string StartupName(string id) => id == "Dimmli" ? "Brightness" : id;

    public void Start(CatalogApp app, bool open)
    {
        if (app.Pro && !LicenseManager.IsPro)
            throw new InvalidOperationException(Texts.Get("Activate Pro first.", "Najpierw aktywuj wersję Pro."));
        var executable = Executable(app.Id);
        if (!File.Exists(executable))
            throw new FileNotFoundException(Texts.Get("Install this tool first.", "Najpierw zainstaluj narzędzie."));
        using var process = Process.Start(new ProcessStartInfo(executable, open ? "--open" : "--start")
        {
            UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)!
        });
    }

    public async Task Stop(string id, CancellationToken token)
    {
        var candidates = Process.GetProcessesByName(id);
        string executable = Executable(id);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var installed = candidates.Where(process => MatchesExecutable(process, executable)).ToArray();
            if (AppLifecycle.IsRunning(id))
            {
                var reply = await AppLifecycle.Send(id, "--exit", timeout.Token);
                if (!reply.Success)
                    throw new IOException(reply.Message);
                while (AppLifecycle.IsRunning(id))
                    await Task.Delay(100, timeout.Token);
            }
            foreach (var process in installed)
                await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException(Texts.Get("Close the running tool and retry.",
                "Zamknij uruchomione narzędzie i spróbuj ponownie."));
        }
        finally
        {
            foreach (var process in candidates)
                process.Dispose();
        }
    }

    private static bool MatchesExecutable(Process process, string executable)
    {
        try { return string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public async Task Install(CatalogApp app, CancellationToken token)
    {
        var source = app.Package;
        if (source is null || !Version.TryParse(source.Version, out _) || source.DownloadUrl is null
            || app.Pro && !LicenseManager.IsPro)
            throw new InvalidOperationException(Texts.Get("This package is unavailable.", "Ta paczka jest niedostępna."));

        Directory.CreateDirectory(root);
        using var operation = Lock(app.Id);
        string destination = Path.Combine(root, app.Id);
        string staging = Path.Combine(root, "." + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(staging, "app");
        string backup = Path.Combine(root, "." + app.Id + ".backup");
        Recover(destination, backup);
        Directory.CreateDirectory(payload);
        try
        {
            var uri = new Uri(source.DownloadUrl, UriKind.Absolute);
            if (uri.IsFile && Path.GetExtension(uri.LocalPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                await Task.Run(() => CopyLoose(uri.LocalPath, payload, app.Id, token), token);
            else
            {
                if (!source.HasHash)
                    throw new InvalidDataException(Texts.Get("The package SHA-256 is missing.", "Brakuje sumy SHA-256 paczki."));
                string zip = Path.Combine(staging, "package.zip");
                await catalog.CopyPackage(uri, zip, token);
                await using (var input = File.OpenRead(zip))
                {
                    byte[] hash = await SHA256.HashDataAsync(input, token);
                    if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(source.Sha256!)))
                        throw new InvalidDataException(Texts.Get("The package SHA-256 does not match.", "Suma SHA-256 paczki jest niezgodna."));
                }
                await Task.Run(() => Extract(zip, payload, token), token);
            }
            if (!File.Exists(Path.Combine(payload, app.Id + ".exe")))
                throw new InvalidDataException(Texts.Get("Executable missing from the package.", "Brak pliku wykonywalnego w paczce."));
            await File.WriteAllTextAsync(Path.Combine(payload, VersionFile), source.Version, token);
            bool running = AppLifecycle.IsRunning(app.Id);
            await Stop(app.Id, token);
            try
            {
                if (Directory.Exists(destination))
                    await MoveInstalledDirectory(destination, backup, token);
                Directory.Move(payload, destination);
            }
            catch
            {
                Recover(destination, backup);
                if (running && File.Exists(Executable(app.Id)))
                    Start(app, open: false);
                throw;
            }
            RemoveDirectory(backup);
            if (running)
                Start(app, open: false);
        }
        finally { RemoveDirectory(staging); }
    }

    public async Task Uninstall(string id, CancellationToken token)
    {
        using var operation = Lock(id);
        string destination = Path.Combine(root, id);
        string backup = Path.Combine(root, "." + id + ".backup");
        Recover(destination, backup);
        await Stop(id, token);
        bool startup = StartsWithWindows(id);
        if (startup)
            SetStartup(id, false);
        string tombstone = Path.Combine(root, ".removed-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (Directory.Exists(destination))
                await MoveInstalledDirectory(destination, tombstone, token);
        }
        catch
        {
            if (startup)
                SetStartup(id, true);
            throw;
        }
        RemoveDirectory(tombstone);
    }

    private static async Task MoveInstalledDirectory(string source, string destination, CancellationToken token)
    {
        for (int retry = 0; ; retry++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException error) when (retry < 15 && (error.HResult & 0xffff) is 32 or 33)
            {
                // Windows can hold an image or scan handle briefly after the process exits.
                await Task.Delay(200, token);
            }
        }
    }

    private FileStream Lock(string id) => new(Path.Combine(root, "." + id + ".lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void Recover(string destination, string backup)
    {
        if (!Directory.Exists(backup))
            return;
        if (Directory.Exists(destination))
            Directory.Delete(backup, recursive: true);
        else
            Directory.Move(backup, destination);
    }

    private static void RemoveDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void CopyLoose(string executable, string destination, string id, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(executable)!;
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            if (name.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(name).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                File.Copy(path, Path.Combine(destination, name));
        }
    }

    private static void Extract(string zip, string destination, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 4096 || archive.Entries.Sum(item => item.Length) > 160L * 1024 * 1024)
            throw new InvalidDataException("Archive exceeds the extraction limit.");
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var parts = entry.FullName.Replace('\\', '/').TrimEnd('/').Split('/');
            if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains(':') || ReservedName(part))
                || (entry.ExternalAttributes & 0xF0000000) == 0xA0000000)
                throw new InvalidDataException("Archive contains an invalid path.");
        }
        archive.ExtractToDirectory(destination, overwriteFiles: false);
    }

    private static bool ReservedName(string part)
    {
        string name = part.Split('.')[0].TrimEnd().ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal)
                || name.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(name[3]);
    }
}
