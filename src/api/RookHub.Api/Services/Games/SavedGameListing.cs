using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using static RookHub.Api.Services.Games.SavedGamePgn;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Die Listen der gespeicherten Partien (zweite Zerlegungsrunde W7b aus <see cref="SavedGameService"/>): „Meine Partien"
/// samt Nachtraegen (Zugzahl, Wertungen aus dem PGN, Genauigkeit) und die Uebersichts-Abfrage der Erweiterung
/// („liegt schon bei RookHub?"). Die Deckel stehen als Konstanten an <see cref="SavedGameService"/>.
/// </summary>
internal sealed class SavedGameListing
{
    private readonly AppDbContext _db;

    internal SavedGameListing(AppDbContext db) => _db = db;

    /// <summary>
    /// Welche dieser Plattform-Partien liegen schon bei RookHub? Fuer die Uebersicht auf chess.com/lichess:
    /// bekannte Partien tragen dort ein Haekchen statt des Sende-Knopfs. Nur Existenz und Analyse-Stand —
    /// keine Zuege, keine PGN.
    /// </summary>
    public async Task<List<KnownGameDto>> KnownAsync(int userId, string? source,
        IReadOnlyCollection<string>? externalIds, CancellationToken ct = default)
    {
        var src = SavedGameService.NormalizeSource(source);
        var ids = (externalIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .Take(SavedGameService.MaxKnownLookup)
            .ToList();
        if (src == null || ids.Count == 0) return new();

        var games = await _db.SavedGames.AsNoTracking()
            .Where(g => g.UserId == userId && g.Source == src && g.ExternalId != null && ids.Contains(g.ExternalId))
            .Select(g => new { g.Id, g.ExternalId, g.GameAnalysisId })
            .ToListAsync(ct);
        // AnalysisStatesAsync schluesselt nach GameAnalysis.Id — NICHT nach SavedGame.Id (wie ListAsync).
        var states = await AnalysisStatesAsync(
            games.Where(g => g.GameAnalysisId != null).Select(g => g.GameAnalysisId!.Value).Distinct().ToList());
        return games
            .Select(g => new KnownGameDto
            {
                ExternalId = g.ExternalId!,
                Id = g.Id,
                Analysis = g.GameAnalysisId != null && states.TryGetValue(g.GameAnalysisId.Value, out var a) ? a : null,
            })
            .ToList();
    }

    /// <summary>Gespeicherte Partien des Users, neueste zuerst (ohne PGN).</summary>
    public async Task<List<SavedGameDto>> ListAsync(int userId, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        // Das PGN (LONGTEXT) wird NUR fuer Zeilen mitgeholt, deren Zaehler noch fehlt. Frueher
        // stand hier CountPlies(g.Pgn) in der Projektion - eine C#-Methode, die EF nicht
        // uebersetzen kann: die Spalte wanderte damit fuer JEDE der bis zu 500 Partien ueber die
        // Leitung, obwohl das Listen-DTO das PGN gar nicht traegt.
        var rows = await _db.SavedGames.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.CreatedAt)
            .Take(take)
            .Select(g => new
            {
                g.Id, g.Source, g.White, g.Black, g.Result, g.PlayedAt,
                g.SourceUrl, g.ShareToken, g.MoveCount, g.CreatedAt, g.GameAnalysisId,
                g.WhiteElo, g.BlackElo, g.TimeControl, g.HeadersScanned, g.Classifier1, g.Classifier2, g.Tags,
                PgnIfUncounted = g.MoveCount == null ? g.Pgn : null,
                ScanId = _db.ScoresheetScans.Where(sc => sc.SavedGameId == g.Id).Select(sc => (int?)sc.Id).FirstOrDefault(),
            })
            .ToListAsync();

        // Wertungen des Altbestands aus dem PGN in die Spalten heben — portionsweise, weil dafuer das
        // LONGTEXT geladen werden muss. Eine EIGENE Abfrage statt eines zweiten Feldes in der Projektion
        // oben: dort gaebe es keinen Deckel, und die erste Liste eines Vielspielers zoege alle PGNs.
        var elos = await BackfillElosAsync(rows.Where(r => !r.HeadersScanned).Select(r => r.Id).ToList());

        var analyses = await AnalysisStatesAsync(
            rows.Where(r => r.GameAnalysisId != null).Select(r => r.GameAnalysisId!.Value).Distinct().ToList());

        var healed = new Dictionary<int, int>();
        foreach (var r in rows)
            if (r.MoveCount == null && r.PgnIfUncounted != null)
                healed[r.Id] = CountPlies(r.PgnIfUncounted);

        // Einmalig nachtragen, damit dieselbe Zeile ihr PGN nie wieder fuer den Zaehler hergibt.
        // Bewusst best-effort: schlaegt das Schreiben fehl, stimmt die Anzeige trotzdem.
        if (healed.Count > 0)
        {
            try
            {
                var ids = healed.Keys.ToList();
                var tracked = await _db.SavedGames.Where(g => ids.Contains(g.Id)).ToListAsync();
                foreach (var g in tracked) g.MoveCount = healed[g.Id];
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException) { /* Anzeige stimmt auch ohne den Nachtrag */ }
        }

        return rows.Select(r => new SavedGameDto
        {
            Id = r.Id,
            Source = r.Source,
            White = r.White,
            Black = r.Black,
            Result = r.Result,
            PlayedAt = r.PlayedAt,
            SourceUrl = SafeSourceUrl(r.SourceUrl),
            ShareToken = r.ShareToken,
            MoveCount = r.MoveCount ?? (healed.TryGetValue(r.Id, out var c) ? c : 0),
            CreatedAt = r.CreatedAt,
            WhiteElo = r.WhiteElo ?? (elos.TryGetValue(r.Id, out var e) ? e.White : null),
            BlackElo = r.BlackElo ?? (elos.TryGetValue(r.Id, out var e2) ? e2.Black : null),
            TimeControl = r.TimeControl,
            Analysis = r.GameAnalysisId is int aid && analyses.TryGetValue(aid, out var state) ? state : null,
            ScanId = r.ScanId,
            Classifier1 = GameClassifier.Effective(r.Source, r.TimeControl, r.Classifier1, r.Classifier2).First,
            Classifier2 = GameClassifier.Effective(r.Source, r.TimeControl, r.Classifier1, r.Classifier2).Second,
            Classifier1Set = GameClassifier.Clean(r.Classifier1),
            Classifier2Set = GameClassifier.Clean(r.Classifier2),
            Tags = GameTags.Parse(r.Tags),
        }).ToList();
    }

    /// <summary>
    /// Traegt die Wertungen aus dem PGN-Header in die Spalten nach und setzt
    /// <see cref="SavedGame.HeadersScanned"/> — auch bei einer Partie OHNE Elo-Header, sonst sieht
    /// dieselbe Zeile bei jedem Aufruf wieder „noch nicht nachgesehen" aus. Best-effort: schlaegt das
    /// Schreiben fehl, stimmt die Anzeige trotzdem (und der naechste Aufruf versucht es erneut).
    /// </summary>
    private async Task<Dictionary<int, (int? White, int? Black)>> BackfillElosAsync(List<int> ids)
    {
        var found = new Dictionary<int, (int? White, int? Black)>();
        if (ids.Count == 0) return found;
        try
        {
            var tracked = await _db.SavedGames.Where(g => ids.Contains(g.Id))
                .OrderByDescending(g => g.CreatedAt).Take(SavedGameService.HeaderBackfillPerCall).ToListAsync();
            foreach (var g in tracked)
            {
                g.WhiteElo = ParseEloHeader(g.Pgn, "WhiteElo");
                g.BlackElo = ParseEloHeader(g.Pgn, "BlackElo");
                g.HeadersScanned = true;
                found[g.Id] = (g.WhiteElo, g.BlackElo);
            }
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) { /* Anzeige stimmt auch ohne den Nachtrag */ }
        return found;
    }

    /// <summary>
    /// Stand der verknuepften Analysen fuer die Liste: Status, Fortschritt (EINE gruppierte Zaehlung ueber alle
    /// Ids statt einer Abfrage je Partie) und die abgelegte Genauigkeit. Eine FERTIGE Analyse ohne Genauigkeit
    /// (von vor 0.515.0) wird hier einmal nachgerechnet und geschrieben — best-effort, die Anzeige stimmt auch
    /// ohne den Nachtrag. Verweise ins Leere (Analyse geloescht) fehlen im Ergebnis, die Partie zeigt dann
    /// wieder den Knopf.
    /// </summary>
    private Task<Dictionary<int, SavedGameAnalysisDto>> AnalysisStatesAsync(List<int> ids) =>
        GameEvalsStore.StatesAsync(_db, ids, SavedGameService.AccuracyBackfillPerCall);
}
