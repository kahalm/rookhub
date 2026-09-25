using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Der eigene Engine-Broker ÜBER HTTP, gegen die echte Anwendung auf echtem KESTREL (nicht TestServer —
/// die Fallen, um die es geht, sind Kestrel-Fallen) und echtes MariaDB. Die Gegenstelle ist ein C#-Nachbau
/// des offiziellen <c>example-provider.py</c> (<see cref="FakeProvider"/>): Registrierung mit API-Token,
/// Long-Poll mit Bearer, CHUNKED-Upload ohne Authentifizierung, Lebenszeichen, <c>bestmove</c> am Ende.
///
/// <para>Geprüft wird, was nur über die ganze Strecke sichtbar ist: die Zeilen kommen beim Browser an,
/// WÄHREND der Upload noch läuft (Zeitstempel); ein Abbruch des Browsers beendet den Upload des Providers
/// sofort; ein minutenlang schweigender Upload wird von Kestrels Mindest-Datenrate NICHT gekappt; der
/// Long-Poll läuft am Rate-Limiter vorbei; und der Auftrags-Worker rechnet einen Hintergrund-Auftrag über
/// eine <c>rhe_</c>-Engine zu Ende.</para>
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class EngineBrokerTests(EngineBrokerFixture fixture) : IAsyncLifetime, IClassFixture<EngineBrokerFixture>
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly List<FakeProvider> _providers = [];

    public Task InitializeAsync() => fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var p in _providers) await p.DisposeAsync();
    }

    /// <summary>OHNE die Zusatz-Handler von <c>CreateClient()</c>: dessen Umleitungs-Handler puffert den
    /// Anfrage-Rumpf (für eine mögliche Wiederholung) — der chunked Upload käme dann erst am Ende am Server an.</summary>
    private HttpClient Client(string? bearer = null)
    {
        // Kein Nachlesen einer abgebrochenen Antwort: SocketsHttpHandler liest sonst bis zu 2 s weiter, um die
        // Verbindung wiederzuverwenden — ein Browser schließt sofort.
        var client = new HttpClient(new SocketsHttpHandler { MaxResponseDrainSize = 0 })
        {
            BaseAddress = fixture.Factory.CreateDefaultClient().BaseAddress,
        };
        client.Timeout = TimeSpan.FromSeconds(60);
        if (bearer is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    /// <summary>Konto + Browser-Login (JWT) + API-Token mit Scope <c>engine</c> — ohne die gedrosselten
    /// Anmelde-Endpunkte (der „auth"-Limiter erlaubt zehn je Minute).</summary>
    private async Task<(int UserId, string Jwt, string EngineToken)> UserAsync(string name = "kahalm")
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Username = name, PasswordHash = BCrypt.Net.BCrypt.HashPassword("egal-egal-2026") };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var jwt = (await scope.ServiceProvider.GetRequiredService<AuthService>().IssueTokenAsync(user)).Token;
        var token = (await scope.ServiceProvider.GetRequiredService<ApiTokenService>()
            .CreateAsync(user.Id, "Engine-Provider", ApiTokenService.EngineScope, null)).RawToken;
        return (user.Id, jwt, token);
    }

    private async Task<FakeProvider> ProviderAsync(string engineToken, FakeProvider.Script script, string name = "Heim-PC")
    {
        var provider = new FakeProvider(Client(), engineToken, name, script);
        _providers.Add(provider);
        await provider.RegisterAsync();
        provider.Start();
        return provider;
    }

    private static object AnalyseBody(int depth = 20, int multiPv = 1) => new
    {
        sessionId = "browser-1", initialFen = Start, moves = new[] { "e2e4" }, multiPv, depth,
    };

    [MySqlFact]
    public async Task Registration_TokenTest_AndTheScopeFence_OverRealHttp()
    {
        var (_, jwt, engineToken) = await UserAsync();
        string extensionToken;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var uid = await db.AppUsers.Select(u => u.Id).SingleAsync();
            extensionToken = (await scope.ServiceProvider.GetRequiredService<ApiTokenService>()
                .CreateAsync(uid, "Ext", ApiTokenService.DefaultScope, null)).RawToken;
        }

        // Vorabprüfung des Providers (preflight.py): anonym, Token im Rumpf, Lichess-Form.
        using var anon = Client();
        var test = await anon.PostAsync("/api/token/test", new StringContent(engineToken, Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        using (var doc = JsonDocument.Parse(await test.Content.ReadAsStringAsync()))
            Assert.Equal("engine:read,engine:write", doc.RootElement.GetProperty(engineToken).GetProperty("scopes").GetString());

        // Registrieren wie der Provider (Bearer = engine-Token) …
        using var provider = Client(engineToken);
        var created = await provider.PostAsJsonAsync("/api/external-engine", new
        {
            name = "Server Live", maxThreads = 8, maxHash = 1024, variants = new[] { "chess" },
            providerSecret = "provider-secret-0123456789",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var list = await provider.GetFromJsonAsync<JsonElement>("/api/external-engine");
        Assert.Equal("Server Live", list[0].GetProperty("name").GetString());
        Assert.True(list[0].TryGetProperty("clientSecret", out _));

        // … ein Extension-Token kommt nicht einmal bis zum Controller (Zaun: 403) …
        using var extension = Client(extensionToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await extension.GetAsync("/api/external-engine")).StatusCode);
        // … und der Engine-Token nicht an die übrige API.
        Assert.Equal(HttpStatusCode.Forbidden, (await provider.GetAsync("/api/profile")).StatusCode);

        // Der Browser (JWT) liest ohne Secret und darf nicht registrieren.
        using var browser = Client(jwt);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/external-engine", new
        {
            name = "zweite", maxThreads = 1, maxHash = 16, variants = new[] { "chess" }, providerSecret = "x-provider-secret-0123",
        })).StatusCode);
        var fromBrowser = await browser.GetFromJsonAsync<JsonElement>("/api/external-engine");
        Assert.False(fromBrowser[0].TryGetProperty("clientSecret", out _));
    }

    [MySqlFact]
    public async Task Analyse_LinesArriveWhileTheUploadRuns_KeepaliveComesThrough_BestmoveEnds()
    {
        var (_, jwt, engineToken) = await UserAsync();
        var keepaliveWritten = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = await ProviderAsync(engineToken, async (job, write, ct) =>
        {
            await write("info depth 1 seldepth 1 multipv 1 score cp -20 nodes 20 nps 20000 time 1 pv e7e5");
            await Task.Delay(1500, ct);
            await write("{\"keepalive\":true}");
            keepaliveWritten.TrySetResult(DateTime.UtcNow);
            await Task.Delay(300, ct);
            await write("info depth 2 seldepth 2 multipv 1 score cp -25 nodes 80 nps 40000 time 2 pv e7e5 g1f3");
            await write("bestmove e7e5 ponder g1f3");
        });

        using var browser = Client(jwt);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/engine/external/{provider.EngineId}/analyse")
        {
            Content = JsonContent.Create(AnalyseBody()),
        };
        using var res = await browser.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/x-ndjson", res.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync());

        var lines = new List<(string Line, DateTime At)>();
        while (await reader.ReadLineAsync() is { } line)
            if (line.Length > 0) lines.Add((line, DateTime.UtcNow));

        Assert.Equal(4, lines.Count);
        // Die erste Zeile war beim Browser, BEVOR der Provider sein Lebenszeichen überhaupt geschrieben hatte:
        // gestreamt, nicht gesammelt.
        Assert.True(lines[0].At < await keepaliveWritten.Task,
            $"erste Zeile um {lines[0].At:HH:mm:ss.fff}, Lebenszeichen geschrieben um {keepaliveWritten.Task.Result:HH:mm:ss.fff}");
        // Schwarz am Zug (1.e4): -20 aus Sicht der Engine = +20 aus Weiß-Sicht.
        Assert.Equal("{\"time\":1,\"depth\":1,\"nodes\":20,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":20,\"depth\":1}]}", lines[0].Line);
        Assert.Equal("{\"keepalive\":true}", lines[1].Line);
        Assert.Contains("\"bestmove\":\"e7e5\",\"ponder\":\"g1f3\"", lines[3].Line);

        var upload = await provider.NextUploadAsync();
        Assert.Equal(HttpStatusCode.OK, upload.Status);
        Assert.True(upload.ScriptFinished);
    }

    [MySqlFact]
    public async Task BrowserLeaves_TheProvidersUploadEndsImmediately()
    {
        var (_, jwt, engineToken) = await UserAsync();
        var provider = await ProviderAsync(engineToken, async (job, write, ct) =>
        {
            await write("info depth 1 multipv 1 score cp 10 nodes 10 time 1 pv e7e5");
            // Eine lange Suche: nur noch Lebenszeichen, 20 s lang.
            for (var i = 0; i < 100 && !ct.IsCancellationRequested; i++)
            {
                await Task.Delay(200, ct);
                await write("{\"keepalive\":true}");
            }
            await write("bestmove e7e5");
        });

        using var browser = Client(jwt);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/engine/external/{provider.EngineId}/analyse")
        {
            Content = JsonContent.Create(AnalyseBody()),
        };
        var res = await browser.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        var reader = new StreamReader(await res.Content.ReadAsStreamAsync());
        var first = await reader.ReadLineAsync();
        // Schwarz am Zug: cp 10 aus Sicht der Engine = -10 aus Weiß-Sicht.
        Assert.True(first?.Contains("\"cp\":-10") == true, $"erste Zeile: {first ?? "(Ende)"}, Status {(int)res.StatusCode}");

        var left = Stopwatch.StartNew();
        res.Dispose();                                   // Stellungswechsel im Browser
        req.Dispose();

        var upload = await provider.NextUploadAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, upload.Status);
        Assert.False(upload.ScriptFinished);             // die Suche lief noch …
        Assert.True(left.Elapsed < TimeSpan.FromSeconds(5), $"Upload endete erst nach {left.Elapsed}");   // … und endete trotzdem sofort
    }

    [MySqlFact]
    public async Task SilentUpload_IsNotCutByKestrelsMinimumDataRate()
    {
        // Kestrel kappt einen Rumpf, der nach 5 s Gnade unter 240 Byte/s liegt. Ein Upload, der bei tiefer
        // MultiPV-Suche nur alle 15 s ein Lebenszeichen schickt, liegt weit darunter — ohne
        // MinDataRate = null endete er hier mit einem Verbindungsabbruch statt mit bestmove.
        var (_, jwt, engineToken) = await UserAsync();
        var provider = await ProviderAsync(engineToken, async (job, write, ct) =>
        {
            await write("info depth 1 multipv 1 score cp 10 nodes 10 time 1 pv e7e5");
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
            await write("info depth 2 multipv 1 score cp 12 nodes 20 time 8000 pv e7e5");
            await write("bestmove e7e5");
        });

        using var browser = Client(jwt);
        using var res = await browser.PostAsJsonAsync($"/api/engine/external/{provider.EngineId}/analyse", AnalyseBody());
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"bestmove\":\"e7e5\"", body);
        var upload = await provider.NextUploadAsync();
        Assert.Equal(HttpStatusCode.OK, upload.Status);
        Assert.True(upload.ScriptFinished);
    }

    [MySqlFact]
    public async Task ProviderPolls_AreNotRateLimited()
    {
        // Der globale Limiter lässt 100 Anfragen je Minute und IP durch. 13 Provider pollen 78-mal je Minute,
        // dazu kommen Uploads — hier 130 Abrufe in wenigen Sekunden, keiner darf 429 sein.
        using var anon = Client();
        var polls = Enumerable.Range(0, 130).Select(_ =>
            anon.PostAsJsonAsync("/api/external-engine/work", new { providerSecret = "never-registered-secret-01" }));
        var results = await Task.WhenAll(polls);
        Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
    }

    [MySqlFact]
    public async Task EngineList_ShowsTheLocalEngineOnline_WhileItsProviderPolls()
    {
        var (_, jwt, engineToken) = await UserAsync();
        var provider = await ProviderAsync(engineToken, (job, write, ct) => write("bestmove (none)"));
        await provider.WaitForPollsAsync(1);

        using var browser = Client(jwt);
        var list = await browser.GetFromJsonAsync<JsonElement>("/api/engine/external");
        var engine = list.GetProperty("engines")[0];
        Assert.Equal(provider.EngineId, engine.GetProperty("id").GetString());
        Assert.Equal("rookhub", engine.GetProperty("source").GetString());
        Assert.True(engine.GetProperty("online").GetBoolean());
        Assert.False(list.GetProperty("hasCredentials").GetBoolean());
    }

    [MySqlFact]
    public async Task Worker_ComputesABackgroundJob_OverAnRheEngine_ToTheEnd()
    {
        var (userId, jwt, engineToken) = await UserAsync();
        var provider = await ProviderAsync(engineToken, async (job, write, ct) =>
        {
            var work = job.GetProperty("work");
            Assert.Equal($"rh-bg-{userId}", work.GetProperty("sessionId").GetString());
            var depth = work.GetProperty("depth").GetInt32();
            for (var d = 1; d <= depth; d++)
            {
                await write($"info depth {d} multipv 1 score cp {-10 - d} nodes {d * 100} time {d * 10} pv e7e5 g1f3");
                await write($"info depth {d} multipv 2 score cp {-30 - d} nodes {d * 100 + 50} time {d * 10} pv c7c5");
            }
            await write("bestmove e7e5 ponder g1f3");
        });

        using var browser = Client(jwt);
        Assert.Equal(HttpStatusCode.OK, (await browser.PutAsJsonAsync("/api/engine/background",
            new { engineIds = new[] { provider.EngineId } })).StatusCode);
        var created = await browser.PostAsJsonAsync("/api/analysis-jobs", new
        {
            fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", targetDepth = 12, multiPv = 2,
        });
        Assert.True(created.IsSuccessStatusCode, $"Auftrag anlegen: {(int)created.StatusCode}");
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var worker = fixture.Factory.Services.GetRequiredService<AnalysisJobWorker>();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            JsonElement job;
            do
            {
                await Task.Delay(250);
                var all = await browser.GetFromJsonAsync<JsonElement>("/api/analysis-jobs");
                job = all.EnumerateArray().Single(j => j.GetProperty("id").GetInt32() == jobId);
            } while (job.GetProperty("status").GetString() is not ("done" or "failed" or "Done" or "Failed") && DateTime.UtcNow < deadline);

            Assert.Equal("done", job.GetProperty("status").GetString()!.ToLowerInvariant());
            Assert.Equal(12, job.GetProperty("reachedDepth").GetInt32());
            var result = job.GetProperty("resultJson").GetString()!;
            Assert.Contains("\"bestmove\":\"e7e5\"", result);
            // Beide Linien in Weiß-Sicht (Schwarz am Zug).
            Assert.Contains("\"cp\":22", result);
            Assert.Contains("\"cp\":42", result);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
