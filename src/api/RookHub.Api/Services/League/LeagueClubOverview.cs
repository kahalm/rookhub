using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Übersicht für den Verein (0.848.0, Wunsch 2026-10-26: „Übersichtsseite für den Verein — zeigt alle Begegnungen + Partien +
/// Marker neben Partien mit interessanten Stellungen"): jede Begegnung einer eigenen Mannschaft der laufenden Saison mit ihren
/// Brettern — dieselben Bretter wie „Alle Aufstellungen" (<see cref="LeagueGameMoves.LineupsAsync"/>, nur lesend, eigene Seite mit
/// echtem Spieler) — und je Vereinspartie der Zahl ihrer markierten Stellungen (<see cref="ClubGameMarks.CountsAsync"/>).
/// Nur Runden mit mindestens einem Brett; Begegnungen ohne Aufstellung fallen weg — auch solche, deren Bretter alle leer sind
/// (0.851.1, Wunsch 2026-10-27: „einige leere Runden" — künftige Runden stehen mit „nicht besetzt – nicht besetzt" da).
/// </summary>
public sealed class LeagueClubOverview(AppDbContext db, LeagueService league, LeagueGameMoves moves, ClubGameMarks marks)
{
    public sealed record Round(int Number, string? Date, List<LeagueGameMoves.LineupMatch> Matches);
    public sealed record League(int Tnr, string Name, List<Round> Rounds);
    public sealed record Overview(string? Season, List<League> Leagues, Dictionary<int, int> Marks);

    /// <summary>Ein Brett mit Inhalt: ein Spieler, ein Ergebnis, eingegebene Züge oder eine Partie.</summary>
    public static bool HasContent(LeagueGameMoves.LineupBoard b) =>
        !string.IsNullOrWhiteSpace(b.HomePlayer) || !string.IsNullOrWhiteSpace(b.AwayPlayer) || !string.IsNullOrWhiteSpace(b.Result)
        || !string.IsNullOrWhiteSpace(b.Moves) || b.Game is not null;

    public async Task<Overview> BuildAsync(LeagueClub club, int userId, CancellationToken ct = default)
    {
        var index = await league.IndexAsync(club, ct);
        var leagues = (index["leagues"] as JsonArray ?? [])
            .Select(l => (Tnr: (int)l!["tnr"]!, Name: (string?)l["name"] ?? ""))
            .ToList();
        var result = new List<League>();
        var gameIds = new HashSet<int>();
        foreach (var (tnr, name) in leagues)
        {
            var rounds = await db.LeagueGames.AsNoTracking().Where(g => g.Tnr == tnr).Select(g => g.Round).Distinct()
                .OrderBy(r => r).ToListAsync(ct);
            // Nur Runden, in denen eine eigene Mannschaft spielt, voll aufstellen (R2-007): vorher lief LineupsAsync (Begegnungen,
            // Bretter, Züge, Vereinspartien) für JEDE Runde jeder Liga und wurde erst danach auf eigene Begegnungen gefiltert.
            // Dieselbe Regel wie LineupMatch.Own (Heim oder Gast aus Spielplan oder Brettpaarung) — eine Runde ohne eigene
            // Mannschaft fiel dort ohnehin weg.
            var ownRounds = await OwnRoundsAsync(club, tnr, ct);
            rounds = rounds.Where(ownRounds.Contains).ToList();
            var list = new List<Round>();
            foreach (var r in rounds)
            {
                if (await moves.LineupsAsync(club, tnr, r, userId, canContribute: false, canManage: false, revealOwn: true, ct) is not { } l)
                    continue;
                var own = l.Matches.Where(m => m.Own && m.Boards.Any(HasContent)).ToList();
                if (own.Count == 0) continue;
                foreach (var b in own.SelectMany(m => m.Boards))
                    if (b.Game?.ClubGameId is { } id) gameIds.Add(id);
                list.Add(new Round(r, l.Date, own));
            }
            if (list.Count > 0) result.Add(new League(tnr, name, list));
        }
        return new Overview((string?)index["season"], result, await marks.CountsAsync(gameIds, ct));
    }

    /// <summary>Die Runden der Liga, in denen laut Spielplan oder Brettpaarungen eine Mannschaft des Vereins spielt — zwei Abfragen
    /// je Liga statt einer vollen Aufstellung je Runde.</summary>
    private async Task<HashSet<int>> OwnRoundsAsync(LeagueClub club, int tnr, CancellationToken ct)
    {
        var fixtures = await db.LeagueMatches.AsNoTracking().Where(m => m.Tnr == tnr)
            .Select(m => new { m.Round, Home = m.Home, Away = m.Away }).Distinct().ToListAsync(ct);
        var pairings = await db.LeagueGames.AsNoTracking().Where(g => g.Tnr == tnr)
            .Select(g => new { g.Round, Home = g.HomeTeam, Away = g.AwayTeam }).Distinct().ToListAsync(ct);
        return fixtures.Select(f => (f.Round, f.Home, f.Away)).Concat(pairings.Select(p => (p.Round, p.Home, p.Away)))
            .Where(x => club.OwnsTeam(x.Home) || club.OwnsTeam(x.Away)).Select(x => x.Round).ToHashSet();
    }
}
