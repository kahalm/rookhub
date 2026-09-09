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
