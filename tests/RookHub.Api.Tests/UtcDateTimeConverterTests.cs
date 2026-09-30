using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// A9-002: aus MariaDB gelesene Zeiten kamen mit <see cref="DateTimeKind.Unspecified"/> zurück und gingen ohne „Z"
/// über die Leitung — der Browser las sie als Ortszeit. Der Dialog der Kalk-Serie zeigte 16:00 statt 18:00 und
/// schickte beim Speichern 14:00Z zurück; jede Bearbeitung zog die Freigabe um den UTC-Versatz vor.
/// InMemory gibt einen Wert so zurück, wie er geschrieben wurde — ein zonenloser Wert steht hier für das, was der
/// MariaDB-Treiber liefert (der Gegenbeweis gegen die echte Datenbank steht in UtcDateTimeSqlTests).
/// </summary>
public class UtcDateTimeConverterTests
{
    private static DbContextOptions<AppDbContext> Options(string name)
        => new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;

    [Fact]
    public void JedeZeitspalte_LiestSichAlsUtcZurueck()
    {
        using var db = new AppDbContext(Options(Guid.NewGuid().ToString()));
        var zeitspalten = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .ToList();

        Assert.NotEmpty(zeitspalten);
        var ohne = zeitspalten.Where(p => p.GetValueConverter() is not UtcDateTimeConverter)
            .Select(p => $"{p.DeclaringType.DisplayName()}.{p.Name}").ToList();
        Assert.True(ohne.Count == 0, "Ohne UTC-Konverter: " + string.Join(", ", ohne));
    }

    [Fact]
    public async Task KalkAusgabe_GeladenerTermin_TraegtDieZone()
    {
        var name = Guid.NewGuid().ToString();
        // So liefert der Treiber den Wert: 18:00 Wien = 16:00 UTC, ohne Zone.
        var publish = new DateTime(2026, 9, 29, 16, 0, 0, DateTimeKind.Unspecified);
        var preview = new DateTime(2026, 9, 28, 16, 0, 0, DateTimeKind.Unspecified);
        await using (var seed = new AppDbContext(Options(name)))
        {
            seed.CalcEditions.Add(new CalcEdition { BookId = 1, Chapter = "Woche A", PublishAt = publish, TesterPreviewAt = preview });
            await seed.SaveChangesAsync();
        }

        // Frischer Kontext: der Wert kommt aus dem Speicher, nicht aus dem Change-Tracker.
        await using var db = new AppDbContext(Options(name));
        var dto = Assert.Single(await new CalcEditionService(db, TestServices.Friends(db)).ListAsync(1));

        Assert.Equal(DateTimeKind.Utc, dto.PublishAt.Kind);
        Assert.Equal(publish, dto.PublishAt);   // derselbe Zeitpunkt, nur gekennzeichnet
        Assert.Equal("\"2026-09-29T16:00:00Z\"", JsonSerializer.Serialize(dto.PublishAt));
        Assert.Equal(DateTimeKind.Utc, dto.TesterPreviewAt!.Value.Kind);
        Assert.Equal("\"2026-09-28T16:00:00Z\"", JsonSerializer.Serialize(dto.TesterPreviewAt));
    }

    [Fact]
    public async Task Projektion_UndLeereSpalte_BleibenRichtig()
    {
        var name = Guid.NewGuid().ToString();
        var until = new DateTime(2026, 9, 29, 17, 30, 0, DateTimeKind.Unspecified);
        await using (var seed = new AppDbContext(Options(name)))
        {
            seed.TournamentMonitors.Add(new TournamentMonitor { UserId = 1, CrawlerTournamentId = "1", ActiveUntil = until });
            await seed.SaveChangesAsync();
        }

        await using var db = new AppDbContext(Options(name));
        var row = await db.TournamentMonitors.Select(m => new { m.ActiveUntil, m.LastCheckedAt }).SingleAsync();

        Assert.Equal(DateTimeKind.Utc, row.ActiveUntil.Kind);
        Assert.Equal(until, row.ActiveUntil);
        Assert.Null(row.LastCheckedAt);
    }

    [Fact]
    public void Schreiben_Ortszeit_WirdNachUtcUmgerechnet_ZonenloserWertBleibt()
    {
        var conv = new UtcDateTimeConverter();
        var local = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 9, 29, 16, 0, 0, DateTimeKind.Unspecified);
        var utc = new DateTime(2026, 9, 29, 16, 0, 0, DateTimeKind.Utc);

        Assert.Equal(local.ToUniversalTime(), (DateTime)conv.ConvertToProvider(local)!);
        Assert.Equal(unspecified, (DateTime)conv.ConvertToProvider(unspecified)!);
        Assert.Equal(utc, (DateTime)conv.ConvertToProvider(utc)!);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)conv.ConvertFromProvider(unspecified)!).Kind);
    }
}
