using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// External-Engine-Anbindung (Lichess-Client-Modus): Token-Verwaltung (verschlüsselt + maskiert),
/// Engine-Liste OHNE clientSecret, Analyse-Proxy (Work-Validierung, Klemmen auf Engine-Maxima,
/// ndjson-Durchreichung). Lichess/Broker sind per StubHandler nachgestellt.
/// </summary>
public class EngineControllerTests : IDisposable
{
    private const string EnginesJson = """
        [{"id":"eei_abc","name":"Stockfish 17 Heim-PC","clientSecret":"ees_secret1",
          "userId":"kahalm","maxThreads":8,"maxHash":512,"variants":["chess"],"providerData":null}]
        """;

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly StubHandler _handler = new();
    private readonly EngineController _controller;

    public EngineControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!"
            })
            .Build();
        _encryption = new EncryptionService(config);

        var lichess = new LichessEngineService(
            new HttpClient(_handler),
            new MemoryCache(new MemoryCacheOptions()),
            config,
            NullLogger<LichessEngineService>.Instance);
        _controller = new EngineController(_db, _encryption, lichess, NullLogger<EngineController>.Instance);
        SetUser(42);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test"))
        };
        httpContext.Response.Body = new MemoryStream();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private async Task<AppUser> CreateUserAsync(int id = 42)
    {
        var user = new AppUser { Id = id, Username = $"user{id}", PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>Antwortet je nach URL: GET …/api/external-engine = Liste, POST …/analyse = Stream.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string ListJson = EnginesJson;
        public HttpStatusCode ListStatus = HttpStatusCode.OK;
        public string AnalyseBody = "";
        public string? CapturedAnalyseRequestBody;
        public string? CapturedAuthHeader;
        public int ListCalls;

        /// <summary>Hält Analyse-Anfragen fest (simuliert einen laufenden Stream) — für den Deckel-Test.</summary>
        public bool BlockAnalyse;
        public int AnalyseCalls;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseAnalyse() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                ListCalls++;
                CapturedAuthHeader = request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(ListStatus)
                {
                    Content = new StringContent(ListJson, Encoding.UTF8, "application/json")
                };
            }
            CapturedAnalyseRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Interlocked.Increment(ref AnalyseCalls);
            if (BlockAnalyse) await _release.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(AnalyseBody, Encoding.UTF8, "application/x-ndjson")
            };
        }
    }

    // ---- Credentials ----

    [Fact]
    public async Task GetCredentials_None_ReturnsHasFalse()
    {
        await CreateUserAsync();
        var result = Assert.IsType<OkObjectResult>(await _controller.GetCredentials());
        var dto = Assert.IsType<LichessEngineCredentialResponse>(result.Value);
        Assert.False(dto.HasCredentials);
        Assert.Null(dto.MaskedToken);
    }

    [Fact]
    public async Task SaveCredentials_StoresEncrypted_AndMasks()
    {
        await CreateUserAsync();
        var result = Assert.IsType<OkObjectResult>(
            await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_superSecretToken" }));
        var dto = Assert.IsType<LichessEngineCredentialResponse>(result.Value);
        Assert.True(dto.HasCredentials);
        Assert.EndsWith("oken", dto.MaskedToken);
        Assert.DoesNotContain("lip_superSecret", dto.MaskedToken);

        var row = await _db.LichessEngineCredentials.SingleAsync();
        Assert.Equal(42, row.UserId);
        Assert.DoesNotContain("lip_superSecretToken", row.EncryptedToken);
        Assert.Equal("lip_superSecretToken", _encryption.Decrypt(row.EncryptedToken));
    }

    [Fact]
    public async Task SaveCredentials_Empty_Returns400()
    {
        await CreateUserAsync();
        Assert.IsType<BadRequestObjectResult>(
            await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "   " }));
    }

    [Fact]
    public async Task SaveCredentials_Twice_OverwritesSingleRow()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_first" });
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_second" });
        var row = await _db.LichessEngineCredentials.SingleAsync();
        Assert.Equal("lip_second", _encryption.Decrypt(row.EncryptedToken));
    }

    [Fact]
    public async Task DeleteCredentials_RemovesRow()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_x" });
        Assert.IsType<NoContentResult>(await _controller.DeleteCredentials());
        Assert.Empty(_db.LichessEngineCredentials);
    }

    // ---- Engine-Liste ----

    [Fact]
    public async Task ListExternalEngines_NoToken_ReturnsEmpty_WithoutLichessCall()
    {
        await CreateUserAsync();
        var result = Assert.IsType<OkObjectResult>(await _controller.ListExternalEngines(CancellationToken.None));
        var dto = Assert.IsType<ExternalEnginesResponse>(result.Value);
        Assert.False(dto.HasCredentials);
        Assert.Empty(dto.Engines);
        Assert.Equal(0, _handler.ListCalls);
    }

    [Fact]
    public async Task ListExternalEngines_ReturnsEngines_WithoutClientSecret()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });

        var result = Assert.IsType<OkObjectResult>(await _controller.ListExternalEngines(CancellationToken.None));
        var dto = Assert.IsType<ExternalEnginesResponse>(result.Value);
        Assert.True(dto.HasCredentials);
        Assert.False(dto.TokenInvalid);
        var engine = Assert.Single(dto.Engines);
        Assert.Equal("eei_abc", engine.Id);
        Assert.Equal(8, engine.MaxThreads);
        // Token wird als Bearer an Lichess geschickt; das clientSecret verlässt den Server nicht.
        Assert.Equal("Bearer lip_tok", _handler.CapturedAuthHeader);
        Assert.DoesNotContain("ees_secret1", JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task ListExternalEngines_Unauthorized_SetsTokenInvalid()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_dead" });
        _handler.ListStatus = HttpStatusCode.Unauthorized;

        var result = Assert.IsType<OkObjectResult>(await _controller.ListExternalEngines(CancellationToken.None));
        var dto = Assert.IsType<ExternalEnginesResponse>(result.Value);
        Assert.True(dto.TokenInvalid);
        Assert.Empty(dto.Engines);
    }

    // ---- Analyse-Proxy ----

    private static EngineAnalyseRequest ValidRequest() => new()
    {
        SessionId = "sess-1",
        InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        Moves = ["e2e4"],
        MultiPv = 3,
        Depth = 22,
        Threads = 64   // > maxThreads 8 → muss geklemmt werden
    };

    [Fact]
    public async Task Analyse_StreamsBrokerBody_AndClampsThreads()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        _handler.AnalyseBody = "{\"time\":5,\"depth\":10,\"nodes\":1000,\"pvs\":[{\"depth\":10,\"cp\":35,\"moves\":[\"e7e5\"]}]}\n";

        var result = await _controller.Analyse("eei_abc", ValidRequest(), CancellationToken.None);
        Assert.IsType<EmptyResult>(result);

        var response = _controller.ControllerContext.HttpContext.Response;
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.ContentType);
        Assert.Equal("no", response.Headers["X-Accel-Buffering"].ToString());
        response.Body.Position = 0;
        var body = await new StreamReader(response.Body).ReadToEndAsync();
        Assert.Contains("\"cp\":35", body);

        // Broker-Request: clientSecret aus der Liste + geklemmte Threads + Work-Felder.
        using var sent = JsonDocument.Parse(_handler.CapturedAnalyseRequestBody!);
        Assert.Equal("ees_secret1", sent.RootElement.GetProperty("clientSecret").GetString());
        var work = sent.RootElement.GetProperty("work");
        Assert.Equal(8, work.GetProperty("threads").GetInt32());
        Assert.Equal(512, work.GetProperty("hash").GetInt32());
        Assert.Equal(22, work.GetProperty("depth").GetInt32());
        Assert.Equal("chess", work.GetProperty("variant").GetString());
        Assert.Equal("e2e4", work.GetProperty("moves")[0].GetString());
    }

    [Fact]
    public async Task Analyse_UnknownEngine_Returns404()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        var result = await _controller.Analyse("eei_unknown", ValidRequest(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Analyse_NoToken_Returns400()
    {
        await CreateUserAsync();
        var result = await _controller.Analyse("eei_abc", ValidRequest(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Analyse_TwoLimits_Returns400()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        var req = ValidRequest();
        req.Movetime = 5000;   // depth UND movetime → oneOf verletzt
        var result = await _controller.Analyse("eei_abc", req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Analyse_NoLimit_Returns400()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        var req = ValidRequest();
        req.Depth = null;
        var result = await _controller.Analyse("eei_abc", req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Analyse_TooManyMoves_Returns400()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        var req = ValidRequest();
        req.Moves = Enumerable.Repeat("e2e4", 601).ToList();
        Assert.IsType<BadRequestObjectResult>(await _controller.Analyse("eei_abc", req, CancellationToken.None));
    }

    /// <summary>Ein Stream hält eine Verbindung, solange die Engine rechnet — über dem Deckel
    /// muss der Server abweisen, statt sich beliebig viele offene Ströme aufhalsen zu lassen.</summary>
    [Fact]
    public async Task Analyse_AboveConcurrencyLimit_Returns429()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        _handler.BlockAnalyse = true;

        // Vier Ströme dürfen offen sein; sie hängen im Handler fest.
        var running = Enumerable.Range(0, 4)
            .Select(_ => _controller.Analyse("eei_abc", ValidRequest(), CancellationToken.None))
            .ToList();
        while (_handler.AnalyseCalls < 4) await Task.Delay(10);

        var overflow = await _controller.Analyse("eei_abc", ValidRequest(), CancellationToken.None);
        var status = Assert.IsType<ObjectResult>(overflow);
        Assert.Equal(429, status.StatusCode);

        _handler.ReleaseAnalyse();
        await Task.WhenAll(running);

        // Nach dem Freiwerden geht es wieder — der Zähler wird sauber zurückgegeben.
        _handler.BlockAnalyse = false;
        Assert.IsType<EmptyResult>(await _controller.Analyse("eei_abc", ValidRequest(), CancellationToken.None));
    }

    // ---- Robustheit der Engine-Liste ----

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"error\":\"nope\"}")]                                  // Objekt statt Array
    [InlineData("[{\"id\":123}]")]                                        // id falscher Typ
    [InlineData("[{\"name\":\"ohne id\",\"maxThreads\":2}]")]             // id fehlt
    public async Task ListExternalEngines_MalformedUpstream_ReturnsEmpty_NotServerError(string body)
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        _handler.ListJson = body;

        var result = Assert.IsType<OkObjectResult>(await _controller.ListExternalEngines(CancellationToken.None));
        var dto = Assert.IsType<ExternalEnginesResponse>(result.Value);
        Assert.Empty(dto.Engines);
        Assert.True(dto.HasCredentials);
    }

    [Fact]
    public async Task ListExternalEngines_MissingOptionalFields_FallsBackToSafeDefaults()
    {
        await CreateUserAsync();
        await _controller.SaveCredentials(new SaveLichessTokenRequest { Token = "lip_tok" });
        _handler.ListJson = "[{\"id\":\"eei_min\",\"clientSecret\":\"ees_x\"}]";

        var result = Assert.IsType<OkObjectResult>(await _controller.ListExternalEngines(CancellationToken.None));
        var dto = Assert.IsType<ExternalEnginesResponse>(result.Value);
        var engine = Assert.Single(dto.Engines);
        Assert.Equal("eei_min", engine.Id);
        Assert.Equal("eei_min", engine.Name);     // Name fehlt → Id als Anzeige
        Assert.True(engine.MaxThreads >= 1);
        Assert.True(engine.MaxHash >= 1);
    }
}
