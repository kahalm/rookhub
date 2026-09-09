using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Schranken gegen FALSCHES ZUSAMMENFUEHREN. Ein Turnier verschwindet bei uns nicht nur, wenn
/// eine Quelle es nicht mehr liefert — es verschwindet auch, wenn wir es einem fremden Eintrag
/// zuschlagen: der Herkunftsvermerk wandert dorthin, und der eigene Eintrag wird als „geht darin
/// auf" zurueckgezogen.
///
/// <para><b>Der Fall, der diese Tests erzwungen hat.</b> Am 2026-09-09 standen drei echte
/// italienische Turniere auf Dev nicht mehr im Verzeichnis — „5° Torneo di scacchi rapid - città di
/// Cormòns", „Torneo Rapid FIDE di settembre di Frascati" und „TORNEO SEMILAMPO SCACCHI Bellante",
/// alle am 20.09. Alle drei hingen am „Torneo Sociale Arci Scacchi Bolzano B" vom 21.09., einem
/// Vereinsturnier, das ueber fuenf Wochen laeuft und damit im Termin-Fenster von jedem
/// Wochenendturnier des Landes liegt. Gemeinsam hatten sie zwei Woerter: „torneo" und
/// „scacchi".</para>
/// </summary>
public class DirectoryMatchGuardTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public DirectoryMatchGuardTests() => ExternalDirectorySource.ResetFillerCache();

    public void Dispose()
    {
        ExternalDirectorySource.ResetFillerCache();
        _db.Dispose();
    }

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateOnly Bolzano = DateOnly.FromDateTime(Now).AddDays(12);
    private const string BolzanoName = "Torneo Sociale Arci Scacchi Bolzano B";
    private const string BolzanoPlace = "Circolo Nikoletti - Via Nazario Sauro 6 - 39100 - Bolzano";
    private const string CormonsName = "5° Torneo di scacchi rapid - città di Cormòns";

    private async Task<TournamentDirectoryEntry> AddBolzanoAsync(string? noteExternalId = null)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = "1492370", ChessResultsId = "1492370", Name = BolzanoName,
            Federation = "ITA", StartDate = Bolzano, EndDate = Bolzano.AddDays(35),
            LocationText = BolzanoPlace, FirstSeenAt = Now, LastSeenAt = Now,
        };
        if (noteExternalId is not null)
            entry.Sources.Add(new TournamentDirectorySource
            {
                Kind = DirectorySourceKind.ItalianChessFederation, ExternalId = noteExternalId,
                FirstSeenAt = Now, LastSeenAt = Now,
            });
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private Task<TournamentDirectoryEntry?> FindAsync(string name, string? place, string externalId = "21699") =>
        ExternalDirectorySource.FindMatchAsync(_db, "ITA", Bolzano.AddDays(-1), name,
            new ExternalDirectorySource.MatchHint(
                DirectorySourceKind.ItalianChessFederation, externalId, place),
            CancellationToken.None);

    [Fact]
    public async Task FremderOrt_wirdNichtZusammengefuehrt()
    {
        await AddBolzanoAsync();

        Assert.Null(await FindAsync(CormonsName, "Cormòns, Gorizia"));
        Assert.Null(await FindAsync("Torneo Rapid FIDE di settembre di Frascati Scacchi",
            "Cocciano - Frascati (RM), Roma"));
        Assert.Null(await FindAsync("TORNEO SEMILAMPO SCACCHI Bellante", "Bellante, Teramo"));
    }

    [Fact]
    public async Task DerselbeOrt_wirdWeiterZusammengefuehrt()
    {
        await AddBolzanoAsync();

        var found = await FindAsync("Torneo Sociale Scacchi Bolzano Gruppe B", "Bolzano");
        Assert.NotNull(found);
        Assert.Equal("1492370", found!.ChessResultsId);
    }

    /// <summary>
    /// Nennt eine Seite keinen Ort, wird nicht widersprochen — dann entscheidet allein der
    /// Namensvergleich. Diese Quellen gibt es wirklich: die niederlaendische nennt nie einen Ort,
    /// die norwegische und die schottische erst auf der Detailseite.
    /// </summary>
    [Fact]
    public async Task OhneOrtsangabe_bleibtDerNamensvergleich()
    {
        await AddBolzanoAsync();

        Assert.NotNull(await FindAsync(CormonsName, null));
    }

    /// <summary>
    /// Und genau dann muss der Haeufigkeitsfilter greifen: in einem echten Bestand sind „torneo"
    /// und „scacchi" keine unterscheidenden Woerter. Gemessen auf Dev: 105 von 345 italienischen
    /// Namen enthalten „torneo", 53 „scacchi".
    /// </summary>
    [Fact]
    public async Task MitEchtemBestand_sindAllerweltswoerterKeinTreffer()
    {
        await AddBolzanoAsync();
        await AddCorpusAsync();
        ExternalDirectorySource.ResetFillerCache();

        Assert.Null(await FindAsync(CormonsName, null));

        // Ein Name mit zwei WIRKLICH unterscheidenden Woertern trifft weiterhin.
        Assert.NotNull(await FindAsync("Sociale Arci Bolzano", null));
    }

    /// <summary>
    /// Die strukturelle Schranke: eine Quelle fuehrt dasselbe Turnier nicht zweimal. Sie haette im
    /// Bolzano-Fall die zweite und dritte Zeile gestoppt, auch ohne jede Ortsangabe.
    /// </summary>
    [Fact]
    public async Task MitFremdemVermerkDerselbenQuelle_wirdNichtZusammengefuehrt()
    {
        await AddBolzanoAsync(noteExternalId: "99999");

        Assert.Null(await FindAsync(CormonsName, null, externalId: "21699"));
    }

    /// <summary>Die EIGENE Kennung darf natuerlich wiedergefunden werden — jede Nacht.</summary>
    [Fact]
    public async Task MitEigenemVermerk_wirdWiedergefunden()
    {
        await AddBolzanoAsync(noteExternalId: "21699");

        Assert.NotNull(await FindAsync(CormonsName, null, externalId: "21699"));
    }

    [Theory]
    // Kein Ort auf einer Seite: kein Widerspruch.
    [InlineData("", "Bolzano", true)]
    [InlineData("Cormòns", null, true)]
    // Nur Beiwerk unter vier Zeichen: kein verwertbarer Beleg, also kein Widerspruch.
    [InlineData("via 6", "Bolzano", true)]
    // Echte Ortswoerter, die sich widersprechen.
    [InlineData("Cormòns, Gorizia", "Circolo Nikoletti - Via Nazario Sauro 6 - 39100 - Bolzano", false)]
    [InlineData("Bellante, Teramo", "Bolzano", false)]
    // Dieselbe Stadt in anderer Schreibweise und Umgebung.
    [InlineData("Cormòns", "Cormons, Gorizia", true)]
    [InlineData("Wien", "Wien, AUT", true)]
    public void PlacesAgree_widersprichtNurBeiEchtenOrtsangaben(string place, string? other, bool expected)
    {
        var normalized = GeoTextNormalizer.Normalize(place);
        Assert.Equal(expected, ExternalDirectorySource.PlacesAgree(normalized, other));
    }

    [Fact]
    public void BuildFiller_findetDieAllerweltswoerter()
    {
        var names = new List<string>();
        for (var i = 0; i < 30; i++) names.Add($"Torneo di scacchi numero {i} - citta di Ort{i}");
        for (var i = 0; i < 20; i++) names.Add($"Campionato Ort{i + 100}");

        var filler = ExternalDirectorySource.BuildFiller(names);

        Assert.Contains("torneo", filler);
        Assert.Contains("scacchi", filler);
        Assert.DoesNotContain("ort1", filler);
        // 20 von 50 Namen sind 40 Prozent — auch das ist ein Fuellwort.
        Assert.Contains("campionato", filler);
    }

    /// <summary>
    /// Unter einer Mindestmenge wird die Haeufigkeit NICHT ausgewertet: bei acht Namen sagt „zwei
    /// von acht" nichts, und ein zu Unrecht gefiltertes Wort kostet einen echten Treffer.
    /// </summary>
    [Fact]
    public void BuildFiller_schweigtBeiKleinemBestand()
    {
        var names = Enumerable.Range(0, 8).Select(i => $"Torneo di scacchi {i}").ToList();

        Assert.Empty(ExternalDirectorySource.BuildFiller(names));
    }

    private async Task AddCorpusAsync()
    {
        for (var i = 0; i < 45; i++)
            _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
            {
                PublicId = $"it-korpus-{i}",
                Name = $"Torneo di scacchi {i} - Ort{i}",
                Federation = "ITA",
                StartDate = Bolzano.AddDays(60 + i),
                EndDate = Bolzano.AddDays(60 + i),
                FirstSeenAt = Now, LastSeenAt = Now,
            });
        await _db.SaveChangesAsync();
    }
}
