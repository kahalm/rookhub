using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Prep;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Spielervorbereitung, Phase 2, durch die ECHTE Pipeline gegen MariaDB: ohne <c>prep.view</c> 403 (auch mit
/// <c>league.view</c>), mit 200; das Konto eines Minderjährigen kommt über <c>/api/prep/*</c> nicht heraus, auch nicht für
/// einen Admin; Suche (LIKE mit Escape, Präfix) und Karte (jüngste Partien zuerst, Grenze, Partien ohne Datum hinten)
/// so, wie MariaDB die Abfragen ausführt. FIDE-IDs im 99xxxx-Bereich.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class PrepReadApiTests(PrepReadFixture fixture) : IAsyncLifetime, IClassFixture<PrepReadFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string Game(string white, string black, string moves, string date, string? whiteFide = null, string? blackFide = null) =>
        $"[Event \"Open Schwaz\"]\n[Site \"Schwaz\"]\n[Date \"{date}\"]\n[Round \"1\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n"
        + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n") + (blackFide is null ? "" : $"[BlackFideId \"{blackFide}\"]\n")
        + "\n" + moves + " 1-0\n\n";

    private async Task ImportAsync(string pgn, byte source = PrepSources.Mega, int chunk = 0)
    {
        await using var db = fixture.Schema.NewContext();
        await new PrepImportService(db).ImportChunkAsync(source, chunk, chunk * 5000L, pgn, default);
    }

    /// <summary>Konto mit den genannten Rechten (über eine eigene Rolle) → JWT, ohne die gedrosselten Anmelde-Endpunkte.</summary>
    private async Task<string> UserAsync(string name, bool admin = false, params string[] permissions)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Username = name, PasswordHash = BCrypt.Net.BCrypt.HashPassword("egal-egal-2026"), IsAdmin = admin };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        if (permissions.Length > 0)
        {
            var role = new Role { Key = "r-" + name, Name = "Rolle " + name };
            foreach (var p in permissions) role.Permissions.Add(new RolePermission { Permission = p });
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            await db.SaveChangesAsync();
        }
        return (await scope.ServiceProvider.GetRequiredService<AuthService>().IssueTokenAsync(user)).Token;
    }

    private HttpClient Client(string? jwt)
    {
        var c = fixture.Factory.CreateClient();
        if (jwt is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    private async Task<int> PlayerIdAsync(string fide)
    {
        await using var db = fixture.Schema.NewContext();
        return db.PrepPlayers.Single(p => p.FideId == fide).Id;
    }

    [MySqlFact]
    public async Task Read_WithoutPrepView_403_WithPrepView_200()
    {
        await ImportAsync(Game("Huber, Franz", "Mair, Josef", "1. e4 c5 2. Nf3 d6", "2024.05.17", "990001"));
        var id = await PlayerIdAsync("990001");
        var urls = new[]
        {
            "/api/prep/players?q=hub", $"/api/prep/player/{id}", $"/api/prep/player/{id}/profile", $"/api/prep/player/{id}/tree",
            $"/api/prep/player/{id}/recent", $"/api/prep/player/{id}/pgn",
        };

        using var anonymous = Client(null);
        using var plain = Client(await UserAsync("ohne"));
        using var leagueOnly = Client(await UserAsync("liga", false, Permissions.LeagueView, Permissions.LeagueManage));
        using var manageOnly = Client(await UserAsync("verwalter", false, Permissions.PrepManage));
        using var viewer = Client(await UserAsync("leser", false, Permissions.PrepView));
        foreach (var url in urls)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await leagueOnly.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await manageOnly.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(url)).StatusCode);
        }
        // Einspielen bleibt hinter prep.manage.
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/prep/admin/imports?source=Mega")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/prep/player/987654321")).StatusCode);
    }

    [MySqlFact]
    public async Task Card_MinorsAccount_NotInAnyResponse_EvenForAdmin()
    {
        await ImportAsync(Game("Kind, Klara", "Mair, Josef", "1. e4 c5 2. Nf3 d6", "2025.05.17", "990050"));
        await using (var db = fixture.Schema.NewContext())
        {
            var acc = new LeagueOnlineAccount
            {
                FideId = "990050", Site = "lichess", UserName = "klarakind2014", Url = "https://lichess.org/@/klarakind2014",
                Confidence = "sicher", GameCount = 1, Evidence = "Schulschach-Kommentar",
            };
            db.LeagueOnlineAccounts.Add(acc);
            await db.SaveChangesAsync();
            db.LeagueOnlineGames.Add(new LeagueOnlineGame
            {
                AccountId = acc.Id, FideId = "990050", ExternalId = "x1", PlayedAt = DateTime.UtcNow.AddDays(-3), Speed = "blitz", White = true,
                Result = "1-0", Opponent = "gegner", Line = "d4 d5", Moves = "d4 d5", Plies = 2,
            });
            db.LeagueAccountScans.Add(new LeagueAccountScan { FideId = "990050", BirthYear = DateTime.UtcNow.Year - 12, ScannedAt = DateTime.UtcNow, Version = 1 });
            await db.SaveChangesAsync();
        }
        var id = await PlayerIdAsync("990050");
        using var admin = Client(await UserAsync("admin", admin: true));
        using var viewer = Client(await UserAsync("leser2", false, Permissions.PrepView));
        foreach (var client in new[] { admin, viewer })
        {
            var all = new StringBuilder();
            foreach (var url in new[]
                     {
                         $"/api/prep/player/{id}", $"/api/prep/player/{id}/profile?source=both&unsure=true",
                         $"/api/prep/player/{id}/tree?color=w&source=both&unsure=true", $"/api/prep/player/{id}/recent",
                         $"/api/prep/player/{id}/pgn", "/api/prep/players?q=kind",
                     })
            {
                var res = await client.GetAsync(url);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                all.Append(await res.Content.ReadAsStringAsync());
            }
            foreach (var secret in new[] { "klarakind2014", "lichess", "Schulschach" })
                Assert.DoesNotContain(secret, all.ToString(), StringComparison.OrdinalIgnoreCase);
            var card = JsonNode.Parse(await client.GetStringAsync($"/api/prep/player/{id}"))!;
            // Verwalter (hier der Admin): nur, DASS es ein Konto gibt; Leser: gar keins.
            if (client == admin) Assert.True(card["accounts"]![0]!["hidden"]!.GetValue<bool>());
            else Assert.Empty(card["accounts"]!.AsArray());
            // Die Partie zählt im Baum (wie bei LeagueHub), das Konto bleibt verborgen.
            var tree = JsonNode.Parse(await client.GetStringAsync($"/api/prep/player/{id}/tree?color=w&source=both"))!;
            Assert.Equal(1, tree["online"]!.GetValue<int>());
        }
    }

    [MySqlFact]
    public async Task Accounts_ViewerOnlySure_ManagerAlsoUnsure()
    {
        await ImportAsync(Game("Konto, Karl", "Mair, Josef", "1. e4 c5 2. Nf3 d6", "2025.05.17", "990060"));
        await using (var db = fixture.Schema.NewContext())
        {
            var sure = new LeagueOnlineAccount { FideId = "990060", Site = "lichess", UserName = "sicherkonto", Url = "https://lichess.org/@/sicherkonto",
                Confidence = "sicher", GameCount = 1, Evidence = "Notiz-geheim" };
            var unsure = new LeagueOnlineAccount { FideId = "990060", Site = "chesscom", UserName = "vermutet99", Url = "https://www.chess.com/member/vermutet99",
                Confidence = "wahrscheinlich", GameCount = 1 };
            db.LeagueOnlineAccounts.AddRange(sure, unsure);
            await db.SaveChangesAsync();
            db.LeagueOnlineGames.AddRange(
                new LeagueOnlineGame { AccountId = sure.Id, FideId = "990060", ExternalId = "s1", PlayedAt = DateTime.UtcNow.AddDays(-2), Speed = "blitz",
                    White = true, Result = "1-0", Line = "d4 d5", Moves = "d4 d5", Plies = 2 },
                new LeagueOnlineGame { AccountId = unsure.Id, FideId = "990060", ExternalId = "u1", PlayedAt = DateTime.UtcNow.AddDays(-2), Speed = "blitz",
                    White = true, Result = "1-0", Line = "g3 d5", Moves = "g3 d5", Plies = 2 });
            await db.SaveChangesAsync();
        }
        var id = await PlayerIdAsync("990060");
        using var viewer = Client(await UserAsync("nurleser", false, Permissions.PrepView));
        using var manager = Client(await UserAsync("prepverwalter", false, Permissions.PrepView, Permissions.PrepManage));

        var v = await viewer.GetStringAsync($"/api/prep/player/{id}");
        var vAcc = Assert.Single(JsonNode.Parse(v)!["accounts"]!.AsArray())!;
        Assert.Equal("sicherkonto", vAcc["user"]!.GetValue<string>());
        Assert.DoesNotContain("vermutet99", v);
        Assert.DoesNotContain("Notiz-geheim", v);
        // unsure=true wirkt für Leser nicht — still wie false.
        var vTree = JsonNode.Parse(await viewer.GetStringAsync($"/api/prep/player/{id}/tree?color=w&source=online&unsure=true"))!;
        Assert.Equal(1, vTree["online"]!.GetValue<int>());
        Assert.Equal("d4", Assert.Single(vTree["moves"]!.AsArray())!["san"]!.GetValue<string>());
        var vProfile = JsonNode.Parse(await viewer.GetStringAsync($"/api/prep/player/{id}/profile?source=online&unsure=true"))!;
        Assert.Equal(1, vProfile["online"]!.GetValue<int>());

        var m = JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{id}"))!;
        Assert.Equal(new[] { "sicherkonto", "vermutet99" }, m["accounts"]!.AsArray().Select(a => a!["user"]!.GetValue<string>()).OrderBy(x => x));
        Assert.Contains(m["accounts"]!.AsArray(), a => a!["comment"]?.GetValue<string>() == "Notiz-geheim");
        var mTree = JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{id}/tree?color=w&source=online&unsure=true"))!;
        Assert.Equal(2, mTree["online"]!.GetValue<int>());
        var mSure = JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{id}/tree?color=w&source=online"))!;
        Assert.Equal(1, mSure["online"]!.GetValue<int>());
    }

    [MySqlFact]
    public async Task Search_PrefixEscapeUmlauts_OnMariaDb()
    {
        await ImportAsync(Game("Höcher, Michael", "Mair, Josef", "1. e4 c5", "2020.01.01")
                          + Game("Hoecher, Martin", "Mair, Josef", "1. d4 d5", "2020.01.02")
                          + Game("O'Kelly de Galway, Alberic", "Mair, Josef", "1. c4 e5", "2020.01.03")
                          + Game("Müller-Lüdenscheidt, Zoe", "Mair, Josef", "1. Nf3 d5", "2020.01.04"), PrepSources.Lumbra);
        using var viewer = Client(await UserAsync("sucher", false, Permissions.PrepView));
        async Task<List<string>> Names(string q) =>
            JsonNode.Parse(await viewer.GetStringAsync("/api/prep/players?q=" + Uri.EscapeDataString(q)))!["items"]!.AsArray()
                .Select(x => x!["name"]!.GetValue<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "Hoecher, Martin", "Höcher, Michael" }, await Names("Hoecher"));
        Assert.Equal(new[] { "Hoecher, Martin", "Höcher, Michael" }, await Names("Höcher"));
        Assert.Equal(new[] { "Höcher, Michael" }, await Names("Michael Höcher"));
        // Apostroph und Bindestrich bleiben im Muster (Parameter, kein SQL-Text); „%" und „_" erreichen es nie.
        Assert.Equal(new[] { "O'Kelly de Galway, Alberic" }, await Names("O'Kelly"));
        Assert.Equal(new[] { "Müller-Lüdenscheidt, Zoe" }, await Names("Mueller-L"));
        Assert.Equal(new[] { "Mair, Josef" }, await Names("Mair, Josef"));
        Assert.Empty(await Names("%"));
        Assert.Empty(await Names("_a%"));
    }

    [MySqlFact]
    public async Task Card_YoungestFirst_LimitAndUndatedLast_OnMariaDb()
    {
        // 1003 Partien mit Datum (eine je Tag ab 2000-01-01) + 2 ohne Datum; die Vorgabe lädt die jüngsten DefaultLimit.
        var files = "abcdefgh";
        var sb = new StringBuilder();
        var n = 0;
        for (var a = 0; a < 8 && n < 1005; a++)
            for (var b = 0; b < 8 && n < 1005; b++)
                for (var c = 0; c < 8 && n < 1005; c++)
                    for (var d = 0; d < 8 && n < 1005; d++, n++)
                    {
                        var date = n < 1003 ? new DateTime(2000, 1, 1).AddDays(n).ToString("yyyy.MM.dd") : "????.??.??";
                        var moves = $"1. {files[a]}3 {files[b]}6 2. {files[c]}4 {files[d]}5";
                        sb.Append(n % 2 == 0 ? Game("Vielspieler, Max", $"Gegner{n % 50}, X", moves, date, "990100")
                            : Game($"Gegner{n % 50}, X", "Vielspieler, Max", moves, date, blackFide: "990100"));
                    }
        await ImportAsync(sb.ToString());
        var id = await PlayerIdAsync("990100");
        using var viewer = Client(await UserAsync("vielleser", false, Permissions.PrepView));

        var card = JsonNode.Parse(await viewer.GetStringAsync($"/api/prep/player/{id}"))!;
        Assert.Equal(PrepCardService.DefaultLimit, card["loaded"]!.GetValue<int>());
        Assert.True(card["limited"]!.GetValue<bool>());
        Assert.Equal(1005, card["games"]!.GetValue<int>());
        Assert.Equal(new DateTime(2000, 1, 1).AddDays(1003 - PrepCardService.DefaultLimit).ToString("yyyy.MM.dd"), card["since"]!.GetValue<string>());
        Assert.Equal(new DateTime(2000, 1, 1).AddDays(1002).ToString("yyyy.MM.dd"), card["recent"]![0]!["date"]!.GetValue<string>());
        Assert.Equal(PrepCardService.DefaultLimit, card["n"]!.GetValue<int>());

        var all = JsonNode.Parse(await viewer.GetStringAsync($"/api/prep/player/{id}?all=true"))!;
        Assert.Equal(1005, all["loaded"]!.GetValue<int>());
        Assert.False(all["limited"]!.GetValue<bool>());

        var res = await viewer.GetAsync($"/api/prep/player/{id}/pgn?all=true");
        Assert.Equal("application/x-chess-pgn", res.Content.Headers.ContentType!.MediaType);
        Assert.Equal("Vielspieler_Max_990100.pgn", res.Content.Headers.ContentDisposition!.FileName);
        var pgn = await res.Content.ReadAsStringAsync();
        var games = PgnParser.SplitGames(pgn).ToList();
        Assert.Equal(1005, games.Count);
        Assert.Equal("????.??.??", games[^1].Headers["Date"]);

        var tree = JsonNode.Parse(await viewer.GetStringAsync($"/api/prep/player/{id}/tree?color=w&line=a3"))!;
        Assert.True(tree["total"]!.GetValue<int>() > 0);
        Assert.Equal("a3", tree["line"]!.GetValue<string>());
    }
}

public sealed class PrepReadFixture() : MariaDbClassFixture("prpr", withApp: true)
{
    // Die Klasse schickt gut 60 Anfragen von EINER Adresse; der globale Deckel (100/min) soll nicht mitentscheiden.
    protected override ApiFactory CreateFactory(string connectionString) =>
        new(connectionString, new Dictionary<string, string?> { [RateLimitScale.ConfigKey] = "10" });
}
