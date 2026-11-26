using System.Text.Json;

namespace Dimmli;

internal enum Profile { Day, Evening }

internal sealed class ProfileStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Dictionary<Profile, Dictionary<string, int>> Load()
    {
        try
        {
            var profiles = JsonSerializer.Deserialize<Dictionary<Profile, Dictionary<string, int>>>(
                File.ReadAllText(path), JsonOptions);
            if (profiles is null || profiles.Values.Any(levels =>
                levels is null || levels.Values.Any(value => value is < 0 or > 100)))
                throw new JsonException("Invalid brightness profile.");
            return profiles;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    public Dictionary<Profile, Dictionary<string, int>> Save(Profile profile, Dictionary<string, int> connected)
    {
        var profiles = Load();
        if (!profiles.TryGetValue(profile, out var levels))
            profiles[profile] = levels = [];
        foreach (var (id, level) in connected)
            levels[id] = level;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Path.GetRandomFileName();
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profiles, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
        return profiles;
    }
}
