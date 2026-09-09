using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Verschwunden-Erkennung der ZUSATZQUELLEN. Bis 2026-09-09 gab es sie nur fuer die
/// Turniersuche: ein Turnier, das aus dem polnischen oder italienischen Kalender verschwand, blieb
/// bei uns fuer immer stehen.
/// </summary>
public class RetireVanishedTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;

    public RetireVanishedTests() => _db = Create();
    private AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
    public void Dispose() => _db.Dispose();

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateOnly Soon = DateOnly.FromDateTime(Now).AddDays(30);

    private async Task<TournamentDirectoryEntry> AddAsync(string publicId, string externalId,
        DirectorySourceKind kind = DirectorySourceKind.PolishChessFederation,
        string? chessResultsId = null, DateOnly? start = null, DirectorySourceKind? second = null)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = publicId, ChessResultsId = chessResultsId, Name = "Turniej " + publicId,
            Federation = "POL", StartDate = start ?? Soon, EndDate = start ?? Soon,
            FirstSeenAt = Now, LastSeenAt = Now,
        };
        entry.Sources.Add(new TournamentDirectorySource
        {
            Kind = kind, ExternalId = externalId, FirstSeenAt = Now, LastSeenAt = Now,
        });
        if (second is { } other)
            entry.Sources.Add(new TournamentDirectorySource
            {
                Kind = other, ExternalId = externalId + "-b", FirstSeenAt = Now, LastSeenAt = Now,
            });
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private Task<int> RunAsync(params string[] delivered) =>
        ExternalDirectorySource.RetireVanishedAsync(_db, DirectorySourceKind.PolishChessFederation,
            delivered, Now);

    [Fact]
    public async Task ZweimalNichtGeliefert_wirdZurueckgezogen()
    {
        await AddAsync("pl1", "a");
        await AddAsync("pl2", "b");

        // Erster Lauf: „a" fehlt — nur zaehlen, ein einzelner Ausfall sagt nichts ab.
        Assert.Equal(0, await RunAsync("b", "c", "d"));
        Assert.Null((await Reload("pl1")).RemovedAt);
        Assert.Equal(1, (await Reload("pl1")).MissedSweeps);

        // Zweiter Lauf: jetzt.
        Assert.Equal(1, await RunAsync("b", "c", "d"));
        Assert.NotNull((await Reload("pl1")).RemovedAt);
        Assert.Null((await Reload("pl2")).RemovedAt);
    }

    [Fact]
    public async Task WiederGeliefert_setztDenZaehlerZurueck()
    {
        await AddAsync("pl1", "a");
        await AddAsync("pl2", "b");
        await RunAsync("b", "c");                 // pl1 fehlt einmal
        Assert.Equal(1, (await Reload("pl1")).MissedSweeps);

        Assert.Equal(0, await RunAsync("a", "b")); // wieder da
        var wieder = await Reload("pl1");
        Assert.Equal(0, wieder.MissedSweeps);
        Assert.Null(wieder.RemovedAt);
    }

    /// <summary>
    /// Fuehrt die TURNIERSUCHE dasselbe Turnier, entscheidet sie ueber sein Verschwinden — sie
    /// sieht mehr als ein Verbandskalender.
    /// </summary>
    [Fact]
    public async Task MitChessResultsNummer_bleibtUnangetastet()
    {
        await AddAsync("1470450", "a", chessResultsId: "1470450");
        await AddAsync("pl2", "b");

        Assert.Equal(0, await RunAsync("b"));
        Assert.Equal(0, (await Reload("1470450")).MissedSweeps);
    }

    /// <summary>Steht ein Turnier auf ZWEI Kalendern, ist sein Fehlen auf einem keine Absage.</summary>
    [Fact]
    public async Task MitZweiterQuelle_bleibtUnangetastet()
    {
        await AddAsync("pl1", "a", second: DirectorySourceKind.CzechChessFederation);
        await AddAsync("pl2", "b");

        Assert.Equal(0, await RunAsync("b"));
        Assert.Equal(0, (await Reload("pl1")).MissedSweeps);
    }

    /// <summary>Was vorbei ist, sagt niemand mehr ab — und die Quellen lassen Vergangenes fallen.</summary>
    [Fact]
    public async Task VergangeneTurniere_bleibenUnangetastet()
    {
        await AddAsync("pl1", "a", start: DateOnly.FromDateTime(Now).AddDays(-10));
        await AddAsync("pl2", "b");

        Assert.Equal(0, await RunAsync("b"));
        Assert.Equal(0, (await Reload("pl1")).MissedSweeps);
    }

    /// <summary>
    /// DIE Bremse: ein halb geladener Lauf darf nichts absagen. Eine systematische Luecke (eine von
    /// 25 Regionen faellt aus) wiederholt sich jede Nacht — die Karenz von zwei Laeufen faengt sie
    /// NICHT ab. Genau dafuer gibt es bei der Turniersuche die MaxRows-Bremse.
    /// </summary>
    [Fact]
    public async Task EinHalberLauf_sagtNichtsAb()
    {
        for (var i = 1; i <= 10; i++) await AddAsync($"pl{i}", $"k{i}");

        // Nur 4 von 10 geliefert — weniger als die Haelfte, also gar nicht pruefen.
        Assert.Equal(0, await RunAsync("k1", "k2", "k3", "k4"));
        Assert.All(_db.TournamentDirectoryEntries.ToList(), e => Assert.Equal(0, e.MissedSweeps));

        // 5 von 10 sind genau die Haelfte und werden geprueft.
        Assert.Equal(0, await RunAsync("k1", "k2", "k3", "k4", "k5"));
        Assert.Equal(1, (await Reload("pl10")).MissedSweeps);
    }

    /// <summary>Ein Lauf, der GAR NICHTS liefert, ist kaputt und nicht eine Massenabsage.</summary>
    [Fact]
    public async Task EinLeererLauf_sagtNichtsAb()
    {
        await AddAsync("pl1", "a");

        Assert.Equal(0, await RunAsync());
        Assert.Equal(0, (await Reload("pl1")).MissedSweeps);
    }

    private async Task<TournamentDirectoryEntry> Reload(string publicId)
    {
        using var fresh = Create();
        return await fresh.TournamentDirectoryEntries.FirstAsync(e => e.PublicId == publicId);
    }
}
