using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>
/// Golden-Tests der Abgleichschleife der Zusatzquellen: dieselbe Abfolge von Laeufen gegen jede
/// Quelle, und festgehalten wird ALLES, was die Schleife sichtbar macht — Ergebniszahlen,
/// Log-Zeilen samt Stufe, die Eintraege (Termin, Ort, Pin, Zurueckziehen, Fehlschlaege) und die
/// Herkunftsvermerke.
///
/// <para>Entstanden vor der Zerlegung der Sweep-Familie (Codereview 2026-09-29, Z7): die
/// Erwartungen sind gegen die handgeschriebenen Schleifen aufgenommen und muessen nach dem Umbau
/// auf <c>ExternalSweepRunner</c> unveraendert gelten.</para>
///
/// <para>Ablauf je Quelle: (1) Erstlauf mit A, B (verortbarer Ort), C, neun Serienturnieren und
/// einer Zeile U ohne lesbaren Termin; (2) chess-results fuehrt A jetzt auch (A geht dort auf);
/// (3)/(4) C fehlt — ein Fehlschlag, dann die Karenz; (5) nur B (die 90-%-Bremse spricht an);
/// (6) leere Liste; (7)/(8) Speichern scheitert am Ende bzw. mitten im Lauf.</para>
/// </summary>
public class ExternalSweepGoldenTests
{
    /// <summary>
    /// Eine Zeile des Szenarios; <see cref="Start"/> null = unlesbarer Termin. <see cref="Cr"/> ist
    /// eine von der Quelle selbst genannte chess-results-Nummer, <see cref="NonTournament"/> eine
    /// Zeile, die kein Turnier ist (beides nur fuer Quellen, die das kennen).
    /// </summary>
    public sealed record RowSpec(string Id, string Name, DateOnly? Start, string? Place,
        string? Cr = null, bool NonTournament = false);

    /// <summary>
    /// Was die Sonderfall-Laeufe einer Quelle (<see cref="SourceSpec.Extra"/>) nach den acht
    /// gemeinsamen Laeufen tun duerfen: weitere Laeufe, Bestand anlegen, Detailantworten je Kennung
    /// setzen, eigene Zeilen in die Aufnahme schreiben.
    /// </summary>
    public sealed record ScenarioSteps(
        Func<string, RowSpec[], Task> Step, Func<AppDbContext> NewDb, MutableHandler Handler,
        Action<string> Note);

    /// <summary>
    /// Eine Quelle im Test: wie man sie baut und wie ihre Crawler-Zeile aussieht. <c>DetailJson</c>
    /// ist die Antwort auf jeden Detailabruf (Pfad endet auf <c>/detail</c>); ohne ihn 404.
    /// <c>ShowDetailCalls</c> schreibt je Lauf die abgerufenen Detailseiten mit; <c>Extra</c> sind
    /// die Sonderfall-Laeufe der Quelle nach den acht gemeinsamen.
    /// </summary>
    public sealed record SourceSpec(
        string Name, string Federation, string GeoCountry,
        Func<AppDbContext, IHttpClientFactory, (Func<Task<ExternalSweepResult>> Run, Func<List<string>> Log)> Create,
        Func<RowSpec, string> RowJson, string? DetailJson = null,
        bool ShowDetailCalls = false, Func<ScenarioSteps, Task>? Extra = null);

    /// <summary>Ein Samstag in rund fuenf Wochen — fester Wochentag, damit „Wochenende" nicht vom Testtag abhaengt.</summary>
    private static readonly DateOnly Soon = NextSaturday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

    private static DateOnly NextSaturday(DateOnly d) => d.AddDays(((int)DayOfWeek.Saturday - (int)d.DayOfWeek + 7) % 7);

    private static string J(string? v) => v is null ? "null" : $"\"{v}\"";
    private static string D(DateOnly? d) => d is { } x ? $"\"{x:yyyy-MM-dd}\"" : "\"kein Datum\"";

    private static (Func<Task<ExternalSweepResult>>, Func<List<string>>) Wrap<T>(
        TestLogger<T> log, Func<Task<ExternalSweepResult>> run) =>
        (run, () => log.Messages.Select((m, i) => $"{log.Levels[i]}: {m}").ToList());

    public static readonly Dictionary<string, SourceSpec> Sources = new()
    {
        ["Frsah"] = new("Frsah", "ROU", "RO",
            (db, http) =>
            {
                var log = new TestLogger<FrsahDirectorySweepService>();
                var svc = new FrsahDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"url":"https://frsah.ro/event/{{r.Id}}/","place":{{J(r.Place)}},"city":null,"postalCode":null,"country":"Romania"}"""),
        ["Wcu"] = new("Wcu", "WLS", "GB",
            (db, http) =>
            {
                var log = new TestLogger<WcuDirectorySweepService>();
                var svc = new WcuDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"place":{{J(r.Place)}},"url":"https://www.welshchessunion.uk/calendar/"}"""),
        ["Cfc"] = new("Cfc", "CAN", "CA",
            (db, http) =>
            {
                var log = new TestLogger<CfcDirectorySweepService>();
                var svc = new CfcDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"place":{{J(r.Place)}},"province":"ON","url":"https://www.chess.ca/en/events/"}"""),
        ["Szs"] = new("Szs", "SLO", "SI",
            (db, http) =>
            {
                var log = new TestLogger<SzsDirectorySweepService>();
                var svc = new SzsDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","date":{{D(r.Start)}},"postalCode":null,"place":{{J(r.Place)}},"cancelled":false}"""),
        ["Ecf"] = new("Ecf", "ENG", "GB",
            (db, http) =>
            {
                var log = new TestLogger<EcfDirectorySweepService>();
                var svc = new EcfDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"url":"https://www.englishchess.org.uk/event/{{r.Id}}/","place":{{J(r.Place)}},"city":{{J(r.Place)}},"postalCode":null,"country":null,"lat":null,"lon":null,"categories":["ECF Rated"],"website":null}"""),
        ["ChessHu"] = new("ChessHu", "HUN", "HU",
            (db, http) =>
            {
                var log = new TestLogger<ChessHuDirectorySweepService>();
                var svc = new ChessHuDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"place":{{J(r.Place)}},"hasVenue":{{(r.Place is null ? "false" : "true")}},"fideRated":true}"""),
        ["Knsb"] = new("Knsb", "NED", "NL",
            (db, http) =>
            {
                var log = new TestLogger<KnsbDirectorySweepService>();
                var svc = new KnsbDirectorySweepService(db, http, log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"slug":"toernooi-{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"url":"https://schaakbond.nl/event/toernooi-{{r.Id}}/","speed":"Rapidschaak","online":false}"""),
        ["Fsi"] = new("Fsi", "ITA", "IT",
            (db, http) =>
            {
                var log = new TestLogger<FsiDirectorySweepService>();
                var svc = new FsiDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync(18));
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"region":"LAZIO","province":"Roma","place":{{J(r.Place)}},"eventType":"Torneo Elo Italia/FIDE","timeControl":"90 minuti + 30 secondi","rounds":7,"note":null}"""),
        ["Schachbund"] = new("Schachbund", "GER", "DE",
            (db, http) =>
            {
                var log = new TestLogger<SchachbundDirectorySweepService>();
                var svc = new SchachbundDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"region":"bayern","place":{{J(r.Place)}},"timeControl":"12 Minuten + 3 Sekunden/Zug","rounds":5,"system":"swiss","url":"https://www.schachbund.de/turnierdetails/{{r.Id}}.html"}"""),
        ["Sjakk"] = new("Sjakk", "NOR", "NO",
            (db, http) =>
            {
                var log = new TestLogger<SjakkDirectorySweepService>();
                var svc = new SjakkDirectorySweepService(db, http, new GeocodingService(db), Config(), log);
                // Fuenf Detailseiten je Lauf: weniger als Zeilen, damit der Deckel mitspielt.
                return Wrap(log, () => svc.RunAsync(5));
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"url":"https://www.sjakk.no/aktiviteter/{{r.Id}}"}""",
            """{"venue":"Testhausen","organizer":"Testhausen Sjakklubb","timeControl":"3 min pluss 2 sekunder","rounds":12,"system":"swiss","website":null}"""),
        ["ChessScotland"] = new("ChessScotland", "SCO", "GB",
            (db, http) =>
            {
                var log = new TestLogger<ChessScotlandDirectorySweepService>();
                var svc = new ChessScotlandDirectorySweepService(db, http, new GeocodingService(db), Config(), log);
                return Wrap(log, () => svc.RunAsync(5));
            },
            r => $$"""{"slug":"slug-{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"url":"https://www.chessscotland.com/calendar/slug-{{r.Id}}","categories":["Adult","Junior"],"timeControls":["Standard","Fide"]}""",
            """{"venue":"Community Hall, Testhausen"}"""),
        ["Icu"] = new("Icu", "IRL", "IE",
            (db, http) =>
            {
                var log = new TestLogger<IcuDirectorySweepService>();
                var svc = new IcuDirectorySweepService(db, http, new GeocodingService(db),
                    Config(("TournamentDirectory:IcuDetailDelaySeconds", "0")), log);
                return Wrap(log, () => svc.RunAsync(5));
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"url":"https://www.icu.ie/events/{{r.Id}}","place":{{J(r.Place)}},"lat":null,"lon":null,"categories":["Classical"],"nonTournament":{{(r.NonTournament ? "true" : "false")}}}""",
            """{"chessResultsId":null,"playerCount":12,"website":null}""",
            ShowDetailCalls: true, Extra: IcuExtraAsync),
        ["ChessSk"] = new("ChessSk", "SVK", "SK",
            (db, http) =>
            {
                var log = new TestLogger<ChessSkDirectorySweepService>();
                var svc = new ChessSkDirectorySweepService(db, http, new GeocodingService(db), log);
                return Wrap(log, () => svc.RunAsync());
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"endDate":{{D(r.Start)}},"city":{{J(r.Place)}},"address":null,"country":"SVK","chessResultsId":{{J(r.Cr)}},"website":null,"url":"https://chess.sk/turnaj/{{r.Id}}","systemText":null,"timeControl":"90 min + 30 s","system":"swiss","rounds":7,"type":"Standard","nonTournament":{{(r.NonTournament ? "true" : "false")}}}""",
            Extra: ChessSkExtraAsync),
        ["ChessArbiter"] = new("ChessArbiter", "POL", "PL",
            (db, http) =>
            {
                var log = new TestLogger<ChessArbiterDirectorySweepService>();
                var svc = new ChessArbiterDirectorySweepService(db, http, new GeocodingService(db), Config(), log);
                return Wrap(log, () => svc.RunAsync(5));
            },
            r => $$"""{"year":"2026","eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"place":{{J(r.Place)}},"region":"mazowieckie","speedText":"klasyczne","url":"https://www.chessarbiter.com/turnieje/2026/ti_{{r.Id}}/"}""",
            """{"startDate":null,"endDate":null,"place":"ul. Testowa 1, Testhausen","timeControl":"90 min + 30 s","rounds":9,"system":"swiss","playerCount":40,"organizer":null}""",
            ShowDetailCalls: true, Extra: ChessArbiterExtraAsync),
        ["Ffe"] = new("Ffe", "FRA", "FR",
            (db, http) =>
            {
                var log = new TestLogger<FfeDirectorySweepService>();
                var svc = new FfeDirectorySweepService(db, http, new GeocodingService(db), Config(), log);
                return Wrap(log, () => svc.RunAsync(detailLimit: 5));
            },
            r => $$"""{"eventId":"{{r.Id}}","name":"{{r.Name}}","startDate":{{D(r.Start)}},"city":{{J(r.Place)}},"department":"58","homologatedBy":"FFE","url":"https://www.echecs.asso.fr/FicheTournoi.aspx?Ref={{r.Id}}"}""",
            """{"startDate":null,"endDate":null,"city":null,"department":null,"address":"1 rue de la Paix","timeControl":"60' + [30'']","rounds":7,"pairingSystem":"Suisse"}""",
            ShowDetailCalls: true, Extra: FfeExtraAsync),
    };

    // ----- Sonderfaelle der Quellen mit eigener Detail-/Nummernlogik ---------------------------

    /// <summary>Ein chess-results-Eintrag im Bestand, dem die Quelle zugeordnet werden kann.</summary>
    private static async Task SeedChessResultsAsync(ScenarioSteps s, string id, string name, string federation,
        DateOnly start)
    {
        await using var db = s.NewDb();
        db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = id, ChessResultsId = id, Name = name, Federation = federation, StartDate = start, EndDate = start,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// ICU: die Detailseite traegt die chess-results-Nummer — exakter Treffer VOR dem
    /// Namensvergleich, und er haelt auch in den Naechten danach, in denen die Seite nicht mehr
    /// geholt wird.
    /// </summary>
    private static async Task IcuExtraAsync(ScenarioSteps s)
    {
        await SeedChessResultsAsync(s, "9100001", "Ganz anderer Name", "IRL", Soon.AddDays(20));
        s.Handler.DetailFor = q => q.Contains("id=4001")
            ? """{"chessResultsId":"9100001","playerCount":30,"website":null}"""
            : q.Contains("id=4002") ? null
            : """{"chessResultsId":null,"playerCount":12,"website":null}""";
        var exact = new RowSpec("4001", "Irish Exact Open", Soon.AddDays(3), "Galway");
        var missing = new RowSpec("4002", "Detailseite Fehlt Cup", Soon.AddDays(4), null);
        await s.Step("9 exakter Treffer per Nummer, Detailseite fehlt", [exact, missing]);
        // Die Nummer stand nur auf der Detailseite der ersten Nacht; der Treffer muss trotzdem
        // halten (ueber den Vermerk am chess-results-Eintrag) — vorher entstand hier ie4001.
        await s.Step("10 schon geholt: kein zweiter Abruf, bleibt zugeordnet, die fehlende wird wiederholt", [exact, missing]);
        s.Handler.DetailFor = null;
        await s.Step("11 Detailseite wieder da", [exact, missing]);
        await s.Step("12 kein Turnier (Unterricht)", [B with { NonTournament = true }]);
    }

    /// <summary>chess.sk: Nummer der Quelle — exakter Treffer, eigener Eintrag mit Nummer, leere Nummer, nachgereicht.</summary>
    private static async Task ChessSkExtraAsync(ScenarioSteps s)
    {
        await SeedChessResultsAsync(s, "9200001", "Ganz anderer Name", "SVK", Soon.AddDays(20));
        await s.Step("9 exakter Treffer per Nummer",
            [new RowSpec("5001", "Slovak Exact Open", Soon.AddDays(3), "Testhausen", Cr: "9200001")]);
        await s.Step("10 Nummer noch nicht im Bestand",
            [new RowSpec("5002", "Nummer Unbekannt Turnier", Soon.AddDays(5), null, Cr: "9200002")]);
        await s.Step("11 leere Nummer",
            [new RowSpec("5003", "Leere Nummer Turnier", Soon.AddDays(6), null, Cr: "")]);
        await s.Step("12 Nummer nachgereicht", [C with { Cr = "9200003" }]);
        await s.Step("13 Schulung", [B with { NonTournament = true }]);
    }

    /// <summary>chessarbiter: leere Detailseite (204) ist endgueltig, 404 wird wiederholt, Altbestand ueber die Adresse.</summary>
    private static async Task ChessArbiterExtraAsync(ScenarioSteps s)
    {
        await using (var db = s.NewDb())
        {
            // Altbestand: Adresse im Vermerk, aber noch keine Fassung — gilt als geholt.
            var old = new TournamentDirectoryEntry
            {
                PublicId = "pl2026-6003", Name = "Altbestand Mit Adresse", Federation = "POL",
                StartDate = Soon.AddDays(8), EndDate = Soon.AddDays(8), ChessArbiterDetailVersion = 0,
            };
            old.Sources.Add(new TournamentDirectorySource
            {
                Kind = DirectorySourceKind.PolishChessFederation, ExternalId = "2026/ti_6003",
                Url = "https://www.chessarbiter.com/turnieje/2026/ti_6003/",
            });
            // Fassung alt, keine Adresse: wird genau einmal nachgefragt.
            var stale = new TournamentDirectoryEntry
            {
                PublicId = "pl2026-6004", Name = "Altbestand Ohne Adresse", Federation = "POL",
                StartDate = Soon.AddDays(9), EndDate = Soon.AddDays(9), ChessArbiterDetailVersion = 0,
            };
            stale.Sources.Add(new TournamentDirectorySource
            {
                Kind = DirectorySourceKind.PolishChessFederation, ExternalId = "2026/ti_6004",
            });
            db.TournamentDirectoryEntries.AddRange(old, stale);
            await db.SaveChangesAsync();
        }

        async Task Versions()
        {
            await using var db = s.NewDb();
            foreach (var e in await db.TournamentDirectoryEntries.AsNoTracking()
                         .Where(e => e.PublicId.StartsWith("pl2026-6")).OrderBy(e => e.PublicId).ToListAsync())
                s.Note($"Fassung {e.PublicId}={e.ChessArbiterDetailVersion}");
        }

        s.Handler.DetailFor = q => q.Contains("id=6001") ? MutableHandler.NoContent
            : q.Contains("id=6002") ? null
            : """{"startDate":null,"endDate":null,"place":null,"timeControl":"3 min + 2 s","rounds":9,"system":"roundRobin","playerCount":10,"organizer":null}""";
        RowSpec[] rows =
        [
            new("6001", "Leere Seite Turniej", Soon.AddDays(3), "Testhausen"),
            new("6002", "Nicht Erreichbar Turniej", Soon.AddDays(4), null),
            new("6003", "Altbestand Mit Adresse", Soon.AddDays(8), null),
            new("6004", "Altbestand Ohne Adresse", Soon.AddDays(9), null),
        ];
        await s.Step("9 Detailseite leer, nicht erreichbar, Altbestand", rows);
        await Versions();
        await s.Step("10 nochmal: nur die nicht erreichbare wird wiederholt", rows);
        await Versions();
        s.Handler.DetailFor = null;
        await s.Step("11 Detailseite wieder erreichbar", rows);
        await Versions();
    }

    /// <summary>FFE: eine fehlende Turnierseite wird wiederholt, eine gelesene nicht.</summary>
    private static async Task FfeExtraAsync(ScenarioSteps s)
    {
        s.Handler.DetailFor = q => q.Contains("id=7001") ? null
            : """{"startDate":null,"endDate":null,"city":null,"department":null,"address":"2 place du Marche","timeControl":"1h30 + [30'']","rounds":5,"pairingSystem":"Berger"}""";
        RowSpec[] rows =
        [
            new("7001", "Tournoi Sans Fiche", Soon.AddDays(3), "Testhausen"),
            new("7002", "Tournoi Avec Fiche", Soon.AddDays(4), "Testhausen"),
        ];
        await s.Step("9 Turnierseite fehlt bei einer", rows);
        await s.Step("10 nochmal: nur die fehlende wird wiederholt", rows);
        s.Handler.DetailFor = null;
        await s.Step("11 Turnierseite wieder da", rows);
    }

    private static Microsoft.Extensions.Configuration.IConfiguration Config(params (string Key, string Value)[] values) =>
        new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();


    public static TheoryData<string> SourceNames() => [.. Sources.Keys];

    private static readonly RowSpec A = new("1001", "Danubius Kronstadt Memorial", Soon, "Nirgendwo");
    private static readonly RowSpec B = new("1002", "Cup Beta Rapid", Soon.AddDays(7), "Testhausen");
    private static readonly RowSpec C = new("1003", "Verschwindibus Pokal", Soon.AddDays(1), null);
    private static readonly RowSpec U = new("1009", "Kein Termin Turnier", null, "Nirgendwo");

    /// <summary>Neun weitere eigene Turniere, damit die 90-%-Bremse einen einzelnen Fehlenden durchlaesst.</summary>
    private static readonly RowSpec[] Series = [.. Enumerable.Range(1, 9)
        .Select(i => new RowSpec($"20{i:00}", $"Serienturnier Nummer{i}", Soon.AddDays(1), null))];

    [Theory]
    [MemberData(nameof(SourceNames))]
    public async Task Loop_BehavesAsRecorded(string source)
    {
        var actual = await RunScenarioAsync(Sources[source]);
        var expected = ExternalSweepGolden.Expected.GetValueOrDefault(source);
        if (expected != actual)
        {
            var dir = Environment.GetEnvironmentVariable("ROOKHUB_GOLDEN_OUT");
            if (dir is { Length: > 0 }) await File.WriteAllTextAsync(Path.Combine(dir, source + ".golden"), actual);
        }
        Assert.Equal(expected, actual);
    }

    internal static async Task<string> RunScenarioAsync(SourceSpec spec)
    {
        var dbName = Guid.NewGuid().ToString();
        var poison = new PoisonSwitch();
        AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new RejectAddedInterceptor(e => poison.On && e is TournamentDirectorySource)).Options);

        var handler = new MutableHandler { DetailBody = spec.DetailJson };
        var http = new StubClientFactory(handler);
        var sb = new StringBuilder();

        await using (var seed = NewDb())
        {
            seed.GeoPlaces.Add(new GeoPlace
            {
                Country = spec.GeoCountry, Name = "Testhausen", NameNormalized = "testhausen",
                NameTranscribed = "testhausen", Lat = 10.5, Lon = 20.25, Kind = GeoPlaceKind.City, Population = 50_000,
            });
            await seed.SaveChangesAsync();
        }

        async Task Step(string title, params RowSpec[] rows)
        {
            handler.Body = "[" + string.Join(",", rows.Select(spec.RowJson)) + "]";
            await using var db = NewDb();
            var (run, log) = spec.Create(db, http);
            string result;
            try { result = (await run()).ToString(); }
            catch (Exception ex) { result = "WIRFT " + ex.GetType().Name + ": " + ex.GetBaseException().Message; }
            sb.AppendLine("== " + title);
            sb.AppendLine("Ergebnis: " + result);
            if (spec.ShowDetailCalls) sb.AppendLine("Detailabrufe: " + string.Join(" ", handler.DetailCalls));
            handler.DetailCalls.Clear();
            foreach (var line in log()) sb.AppendLine("Log " + line);
            await using var check = NewDb();
            await DumpAsync(check, sb);
        }

        await Step("1 Erstlauf", [A, B, C, U, .. Series]);

        await using (var seed = NewDb())
        {
            seed.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
            {
                PublicId = "9000001", ChessResultsId = "9000001", Name = A.Name, Federation = spec.Federation,
                StartDate = A.Start, EndDate = A.Start,
            });
            await seed.SaveChangesAsync();
        }
        await Step("2 chess-results fuehrt A", [A, B, C, U, .. Series]);
        await Step("3 C fehlt", [B, U, .. Series]);
        await Step("4 C fehlt wieder (Karenz)", [B, U, .. Series]);
        await Step("5 nur B (Bremse)", B);
        await Step("6 leer", []);

        poison.On = true;
        await Step("7 Speichern scheitert", A, B with { Id = "1004", Name = "Neu Gamma Blitz" });
        // Mehr als SaveEvery (25) Zeilen: der Zwischenstand-Speicher MITTEN im Lauf scheitert.
        await Step("8 Speichern scheitert mitten im Lauf", [.. Enumerable.Range(1, 30)
            .Select(i => new RowSpec($"30{i:00}", $"Massenturnier Nummer{i}", Soon.AddDays(2), null))]);

        if (spec.Extra is not null)
        {
            poison.On = false;
            await spec.Extra(new ScenarioSteps((t, r) => Step(t, r), NewDb, handler, l => sb.AppendLine(l)));
        }
        return sb.ToString().Replace("\r\n", "\n");
    }

    private static async Task DumpAsync(AppDbContext db, StringBuilder sb)
    {
        static string Rel(DateOnly? d) => d is { } x ? $"S{x.DayNumber - Soon.DayNumber:+0;-0;+0}" : "-";
        static string Set(DateTime? t) => t is null ? "-" : "ja";

        foreach (var e in await db.TournamentDirectoryEntries.AsNoTracking().Include(e => e.Sources)
                     .OrderBy(e => e.PublicId).ToListAsync())
        {
            sb.AppendLine(
                $"Eintrag {e.PublicId} cr={e.ChessResultsId ?? "-"} name={e.Name} fed={e.Federation} state={e.State ?? "-"} " +
                $"start={Rel(e.StartDate)} ende={Rel(e.EndDate)} we={e.StartsOnWeekend} ort={e.LocationText ?? "-"} " +
                $"geo={e.GeoSource} lat={e.Lat?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} " +
                $"platz={e.GeoPlaceName ?? "-"} gesehen={Set(e.LastSeenAt)} weg={Set(e.RemovedAt)} " +
                $"fehl={e.MissedSweeps} fehlAm={Set(e.LastMissAt)} alter={e.AgeGroups} gruppe={(e.GroupKey is null ? "-" : "ja")}");
            foreach (var s in e.Sources.OrderBy(s => s.Kind).ThenBy(s => s.ExternalId))
                sb.AppendLine($"  Vermerk {s.Kind} {s.ExternalId} url={s.Url ?? "-"} erst={Set(s.FirstSeenAt)} zuletzt={Set(s.LastSeenAt)}");
        }
    }

    private sealed class PoisonSwitch { public bool On; }

    public sealed class MutableHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "[]";
        public string? DetailBody { get; init; }

        /// <summary>
        /// Detailantwort je Abfrage (<c>?id=…</c>), wenn gesetzt — statt <see cref="DetailBody"/>.
        /// <c>null</c> = 404, <see cref="NoContent"/> = 204.
        /// </summary>
        public Func<string, string?>? DetailFor { get; set; }

        public const string NoContent = "<204>";

        /// <summary>Die Abfragen der Detailabrufe seit dem letzten Lauf.</summary>
        public List<string> DetailCalls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var detail = request.RequestUri?.AbsolutePath.EndsWith("/detail", StringComparison.Ordinal) == true;
            var query = request.RequestUri?.Query ?? "";
            if (detail) DetailCalls.Add(query.TrimStart('?'));
            var body = detail ? (DetailFor is { } f ? f(query) : DetailBody) : Body;
            if (body == NoContent) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
        }
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler") };
    }
}
