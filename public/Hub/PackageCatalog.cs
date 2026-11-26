using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraySuite.Hub;

internal sealed record PackageInfo(string Version, long SizeBytes, string? Sha256, string? DownloadUrl)
{
    [JsonIgnore] public bool HasHash => Sha256 is { Length: 64 } hash && hash.All(Uri.IsHexDigit);
}

internal sealed record CatalogApp(string Id, string Tier, string DescriptionEn, string DescriptionPl,
    Dictionary<string, PackageInfo>? Packages = null)
{
    [JsonIgnore] public bool Pro => Tier == "pro";
    [JsonIgnore] public string Description => Texts.Get(DescriptionEn, DescriptionPl);
    [JsonIgnore] public PackageInfo? Package => Packages?.GetValueOrDefault(PackageCatalog.Runtime);
}

internal sealed record Catalog(int SchemaVersion, CatalogApp[] Apps);

internal sealed class PackageCatalog
{
    public static readonly string Runtime = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly Uri PublicManifest = new("https://github.com/misiek1205/traysuite/releases/latest/download/manifest.json");
    private static readonly Catalog BuiltIn = ReadBuiltIn();
    private readonly string? local;
    private readonly HttpClient http;

    public PackageCatalog(HttpClient http, string? local)
    {
        this.http = http;
        this.local = string.IsNullOrWhiteSpace(local) ? null : Path.GetFullPath(local);
    }

    public bool LocalMode => local is not null;

    public IReadOnlyList<CatalogApp> Fallback => BuiltIn.Apps;

    public async Task<IReadOnlyList<CatalogApp>> Load(Uri? proManifest, CancellationToken token, bool includePrivate = true)
    {
        bool loadPro = includePrivate && LicenseManager.IsPro;
        Catalog publicCatalog;
        Catalog? privateCatalog = null;
        Uri origin;
        Uri? privateOrigin = null;
        if (local is not null)
        {
            string path = Path.Combine(local, "manifest.json");
            origin = new Uri(path);
            publicCatalog = File.Exists(path) ? await ReadFile(path, token) : BuiltIn;
            var privatePath = Path.Combine(local, "manifest-pro.json");
            if (loadPro && File.Exists(privatePath))
            {
                privateCatalog = await ReadFile(privatePath, token);
                privateOrigin = new Uri(privatePath);
            }
        }
        else
        {
            (publicCatalog, origin) = await ReadRemote(PublicManifest, token);
            if (loadPro && proManifest is not null)
                (privateCatalog, privateOrigin) = await ReadRemote(proManifest, token);
        }

        return BuiltIn.Apps.Select(item =>
        {
            var published = publicCatalog.Apps.SingleOrDefault(app => app.Id == item.Id && app.Tier == item.Tier);
            var package = published?.Package;
            var packageOrigin = origin;
            if (item.Pro)
            {
                var authorized = privateCatalog?.Apps.SingleOrDefault(app => app.Id == item.Id && app.Pro)?.Package;
                package = authorized ?? (package is null ? null : package with { DownloadUrl = null });
                if (authorized is not null)
                    packageOrigin = privateOrigin!;
            }

            if (package?.DownloadUrl is { Length: > 0 } download)
                package = package with { DownloadUrl = Resolve(packageOrigin, download).AbsoluteUri };
            else if (local is not null && (!item.Pro || loadPro))
                package = DiscoverLocal(item, package);
            return item with { Packages = package is null ? null : new() { [Runtime] = package } };
        }).ToArray();
    }

    private PackageInfo? DiscoverLocal(CatalogApp app, PackageInfo? advertised)
    {
        string root = local!;
        string zip = Path.Combine(root, $"{app.Id}-{Runtime}.zip");
        if (File.Exists(zip))
        {
            string sidecar = zip + ".sha256";
            string? hash = advertised?.Sha256;
            if (hash is null && File.Exists(sidecar))
                hash = File.ReadLines(sidecar).FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return new(advertised?.Version ?? "0.0", new FileInfo(zip).Length, hash, new Uri(zip).AbsoluteUri);
        }

        string[] paths = [Path.Combine(root, app.Id, Runtime), Path.Combine(root, $"{app.Id}-{Runtime}"),
            Path.Combine(root, app.Id), root];
        string? directory = paths.FirstOrDefault(path => File.Exists(Path.Combine(path, app.Id + ".exe")));
        if (directory is null)
            return advertised;
        var executable = new FileInfo(Path.Combine(directory, app.Id + ".exe"));
        return new(advertised?.Version ?? "0.0", executable.Length, null, new Uri(executable.FullName).AbsoluteUri);
    }

    public async Task CopyPackage(Uri source, string destination, CancellationToken token)
    {
        if (source.IsFile && local is not null)
        {
            await using var file = File.OpenRead(source.LocalPath);
            await CopyLimited(file, destination, token);
            return;
        }

        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Package must use HTTPS.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        await CopyLimited(stream, destination, token);
    }

    private static async Task CopyLimited(Stream input, string destination, CancellationToken token)
    {
        await using var file = File.Create(destination);
        byte[] buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (file.Position + count > 100L * 1024 * 1024)
                throw new InvalidDataException("Package is too large.");
            await file.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    private static Catalog ReadBuiltIn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TraySuite.Catalog")!;
        return JsonSerializer.Deserialize<Catalog>(stream, Json)!;
    }

    private static async Task<Catalog> ReadFile(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return await Read(stream, token);
    }

    private async Task<(Catalog, Uri)> ReadRemote(Uri address, CancellationToken token)
    {
        using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var origin = response.RequestMessage?.RequestUri ?? address;
        if (origin.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Manifest must use HTTPS.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return (await Read(stream, token), address == PublicManifest ? address : origin);
    }

    private static async Task<Catalog> Read(Stream stream, CancellationToken token)
    {
        using var limited = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (limited.Length + count > 256 * 1024)
                throw new InvalidDataException("Manifest is too large.");
            limited.Write(buffer, 0, count);
        }
        var catalog = JsonSerializer.Deserialize<Catalog>(limited.ToArray(), Json);
        if (catalog?.SchemaVersion != 1 || catalog.Apps is null || catalog.Apps.Length > 32
            || catalog.Apps.Any(app => app is null || string.IsNullOrWhiteSpace(app.Id))
            || catalog.Apps.Select(app => app.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Apps.Length)
            throw new InvalidDataException("Invalid package manifest.");
        return catalog;
    }

    private Uri Resolve(Uri origin, string value)
    {
        if (!Uri.TryCreate(origin, value, out var result)
            || result.Scheme != Uri.UriSchemeHttps && !(result.IsFile && local is not null
                && Path.GetFullPath(result.LocalPath).StartsWith(Path.TrimEndingDirectorySeparator(local)
                    + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid package address.");
        return result;
    }
}
