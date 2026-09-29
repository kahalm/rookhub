using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RookHub.Api.Services;

namespace RookHub.Api.Authorization;

/// <summary>Ergebnis der Pfad-Signaturprüfung eines Bot-Aufrufs (<see cref="BotRequestSignature.CheckPath"/>).</summary>
public enum BotSignatureCheck
{
    /// <summary>Kein <c>X-Bot-Signature</c>-Header — gewöhnlicher (anonymer oder eingeloggter) Aufruf.</summary>
    Absent,
    /// <summary>Header gesetzt, Signatur + Timestamp gültig — Aufruf stammt vom Schach-Bot.</summary>
    Valid,
    /// <summary>Header gesetzt, aber falsch/abgelaufen oder Secret serverseitig leer → 401.</summary>
    Invalid,
}

/// <summary>Ob eine Antwort die Discord-Verknüpfung der Spieler enthalten darf (<see cref="BotRequestSignature.ResolveDiscordAccess"/>).</summary>
public enum DiscordFieldAccess
{
    /// <summary>Anonymer Aufruf: DiscordId/DiscordUsername werden entfernt.</summary>
    Redact,
    /// <summary>Signierter Bot oder eingeloggter Nutzer: Antwort unverändert.</summary>
    Include,
    /// <summary>Bot-Signatur mitgeschickt, aber ungültig → 401 (der Bot holt dann unsigniert nach).</summary>
    InvalidSignature,
}

/// <summary>
/// Eingehende Signatur des Schach-Bots (<c>SchachBot:StatsSecret</c> == Bot-<c>ROOKHUB_STATS_SECRET</c>):
/// <c>X-Bot-Timestamp</c> = Unix-Sekunden, <c>X-Bot-Signature: sha256=&lt;hex(HMAC_SHA256(secret,
/// "&lt;ts&gt;.&lt;objekt&gt;"))&gt;</c>, Timestamp PFLICHT und ±<see cref="TimestampToleranceSeconds"/>.
/// Eine Implementierung für den Stats-Pull (<c>/api/bot/player-progress</c>, Objekt = Discord-ID) und die
/// Ergebnis-GETs (Objekt = Anfragepfad ohne Query, siehe <see cref="CheckPath"/>).
/// </summary>
public static class BotRequestSignature
{
    public const string SecretConfigKey = "SchachBot:StatsSecret";
    public const string SignatureHeader = "X-Bot-Signature";
    public const string TimestampHeader = "X-Bot-Timestamp";

    /// <summary>±300 s Toleranz für den Replay-Schutz (analog zum Solver-Webhook).</summary>
    public const int TimestampToleranceSeconds = 300;

    /// <summary>
    /// Prüft <paramref name="provided"/> (<c>sha256=&lt;hex&gt;</c> oder nacktes Hex) konstant-zeitig gegen
    /// die HMAC über <c>"&lt;ts&gt;.&lt;signedObject&gt;"</c>. Ohne gültigen, frischen Timestamp → false
    /// (eine Signatur ohne Timestamp wäre für immer replaybar).
    /// </summary>
    public static bool Verify(string secret, string signedObject, string? provided, string? timestamp)
    {
        if (string.IsNullOrEmpty(provided))
            return false;
        if (string.IsNullOrWhiteSpace(timestamp))
            return false;
        var sig = provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? provided["sha256=".Length..]
            : provided;

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
            return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > TimestampToleranceSeconds)
            return false;
        var signedMessage = ts.ToString(CultureInfo.InvariantCulture) + "." + signedObject;

        var expected = SchachBotWebhookService.ComputeHmacHex(secret, signedMessage);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// Bot-Signatur über den Anfragepfad (<c>Request.Path</c>, ohne Query — z. B.
    /// <c>/api/book-puzzles/123/results</c>). Die Bindung an den Pfad verhindert, dass eine abgefangene
    /// Signatur für andere Puzzle-IDs/Endpunkte taugt. Ohne Request (Controller direkt instanziiert) → Absent.
    /// </summary>
    public static BotSignatureCheck CheckPath(HttpRequest? request, string? secret)
    {
        var provided = request?.Headers[SignatureHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(provided))
            return BotSignatureCheck.Absent;
        if (string.IsNullOrEmpty(secret))
            return BotSignatureCheck.Invalid;
        var timestamp = request!.Headers[TimestampHeader].FirstOrDefault();
        return Verify(secret, request.Path.Value ?? string.Empty, provided, timestamp)
            ? BotSignatureCheck.Valid
            : BotSignatureCheck.Invalid;
    }

    /// <summary>
    /// Wer bekommt in den anonym erreichbaren Ergebnis-Endpunkten (Tagespuzzle-Löser, Tages-Ladder,
    /// Hall of Fame, Wochenpost-Ergebnisse) die Discord-Verknüpfung (DiscordId/-Username) der Spieler?
    /// Der per Pfad-Signatur ausgewiesene Bot (setzt daraus die Erwähnungen) und eingeloggte Nutzer
    /// (Wochenpost-Bestenliste der App) — anonyme Aufrufer NICHT: sonst wäre die Zuordnung
    /// RookHub-Konto ↔ Discord-Konto für jeden Unangemeldeten abrufbar (vgl. <c>PublicProfileDto</c>).
    /// </summary>
    public static DiscordFieldAccess ResolveDiscordAccess(HttpContext? context, string? secret, ILogger? logger = null)
    {
        switch (CheckPath(context?.Request, secret))
        {
            case BotSignatureCheck.Valid:
                return DiscordFieldAccess.Include;
            case BotSignatureCheck.Invalid:
                logger?.LogWarning("Bot-Signatur ungültig für {Path} — 401", context!.Request.Path.Value);
                return DiscordFieldAccess.InvalidSignature;
            default:
                return context?.User?.Identity?.IsAuthenticated == true
                    ? DiscordFieldAccess.Include
                    : DiscordFieldAccess.Redact;
        }
    }
}
