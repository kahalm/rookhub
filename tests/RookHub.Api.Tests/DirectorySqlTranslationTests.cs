using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

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
        .UseMySql("server=localhost;database=x;user=x;password=x", new MariaDbServerVersion(new Version(11, 4)))
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
}
