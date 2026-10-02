using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services;

/// <summary>
/// Endlos-Modus der Kinderseite: je Rating-Fenster ein kindgerechtes Lichess-Puzzle. Die Kurve (welches
/// Rating als naechstes kommt) rechnet KidHub selbst (<c>src-kidhub/app/core/kids-endless.ts</c>), wie
/// RookHubs Endlos-Modus — der Server liefert nur die Puzzles.
///
/// <para>Kindgerecht heisst: hoechstens <see cref="MaxOwnMoves"/> eigene Zuege, dieselben Qualitaets-Grenzen
/// wie die Stufen-Leiter (<see cref="KidsCurriculum.MaxRatingDeviation"/>, <see cref="KidsCurriculum.MinPopularity"/>,
/// <see cref="KidsCurriculum.MinPlays"/>) und ohne en passant, Rochade, Unterverwandlung. Die Figurenzahl ist
/// hier bewusst frei — der Modus soll mit dem Kind schwerer werden.</para>
///
/// <para>Gezogen wird per Zufalls-Sprung in den Id-Raum: ab einer zufaelligen Id die naechsten
/// <see cref="CandidatesPerSeek"/> Puzzles des Fensters, davon das erste passende. Die Kinder-Bedingungen stehen
/// in keinem Index; so liest jede Ziehung eine Handvoll Zeilen statt aller Puzzles eines Fensters (bei mittleren
/// Ratings zehntausende).</para>
/// </summary>
public class KidsEndlessService
{
    public const int MaxOwnMoves = 3;
    public const int MaxWindows = 40;
    public const int MaxExclude = 1000;
    internal const int CandidatesPerSeek = 12;
    internal const int SeeksPerWindow = 3;
    /// <summary>Treffen alle Spruenge nur Ungeeignete: zufaellig unter den ersten so vielen des Fensters.</summary>
    internal const int FallbackScan = 200;

    private readonly AppDbContext _db;
    private readonly Random _random;

    public KidsEndlessService(AppDbContext db) : this(db, Random.Shared) { }

    internal KidsEndlessService(AppDbContext db, Random random)
    {
        _db = db;
        _random = random;
    }

    private sealed record Candidate(int Id, string Fen, string Moves, int Rating, int RatingDeviation,
        int Popularity, int NbPlays, string? Themes);

    /// <summary>Je Fenster ein Puzzle, in Fensterreihenfolge; im Lauf keins doppelt. Fenster ohne passendes
    /// Puzzle fallen weg. <see cref="DomainValidationException"/> (→ 400) ueber den Deckeln.</summary>
    public async Task<List<KidsEndlessPuzzleDto>> BatchAsync(KidsEndlessBatchRequest request, CancellationToken ct = default)
    {
        if (request.Windows.Count > MaxWindows) throw new DomainValidationException($"At most {MaxWindows} windows.");
        if (request.Exclude.Count > MaxExclude) throw new DomainValidationException($"At most {MaxExclude} excluded puzzles.");
        var result = new List<KidsEndlessPuzzleDto>();
        if (request.Windows.Count == 0) return result;

        var minId = await _db.Puzzles.MinAsync(p => (int?)p.Id, ct);
        var maxId = await _db.Puzzles.MaxAsync(p => (int?)p.Id, ct);
        if (minId is null || maxId is null) return result;

        var used = new HashSet<int>(request.Exclude);
        foreach (var window in request.Windows)
        {
            var lo = Math.Min(window.MinRating, window.MaxRating);
            var hi = Math.Max(window.MinRating, window.MaxRating);
            Candidate? pick = null;
            for (var attempt = 0; attempt < SeeksPerWindow && pick is null; attempt++)
            {
                var from = _random.Next(minId.Value, maxId.Value + 1);
                var candidates = await SeekAsync(lo, hi, from, forward: true, ct);
                // Nahe am Ende des Id-Raums: von vorn weiter, sonst waere das Ende seltener zu ziehen.
                if (candidates.Count < CandidatesPerSeek)
                    candidates.AddRange(await SeekAsync(lo, hi, from, forward: false, ct));
                pick = candidates.FirstOrDefault(c => !used.Contains(c.Id) && Suitable(c));
            }
            if (pick is null)
            {
                // Ein Fenster, in dem Passendes selten ist (hohe Ratings: meist mehr als drei eigene Zuege) —
                // gedeckelt statt das ganze Fenster zu lesen.
                var fallback = (await _db.Puzzles.AsNoTracking().Where(p => p.Rating >= lo && p.Rating <= hi)
                        .OrderBy(p => p.Id).Take(FallbackScan)
                        .Select(p => new Candidate(p.Id, p.Fen, p.Moves, p.Rating, p.RatingDeviation, p.Popularity, p.NbPlays, p.Themes))
                        .ToListAsync(ct))
                    .Where(c => !used.Contains(c.Id) && Suitable(c)).ToList();
                if (fallback.Count > 0) pick = fallback[_random.Next(fallback.Count)];
            }
            if (pick is null) continue;
            used.Add(pick.Id);
            result.Add(new KidsEndlessPuzzleDto { Id = pick.Id, Fen = pick.Fen, Moves = pick.Moves, Rating = pick.Rating });
        }
        return result;
    }

    private Task<List<Candidate>> SeekAsync(int lo, int hi, int from, bool forward, CancellationToken ct)
    {
        var inWindow = _db.Puzzles.AsNoTracking().Where(p => p.Rating >= lo && p.Rating <= hi);
        var slice = forward ? inWindow.Where(p => p.Id >= from) : inWindow.Where(p => p.Id < from);
        return slice.OrderBy(p => p.Id).Take(CandidatesPerSeek)
            .Select(p => new Candidate(p.Id, p.Fen, p.Moves, p.Rating, p.RatingDeviation, p.Popularity, p.NbPlays, p.Themes))
            .ToListAsync(ct);
    }

    private static bool Suitable(Candidate c)
    {
        if (c.RatingDeviation > KidsCurriculum.MaxRatingDeviation || c.Popularity < KidsCurriculum.MinPopularity
            || c.NbPlays < KidsCurriculum.MinPlays) return false;
        // Lichess: moves[0] stellt die Aufgabe, danach abwechselnd Kind und Gegner, der letzte gehoert dem Kind.
        var plies = c.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (plies < 2 || plies % 2 != 0 || plies / 2 > MaxOwnMoves) return false;
        var themes = (c.Themes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return !themes.Any(t => KidsCurriculum.ExcludedThemes.Contains(t));
    }
}
