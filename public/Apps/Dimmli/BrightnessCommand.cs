using System.Globalization;

namespace Dimmli;

internal sealed record BrightnessCommand(Profile? Profile = null, int? Level = null, int? Monitor = null, bool List = false, int? GameOwner = null, bool ReleaseGame = false)
{
    public const string Usage = """
        Dimmli.exe --list
        Dimmli.exe --profile <Day|Evening|Dzień|Wieczór>
        Dimmli.exe [--monitor <number>] --brightness <0-100>
        """;

    public bool IsValid() => GameOwner is not null ? GameOwner > 0 && Profile is null && Level is null && Monitor is null && !List
        : !ReleaseGame && (List ? Profile is null && Level is null && Monitor is null
        : Profile is { } profile ? Enum.IsDefined(profile) && Level is null && Monitor is null
        : Level is >= 0 and <= 100 && (Monitor is null or > 0));

    public static BrightnessCommand? Parse(string[] args)
    {
        if (args is ["--game-brightness" or "--game-restore", var ownerText]
            && int.TryParse(ownerText, NumberStyles.None, CultureInfo.InvariantCulture, out int owner) && owner > 0)
            return new(GameOwner: owner, ReleaseGame: args[0] == "--game-restore");
        if (args is ["--list"])
            return new(List: true);
        if (args.Length == 0 || args.Length % 2 != 0)
            return null;

        Profile? profile = null;
        int? level = null, monitor = null;
        for (int index = 0; index < args.Length; index += 2)
        {
            string value = args[index + 1];
            switch (args[index])
            {
                case "--profile" when profile is null:
                    profile = value.ToUpperInvariant() switch
                    {
                        "DAY" or "DZIEŃ" => Dimmli.Profile.Day,
                        "EVENING" or "WIECZÓR" => Dimmli.Profile.Evening,
                        _ => null
                    };
                    if (profile is null)
                        return null;
                    break;
                case "--brightness" when level is null:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int brightness))
                        return null;
                    level = brightness;
                    break;
                case "--monitor" when monitor is null:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                        return null;
                    monitor = number;
                    break;
                default:
                    return null;
            }
        }

        var command = new BrightnessCommand(profile, level, monitor);
        return command.IsValid() ? command : null;
    }
}
