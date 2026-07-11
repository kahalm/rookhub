using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Circuit-Breaker für den pro-User gespeicherten Chessable-Bearer.
///
/// Hintergrund (Vorfall 2026-06-30): Chessable wies einen Bearer mit
/// <c>{"error":"User is banned or deleted"}</c> ab; der Import-Watchdog draint dann die wartende
/// Queue immer wieder und feuert dabei laufend FEHLSCHLAGENDE Chessable-Requests mit demselben,
/// längst toten Bearer → Warnungs-Spike + sinnloser Traffic (und das Risiko, dass eine ohnehin
/// gesperrte/gelöschte Identität weiter angeklopft wird).
///
/// Lösung: Sobald eine Antwort als <see cref="IsBearerFatal"/> klassifiziert wird (Account
/// gesperrt/gelöscht oder Token abgelaufen/ungültig), wird der Breaker für genau diesen Bearer
/// „geöffnet" (<see cref="Models.ChessableCredential.BlockedAt"/> gesetzt). Bei offenem Breaker
/// macht RookHub mit diesem Bearer KEINE weitere Chessable-Anfrage mehr — Importe pausieren statt zu
/// scheitern, Lese-Endpoints verweigern frische Abrufe. Erst ein erfolgreicher „Testen“-Klick
/// (<see cref="ClearAndResumeAsync"/>) schließt den Breaker wieder und nimmt die pausierten Importe
/// auf.
///
/// Bewusst NICHT ausgelöst wird der Breaker bei einem reinen IP-/Cloudflare-Block: dort ist die
/// VPN-Ausgangs-IP gesperrt, nicht der Bearer — ein anderer Tunnel löst das, der Bearer bleibt gut.
/// </summary>
public class ChessableBearerBreaker
{
    private readonly AppDbContext _db;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<ChessableBearerBreaker> _logger;

    public ChessableBearerBreaker(
        AppDbContext db,
        IBackgroundTaskQueue taskQueue,
        ILogger<ChessableBearerBreaker> logger)
    {
        _db = db;
        _taskQueue = taskQueue;
        _logger = logger;
    }

    /// <summary>
    /// True, wenn die Fehlermeldung bedeutet, dass der BEARER SELBST tot ist (Account gesperrt/gelöscht
    /// oder Token abgelaufen/ungültig) — ein erneuter Versuch mit demselben Bearer ist also zwecklos.
    /// Explizit FALSE bei einem IP-/Cloudflare-/VPN-Block (dort ist die Ausgangs-IP das Problem, nicht
    /// der Bearer) und bei der mehrdeutigen „kein gültiges JSON … bzw. VPN-IP prüfen"-Meldung — in
    /// beiden Fällen darf der Bearer weiter verwendet werden (anderer Tunnel). Rein/testbar.
    /// </summary>
    public static bool IsBearerFatal(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var m = message;

        // IP-/VPN-Block oder mehrdeutig (Meldung erwähnt die VPN-IP) → NICHT der Bearer. Konservativ:
        // im Zweifel den Breaker NICHT öffnen, damit ein bloßer IP-Block keine Importe lahmlegt.
        if (m.Contains("VPN", StringComparison.OrdinalIgnoreCase)
            || m.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase)
            || m.Contains("IP rotieren", StringComparison.OrdinalIgnoreCase))
            return false;

        // HINWEIS (Altitude/[[chessable-bearer-circuit-breaker]]): diese Klassifikation matcht
        // FREITEXT-Fehlermeldungen des piratechess-Proxys. Der robuste Fix wäre ein STRUKTURIERTER
        // Fehlercode aus dem Proxy (z. B. BEARER_DEAD/IP_BLOCKED) statt Substring-Sniffing — solange
        // der nicht existiert, decken wir hier bewusst BEIDE Sprachen ab (piratechess wickelt Meldungen
        // meist auf Deutsch ein, reicht aber rohe Chessable-Meldungen wie „Expired token" durch).
        var fatal = new[]
        {
            // Account gesperrt/gelöscht (z. B. Chessable: „User is banned or deleted").
            "banned", "deleted", "gesperrt", "gelöscht",
            // Token endgültig unbrauchbar (abgelaufen/ungültig → „bitte den Bearer neu hinterlegen").
            "abgelaufen", "ungültig", "neu hinterlegen",
            "expired", "invalid token", "re-enter", "reauth", "unauthorized",
        };
        return fatal.Any(k => m.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Öffnet den Breaker für den Bearer des angegebenen Users (idempotent: ein bereits
    /// offener Breaker bleibt mit seiner ursprünglichen Ursache stehen). Liefert <c>true</c>, wenn
    /// dieser Aufruf den Breaker NEU geöffnet hat.</summary>
    public async Task<bool> TripAsync(int bearerUserId, string reason, CancellationToken ct = default)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == bearerUserId, ct);
        if (cred is null) return false;
        if (cred.BlockedAt is not null) return false; // bereits offen → erste Ursache behalten

        cred.BlockedAt = DateTime.UtcNow;
        cred.BlockedReason = Trunc(reason, 500);
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Chessable-Bearer von User {UserId} gesperrt (Circuit-Breaker offen) — keine weiteren Anfragen bis „Testen“ bestätigt: {Reason}",
            bearerUserId, reason);
        return true;
    }

    /// <summary>Ist der Breaker für den Bearer dieses Users offen?</summary>
    public async Task<bool> IsOpenAsync(int bearerUserId, CancellationToken ct = default) =>
        await _db.ChessableCredentials.AnyAsync(c => c.UserId == bearerUserId && c.BlockedAt != null, ct);

    /// <summary>
    /// Schließt den Breaker (z. B. nach erfolgreichem „Testen“) und nimmt alle Importe, die WEGEN des
    /// Breakers pausiert wurden (<c>Status="paused"</c>, <c>Phase="bearer-blocked"</c>), die diesen
    /// Bearer benutzen, wieder auf. Download-Lane-Importe bekommen ein Queue-Ticket; voll-gecachte
    /// (Fast-Lane) holt der Fast-Lane-Service selbst ab. Liefert die Anzahl wieder aufgenommener Importe.
    /// No-op, wenn der Breaker gar nicht offen war.
    /// </summary>
    public async Task<int> ClearAndResumeAsync(int bearerUserId, CancellationToken ct = default)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == bearerUserId, ct);
        if (cred?.BlockedAt is null) return 0;

        cred.BlockedAt = null;
        cred.BlockedReason = null;

        var paused = await _db.ChessableImports
            .Where(i => i.Status == "paused" && i.Phase == "bearer-blocked"
                && (i.BearerUserId ?? i.UserId) == bearerUserId)
            .ToListAsync(ct);
        foreach (var imp in paused)
        {
            imp.Status = "running";
            imp.Phase = "queued";
            imp.Attempts = 0;
        }
        await _db.SaveChangesAsync(ct);

        // Pro Download-Import ein Ticket (Fast-Lane-Importe treibt ihr eigener Loop).
        foreach (var _ in paused.Where(p => p.FullyCached != true))
        {
            await _taskQueue.EnqueueAsync(async (sp, c) =>
            {
                var svc = sp.GetRequiredService<ChessableImportService>();
                await svc.RunNextAsync(c);
            });
        }

        _logger.LogInformation(
            "Chessable-Bearer von User {UserId} freigegeben (Test erfolgreich) — {Count} pausierte Importe wieder aufgenommen",
            bearerUserId, paused.Count);
        return paused.Count;
    }

    private static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;
}
