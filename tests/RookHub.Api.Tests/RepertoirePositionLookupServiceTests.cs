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
        _svc = new RepertoirePositionLookupService(_db, _cache);
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
        var line = Assert.Single(rep.Lines);
        Assert.Equal("Sicilian Defence", line.Chapter);          // [Black]
        Assert.Equal("Open Sicilian: 2...d6", line.LineName);      // [White]
        Assert.Equal(0, line.GameIndex);
        Assert.Equal(3, line.Ply);
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
}
