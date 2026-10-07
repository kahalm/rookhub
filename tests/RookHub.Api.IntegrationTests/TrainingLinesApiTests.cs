using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Prep;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Trainingslinien gegen einen Gegner (2026-10-07) durch die ECHTE Pipeline gegen MariaDB: Prep-Endpunkt hinter <c>prep.view</c>,
/// Liga-Endpunkt hinter <c>league.view</c> (beide 401 ohne Anmeldung, 403 ohne Recht), ein fremdes oder nicht freigegebenes
/// Repertoire 404, und über einen Teilen-Link gibt es den Endpunkt nicht. FIDE-IDs im 99xxxx-Bereich.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class TrainingLinesApiTests(TrainingLinesFixture fixture) : IAsyncLifetime, IClassFixture<TrainingLinesFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string Game(string white, string black, string moves, string date, string? whiteFide = null, string? blackFide = null) =>
        $"[Event \"Open Schwaz\"]\n[Site \"Schwaz\"]\n[Date \"{date}\"]\n[Round \"1\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n"
        + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n") + (blackFide is null ? "" : $"[BlackFideId \"{blackFide}\"]\n")
        + "\n" + moves + " 1-0\n\n";

    private async Task<(int Id, string Jwt)> UserAsync(string name, params string[] permissions)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Username = name, PasswordHash = BCrypt.Net.BCrypt.HashPassword("egal-egal-2026") };
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
        return (user.Id, (await scope.ServiceProvider.GetRequiredService<AuthService>().IssueTokenAsync(user)).Token);
    }

    private async Task<int> RepertoireAsync(int userId, string pgn, bool forExtension = true)
    {
        await using var db = fixture.Schema.NewContext();
        var rep = new Repertoire { UserId = userId, Name = "Rep " + userId, UseForExtension = forExtension, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Repertoires.Add(rep);
        await db.SaveChangesAsync();
        db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "r.pgn", PgnContent = pgn, FileSize = pgn.Length });
        await db.SaveChangesAsync();
        return rep.Id;
    }

    private HttpClient Client(string? jwt)
    {
        var c = fixture.Factory.CreateClient();
        if (jwt is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    private const string Najdorf = "[Event \"x\"]\n[Black \"Sizilianisch\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 *\n\n"
                                   + "[Event \"x\"]\n[Black \"Sizilianisch\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *\n";

    [MySqlFact]
    public async Task Prep_RightsAndOwnership_AndTheRanking()
    {
        // Gegner 990101 hat Schwarz gespielt: zweimal 1...c5, einmal 1...e5
        var pgn = Game("Huber, Franz", "Gegner, Gerd", "1. e4 c5 2. Nf3 d6 3. d4 cxd4", "2024.05.17", blackFide: "990101")
                  + Game("Mair, Josef", "Gegner, Gerd", "1. e4 c5 2. Nf3 d6 3. Bb5+", "2023.03.01", blackFide: "990101")
                  + Game("Pichler, Anna", "Gegner, Gerd", "1. e4 e5 2. Nf3 Nc6 3. Bc4", "2022.01.01", blackFide: "990101");
        await using (var db = fixture.Schema.NewContext())
            await new PrepImportService(db).ImportChunkAsync(PrepSources.Mega, 0, 0, pgn, default);
        int id;
        await using (var db = fixture.Schema.NewContext()) id = db.PrepPlayers.Single(p => p.FideId == "990101").Id;

        var (viewerId, viewerJwt) = await UserAsync("leser", Permissions.PrepView);
        var (_, plainJwt) = await UserAsync("ohne");
        var (_, leagueJwt) = await UserAsync("liga", Permissions.LeagueView);
        var (otherId, _) = await UserAsync("anderer", Permissions.PrepView);
        var mine = await RepertoireAsync(viewerId, Najdorf);
        var off = await RepertoireAsync(viewerId, Najdorf, forExtension: false);
        var foreign = await RepertoireAsync(otherId, Najdorf);

        var url = $"/api/prep/player/{id}/training-lines";
        using (var anonymous = Client(null)) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        using (var plain = Client(plainJwt)) Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync(url)).StatusCode);
        using (var league = Client(leagueJwt)) Assert.Equal(HttpStatusCode.Forbidden, (await league.GetAsync(url)).StatusCode);

        using var viewer = Client(viewerJwt);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"{url}?repertoire={foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"{url}?repertoire={off}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/prep/player/987654321/training-lines")).StatusCode);

        var r = JsonNode.Parse(await viewer.GetStringAsync(url))!;
        Assert.Equal(mine, Assert.Single(r["repertoires"]!.AsArray())!["id"]!.GetValue<int>());
        Assert.Equal(mine, r["repertoire"]!.GetValue<int>());
        Assert.Equal("w", r["color"]!.GetValue<string>());
        Assert.Equal(3, r["games"]!.GetValue<int>());
        var lines = r["lines"]!.AsArray();
        Assert.Equal("c5", lines[0]!["moves"]![1]!.GetValue<string>());
        Assert.Equal(2.0 / 3, lines[0]!["probability"]!.GetValue<double>(), 4);
        Assert.Equal(2, lines[0]!["reached"]!.GetValue<int>());
        Assert.Equal(2024, lines[0]!["lastYear"]!.GetValue<int>());
        Assert.Equal(1.0 / 3, lines[1]!["probability"]!.GetValue<double>(), 4);
    }

    [MySqlFact]
    public async Task League_RightsAndOwnership_NoShareLinkVariant()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile
            {
                FideId = "990102", Name = "Gegner, Gerd", GameCount = 1, UpdatedAt = DateTime.UtcNow,
                Pgn = Game("Huber, Franz", "Gegner, Gerd", "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6", "2025.02.01", blackFide: "990102"),
            });
            await db.SaveChangesAsync();
        }
        var (readerId, readerJwt) = await UserAsync("ligaleser", Permissions.LeagueView);
        var (_, prepJwt) = await UserAsync("nurprep", Permissions.PrepView);
        var (otherId, _) = await UserAsync("fremder", Permissions.LeagueView);
        await RepertoireAsync(readerId, Najdorf);
        var foreign = await RepertoireAsync(otherId, Najdorf);

        const string url = "/api/league/player/990102/training-lines";
        using (var anonymous = Client(null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
            // über einen Teilen-Link gibt es den Endpunkt nicht (es gäbe auch kein Repertoire)
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/league/s/irgendein-token/player/990102/training-lines")).StatusCode);
        }
        using (var prep = Client(prepJwt)) Assert.Equal(HttpStatusCode.Forbidden, (await prep.GetAsync(url)).StatusCode);

        using var reader = Client(readerJwt);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"{url}?repertoire={foreign}")).StatusCode);
        var r = JsonNode.Parse(await reader.GetStringAsync(url))!;
        Assert.Equal(1, r["games"]!.GetValue<int>());
        var lines = r["lines"]!.AsArray();
        Assert.Equal("e5", lines[0]!["moves"]![1]!.GetValue<string>());
        Assert.Equal(1.0, lines[0]!["probability"]!.GetValue<double>(), 4);
        Assert.Equal(2025, lines[0]!["lastYear"]!.GetValue<int>());
        Assert.True(lines[1]!["neverReached"]!.GetValue<bool>());
    }
}

public sealed class TrainingLinesFixture() : MariaDbClassFixture("trln", withApp: true);
