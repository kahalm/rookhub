using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>
/// Interessante Stellungen der Vereinspartien (0.848.0, Wunsch 2026-10-26: „Marker neben Partien mit interessanten Stellungen,
/// in der Partie Sprungmarken — nicht jede muss in der Partie vorkommen, sie kann auch in einer Variation markiert werden"):
/// Hauptlinie → Halbzug, Variante → Abzweig + Züge, Markierungen an einer Kopie zählen mit, anderer Verein sieht nichts.
/// </summary>
public class ClubGameMarksTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private const string Pgn = "[White \"Ackermann, Anna\"]\n[Black \"Testdorf\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 Nc6 1-0\n";
    // nach 1.e4 e5 2.Sf3 (Halbzug 3) — steht in der Partie
    private const string AfterNf3 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2";
    // nach 1.e4 c5 2.Sf3 — Sizilianisch, steht NICHT in der Partie (Variante ab Halbzug 1)
    private const string Sicilian = "rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2";
    // nach 1.e4 (Halbzug 1) — chess.js schreibt kein en-passant-Feld, die Server-Bibliothek schon
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

    private async Task<LeagueClubGame> SeedAsync()
    {
        await TestClubs.SeedAsync(_db);
        _db.AppUsers.AddRange(new AppUser { Id = 1, Username = "a", Email = "a@t", PasswordHash = "x" },
            new AppUser { Id = 2, Username = "b", Email = "b@t", PasswordHash = "x" });
        var g = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "Ackermann, Anna", Black = "Testdorf", Result = "1-0",
            Plies = 4, Pgn = Pgn, MovesHash = "h1", UploadedByUserId = 1 };
        _db.LeagueClubGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task ForGame_MainlineGetsPly_VariationGetsBranch_CopyCounts_OtherClubNothing()
    {
        var g = await SeedAsync();
        var svc = new MarkedPositionService(_db);
        // Nutzer 1 in der Hauptlinie (mit Halbzug), Nutzer 2 dieselbe Stellung ohne Halbzug → zusammen 2×
        await svc.MarkAsync(1, new MarkPositionInputDto { Fen = AfterNf3, ClubGameId = g.Id, Ply = 3 });
        await svc.MarkAsync(2, new MarkPositionInputDto { Fen = AfterNf3, ClubGameId = g.Id });
        // Variante ab Halbzug 1: 1…c5 2.Sf3
        await svc.MarkAsync(2, new MarkPositionInputDto { Fen = Sicilian, ClubGameId = g.Id, VariationFromPly = 1, VariationUci = "c7c5 g1f3" });
        // an einer Kopie in „Meine Partien" markiert, ohne Vereinspartie-Id — zählt trotzdem
        var copy = new SavedGame { UserId = 1, Source = "club", Pgn = Pgn, ShareToken = "tok", LeagueClubGameId = g.Id };
        _db.SavedGames.Add(copy);
        await _db.SaveChangesAsync();
        await svc.MarkAsync(1, new MarkPositionInputDto { Fen = AfterE4, SavedGameId = copy.Id });

        var marks = new ClubGameMarks(_db);
        var list = (await marks.ForGameAsync(TestClubs.Home, g.Id))!;
        Assert.Equal(3, list.Count);
        // Partiereihenfolge: 1.e4 (Halbzug 1, trotz anderem en-passant-Feld gefunden), die Variante an ihrem Abzweig 1, dann 2.Sf3
        Assert.Equal(((int?)1, 1), (list[0].Ply, list[0].Count));
        Assert.Equal(((int?)null, (int?)1, "c7c5 g1f3"), (list[1].Ply, list[1].VariationFromPly, list[1].VariationUci));
        Assert.Equal(((int?)3, 2), (list[2].Ply, list[2].Count));
        Assert.Null(await marks.ForGameAsync(TestClubs.Other, g.Id));

        var counts = await marks.CountsAsync([g.Id, g.Id + 99]);
        Assert.Equal(3, counts[g.Id]);
        Assert.False(counts.ContainsKey(g.Id + 99));
        // 0.850.0: Ids vom Aufrufer (Aufstellungen einer Runde) — nur Partien des eigenen Vereins
        Assert.Equal(3, (await marks.CountsForClubAsync(TestClubs.Home, [g.Id]))[g.Id]);
        Assert.Empty(await marks.CountsForClubAsync(TestClubs.Other, [g.Id]));
    }

    [Fact]
    public async Task Mark_VariationOnlyWithoutPly_AndOnlyValidMoves()
    {
        var g = await SeedAsync();
        var svc = new MarkedPositionService(_db);
        var withPly = (await svc.MarkAsync(1, new MarkPositionInputDto { Fen = AfterNf3, ClubGameId = g.Id, Ply = 3,
            VariationFromPly = 1, VariationUci = "e7e5 g1f3" })).Mark!;
        Assert.Null(withPly.VariationFromPly);                                         // steht in der Partie — kein Abzweig
        var bad = (await svc.MarkAsync(1, new MarkPositionInputDto { Fen = Sicilian, ClubGameId = g.Id,
            VariationFromPly = 1, VariationUci = "c7c5; drop table" })).Mark!;
        Assert.Equal(((int?)null, (string?)null), (bad.VariationFromPly, bad.VariationUci));
        Assert.Equal(((int?)2, "c7c5 g1f3"), MarkedPositionVariation.Clean(2, " C7C5 G1F3 "));
        Assert.Equal(((int?)null, (string?)null), MarkedPositionVariation.Clean(-1, "c7c5"));
    }

    // 0.849.0, Wunsch 2026-10-27: „in der Übersicht … zeige auch !! an" — PGN + schlanke Bewertungen in einem Aufruf
    [Fact]
    public async Task ReviewInputs_LatestClubAnalysis_WithoutCandidates_OnlyOwnClubAndAnalyzed()
    {
        var g = await SeedAsync();
        var bare = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "B", Black = "C", Pgn = Pgn, MovesHash = "h2" };
        var other = new LeagueClubGame { ClubId = TestClubs.OtherId, Year = 2026, White = "D", Black = "E", Pgn = Pgn, MovesHash = "h3" };
        _db.LeagueClubGames.AddRange(bare, other);
        await _db.SaveChangesAsync();
        GameAnalysis Analysis(int gameId, string bestUci) => new()
        {
            UserId = 1, Pgn = Pgn, StartFen = "startpos", Status = GameAnalysisStatus.Done, PlyCount = 4, TargetDepth = 20,
            Origin = GameAnalysisOrigin.Club, LeagueClubGameId = gameId,
            Positions = { new GameAnalysisPosition { Ply = 0, Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                GameMoveUci = "e2e4", GameMoveSan = "e4", Depth = 20,
                CandidatesJson = $$"""[{"uci":"{{bestUci}}","cp":30,"pv":["{{bestUci}}","e7e5"]},{"uci":"e2e4","cp":20}]""" } },
        };
        _db.GameAnalyses.AddRange(Analysis(g.Id, "d2d4"), Analysis(g.Id, "e2e4"), Analysis(other.Id, "e2e4"));
        await _db.SaveChangesAsync();

        var club = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        var got = await club.ReviewInputsAsync(TestClubs.Home, [g.Id, bare.Id, other.Id, 999_123]);
        Assert.Equal([g.Id], got.Keys);                                     // ohne Analyse / fremder Verein / unbekannt fehlen
        Assert.Equal(Pgn, got[g.Id].Pgn);
        Assert.Null(got[g.Id].Brilliant);                                    // nicht geerntet: der Browser rechnet
        var ply = Assert.Single(got[g.Id].Evals!.Plies);
        Assert.Equal(("e2e4", 20), (ply.BestUci, ply.SecondCp));            // die jüngste Analyse zählt
        Assert.Empty(ply.Candidates);                                       // schlank: keine Kandidatenlisten

        // 0.854.0: geerntet im aktuellen Regelstand → nur die „!!"-Halbzüge, ohne PGN und Bewertungen.
        var newest = _db.GameAnalyses.Where(a => a.LeagueClubGameId == g.Id).OrderByDescending(a => a.Id).First();
        newest.MoveClasses = "SB-B";
        newest.MoveClassesVersion = GameMoveClasses.Version;
        await _db.SaveChangesAsync();
        var harvested = (await club.ReviewInputsAsync(TestClubs.Home, [g.Id]))[g.Id];
        Assert.Equal([1, 3], harvested.Brilliant);
        Assert.Null(harvested.Pgn);
        Assert.Null(harvested.Evals);

        newest.MoveClassesVersion = GameMoveClasses.Version - 1;            // alter Regelstand: wieder rechnen lassen
        await _db.SaveChangesAsync();
        Assert.Null((await club.ReviewInputsAsync(TestClubs.Home, [g.Id]))[g.Id].Brilliant);
    }

    // 0.851.1, Wunsch 2026-10-27: „einige leere Runden" — Begegnungen, deren Bretter alle „nicht besetzt" sind, fallen weg
    [Fact]
    public void Overview_EmptyBoardsHaveNoContent()
    {
        LeagueGameMoves.LineupBoard B(string? home = null, string result = "", string? moves = null) =>
            new(1, home, null, null, null, null, null, null, result, 0, moves, false);
        Assert.False(LeagueClubOverview.HasContent(B()));
        Assert.False(LeagueClubOverview.HasContent(B(home: " ")));
        Assert.True(LeagueClubOverview.HasContent(B(home: "Hess, Max")));
        Assert.True(LeagueClubOverview.HasContent(B(result: "+ - -")));
        Assert.True(LeagueClubOverview.HasContent(B(moves: "e4")));
    }

    // 0.862.0 (Wunsch 2026-10-27: „Diagramm, wer am Zug, Klassifizierung + Bemerkung — hier änderbar/erweiterbar").
    [Fact]
    public async Task SetNotes_MarksForTheViewer_AndForGameSplitsOwnFromOthers()
    {
        var g = await SeedAsync();
        var svc = new MarkedPositionService(_db);
        await svc.MarkAsync(2, new MarkPositionInputDto { Fen = Sicilian, ClubGameId = g.Id, VariationFromPly = 1, VariationUci = "c7c5 g1f3" });
        await svc.SetNotesAsync(2, new MarkedPositionNotesDto { Fen = Sicilian, Tags = ["opening"], Comment = "von b" });
        var marks = new ClubGameMarks(_db);

        // Nutzer 1 ergänzt die Variante, die nur Nutzer 2 markiert hatte → eigene Markierung mit Abzweig
        var (ok, err) = await marks.SetNotesAsync(TestClubs.Home, g.Id, 1,
            new MarkedPositionNotesDto { Fen = Sicilian, Tags = ["trap", "opening"], Comment = "von a" }, svc);
        Assert.True(ok, err);
        var own = _db.MarkedPositions.Single(p => p.UserId == 1);
        Assert.Equal((g.Id, (int?)1, "c7c5 g1f3"), (own.LeagueClubGameId!.Value, own.VariationFromPly, own.VariationUci));

        var m = Assert.Single((await marks.ForGameAsync(TestClubs.Home, g.Id, 1))!);
        Assert.Equal(2, m.Count);
        Assert.True(m.Mine);
        Assert.Equal(["opening", "trap"], m.Tags);                 // alle Schlagworte, ohne Doppel
        Assert.Equal(["trap", "opening"], m.MyTags);
        Assert.Equal("von a", m.MyComment);                       // eigene getrennt …
        Assert.Equal(["von b"], m.Comments);                      // … fremde ohne Namen

        // Hauptlinie: Halbzug wird aus der Partie bestimmt; fremder Verein → notFound
        Assert.True((await marks.SetNotesAsync(TestClubs.Home, g.Id, 1, new MarkedPositionNotesDto { Fen = AfterNf3 }, svc)).Ok);
        Assert.Equal(3, _db.MarkedPositions.Single(p => p.UserId == 1 && p.Ply != null).Ply);
        Assert.Equal("notFound", (await marks.SetNotesAsync(TestClubs.Other, g.Id, 1, new MarkedPositionNotesDto { Fen = AfterNf3 }, svc)).Error);
    }
}
