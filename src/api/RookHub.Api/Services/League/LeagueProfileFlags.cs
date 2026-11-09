namespace RookHub.Api.Services.League;

/// <summary>
/// Profil-Flagge → Land (G1-003). Nicht jede Flagge ist ein Land: Lichess kennt „_earth“, „_pirate“, „_rainbow“,
/// „_united-nations“ und „EU“, chess.com liefert für „International“ den Code „XX“. Das ist „keine Angabe“ — weder ein
/// Hinweis noch ein Ausschluss. Vorher wurde daraus „_E“/„EU“/„XX“, das nie erlaubt war, und das Konto fiel als
/// „anderes Land“ heraus, auch mit vollem Klarnamen. Regionalflaggen zählen für ihr Land (Lichess „GB-ENG“ ergibt über
/// die ersten zwei Zeichen ohnehin GB; chess.com XE/XS/XW = England/Schottland/Wales, XB/XC/XA = Baskenland/Katalonien/Kanaren).
/// </summary>
public static class LeagueProfileFlags
{
    /// <summary>ISO-Land der Flagge (zwei Großbuchstaben) oder <c>null</c> = keine Landesangabe.</summary>
    public static string? Country(string? flag)
    {
        var f = (flag ?? "").Trim();
        if (f.Length < 2 || f[0] == '_') return null;
        var code = f[..2].ToUpperInvariant();
        return code switch
        {
            "EU" or "UN" or "XX" => null,
            "XE" or "XS" or "XW" => "GB",
            "XB" or "XC" or "XA" => "ES",
            "XK" => code,                       // Kosovo (vergebener Nutzer-Code, auf beiden Seiten ein Land)
            _ when code[0] == 'X' => null,      // weitere chess.com-Sondercodes ohne Land
            _ => code,
        };
    }
}
