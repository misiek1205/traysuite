using System.Diagnostics;
using System.Drawing.Text;
using System.IO.Compression;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace QuickView;

internal sealed class PreviewWindow : ToolWindow
{
    private readonly Panel viewer = new();
    private readonly Label title = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button nextPage;
    private readonly Button previousPage;
    private string[] files = [];
    private int index;
    private uint page;
    private PdfDocument? pdf;
    private CoreWebView2Environment? webEnvironment;

    [STAThread]
    private static int Main(string[] args) => Run("QuickView", args, paths => new PreviewWindow(paths));

    private PreviewWindow(string[] paths) : base("QuickView", 875, 625)
    {
        KeyPreview = true;
        nextPage = ActionButton("Next page", () => ChangePage(1));
        previousPage = ActionButton("Previous page", () => ChangePage(-1));
        AddRow(Actions(ActionButton("Choose or drop files", () => PickFiles(Select)),
            ActionButton("Previous file", () => MoveFile(-1)), ActionButton("Next file", () => MoveFile(1)),
            ActionButton("Open original", OpenOriginal)));
        AddRow(title, 27);
        AddRow(viewer, 0);
        AddRow(Actions(previousPage, nextPage));
        SetPageButtons(false);
        FileDrop(this, Select);
        FileDrop(viewer, Select);
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Escape: Close(); break;
                case Keys.Left when e.Alt: MoveFile(-1); break;
                case Keys.Right when e.Alt: MoveFile(1); break;
            }
        };
        Shown += (_, _) => Select(paths);
    }

    private void Select(string[] paths)
    {
        var matches = paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (matches.Length == 0)
        {
            if (files.Length == 0) Status("Choose a file to preview.");
            return;
        }
        files = matches;
        index = 0;
        LoadFile();
    }

    private void MoveFile(int delta)
    {
        if (files.Length < 2) return;
        index = (index + delta + files.Length) % files.Length;
        LoadFile();
    }

    private void OpenOriginal() => RunWork(_ =>
    {
        if (files.Length > 0)
            using (Process.Start(new ProcessStartInfo(files[index]) { UseShellExecute = true })) { }
        return Task.CompletedTask;
    });

    private void LoadFile() => RunWork(async token =>
    {
        ClearViewer();
        pdf = null;
        SetPageButtons(false);
        string file = files[index];
        title.Text = $"{index + 1} / {files.Length}    {Path.GetFileName(file)}";
        Status("Loading preview…");
        string extension = Path.GetExtension(file).ToLowerInvariant();
        switch (extension)
        {
            case ".pdf":
                var storage = await StorageFile.GetFileFromPathAsync(file).AsTask(token);
                pdf = await PdfDocument.LoadFromFileAsync(storage).AsTask(token);
                page = 0;
                await ShowPage(token);
                return;
            case ".png": case ".jpg": case ".jpeg": case ".bmp": case ".gif": case ".tif": case ".tiff": case ".ico":
                ShowImage(await Task.Run(() => LoadBitmap(file), token));
                break;
            case ".ttf": case ".otf":
                ShowImage(await Task.Run(() => FontSpecimen(file), token));
                break;
            case ".zip":
                ShowText(await Task.Run(() => ListArchive(file, token), token));
                break;
            case ".mp4": case ".webm": case ".mov": case ".m4v": case ".ogg": case ".mp3": case ".wav": case ".webp":
                await ShowMedia(file, token);
                break;
            case ".txt": case ".md": case ".json": case ".xml": case ".csv": case ".log": case ".ini":
            case ".yaml": case ".yml": case ".toml": case ".cs": case ".ps1": case ".js": case ".ts":
            case ".py": case ".css": case ".html": case ".htm": case ".sql": case ".svg": case ".rs": case ".go":
                ShowText(await ReadText(file, token));
                break;
            default:
                Status($"Preview unavailable · {SizeText(new FileInfo(file).Length)}");
                return;
        }
        Status($"{SizeText(new FileInfo(file).Length)} · Alt+Left / Alt+Right to switch files");
    });

    private void ChangePage(int delta)
    {
        if (pdf is null || (long)page + delta < 0 || (long)page + delta >= pdf.PageCount) return;
        RunWork(async token =>
        {
            uint requested = (uint)(page + delta);
            await ShowPage(token, requested);
        });
    }

    private async Task ShowPage(CancellationToken token, uint? requested = null)
    {
        if (pdf is null || pdf.PageCount == 0)
            return;
        uint current = requested ?? page;
        using var sourcePage = pdf.GetPage(current);
        using var output = new InMemoryRandomAccessStream();
        double scale = Math.Min(1600 / sourcePage.Size.Width, 2000 / sourcePage.Size.Height);
        var options = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, sourcePage.Size.Width * scale) };
        await sourcePage.RenderToStreamAsync(output, options).AsTask(token);
        output.Seek(0);
        using var image = Image.FromStream(output.AsStreamForRead());
        ShowImage(new Bitmap(image));
        page = current;
        SetPageButtons(true);
        Status($"Page {page + 1} of {pdf.PageCount}.");
    }

    private void SetPageButtons(bool show)
    {
        previousPage.Visible = nextPage.Visible = show;
        previousPage.Enabled = page > 0;
        nextPage.Enabled = pdf is not null && page + 1 < pdf.PageCount;
    }

    private async Task ShowMedia(string path, CancellationToken token)
    {
        webEnvironment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(LicenseManager.DataDirectory, "QuickView", "WebView"));
        token.ThrowIfCancellationRequested();
        var media = new WebView2 { Dock = DockStyle.Fill };
        viewer.Controls.Add(media);
        await media.EnsureCoreWebView2Async(webEnvironment);
        token.ThrowIfCancellationRequested();
        var core = media.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        string address = "https://preview.traysuite.invalid/" + Uri.EscapeDataString(Path.GetFileName(path));
        core.SetVirtualHostNameToFolderMapping("preview.traysuite.invalid", Path.GetDirectoryName(path)!, CoreWebView2HostResourceAccessKind.Deny);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!string.Equals(e.Request.Uri, address, StringComparison.Ordinal))
                e.Response = webEnvironment.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
        core.NavigationStarting += (_, e) => e.Cancel = !string.Equals(e.Uri, address, StringComparison.Ordinal);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => { e.Cancel = true; Status("This format cannot be played by the installed runtime."); };
        core.Navigate(address);
    }

    private static Bitmap LoadBitmap(string file)
    {
        using var stream = File.OpenRead(file);
        if (stream.Length > 128 * 1024 * 1024) throw new IOException("Image file exceeds the preview size limit.");
        using var image = Image.FromStream(stream);
        if ((long)image.Width * image.Height > 60_000_000) throw new IOException("Image exceeds the preview pixel limit.");
        return new Bitmap(image);
    }

    private static Bitmap FontSpecimen(string file)
    {
        if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new IOException("Font exceeds the preview size limit.");
        using var fonts = new PrivateFontCollection();
        fonts.AddFontFile(file);
        using var family = fonts.Families.First();
        var style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
            : family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Italic;
        using var typeface = new Font(family, 36, style);
        var bitmap = new Bitmap(970, 370);
        using var graphics = Graphics.FromImage(bitmap);
        using var heading = new Font(SystemFonts.MessageBoxFont!.FontFamily, 22);
        graphics.Clear(Color.White);
        graphics.DrawString(family.Name, heading, Brushes.Black, 24, 16);
        graphics.DrawString("The quick brown fox jumps\nAa Bb Cc Dd Ee Ff Gg Hh\n0123456789 .,?!@#%\nĄą Ęę Łł Óó Śś Żż Źź", typeface, Brushes.Black, 24, 76);
        return bitmap;
    }

    private static string ListArchive(string file, CancellationToken token)
    {
        if (new FileInfo(file).Length > 512L * 1024 * 1024) throw new IOException("ZIP exceeds the preview size limit.");
        using var archive = ZipFile.OpenRead(file);
        var lines = new List<string>();
        foreach (var item in archive.Entries.Take(2000))
        {
            token.ThrowIfCancellationRequested();
            lines.Add($"{SizeText(item.Length),12}    {item.FullName.Replace('\r', ' ').Replace('\n', ' ')}");
        }
        if (archive.Entries.Count > lines.Count)
            lines.Add($"… {archive.Entries.Count - lines.Count:N0} more entries.");
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> ReadText(string file, CancellationToken token)
    {
        using var reader = File.OpenText(file);
        var buffer = new char[240_000];
        int count = await reader.ReadBlockAsync(buffer, token);
        return new string(buffer, 0, count) + (count == buffer.Length ? "\r\n… Preview truncated." : "");
    }

    private void ShowText(string text)
    {
        var display = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = text,
            WordWrap = false, ScrollBars = ScrollBars.Both, Font = PopupAppearance.KeyFont };
        viewer.Controls.Add(display);
        FileDrop(display, Select);
    }

    private void ShowImage(Bitmap image)
    {
        ClearViewer();
        var display = new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom };
        display.Disposed += (_, _) => image.Dispose();
        viewer.Controls.Add(display);
        FileDrop(display, Select);
    }

    private void ClearViewer()
    {
        while (viewer.Controls.Count > 0)
            viewer.Controls[0].Dispose();
    }
}
