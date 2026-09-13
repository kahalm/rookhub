namespace RookHub.Api.Services;

/// <summary>
/// Faktor auf alle Deckel des Rate-Limiters (<c>RateLimiting:PermitScale</c>), Vorgabe 1 = unveraendert.
///
/// Gedacht fuer den E2E-Stack (<c>compose.e2e.yml</c>): dort faehrt die GANZE Suite von einer
/// Adresse, und der globale Deckel von 100 Anfragen je Minute schlug mitten im Lauf zu — am
/// 2026-09-13 lokal gemessen 25 Absagen in einem Lauf von zwei Minuten, verteilt auf Menue,
/// Anmeldung und Puzzle-Endpunkte. Welcher Test dabei scheitert, ist Zufall.
///
/// Der Wert wird auf <see cref="Min"/>..<see cref="Max"/> geklemmt, und etwas Unlesbares gilt als 1:
/// der Schalter kann die Limiter weder abschalten noch verschaerfen, und ein Tippfehler in einer
/// Produktivumgebung laesst alles beim Alten.
/// </summary>
public static class RateLimitScale
{
    public const string ConfigKey = "RateLimiting:PermitScale";
    public const int Min = 1;
    public const int Max = 100;

    public static int FromConfig(IConfiguration config) =>
        int.TryParse(config[ConfigKey], out var scale) ? Math.Clamp(scale, Min, Max) : Min;
}
