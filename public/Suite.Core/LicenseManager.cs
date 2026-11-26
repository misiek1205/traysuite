using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Suite.Core;

public sealed record LicenseState(string Key, string InstanceId, DateTimeOffset? ExpiresAt, bool Development);
public sealed record LicenseActivation(LicenseState State, Uri? PackageManifest);

public static class LicenseManager
{
    private const string LicenseApi = "https://api.lemonsqueezy.com/v1/licenses/";
    private static readonly byte[] Entropy = "TraySuite License V1"u8.ToArray();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<string, string?> Configuration = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(item => item.Key, item => item.Value);

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraySuite");
    private static string StatePath => Path.Combine(DataDirectory, "license.dat");
    public static Uri? Checkout => ReadHttps(Configuration.GetValueOrDefault("TraySuiteCheckout"));
    public static bool IsPro => Read() is not null;

    public static LicenseState? Read()
    {
        try
        {
            using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is <= 0 or > 64 * 1024)
                return null;
            byte[] encrypted = new byte[(int)stream.Length];
            stream.ReadExactly(encrypted);
            var state = JsonSerializer.Deserialize<LicenseState>(
                ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser));
            if (state is null || state.ExpiresAt <= DateTimeOffset.UtcNow)
                return null;
#if !DEBUG
            if (state.Development)
                return null;
#endif
            return state;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    public static async Task<LicenseActivation> Activate(string key, CancellationToken cancellation = default)
    {
        key = key.Trim();
        if (key.Length is 0 or > 200)
            throw new InvalidOperationException(Texts.Get("Enter a license key.", "Wpisz klucz licencyjny."));
        if (Read() is { } previous && previous.Key == key)
            return new(previous, null);
#if DEBUG
        if (key == "DEV-PRO-TEST")
        {
            var test = new LicenseState(key, "debug", null, true);
            Save(test);
            return new(test, null);
        }
#endif
        if (!long.TryParse(Configuration.GetValueOrDefault("TraySuiteStore"), out long store) || store <= 0
            || !long.TryParse(Configuration.GetValueOrDefault("TraySuiteProduct"), out long product) || product <= 0)
            throw new InvalidOperationException(Texts.Get("This build needs a Pro store and product ID.",
                "Ta kompilacja wymaga identyfikatorów sklepu i produktu Pro."));

        using var request = new HttpRequestMessage(HttpMethod.Post, LicenseApi + "activate")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"] = key, ["instance_name"] = Environment.MachineName + " / TraySuite"
            })
        };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Client.SendAsync(request, cancellation);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation));
        var root = body.RootElement;
        if (!response.IsSuccessStatusCode || !root.TryGetProperty("activated", out var activated)
            || activated.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException(Texts.Get("Activation failed. Check the key or its activation limit.",
                "Aktywacja nie powiodła się. Sprawdź klucz lub limit aktywacji."));

        var meta = root.GetProperty("meta");
        var license = root.GetProperty("license_key");
        string instance = root.GetProperty("instance").GetProperty("id").GetString() ?? "";
        if (meta.GetProperty("store_id").GetInt64() != store || meta.GetProperty("product_id").GetInt64() != product
            || instance.Length == 0 || license.GetProperty("status").GetString() != "active")
            throw new InvalidOperationException(Texts.Get("This key is not for TraySuite Pro.", "Klucz nie należy do TraySuite Pro."));
        DateTimeOffset? expiry = null;
        if (license.TryGetProperty("expires_at", out var expires) && expires.ValueKind != JsonValueKind.Null)
        {
            if (!DateTimeOffset.TryParse(expires.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) || parsed <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException(Texts.Get("The license has expired.", "Licencja wygasła."));
            expiry = parsed;
        }

        Uri? manifest = root.TryGetProperty("download_manifest_url", out var url) ? ReadHttps(url.GetString()) : null;
        var state = new LicenseState(key, instance, expiry, false);
        Save(state);
        return new(state, manifest);
    }

    public static async Task<Uri?> RequestPackageManifest(LicenseState state, CancellationToken cancellation)
    {
        if (state.Development || ReadHttps(Configuration.GetValueOrDefault("TraySuiteDownloads")) is not { } endpoint)
            return null;
        using var response = await Client.PostAsJsonAsync(endpoint, new
        {
            license_key = state.Key, instance_id = state.InstanceId
        }, cancellation);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation));
        return ReadHttps(body.RootElement.GetProperty("download_manifest_url").GetString());
    }

    private static Uri? ReadHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    private static void Save(LicenseState state)
    {
        byte[] encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(state), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(DataDirectory);
        string temporary = Path.Combine(DataDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, StatePath, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
