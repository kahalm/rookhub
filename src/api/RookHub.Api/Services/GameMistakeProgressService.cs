using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Buchfuehrung zum Fehler-Training einer gespeicherten Partie. Der Trainer laeuft im Browser; hier wird
/// nur vermerkt, WELCHE Halbzuege selbst gefunden wurden — daraus zeigt die Uebersicht „4 von 7 · 3 offen".
///
/// <para>Gemeldet wird additiv: eine Meldung nennt die Aufgabenzahl und die in DIESEM Durchlauf gefundenen
/// Halbzuege, gespeichert wird die VEREINIGUNG. Damit ist die Meldung idempotent (eine doppelt gesendete
/// verändert nichts) und ein zweiter Durchlauf nimmt nichts weg — „schon gefunden" bleibt gefunden.</para>
/// </summary>
public class GameMistakeProgressService
{
    /// <summary>Mehr Halbzuege kann eine Partie nicht haben (600 Halbzuege sind das Limit beim Speichern).</summary>
    public const int MaxPly = 600;

    private readonly AppDbContext _db;

    public GameMistakeProgressService(AppDbContext db) => _db = db;

    /// <summary>
    /// Fortschritt melden. <paramref name="total"/> = Aufgaben der Analyse, <paramref name="solved"/> =
    /// selbst gefundene Halbzuege. `null`, wenn die Partie dem Nutzer nicht gehoert (die Ansicht sieht 404).
    /// </summary>
    public async Task<GameMistakeProgressDto?> RecordAsync(int userId, int savedGameId, int total,
        IEnumerable<int>? solved, CancellationToken ct = default)
    {
        var gehoert = await _db.SavedGames.AnyAsync(g => g.Id == savedGameId && g.UserId == userId, ct);
        if (!gehoert) return null;

        var neu = (solved ?? Enumerable.Empty<int>()).Where(p => p >= 0 && p < MaxPly).ToHashSet();
        var jetzt = DateTime.UtcNow;
        var zeile = await _db.GameMistakeProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.SavedGameId == savedGameId, ct);

        if (zeile == null)
        {
            zeile = new GameMistakeProgress
            {
                UserId = userId, SavedGameId = savedGameId, FirstTrainedAt = jetzt,
            };
            _db.GameMistakeProgresses.Add(zeile);
        }
        else
        {
            foreach (var p in Parse(zeile.SolvedPlies)) neu.Add(p);
        }

        var liste = neu.OrderBy(p => p).ToList();
        zeile.Total = Math.Clamp(total, liste.Count, MaxPly);
        zeile.SolvedPlies = string.Join(',', liste);
        zeile.SolvedCount = liste.Count;
        zeile.LastTrainedAt = jetzt;
        await _db.SaveChangesAsync(ct);
        return Map(zeile);
    }

    /// <summary>Fortschritt zu mehreren Partien — fuer die Uebersicht (eine Abfrage statt N).</summary>
    public async Task<Dictionary<int, GameMistakeProgressDto>> ForGamesAsync(int userId,
        IReadOnlyCollection<int> gameIds, CancellationToken ct = default)
    {
        if (gameIds.Count == 0) return new();
        var zeilen = await _db.GameMistakeProgresses.AsNoTracking()
            .Where(p => p.UserId == userId && gameIds.Contains(p.SavedGameId))
            .ToListAsync(ct);
        return zeilen.ToDictionary(p => p.SavedGameId, Map);
    }

    /// <summary>Welche Halbzuege gelten schon als gefunden? Der Trainer faerbt damit seine Liste.</summary>
    public async Task<GameMistakeProgressDto?> GetAsync(int userId, int savedGameId, CancellationToken ct = default)
    {
        var zeile = await _db.GameMistakeProgresses.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.SavedGameId == savedGameId, ct);
        return zeile == null ? null : Map(zeile);
    }

    internal static IEnumerable<int> Parse(string? csv)
    {
        if (string.IsNullOrEmpty(csv)) yield break;
        foreach (var teil in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(teil, out var p) && p >= 0 && p < MaxPly) yield return p;
    }

    private static GameMistakeProgressDto Map(GameMistakeProgress p) => new()
    {
        Total = p.Total,
        Solved = p.SolvedCount,
        Open = Math.Max(0, p.Total - p.SolvedCount),
        SolvedPlies = Parse(p.SolvedPlies).ToList(),
        LastTrainedAt = p.LastTrainedAt,
    };
}
