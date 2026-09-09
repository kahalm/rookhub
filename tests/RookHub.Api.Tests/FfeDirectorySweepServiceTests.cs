using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des franzoesischen Verbands (FFE).
///
/// <para>Warum die Quelle zaehlt: von 40 gegengeprueften FFE-Turnieren stehen ZWEI auf
/// chess-results (5 %) — die FFE fuehrt die nicht-FIDE-gewerteten Vereins- und Ligue-Turniere,
/// die dort praktisch gar nicht vorkommen. Der teure Teil ist die Turnierseite, und die wird nur
/// fuer noch unbekannte Turniere geholt.</para>
/// </summary>
public class FfeDirectorySweepServiceTests : IDisposable
{
    /// <summary>
    /// Der Name der InMemory-Datenbank, nicht der Kontext: mehrere Kontexte auf demselben Namen
    /// bilden nach, was in Wirklichkeit passiert — jede Nacht ein frischer Scope auf demselben
    /// Bestand. Genau daran haengt die Frage, ob eine schon gelesene Turnierseite als gelesen
    /// erkannt wird.
    /// </summary>
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly List<AppDbContext> _contexts = [];
    private readonly AppDbContext _db;

    public FfeDirectorySweepServiceTests()
    {
        _db = NewContext();
    }

    private AppDbContext NewContext()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options);
        _contexts.Add(context);
        return context;
    }

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
    }

    private RouteHandler _routes = new();

    private FfeDirectorySweepService CreateService(
        string listJson, string? detailJson = null, int batchSize = 150, AppDbContext? db = null)
    {
        _routes = new RouteHandler(listJson, detailJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TournamentDirectory:FfeDetailBatchSize"] = batchSize.ToString(),
        }).Build();

        var context = db ?? _db;
        return new FfeDirectorySweepService(context, new StubClientFactory(_routes),
            new GeocodingService(context), config, new TestLogger<FfeDirectorySweepService>());
    }

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "72680", string? city = "SAINT BRISSON",
        string? department = "58", string? homologatedBy = "FFE") =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}","startDate":"{{Soon:yyyy-MM-dd}}",
           "city":{{Json(city)}},"department":{{Json(department)}},
           "homologatedBy":{{Json(homologatedBy)}},
           "url":"https://www.echecs.asso.fr/FicheTournoi.aspx?Ref={{eventId}}"}
          """;

    private static string Detail(string? address = "Salle de la Maison du Parc 58230 Saint Brisson",
        string? timeControl = "60' + [30'']", int? rounds = 5, string? pairing = "Suisse",
        int endOffsetDays = 1) =>
        $$"""
          {"startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{Soon.AddDays(endOffsetDays):yyyy-MM-dd}}",
           "city":"SAINT BRISSON","department":"58","address":{{Json(address)}},
           "timeControl":{{Json(timeControl)}},
           "rounds":{{(rounds is null ? "null" : rounds.ToString())}},
           "pairingSystem":{{Json(pairing)}}}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    private async Task<TournamentDirectoryEntry> AddSearchEntryAsync(string tnr, string name)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = tnr, ChessResultsId = tnr, Name = name, Federation = "FRA",
            StartDate = Soon, EndDate = Soon.AddDays(1),
        };
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    // ----- Der Regelfall: chess-results kennt das Turnier nicht ---------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Open international du Morvan")}]", Detail()).RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("fr72680", entry.PublicId);
        Assert.Null(entry.ChessResultsId);
        Assert.Equal("FRA", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
    }

    /// <summary>
    /// Die Turnierseite ist teuer (ein Abruf je Turnier) und wird genau dafuer geholt: Enddatum,
    /// Bedenkzeit, Rundenzahl, System — und die Anschrift mit der Postleitzahl. Ohne sie stuende
    /// jedes mehrtaegige Open nur an seinem ersten Tag im Kalender und haette
    /// <see cref="TournamentSpeed.Unknown"/>.
    /// </summary>
    [Fact]
    public async Task RunAsync_DetailPage_AddsWhatTheListDoesNotHave()
    {
        await CreateService($"[{Row("Open international du Morvan")}]", Detail()).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(Soon.AddDays(1), entry.EndDate);
        Assert.Equal("60' + [30'']", entry.TimeControlText);
        Assert.Equal(TournamentSpeed.Standard, entry.Speed);
        Assert.Equal(5, entry.Rounds);
        Assert.Equal(TournamentSystem.Swiss, entry.System);
        Assert.Contains("58230", entry.LocationText);
    }

    /// <summary>
    /// Ohne Turnierseite bleibt der Eintrag stehen — mit dem, was die Liste hergibt. Das Ende ist
    /// dann der Starttag, denn ein erfundenes Ende waere schlechter als gar keins.
    /// </summary>
    [Fact]
    public async Task RunAsync_DetailPageUnreadable_KeepsTheListData()
    {
        var result = await CreateService($"[{Row("Open de Niort")}]", detailJson: null).RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Updated);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(Soon, entry.StartDate);
        Assert.Equal(Soon, entry.EndDate);
        Assert.Equal("SAINT BRISSON (58)", entry.LocationText);
        Assert.Equal(TournamentSpeed.Unknown, entry.Speed);
    }

    /// <summary>
    /// Der Deckel ist die Kostenbremse: Frankreich hat beim ersten Durchgang rund 180 unbekannte
    /// Turniere, und jedes kostet einen eigenen Abruf. Nach ein paar Naechten ist der Bestand
    /// vollstaendig, danach kostet er nur noch, was wirklich neu ist.
    /// </summary>
    [Fact]
    public async Task RunAsync_DetailBudget_IsCapped()
    {
        var rows = string.Join(",", Enumerable.Range(1, 5)
            .Select(i => Row($"Open Nummer {i}", eventId: $"7268{i}")));

        var result = await CreateService($"[{rows}]", Detail(), batchSize: 2).RunAsync();

        Assert.Equal(5, result.Added);
        Assert.Equal(2, result.Updated);
        Assert.Equal(2, _routes.DetailCalls);
    }

    /// <summary>
    /// „Schon geholt" steht in der Adresse des Herkunftsvermerks. Der zweite Durchgang laeuft in
    /// einem FRISCHEN Kontext — so wie in Wirklichkeit jede Nacht — und darf die Turnierseite
    /// nicht erneut holen: sonst kostete Frankreich jede Nacht 180 Abrufe statt einmalig.
    /// </summary>
    [Fact]
    public async Task RunAsync_SecondNight_DoesNotFetchTheDetailPageAgain()
    {
        var json = $"[{Row("Open international du Morvan")}]";
        await CreateService(json, Detail()).RunAsync();

        var second = CreateService(json, Detail(), db: NewContext());
        await second.RunAsync();

        Assert.Equal(0, _routes.DetailCalls);
    }

    /// <summary>
    /// Und er darf auch keinen zweiten Herkunftsvermerk anlegen — die Kennung ist im Bestand
    /// eindeutig (Index ueber Kind + ExternalId), eine zweite Zeile waere ein Schreibfehler gegen
    /// die Datenbank.
    /// </summary>
    [Fact]
    public async Task RunAsync_SecondNight_KeepsExactlyOneSourceNote()
    {
        var json = $"[{Row("Open international du Morvan")}]";
        await CreateService(json, Detail()).RunAsync();
        await CreateService(json, Detail(), db: NewContext()).RunAsync();

        var check = NewContext();
        var sources = check.TournamentDirectorySources
            .Where(s => s.Kind == DirectorySourceKind.FrenchChessFederation).ToList();

        var source = Assert.Single(sources);
        Assert.Equal("72680", source.ExternalId);
        Assert.Contains("FicheTournoi.aspx?Ref=72680", source.Url);
    }

    /// <summary>Der Herkunftsvermerk sagt, woher die Angabe stammt — n-zu-n ueber die Quellen.</summary>
    [Fact]
    public async Task RunAsync_NotesTheSource()
    {
        await CreateService($"[{Row("Open international du Morvan")}]", Detail()).RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.FrenchChessFederation, source.Kind);
        Assert.Equal("72680", source.ExternalId);
    }

    // ----- Der Ausnahmefall: chess-results kennt es doch ----------------------

    /// <summary>
    /// Steht dasselbe Turnier schon unter seiner chess-results-Nummer, bleibt es dabei — die FFE
    /// legt keinen zweiten Eintrag an. Der Fall ist bei Frankreich die Ausnahme (2 von 40), und
    /// die teure Turnierseite wird dafuer nicht geholt: der Eintrag steht ja schon.
    /// </summary>
    [Fact]
    public async Task RunAsync_KnownTournament_IsOnlyNoted()
    {
        await AddSearchEntryAsync("1490241", "Open de Niort Accession");

        var result = await CreateService($"[{Row("Open de Niort Accession")}]", Detail()).RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, _routes.DetailCalls);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>
    /// Holt die Turniersuche einen eigenen Eintrag ein, wird der eigene zurueckgezogen — sonst
    /// stuende dieselbe Veranstaltung unter zwei Kennungen im Kalender.
    /// </summary>
    [Fact]
    public async Task RunAsync_SearchCatchesUp_RetiresOwnEntry()
    {
        var json = $"[{Row("Open de Niort Accession")}]";
        await CreateService(json, Detail()).RunAsync();
        await AddSearchEntryAsync("1490241", "Open de Niort Accession");

        var result = await CreateService(json, Detail(), db: NewContext()).RunAsync();

        Assert.Equal(1, result.Retired);
        var check = NewContext();
        var own = check.TournamentDirectoryEntries.Single(e => e.PublicId == "fr72680");
        Assert.NotNull(own.RemovedAt);
    }

    [Fact]
    public async Task RunAsync_CrawlerError_Throws() =>
        await Assert.ThrowsAnyAsync<Exception>(() => CreateService("kaputt").RunAsync());

    [Fact]
    public async Task RunAsync_EmptyCalendar_DoesNothing()
    {
        var result = await CreateService("[]").RunAsync();

        Assert.Equal(0, result.Read);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    // ----- Die Bausteine -----------------------------------------------------

    /// <summary>
    /// Das Departement ist der einzige Unterscheider, den die LISTE mitbringt. Frankreich hat
    /// reichlich gleichnamige Orte; ohne ihn liefert der Namensweg des Geocoders dort
    /// <see cref="GeoSource.Ambiguous"/> und setzt gar keinen Pin.
    /// </summary>
    [Theory]
    [InlineData("SAINT BRISSON", "58", "SAINT BRISSON (58)")]
    [InlineData("SAINT GIRONS", "09", "SAINT GIRONS (09)")]
    [InlineData("PARIS", null, "PARIS")]
    [InlineData(null, "58", null)]
    [InlineData(null, null, null)]
    public void LocationOf_CombinesCityAndDepartment(string? city, string? department, string? expected) =>
        Assert.Equal(expected, FfeDirectorySweepService.LocationOf(
            new FfeDirectorySweepService.CrawlerFfeEvent("1", "n", Soon, city, department, null, null)));

    /// <summary>
    /// Die Anschrift ist der Ortstext der Turnierseite — sie traegt die Postleitzahl. Der
    /// Ortsname haengt nur dann hinten dran, wenn die Anschrift ihn nicht selbst nennt (gemessen
    /// an „Espace Kerourgue 53 rue de Kerourgue", die ohne Ort auskam).
    /// </summary>
    [Theory]
    [InlineData("Salle des Charruauds rue Max Linder 33500 Libourne", "LIBOURNE",
        "Salle des Charruauds rue Max Linder 33500 Libourne")]
    [InlineData("Espace Kerourgue 53 rue de Kerourgue", "FOUESNANT",
        "Espace Kerourgue 53 rue de Kerourgue, FOUESNANT")]
    [InlineData("Espace des Parapluies\n18 rue Saint-Lambert\n54280 Seichamps", "SEICHAMPS",
        "Espace des Parapluies 18 rue Saint-Lambert 54280 Seichamps")]
    [InlineData(null, "NIORT", null)]
    [InlineData("2 rue Bernard Cathelin 26200 Montelimar", null,
        "2 rue Bernard Cathelin 26200 Montelimar")]
    public void AddressOf_KeepsThePostalCodeAndAddsTheCityOnlyIfMissing(
        string? address, string? city, string? expected) =>
        Assert.Equal(expected, FfeDirectorySweepService.AddressOf(address, city));

    /// <summary>
    /// Eine nackte „58" saehe in der Oberflaeche neben lauter Bundeslaendern wie ein Fehler aus.
    /// Die Ligue-Spalte der Quelle taugt als Region NICHT — sie sagt, wer die Wertung fuehrt.
    /// </summary>
    [Theory]
    [InlineData("58", "Dept. 58")]
    [InlineData("09", "Dept. 09")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void RegionOf_NamesTheDepartment(string? department, string? expected) =>
        Assert.Equal(expected, FfeDirectorySweepService.RegionOf(department));

    /// <summary>
    /// „S.A.D." (Systeme Accelere Degressif) und „Haley" sind Beschleunigungsverfahren INNERHALB
    /// des Schweizer Systems, keine eigene Turnierform. Was nicht erkannt wird, laesst den
    /// bisherigen Wert stehen — raten waere schlechter als schweigen.
    /// </summary>
    [Theory]
    [InlineData("Suisse", TournamentSystem.Swiss)]
    [InlineData("S.A.D.", TournamentSystem.Swiss)]
    [InlineData("Haley", TournamentSystem.Swiss)]
    [InlineData("Toutes Rondes", TournamentSystem.RoundRobin)]
    [InlineData("Irgendwas", TournamentSystem.Unknown)]
    [InlineData("", TournamentSystem.Unknown)]
    [InlineData(null, TournamentSystem.Unknown)]
    public void SystemOf_ReadsThePairingMethod(string? text, TournamentSystem expected) =>
        Assert.Equal(expected, FfeDirectorySweepService.SystemOf(text, TournamentSystem.Unknown));

    /// <summary>
    /// Die FFE schreibt das Inkrement mit ZWEI APOSTROPHEN. Der gemeinsame Klassifizierer kennt
    /// „sec", „sek", <c>"</c> und <c>″</c> — aber nicht <c>''</c>, und die Stundenform „1h30"
    /// faellt bei ihm ebenfalls durch (das <c>h</c> steht direkt vor einer Ziffer). Ungeuebersetzt
    /// wurde aus einem FIDE-Standardturnier (60 + 30 = 90 Minuten) Schnellschach und aus
    /// „1h30 + [30'']" ein 30-Minuten-Turnier.
    /// </summary>
    [Theory]
    [InlineData("60' + [30'']", "60 min + 30 sec")]
    [InlineData("60' + [30\"]", "60 min + 30 sec")]
    [InlineData("1h30 + [30'']", "90 min + 30 sec")]
    [InlineData("1h30 + [30']", "90 min + 30 sec")]   // Tippfehler des Veranstalters: Klammer = Sekunden
    [InlineData("15' + [10'']", "15 min + 10 sec")]
    [InlineData("10' + [5'']", "10 min + 5 sec")]
    [InlineData("60\' + 30\'\'", "60 min + 30 sec")]   // dieselbe Angabe ohne Klammer
    [InlineData("2h", "120 min")]
    [InlineData("K.O.", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeCadence_TranslatesTheFrenchNotation(string? cadence, string? expected) =>
        Assert.Equal(expected, FfeDirectorySweepService.NormalizeCadence(cadence));

    /// <summary>
    /// Und das Ergebnis muss die richtige Klasse tragen: 60 Minuten plus 30 Sekunden sind nach der
    /// FIDE-Formel 90 Minuten und damit Turnierschach, nicht Schnellschach.
    /// </summary>
    [Theory]
    [InlineData("60' + [30'']", TournamentSpeed.Standard)]
    [InlineData("1h30 + [30'']", TournamentSpeed.Standard)]
    [InlineData("15' + [10'']", TournamentSpeed.Rapid)]
    [InlineData("3' + [2'']", TournamentSpeed.Blitz)]
    [InlineData("K.O.", TournamentSpeed.Unknown)]
    public void NormalizeCadence_LandsInTheRightClass(string cadence, TournamentSpeed expected) =>
        Assert.Equal(expected, TournamentSpeedClassifier.Classify(
            FfeDirectorySweepService.NormalizeCadence(cadence)));

    [Fact]
    public void SystemOf_KeepsTheCurrentValueWhenItCannotTell() =>
        Assert.Equal(TournamentSystem.RoundRobin,
            FfeDirectorySweepService.SystemOf("Irgendwas", TournamentSystem.RoundRobin));

    // ----- Testdoppel --------------------------------------------------------

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>Zwei Routen, ein Handler: die Monatsliste und die Turnierseite.</summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly string _list;
        private readonly string? _detail;

        public RouteHandler(string list = "[]", string? detail = null)
        {
            _list = list;
            _detail = detail;
        }

        public int DetailCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/detail", StringComparison.Ordinal))
            {
                DetailCalls++;
                return Task.FromResult(_detail is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Ok(_detail));
            }

            return Task.FromResult(_list == "kaputt"
                ? new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("") }
                : Ok(_list));
        }

        private static HttpResponseMessage Ok(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
