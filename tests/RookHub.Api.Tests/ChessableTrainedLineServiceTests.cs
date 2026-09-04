using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Auf Chessable trainiert → in RookHub gelernt": Kurs-Linie als gelöst markieren (idempotent,
/// ohne Fake-Versuch) + Repertoire-SR nachziehen (neu → gelernt, fällig → +1 Stufe, nicht
/// fällig/pausiert → unangetastet). Dazu der SERVERSEITIGE SPIEGEL des Frontend-Linien-Schlüssels
/// (repertoire-line-key.util.ts) — die Vektoren stammen aus der Original-JS-Implementierung.
/// </summary>
public class ChessableTrainedLineServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChessableTrainedLineService _svc;

    public ChessableTrainedLineServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new ChessableTrainedLineService(_db, new RepertoireTrainingService(_db));
    }

    public void Dispose() => _db.Dispose();

    // ===== Hash-Spiegel (Vektoren aus repertoire-line-key.util.ts via Node erzeugt) ==========

    [Theory]
    [InlineData("l2eac1aan9n2", new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" })]
    [InlineData("lgaojnnug81", new[] { "d4", "d5", "c4", "e6", "Nc3", "Nf6", "Bg5", "Be7" })]
    [InlineData("l1pzvjpxtqs9", new[] { "e4", "c6", "d4", "d5", "exd5", "cxd5" })]
    [InlineData("l1sv2o9a142o", new[] { "e4", "e6", "d4", "d5", "Nd2", "c5", "exd5", "Qxd5+" })]
    [InlineData("l65y1fu91w7", new[] { "O-O", "O-O-O" })]
    [InlineData("lbve4kkccho", new[] { "e4" })]
    // Chessable-Schreibweisen: Umwandlung OHNE "=" und Rochade mit Nullen müssen denselben
    // Schlüssel ergeben wie die chess.js-Form, aus der das Frontend seine Keys bildet.
    [InlineData("l3oj25ls9tj", new[] { "e4", "d5", "exd5", "c6", "dxc6", "b5", "c7", "a5", "c8=Q" })]
    [InlineData("l3oj25ls9tj", new[] { "e4", "d5", "exd5", "c6", "dxc6", "b5", "c7", "a5", "c8Q" })]
    [InlineData("l27uzjsul7qp", new[] { "a2", "b1=Q+" })]
    [InlineData("l27uzjsul7qp", new[] { "a2", "b1Q+" })]
    [InlineData("lf2l4fs48fe", new[] { "O-O" })]
    [InlineData("lf2l4fs48fe", new[] { "0-0" })]
    public void LineKey_MirrorsFrontendHash(string expected, string[] sans)
    {
        Assert.Equal(expected, ChessableTrainedLineService.LineKeyFromSans(sans));
    }

    // ===== Abschnitts-Fenster: nur die Linie mit dem oid, nicht das ganze PGN ================

    [Fact]
    public void MainlineForOid_PicksExactlyTheMarkedSection_InAMultiGamePgn()
    {
        // Statt das ganze PGN in alle [Event-Abschnitte zu zerlegen, wird das Fenster um den Marker
        // geschnitten. Das Ergebnis muss identisch sein — auch für die ERSTE und die LETZTE Linie,
        // und die Nachbar-Züge dürfen nicht mit hineinrutschen.
        const string pgn = """
[Event "Kurs"]
[White "Erste"]
[ChessableOid "1"]

1. e4 e5 *

[Event "Kurs"]
[White "Mitte"]
[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]
[ChessableOid "2"]

1. d4 d5 2. c4 *

[Event "Kurs"]
[White "Letzte"]
[ChessableOid "3"]

1. Nf3 Nf6 *
""";
        Assert.Equal(new[] { "e4", "e5" }, ChessableTrainedLineService.MainlineSansForOid(pgn, "1"));
        Assert.Equal(new[] { "d4", "d5", "c4" }, ChessableTrainedLineService.MainlineSansForOid(pgn, "2"));
        Assert.Equal(new[] { "Nf3", "Nf6" }, ChessableTrainedLineService.MainlineSansForOid(pgn, "3"));
        Assert.Null(ChessableTrainedLineService.MainlineSansForOid(pgn, "999"));

        // Die Start-FEN gehört zum FENSTER, nicht zum Nachbarn: nur Linie 2 hat einen FEN-Header.
        Assert.Null(ChessableTrainedLineService.MainlineForOid(pgn, "1").StartFen);
        Assert.NotNull(ChessableTrainedLineService.MainlineForOid(pgn, "2").StartFen);
        Assert.Null(ChessableTrainedLineService.MainlineForOid(pgn, "3").StartFen);
    }

    [Fact]
    public void MainlineForOid_DoesNotTreatEventDateAsASectionStart()
    {
        // Der frühere Split hing an `(?=\[Event\s)` — „[EventDate" ist KEIN Abschnittsanfang.
        // Das Fenster muss dieselbe Regel anwenden, sonst schneidet es mitten in die Header.
        const string pgn = """
[Event "Kurs"]
[EventDate "2026.09.04"]
[White "Linie"]
[ChessableOid "77"]

1. e4 c5 *
""";
        Assert.Equal(new[] { "e4", "c5" }, ChessableTrainedLineService.MainlineSansForOid(pgn, "77"));
    }

    // ===== Brett-Kanonisierung: Schreibweisen, die nur ÜBER DIE STELLUNG auflösbar sind =======
    // Die textuelle Normalisierung kann „Nf3e5" (lang algebraisch) nicht in „Nxe5" überführen —
    // dafür braucht es das Brett. Ohne diesen Schritt berechnete der Server einen ANDEREN Hash als
    // der Trainer, fand die Karte nicht und legte eine unerreichbare Phantom-Karte an.

    [Theory]
    // lang algebraisch mit Figur + Schlag-x, gemischt mit reiner UCI-Form und Bindestrich-Notation
    [InlineData(new[] { "e4", "e5", "Nf3", "Nc6", "Nxe5" }, new[] { "e2e4", "e7e5", "Ng1f3", "Nb8c6", "Nf3xe5" })]
    [InlineData(new[] { "d4", "d5", "Nc3" }, new[] { "d2-d4", "d7-d5", "Nb1-c3" })]
    // Schlag ohne „x" (kommt in Exporten vor) und eine Rochade mit Nullen im selben Zug
    [InlineData(new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "O-O" },
                new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "0-0" })]
    public void BoardCanonicalSans_YieldsTheKeyTheTrainerComputes(string[] chessJsSans, string[] rawTokens)
    {
        var canonical = ChessableTrainedLineService.BoardCanonicalSans(rawTokens);
        Assert.NotNull(canonical);
        Assert.Equal(chessJsSans, canonical);
        // Und damit derselbe Linien-Schlüssel wie im Frontend.
        Assert.Equal(ChessableTrainedLineService.LineKeyFromSans(chessJsSans),
                     ChessableTrainedLineService.LineKeyFromSans(canonical!));
    }

    [Fact]
    public void BoardCanonicalSans_UnresolvableLine_ReturnsNull_NotAPrefix()
    {
        // Ein nicht auflösbarer Zug darf KEIN kürzeres Präfix liefern — das wäre eine andere Linie
        // und damit ein Hash, der auf nichts zeigt. Dann bleibt es beim textuellen Schlüssel.
        Assert.Null(ChessableTrainedLineService.BoardCanonicalSans(new[] { "e4", "Qh5xz9" }));
        Assert.Null(ChessableTrainedLineService.BoardCanonicalSans(new[] { "e5" }));  // illegal im 1. Zug
    }

    [Fact]
    public void BoardCanonicalSans_HonoursTheFenHeaderOfTheSection()
    {
        // Eine Linie, die NICHT in der Grundstellung beginnt: ohne die Start-FEN wäre schon der
        // erste Zug illegal und die Kanonisierung fiele aus.
        const string pgn = """
[Event "Kurs"]
[FEN "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2"]
[ChessableOid "4242"]

1. Ng1f3 Nb8c6 *
""";
        var (sans, startFen) = ChessableTrainedLineService.MainlineForOid(pgn, "4242");
        Assert.NotNull(startFen);
        var canonical = ChessableTrainedLineService.BoardCanonicalSans(sans!, startFen);
        Assert.Equal(new[] { "Nf3", "Nc6" }, canonical);
    }

    // ===== Der ECHTE Pfad: PGN-Tokenizer -> Hash (nicht nur die Hilfsfunktionen) ==============
    // Regression: `IsMoveToken` prüfte das erste Zeichen, also fiel eine Rochade mit Nullen
    // ("0-0") still ganz aus der Zugliste — andere LÄNGE, komplett anderer Hash, Karte
    // unauffindbar. Der Vektor stammt aus der Frontend-Implementierung (chess.js liefert "O-O").

    private const string CastlingPgn = """
[Event "Kurs"]
[White "Rochade-Linie"]
[ChessableOid "777"]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. 0-0 Nf6 *
""";

    private const string CastlingPgnLetters = """
[Event "Kurs"]
[White "Rochade-Linie"]
[ChessableOid "777"]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. O-O Nf6 *
""";

    [Fact]
    public void MainlineSans_ZeroCastling_IsKeptAndCanonicalised()
    {
        var sans = ChessableTrainedLineService.MainlineSansForOid(CastlingPgn, "777")!;
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "O-O", "Nf6" }, sans);
    }

    [Fact]
    public void LineKey_ZeroCastlingAndLetterCastling_HashIdenticallyAndMatchFrontend()
    {
        var zeros = ChessableTrainedLineService.MainlineSansForOid(CastlingPgn, "777")!;
        var letters = ChessableTrainedLineService.MainlineSansForOid(CastlingPgnLetters, "777")!;

        Assert.Equal(ChessableTrainedLineService.LineKeyFromSans(letters),
                     ChessableTrainedLineService.LineKeyFromSans(zeros));
        // Vektor aus der Frontend-Implementierung (repertoire-line-key.util.ts) erzeugt.
        Assert.Equal("lcvspxwtgmj", ChessableTrainedLineService.LineKeyFromSans(zeros));
    }

    [Fact]
    public void MainlineSans_DropsEnPassantAnnotation()
    {
        // "e.p." beginnt mit 'e' und ginge als Zug durch — chess.js liefert es im Frontend nie,
        // die Liste wäre also einen Eintrag zu lang.
        var pgn = """
[Event "Kurs"]
[White "e.p."]
[ChessableOid "888"]

1. e4 d5 2. e5 f5 3. exf6 e.p. Nxf6 *
""";
        var sans = ChessableTrainedLineService.MainlineSansForOid(pgn, "888")!;
        Assert.DoesNotContain("e.p.", sans);
        Assert.Equal(new[] { "e4", "d5", "e5", "f5", "exf6", "Nxf6" }, sans);
    }

    [Theory]
    [InlineData("c8Q", "c8=Q")]
    [InlineData("c8q", "c8=Q")]
    [InlineData("c8=q", "c8=Q")]
    [InlineData("bxc8N+", "bxc8=N")]
    [InlineData("0-0", "O-O")]
    [InlineData("0-0-0", "O-O-O")]
    [InlineData("Nf3!", "Nf3")]
    [InlineData("Qxd5+", "Qxd5")]
    [InlineData("e4", "e4")]
    public void CanonicalSan_MatchesFrontendNormSan(string raw, string expected)
    {
        Assert.Equal(expected, ChessableTrainedLineService.CanonicalSan(raw));
    }

    // ===== PGN-Extraktion =====================================================================

    private const string RepPgn = """
[Event "Kurs"]
[White "Linie A"]
[ChessableOid "111"]

1. e4 e5 2. Nf3 (2. Nc3 Nf6) 2... Nc6 {Kommentar} 3. Bb5! *

[Event "Kurs"]
[White "Linie B"]
[ChessableOid "222"]

1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 1-0
""";

    [Fact]
    public void MainlineSansForOid_FindsGame_SkipsVariationsCommentsAnnotations()
    {
        var sans = ChessableTrainedLineService.MainlineSansForOid(RepPgn, "111");
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" }, sans);
        // Der Schlüssel der Mainline entspricht dem Frontend-Vektor derselben Zugfolge.
        Assert.Equal("l2eac1aan9n2", ChessableTrainedLineService.LineKeyFromSans(sans!));

        Assert.Equal(8, ChessableTrainedLineService.MainlineSansForOid(RepPgn, "222")!.Count);
        Assert.Null(ChessableTrainedLineService.MainlineSansForOid(RepPgn, "999"));
    }

    // ===== Kurs + SR ==========================================================================

    private async Task<AppUser> CreateUserAsync(string name = "u1")
    {
        var user = new AppUser { Username = name, PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<(Book Book, BookPuzzle Puzzle)> SeedBookAsync(int userId, string bid, string oid)
    {
        var file = $"chessable-u{userId}-{bid}.pgn";
        var book = new Book { FileName = file, DisplayName = "Kurs", OwnerUserId = userId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var bp = new BookPuzzle
        {
            LineId = $"{file}:1", BookFileName = file, BookId = book.Id, Round = "1",
            Fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1", Moves = "e2e3", StartPly = -1,
            ChessableOid = oid,
        };
        _db.BookPuzzles.Add(bp);
        await _db.SaveChangesAsync();
        return (book, bp);
    }

    private async Task<Repertoire> SeedRepertoireAsync(int userId, string bid, string pgn)
    {
        var rep = new Repertoire
        {
            UserId = userId, Name = "Rep", Kind = RepertoireKind.Opening,
            ChessableCourseId = bid, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireFiles.Add(new RepertoireFile
        {
            RepertoireId = rep.Id, FileName = $"chessable-{bid}.pgn",
            PgnContent = pgn, FileSize = pgn.Length,
        });
        await _db.SaveChangesAsync();
        return rep;
    }

    [Fact]
    public async Task MarkTrained_MarksCourseLineOnce_WithoutAttemptRow()
    {
        var user = await CreateUserAsync();
        var (book, bp) = await SeedBookAsync(user.Id, "500", "111");

        var first = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.True(first.CourseLineFound);
        Assert.True(first.CourseLineMarked);

        var second = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.True(second.CourseLineFound);
        Assert.False(second.CourseLineMarked);   // idempotent

        var result = Assert.Single(_db.CoursePuzzleResults.Where(r => r.UserId == user.Id));
        Assert.Equal(bp.Id, result.BookPuzzleId);
        Assert.Equal(book.Id, result.BookId);
        Assert.Empty(_db.CourseAttempts);        // kein 0-Sekunden-Fake-Versuch in der Statistik
    }

    [Fact]
    public async Task MarkTrained_LearnsNewRepertoireLine()
    {
        var user = await CreateUserAsync();
        var rep = await SeedRepertoireAsync(user.Id, "500", RepPgn);

        var res = await _svc.MarkTrainedAsync(user.Id, "500", "111");

        Assert.Equal(1, res.RepertoireLinesAdvanced);
        var card = Assert.Single(_db.RepertoireCardStates.Where(c => c.RepertoireId == rep.Id));
        Assert.Equal("l2eac1aan9n2", card.CardKey);
        Assert.Equal(1, card.Level);             // „einmal gelernt"
        Assert.True(card.InPool);
        Assert.True(card.DueAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task MarkTrained_LongAlgebraicPgn_AdvancesTheTrainersCard_NoPhantom()
    {
        // Das PGN schreibt lang algebraisch („Ng1f3"), der Trainer hat seine Karte aber unter dem
        // chess.js-Schlüssel („Nf3") liegen. Ohne Brett-Kanonisierung rechnete der Server einen
        // anderen Hash, fand die Karte nicht (⇒ „actionable") und legte eine ZWEITE, für den
        // Trainer unerreichbare Karte an: der sichtbare Fortschritt rückte nie vor.
        const string longAlgPgn = """
[Event "Kurs"]
[White "Lang algebraisch"]
[ChessableOid "911"]

1. e2e4 e7e5 2. Ng1f3 Nb8c6 3. Bf1b5 *
""";
        var user = await CreateUserAsync();
        var rep = await SeedRepertoireAsync(user.Id, "500", longAlgPgn);
        // Genau der Schlüssel, den das Frontend für diese Linie bildet (chess.js-SAN).
        var trainerKey = ChessableTrainedLineService.LineKeyFromSans(
            new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" });
        _db.RepertoireCardStates.Add(new RepertoireCardState
        {
            UserId = user.Id, RepertoireId = rep.Id, CardKey = trainerKey,
            Level = 3, InPool = true, DueAt = DateTime.UtcNow.AddHours(-1),
        });
        await _db.SaveChangesAsync();

        var res = await _svc.MarkTrainedAsync(user.Id, "500", "911");

        Assert.Equal(1, res.RepertoireLinesAdvanced);
        var card = Assert.Single(_db.RepertoireCardStates.Where(c => c.RepertoireId == rep.Id));
        Assert.Equal(trainerKey, card.CardKey);   // dieselbe Karte, keine Phantom-Karte daneben
        Assert.Equal(4, card.Level);              // eine Stufe vorgerückt
    }

    [Fact]
    public async Task MarkTrained_LongAlgebraicPgn_NewLine_UsesTheTrainersKey()
    {
        // Ohne vorhandene Karte muss die NEUE unter dem brett-kanonischen Schlüssel entstehen —
        // sonst sieht der Trainer die gerade gelernte Linie weiterhin als ungelernt.
        const string longAlgPgn = """
[Event "Kurs"]
[White "Lang algebraisch"]
[ChessableOid "912"]

1. e2e4 e7e5 2. Ng1f3 *
""";
        var user = await CreateUserAsync();
        var rep = await SeedRepertoireAsync(user.Id, "500", longAlgPgn);

        var res = await _svc.MarkTrainedAsync(user.Id, "500", "912");

        Assert.Equal(1, res.RepertoireLinesAdvanced);
        var card = Assert.Single(_db.RepertoireCardStates.Where(c => c.RepertoireId == rep.Id));
        Assert.Equal(ChessableTrainedLineService.LineKeyFromSans(new[] { "e4", "e5", "Nf3" }), card.CardKey);
    }

    [Fact]
    public async Task MarkTrained_AdvancesDueLine_ButLeavesNotDueAndPausedAlone()
    {
        var user = await CreateUserAsync();
        var rep = await SeedRepertoireAsync(user.Id, "500", RepPgn);
        var keyA = "l2eac1aan9n2";               // Linie A (oid 111)
        var keyB = ChessableTrainedLineService.LineKeyFromSans(
            ChessableTrainedLineService.MainlineSansForOid(RepPgn, "222")!);

        _db.RepertoireCardStates.AddRange(
            new RepertoireCardState { UserId = user.Id, RepertoireId = rep.Id, CardKey = keyA,
                Level = 3, InPool = true, DueAt = DateTime.UtcNow.AddHours(-1) },      // fällig
            new RepertoireCardState { UserId = user.Id, RepertoireId = rep.Id, CardKey = keyB,
                Level = 3, InPool = true, DueAt = DateTime.UtcNow.AddDays(3) });       // nicht fällig
        await _db.SaveChangesAsync();

        var resA = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.Equal(1, resA.RepertoireLinesAdvanced);
        Assert.Equal(4, _db.RepertoireCardStates.Single(c => c.CardKey == keyA).Level);  // +1 Stufe

        var resB = await _svc.MarkTrainedAsync(user.Id, "500", "222");
        Assert.Equal(0, resB.RepertoireLinesAdvanced);
        Assert.Equal(1, resB.RepertoireLinesSkipped);
        Assert.Equal(3, _db.RepertoireCardStates.Single(c => c.CardKey == keyB).Level);  // unangetastet

        // Pausierte Linie bleibt pausiert und rückt nicht vor.
        var cardA = _db.RepertoireCardStates.Single(c => c.CardKey == keyA);
        cardA.Paused = true; cardA.DueAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();
        var resPaused = await _svc.MarkTrainedAsync(user.Id, "500", "111");
        Assert.Equal(0, resPaused.RepertoireLinesAdvanced);
        Assert.Equal(4, _db.RepertoireCardStates.Single(c => c.CardKey == keyA).Level);
    }

    [Fact]
    public async Task MarkTrained_UnknownBidOrOid_IsHarmless()
    {
        var user = await CreateUserAsync();
        var res = await _svc.MarkTrainedAsync(user.Id, "12345", "999");
        Assert.False(res.CourseLineFound);
        Assert.Equal(0, res.RepertoireLinesAdvanced);
    }
}
