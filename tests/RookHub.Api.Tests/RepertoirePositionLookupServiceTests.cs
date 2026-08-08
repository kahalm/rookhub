using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoirePositionLookupServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly RepertoirePositionLookupService _svc;

    public RepertoirePositionLookupServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _svc = new RepertoirePositionLookupService(new RepertoireLineSource(_db, _cache), _cache);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    private async Task<int> AddUserAsync(string name)
    {
        var u = new AppUser { Username = name, Email = name + "@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> AddRepertoireAsync(int ownerId, string name, string pgn)
    {
        var rep = new Repertoire { UserId = ownerId, Name = name, Kind = RepertoireKind.Opening };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireFiles.Add(new RepertoireFile
        {
            RepertoireId = rep.Id,
            FileName = "rep.pgn",
            PgnContent = pgn,
            FileSize = pgn.Length,
        });
        await _db.SaveChangesAsync();
        return rep.Id;
    }

    private async Task ShareAsync(int repertoireId, int ownerId, int recipientId)
    {
        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = repertoireId, OwnerId = ownerId, RecipientId = recipientId });
        await _db.SaveChangesAsync();
    }

    /// <summary>Baut die normalisierte FEN nach einer SAN-Zugfolge — robust gegen FEN-Tippfehler im Test.</summary>
    private static string FenAfter(params string[] sans)
    {
        var b = new ChessBoard();
        foreach (var s in sans) b.Move(s);
        return b.ToFen();
    }

    private const string SicilianPgn =
        "[Event \"Repertoire\"]\n[White \"Open Sicilian: 2...d6\"]\n[Black \"Sicilian Defence\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 cxd4 *\n";

    [Fact]
    public async Task Lookup_PositionOnMainline_ReturnsRepertoireChapterLineAndPly()
    {
        var user = await AddUserAsync("u1");
        await AddRepertoireAsync(user, "My Sicilian", SicilianPgn);

        // Stellung nach 1.e4 c5 2.Nf3 (3 Halbzüge).
        var res = await _svc.LookupAsync(user, FenAfter("e4", "c5", "Nf3"), CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        Assert.Equal("My Sicilian", rep.RepertoireName);
        Assert.Equal("Opening", rep.Kind);
        Assert.False(rep.Shared);
        var line = Assert.Single(rep.Lines);
        Assert.Equal("Sicilian Defence", line.Chapter);          // [Black]
        Assert.Equal("Open Sicilian: 2...d6", line.LineName);      // [White]
        Assert.Equal(0, line.GameIndex);
        Assert.Equal(3, line.Ply);
    }

    [Fact]
    public async Task Lookup_IncludesRepertoiresSharedWithMe_FlaggedShared()
    {
        var me = await AddUserAsync("me");
        var owner = await AddUserAsync("owner");
        var repId = await AddRepertoireAsync(owner, "Owner's Sicilian", SicilianPgn);
        await ShareAsync(repId, owner, me);

        var res = await _svc.LookupAsync(me, FenAfter("e4", "c5", "Nf3"), CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        Assert.Equal("Owner's Sicilian", rep.RepertoireName);
        Assert.True(rep.Shared);
        Assert.Single(rep.Lines);
    }

    [Fact]
    public async Task Lookup_UnknownPosition_ReturnsEmpty()
    {
        var user = await AddUserAsync("u2");
        await AddRepertoireAsync(user, "My Sicilian", SicilianPgn);

        // Eine Caro-Kann-Stellung, die im Sizilianisch-Repertoire nicht vorkommt.
        var res = await _svc.LookupAsync(user, FenAfter("e4", "c6", "d4", "d5"), CancellationToken.None);

        Assert.Empty(res.Repertoires);
    }

    [Fact]
    public async Task Lookup_Transposition_MatchesRegardlessOfMoveOrder()
    {
        var user = await AddUserAsync("u3");
        // Linie über 1.Nf3 c5 2.e4 — transponiert in dieselbe Stellung wie 1.e4 c5 2.Nf3.
        var pgn = "[Event \"Repertoire\"]\n[White \"Transpo\"]\n[Black \"Sicilian\"]\n\n1. Nf3 c5 2. e4 d6 *\n";
        await AddRepertoireAsync(user, "Move-order Rep", pgn);

        var res = await _svc.LookupAsync(user, FenAfter("e4", "c5", "Nf3"), CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        var line = Assert.Single(rep.Lines);
        Assert.Equal("Transpo", line.LineName);
    }

    [Fact]
    public async Task Lookup_OnlyReturnsOwnRepertoires()
    {
        var mine = await AddUserAsync("mine");
        var other = await AddUserAsync("other");
        await AddRepertoireAsync(other, "Someone else's", SicilianPgn);

        var res = await _svc.LookupAsync(mine, FenAfter("e4", "c5", "Nf3"), CancellationToken.None);

        Assert.Empty(res.Repertoires);
    }

    [Fact]
    public async Task Lookup_PositionInVariation_ReturnsLineWithPlyMinusOne()
    {
        var user = await AddUserAsync("u4");
        // Hauptlinie 1.e4 e5; Variante (1...c5 2.Nf3) hängt am ersten Zug.
        var pgn = "[Event \"Repertoire\"]\n[White \"e4 with sidelines\"]\n[Black \"Open Games\"]\n\n1. e4 e5 (1... c5 2. Nf3 d6) 2. Nf3 Nc6 *\n";
        await AddRepertoireAsync(user, "e4 Rep", pgn);

        // Stellung nach 1.e4 c5 2.Nf3 kommt NUR in der Variante vor.
        var res = await _svc.LookupAsync(user, FenAfter("e4", "c5", "Nf3"), CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        var line = Assert.Single(rep.Lines);
        Assert.Equal(-1, line.Ply);
    }

    // ===== [FEN]-Header: Linien, die NICHT in der Grundstellung beginnen =====
    // piratechess schreibt je Chessable-Linie die Startstellung der Variante als [FEN]-Header.
    // Wird der ignoriert, ist der erste Zug aus der Grundstellung illegal und die ganze Linie
    // fehlt still im Index (Prod: ~7.700 solche Linien).

    private const string FenHeaderPgn =
        "[Event \"Kurs\"]\n[White \"Turmendspiel\"]\n[Black \"Endspiele\"]\n" +
        "[FEN \"8/8/8/8/8/5k2/4p3/4K2R w K - 0 1\"]\n\n1. Rh3+ Kg2 2. Rh8 *\n";

    [Fact]
    public async Task Lookup_LineStartingFromFenHeader_IsFound()
    {
        var user = await AddUserAsync("f1");
        await AddRepertoireAsync(user, "Endspiel-Rep", FenHeaderPgn);

        // Stellung nach 1.Rh3+ — nur erreichbar, wenn die [FEN]-Startstellung benutzt wird.
        var board = ChessBoard.LoadFromFen("8/8/8/8/8/5k2/4p3/4K2R w K - 0 1");
        board.Move("Rh3+");

        var res = await _svc.LookupAsync(user, board.ToFen(), CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        var line = Assert.Single(rep.Lines);
        Assert.Equal("Turmendspiel", line.LineName);
        Assert.Equal("Endspiele", line.Chapter);
    }

    [Fact]
    public async Task Lookup_FenHeaderStartPositionItself_IsFound()
    {
        var user = await AddUserAsync("f2");
        await AddRepertoireAsync(user, "Endspiel-Rep", FenHeaderPgn);

        var res = await _svc.LookupAsync(user, "8/8/8/8/8/5k2/4p3/4K2R w K - 0 1", CancellationToken.None);

        Assert.Single(res.Repertoires);
    }

    [Fact]
    public async Task Tree_LineStartingFromFenHeader_ReturnsContinuation()
    {
        var user = await AddUserAsync("f3");
        await AddRepertoireAsync(user, "Endspiel-Rep", FenHeaderPgn);

        var res = await _svc.TreeAsync(user, "8/8/8/8/8/5k2/4p3/4K2R w K - 0 1", 0, CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        var rh3 = Assert.Single(rep.Moves);
        Assert.Equal("Rh3", rh3.San);          // Tokenizer strippt das '+'
    }

    [Fact]
    public async Task Lookup_EmptyFenHeader_FallsBackToStartPosition()
    {
        var user = await AddUserAsync("f5");
        // piratechess schreibt fuer Linien aus der Grundstellung ein LEERES [FEN ""] (PirateChessLib.cs:322)
        // — in Prod ~34 mal. Das darf nicht als unbrauchbare FEN gelten (Linie waere sonst weg),
        // sondern muss auf die Grundstellung zurueckfallen.
        var pgn = "[Event \"Kurs\"]\n[White \"Leerer Header\"]\n[Black \"Kapitel\"]\n[FEN \"\"]\n\n1. e4 c5 2. Nf3 *\n";
        await AddRepertoireAsync(user, "Rep", pgn);

        var res = await _svc.LookupAsync(user, FenAfter("e4", "c5", "Nf3"), CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        Assert.Equal("Leerer Header", Assert.Single(rep.Lines).LineName);
    }

    [Fact]
    public async Task Lookup_EmptyFenHeader_DoesNotIndexTheStartPositionAsAMatch()
    {
        var user = await AddUserAsync("f6");
        // Gegenprobe zur Regel "Startstellung nur bei EIGENER [FEN] mitindizieren": ein leerer
        // Header ist keine eigene Startstellung — sonst matchte dieses Repertoire auf die
        // Grundstellung und damit auf jede frische Partie.
        var pgn = "[Event \"Kurs\"]\n[White \"Leerer Header\"]\n[Black \"Kapitel\"]\n[FEN \"\"]\n\n1. e4 c5 *\n";
        await AddRepertoireAsync(user, "Rep", pgn);

        var res = await _svc.LookupAsync(user, new ChessBoard().ToFen(), CancellationToken.None);

        Assert.Empty(res.Repertoires);
    }

    [Fact]
    public async Task Lookup_UnusableFenHeader_SkipsLineInsteadOfIndexingWrongPositions()
    {
        var user = await AddUserAsync("f4");
        // Kaputte FEN: die Linie darf NICHT ersatzweise aus der Grundstellung gespielt werden.
        var pgn = "[Event \"Kurs\"]\n[White \"Kaputt\"]\n[Black \"X\"]\n[FEN \"nonsense\"]\n\n1. e4 e5 *\n";
        await AddRepertoireAsync(user, "Rep", pgn);

        var res = await _svc.LookupAsync(user, FenAfter("e4"), CancellationToken.None);

        Assert.Empty(res.Repertoires);
    }

    // ===== Baummodus =====

    [Fact]
    public async Task Tree_FromMainlinePosition_ReturnsFollowingMoves()
    {
        var user = await AddUserAsync("t1");
        await AddRepertoireAsync(user, "My Sicilian", SicilianPgn);   // 1.e4 c5 2.Nf3 d6 3.d4 cxd4

        var res = await _svc.TreeAsync(user, FenAfter("e4", "c5", "Nf3"), 0, CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        Assert.Equal("My Sicilian", rep.RepertoireName);
        Assert.Equal(1, rep.Occurrences);
        Assert.False(rep.Truncated);
        var d6 = Assert.Single(rep.Moves);
        Assert.Equal("d6", d6.San);
        Assert.Equal(1, d6.Count);
        var d4 = Assert.Single(d6.Children);
        Assert.Equal("d4", d4.San);
        var cxd4 = Assert.Single(d4.Children);
        Assert.Equal("cxd4", cxd4.San);
        Assert.True(cxd4.IsEnd);                                       // Linie endet hier
    }

    [Fact]
    public async Task Tree_MergesBranchesAcrossLines_AndCountsPaths()
    {
        var user = await AddUserAsync("t2");
        // Zwei Linien mit gemeinsamem Anfang, die nach 2...d6 bzw. 2...Nc6 auseinanderlaufen.
        var pgn =
            "[Event \"R\"]\n[White \"Najdorf\"]\n[Black \"Sicilian\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 *\n\n" +
            "[Event \"R\"]\n[White \"Sveshnikov\"]\n[Black \"Sicilian\"]\n\n1. e4 c5 2. Nf3 Nc6 3. d4 *\n";
        await AddRepertoireAsync(user, "Sicilian Rep", pgn);

        var res = await _svc.TreeAsync(user, FenAfter("e4", "c5", "Nf3"), 0, CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        Assert.Equal(2, rep.Occurrences);
        Assert.Equal(2, rep.Moves.Count);
        Assert.Equal(new[] { "d6", "Nc6" }, rep.Moves.Select(m => m.San).ToArray());
        Assert.All(rep.Moves, m => Assert.Equal(1, m.Count));
        // Jeder Zweig ist eindeutig EINER Linie zuzuordnen → Kapitel/Linie fürs „Trainieren".
        Assert.Equal("Najdorf", rep.Moves[0].LineName);
        Assert.Equal("Sicilian", rep.Moves[0].Chapter);
        Assert.Equal(0, rep.Moves[0].GameIndex);
        Assert.Equal("Sveshnikov", rep.Moves[1].LineName);
        Assert.Equal(1, rep.Moves[1].GameIndex);
    }

    [Fact]
    public async Task Tree_SharedPrefixFromTwoLines_IsAmbiguous_NoLineReference()
    {
        var user = await AddUserAsync("t3");
        // Beide Linien spielen 2...d6, trennen sich erst im 4. Zug.
        var pgn =
            "[Event \"R\"]\n[White \"A\"]\n[Black \"Sicilian\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 *\n\n" +
            "[Event \"R\"]\n[White \"B\"]\n[Black \"Sicilian\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Qxd4 Nc6 *\n";
        await AddRepertoireAsync(user, "Sicilian Rep", pgn);

        var res = await _svc.TreeAsync(user, FenAfter("e4", "c5", "Nf3"), 0, CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        var d6 = Assert.Single(rep.Moves);
        Assert.Equal(2, d6.Count);                                     // beide Linien laufen hier durch
        Assert.Null(d6.LineName);                                      // mehrdeutig → keine Linien-Referenz
        Assert.Null(d6.GameIndex);
        var cxd4 = Assert.Single(Assert.Single(d6.Children).Children); // 3.d4 → 3...cxd4
        Assert.Equal(2, cxd4.Children.Count);                          // 4.Nxd4 / 4.Qxd4
        Assert.Equal("A", cxd4.Children[0].LineName);                  // ab hier wieder eindeutig
        Assert.Equal("B", cxd4.Children[1].LineName);
    }

    [Fact]
    public async Task Tree_IncludesVariations_NotJustMainline()
    {
        var user = await AddUserAsync("t4");
        // Nach 1.e4 c5 2.Nf3 stehen Hauptzug d6 und die Variante Nc6 zur Wahl.
        var pgn = "[Event \"R\"]\n[White \"Open Sicilian\"]\n[Black \"Sicilian\"]\n\n1. e4 c5 2. Nf3 d6 (2... Nc6 3. Bb5) 3. d4 *\n";
        await AddRepertoireAsync(user, "e4 Rep", pgn);

        var res = await _svc.TreeAsync(user, FenAfter("e4", "c5", "Nf3"), 0, CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        Assert.Equal(new[] { "d6", "Nc6" }, rep.Moves.Select(m => m.San).ToArray());
        Assert.Equal("Bb5", Assert.Single(rep.Moves[1].Children).San);
    }

    [Fact]
    public async Task Tree_RespectsMaxDepth()
    {
        var user = await AddUserAsync("t5");
        await AddRepertoireAsync(user, "My Sicilian", SicilianPgn);   // ab der Stellung: d6, d4, cxd4

        var res = await _svc.TreeAsync(user, FenAfter("e4", "c5", "Nf3"), 2, CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        var d6 = Assert.Single(rep.Moves);
        var d4 = Assert.Single(d6.Children);
        Assert.Empty(d4.Children);                                     // 3...cxd4 liegt jenseits der Tiefe
    }

    [Fact]
    public async Task Tree_StartPosition_ReturnsFirstMoves()
    {
        var user = await AddUserAsync("t6");
        await AddRepertoireAsync(user, "My Sicilian", SicilianPgn);

        var res = await _svc.TreeAsync(user, FenAfter(), 0, CancellationToken.None);   // Grundstellung

        var rep = Assert.Single(res.Repertoires);
        Assert.Equal("e4", Assert.Single(rep.Moves).San);
    }

    [Fact]
    public async Task Tree_UnknownPosition_ReturnsEmpty()
    {
        var user = await AddUserAsync("t7");
        await AddRepertoireAsync(user, "My Sicilian", SicilianPgn);

        var res = await _svc.TreeAsync(user, FenAfter("e4", "c6", "d4", "d5"), 0, CancellationToken.None);

        Assert.Empty(res.Repertoires);
    }

    [Fact]
    public async Task Tree_OnlyReturnsReadableRepertoires()
    {
        var mine = await AddUserAsync("t8");
        var other = await AddUserAsync("t8other");
        await AddRepertoireAsync(other, "Someone else's", SicilianPgn);

        var res = await _svc.TreeAsync(mine, FenAfter("e4", "c5", "Nf3"), 0, CancellationToken.None);

        Assert.Empty(res.Repertoires);
    }

    [Fact]
    public async Task Tree_SharedRepertoire_IsFlaggedShared()
    {
        var me = await AddUserAsync("t9");
        var owner = await AddUserAsync("t9owner");
        var repId = await AddRepertoireAsync(owner, "Owner's Sicilian", SicilianPgn);
        await ShareAsync(repId, owner, me);

        var res = await _svc.TreeAsync(me, FenAfter("e4", "c5", "Nf3"), 0, CancellationToken.None);

        var rep = Assert.Single(res.Repertoires);
        Assert.True(rep.Shared);
    }
}
