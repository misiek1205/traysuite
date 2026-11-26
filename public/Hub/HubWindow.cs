using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace TraySuite.Hub;

internal sealed class HubWindow : ModuleWindow
{
    public const string PreferencesKey = @"Software\TraySuite\Hub";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly CancellationTokenSource closing = new();
    private readonly System.Windows.Forms.Timer refreshRunning = new() { Interval = 1250 };
    private readonly Dictionary<string, PopupToggle> runningToggles = [];
    private readonly PackageCatalog catalog;
    private readonly PackageStore store;
    private IReadOnlyList<CatalogApp> apps;
    private Uri? activationManifest;
    private bool busy, settings, loaded, syncing;
    public bool ShowTray { get; private set; }
    public event Action<bool>? TrayChanged;
    public event Action? ReadyToExit;

    public HubWindow(string? local, bool showTray) : base("TraySuite", Texts.Get("Tools", "Narzędzia"), 650)
    {
        ShowTray = showTray;
        catalog = new PackageCatalog(http, local);
        store = new PackageStore(catalog, Path.Combine(LicenseManager.DataDirectory, "Apps"));
        apps = catalog.Fallback;
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                refreshRunning.Start();
                if (!loaded)
                    Run(Refresh);
                else
                    SyncRunning();
            }
            else
            {
                refreshRunning.Stop();
                ExitIfHidden();
            }
        };
        refreshRunning.Tick += (_, _) => SyncRunning();
        Render();
    }

    private void Render()
    {
        SuspendLayout();
        Body.SuspendLayout();
        try
        {
            runningToggles.Clear();
            ClearBody();
            if (settings)
                RenderSettings();
            else
            {
                Add(Columns(Button(Texts.Get("Settings", "Ustawienia"), () => ChangePage(true)),
                    Button(Texts.Get("Refresh", "Odśwież"), () => Run(Refresh))), 32);
                foreach (var app in apps)
                    Add(AppCard(app), store.InstalledVersion(app.Id) is null ? 84 : 112);
            }
            Body.Enabled = !busy;
        }
        finally
        {
            Body.ResumeLayout();
            ResumeLayout();
        }
        Place();
    }

    private Control AppCard(CatalogApp app)
    {
        string? installed = store.InstalledVersion(app.Id);
        var package = app.Package;
        bool update = installed is not null && Version.TryParse(installed, out var current)
            && Version.TryParse(package?.Version, out var published) && published > current;
        bool available = package?.DownloadUrl is not null && (!app.Pro || LicenseManager.IsPro);
        var card = new PopupCard(Theme.Card) { BackColor = Theme.Background, Padding = new Padding(8, 3, 8, 3) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = installed is null ? 3 : 4 };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 3, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 77));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, app.Pro ? 42 : 0));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(Label(app.Id, font: PopupAppearance.LabelFont), 0, 0);
        if (app.Pro)
            header.Controls.Add(new ProBadge { Margin = Padding.Empty, Anchor = AnchorStyles.Left }, 1, 0);
        string state = installed is null ? Texts.Get("Available", "Dostępne")
            : update ? Texts.Get("Update available", "Dostępna aktualizacja") : Texts.Get("Installed", "Zainstalowane");
        var status = Label(state, muted: true);
        status.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(status, 2, 0);
        content.Controls.Add(header, 0, 0);
        string packageSize = package is { SizeBytes: > 0 } ? FormatSize(package.SizeBytes) : "";
        var description = Label(app.Description, muted: true);
        var size = Label(packageSize, muted: true);
        size.TextAlign = ContentAlignment.MiddleRight;
        var details = Columns(description, size);
        details.ColumnStyles[0].Width = packageSize.Length == 0 ? 100 : 79;
        details.ColumnStyles[1].Width = packageSize.Length == 0 ? 0 : 21;
        content.Controls.Add(details, 0, 1);

        var install = Button(update ? Texts.Get("Update", "Aktualizuj") : Texts.Get("Install", "Zainstaluj"),
            () => Run(token => Install(app, token)));
        install.AccessibleName = $"{install.Text} {app.Id}";
        install.Enabled = available;
        if (app.Pro && !LicenseManager.IsPro && installed is null)
        {
            install.Dispose();
            content.Controls.Add(Button(Texts.Get("Activate Pro", "Aktywuj Pro"), () => ChangePage(true)), 0, 2);
        }
        else if (installed is null)
            content.Controls.Add(install, 0, 2);
        else
        {
            var open = Button(Texts.Get("Open", "Otwórz"), () => Run(_ => { store.Start(app, open: true); return Task.CompletedTask; }));
            open.AccessibleName = $"{open.Text} {app.Id}";
            open.Enabled = !app.Pro || LicenseManager.IsPro;
            var remove = Button(Texts.Get("Uninstall", "Odinstaluj"), () =>
            {
                Enabled = false;
                try
                {
                    if (MessageBox.Show(this, Texts.Get($"Uninstall {app.Id}?", $"Odinstalować {app.Id}?"), Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        Run(token => store.Uninstall(app.Id, token));
                }
                finally { Enabled = true; Activate(); }
            });
            remove.AccessibleName = $"{remove.Text} {app.Id}";
            if (!update)
                install.Dispose();
            content.Controls.Add(update ? Columns(open, install, remove) : Columns(open, remove), 0, 2);

            var running = new PopupToggle { Text = Texts.Get("Enabled", "Włączone"), Dock = DockStyle.Fill,
                Checked = AppLifecycle.IsRunning(app.Id), Enabled = !app.Pro || LicenseManager.IsPro };
            runningToggles[app.Id] = running;
            running.CheckedChanged += (_, _) =>
            {
                if (syncing)
                    return;
                Run(async token =>
                {
                    if (running.Checked)
                        store.Start(app, open: false);
                    else
                    {
                        await store.Stop(app.Id, token);
                        store.SetStartup(app.Id, false);
                    }
                });
            };
            var startup = new PopupToggle { Text = Texts.Get("Startup", "Autostart"), Dock = DockStyle.Fill,
                Checked = store.StartsWithWindows(app.Id), Enabled = !app.Pro || LicenseManager.IsPro };
            startup.CheckedChanged += (_, _) => Run(_ => { store.SetStartup(app.Id, startup.Checked); return Task.CompletedTask; });
            content.Controls.Add(Columns(running, startup), 0, 3);
        }
        card.Controls.Add(content);
        return card;
    }

    private static string FormatSize(long size) => size >= 1024 * 1024
        ? (size / 1048576d).ToString("0.0", CultureInfo.CurrentCulture) + " MB"
        : Math.Max(1, size / 1024).ToString(CultureInfo.CurrentCulture) + " KB";

    private void ChangePage(bool showSettings)
    {
        settings = showSettings;
        LogicalHeight = settings ? 310 : 650;
        SetStatus("");
        Render();
    }

    private void RenderSettings()
    {
        Add(Button(Texts.Get("Back", "Wróć"), () => ChangePage(false)), 31);
        var tray = new PopupToggle { Text = Texts.Get("Show Hub tray icon", "Pokazuj ikonę Huba w zasobniku"), Checked = ShowTray };
        tray.CheckedChanged += (_, _) => Run(_ =>
        {
            using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKey);
            if (!tray.Checked)
                Startup.Set("Hub", Application.ExecutablePath, false, onlyOwnEntry: true);
            preferences.SetValue("Tray", tray.Checked ? 1 : 0);
            ShowTray = tray.Checked;
            TrayChanged?.Invoke(ShowTray);
            return Task.CompletedTask;
        });
        Add(tray, 27);
        Add(Label(LicenseManager.IsPro ? Texts.Get("Pro is active", "Wersja Pro aktywna") : Texts.Get("Pro license", "Licencja Pro"),
            font: PopupAppearance.LabelFont), 22);
        var key = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 200,
            AccessibleName = Texts.Get("License key", "Klucz licencyjny"), BackColor = Theme.Card, ForeColor = Theme.Text };
        Add(key, 25);
        var activate = Button(Texts.Get("Activate", "Aktywuj"), () =>
        {
            string value = key.Text;
            Run(async token =>
            {
                activationManifest = (await LicenseManager.Activate(value, token)).PackageManifest;
                await Refresh(token);
            });
        });
        var buy = new LinkLabel { Dock = DockStyle.Fill, Text = Texts.Get("Buy Pro key", "Kup klucz Pro"),
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, DisabledLinkColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleCenter, Enabled = LicenseManager.Checkout is not null };
        buy.LinkClicked += (_, _) => Run(_ =>
        {
            using var process = Process.Start(new ProcessStartInfo(LicenseManager.Checkout!.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        Add(Columns(activate, buy), 31);
    }

    private async Task Install(CatalogApp app, CancellationToken token)
    {
        if (!catalog.LocalMode && app.Pro && LicenseManager.Read() is { } license)
        {
            var renewed = await LicenseManager.RequestPackageManifest(license, token);
            if (renewed is not null)
            {
                activationManifest = renewed;
                apps = await catalog.Load(activationManifest, token);
                app = apps.Single(item => item.Id == app.Id);
            }
        }
        await store.Install(app, token);
    }

    private async Task Refresh(CancellationToken token)
    {
        loaded = true;
        string? downloadError = null;
        if (!catalog.LocalMode && activationManifest is null && LicenseManager.Read() is { } license)
        {
            try { activationManifest = await LicenseManager.RequestPackageManifest(license, token); }
            catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                downloadError = Texts.Get("Pro downloads are temporarily unavailable.", "Pobieranie Pro jest chwilowo niedostępne.");
            }
        }
        try { apps = await catalog.Load(activationManifest, token); }
        catch (Exception error) when (activationManifest is not null && error is HttpRequestException or InvalidDataException or JsonException)
        {
            activationManifest = null;
            apps = await catalog.Load(null, token);
            downloadError = Texts.Get("Pro downloads are temporarily unavailable.", "Pobieranie Pro jest chwilowo niedostępne.");
        }
        SetStatus(downloadError ?? "");
    }

    private void SyncRunning()
    {
        if (busy)
            return;
        syncing = true;
        try
        {
            foreach (var (id, toggle) in runningToggles)
                toggle.Checked = AppLifecycle.IsRunning(id);
        }
        finally { syncing = false; }
    }

    private async void Run(Func<CancellationToken, Task> operation)
    {
        if (busy || IsDisposed)
            return;
        busy = true;
        Body.Enabled = false;
        SetStatus("");
        UseWaitCursor = true;
        try
        {
            await operation(closing.Token);

        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException
            or JsonException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception
            or KeyNotFoundException or OperationCanceledException or System.Security.SecurityException)
        {
            if (!IsDisposed)
                SetStatus(error.Message);
        }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                Render();
                ExitIfHidden();
            }
        }
    }

    private void ExitIfHidden()
    {
        if (!Visible && !ShowTray && !busy)
            ReadyToExit?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing.Cancel();
            refreshRunning.Dispose();
            http.Dispose();
            closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
