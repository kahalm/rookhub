using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Rückfallschutz für das Tabellensplitting <see cref="Book"/> / <see cref="BookSource"/> (0.508.2).
///
/// <para><b>Der Vorfall:</b> die Prod-API lief auf 23 GB RAM. <c>Books.SourcePgn</c> (LONGTEXT, das
/// komplette Roh-PGN; Ø ~480 KB, bis 6 MB) hing an <see cref="Book"/> und kam deshalb mit JEDEM
/// <c>.Include(bp =&gt; bp.Book)</c> mit — bei <c>GET /api/courses/{id}/puzzles</c> in jeder Puzzle-Zeile:
/// 6 MB × 1.881 Linien = 11 GB aus der DB für einen Request (35 s). Der öffentliche Kurs-Pfad war
/// sogar ohne Login erreichbar.</para>
///
/// <para>Die InMemory-Tests sehen davon nichts (dort gibt es kein SQL und keine Spalten). Diese Tests
/// übersetzen die kritischen Abfragen mit dem ECHTEN MySQL-Provider per <c>ToQueryString()</c> — ohne
/// Verbindung — und prüfen, dass <c>SourcePgn</c> nicht im SQL steht.</para>
/// </summary>
public class BookSourceSplitSqlTests
{
    /// <summary>Kontext auf dem echten Provider; es wird nie verbunden, nur übersetzt.</summary>
    private static AppDbContext MySqlContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql("server=localhost;database=x;user=x;password=x", new MySqlServerVersion(new Version(11, 0, 0)))
        .Options);

    [Fact]
    public void KursPuzzles_GetAllPuzzles_LaedtBuchOhneSourcePgn()
    {
        using var db = MySqlContext();

        // Genau die Abfrage aus CourseService.GetAllPuzzlesAsync (GET /api/courses/{id}/puzzles).
        var sql = CourseService.PuzzlesWithBookInReadingOrder(db, 430).ToQueryString();

        Assert.Contains("JOIN `Books`", sql);        // die Buch-Metadaten kommen weiterhin mit …
        Assert.Contains("`DisplayName`", sql);
        Assert.DoesNotContain("SourcePgn", sql);     // … das Roh-PGN nicht mehr
    }

    [Fact]
    public void OeffentlicherKurs_mitPaging_LaedtBuchOhneSourcePgn()
    {
        using var db = MySqlContext();

        // CourseService.GetPublicCoursePuzzlesAsync — anonym erreichbar, mit DB-seitigem Paging.
        var sql = CourseService.PuzzlesWithBookInReadingOrder(db, 340).Skip(50).Take(50).ToQueryString();

        Assert.Contains("LIMIT", sql);
        Assert.Contains("`DisplayName`", sql);
        Assert.DoesNotContain("SourcePgn", sql);
    }

    [Fact]
    public void EinzelPuzzle_GetById_LaedtBuchOhneSourcePgn()
    {
        using var db = MySqlContext();

        // BookPuzzleService.GetByIdAsync (Teilen-Links, Tagespuzzle, OG-Vorschau, Bot-Lookup).
        var sql = BookPuzzleService.PuzzleWithBook(db, 1).ToQueryString();

        Assert.Contains("`IsCalculation`", sql);
        Assert.DoesNotContain("SourcePgn", sql);
    }

    [Fact]
    public void IncludeBookMuster_UndGanzeBuecher_LadenKeinSourcePgn()
    {
        using var db = MySqlContext();

        // Das allgemeine Muster (WeeklyPostService, Pools, Tagespuzzle) und ganze Book-Entitäten.
        var viaPuzzle = db.BookPuzzles.Include(bp => bp.Book).Where(bp => !bp.IsInfoOnly).ToQueryString();
        var viaDaily = db.DailyPuzzles.Include(d => d.BookPuzzle!).ThenInclude(bp => bp.Book).ToQueryString();
        var buecher = db.Books.Where(b => b.OwnerUserId == 1).ToQueryString();

        Assert.All(new[] { viaPuzzle, viaDaily, buecher }, sql =>
        {
            Assert.Contains("`FileName`", sql);
            Assert.DoesNotContain("SourcePgn", sql);
        });
    }

    [Fact]
    public void IncludeSource_LaedtSourcePgn_Gegenprobe()
    {
        using var db = MySqlContext();

        // Gegenprobe: wer den Text wirklich braucht, bekommt ihn — aber nur ausdrücklich.
        var mitInclude = db.Books.Include(b => b.Source).Where(b => b.Id == 1).ToQueryString();
        var nurText = db.BookSources.Where(s => s.Id == 1).Select(s => s.SourcePgn).ToQueryString();

        Assert.Contains("SourcePgn", mitInclude);
        Assert.Contains("SourcePgn", nurText);
        Assert.Contains("FROM `Books`", nurText);    // dieselbe Tabelle — kein Schemaeingriff
        Assert.DoesNotContain("`DisplayName`", nurText);
    }

    [Fact]
    public void Modell_BookHatKeineSpalteSourcePgn_BookSourceTeiltDieZeile()
    {
        using var db = MySqlContext();

        var book = db.Model.FindEntityType(typeof(Book))!;
        var source = db.Model.FindEntityType(typeof(BookSource))!;

        // Käme SourcePgn als Property an Book zurück, mappte EF die Spalte still auf BEIDE Typen
        // (geteilte Spalte) — und jedes Include(bp => bp.Book) lüde wieder den ganzen Text.
        Assert.DoesNotContain(book.GetProperties(), p => p.GetColumnName() == "SourcePgn");
        Assert.Equal("Books", source.GetTableName());
        Assert.Equal("SourcePgn", source.FindProperty(nameof(BookSource.SourcePgn))!.GetColumnName());
        Assert.True(book.FindNavigation(nameof(Book.Source))!.ForeignKey.IsRequiredDependent);
    }
}
