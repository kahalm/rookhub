using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Tages-Zeilenlimit pro Chessable-Bearer-User (Standard 2000 Zeilen/24h, konfigurierbar via
/// <c>Chessable:DailyLineLimitPerUser</c>). Zählt NUR echte Netz-Fetches (Download-Lane) — voll-
/// gecachte Kurse (<see cref="ChessableImport.FullyCached"/> == true) kosten Chessable nichts und
/// zählen daher nicht. Das Fenster liegt auf <see cref="ChessableCredential"/> (keyed by Bearer-User),
/// analog zum <see cref="ChessableBearerBreaker"/>.
/// </summary>
public class ChessableRateLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;
    private readonly int _dailyLimit;

    public ChessableRateLimiter(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _dailyLimit = Math.Max(1, configuration.GetValue("Chessable:DailyLineLimitPerUser", 2000));
    }

    public int DailyLimit => _dailyLimit;

    /// <summary>Setzt das Fenster zurück, wenn es abgelaufen (oder noch nie gesetzt) ist. Persistiert
    /// NICHT selbst — der Aufrufer speichert zusammen mit den übrigen Änderungen.</summary>
    public void EnsureFreshWindow(ChessableCredential cred, DateTime now)
    {
        if (cred.RateLimitWindowStartedAt is null || now - cred.RateLimitWindowStartedAt.Value >= Window)
        {
            cred.RateLimitWindowStartedAt = now;
            cred.RateLimitLinesUsed = 0;
        }
    }

    /// <summary>true, wenn das Tageslimit im aktuellen Fenster bereits ausgeschöpft ist. Ruf vorher
    /// <see cref="EnsureFreshWindow"/> auf, damit ein abgelaufenes Fenster nicht fälschlich blockt.</summary>
    public bool IsOverLimit(ChessableCredential cred) => cred.RateLimitLinesUsed >= _dailyLimit;

    /// <summary>Verbucht tatsächlich über Chessable abgerufene Zeilen im aktuellen Fenster des
    /// Bearer-Users (Fenster wird bei Bedarf zuerst aufgefrischt). No-op ohne Credential/ohne Zeilen.</summary>
    public async Task RecordUsageAsync(int bearerUserId, int lines, CancellationToken ct = default)
    {
        if (lines <= 0) return;
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == bearerUserId, ct);
        if (cred is null) return;
        EnsureFreshWindow(cred, DateTime.UtcNow);
        cred.RateLimitLinesUsed += lines;
        await _db.SaveChangesAsync(ct);
    }
}
