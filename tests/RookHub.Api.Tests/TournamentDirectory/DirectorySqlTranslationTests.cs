using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>
/// Prueft die SQL-UEBERSETZUNG der Abfragen, die eine Sortierung oder einen Ausdruck enthalten,
/// den der MySQL-Provider kennen muss.
///
/// <para><b>Warum das noetig ist:</b> alle uebrigen Tests laufen gegen die InMemory-Datenbank, also
/// gegen LINQ-to-Objects — dort funktioniert JEDER Ausdruck. Ein nicht uebersetzbarer Ausdruck
/// fliegt erst gegen echtes MariaDB, und zwar zur LAUFZEIT im Endpunkt (siehe die Warnung in
/// CLAUDE.md). `ToQueryString()` erzeugt das SQL, ohne eine Verbindung zu brauchen: die Uebersetzung
/// laesst sich damit ohne Datenbank festnageln.</para>
/// </summary>
public class DirectorySqlTranslationTests
{
    /// <summary>Ein Kontext auf dem ECHTEN Provider. Es wird nie verbunden — nur uebersetzt;
    /// die Server-Fassung ist deshalb fest angegeben (Autoerkennung braeuchte den Server).</summary>
    private static AppDbContext MySqlContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql("server=localhost;database=x;user=x;password=x", DbServerVersion.Current)
        .Options);

    [Fact]
    public void VereinsAufloesung_ohnePinZuerst_laesstSichUebersetzen()
    {
        using var db = MySqlContext();

        // Genau die Sortierung aus VenueDisambiguationService.RunAsync.
        var sql = db.TournamentDirectoryEntries
            .Where(e => e.RemovedAt == null
                        && (e.GeoSource == GeoSource.Ambiguous || e.GeoSource == GeoSource.City))
            .OrderByDescending(e => e.Lat == null)
            .ThenBy(e => e.StartDate)
            .Take(50)
            .ToQueryString();

        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        // Die Bedingung muss im SQL stehen und nicht still im Speicher ausgewertet werden.
        Assert.Contains("Lat", sql);
    }

    [Fact]
    public void VerortungsLauf_ohneDeckel_laesstSichUebersetzen()
    {
        using var db = MySqlContext();

        // Der Zweig ohne Deckel (limit <= 0) aus GeocodeMissing: `Take(int.MaxValue)`.
        var sql = db.TournamentDirectoryEntries
            .Where(e => e.RemovedAt == null
                        && e.GeoSource != GeoSource.Manual
                        && e.GeoSource != GeoSource.SourceProvided
                        && e.GeoSource != GeoSource.TeamHint
                        && e.Lat == null)
            .OrderBy(e => e.StartDate)
            .Take(int.MaxValue)
            .ToQueryString();

        Assert.Contains("ORDER BY", sql);
        Assert.DoesNotContain("LIMIT 1000", sql);
    }

    /// <summary>
    /// UX-039: Plausibilitaetsfilter (Laufzeit per <c>AddYears</c>, Spieltermine per EXISTS) und die
    /// Blockreihenfolge ueber der Gruppierung (erst was im Zeitraum beginnt, dann was schon laeuft)
    /// muessen beide in SQL laufen — sonst zaehlt die Seitennavigation falsch. Form wie im Weg
    /// ohne Umkreis von TournamentDirectoryQueryService.SearchAsync.
    /// </summary>
    [Fact]
    public void Verzeichnis_PlausibilitaetUndBlockreihenfolge_lassenSichUebersetzen()
    {
        using var db = MySqlContext();
        var windowStart = new DateOnly(2026, 10, 1);

        var sql = db.TournamentDirectoryEntries
            .Where(e => e.RemovedAt == null)
            .Where(TournamentDirectoryQueryService.Plausible(windowStart))
            .GroupBy(e => e.GroupKey ?? "id:" + e.Id)
            .Select(g => new { Key = g.Key, Start = g.Min(x => x.StartDate), PrimaryId = g.Min(x => x.Id) })
            .OrderBy(g => g.Start == null ? 2 : g.Start < windowStart ? 1 : 0)
            .ThenBy(g => g.Start).ThenBy(g => g.PrimaryId)
            .Skip(50).Take(50)
            .ToQueryString();

        Assert.Contains("DATE_ADD", sql);
        Assert.Contains("EXISTS", sql);
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("LIMIT", sql);
    }

    [Fact]
    public void PolenDetail_offeneKandidaten_laesstSichUebersetzen()
    {
        using var db = MySqlContext();

        // Die neue Fassungs-Bedingung des Polen-Detailabrufs.
        var sql = db.TournamentDirectoryEntries
            .Where(e => e.RemovedAt == null && e.ChessArbiterDetailVersion < 1)
            .OrderBy(e => e.StartDate)
            .ToQueryString();

        Assert.Contains("ChessArbiterDetailVersion", sql);
    }

    [Fact]
    public void Ortsvorschlag_mitUmschrift_laesstSichUebersetzen()
    {
        using var db = MySqlContext();
        var normalized = "munchen";
        var transcribed = "muenchen";
        var transcribedUa = "muenchen";

        // Der Zweig „Umlaute/gemischte Schrift" aus TournamentDirectoryQueryService.SuggestPlacesAsync.
        var sql = db.GeoPlaces
            .Where(g => g.NameNormalized.StartsWith(normalized)
                        || g.NameTranscribed.StartsWith(transcribed) || g.NameTranscribed.StartsWith(transcribedUa))
            .OrderByDescending(g => g.NameNormalized == normalized
                                    || g.NameTranscribed == transcribed || g.NameTranscribed == transcribedUa)
            .ThenByDescending(g => g.Population)
            .ThenBy(g => g.Name)
            .Take(10)
            .ToQueryString();

        // Beide Spalten im WHERE — nicht still im Speicher gefiltert.
        Assert.Contains("WHERE", sql);
        Assert.Contains("`NameNormalized`", sql);
        Assert.Contains("`NameTranscribed`", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
    }

    /// <summary>GA-06 (G1-001): der Ausblende-Filter übersetzt sich in zwei getrennte NOT EXISTS ohne OR — vorher EINE
    /// abhängige Unterabfrage mit OR und innerem EXISTS, die MariaDB je Kandidatenzeile über alle Ausblendungen des
    /// Nutzers laufen ließ. Ob der Planer die Indizes wirklich nimmt, zeigt nur EXPLAIN auf MariaDB.</summary>
    [Fact]
    public void Ausblendefilter_ohneOr_zweiNotExists()
    {
        using var db = MySqlContext();

        var sql = db.TournamentDirectoryEntries.WhereNotIgnored(db, 5).Select(e => e.Id).ToQueryString();

        Assert.DoesNotContain(" OR ", sql);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sql, "NOT EXISTS").Count);
    }

    /// <summary>GA-06: die Umformung behält das Verhalten — direkt ausgeblendet, über eine andere Gruppe desselben Turniers
    /// ausgeblendet, und zwei Einträge OHNE Gruppenschlüssel sind keine Geschwister (null ist kein gemeinsamer Schlüssel).</summary>
    [Fact]
    public async Task Ausblendefilter_DirektGruppeUndOhneGruppe()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        TournamentDirectoryEntry E(string id, string? group) => new()
        {
            PublicId = id, ChessResultsId = id, Name = "T" + id, GroupKey = group, FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        };
        db.TournamentDirectoryEntries.AddRange(E("1", null), E("2", null), E("3", "g"), E("4", "g"), E("5", "h"));
        db.TournamentDirectoryIgnores.AddRange(
            new TournamentDirectoryIgnore { UserId = 5, PublicId = "1" },    // ohne Gruppe: nur er selbst
            new TournamentDirectoryIgnore { UserId = 5, PublicId = "3" },    // Gruppe g: 3 und 4
            new TournamentDirectoryIgnore { UserId = 6, PublicId = "5" });   // fremder Nutzer: zählt nicht
        await db.SaveChangesAsync();

        var visible = await db.TournamentDirectoryEntries.WhereNotIgnored(db, 5).Select(e => e.PublicId).OrderBy(x => x).ToListAsync();

        Assert.Equal(["2", "5"], visible);
    }
}
