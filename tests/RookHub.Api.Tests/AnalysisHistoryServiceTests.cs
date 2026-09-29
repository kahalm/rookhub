using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Analyse-Verlauf (0.603.0): letzte 20 Analysen je Nutzer, fortschreiben über die Kennung, Sterne.</summary>
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

    private static SaveAnalysisHistoryRequest Req(string moves, int ply = 0, int? id = null, string? title = null, params int[] stars)
        => new() { Id = id, StartFen = START, Moves = moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(), Ply = ply, Title = title, Starred = stars.ToList() };

    [Fact]
    public async Task Save_legtAn_undSchreibtUeberDieKennungFort()
    {
        var first = await _svc.SaveAsync(1, Req("e2e4 e7e5", 2, title: "Carlsen – Nakamura"));
        Assert.Equal(new[] { "e2e4", "e7e5" }, first.Moves);
        Assert.Equal("1.e4 e5", first.Preview);
        Assert.Equal("Carlsen – Nakamura", first.Title);

        // Weitergezogen: derselbe Eintrag, kein zweiter
        var next = await _svc.SaveAsync(1, Req("e2e4 e7e5 g1f3", 3, id: first.Id, stars: [1, 3]));
        Assert.Equal(first.Id, next.Id);
        Assert.Equal(3, next.MoveCount);
        Assert.Equal(new[] { 1, 3 }, next.Starred);
        Assert.Equal("Carlsen – Nakamura", next.Title);   // ohne Titel bleibt der alte
        Assert.Single(_db.AnalysisHistoryEntries);
    }

    [Fact]
    public async Task Save_ohneKennung_nimmtDieselbeZugfolgeWieder_stattEinenZweitenAnzulegen()
    {
        var a = await _svc.SaveAsync(1, Req("d2d4 d7d5"));
        var b = await _svc.SaveAsync(1, Req("d2d4 d7d5", 1));
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
        await _svc.SaveAsync(1, Req("a2a3", 1, id: first.Id));
        await _svc.SaveAsync(1, Req("e2e4 e7e5"));

        var list = await _svc.ListAsync(1);
        Assert.Equal(AnalysisHistoryService.MaxPerUser, list.Count);
        Assert.Equal(AnalysisHistoryService.MaxPerUser, _db.AnalysisHistoryEntries.Count(h => h.UserId == 1));
        Assert.Contains(list, h => h.Id == first.Id);
        Assert.DoesNotContain(list, h => h.Moves.SequenceEqual(new[] { "a2a4" }));
        Assert.Equal(new[] { "e2e4", "e7e5" }, list[0].Moves);
    }

    [Fact]
    public async Task Save_weistUnlesbaresAb_undKlemmtPlyUndSterne()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(1, Req("e2e4 e2e4")));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(1, new SaveAnalysisHistoryRequest { StartFen = "kaputt", Moves = [] }));
        Assert.Empty(_db.AnalysisHistoryEntries);

        var dto = await _svc.SaveAsync(1, Req("e2e4 e7e5", 99, stars: [5, 2, 2, -1, 0]));
        Assert.Equal(2, dto.Ply);
        Assert.Equal(new[] { 0, 2 }, dto.Starred);
    }

    [Fact]
    public async Task Rochade_alsKoenigSchlaegtTurm_wirdAlsE1G1Gespeichert()
    {
        var dto = await _svc.SaveAsync(1, Req("e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 e1h1"));
        Assert.Equal("e1g1", dto.Moves[^1]);
        Assert.Contains("4.O-O", dto.Preview);
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
