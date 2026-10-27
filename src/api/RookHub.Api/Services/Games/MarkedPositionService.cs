using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.DTOs.Games;

namespace RookHub.Api.Services.Games;

/// <summary>
/// „+"-Markierungen besonders guter Stellungen (0.749.0) — markieren, zurücknehmen, auflisten. Eine Stellung je Nutzer
/// höchstens einmal (Schlüssel = FEN ohne Zugzähler); ein zweites Markieren aktualisiert nur die Herkunft, wo die neue
/// mehr weiß. Vorrat für ein späteres „Stellungen für alle nachspielen".
/// <para>Seit 0.847.0 auch ohne Anmeldung (<see cref="MarkCaller"/>): die Zeile hält fest, wer markiert hat — Konto oder
/// anonyme Kennung —, und jedes Markieren steht zusätzlich im Protokoll. Beim Anmelden wandern die anonymen ins Konto.</para>
/// </summary>
public partial class MarkedPositionService
{
    /// <summary>So viele Markierungen hält ein Konto höchstens — eine Bremse gegen Massen-Einträge, kein Sammelziel.</summary>
    public const int MaxPerUser = 5000;

    /// <summary>Ohne Anmeldung weniger — die Kennung kostet nichts, ein Konto schon.</summary>
    public const int MaxPerAnon = 500;

    /// <summary>Die festen Schlagworte (0.862.0) — Codes, die Oberfläche übersetzt sie (<c>markPosition.tag.*</c>).
    /// Alles andere ist ein eigenes Wort des Nutzers und bleibt, wie er es geschrieben hat.</summary>
    public static readonly IReadOnlyList<string> KnownTags =
    [
        "tactic", "combination", "sacrifice", "attack", "defence", "strategy", "positional", "calculation",
        "endgame", "opening", "trap", "critical", "instructive", "beautiful",
    ];

    public const int MaxTags = 12;
    public const int MaxTagLength = 30;
    public const int MaxTagsLength = 400;
    public const int MaxCommentLength = 1000;

    private static readonly HashSet<string> Contexts = new() { "analysis", "board", "mistake" };

    [GeneratedRegex("^[a-h][1-8][a-h][1-8][qrbn]?$")]
    private static partial Regex UciPattern();

    private readonly AppDbContext _db;
    private readonly ILogger<MarkedPositionService> _logger;

    public MarkedPositionService(AppDbContext db, ILogger<MarkedPositionService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<MarkedPositionService>.Instance;
    }

    /// <summary>Die Markierungen genau dieses Aufrufers (Konto ODER anonyme Kennung, nie beides).</summary>
    private IQueryable<MarkedPosition> Of(MarkCaller caller) => caller.UserId is int u
        ? _db.MarkedPositions.Where(p => p.UserId == u)
        : _db.MarkedPositions.Where(p => p.UserId == null && p.AnonSessionId == caller.AnonId);

    /// <summary>Stellungsschlüssel: Brett, Seite am Zug, Rochade, en passant (wie <see cref="GapSolver.PositionKey"/>).</summary>
    public static string? KeyOf(string? fen)
    {
        var norm = FenListParser.NormalizeFen(fen);
        return norm == null ? null : GapSolver.PositionKey(norm);
    }

    public async Task<List<MarkedPositionDto>> ListAsync(MarkCaller caller, CancellationToken ct = default)
        => caller.IsNobody ? [] : (await Of(caller).AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .Take(MaxPerUser)
                .ToListAsync(ct))
            .Select(Map).ToList();

    /// <summary>Markieren. <c>(null, "invalidFen")</c> bei unbrauchbarer FEN, <c>(null, "limit")</c> am Deckel,
    /// <c>(null, "noVisitor")</c> ohne Konto und ohne gültige anonyme Kennung.</summary>
    public async Task<(MarkedPositionDto? Mark, string? Error)> MarkAsync(MarkCaller caller, MarkPositionInputDto input,
        CancellationToken ct = default)
    {
        if (caller.IsNobody) return (null, "noVisitor");
        var userId = caller.UserId;
        var fen = FenListParser.NormalizeFen(input.Fen);
        if (fen == null) return (null, "invalidFen");
        var key = GapSolver.PositionKey(fen);

        int? savedGameId = null;
        if (userId != null && input.SavedGameId is int sg && await _db.SavedGames.AnyAsync(g => g.Id == sg && g.UserId == userId, ct))
            savedGameId = sg;
        var bestUci = input.BestUci?.Trim().ToLowerInvariant();
        if (bestUci != null && !UciPattern().IsMatch(bestUci)) bestUci = null;
        var context = input.Context != null && Contexts.Contains(input.Context) ? input.Context : "analysis";
        var token = string.IsNullOrWhiteSpace(input.ShareToken) || input.ShareToken.Length > 32 ? null : input.ShareToken.Trim();
        var ply = input.Ply is >= 0 and < GameMistakeProgressService.MaxPly ? input.Ply : null;
        var clubGameId = input.ClubGameId is > 0 ? input.ClubGameId : null;
        // 0.848.0: in einer Nebenvariante markiert — der Abzweig zählt nur, wenn die Stellung nicht selbst in der Partie steht
        var (fromPly, line) = ply is null ? MarkedPositionVariation.Clean(input.VariationFromPly, input.VariationUci) : (null, null);

        var row = await Of(caller).FirstOrDefaultAsync(p => p.PositionKey == key, ct);
        if (row == null)
        {
            if (await Of(caller).CountAsync(ct) >= (userId != null ? MaxPerUser : MaxPerAnon)) return (null, "limit");
            row = new MarkedPosition
            {
                UserId = userId, AnonSessionId = userId == null ? caller.AnonId : null,
                PositionKey = key, Fen = fen, Context = context, CreatedAt = DateTime.UtcNow,
            };
            _db.MarkedPositions.Add(row);
            _logger.LogInformation("Stellung markiert von {MarkedBy}: {PositionKey} ({Context})", caller.ToString(), key, context);
        }
        // Herkunft ergänzen, nie mit „weiß nichts" überschreiben.
        row.SavedGameId ??= savedGameId;
        row.LeagueClubGameId ??= clubGameId;
        row.ShareToken ??= token;
        row.Ply ??= ply;
        row.BestUci ??= bestUci;
        if (row.Ply is null && row.VariationFromPly is null && fromPly is not null)
            (row.VariationFromPly, row.VariationUci) = (fromPly, line);
        await _db.SaveChangesAsync(ct);
        return (Map(row), null);
    }

    /// <summary>
    /// Schlagworte und Kommentar einer markierten Stellung setzen (0.862.0). Ersetzt beide; leere Liste/leerer Text löscht.
    /// <c>(null, "notMarked")</c>, wenn die Stellung nicht markiert ist, <c>(null, "invalid")</c> bei zu vielen/zu langen
    /// Schlagworten oder zu langem Kommentar.
    /// </summary>
    public async Task<(MarkedPositionDto? Mark, string? Error)> SetNotesAsync(MarkCaller caller, MarkedPositionNotesDto input,
        CancellationToken ct = default)
    {
        var key = KeyOf(input.Fen);
        if (key == null || caller.IsNobody) return (null, "notMarked");
        var tags = CleanTags(input.Tags);
        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim().ReplaceLineEndings("\n");
        if (tags == null || comment?.Length > MaxCommentLength) return (null, "invalid");
        var row = await Of(caller).FirstOrDefaultAsync(p => p.PositionKey == key, ct);
        if (row == null) return (null, "notMarked");
        row.Tags = string.Join(';', tags);
        row.Comment = comment;
        await _db.SaveChangesAsync(ct);
        return (Map(row), null);
    }

    /// <summary>Schlagworte säubern: getrimmt, ohne Leere und Doppel (Groß/klein egal), feste Codes klein geschrieben;
    /// <c>null</c> bei mehr als <see cref="MaxTags"/>, einem zu langen Wort oder einem <c>;</c> darin.</summary>
    public static List<string>? CleanTags(IEnumerable<string>? raw)
    {
        var result = new List<string>();
        foreach (var t in raw ?? [])
        {
            var tag = (t ?? string.Empty).Trim();
            if (tag.Length == 0) continue;
            if (tag.Length > MaxTagLength || tag.Contains(';')) return null;
            var known = KnownTags.FirstOrDefault(k => string.Equals(k, tag, StringComparison.OrdinalIgnoreCase));
            tag = known ?? tag;
            if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase)) result.Add(tag);
        }
        return result.Count > MaxTags ? null : result;
    }

    /// <summary>Markierung einer Stellung zurücknehmen; <c>false</c>, wenn es keine gab.</summary>
    public async Task<bool> UnmarkAsync(MarkCaller caller, string? fen, CancellationToken ct = default)
    {
        var key = KeyOf(fen);
        if (key == null || caller.IsNobody) return false;
        var row = await Of(caller).FirstOrDefaultAsync(p => p.PositionKey == key, ct);
        if (row == null) return false;
        _db.MarkedPositions.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Die anonymen Markierungen dieser Kennung ins Konto übernehmen (beim Anmelden). Hat das Konto dieselbe Stellung schon,
    /// bleibt seine Zeile (Herkunft ergänzt) und die anonyme fällt weg. Liefert die Zahl der übernommenen Stellungen.
    /// </summary>
    public async Task<int> ClaimAnonymousAsync(int userId, string? anonId, CancellationToken ct = default)
    {
        if (!Ebooks.EbookCaller.IsValidAnonId(anonId)) return 0;
        var anon = await _db.MarkedPositions.Where(p => p.UserId == null && p.AnonSessionId == anonId).ToListAsync(ct);
        if (anon.Count == 0) return 0;
        var keys = anon.Select(a => a.PositionKey).ToList();
        var mine = await _db.MarkedPositions.Where(p => p.UserId == userId && keys.Contains(p.PositionKey))
            .ToDictionaryAsync(p => p.PositionKey, ct);
        var room = MaxPerUser - await _db.MarkedPositions.CountAsync(p => p.UserId == userId, ct);
        var claimed = 0;
        foreach (var a in anon.OrderBy(a => a.CreatedAt))
        {
            if (mine.TryGetValue(a.PositionKey, out var own))
            {
                own.SavedGameId ??= a.SavedGameId;
                own.LeagueClubGameId ??= a.LeagueClubGameId;
                own.ShareToken ??= a.ShareToken;
                own.Ply ??= a.Ply;
                own.BestUci ??= a.BestUci;
                if (own.Tags.Length == 0) own.Tags = a.Tags;
                own.Comment ??= a.Comment;
                _db.MarkedPositions.Remove(a);
                claimed++;
            }
            else if (room > 0)
            {
                a.UserId = userId;
                a.AnonSessionId = null;
                room--;
                claimed++;
            }
        }
        await _db.SaveChangesAsync(ct);
        if (claimed > 0) _logger.LogInformation("Markierte Stellungen von anon {AnonId} ins Konto {UserId}: {Count}", anonId, userId, claimed);
        return claimed;
    }

    private static MarkedPositionDto Map(MarkedPosition p) => new()
    {
        Id = p.Id, Fen = p.Fen, PositionKey = p.PositionKey, Context = p.Context, SavedGameId = p.SavedGameId,
        ClubGameId = p.LeagueClubGameId, ShareToken = p.ShareToken, Ply = p.Ply, BestUci = p.BestUci, CreatedAt = p.CreatedAt,
        VariationFromPly = p.VariationFromPly, VariationUci = p.VariationUci,
        Tags = p.Tags.Length == 0 ? [] : p.Tags.Split(';').ToList(), Comment = p.Comment,
    };
}
