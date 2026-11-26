using System.Globalization;

namespace Dimmli;

internal static class Texts
{
    private static readonly bool Polish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl";
    public static string Get(string english, string polish) => Polish ? polish : english;
}
