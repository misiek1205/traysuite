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

    protected override int LogicalWidth => 468;

    public HubWindow(string? local, bool showTray) : base("TraySuite", Texts.Get("Tools", "Narzędzia"), 694)
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
        int offset = ScrollOffset;
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
                Add(Columns(Button(Texts.Get("Settings", "Ustawienia"), () => ChangePage(true), PopupGlyph.Settings),
                    Button(Texts.Get("Refresh", "Odśwież"), () => Run(Refresh), PopupGlyph.Refresh)), 37);
                bool pro = LicenseManager.IsPro;
                foreach (var app in apps)
                    Add(AppCard(app, pro), store.InstalledVersion(app.Id) is null ? 102 : 136);
            }
            Body.Enabled = !busy;
        }
        finally
        {
            Body.ResumeLayout();
            ResumeLayout();
        }
        Place();
        ScrollOffset = offset;
    }

    private Control AppCard(CatalogApp app, bool pro)
    {
        string? installed = store.InstalledVersion(app.Id);
        var package = app.Package;
        bool update = installed is not null && Version.TryParse(installed, out var current)
            && Version.TryParse(package?.Version, out var published) && published > current;
        bool available = package?.DownloadUrl is not null && (!app.Pro || pro);
        var content = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = installed is null ? 2 : 3, BackColor = Theme.Card
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        var mark = new ModuleMark(app.Id, 36) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 5), TabStop = false };
        content.Controls.Add(mark, 0, 0);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5), ColumnCount = 3, RowCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, app.Pro ? 46 : 0));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 53));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
        var title = Label(app.Id, font: PopupAppearance.LabelFont);
        title.AutoSize = true;
        header.Controls.Add(title, 0, 0);
        if (app.Pro)
            header.Controls.Add(new ProBadge { Margin = new Padding(7, 0, 0, 0), Anchor = AnchorStyles.Left }, 1, 0);
        string state = installed is null ? Texts.Get("Available", "Dostępne")
            : update ? Texts.Get("Update available", "Dostępna aktualizacja") : Texts.Get("Installed", "Zainstalowane");
        var status = Label(state, muted: true);
        if (update)
            status.ForeColor = Theme.Accent;
        status.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(status, 2, 0);
        content.Controls.Add(header, 1, 0);
        string packageSize = package is { SizeBytes: > 0 } ? FormatSize(package.SizeBytes) : "";
        var description = Label(app.Description, muted: true);
        var size = Label(packageSize, muted: true);
        size.TextAlign = ContentAlignment.MiddleRight;
        var details = Columns(description, size);
        details.ColumnStyles[0].Width = packageSize.Length == 0 ? 100 : 83;
        details.ColumnStyles[1].Width = packageSize.Length == 0 ? 0 : 17;
        header.Controls.Add(details, 0, 1);
        header.SetColumnSpan(details, 3);

        var install = Button(update ? Texts.Get("Update", "Aktualizuj") : Texts.Get("Install", "Zainstaluj"),
            () => Run(token => Install(app, token)), PopupGlyph.Download,
            update ? PopupButtonStyle.Primary : PopupButtonStyle.Standard);
        install.ForeColor = Theme.Accent;
        install.AccessibleName = $"{install.Text} {app.Id}";
        install.Enabled = available;
        Control actions;
        if (app.Pro && !pro && installed is null)
        {
            install.Dispose();
            actions = Button(Texts.Get("Activate Pro", "Aktywuj Pro"), () => ChangePage(true), PopupGlyph.Lock);
        }
        else if (installed is null)
            actions = install;
        else
        {
            var open = Button(Texts.Get("Open", "Otwórz"), () => Run(_ => { store.Start(app, open: true); return Task.CompletedTask; }),
                style: update ? PopupButtonStyle.Standard : PopupButtonStyle.Primary);
            open.AccessibleName = $"{open.Text} {app.Id}";
            open.Enabled = !app.Pro || pro;
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
            }, style: PopupButtonStyle.Quiet);
            remove.AccessibleName = $"{remove.Text} {app.Id}";
            if (!update)
                install.Dispose();
            actions = update ? Columns(open, install, remove) : Columns(open, remove);

            var running = new PopupToggle { Text = Texts.Get("Enabled", "Włączone"), Dock = DockStyle.Fill,
                Checked = AppLifecycle.IsRunning(app.Id), Enabled = !app.Pro || pro };
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
                Checked = store.StartsWithWindows(app.Id), Enabled = !app.Pro || pro };
            startup.CheckedChanged += (_, _) => Run(_ => { store.SetStartup(app.Id, startup.Checked); return Task.CompletedTask; });
            var options = Columns(running, startup);
            options.Margin = new Padding(2, 5, 0, 0);
            content.Controls.Add(options, 0, 2);
            content.SetColumnSpan(options, 2);
        }
        actions.Margin = Padding.Empty;
        if (installed is null)
        {
            var singleAction = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
            singleAction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            singleAction.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            singleAction.Controls.Add(actions, 1, 0);
            actions = singleAction;
        }
        content.Controls.Add(actions, 0, 1);
        content.SetColumnSpan(actions, 2);
        var card = Card(content, new Padding(11, 9, 11, 9));
        content.BackColor = Theme.Card;
        return card;
    }

    private static string FormatSize(long size) => size >= 1024 * 1024
        ? (size / 1048576d).ToString("0.0", CultureInfo.CurrentCulture) + " MB"
        : Math.Max(1, size / 1024).ToString(CultureInfo.CurrentCulture) + " KB";

    private void ChangePage(bool showSettings)
    {
        settings = showSettings;
        SetSubtitle(settings ? Texts.Get("Settings", "Ustawienia") : Texts.Get("Tools", "Narzędzia"));
        LogicalHeight = settings ? 389 : 694;
        ScrollOffset = 0;
        SetStatus("");
        Render();
    }

    private void RenderSettings()
    {
        Add(Button(Texts.Get("Back", "Wróć"), () => ChangePage(false), PopupGlyph.Back,
            PopupButtonStyle.Quiet, leftAligned: true), 34);
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
        Add(Card(tray), 44);
        Add(Label(LicenseManager.IsPro ? Texts.Get("Pro is active", "Wersja Pro aktywna") : Texts.Get("Pro license", "Licencja Pro"),
            font: PopupAppearance.LabelFont), 22);
        var key = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 200,
            AccessibleName = Texts.Get("License key", "Klucz licencyjny"), BackColor = Theme.Card, ForeColor = Theme.Text };
        Add(PopupField.Wrap(key), 38);
        var activate = Button(Texts.Get("Activate", "Aktywuj"), () =>
        {
            string value = key.Text;
            Run(async token =>
            {
                activationManifest = (await LicenseManager.Activate(value, token)).PackageManifest;
                await Refresh(token, renewManifest: activationManifest is null);
            });
        }, style: PopupButtonStyle.Primary);
        var buy = new LinkLabel { Dock = DockStyle.Fill, Text = Texts.Get("Buy Pro key", "Kup klucz Pro"),
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, DisabledLinkColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleCenter, Enabled = LicenseManager.Checkout is not null };
        buy.LinkClicked += (_, _) => Run(_ =>
        {
            using var process = Process.Start(new ProcessStartInfo(LicenseManager.Checkout!.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        Add(Columns(activate, buy), 31);
        Add(Button(ExplorerRestart.ActionText, () => Run(_ => ExplorerRestart.Run())), 31);
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

    private Task Refresh(CancellationToken token) => Refresh(token, renewManifest: true);

    private async Task Refresh(CancellationToken token, bool renewManifest)
    {
        loaded = true;
        string? downloadError = null;
        if (!catalog.LocalMode && renewManifest && LicenseManager.Read() is { } license)
        {
            try { activationManifest = await LicenseManager.RequestPackageManifest(license, token) ?? activationManifest; }
            catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or IOException
                or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
            {
                downloadError = Texts.Get("Pro downloads are temporarily unavailable.", "Pobieranie Pro jest chwilowo niedostępne.");
            }
        }
        try { apps = await catalog.Load(activationManifest, token); }
        catch (Exception error) when (!token.IsCancellationRequested && (catalog.LocalMode || activationManifest is not null)
            && error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            activationManifest = null;
            apps = await catalog.Load(null, token, includePrivate: false);
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
            or KeyNotFoundException or OperationCanceledException or TimeoutException or System.Security.SecurityException)
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
