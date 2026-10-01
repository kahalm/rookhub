namespace RookHub.Api.Services;

/// <summary>
/// Oeffentliche Teilen-Links (<c>/g/</c> Partie, <c>/w/</c> Aufgabenblatt, <c>/l/</c> Linie, <c>/r/</c> Rekonstruktion,
/// Liga-Begegnung): Zufallstoken, URL-sicher, im Klartext gespeichert (der Link IST der Zugang). Vorher stand die
/// Erzeugung samt Eindeutigkeits-Schleife viermal wortgleich in den Diensten (Codereview 2026-09-29, A2-015) — eine
/// Aenderung (Laenge, Praefix fuer Secret-Scanner) haette an vier Stellen nachgezogen werden muessen.
/// </summary>
public static class ShareTokens
{
    /// <summary>16 Byte = 128 Bit → 22 Zeichen Base64URL.</summary>
    public const int DefaultBytes = 16;

    /// <summary>So oft wird bei einer Kollision neu gewuerfelt, bevor der Fallback greift.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Neues Token aus <paramref name="bytes"/> Zufallsbytes (Base64URL ohne Padding).</summary>
    public static string New(int bytes = DefaultBytes) => SecretTokens.NewRaw(bytes);

    /// <summary>Neues Token, das laut <paramref name="exists"/> noch nicht vergeben ist. Nach
    /// <see cref="MaxAttempts"/> Kollisionen (bei 128 Bit praktisch ausgeschlossen) kommt ungeprueft ein weiteres —
    /// der Unique-Index der Tabelle faengt den Rest.</summary>
    public static async Task<string> NewUniqueAsync(Func<string, Task<bool>> exists)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var token = New();
            if (!await exists(token)) return token;
        }
        return New();   // extrem unwahrscheinlicher Kollisions-Fallback
    }
}
