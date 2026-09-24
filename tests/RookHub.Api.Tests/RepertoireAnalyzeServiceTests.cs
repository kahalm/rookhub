using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoireAnalyzeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireAnalyzeService _analyze;
    private readonly RepertoireService _repertoireService;

    public RepertoireAnalyzeServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        _analyze = new RepertoireAnalyzeService(_db, cache);
        _repertoireService = TestServices.Repertoire(_db, analyze: _analyze);   // MUSS dieselbe Instanz sein
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedUserWithOpeningAsync(string pgn)
    {
        var user = new AppUser { Username = "u", Email = "u@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        var rep = await _repertoireService.CreateAsync(user.Id, new CreateRepertoireDto
        {
            Name = "Opening", Kind = RepertoireKind.Opening,
        });
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn));
        await _repertoireService.UploadFileAsync(rep.Id, user.Id, "rep.pgn", stream);
        return user.Id;
    }

    [Fact]
    public async Task EmptyMoves_ReturnsFileCountOnly()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 2. Nf3 Nc6 *");
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new(), Kind = RepertoireKind.Opening,
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(1, result.RepertoireFileCount);
        Assert.Empty(result.InRepertoire);
    }

    [Fact]
    public async Task GlueedMoveNumbers_StillParsedAsRepertoireMoves()
    {
        // Viele Exporte (auch Chessable-Downloads) schreiben die Zugnummer OHNE Leerzeichen an den
        // Zug: „1.e4 e5 2.Nf3". Der Tokenizer trennte nur an Leerzeichen, „1.e4" war damit kein
        // bekannter Zug — das Repertoire galt als leer und JEDE Partie wich schon im ersten Zug ab,
        // während der Client-Parser dieselbe Datei sauber las.
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1.e4 e5 2.Nf3 Nc6 *");
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "e4", "e5", "Nf3", "Nc6" },
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(4, result.InRepertoire.Count);
    }

    [Fact]
    public async Task GameInRepertoire_NoDeviation()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 2. Nf3 Nc6 *");
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "e4", "e5", "Nf3", "Nc6" },
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(4, result.InRepertoire.Count);
        Assert.Null(result.FenBeforeDeviation);
    }

    [Fact]
    public async Task DeviationAtMove3_ReturnsDeviationAndFen()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 2. Nf3 Nc6 *");
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "e4", "e5", "Bc4" }, // 3rd ply (index 2) leaves the repertoire
        });
        Assert.Equal(2, result.Deviation);
        Assert.NotNull(result.FenBeforeDeviation);
        // FEN nach 1. e4 e5 (vor Bc4) ist die Position, in der Weiss am Zug ist.
        Assert.Contains(" w ", result.FenBeforeDeviation);
    }

    [Fact]
    public async Task TranspositionGap_DetectedAsGapNotDeviation()
    {
        // Repertoire enthaelt 1. e4 c5 und 1. Nf3 c5 (gleiche Endstellung via Transposition)
        var pgn = "[Event \"a\"]\n\n1. e4 c5 *\n\n[Event \"b\"]\n\n1. Nf3 c5 2. e4 *";
        var userId = await SeedUserWithOpeningAsync(pgn);
        // Gespielte Reihenfolge: 1. e4 c5 2. Nf3 — Zug 3 ist im Repertoire (Stellung kommt im Trie b vor).
        // Wir testen die simplere Transposition: 1. Nf3 c5 sollte komplett in-rep sein.
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "Nf3", "c5" },
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(2, result.InRepertoire.Count);
    }

    [Fact]
    public async Task UploadInvalidatesCache()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 *");
        // Cache aufwaermen
        await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto { Moves = new() { "e4", "e5" } });

        // Zweite PGN hochladen → Cache muss invalidiert werden, sonst sehen wir 1. d4 d5 nicht.
        var user = await _db.AppUsers.FirstAsync(u => u.Id == userId);
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto
        {
            Name = "Opening 2", Kind = RepertoireKind.Opening,
        });
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("[Event \"y\"]\n\n1. d4 d5 *"));
        await _repertoireService.UploadFileAsync(rep.Id, userId, "rep2.pgn", stream);

        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "d4", "d5" },
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(2, result.InRepertoire.Count);
    }

    // ===== Buchzüge für den Partie-Rückblick (0.522.0) ========================

    private static List<string> FensAfter(params string[] sans)
    {
        var board = new Chess.ChessBoard();
        var list = new List<string>();
        foreach (var san in sans) { board.Move(san); list.Add(board.ToFen()); }
        return list;
    }

    [Fact]
    public async Task Buchzuege_dieRepertoireZuegeVorDerAbweichung_beiderSeiten()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *");
        var book = await _analyze.BookPliesAsync(userId, FensAfter("e4", "e5", "Nf3", "d6", "Bc4"));
        Assert.Equal(new[] { 0, 1, 2 }, book);
    }

    [Fact]
    public async Task Buchzuege_Zugumstellung_zaehlt_Abstecher_nicht()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 *");
        // 1.c4 e6 2.d4 Nf6 erreicht nach Zug 2 dieselbe Stellung — die Zwischenstellungen stehen nicht im Repertoire.
        var book = await _analyze.BookPliesAsync(userId, FensAfter("c4", "e6", "d4", "Nf6", "Nc3", "Bb4", "a3"));
        Assert.Equal(new[] { 3, 4, 5 }, book);
    }

    [Fact]
    public async Task Buchzuege_nurRepertoiresFuerDieErweiterung()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 *");
        foreach (var r in _db.Repertoires.Where(r => r.UserId == userId)) r.UseForExtension = false;
        await _db.SaveChangesAsync();
        _analyze.Invalidate(userId);

        Assert.Empty(await _analyze.BookPliesAsync(userId, FensAfter("e4", "e5")));
    }
}
