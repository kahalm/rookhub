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

    private async Task AddFestivalAsync(string chessResultsId, string name)
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = chessResultsId, ChessResultsId = chessResultsId, Name = name,
            Federation = "ITA", StartDate = Bolzano.AddDays(-1), EndDate = Bolzano.AddDays(-1),
            LocationText = "Praha", FirstSeenAt = Now, LastSeenAt = Now,
        });
        await _db.SaveChangesAsync();
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

    // ----- Bedenkzeit als Unterscheider -------------------------------------

    /// <summary>
    /// Der Fall aus der Nacht zum 2026-09-10: alle DREI tschechischen Zeilen des „UCT Chess
    /// Festival" liefen auf den Rapid-Eintrag, obwohl es Blitz, Rapid und Standard je als eigenen
    /// chess-results-Eintrag gibt — gleicher Termin, gleicher Name bis auf das Bedenkzeit-Wort.
    /// „blitz", „rapid" und „standard" stehen in der Fuellwortliste, der Abgleich hatte dort also
    /// gar keinen Unterscheider mehr.
    /// </summary>
    [Fact]
    public async Task VerschiedeneBedenkzeit_wirdNichtZusammengefuehrt()
    {
        await AddFestivalAsync("1474368", "UCT Chess Festival 09/2026 -Rapid");

        Assert.Null(await FindAsync("UCT Chess Festival 09/2026 -Blitz", "Praha"));
        Assert.Null(await FindAsync("UCT Chess Festival 09/2026- standard", "Praha"));
    }

    /// <summary>Und der richtige Teil trifft weiterhin.</summary>
    [Fact]
    public async Task GleicheBedenkzeit_wirdWeiterZusammengefuehrt()
    {
        await AddFestivalAsync("1474368", "UCT Chess Festival 09/2026 -Rapid");

        var found = await FindAsync("UCT Chess Festival 09/2026 -Rapid", "Praha");
        Assert.NotNull(found);
        Assert.Equal("1474368", found!.ChessResultsId);
    }

    /// <summary>Nennt nur EINE Seite eine Bedenkzeit, entscheidet weiter der Wortvergleich.</summary>
    [Fact]
    public async Task NurEineSeiteMitBedenkzeit_bleibtEinTreffer()
    {
        await AddFestivalAsync("1474368", "UCT Chess Festival 09/2026");

        Assert.NotNull(await FindAsync("UCT Chess Festival 09/2026 -Rapid", "Praha"));
    }

    [Theory]
    [InlineData("24 Stunden Blitzturnier", TournamentSpeed.Blitz)]
    [InlineData("Offenes Schnellschachturnier", TournamentSpeed.Rapid)]
    [InlineData("Rapid Open Praha", TournamentSpeed.Rapid)]
    // Italienisch: „semilampo" ist Schnellschach und endet auf „lampo" (Blitz) — die Reihenfolge
    // der Liste entscheidet.
    [InlineData("TORNEO SEMILAMPO SCACCHI Bellante", TournamentSpeed.Rapid)]
    [InlineData("Torneo lampo di Natale", TournamentSpeed.Blitz)]
    [InlineData("Turniej szachów szybkich", TournamentSpeed.Rapid)]
    [InlineData("Turniej błyskawiczny", TournamentSpeed.Blitz)]
    [InlineData("Bleskový turnaj", TournamentSpeed.Blitz)]
    [InlineData("Villámsakk Kupa", TournamentSpeed.Blitz)]
    [InlineData("Standard FIDE Open", TournamentSpeed.Standard)]
    public void SpeedWordOf_findetDieKlasseImNamen(string name, TournamentSpeed expected)
    {
        Assert.Equal(expected, ExternalDirectorySource.SpeedWordOf(name));
    }

    /// <summary>Kein Bedenkzeit-Wort heisst NULL und nicht „Standard" — sonst gaebe es Vetos,
    /// wo nichts widerspricht.</summary>
    [Theory]
    [InlineData("Pierwszy Krok 2026")]
    [InlineData("Grand Prix Polonii")]
    [InlineData("Memoriał Zamenhofa")]
    public void SpeedWordOf_ohneAngabe_istNull(string name)
    {
        Assert.Null(ExternalDirectorySource.SpeedWordOf(name));
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
