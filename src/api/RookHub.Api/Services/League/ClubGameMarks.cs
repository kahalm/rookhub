using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.League;

/// <summary>
/// Interessante Stellungen einer Vereinspartie (0.848.0, Wunsch 2026-10-26: „Übersichtsseite für den Verein mit Markern neben
/// Partien mit interessanten Stellungen; in der Partie Sprungmarken zu ihnen — nicht jede muss in der Partie vorkommen, sie kann
/// auch in einer Variation markiert werden"). Quelle sind die „+"-Markierungen (<see cref="MarkedPosition"/>) ALLER Nutzer, die
/// an der Vereinspartie hängen — direkt (<see cref="MarkedPosition.LeagueClubGameId"/>) oder über eine Kopie in „Meine Partien"
/// (<see cref="SavedGame.LeagueClubGameId"/>). Wer markiert hat, geht nicht hinaus — nur Stellung, Ort und Anzahl.
/// </summary>
public sealed class ClubGameMarks(AppDbContext db)
{
    /// <summary>Eine interessante Stellung: <paramref name="Ply"/> = Halbzüge bis zu ihr in der Partie (Sprungmarke), sonst aus
    /// einer Variante — <paramref name="VariationFromPly"/>/<paramref name="VariationUci"/> sagen, wo sie abzweigt (fehlt bei
    /// älteren Markierungen); <paramref name="Count"/> = so oft markiert.
    /// <para>0.862.0 (Wunsch 2026-10-27: „Klassifizierung + Bemerkung, hier änderbar"): <paramref name="Tags"/> = Schlagworte
    /// aller Markierenden (ohne Doppel), <paramref name="Comments"/> = Bemerkungen der ANDEREN (ohne Namen),
    /// <paramref name="MyTags"/>/<paramref name="MyComment"/> = die eigenen, <paramref name="Mine"/> = selbst markiert.</para></summary>
    public sealed record Mark(string Fen, string PositionKey, int? Ply, int? VariationFromPly, string? VariationUci, int Count,
        List<string> Tags, List<string> Comments, List<string> MyTags, string? MyComment, bool Mine);

    private IQueryable<MarkedPosition> Rows(IReadOnlyCollection<int> ids)
    {
        var copies = db.SavedGames.Where(s => s.LeagueClubGameId != null && ids.Contains(s.LeagueClubGameId.Value)).Select(s => s.Id);
        return db.MarkedPositions.Where(m => (m.LeagueClubGameId != null && ids.Contains(m.LeagueClubGameId.Value))
            || (m.SavedGameId != null && copies.Contains(m.SavedGameId.Value)));
    }

    /// <summary>Zahl der verschiedenen markierten Stellungen je Vereinspartie (fehlt = keine) — für die Marker der Übersicht.</summary>
    public async Task<Dictionary<int, int>> CountsAsync(IReadOnlyCollection<int> clubGameIds, CancellationToken ct = default)
    {
        if (clubGameIds.Count == 0) return new();
        var copies = await db.SavedGames.AsNoTracking()
            .Where(s => s.LeagueClubGameId != null && clubGameIds.Contains(s.LeagueClubGameId.Value))
            .Select(s => new { s.Id, Game = s.LeagueClubGameId!.Value }).ToDictionaryAsync(x => x.Id, x => x.Game, ct);
        var rows = await Rows(clubGameIds).AsNoTracking()
            .Select(m => new { m.LeagueClubGameId, m.SavedGameId, m.PositionKey }).ToListAsync(ct);
        return rows
            .Select(r => (Game: r.LeagueClubGameId is { } g && clubGameIds.Contains(g) ? g
                : r.SavedGameId is { } s && copies.TryGetValue(s, out var cg) ? cg : 0, r.PositionKey))
            .Where(x => x.Game != 0).Distinct()
            .GroupBy(x => x.Game).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Höchstzahl Partien je <see cref="CountsForClubAsync"/>-Aufruf.</summary>
    public const int MaxCountIds = 200;

    /// <summary>Wie <see cref="CountsAsync"/>, aber nur für Partien DIESES Vereins (0.850.0, Marker in den Aufstellungen einer
    /// Runde — die Ids kommen dort vom Aufrufer); fremde und unbekannte Ids fehlen.</summary>
    public async Task<Dictionary<int, int>> CountsForClubAsync(LeagueClub club, IReadOnlyCollection<int> clubGameIds, CancellationToken ct = default)
    {
        if (clubGameIds.Count == 0) return new();
        var own = await db.LeagueClubGames.AsNoTracking().Where(g => g.ClubId == club.Id && clubGameIds.Contains(g.Id))
            .Select(g => g.Id).ToListAsync(ct);
        return await CountsAsync(own, ct);
    }

    /// <summary>Die interessanten Stellungen EINER Vereinspartie des Vereins, in Partiereihenfolge (Hauptlinie nach Halbzug,
    /// Varianten nach Abzweig, ohne Ort zuletzt). <c>null</c> = keine Partie dieses Vereins. <paramref name="userId"/> = der
    /// Betrachter (seine Schlagworte/Bemerkung stehen getrennt).</summary>
    public async Task<List<Mark>?> ForGameAsync(LeagueClub club, int clubGameId, int? userId = null, CancellationToken ct = default)
    {
        var pgn = await db.LeagueClubGames.AsNoTracking().Where(g => g.Id == clubGameId && g.ClubId == club.Id)
            .Select(g => g.Pgn).FirstOrDefaultAsync(ct);
        if (pgn is null) return null;
        var rows = await Rows([clubGameId]).AsNoTracking().ToListAsync(ct);
        if (rows.Count == 0) return new();
        var mainline = MainlineKeys(pgn);
        return rows.GroupBy(r => r.PositionKey).Select(g =>
        {
            var first = g.OrderBy(r => r.CreatedAt).First();
            var ply = mainline.TryGetValue(LooseKey(g.Key), out var p) ? p : (int?)null;
            var withPlace = ply is null ? g.FirstOrDefault(r => r.VariationFromPly is not null) : null;
            var mine = userId is int u ? g.FirstOrDefault(r => r.UserId == u) : null;
            var tags = g.OrderBy(r => r.CreatedAt).SelectMany(r => SplitTags(r.Tags)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var comments = g.Where(r => r != mine && !string.IsNullOrWhiteSpace(r.Comment)).OrderBy(r => r.CreatedAt)
                .Select(r => r.Comment!).ToList();
            return new Mark(first.Fen, g.Key, ply, withPlace?.VariationFromPly, withPlace?.VariationUci, g.Count(),
                tags, comments, mine is null ? [] : SplitTags(mine.Tags), mine?.Comment, mine is not null);
        }).OrderBy(m => m.Ply ?? m.VariationFromPly ?? int.MaxValue).ThenBy(m => m.Ply is null).ToList();
    }

    private static List<string> SplitTags(string tags) => tags.Length == 0 ? [] : tags.Split(';').ToList();

    /// <summary>
    /// Schlagworte und Bemerkung des Betrachters zu einer Stellung DIESER Vereinspartie setzen (0.862.0). Hat er sie noch
    /// nicht selbst markiert, wird sie es jetzt (mit Vereinspartie und Halbzug bzw. Abzweig der vorhandenen Markierung).
    /// <c>(false, "notFound")</c> = keine Partie dieses Vereins; sonst die Fehler von <see cref="MarkedPositionService"/>.
    /// </summary>
    public async Task<(bool Ok, string? Error)> SetNotesAsync(LeagueClub club, int clubGameId, int userId,
        MarkedPositionNotesDto input, MarkedPositionService service, CancellationToken ct = default)
    {
        var pgn = await db.LeagueClubGames.AsNoTracking().Where(g => g.Id == clubGameId && g.ClubId == club.Id)
            .Select(g => g.Pgn).FirstOrDefaultAsync(ct);
        if (pgn is null) return (false, "notFound");
        var key = MarkedPositionService.KeyOf(input.Fen);
        if (key is null) return (false, "invalidFen");
        if (MarkedPositionService.CleanTags(input.Tags) is null) return (false, "invalid");
        var ply = MainlineKeys(pgn).TryGetValue(LooseKey(key), out var p) ? p : (int?)null;
        var other = ply is null
            ? await Rows([clubGameId]).AsNoTracking().FirstOrDefaultAsync(r => r.PositionKey == key && r.VariationFromPly != null, ct)
            : null;
        var (_, markError) = await service.MarkAsync(userId, new MarkPositionInputDto
        {
            Fen = input.Fen, Context = "analysis", ClubGameId = clubGameId, Ply = ply,
            VariationFromPly = other?.VariationFromPly, VariationUci = other?.VariationUci,
        }, ct);
        if (markError is not null) return (false, markError);
        var (_, error) = await service.SetNotesAsync(userId, input, ct);
        return (error is null, error);
    }

    /// <summary>Brett, Seite am Zug, Rochade — OHNE en passant: chess.js (Brett der Seite) schreibt das Feld nur, wenn ein
    /// Schlag möglich ist, die Server-Bibliothek nach jedem Doppelschritt; sonst fände sich eine Stellung nach 1.e4 nie wieder.</summary>
    internal static string LooseKey(string fenOrKey) => string.Join(' ', fenOrKey.Trim().Split(' ').Take(3));

    /// <summary>Stellung (<see cref="LooseKey"/>) → erster Halbzug, an dem sie in der Hauptlinie steht (0 = Grundstellung).</summary>
    internal static Dictionary<string, int> MainlineKeys(string pgn)
    {
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var fen = new Chess.ChessBoard().ToFen();
        keys[LooseKey(fen)] = 0;
        var ply = 0;
        foreach (var uci in ClubPgnText.UciOf(pgn).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (MoveComparisonService.PlayUci(fen, uci) is not { } next) break;
            fen = next;
            keys.TryAdd(LooseKey(fen), ++ply);
        }
        return keys;
    }
}
