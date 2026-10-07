using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Prep;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Spielervorbereitung, Phase 4, durch die ECHTE Pipeline gegen MariaDB — mit eingeschaltetem <c>Prep:AccountSearch</c>, aber ohne
/// eine einzige Suche (keine Abrufe bei Lichess/chess.com): Vorschläge werden gesät. Geprüft: nur mit <c>prep.manage</c>; Vorschlag,
/// Prüfung und Übernahme eines Minderjährigen kommen über <c>/api/prep/*</c> nicht heraus, auch nicht für einen Admin; das Übernehmen
/// legt das Konto in der Tabelle von LeagueHub an; LeagueHubs Übersicht und Zähler (SQL mit EXISTS) bleiben frei davon.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class PrepAccountApiTests(PrepAccountFixture fixture) : IAsyncLifetime, IClassFixture<PrepAccountFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string Game(string white, string? whiteFide) =>
        $"[Event \"Open\"]\n[Date \"2024.05.17\"]\n[White \"{white}\"]\n[Black \"Gegner, Eins\"]\n[Result \"1-0\"]\n[WhiteElo \"2210\"]\n"
        + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n") + $"\n1. e4 e5 2. Nf3 {(whiteFide == "990801" ? "Nc6" : "Nf6")} 1-0\n\n";

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

    private HttpClient Client(string jwt)
    {
        var c = fixture.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    /// <summary>Erwachsener 990801 und Minderjähriger 990802 im Bestand, je ein offener Vorschlag; ein Ligaspieler 990803 mit Vorschlag.</summary>
    private async Task<(int Adult, int Minor, int AdultSugg, int MinorSugg)> SeedAsync()
    {
        await using var db = fixture.Schema.NewContext();
        await new PrepImportService(db).ImportChunkAsync(PrepSources.Mega, 0, 0, Game("Erwachsen, Anton", "990801") + Game("Kind, Klara", "990802"), default);
        // ein Verein (Mandanten-Schritt 2026-10-07): die vereinsgebundenen LeagueHub-Endpunkte (/sources) brauchen einen —
        // ResetAsync leert auch LeagueClubs; ein Admin ohne Gruppe nimmt dann den einzigen
        db.LeagueClubs.Add(new LeagueClub { Name = "SK Testdorf", TeamPrefix = "Testdorf", AnonName = "Testdorf", CreatedAt = DateTime.UtcNow });
        db.LeagueTournaments.Add(new LeagueTournament { Tnr = 9901, Name = "Landesliga", Season = "2026/27", League = "LL", Stage = "Liga" });
        db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 9901, Team = "Kufstein 1", Name = "Liga, Lena", NameKey = "liga, lena", FideId = "990803", Fed = "AUT", EloI = 1900 });
        db.LeagueAccountScans.AddRange(
            new LeagueAccountScan { FideId = "990801", BirthYear = 1980, ScannedAt = DateTime.UtcNow, Version = 7 },
            new LeagueAccountScan { FideId = "990802", BirthYear = DateTime.UtcNow.Year - 12, ScannedAt = DateTime.UtcNow, Version = 7 });
        LeagueAccountSuggestion S(string fide, string user) => new()
        {
            FideId = fide, Site = "lichess", UserName = user, Url = "https://lichess.org/@/" + user, Score = 5,
            Evidence = "Nutzername aus dem Namen; Klarname im Profil („" + user + "“)", Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow,
        };
        var adult = S("990801", "AntonErwachsen");
        var minor = S("990802", "KlaraKind2014");
        db.LeagueAccountSuggestions.AddRange(adult, minor, S("990803", "LenaLiga"));
        await db.SaveChangesAsync();
        return (db.PrepPlayers.Single(p => p.FideId == "990801").Id, db.PrepPlayers.Single(p => p.FideId == "990802").Id, adult.Id, minor.Id);
    }

    [MySqlFact]
    public async Task OnlyPrepManage_AndTheCardSaysSo()
    {
        var (adult, _, adultSugg, _) = await SeedAsync();
        using var viewer = Client(await UserAsync("nurleser", false, Permissions.PrepView));
        using var manager = Client(await UserAsync("prepverwalter", false, Permissions.PrepView, Permissions.PrepManage));
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/prep/player/{adult}/suggestions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/prep/suggestions/{adultSugg}/accept", new { sure = true })).StatusCode);
        Assert.False(JsonNode.Parse(await viewer.GetStringAsync($"/api/prep/player/{adult}"))!["accountSearch"]!.GetValue<bool>());
        Assert.True(JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{adult}"))!["accountSearch"]!.GetValue<bool>());
        var list = JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{adult}/suggestions"))!;
        Assert.Equal("AntonErwachsen", Assert.Single(list["items"]!.AsArray())!["user"]!.GetValue<string>());
        Assert.Equal(PrepAccountSearch.DefaultPerHour, list["remaining"]!.GetValue<int>());
    }

    [MySqlFact]
    public async Task LeagueHubEndpoints_PrepOnlyFide_UnknownLikeBefore_EvenWithSwitchOn()
    {
        // Ein Netz ohne Netz: fragte ein Endpunkt doch bei Lichess/chess.com nach, schlüge der Test fehl statt abzurufen.
        var calls = 0;
        using var guarded = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(LeagueOnlineSync.ClientName).ConfigurePrimaryHttpMessageHandler(() => new NoNetwork(() => calls++))));
        var (adult, _, adultSugg, _) = await SeedAsync();
        using var admin = guarded.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UserAsync("ligaverwalter", admin: true));
        async Task NotFound(Task<HttpResponseMessage> call, string? reason = null)
        {
            using var r = await call;
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            if (reason is not null) Assert.Contains(reason, await r.Content.ReadAsStringAsync());
        }

        // Anlegen, suchen, übernehmen, prüfen, verwerfen, auflisten über LeagueHub: der Spieler ist unbekannt — wie vor 0.637.0.
        await NotFound(admin.PostAsync("/api/league/player/990801/suggestions/scan", null), "unknownPlayer");
        await NotFound(admin.PostAsJsonAsync("/api/league/player/990801/accounts", new { site = "lichess", user = "AntonErwachsen", sure = true }), "unknownPlayer");
        await NotFound(admin.PostAsJsonAsync($"/api/league/suggestions/{adultSugg}/accept", new { sure = true }));
        await NotFound(admin.GetAsync($"/api/league/suggestions/{adultSugg}/checks"));
        await NotFound(admin.PostAsync($"/api/league/suggestions/{adultSugg}/reject", null));
        Assert.Empty(JsonNode.Parse(await admin.GetStringAsync("/api/league/player/990801/suggestions"))!["items"]!.AsArray());

        // Der Weg der Spielervorbereitung geht — danach steht das Konto auf ihrer Karte, LeagueHub sieht es weiter nicht.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/prep/suggestions/{adultSugg}/accept", new { sure = true })).StatusCode);
        Assert.Contains("AntonErwachsen", await admin.GetStringAsync($"/api/prep/player/{adult}"));
        int accountId;
        await using (var db = fixture.Schema.NewContext()) accountId = db.LeagueOnlineAccounts.Single(a => a.FideId == "990801").Id;
        await NotFound(admin.GetAsync("/api/league/player/990801"));
        await NotFound(admin.GetAsync("/api/league/player/990801/tree?source=both"));
        await NotFound(admin.GetAsync("/api/league/player/990801/profile?source=both"));
        await NotFound(admin.PutAsJsonAsync($"/api/league/accounts/{accountId}", new { comment = "x" }));
        await NotFound(admin.PostAsync($"/api/league/accounts/{accountId}/sync", null));
        await NotFound(admin.GetAsync($"/api/league/accounts/{accountId}/checks"));
        await NotFound(admin.DeleteAsync($"/api/league/accounts/{accountId}"));
        var sources = JsonNode.Parse(await admin.GetStringAsync("/api/league/sources?fides=990801"))!;
        Assert.Equal(0, sources["opponent"]!["onlineAccounts"]!.GetValue<int>());
        await using (var db = fixture.Schema.NewContext()) Assert.Single(db.LeagueOnlineAccounts.Where(a => a.FideId == "990801").ToList());

        // Gegenrichtung: für den Ligaspieler 990803 bleibt alles beim Alten.
        var lena = Assert.Single(JsonNode.Parse(await admin.GetStringAsync("/api/league/player/990803/suggestions"))!["items"]!.AsArray())!;
        Assert.Equal("LenaLiga", lena["user"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/league/suggestions/{lena["id"]!.GetValue<int>()}/reject", null)).StatusCode);
        Assert.Equal(0, calls);
    }

    [MySqlFact]
    public async Task Accounts_MaintainedThroughPrep_LeaguePlayersInLeagueHub_MinorsNever()
    {
        var (adult, minor, _, _) = await SeedAsync();
        int league, adultAcc, minorAcc, leagueAcc;
        await using (var db = fixture.Schema.NewContext())
        {
            // Lena Liga steht auch im Bestand — ihre Konten pflegt trotzdem LeagueHub.
            await new PrepImportService(db).ImportChunkAsync(PrepSources.Mega, 1, 2, Game("Liga, Lena", "990803"), default);
            league = db.PrepPlayers.Single(p => p.FideId == "990803").Id;
            LeagueOnlineAccount A(string fide, string user) => new()
            {
                FideId = fide, Site = "lichess", UserName = user, Url = "https://lichess.org/@/" + user, Confidence = LeagueOnlineAccountService.Unsure,
                Evidence = "Vorschlag der Konto-Suche: 2 Punkte", Manual = true, GameCount = 1,
            };
            var accs = new[] { A("990801", "AntonFalsch"), A("990802", "KlaraKind2014"), A("990803", "LenaLiga") };
            db.LeagueOnlineAccounts.AddRange(accs);
            await db.SaveChangesAsync();
            foreach (var a in accs)
                db.LeagueOnlineGames.Add(new LeagueOnlineGame { AccountId = a.Id, FideId = a.FideId, ExternalId = a.UserName + "-1", PlayedAt = DateTime.UtcNow,
                    Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" });
            await db.SaveChangesAsync();
            (adultAcc, minorAcc, leagueAcc) = (accs[0].Id, accs[1].Id, accs[2].Id);
        }
        using var viewer = Client(await UserAsync("kontenleser", false, Permissions.PrepView));
        using var manager = Client(await UserAsync("kontenpfleger", false, Permissions.PrepView, Permissions.PrepManage));
        using var leagueAdmin = Client(await UserAsync("ligapfleger", admin: true));

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/prep/accounts/{adultAcc}", new { sure = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/prep/accounts/{adultAcc}")).StatusCode);

        // Spieler nur im Bestand: die Liste zeigt das Konto, umstufen und entfernen gehen — mit seinen Partien.
        var list = JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{adult}/suggestions"))!;
        Assert.False(list["leagueHub"]!.GetValue<bool>());
        Assert.Equal("AntonFalsch", Assert.Single(list["accounts"]!.AsArray())!["user"]!.GetValue<string>());
        var put = await manager.PutAsJsonAsync($"/api/prep/accounts/{adultAcc}", new { sure = true, comment = "doch er" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(LeagueOnlineAccountService.Sure, JsonNode.Parse(await put.Content.ReadAsStringAsync())!["conf"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await leagueAdmin.DeleteAsync($"/api/league/accounts/{adultAcc}")).StatusCode);   // LeagueHub nicht
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/prep/accounts/{adultAcc}")).StatusCode);
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.False(db.LeagueOnlineAccounts.Any(a => a.Id == adultAcc));
            Assert.False(db.LeagueOnlineGames.Any(g => g.FideId == "990801"));                     // seine geholten Partien sind weg
            Assert.Equal(LeagueSuggestionStatus.Rejected, db.LeagueAccountSuggestions.Single(x => x.UserName == "AntonFalsch").Status);
        }

        // Minderjährig: nichts — weder in der Liste noch zum Pflegen, auch nicht für einen Admin.
        var minorList = await leagueAdmin.GetStringAsync($"/api/prep/player/{minor}/suggestions");
        Assert.Empty(JsonNode.Parse(minorList)!["accounts"]!.AsArray());
        Assert.DoesNotContain("KlaraKind2014", minorList);
        Assert.Equal(HttpStatusCode.NotFound, (await leagueAdmin.PutAsJsonAsync($"/api/prep/accounts/{minorAcc}", new { sure = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leagueAdmin.DeleteAsync($"/api/prep/accounts/{minorAcc}")).StatusCode);

        // Auch Ligaspieler: ansehen ja, pflegen nein (409 leagueHub) — LeagueHub pflegt wie bisher.
        var leagueList = JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{league}/suggestions"))!;
        Assert.True(leagueList["leagueHub"]!.GetValue<bool>());
        Assert.Single(leagueList["accounts"]!.AsArray());
        var refused = await manager.DeleteAsync($"/api/prep/accounts/{leagueAcc}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("leagueHub", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync($"/api/prep/accounts/{leagueAcc}", new { sure = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await leagueAdmin.PutAsJsonAsync($"/api/league/accounts/{leagueAcc}", new { sure = true })).StatusCode);
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.True(db.LeagueOnlineAccounts.Any(a => a.Id == minorAcc));
            Assert.Equal(1, db.LeagueOnlineGames.Count(g => g.FideId == "990802"));
            Assert.Equal(LeagueOnlineAccountService.Sure, db.LeagueOnlineAccounts.Single(a => a.Id == leagueAcc).Confidence);
        }
    }

    /// <summary>Statt Lichess/chess.com: zählt und verweigert.</summary>
    private sealed class NoNetwork(Action seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            seen();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    [MySqlFact]
    public async Task Minor_NothingComesOut_NotEvenForAdmins()
    {
        var (_, minor, _, minorSugg) = await SeedAsync();
        using var admin = Client(await UserAsync("admin", admin: true));
        var list = await admin.GetStringAsync($"/api/prep/player/{minor}/suggestions");
        Assert.Empty(JsonNode.Parse(list)!["items"]!.AsArray());
        Assert.DoesNotContain("KlaraKind2014", list);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/prep/suggestions/{minorSugg}/checks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/prep/suggestions/{minorSugg}/accept", new { sure = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/prep/suggestions/{minorSugg}/reject", null)).StatusCode);
        Assert.DoesNotContain("KlaraKind2014", await admin.GetStringAsync($"/api/prep/player/{minor}"));
        await using var db = fixture.Schema.NewContext();
        Assert.Empty(db.LeagueOnlineAccounts.Where(a => a.FideId == "990802").ToList());
    }

    [MySqlFact]
    public async Task Accept_IntoLeagueTable_LeagueHubOverviewAndCountersStayClean()
    {
        var (adult, _, adultSugg, _) = await SeedAsync();
        using var manager = Client(await UserAsync("prepverwalter2", false, Permissions.PrepView, Permissions.PrepManage));
        var res = await manager.PostAsJsonAsync($"/api/prep/suggestions/{adultSugg}/accept", new { sure = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("AntonErwachsen", JsonNode.Parse(await res.Content.ReadAsStringAsync())!["user"]!.GetValue<string>());
        var card = JsonNode.Parse(await manager.GetStringAsync($"/api/prep/player/{adult}"))!;
        Assert.Equal("AntonErwachsen", card["accounts"]![0]!["user"]!.GetValue<string>());

        await using (var db = fixture.Schema.NewContext())
        {
            var acc = db.LeagueOnlineAccounts.Single(a => a.FideId == "990801");
            Assert.Equal("prepverwalter2", acc.AddedBy);
            db.LeagueOnlineGames.Add(new LeagueOnlineGame { AccountId = acc.Id, FideId = "990801", ExternalId = "x1", PlayedAt = DateTime.UtcNow,
                Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4", Plies = 1 });
            await db.SaveChangesAsync();
        }
        using var leagueAdmin = Client(await UserAsync("ligaadmin", admin: true));
        var overview = await leagueAdmin.GetStringAsync("/api/league/suggestions");
        Assert.Contains("LenaLiga", overview);                                        // Ligaspieler weiter da …
        Assert.DoesNotContain("AntonErwachsen", overview);                            // … Prep-Spieler nicht
        Assert.DoesNotContain("KlaraKind2014", overview);
        var sources = JsonNode.Parse(await leagueAdmin.GetStringAsync("/api/league/sources"))!;
        Assert.Equal(0, sources["onlineTotal"]!.GetValue<int>());                      // die Partie des Prep-Kontos zählt dort nicht
    }
}

public sealed class PrepAccountFixture() : MariaDbClassFixture("pacc", withApp: true)
{
    protected override ApiFactory CreateFactory(string connectionString) =>
        new(connectionString, new Dictionary<string, string?>
        {
            [PrepAccountSearch.EnabledKey] = "true",
            [RateLimitScale.ConfigKey] = "10",
        });
}
