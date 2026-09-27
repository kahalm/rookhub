using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Endlos-Modus der Kinderseite (<see cref="KidsEndlessService"/>): je Rating-Fenster ein kindgerechtes Puzzle,
/// im Lauf keins doppelt, ungeeignete (zu lang, Rochade, schlecht bewertet) nie.
/// </summary>
public class KidsEndlessServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public KidsEndlessServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private int _next = 1;

    private Puzzle Add(int rating, string moves = "e7e5 d1h5", int popularity = 95, int plays = 500, int rd = 75,
        string themes = "mateIn1 short")
    {
        var p = new Puzzle
        {
            Id = _next++, LichessId = $"k{_next}", Fen = "4k3/8/8/8/8/8/8/4K2Q b - - 0 1", Moves = moves, Rating = rating,
            RatingDeviation = rd, Popularity = popularity, NbPlays = plays, Themes = themes,
        };
        _db.Puzzles.Add(p);
        return p;
    }

    private KidsEndlessService Service(int seed = 7) => new(_db, new Random(seed));

    private static KidsEndlessBatchRequest Windows(params (int Min, int Max)[] w) => new()
    {
        Windows = w.Select(x => new KidsEndlessWindowDto { MinRating = x.Min, MaxRating = x.Max }).ToList(),
    };

    [Fact]
    public async Task JeFensterEinPuzzle_InFensterreihenfolge()
    {
        var a = Add(700);
        var b = Add(900, "a1a2 b1b2 c1c2 d1d2");
        var c = Add(1100, "a1a2 b1b2 c1c2 d1d2 e1e2 f1f2");
        await _db.SaveChangesAsync();

        var batch = await Service().BatchAsync(Windows((1080, 1120), (680, 720), (880, 920)));

        Assert.Equal(new[] { c.Id, a.Id, b.Id }, batch.Select(p => p.Id));
        Assert.Equal(new[] { 1100, 700, 900 }, batch.Select(p => p.Rating));
    }

    [Fact]
    public async Task Ungeeignete_KommenNie()
    {
        Add(700, "a1a2 b1b2 c1c2 d1d2 e1e2 f1f2 g1g2 h1h2");       // vier eigene Zuege
        Add(700, "a1a2 b1b2 c1c2");                                  // ungerade — keine Lichess-Form
        Add(700, themes: "mateIn1 castling");
        Add(700, themes: "enPassant");
        Add(700, themes: "advantage underPromotion");
        Add(700, popularity: 40);
        Add(700, plays: 10);
        Add(700, rd: 200);
        var good = Add(700);
        await _db.SaveChangesAsync();

        for (var seed = 0; seed < 20; seed++)
        {
            var batch = await Service(seed).BatchAsync(Windows((690, 710)));
            Assert.Equal(good.Id, Assert.Single(batch).Id);
        }
    }

    [Fact]
    public async Task ImLaufKeinsDoppelt_UndGespieltesKommtNichtWieder()
    {
        var a = Add(700);
        var b = Add(705);
        var c = Add(710);
        await _db.SaveChangesAsync();

        var batch = await Service().BatchAsync(Windows((690, 720), (690, 720), (690, 720), (690, 720)));
        Assert.Equal(3, batch.Select(p => p.Id).Distinct().Count());       // das vierte Fenster geht leer aus

        var again = await Service().BatchAsync(new KidsEndlessBatchRequest
        {
            Windows = new() { new() { MinRating = 690, MaxRating = 720 } },
            Exclude = new() { a.Id, b.Id },
        });
        Assert.Equal(c.Id, Assert.Single(again).Id);
    }

    [Fact]
    public async Task FensterOhneTreffer_FaelltWeg_VertauschteGrenzenGehen()
    {
        var a = Add(800);
        await _db.SaveChangesAsync();

        var batch = await Service().BatchAsync(Windows((1500, 1600), (820, 780)));

        Assert.Equal(a.Id, Assert.Single(batch).Id);
    }

    [Fact]
    public async Task ZufallsSprung_ErreichtAuchDasEndeDesIdRaums()
    {
        // Das einzige passende Puzzle hat die hoechste Id: jeder Sprung dahinter muss von vorn weitersuchen.
        for (var i = 0; i < 30; i++) Add(700, popularity: 10);
        var last = Add(700);
        await _db.SaveChangesAsync();

        for (var seed = 0; seed < 10; seed++)
            Assert.Equal(last.Id, Assert.Single(await Service(seed).BatchAsync(Windows((690, 710)))).Id);
    }

    [Fact]
    public async Task LeererBestand_LeereAntwort()
    {
        Assert.Empty(await Service().BatchAsync(Windows((690, 710))));
    }

    [Fact]
    public async Task Deckel_Sind400()
    {
        var controller = new KidsController(new KidsPuzzleService(_db), endless: Service());
        var tooMany = new KidsEndlessBatchRequest
        {
            Windows = Enumerable.Range(0, KidsEndlessService.MaxWindows + 1).Select(_ => new KidsEndlessWindowDto { MinRating = 700, MaxRating = 720 }).ToList(),
        };

        Assert.IsType<BadRequestObjectResult>((await controller.GetEndlessBatch(tooMany, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetEndlessBatch(new KidsEndlessBatchRequest
        {
            Windows = new() { new() { MinRating = 700, MaxRating = 720 } },
            Exclude = Enumerable.Range(1, KidsEndlessService.MaxExclude + 1).ToList(),
        }, CancellationToken.None)).Result);
    }
}
