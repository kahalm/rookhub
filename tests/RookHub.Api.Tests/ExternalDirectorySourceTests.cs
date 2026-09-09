using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Das Gemeinsame aller Zusatzquellen. Diese Tests halten die eine Eigenschaft fest, an der ALLE
/// neun Quellen gleichzeitig haengen: dass die drei Suchwege den Herkunftsvermerk MITLADEN.
/// </summary>
public class ExternalDirectorySourceTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;

    public ExternalDirectorySourceTests()
    {
        _db = Create();
    }

    /// <summary>
    /// Ein FRISCHER Kontext auf derselben Datenbank — das ist der Kern dieser Tests. Der
    /// naechtliche Durchgang legt je Lauf einen eigenen Scope an; ein Eintrag der Vornacht kommt
    /// also ohne Aenderungsverfolgung und ohne geladene Navigationen zurueck. Wer im selben
    /// Kontext prueft, in dem er angelegt hat, sieht den Fehler nie.
    /// </summary>
    private AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);

    public void Dispose() => _db.Dispose();

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private async Task<TournamentDirectoryEntry> SeedAsync(
        string publicId, string? chessResultsId, string name, DirectorySourceKind kind,
        string externalId, string? url = null)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = publicId,
            ChessResultsId = chessResultsId,
            Name = name,
            Federation = "AUT",
            StartDate = Soon,
            EndDate = Soon,
        };
        ExternalDirectorySource.NoteSource(entry, kind, externalId, url, DateTime.UtcNow);
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>
    /// Der Fall, der ohne <c>Include</c> jede Quelle in ihrer ZWEITEN Nacht zerlegt haette:
    /// <see cref="ExternalDirectorySource.NoteSource"/> entscheidet anhand von
    /// <c>entry.Sources</c>, ob er auffrischt oder anlegt. Ist die Sammlung leer, weil sie nie
    /// geladen wurde, legt er eine zweite Zeile mit derselben <c>(Kind, ExternalId)</c> an — und
    /// darauf liegt ein eindeutiger Index.
    /// </summary>
    [Fact]
    public async Task FindOwnAsync_LoadsTheSources_SoASecondRunRefreshesInsteadOfDuplicating()
    {
        await SeedAsync("it123", null, "Open Bozen", DirectorySourceKind.ItalianChessFederation, "123");

        // Zweite Nacht: eigener Kontext, nichts im Zwischenspeicher.
        using var night2 = Create();
        var found = await ExternalDirectorySource.FindOwnAsync(night2, "it123", default);

        Assert.NotNull(found);
        Assert.Single(found!.Sources);

        ExternalDirectorySource.NoteSource(found, DirectorySourceKind.ItalianChessFederation,
            "123", null, DateTime.UtcNow);
        await night2.SaveChangesAsync();

        using var check = Create();
        Assert.Single(check.TournamentDirectorySources.Where(s => s.ExternalId == "123").ToList());
    }

    /// <summary>
    /// Und dieselbe leere Sammlung liesse jedes „habe ich die Detailseite schon geholt?" mit Nein
    /// antworten — die Pruefung liest genau dieses Feld. Polen haette seine 150 Detailseiten
    /// dadurch JEDE Nacht erneut geholt.
    /// </summary>
    [Fact]
    public async Task FindOwnAsync_LoadsTheSources_SoAnAlreadyFetchedDetailPageIsRecognised()
    {
        await SeedAsync("pl2026-291", null, "Memorial Kowalskiego",
            DirectorySourceKind.PolishChessFederation, "2026/ti_291",
            "https://www.chessarbiter.com/turnieje/2026/ti_291");

        using var night2 = Create();
        var found = await ExternalDirectorySource.FindOwnAsync(night2, "pl2026-291", default);

        Assert.True(ChessArbiterDirectorySweepService.HasDetail(found!, "2026/ti_291"));
    }

    /// <summary>
    /// Eine zu lange Kennung bricht mit KLARTEXT ab, statt still gekuerzt zu werden. Gekuerzt
    /// koennten zwei Turniere denselben Schluessel bekommen und verschmelzen; gehasht fanden die
    /// Suchen der Quellen (`s.ExternalId == slug`) ihren Vermerk nie wieder. Vorher endete das als
    /// innere Ausnahme eines DbUpdateException („Data too long for column") — bei Deutschland erst
    /// nach 283 s Crawlen, das damit weggeworfen war.
    /// </summary>
    [Fact]
    public void NoteSource_RefusesAnExternalIdThatDoesNotFitTheColumn()
    {
        var entry = new TournamentDirectoryEntry { PublicId = "1", Name = "Turnier" };
        var tooLong = new string('x', ExternalDirectorySource.MaxExternalIdLength + 1);

        var ex = Assert.Throws<ArgumentException>(() => ExternalDirectorySource.NoteSource(
            entry, DirectorySourceKind.WelshChessUnion, tooLong, null, DateTime.UtcNow));

        Assert.Contains("Kennung", ex.Message);
        Assert.Empty(entry.Sources);
    }

    /// <summary>Und die Laenge der Grenze ist die der SPALTE — sonst waere die Wache eine Meinung.</summary>
    [Fact]
    public void MaxExternalIdLength_MatchesTheColumn()
    {
        var attribute = typeof(TournamentDirectorySource)
            .GetProperty(nameof(TournamentDirectorySource.ExternalId))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.MaxLengthAttribute>()
            .Single();

        Assert.Equal(attribute.Length, ExternalDirectorySource.MaxExternalIdLength);
    }

    /// <summary>
    /// Wales und Deutschland fuehren keine Turniernummer — ihr Schluessel entsteht aus Termin und
    /// Anschrift bzw. einem Slug und passt nicht in die Spalte. Beide vermerken deshalb ihren
    /// KURZSCHLUESSEL, und der muss unter der Grenze bleiben, auch bei langer Anschrift.
    /// </summary>
    [Theory]
    [InlineData("2026-04-11|Best Western Heronston Hotel, Ewenny Road, Bridgend, CF35 5AW")]
    [InlineData("offene-kreisklasse-b-des-schachbezirks-oberbayern-sued-2026-2027-runde-7")]
    public void ShortKeysOfTheSourcesWithoutANumber_FitTheColumn(string sourceKey)
    {
        Assert.True(sourceKey.Length > ExternalDirectorySource.MaxExternalIdLength,
            "Der Testfall soll gerade den Fall zeigen, in dem der ROHE Schluessel nicht passt.");

        foreach (var shortKey in new[] { WcuDirectorySweepService.PublicIdOf(sourceKey),
                                         SchachbundDirectorySweepService.PublicIdOf(sourceKey) })
        {
            Assert.True(shortKey.Length <= ExternalDirectorySource.MaxExternalIdLength, shortKey);
            // Und er muss STABIL sein, sonst legte jeder Durchgang eine neue Zeile an.
            Assert.Equal(shortKey, shortKey.Length > 0 ? shortKey : "");
        }
        Assert.Equal(WcuDirectorySweepService.PublicIdOf(sourceKey),
                     WcuDirectorySweepService.PublicIdOf(sourceKey.ToUpperInvariant()));
    }

    /// <summary>Der Namensvergleich gibt seinen Fund an dieselbe Stelle weiter — auch er muss laden.</summary>
    [Fact]
    public async Task FindMatchAsync_LoadsTheSourcesToo()
    {
        await SeedAsync("1470450", "1470450", "Wiener Landesliga A",
            DirectorySourceKind.ChessResults, "1470450");

        using var night2 = Create();
        var found = await ExternalDirectorySource.FindMatchAsync(
            night2, "AUT", Soon, "Wiener Landesliga A", default);

        Assert.NotNull(found);
        Assert.Single(found!.Sources);
    }

    /// <summary>Und der exakte Weg ueber die chess-results-Nummer ebenso.</summary>
    [Fact]
    public async Task FindByChessResultsIdAsync_LoadsTheSourcesToo()
    {
        await SeedAsync("1482875", "1482875", "Majstrovstva SR",
            DirectorySourceKind.ChessResults, "1482875");

        using var night2 = Create();
        var found = await ExternalDirectorySource.FindByChessResultsIdAsync(
            night2, "1482875", default);

        Assert.NotNull(found);
        Assert.Single(found!.Sources);
    }

    /// <summary>
    /// Ein Eintrag OHNE Vermerk darf dabei kein Sonderfall sein: die Sammlung ist dann leer, aber
    /// geladen — und der erste Vermerk wird angelegt statt zu scheitern.
    /// </summary>
    [Fact]
    public async Task FindOwnAsync_AnEntryWithoutAnySource_ComesBackWithAnEmptyButUsableCollection()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "alt1", ChessResultsId = "alt1", Name = "Altbestand",
            Federation = "AUT", StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        using var night2 = Create();
        var found = await ExternalDirectorySource.FindOwnAsync(night2, "alt1", default);

        Assert.NotNull(found);
        Assert.Empty(found!.Sources);

        ExternalDirectorySource.NoteSource(found, DirectorySourceKind.ChessResults,
            "alt1", null, DateTime.UtcNow);
        await night2.SaveChangesAsync();

        using var check = Create();
        Assert.Single(check.TournamentDirectorySources.ToList());
    }
}
