using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers.Games;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Buchbeispiele der Empfohlenen Übungen (<see cref="BookExercises"/>, 0.962.0): Signaturen aus Buchdiagrammen, Lauf,
/// Rangfolge Buch vor Lichess, Rechte wie die Bibliothek, Lösbarkeit nur mit bestätigter Buchlösung.</summary>
public class BookExercisesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    // Weiß am Zug: Dg7# — gedeckt vom Bauern f6 bzw. (gleiche Familie) vom Springer h5. Wie die Lichess-Aufgaben in GameExercisesTests.
    private const string QueenPawn = "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1";
    private const string QueenKnight = "6k1/5p1p/7Q/7N/8/8/8/K7 w - - 0 1";

    [Fact]
    public void SignaturesOf_BookMate_SameAsLichess_PawnAndKnightSameFamily()
    {
        var pawn = Assert.Single(BookExercises.SignaturesOf(QueenPawn, "h6g7", null));
        var knight = Assert.Single(BookExercises.SignaturesOf(QueenKnight, "h6g7", null));
        Assert.Equal(PuzzleSignatureKind.Mate, pawn.Kind);
        Assert.Equal(1, pawn.MateIn);
        // Dieselbe Signatur wie die Lichess-Aufgabe mit demselben Bild (Fen vor dem Gegnerzug, dann Gegnerzug + Lösung).
        var lichess = MatePattern.OfPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7")!;
        Assert.Equal(lichess.StrictHash, pawn.Strict);
        Assert.NotEqual(pawn.Strict, knight.Strict);
        Assert.Equal(pawn.Family, knight.Family);
    }

    [Fact]
    public void SignaturesOf_Motif_OnlyWhenWinning_BrokenLinesEmpty()
    {
        // Sc7+ gabelt König und Turm (wie die verpasste Gabel der Partie), Bewertung +6.
        const string fork = "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1";
        var row = Assert.Single(BookExercises.SignaturesOf(fork, "d5c7 e8d7 c7a8", 600));
        Assert.Equal(PuzzleSignatureKind.Fork, row.Kind);
        Assert.Equal(TacticMotifs.Detect(fork, ["d5c7", "e8d7", "c7a8"])[0].StrictHash, row.Strict);
        Assert.Empty(BookExercises.SignaturesOf(fork, "d5c7 e8d7 c7a8", 100));   // nicht klar gewinnend
        Assert.Empty(BookExercises.SignaturesOf(fork, "d5c7 e8d7 c7a8", null));
        Assert.Empty(BookExercises.SignaturesOf(fork, "a1a8", 600));              // illegale Lösung
        Assert.Empty(BookExercises.SignaturesOf("kaputt", "h6g7", null));
        Assert.Empty(BookExercises.SignaturesOf(QueenPawn, "", null));
    }

    [Theory]
    [InlineData(EbookSolutionCheck.Confirmed, "book", true, true)]
    [InlineData(EbookSolutionCheck.Replaced, "book", true, true)]
    [InlineData(EbookSolutionCheck.Engine, "engine", false, true)]
    [InlineData(EbookSolutionCheck.Confirmed, "engine", false, true)]
    [InlineData(EbookSolutionCheck.Doubtful, "book", false, false)]
    [InlineData(EbookSolutionCheck.Unchecked, "book", false, false)]
    [InlineData(EbookSolutionCheck.NoPuzzle, null, false, false)]
    public void Solvable_OnlyConfirmedBookSolution(EbookSolutionCheck check, string? source, bool solvable, bool usable)
    {
        Assert.Equal(solvable, BookExercises.IsSolvable(check, source));
        Assert.Equal(usable, BookExercises.IsUsable(check, source));
    }

    // 0.973.1: Schwarz am Zug, die Buchlinie ist die Partiefortsetzung bis zum Matt durch WEISS (a6, dann Dg7#).
    private const string QueenPawnBlackToMove = "6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1";

    [Fact]
    public async Task LoserToMoveInTheDiagram_ExerciseStartsWithTheOpponentMove_UndeterminableSideIsOnlyAnExample()
    {
        await SeedGameAsync();
        AddBook(5, 10);
        AddDiagram(5, QueenPawnBlackToMove, id: 501, solution: "a7a6 h6g7");      // der Gegner der Seite am Zug setzt matt
        AddDiagram(5, QueenPawn, id: 502, solution: "h6h5").EngineEval = null;    // bestätigt, aber kein Matt und keine Bewertung
        await _db.SaveChangesAsync();
        // Zeile für 502 von Hand (ohne Matt und ohne klare Bewertung rechnet der Lauf keine) — die Auswahl prüft selbst.
        var strict = (await _db.EbookDiagramSignatures.AsNoTracking().FirstAsync(x => x.DiagramId == 101)).Strict;
        var family = (await _db.EbookDiagramSignatures.AsNoTracking().FirstAsync(x => x.DiagramId == 101)).Family;
        foreach (var (id, fen, line) in new[] { (501, QueenPawnBlackToMove, "a7a6 h6g7"), (502, QueenPawn, "h6h5") })
            _db.EbookDiagramSignatures.Add(new EbookDiagramSignature
            {
                DiagramId = id, Kind = PuzzleSignatureKind.Mate, Version = MatePattern.Version, Strict = strict, Family = family, EbookId = 5,
                Solvable = true, LineHash = BookExercises.LineHash(fen, line), Shuffle = BookExercises.ShuffleOf(id), MateIn = 1,
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var seen = new Dictionary<int, RookHub.Api.DTOs.Puzzles.GameExerciseDto>();
        // Je Stelle höchstens sechs, fest gesät: beide Diagramme über die Kandidatenliste direkt prüfen.
        var o = GameExercises.Group(GameExercises.Occurrences(await _db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == 1)
            .Select(p => new GameExercises.Position(p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson)).ToListAsync())).First(g => g.First.Ply == 10).First;
        var used = Enumerable.Range(100, 400).ToHashSet();                         // alle anderen Diagramme schon gezeigt
        foreach (var e in await new BookExercises(_db).PickAsync(o, 1500, User1, used, seed: 0)) seen[e.Id] = e;

        var turned = seen[501];
        Assert.True(turned.Book!.Solvable);
        Assert.True(turned.Book.OpponentFirst);                                    // Löser = Weiß, erst zieht Schwarz
        Assert.Equal(QueenPawnBlackToMove, turned.Fen);
        Assert.Equal(["a7a6", "h6g7"], turned.Moves);
        var unsure = seen[502];
        Assert.False(unsure.Book!.Solvable);                                       // gewinnende Seite nicht bestimmbar → nur Beispiel
        Assert.False(unsure.Book.OpponentFirst);
        Assert.Empty(unsure.Moves);
    }

    [Fact]
    public async Task RunBatch_SolvableColumn_FollowsTheSolverRule()
    {
        AddBook(1, null);
        AddDiagram(1, QueenPawn);                                                 // 1 Seite am Zug setzt matt
        AddDiagram(1, QueenPawnBlackToMove, solution: "a7a6 h6g7");               // 2 Gegner setzt matt → lösbar (Gegnerzug zuerst)
        AddDiagram(1, QueenPawnBlackToMove, solution: "a7a6 h6g7 g8h8");          // 3 Zug nach dem Matt: nicht regelgerecht
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        while (await BookExercises.RunBatchAsync(_db, null, 10) > 0) { }
        var rows = await _db.EbookDiagramSignatures.AsNoTracking().OrderBy(s => s.DiagramId).ToListAsync();
        Assert.Equal([(1, true), (2, true)], rows.Where(r => r.DiagramId < 3).Select(r => (r.DiagramId, r.Solvable)));
        Assert.DoesNotContain(rows, r => r.DiagramId == 3 && r.Solvable);
    }

    private int _diagram;
    private EbookDiagram AddDiagram(int ebookId, string fen, EbookSolutionCheck check = EbookSolutionCheck.Confirmed, string? source = "book",
        int page = 10, float? confidence = null, string? font = null, int? id = null, string solution = "h6g7")
    {
        var d = new EbookDiagram
        {
            Id = id ?? ++_diagram, EbookId = ebookId, Page = page, W = 0.3, H = 0.3, Fen = fen, SolutionUci = solution, SolutionSource = source,
            SolutionCheck = check, EngineEval = 9999, Confidence = confidence, FontFamily = font,
        };
        _db.EbookDiagrams.Add(d);
        return d;
    }

    private void AddBook(int id, int? groupId, int? targetElo = null, int? owner = null) =>
        _db.Ebooks.Add(new Ebook
        {
            Id = id, Title = $"Buch {id}", Author = $"Autor {id}", TargetMinElo = targetElo, OwnerUserId = owner,
            GroupAccess = groupId is int g ? [new EbookGroupAccess { EbookId = id, GroupId = g }] : [],
        });

    [Fact]
    public async Task RunBatch_InPortions_SkipsUnreadAndUnsureEngine_RepassAfterAWeek()
    {
        AddBook(1, null);
        AddDiagram(1, QueenPawn);                                                                   // 1 lösbar
        AddDiagram(1, QueenKnight, EbookSolutionCheck.Engine, "engine");                             // 2 Beispiel
        AddDiagram(1, QueenKnight, EbookSolutionCheck.Engine, "engine", confidence: 0.5f);           // 3 unsicher erkannt, nur Engine
        AddDiagram(1, QueenPawn, confidence: 0.5f);                                                  // 4 unsicher, aber Buchlösung bestätigt
        AddDiagram(1, QueenPawn, font: EbookDiagramVisibility.Unread);                               // 5 nicht lesbar
        AddDiagram(1, QueenPawn, EbookSolutionCheck.Doubtful);                                       // 6 fraglich
        _db.EbookDiagrams.Add(new EbookDiagram { Id = ++_diagram, EbookId = 1, Fen = QueenPawn });   // 7 ohne Lösung
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var t0 = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(4, await BookExercises.RunBatchAsync(_db, null, 4, now: t0));
        Assert.Equal(4, await BookExercises.CursorAsync(_db));
        Assert.Equal(2, await BookExercises.RunBatchAsync(_db, null, 4, now: t0));   // 5, 6 (7 hat keine Lösung)
        Assert.Equal(0, await BookExercises.RunBatchAsync(_db, null, 4, now: t0));
        var rows = await _db.EbookDiagramSignatures.OrderBy(s => s.DiagramId).ToListAsync();
        Assert.Equal([1, 2, 4], rows.Select(r => r.DiagramId));
        Assert.Equal([true, false, true], rows.Select(r => r.Solvable));
        Assert.All(rows, r => Assert.Equal(1, r.EbookId));

        // Lösung von 1 verworfen: erst der nächste Durchgang (nach einer Woche) räumt die Zeile ab.
        var d1 = await _db.EbookDiagrams.FirstAsync(d => d.Id == 1);
        d1.SolutionCheck = EbookSolutionCheck.Doubtful;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        Assert.Equal(0, await BookExercises.RunBatchAsync(_db, null, 10, now: t0.AddDays(1)));
        Assert.Equal(6, await BookExercises.CursorAsync(_db));
        Assert.Equal(0, await BookExercises.RunBatchAsync(_db, null, 10, now: t0.AddDays(8)));   // setzt zurück
        Assert.Equal(0, await BookExercises.CursorAsync(_db));
        Assert.Equal(6, await BookExercises.RunBatchAsync(_db, null, 10, now: t0.AddDays(8)));
        Assert.Equal([2, 4], await _db.EbookDiagramSignatures.OrderBy(s => s.DiagramId).Select(s => s.DiagramId).ToListAsync());
    }

    // Partie wie in GameExercisesTests: Weiß (1550) verpasst Dg7# (Bauer f6 deckt), später Matt in der Ecke.
    private async Task SeedGameAsync()
    {
        _db.SavedGames.Add(new SavedGame { Id = 7, UserId = 1, Pgn = "", WhiteElo = 1550, BlackElo = 1500, GameAnalysisId = 1, ShareToken = "tok7" });
        var n = 0;
        foreach (var (ply, fen, move, cands) in new[]
        {
            (10, "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", "a1b1", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]},{"uci":"h6h5","cp":800}]"""),
            (11, "6k1/5p1p/5P1Q/8/8/8/8/1K6 b - - 1 1", "g8h8", """[{"uci":"g8h8","cp":-900}]"""),
            (12, "7k/5p1p/5P1Q/8/8/8/8/1K6 w - - 2 2", "h6g7", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]}]"""),
        })
            _db.GameAnalysisPositions.Add(new GameAnalysisPosition { Id = ++n, GameAnalysisId = 1, Ply = ply, Fen = fen, GameMoveUci = move, GameMoveSan = "x", CandidatesJson = cands });
        var pid = 0;
        foreach (var r in new[] { 1500, 1550, 1600 })
            _db.Puzzles.Add(new Puzzle { Id = ++pid, LichessId = $"x{pid}", Fen = "6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", Moves = "a7a6 h6g7", Rating = r, Popularity = 95, NbPlays = 1000, Themes = "mate mateIn1" });

        // Gruppen: „Alle“ (10) und eine Vereinsgruppe (11) mit Nutzer 1; Gruppe 12 ohne ihn.
        _db.Groups.AddRange(new Group { Id = 10, Name = "Everyone", IsEveryone = true }, new Group { Id = 11, Name = "Verein" }, new Group { Id = 12, Name = "Andere" });
        _db.UserGroups.Add(new UserGroup { UserId = 1, GroupId = 11 });
        AddBook(1, 11, targetElo: 1400);                     // Vereinsbuch, passt zur Stärke
        AddDiagram(1, QueenPawn, id: 101, page: 30);                                         // streng, lösbar
        AddDiagram(1, QueenKnight, EbookSolutionCheck.Replaced, id: 102, page: 31);          // Familie, lösbar
        AddDiagram(1, QueenPawn, EbookSolutionCheck.Engine, "engine", id: 103, page: 33);    // streng, nur Beispiel
        AddBook(2, 12, owner: 3);                            // fremdes Buch
        AddDiagram(2, QueenPawn, id: 201);
        AddBook(3, 10);                                      // für alle, aber gemeldet bzw. fraglich
        AddDiagram(3, QueenPawn, id: 301);
        AddDiagram(3, QueenPawn, EbookSolutionCheck.Doubtful, id: 302);
        AddBook(4, 10);                                      // für alle
        AddDiagram(4, QueenKnight, id: 401);
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "u", Email = "u@test.invalid", PasswordHash = "x" });
        _db.EbookDiagramReports.Add(new EbookDiagramReport { EbookId = 3, DiagramId = 301, UserId = 1, Page = 10, Fen = QueenPawn, CreatedAt = new DateTime(2026, 11, 1) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        while (await PuzzleSignatureWorker.RunBatchAsync(_db, 100) > 0) { }
        while (await BookExercises.RunBatchAsync(_db, null, 100) > 0) { }
    }

    private GameExercises Service() => new(_db, new EbookSuggestions(_db));
    private static EbookViewer User1 => new(1, false);

    [Fact]
    public async Task Books_BeforeLichess_StrictThenFamily_SolvableBeforeExample_AtMostSix_Stable()
    {
        await SeedGameAsync();
        var dto = (await Service().ForOwnGameAsync(1, 7, default, User1))!;
        var g = dto.Groups.First(x => x.Ply == 10);
        Assert.Equal(GameExercises.PerGroup, g.Exercises.Count);
        var books = g.Exercises.TakeWhile(e => e.Source == "book").ToList();
        Assert.Equal([101, 102, 401, 103], books.Select(e => e.Id));          // streng vor Familie, lösbar vor Beispiel
        Assert.All(g.Exercises.Skip(books.Count), e => Assert.Equal("lichess", e.Source));
        Assert.Equal(["strict", "family", "family", "strict"], books.Select(e => e.Match));

        var solvable = books[0];
        Assert.Equal(QueenPawn, solvable.Fen);
        Assert.Equal(["h6g7"], solvable.Moves);                               // Löser zieht zuerst
        Assert.Equal(("Buch 1", "Autor 1", 30, true, true), (solvable.Book!.Title, solvable.Book.Author, solvable.Book.Page, solvable.Book.PdfPage, solvable.Book.Solvable));
        var example = books[3];
        Assert.False(example.Book!.Solvable);
        Assert.Empty(example.Moves);

        // Matt in der Ecke: kein Buch, keine Lichess-Aufgabe → keine Stelle (nichts aufgefüllt).
        Assert.DoesNotContain(dto.Groups, x => x.Ply == 12);

        var again = (await Service().ForOwnGameAsync(1, 7, default, User1))!;
        Assert.Equal(dto.Groups.SelectMany(x => x.Exercises).Select(e => (e.Source, e.Id)), again.Groups.SelectMany(x => x.Exercises).Select(e => (e.Source, e.Id)));
    }

    [Fact]
    public async Task Rights_LikeLibrary_NoAccessNoBook_AnonymousOnlyEveryone_AdminAll_NoViewerOnlyLichess()
    {
        await SeedGameAsync();
        static List<int> BookIds(DTOs.Puzzles.GameExercisesDto dto) =>
            dto.Groups.SelectMany(g => g.Exercises).Where(e => e.Source == "book").Select(e => e.Id).OrderBy(i => i).ToList();

        var user = BookIds((await Service().ForOwnGameAsync(1, 7, default, User1))!);
        Assert.DoesNotContain(201, user);                                     // fremdes Buch: weder Stellung noch Titel
        var anon = (await Service().ForSharedOwnGameAsync("tok7", default, new EbookViewer(EbookCaller.Anonymous(null), false)))!;
        Assert.Equal([401], BookIds(anon));                                   // nur „für alle“ freigegeben
        Assert.DoesNotContain(anon.Groups.SelectMany(g => g.Exercises), e => e.Book?.Title == "Buch 1");
        // Ein Admin-Recht zählt ohne Anmeldung nicht.
        Assert.Equal([401], BookIds((await Service().ForSharedOwnGameAsync("tok7", default, new EbookViewer(EbookCaller.Anonymous(null), true)))!));
        Assert.Contains(201, BookIds((await Service().ForOwnGameAsync(1, 7, default, new EbookViewer(1, true)))!));
        Assert.Empty(BookIds((await Service().ForOwnGameAsync(1, 7))!));      // ohne Betrachter: nur Lichess
    }

    [Fact]
    public async Task Controller_SharedLink_AnonymousOnlyEveryone_SignedInAdminAll()
    {
        await SeedGameAsync();
        static async Task<List<int>> Call(ClaimsPrincipal user, GameExercises svc)
        {
            var c = new GameSummaryController(null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } } };
            var dto = Assert.IsType<DTOs.Puzzles.GameExercisesDto>(Assert.IsType<OkObjectResult>(await c.SharedExercises("tok7", svc, default)).Value);
            return dto.Groups.SelectMany(g => g.Exercises).Where(e => e.Source == "book").Select(e => e.Id).OrderBy(i => i).ToList();
        }
        Assert.Equal([401], await Call(new ClaimsPrincipal(new ClaimsIdentity()), Service()));
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin")], "test"));
        Assert.Contains(201, await Call(admin, Service()));
    }

    [Fact]
    public async Task Quality_ReportedUntilCorrected_ChangedDiagramSkipped()
    {
        await SeedGameAsync();
        var viewer = new EbookViewer(1, true);
        static IEnumerable<int> Ids(DTOs.Puzzles.GameExercisesDto dto) => dto.Groups.SelectMany(g => g.Exercises).Where(e => e.Source == "book").Select(e => e.Id);
        var before = Ids((await Service().ForOwnGameAsync(1, 7, default, viewer))!).ToList();
        Assert.DoesNotContain(301, before);   // gemeldet
        Assert.DoesNotContain(302, before);   // fraglich: gar nicht berechnet

        _db.EbookDiagramCorrections.Add(new EbookDiagramCorrection { EbookId = 3, DiagramId = 301, Page = 10, OldFen = QueenPawn, NewFen = QueenPawn, CreatedAt = new DateTime(2026, 11, 2) });
        var d101 = await _db.EbookDiagrams.FirstAsync(d => d.Id == 101);
        d101.Fen = QueenKnight;               // korrigiert, Signatur noch alt → übersprungen bis zum nächsten Lauf
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var after = Ids((await Service().ForOwnGameAsync(1, 7, default, viewer))!).ToList();
        Assert.Contains(301, after);
        Assert.DoesNotContain(101, after);
    }

    [Fact]
    public void Choose_DistinctBooksFirst_SecondFromSameChapter_ExamplesCapped()
    {
        BookExercises.Candidate C(int id, int book, string? chapter, bool solvable = true, int page = 10, int? elo = null) =>
            new(id, book, id, chapter, page, true, null, QueenPawn, "h6g7", solvable ? new BookSolverLine(["h6g7"], false) : null, $"B{book}", null, elo);
        var picked = BookExercises.Choose(
        [
            C(1, 1, "Kap. 1"), C(2, 1, "Kap. 2"), C(3, 1, "Kap. 1"),
            C(4, 2, null, page: 50), C(5, 2, null, page: 90), C(6, 2, null, page: 55),
            C(7, 3, null, solvable: false), C(8, 4, null, solvable: false), C(9, 5, null, solvable: false),
        ], 1500, 6, BookExercises.MaxExamples, seed: 42);
        Assert.Equal(6, picked.Count);
        Assert.Equal(2, picked.Count(c => !c.Solvable));                       // höchstens zwei Beispiele
        Assert.Equal([1, 3], picked.Where(c => c.EbookId == 1).Select(c => c.DiagramId).Order());   // dasselbe Kapitel
        Assert.Equal([4, 6], picked.Where(c => c.EbookId == 2).Select(c => c.DiagramId).Order());   // ±10 Seiten
        // Passende Spielstärke zuerst.
        var fit = BookExercises.Choose([C(1, 1, null, elo: 2300), C(2, 2, null, elo: 1400)], 1500, 1, 2, seed: 1);
        Assert.Equal(2, Assert.Single(fit).EbookId);
    }
}
