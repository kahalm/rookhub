using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Ähnliche Meisterpartien" (0.543.0): zu einer gespeicherten Partie die kommentierten Partien des Rohbestands
/// (<see cref="LibraryGame"/>), die am LÄNGSTEN dieselben Züge gespielt haben — samt der Stelle, an der der Meister
/// abbog („gleich bis 7...e6, dann 8.Nc3 statt 8.Nf3"). Von dort geht es wie im Anforderungs-Dialog weiter: spielen, was
/// schon spielbar ist, sonst anfordern.
///
/// <para><b>Über die Zugfolge, nicht über den Kommentar-Index.</b> Die Idee war eine Vektorsuche mit den Textstücken der
/// Partie — eine eigene Partie hat aber keine Kommentare, und der Index findet zu Texten über Züge vor allem Stücke mit
/// DENSELBEN Zugnummern aus anderen Partien (gemessen 2026-09-26, siehe <see cref="MasterComments"/>). Dieselbe
/// Eröffnung ist dagegen eine klare Auskunft („wie spielen Meister das weiter?") und kostet ein paar Zählungen über den
/// Index auf <see cref="LibraryGame.OpeningLine"/>.</para>
/// <para><b>Auswahl</b>: die tiefste Zugfolge, die noch eine kommentierte Partie hat (binär gesucht — die Zahl der Treffer
/// fällt mit jedem Halbzug), dort die besten nach Note; reichen sie nicht für <see cref="Take"/>, kommen Partien dazu, die
/// früher abbogen — Stufe für Stufe, bis die Liste voll ist (eine Abfrage je Stufe, meist reichen zwei oder drei). Unter
/// <see cref="MinSharedPlies"/> gemeinsamen Halbzügen ist es keine Ähnlichkeit mehr, sondern „auch 1.e4".</para>
/// </summary>
public sealed class SimilarGamesService
{
    public const int MinSharedPlies = 4;
    public const int Take = 5;

    private readonly AppDbContext _db;
    private readonly LibraryGameService _library;

    public SimilarGamesService(AppDbContext db, LibraryGameService library)
    {
        _db = db;
        _library = library;
    }

    /// <summary>Zur eigenen Partie; <c>null</c>, wenn es sie nicht gibt oder sie fremd ist.</summary>
    public async Task<SimilarGamesDto?> ForOwnAsync(int userId, int gameId, CancellationToken ct = default)
    {
        var pgn = await _db.SavedGames.AsNoTracking().Where(g => g.Id == gameId && g.UserId == userId)
            .Select(g => g.Pgn).FirstOrDefaultAsync(ct);
        return pgn == null ? null : await BuildAsync(pgn, userId, ct);
    }

    /// <summary>Zur geteilten Partie; <c>null</c> bei unbekanntem Token. Anonym = <c>0</c> (nur „im Bestand" trägt).</summary>
    public async Task<SimilarGamesDto?> ForSharedAsync(string token, int? callerUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var pgn = await _db.SavedGames.AsNoTracking().Where(g => g.ShareToken == token).Select(g => g.Pgn).FirstOrDefaultAsync(ct);
        return pgn == null ? null : await BuildAsync(pgn, callerUserId ?? 0, ct);
    }

    internal async Task<SimilarGamesDto> BuildAsync(string pgn, int userId, CancellationToken ct)
    {
        var dto = new SimilarGamesDto { Opening = GameRecapService.OpeningName(pgn) };
        var parsed = GamePlies.Parse(pgn, LibraryGameReader.OpeningPlies + 1);
        if (parsed is not { } game || !LibraryGameReader.StartsFromInitialPosition(game.Header.StartFen)) return dto;
        var plies = game.Plies;
        var tokens = plies.Take(LibraryGameReader.OpeningPlies).Select(p => Normalized(p.San)).ToList();
        if (tokens.Count < MinSharedPlies) return dto;

        // Die tiefste Zugfolge mit mindestens einer kommentierten Partie: die Zahl fällt mit jedem Halbzug.
        if (await CountAsync(Prefix(tokens, MinSharedPlies), ct) == 0) return dto;
        int lo = MinSharedPlies, hi = tokens.Count;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (await CountAsync(Prefix(tokens, mid), ct) > 0) lo = mid; else hi = mid - 1;
        }
        dto.SharedPlies = lo;
        dto.SharedLine = Numbered(plies, lo);

        // Ohne das PGN: fünf Zeilen LONGTEXT für eine Liste, die keine Züge zeigt.
        var picked = new List<(Row Row, int Shared)>();
        for (var depth = lo; depth >= MinSharedPlies && picked.Count < Take; depth--)
        {
            var taken = picked.Select(p => p.Row.Game.Id).ToList();
            var rows = await Matching(Prefix(tokens, depth))
                .Where(g => !taken.Contains(g.Id))
                .OrderByDescending(g => g.Score).ThenBy(g => g.Id)
                .Take(Take - picked.Count)
                .Select(g => new Row(g.OpeningLine!, new LibraryGameDto
                {
                    Id = g.Id, White = g.White, Black = g.Black, WhiteElo = g.WhiteElo, BlackElo = g.BlackElo, Result = g.Result,
                    Event = g.Event, PlayedOn = g.PlayedOn, Eco = g.Eco, PlyCount = g.PlyCount, Annotator = g.Annotator,
                    CommentedPlies = g.CommentedPlies, CommentChars = g.CommentChars, Languages = g.Languages, Score = g.Score,
                    SourceTitle = g.SourceTitle,
                }))
                .ToListAsync(ct);
            picked.AddRange(rows.Select(r => (r, depth)));
        }

        await _library.MarkKnownAsync(userId, picked.Select(p => p.Row.Game).ToList(), ct);
        foreach (var (row, shared) in picked)
        {
            var masterTokens = row.OpeningLine.Split(' ');
            dto.Items.Add(new SimilarGameDto
            {
                Game = row.Game,
                SharedPlies = shared,
                LastSharedMove = shared > 0 ? MoveLabel(plies, shared - 1, tokens[shared - 1]) : null,
                MasterMove = shared < masterTokens.Length && shared < plies.Count ? MoveLabel(plies, shared, masterTokens[shared]) : null,
                GameMove = shared < plies.Count ? MoveLabel(plies, shared, Normalized(plies[shared].San)) : null,
            });
        }
        return dto;
    }

    private sealed record Row(string OpeningLine, LibraryGameDto Game);

    private IQueryable<LibraryGame> Matching(string prefix)
    {
        var withSeparator = prefix + " ";
        return _db.LibraryGames.AsNoTracking().Where(g => g.OpeningLine != null
            && (g.OpeningLine == prefix || g.OpeningLine.StartsWith(withSeparator))
            && g.CommentedPlies > 0 && g.Status != LibraryGameStatus.Duplicate && g.Status != LibraryGameStatus.Rejected);
    }

    private Task<int> CountAsync(string prefix, CancellationToken ct) => Matching(prefix).CountAsync(ct);

    private static string Prefix(IReadOnlyList<string> tokens, int count) => string.Join(' ', tokens.Take(count));

    /// <summary>Dieselbe Form wie <see cref="LibraryGame.OpeningLine"/> (<see cref="LibraryGameReader.AppendOpeningSan"/>).</summary>
    private static string Normalized(string san)
    {
        var sb = new StringBuilder(san.Length);
        LibraryGameReader.AppendOpeningSan(sb, san);
        return sb.ToString();
    }

    /// <summary>„7...e6" — Zugnummer aus der Stellung VOR dem Halbzug.</summary>
    private static string MoveLabel(IReadOnlyList<GamePlies.Ply> plies, int ply, string san)
    {
        var fen = plies[ply].Fen.Split(' ');
        var number = fen.Length >= 6 ? fen[5] : "?";
        return number + (fen.Length >= 2 && fen[1] == "b" ? "..." : ".") + san;
    }

    /// <summary>Die gemeinsamen Züge wie man sie schreibt: „1.e4 c6 2.d4 d5 3.f3".</summary>
    private static string Numbered(IReadOnlyList<GamePlies.Ply> plies, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count && i < plies.Count; i++)
        {
            var white = plies[i].Fen.Split(' ') is { Length: >= 2 } f && f[1] == "w";
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(white || i == 0 ? MoveLabel(plies, i, Normalized(plies[i].San)) : Normalized(plies[i].San));
        }
        return sb.ToString();
    }
}
