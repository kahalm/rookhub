using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
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
    /// <summary>Eine Zeile des Szenarios; <see cref="Start"/> null = unlesbarer Termin.</summary>
    public sealed record RowSpec(string Id, string Name, DateOnly? Start, string? Place);

    /// <summary>Eine Quelle im Test: wie man sie baut und wie ihre Crawler-Zeile aussieht.</summary>
    public sealed record SourceSpec(
        string Name, string Federation, string GeoCountry,
        Func<AppDbContext, IHttpClientFactory, (Func<Task<ExternalSweepResult>> Run, Func<List<string>> Log)> Create,
        Func<RowSpec, string> RowJson);

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
    };

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

        var handler = new MutableHandler();
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

    private sealed class MutableHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "[]";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler") };
    }
}
