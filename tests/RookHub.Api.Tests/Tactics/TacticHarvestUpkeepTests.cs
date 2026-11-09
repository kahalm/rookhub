using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Tactics;

namespace RookHub.Api.Tests.Tactics;

/// <summary>Charakterisierung der Pflege-Schritte der Taktik-Ernte (Kommentare umformulieren, Partie-Verweise nachtragen,
/// Vereinstitel) vor dem Herausziehen aus <see cref="TacticHarvestService"/> (Zerlegung 2026-11-09).</summary>
public class TacticHarvestUpkeepTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private TacticHarvestService Svc() => new(_db, new AnalysisJobService(_db), new QuietHours("", "UTC"),
        new ConfigurationBuilder().Build(), NullLogger<TacticHarvestService>.Instance);

    private BookPuzzle Puzzle(string file, string lineId, string? comment = null, string? source = "tactic-harvest") => new()
    {
        LineId = lineId, BookFileName = file, Round = "1", Fen = "x", Moves = "e2e4", Comment = comment, Source = source,
    };

    [Fact]
    public async Task Publish_RewordsOldNumericComments_LeavesOthersAlone()
    {
        _db.BookPuzzles.AddRange(
            Puzzle("tactics-masters.pgn", "a", "In der Partie gefunden. Nach dem ersten Zug: +5.3."),
            Puzzle("tactics-masters.pgn", "b", "In der Partie verpasst — gespielt wurde Kg2. Nach dem ersten Zug: #3."),
            Puzzle("tactics-masters.pgn", "f", "In der Partie gefunden. Die Lösung bringt #3."),
            Puzzle("tactics-masters.pgn", "c", "In der Partie gefunden. Die Lösung bringt -1.2."),
            Puzzle("tactics-masters.pgn", "d", "In der Partie gefunden. Die Lösung bringt +5.3.", source: "import"),
            Puzzle("other.pgn", "e", "Nach dem ersten Zug: +5.3."));
        await _db.SaveChangesAsync();

        await Svc().PublishAsync(default);

        var byLine = await _db.BookPuzzles.ToDictionaryAsync(p => p.LineId, p => p.Comment);
        Assert.Equal("In der Partie gefunden. Die Lösung führt zur Gewinnstellung.", byLine["a"]);
        Assert.Equal("In der Partie verpasst — gespielt wurde Kg2. Die Lösung setzt in 3 Zügen matt.", byLine["b"]);
        Assert.Equal("In der Partie gefunden. Die Lösung bringt einen leichten Vorteil.", byLine["c"]);
        Assert.Equal("In der Partie gefunden. Die Lösung bringt +5.3.", byLine["d"]);
        // „Die Lösung bringt #3." trifft die Abfrage nicht (sie sucht nur „bringt +"/„bringt -") — bleibt stehen
        Assert.Equal("In der Partie gefunden. Die Lösung bringt #3.", byLine["f"]);
        Assert.Equal("Nach dem ersten Zug: +5.3.", byLine["e"]);
    }

    [Fact]
    public async Task Publish_LinksPublishedPuzzlesToTheirGame_ClubAndOwn_MasterStaysUnlinked()
    {
        _db.GameAnalyses.AddRange(
            new GameAnalysis { Id = 1, UserId = 4, Origin = GameAnalysisOrigin.Club, LeagueClubGameId = 77, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done },
            new GameAnalysis { Id = 2, UserId = 4, Origin = GameAnalysisOrigin.SavedGame, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done },
            new GameAnalysis { Id = 3, UserId = 4, Origin = GameAnalysisOrigin.SavedGame, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
        _db.SavedGames.Add(new SavedGame { Id = 55, UserId = 4, Pgn = "", GameAnalysisId = 2 });
        TacticCandidate Cand(int analysis, string line, int ply) => new()
        {
            GameAnalysisId = analysis, Ply = ply, PrevFen = "x", BlunderUci = "e2e4", Fen = "x", GameMoveUci = "e7e5", Kind = "material",
            Moves = "d2d4", Status = TacticCandidateStatus.Published, LineId = line,
        };
        _db.TacticCandidates.AddRange(Cand(1, "tactics-club-1.pgn:t1", 21), Cand(2, "tactics-u4.pgn:t2", 30), Cand(3, "tactics-u4.pgn:t3", 9));
        _db.BookPuzzles.AddRange(Puzzle("tactics-club-1.pgn", "tactics-club-1.pgn:t1"), Puzzle("tactics-u4.pgn", "tactics-u4.pgn:t2"),
            Puzzle("tactics-u4.pgn", "tactics-u4.pgn:t3"));
        await _db.SaveChangesAsync();

        await Svc().PublishAsync(default);

        var byLine = await _db.BookPuzzles.ToDictionaryAsync(p => p.LineId, p => p.SourceGame);
        Assert.Equal("club:77:21", byLine["tactics-club-1.pgn:t1"]);
        Assert.Equal("own:55:30", byLine["tactics-u4.pgn:t2"]);
        Assert.Null(byLine["tactics-u4.pgn:t3"]);   // gespeicherte Partie gibt es nicht (mehr) → bleibt ohne Verweis
    }

    [Fact]
    public void ClubTitle_NamesTheAnonymousSideFromTheRound_KeepsTheClubNameWithoutRound()
    {
        var club = TestClubs.Home;
        var g = new LeagueClubGame { White = "Moser, Axel", WhiteFide = "24602094", Black = "Testdorf", Year = 2026, Pgn = "", MovesHash = "h" };
        var round = new TacticHarvestService.LeagueRound("2026/27 · Landesliga · Runde 1", "Moser, Axel", "Streiter, Gerhard");
        Assert.Equal("Moser, Axel – Streiter, Gerhard (2026)", TacticHarvestService.ClubTitle(g, round, club));
        Assert.Equal("Moser, Axel – Testdorf (2026)", TacticHarvestService.ClubTitle(g, null, club));
        g.Year = null;
        Assert.Equal("Moser, Axel – Testdorf", TacticHarvestService.ClubTitle(g, new TacticHarvestService.LeagueRound("x", "A", " "), club));
    }

    [Theory]
    [InlineData(null, "Die Lösung bringt einen Vorteil.")]
    [InlineData("?", "Die Lösung bringt einen Vorteil.")]
    [InlineData("#x", "Die Lösung bringt einen Vorteil.")]
    [InlineData("-0.4", "Die Lösung bringt einen leichten Vorteil.")]
    public void Outcome_UnreadableOrSmall(string? eval, string expected) => Assert.Equal(expected, TacticHarvestService.Outcome(eval));

    [Fact]
    public void ClubChapterKey_UnreadableGoesLast()
    {
        Assert.Equal((1, 0, "", 0), TacticHarvestService.ClubChapterKey(null));
        Assert.Equal((1, 0, "2026 · Liga · Runde 1", 0), TacticHarvestService.ClubChapterKey("2026 · Liga · Runde 1"));
    }
}
