using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Analyse-Verlauf (0.603.0): letzte 20 Analysen je Nutzer, fortschreiben über die Kennung, Sterne; seit 0.604.0
/// als Zugbaum mit Varianten in flacher Form.</summary>
public class AnalysisHistoryServiceTests : IDisposable
{
    private const string START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly AppDbContext _db;
    private readonly AnalysisHistoryService _svc;

    public AnalysisHistoryServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.AddRange(new AppUser { Id = 1, Username = "a", PasswordHash = "x" }, new AppUser { Id = 2, Username = "b", PasswordHash = "x" });
        _db.SaveChanges();
        _svc = new AnalysisHistoryService(_db);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Eine Zugfolge ohne Varianten; <paramref name="stars"/> = Indizes der markierten Knoten.</summary>
    private static SaveAnalysisHistoryRequest Req(string moves, int current = -1, int? id = null, string? title = null, params int[] stars)
    {
        var ucis = moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new()
        {
            Id = id, StartFen = START, Title = title, Current = current,
            Tree = new AnalysisTreeDto { N = ucis.Select((u, i) => Node(i - 1, u, stars.Contains(i))).ToList() },
        };
    }

    private static AnalysisTreeNodeDto Node(int p, string u, bool star = false, string? eval = null)
        => new() { P = p, U = u, S = star ? true : null, E = eval };

    private static SaveAnalysisHistoryRequest TreeReq(int current, params AnalysisTreeNodeDto[] nodes)
        => new() { StartFen = START, Current = current, Tree = new AnalysisTreeDto { N = nodes.ToList() } };

    [Fact]
    public async Task Save_legtAn_undSchreibtUeberDieKennungFort()
    {
        var first = await _svc.SaveAsync(1, Req("e2e4 e7e5", 1, title: "Carlsen – Nakamura"));
        Assert.Equal(new[] { "e2e4", "e7e5" }, first.Moves);
        Assert.Equal("1.e4 e5", first.Preview);
        Assert.Equal("Carlsen – Nakamura", first.Title);
        Assert.Equal(2, first.Ply);

        // Weitergezogen: derselbe Eintrag, kein zweiter
        var next = await _svc.SaveAsync(1, Req("e2e4 e7e5 g1f3", 2, id: first.Id, stars: [0, 2]));
        Assert.Equal(first.Id, next.Id);
        Assert.Equal(3, next.MoveCount);
        Assert.Equal(2, next.StarCount);
        Assert.Equal("Carlsen – Nakamura", next.Title);   // ohne Titel bleibt der alte
        Assert.Single(_db.AnalysisHistoryEntries);
    }

    [Fact]
    public async Task Save_ohneKennung_nimmtDieselbeZugfolgeWieder_stattEinenZweitenAnzulegen()
    {
        var a = await _svc.SaveAsync(1, Req("d2d4 d7d5"));
        var b = await _svc.SaveAsync(1, Req("d2d4 d7d5", 0));
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, b.Ply);
        // Ein anderer Nutzer bekommt seinen eigenen
        var other = await _svc.SaveAsync(2, Req("d2d4 d7d5"));
        Assert.NotEqual(a.Id, other.Id);
    }

    [Fact]
    public async Task Save_fremdeKennung_legtNeuAn_undFasstFremdesNichtAn()
    {
        var mine = await _svc.SaveAsync(1, Req("e2e4"));
        var theirs = await _svc.SaveAsync(2, Req("c2c4", id: mine.Id));
        Assert.NotEqual(mine.Id, theirs.Id);
        Assert.Equal(new[] { "e2e4" }, (await _svc.GetAsync(1, mine.Id))!.Moves);
        Assert.Null(await _svc.GetAsync(2, mine.Id));
    }

    [Fact]
    public async Task HoechstensZwanzig_dieZuletztAngefasstenBleiben()
    {
        var first = await _svc.SaveAsync(1, Req("a2a3"));
        var moves = new[] { "a2a4", "b2b3", "b2b4", "c2c3", "c2c4", "d2d3", "d2d4", "e2e3", "e2e4", "f2f3", "f2f4", "g2g3", "g2g4",
            "h2h3", "h2h4", "b1a3", "b1c3", "g1f3", "g1h3" };
        foreach (var m in moves) await _svc.SaveAsync(1, Req(m));
        // Den ersten noch einmal anfassen → er rückt nach oben, der zweitälteste fliegt beim nächsten raus
        await _svc.SaveAsync(1, Req("a2a3", 0, id: first.Id));
        await _svc.SaveAsync(1, Req("e2e4 e7e5"));

        var list = await _svc.ListAsync(1);
        Assert.Equal(AnalysisHistoryService.MaxPerUser, list.Count);
        Assert.Equal(AnalysisHistoryService.MaxPerUser, _db.AnalysisHistoryEntries.Count(h => h.UserId == 1));
        Assert.Contains(list, h => h.Id == first.Id);
        Assert.DoesNotContain(list, h => h.Moves.SequenceEqual(new[] { "a2a4" }));
        Assert.Equal(new[] { "e2e4", "e7e5" }, list[0].Moves);
    }

    [Fact]
    public async Task Save_weistUnlesbaresAb_undKlemmtDenStandort()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(1, Req("e2e4 e2e4")));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(1, new SaveAnalysisHistoryRequest { StartFen = "kaputt" }));
        // Elternverweis nach vorn, derselbe Zug zweimal unter einem Knoten
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(1, TreeReq(-1, Node(1, "e2e4"), Node(-1, "d2d4"))));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(1, TreeReq(-1, Node(-1, "e2e4"), Node(-1, "e2e4"))));
        // Mehr als MaxStars Sterne: 20 erste Züge, je drei Antworten, alle markiert (ohne Zugwiederholung — die beendete die Partie)
        var white = new[] { "a2a3", "a2a4", "b2b3", "b2b4", "c2c3", "c2c4", "d2d3", "d2d4", "e2e3", "e2e4", "f2f3", "f2f4", "g2g3", "g2g4",
            "h2h3", "h2h4", "b1a3", "b1c3", "g1f3", "g1h3" };
        var starredNodes = new List<AnalysisTreeNodeDto>();
        foreach (var w in white)
        {
            var at = starredNodes.Count;
            starredNodes.Add(Node(-1, w, star: true));
            foreach (var b in new[] { "a7a6", "b7b6", "c7c6" }) starredNodes.Add(Node(at, b, star: true));
        }
        Assert.True(starredNodes.Count > AnalysisHistoryService.MaxStars);
        var tooManyStars = new SaveAnalysisHistoryRequest { StartFen = START, Tree = new AnalysisTreeDto { N = starredNodes } };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(1, tooManyStars));
        Assert.Contains("stars", ex.Message);
        Assert.Empty(_db.AnalysisHistoryEntries);

        var dto = await _svc.SaveAsync(1, Req("e2e4 e7e5", 99));
        Assert.Equal(-1, dto.Current);
        Assert.Equal(0, dto.Ply);
    }

    [Fact]
    public async Task Rochade_alsKoenigSchlaegtTurm_wirdAlsE1G1Gespeichert()
    {
        var dto = await _svc.SaveAsync(1, Req("e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 e1h1"));
        Assert.Equal("e1g1", dto.Moves[^1]);
        Assert.Equal("e1g1", dto.Tree!.N![^1].U);
        Assert.Contains("4.O-O", dto.Preview);
    }

    [Fact]
    public async Task Varianten_bleibenErhalten_dieHauptlinieIstDasErsteKind()
    {
        // 1.e4 e5 (1...c5 2.Nf3) — gestanden bei 2.Nf3 in der Variante
        var dto = await _svc.SaveAsync(1, TreeReq(3,
            Node(-1, "e2e4", eval: "+0.25"), Node(0, "e7e5", star: true), Node(0, "c7c5", eval: "#-3"), Node(2, "g1f3", eval: "hack")));
        Assert.Equal(new[] { "e2e4", "e7e5" }, dto.Moves);
        Assert.Equal(2, dto.MoveCount);
        Assert.Equal(4, dto.NodeCount);
        Assert.Equal(1, dto.StarCount);
        Assert.Equal(3, dto.Current);
        Assert.Equal(3, dto.Ply);

        var back = (await _svc.GetAsync(1, dto.Id))!;
        var n = back.Tree!.N!;
        Assert.Equal(new[] { -1, 0, 0, 2 }, n.Select(x => x.P));
        Assert.Equal(new[] { "e2e4", "e7e5", "c7c5", "g1f3" }, n.Select(x => x.U));
        Assert.Equal(new bool?[] { null, true, null, null }, n.Select(x => x.S));
        Assert.Equal(new[] { "+0.25", null, "#-3", null }, n.Select(x => x.E));   // unbrauchbare Bewertung fällt weg
        Assert.Equal(3, back.Current);

        // Die Liste trägt den Baum nicht mit, nur die Zähler
        var listed = (await _svc.ListAsync(1)).Single();
        Assert.Null(listed.Tree);
        Assert.Equal(4, listed.NodeCount);
    }

    [Fact]
    public async Task EintragVon0603_ohneBaum_kommtAlsZugfolgeZurueck()
    {
        _db.AnalysisHistoryEntries.Add(new AnalysisHistoryEntry
        {
            Id = 77, UserId = 1, StartFen = START, Moves = "e2e4 e7e5 g1f3", MoveCount = 3, Ply = 2,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        var dto = (await _svc.GetAsync(1, 77))!;
        Assert.Equal(new[] { -1, 0, 1 }, dto.Tree!.N!.Select(x => x.P));
        Assert.Equal(1, dto.Current);
        Assert.Equal(3, dto.NodeCount);
    }

    [Fact]
    public async Task Loeschen_nurEigene()
    {
        var dto = await _svc.SaveAsync(1, Req("e2e4"));
        Assert.False(await _svc.DeleteAsync(2, dto.Id));
        Assert.True(await _svc.DeleteAsync(1, dto.Id));
        Assert.Empty(_db.AnalysisHistoryEntries);
    }
}
