using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Zeitplan der Engine-Clients auf der RookHub-Seite (0.679.0): „wenn der client betriebszeiten meldet halte ich mich an
/// die, wenn nicht nehm ich die voreingestellten von rookhub". Die Regel-Faelle sind dieselben wie in
/// <c>engine-provider/test/schedule.test.sh</c> — RookHub und Client muessen fuer jede Engine zum selben Ergebnis kommen.
/// </summary>
public class EngineClientScheduleTests : IDisposable
{
    private const string Plan = "Mo-Do 08:00-17:00 0%; Fr 08:00-14:00 25%; Sa,So 100%";
    private readonly AppDbContext _db;

    public EngineClientScheduleTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.Add(new AppUser { Id = 5, Username = "kahalm", PasswordHash = "x" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ── Regeln: dieselben Faelle wie der Client ──────────────────────────────────────────────────

    private static int Target(string plan, int isoDay, string time, int total = 16, string scope = "background")
    {
        var p = time.Split(':');
        var pct = EngineScheduleRules.PercentAt(EngineScheduleRules.Parse(plan), isoDay, int.Parse(p[0]) * 60 + int.Parse(p[1]));
        return EngineScheduleRules.TargetCount(pct, total, scope);
    }

    [Theory]
    [InlineData(Plan, 1, "09:00", 0)]     // Sperrzeit: nichts
    [InlineData(Plan, 1, "19:00", 16)]    // abends voll
    [InlineData(Plan, 5, "09:00", 5)]     // Teillast: Live + 4 von 15
    [InlineData(Plan, 4, "17:00", 16)]    // das Ende zaehlt nicht mehr
    [InlineData(Plan, 6, "03:00", 16)]    // Kurzform ohne Uhrzeit = ganzer Tag
    [InlineData("* 22:00-06:00 100%; * 06:00-22:00 0%", 3, "02:00", 16)]   // ueber Mitternacht
    [InlineData("mon-fri 08:00-17:00 50%", 1, "09:00", 9)]                // englische Tage
    [InlineData("fr-mo 50%", 7, "12:00", 9)]                              // Bereich uebers Wochenende
    [InlineData("fr-mo 50%", 3, "12:00", 16)]                             // … Mittwoch liegt nicht drin
    public void Regeln_rechnenWieDerClient(string plan, int day, string time, int expected)
        => Assert.Equal(expected, Target(plan, day, time));

    [Fact]
    public void Scope_all_drosseltAuchDieLiveEngine()
    {
        Assert.Equal(5, Target("* 25%", 1, "09:00", scope: "background"));
        Assert.Equal(4, Target("* 25%", 1, "09:00", scope: "all"));
        Assert.Equal(1, Target("* 1%", 1, "09:00", scope: "all"));
        Assert.Equal(0, Target("* 0%", 1, "09:00", scope: "all"));
        Assert.Equal(1, Target("* 50%", 1, "09:00", total: 1));   // eine einzelne Engine bleibt bei > 0 % an
    }

    [Theory]
    [InlineData("Mo-Do 08:00-17:00")]
    [InlineData("Xy 08:00-17:00 50%")]
    [InlineData("Mo 8-17 50%")]
    [InlineData("Mo 08:00-17:00 150%")]
    [InlineData("Mo 25:00-26:00 50%")]
    [InlineData(" ; ")]
    public void KaputteRegeln_werdenAbgelehnt(string plan)
        => Assert.Throws<FormatException>(() => EngineScheduleRules.Parse(plan));

    [Fact]
    public void Zeitzone_IanaUndWindowsKennung()
    {
        Assert.NotNull(EngineScheduleRules.ResolveZone("Europe/Vienna"));
        Assert.NotNull(EngineScheduleRules.ResolveZone("W. Europe Standard Time"));   // was das Windows-Skript meldet
        Assert.Null(EngineScheduleRules.ResolveZone("Mond/Krater"));
    }

    // ── Verfuegbarkeit je Engine ─────────────────────────────────────────────────────────────────

    private void Register(string id, string name)
    {
        _db.ExternalEngineRegistrations.Add(new ExternalEngineRegistration
        {
            Id = id, UserId = 5, Name = name, ClientSecret = "s", MaxThreads = 2, MaxHash = 16,
            Variants = "chess", ProviderSelector = "sel" + id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();
    }

    private async Task Report(string rule, string scope, params string[] namesInSlotOrder)
    {
        var svc = new EngineClientScheduleService(_db, NullLogger<EngineClientScheduleService>.Instance);
        var r = await svc.ReportAsync(5, new EngineScheduleReport
        {
            TimeZone = "Europe/Vienna", Scope = scope, Rule = rule,
            Engines = namesInSlotOrder.Select((n, i) => new EngineScheduleReportEngine { Name = n, Slot = i + 1 }).ToList(),
        });
        Assert.Null(r.Error);
    }

    // Montag, 05.10.2026, 10:00 Wiener Zeit (Sommerzeit) = 08:00 UTC — mitten in den Sperrzeiten von RookHub.
    private static readonly DateTimeOffset MondayTenVienna = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static QuietHours Quiet() => new(QuietHours.DefaultSpec, QuietHours.DefaultTimeZone);

    [Fact]
    public async Task OhneMeldung_geltenDieSperrzeitenVonRookHub_nurFuerDenStapel()
    {
        Register("rhe_server00001", "Server Hintergrund");
        string[] list = ["rhe_server00001", "eei_lichess0001"];

        Assert.Empty(await EngineAvailability.UsableAsync(_db, 5, list, batch: true, Quiet(), MondayTenVienna));
        Assert.Equal(list, await EngineAvailability.UsableAsync(_db, 5, list, batch: false, Quiet(), MondayTenVienna));
    }

    [Fact]
    public async Task MitMeldung_giltDerZeitplanDesClients_auchInDerSperrzeit()
    {
        Register("rhe_pclive00001", "PC");
        Register("rhe_pcbg0000001", "PC Hintergrund");
        Register("rhe_pcbg0000002", "PC Hintergrund 2");
        Register("rhe_server00001", "Server Hintergrund");   // meldet nichts
        // Der PC rechnet montags um 10 mit 50 %: Live + 1 von 2 Hintergrund-Engines.
        await Report("Mo 08:00-17:00 50%", "background", "PC", "PC Hintergrund", "PC Hintergrund 2");

        var usable = await EngineAvailability.UsableAsync(_db, 5,
            ["rhe_pcbg0000001", "rhe_pcbg0000002", "rhe_server00001"], batch: true, Quiet(), MondayTenVienna);

        // Hintergrund 1 laeuft (Platz 2 ≤ 2), Hintergrund 2 steht (Platz 3), der Server haelt die Sperrzeit ein.
        Assert.Equal(["rhe_pcbg0000001"], usable);
    }

    [Fact]
    public async Task MitMeldung_0Prozent_schliesstAuchNormaleAuftraegeAus()
    {
        Register("rhe_pcbg0000001", "PC Hintergrund");
        await Report("Mo 08:00-17:00 0%", "background", "PC", "PC Hintergrund");

        Assert.Empty(await EngineAvailability.UsableAsync(_db, 5, ["rhe_pcbg0000001"], batch: false, Quiet(), MondayTenVienna));
    }

    [Fact]
    public async Task LeereRegel_loeschtDieMeldung_undRookHubRechnetWiederSelbst()
    {
        Register("rhe_pcbg0000001", "PC Hintergrund");
        await Report("* 100%", "background", "PC", "PC Hintergrund");
        Assert.Equal(2, await _db.EngineClientSchedules.CountAsync());

        var svc = new EngineClientScheduleService(_db, NullLogger<EngineClientScheduleService>.Instance);
        var r = await svc.ReportAsync(5, new EngineScheduleReport
        {
            Rule = "", Engines = [new() { Name = "PC", Slot = 1 }, new() { Name = "PC Hintergrund", Slot = 2 }],
        });

        Assert.Equal(2, r.Cleared);
        Assert.Empty(await _db.EngineClientSchedules.ToListAsync());
        // Ohne Meldung wieder die Sperrzeit: der Stapel bekommt die Engine montags um 10 nicht.
        Assert.Empty(await EngineAvailability.UsableAsync(_db, 5, ["rhe_pcbg0000001"], batch: true, Quiet(), MondayTenVienna));
    }

    // ── Meldung annehmen ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Mo 8-17 50%", "background", "Europe/Vienna", "keine Zeitspanne")]
    [InlineData("* 50%", "manchmal", "Europe/Vienna", "background")]
    [InlineData("* 50%", "all", "Mond/Krater", "Zeitzone")]
    public async Task Meldung_mitFehler_wirdMitKlartextAbgelehnt(string rule, string scope, string zone, string hint)
    {
        var svc = new EngineClientScheduleService(_db, NullLogger<EngineClientScheduleService>.Instance);
        var r = await svc.ReportAsync(5, new EngineScheduleReport
        {
            Rule = rule, Scope = scope, TimeZone = zone, Engines = [new() { Name = "PC", Slot = 1 }],
        });
        Assert.Contains(hint, r.Error);
        Assert.Empty(await _db.EngineClientSchedules.ToListAsync());
    }

    [Fact]
    public async Task Meldung_doppelterPlatzOderName_wirdAbgelehnt()
    {
        var svc = new EngineClientScheduleService(_db, NullLogger<EngineClientScheduleService>.Instance);
        Assert.NotNull((await svc.ReportAsync(5, new EngineScheduleReport
        { Rule = "* 50%", Engines = [new() { Name = "A", Slot = 1 }, new() { Name = "B", Slot = 1 }] })).Error);
        Assert.NotNull((await svc.ReportAsync(5, new EngineScheduleReport
        { Rule = "* 50%", Engines = [new() { Name = "A", Slot = 1 }, new() { Name = "a", Slot = 2 }] })).Error);
    }

    [Fact]
    public async Task Meldung_zweimal_aktualisiertStattVerdoppelt()
    {
        await Report("* 50%", "background", "PC", "PC Hintergrund");
        await Report("Sa,So 100%", "all", "PC", "PC Hintergrund");

        var rows = await _db.EngineClientSchedules.OrderBy(s => s.Slot).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Sa,So 100%", r.Rule));
        Assert.All(rows, r => Assert.Equal("all", r.Scope));
        Assert.All(rows, r => Assert.Equal(2, r.Total));
    }
}
