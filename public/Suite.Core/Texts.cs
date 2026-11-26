using System.Globalization;

namespace Suite.Core;

public static class Texts
{
    public static bool Polish { get; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl";
    public static string Get(string english, string polish) => Polish ? polish : english;
}
