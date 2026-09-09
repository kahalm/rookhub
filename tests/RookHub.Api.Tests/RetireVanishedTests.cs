using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Verschwunden-Erkennung der ZUSATZQUELLEN. Bis 2026-09-09 gab es sie nur fuer die
/// Turniersuche: ein Turnier, das aus dem polnischen oder italienischen Kalender verschwand, blieb
/// bei uns fuer immer stehen.
///
/// <para>Die Zahlen in den Tests sind bewusst gross: die Bremse verlangt, dass ein Lauf fast alle
/// eigenen Kandidaten wiederbringt, und mit zwei Eintraegen laesst sich „fast alle" nicht
/// darstellen.</para>
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

    /// <summary>Zehn Eintraege „k1".."k10" mit demselben Termin.</summary>
    private async Task AddTenAsync()
    {
        for (var i = 1; i <= 10; i++) await AddAsync($"pl{i}", $"k{i}");
    }

    private static string[] AllBut(params int[] missing) =>
        Enumerable.Range(1, 10).Where(i => !missing.Contains(i)).Select(i => $"k{i}").ToArray();

    private Task<int> RunAsync(params string[] delivered) => RunAtAsync(Now, delivered);

    private Task<int> RunAtAsync(DateTime now, params string[] delivered) =>
        ExternalDirectorySource.RetireVanishedAsync(_db, DirectorySourceKind.PolishChessFederation,
            delivered, now);

    [Fact]
    public async Task ZweiNaechteNichtGeliefert_wirdZurueckgezogen()
    {
        await AddTenAsync();

        // Erste Nacht: „k1" fehlt — nur zaehlen, ein einzelner Ausfall sagt nichts ab.
        Assert.Equal(0, await RunAtAsync(Now, AllBut(1)));
        Assert.Null((await Reload("pl1")).RemovedAt);
        Assert.Equal(1, (await Reload("pl1")).MissedSweeps);

        // Zweite Nacht: jetzt.
        Assert.Equal(1, await RunAtAsync(Now.AddDays(1), AllBut(1)));
        Assert.NotNull((await Reload("pl1")).RemovedAt);
        Assert.Null((await Reload("pl2")).RemovedAt);
    }

    [Fact]
    public async Task WiederGeliefert_setztZaehlerUndFehlschlagZurueck()
    {
        await AddTenAsync();
        await RunAtAsync(Now, AllBut(1));
        Assert.Equal(1, (await Reload("pl1")).MissedSweeps);
        Assert.NotNull((await Reload("pl1")).LastMissAt);

        Assert.Equal(0, await RunAtAsync(Now.AddDays(1), AllBut()));
        var wieder = await Reload("pl1");
        Assert.Equal(0, wieder.MissedSweeps);
        Assert.Null(wieder.LastMissAt);
        Assert.Null(wieder.RemovedAt);
    }

    /// <summary>
    /// ZWEI Durchgaenge sind nicht zwei Naechte. Der Aufhol-Lauf nach einem Deploy und
    /// <c>directory-runs.sh</c> mit bis zu elf Durchgaengen haetten sonst binnen einer Stunde
    /// abgesagt, was eine Quelle kurz nicht auswies.
    /// </summary>
    [Fact]
    public async Task ZweiDurchgaengeAmSelbenAbend_zaehlenEinmal()
    {
        await AddTenAsync();

        Assert.Equal(0, await RunAtAsync(Now, AllBut(1)));
        Assert.Equal(0, await RunAtAsync(Now.AddMinutes(20), AllBut(1)));
        Assert.Equal(0, await RunAtAsync(Now.AddHours(3), AllBut(1)));

        var entry = await Reload("pl1");
        Assert.Equal(1, entry.MissedSweeps);
        Assert.Null(entry.RemovedAt);
    }

    [Fact]
    public async Task NachAblaufDerKarenz_zaehltDerNaechsteFehlschlag()
    {
        await AddTenAsync();

        Assert.Equal(0, await RunAtAsync(Now, AllBut(1)));
        Assert.Equal(1, await RunAtAsync(Now + ExternalDirectorySource.MissCooldown, AllBut(1)));
        Assert.NotNull((await Reload("pl1")).RemovedAt);
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
    /// 25 Regionen faellt aus, eine von 12 Monatsseiten) wiederholt sich jede Nacht — die Karenz
    /// von zwei Naechten faengt sie NICHT ab.
    ///
    /// <para>Gemessen wird der Anteil der wiedergesehenen KANDIDATEN. Die erste Fassung verglich
    /// die Zahl der gelieferten ZEILEN mit der Zahl der Kandidaten und liess alles ab der Haelfte
    /// gelten — bei Polen (620 Kandidaten, an einem gewoehnlichen Tag fuenf Abgaenge) haette das
    /// 310 falsche Absagen zugelassen.</para>
    /// </summary>
    [Fact]
    public async Task EinLueckenhafterLauf_sagtNichtsAb()
    {
        await AddTenAsync();

        // 8 von 10, also 80 % — unter der Schwelle, gar nicht pruefen.
        Assert.Equal(0, await RunAsync(AllBut(9, 10)));
        Assert.All(_db.TournamentDirectoryEntries.ToList(), e => Assert.Equal(0, e.MissedSweeps));

        // 9 von 10 sind 90 % und werden geprueft.
        Assert.Equal(0, await RunAsync(AllBut(10)));
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

    /// <summary>
    /// Eine Quelle mit kurzem Vorschau-Fenster darf nicht alles dahinter abraeumen. Der Horizont
    /// ist der spaeteste Termin, den der Lauf selbst geliefert hat.
    /// </summary>
    [Fact]
    public async Task JenseitsDesHorizonts_wirdNichtsZurueckgezogen()
    {
        await AddTenAsync();
        var fern = DateOnly.FromDateTime(Now).AddDays(200);
        await AddAsync("plFern", "kFern", start: fern);

        // Der Lauf liefert alle nahen Termine, den fernen nicht — er zeigt nur zwei Monate.
        Assert.Equal(0, await RunAtAsync(Now, AllBut()));
        Assert.Equal(0, (await Reload("plFern")).MissedSweeps);
        Assert.Null((await Reload("plFern")).LastMissAt);

        // Und die Bremse haelt trotzdem nicht die nahen auf: dort fehlt nichts.
        Assert.Equal(0, await RunAtAsync(Now.AddDays(1), AllBut(1)));
        Assert.Equal(1, (await Reload("pl1")).MissedSweeps);
    }

    /// <summary>
    /// Die Spalte vergleicht MySQL ohne Ruecksicht auf Gross- und Kleinschreibung. Ein
    /// Ordinal-Vergleich machte aus einer wiedergesehenen Zeile eine verschwundene, sobald eine
    /// Quelle die Schreibweise aendert.
    /// </summary>
    [Fact]
    public async Task Schreibweise_entscheidetNicht()
    {
        await AddTenAsync();

        Assert.Equal(0, await RunAsync([.. AllBut(1), "K1"]));
        Assert.Equal(0, (await Reload("pl1")).MissedSweeps);
    }

    private async Task<TournamentDirectoryEntry> Reload(string publicId)
    {
        using var fresh = Create();
        return await fresh.TournamentDirectoryEntries.FirstAsync(e => e.PublicId == publicId);
    }
}
