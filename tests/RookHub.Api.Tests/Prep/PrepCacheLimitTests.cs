using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests.Prep;

/// <summary>R2-004: die Spielervorbereitung lag mit großen Karten im allgemeinen <see cref="IMemoryCache"/> ohne Größengrenze, die
/// Trainingslinien mit frei wählbaren, beliebig langen Schlüsseln. Jetzt: eigener Cache mit Grenze in Partien (jeder Eintrag
/// trägt seine Größe — in einem begrenzten Cache wirft ein Eintrag ohne Größe), Schlüssel mit festem Fingerabdruck.</summary>
public class PrepCacheLimitTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private const string Pgn = "[Event \"Open\"]\n[Date \"2024.05.17\"]\n[White \"Huber, Franz\"]\n[Black \"Mair, Josef\"]\n[Result \"1-0\"]\n"
        + "[WhiteFideId \"990001\"]\n\n1. e4 c5 2. Nf3 d6 1-0\n\n"
        + "[Event \"Open\"]\n[Date \"2023.03.01\"]\n[White \"Gruber, Karl\"]\n[Black \"Huber, Franz\"]\n[Result \"0-1\"]\n"
        + "[BlackFideId \"990001\"]\n\n1. d4 Nf6 2. c4 e6 0-1\n\n";

    [Fact]
    public async Task Karte_imBegrenztenCache_traegtIhreGroesse()
    {
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Mega, 0, 0, Pgn, default);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = PrepCardService.CacheSizeLimit, TrackStatistics = true });
        var cards = new PrepCardService(_db, cache, new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance));
        var huber = await _db.PrepPlayers.SingleAsync(p => p.FideId == "990001");

        var loaded = (await cards.LoadAsync(huber.Id, false, false, default))!;

        Assert.Equal(2, loaded.Games.Count);
        Assert.Equal(2, cache.GetCurrentStatistics()!.CurrentEstimatedSize);   // Größe = Partien
        Assert.Same(loaded, await cards.LoadAsync(huber.Id, false, false, default));
    }

    [Fact]
    public void Farbangabe_imSchluessel_hatFesteLaenge()
    {
        var huge = TrainingLinesService.OverridesKey(new string('x', 200_000));
        Assert.Equal(64, huge.Length);
        Assert.NotEqual(huge, TrainingLinesService.OverridesKey(new string('x', 199_999)));
        Assert.Equal(TrainingLinesService.OverridesKey("a=w"), TrainingLinesService.OverridesKey("a=w"));
    }
}
