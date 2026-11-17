using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.TrainingVideos;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Teilen-Links der Trainerstunden durch die ECHTE Pipeline gegen MariaDB: der Besitzer legt per API an, der Gast liest
/// ohne Anmeldung den Eintrag und das Video mit Bereichsanfrage (206), ohne Notizen; widerrufen/abgelaufen = 410 an
/// beiden Wegen, unbekannt = 404, fremde Konten sehen und widerrufen nichts.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class TrainingShareApiTests(TrainingShareFixture fixture) : IAsyncLifetime, IClassFixture<TrainingShareFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Lesson = """
    {
      "schema": 1, "date": "2026-10-27", "video": { "file": "2026-10-27.mp4", "durationSec": 3737 },
      "summary": "Französisch",
      "games": [ { "id": "g1", "kind": "own", "white": "A", "black": "B" },
                 { "id": "g2", "kind": "lesson", "white": "Capablanca", "black": "Lasker" } ],
      "segments": [
        { "t": "0:04:32", "gameId": "g1", "fen": "r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 2 5",
          "move": "Bb5", "comment": "Mit dem Läuferzug nimmst du dem Springer den Halt.", "tags": ["key"] },
        { "t": "0:50:00", "gameId": "g2", "fen": "r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 2 5",
          "comment": "Lehrbeispiel." }
      ],
      "notes": [ { "t": "0:30:00", "category": "schwaeche", "text": "GEHEIM-Zu-viel-Bedenkzeit", "quote": "Uhr" } ]
    }
    """;

    private async Task<(int Id, string Jwt)> UserAsync(string name)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Username = name, Email = name + "@test.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword("egal-egal-2026") };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, (await scope.ServiceProvider.GetRequiredService<AuthService>().IssueTokenAsync(user)).Token);
    }

    private HttpClient Client(string? jwt)
    {
        var c = fixture.Factory.CreateClient();
        if (jwt is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    [MySqlFact]
    public async Task OwnerShares_GuestReadsLessonAndVideoRange_RevokeGivesGone()
    {
        var (ownerId, ownerJwt) = await UserAsync("tvs-owner");
        var (_, otherJwt) = await UserAsync("tvs-other");
        int privateId, generalId;
        await using (var db = fixture.Schema.NewContext())
        {
            await new TrainingVideoImporter(db, Options.Create(new TrainingVideoOptions())).ImportAsync(ownerId, Lesson, false, default);
            privateId = await db.TrainingLessons.Where(l => l.Part == TrainingLessonParts.Private).Select(l => l.Id).SingleAsync();
            generalId = await db.TrainingLessons.Where(l => l.Part == TrainingLessonParts.General).Select(l => l.Id).SingleAsync();
        }

        var owner = Client(ownerJwt);
        var guest = Client(null);
        var created = await owner.PostAsJsonAsync($"/api/training-videos/{privateId}/shares", new { days = 7 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var share = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var token = share["token"]!.GetValue<string>();
        var shareId = share["id"]!.GetValue<int>();
        Assert.Equal(43, token.Length);

        // Fremdes Konto: weder anlegen noch sehen noch widerrufen.
        var other = Client(otherJwt);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/training-videos/{privateId}/shares", new { days = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/training-videos/{privateId}/shares")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/training-videos/{privateId}/shares/{shareId}")).StatusCode);
        // Ohne Anmeldung gibt es die Besitzer-Wege nicht.
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/training-videos/{privateId}/shares")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/training-videos/{privateId}/shares", new { days = 3 })).StatusCode);

        // Gast: der Eintrag, ohne Notizen, ohne den anderen Teil.
        var view = await guest.GetAsync($"/api/training-videos/shared/{token}");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        var body = await view.Content.ReadAsStringAsync();
        Assert.Contains("Mit dem Läuferzug", body);
        Assert.DoesNotContain("GEHEIM", body);
        Assert.DoesNotContain("Lehrbeispiel", body);
        Assert.DoesNotContain("tvs-owner", body);
        var json = JsonNode.Parse(body)!;
        Assert.Empty(json["otherParts"]!.AsArray());
        Assert.True(json["hasVideo"]!.GetValue<bool>());
        // Gast kann über den Link nicht an eine andere Id: die Besitzer-Wege wollen eine Anmeldung.
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/training-videos/{generalId}")).StatusCode);

        // Video mit Bereichsanfrage.
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/training-videos/shared/{token}/video");
        req.Headers.Range = new RangeHeaderValue(5, 9);
        var part = await guest.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal("56789", await part.Content.ReadAsStringAsync());
        Assert.Equal("bytes 5-9/20", part.Content.Headers.ContentRange!.ToString());

        // Liste des Besitzers, dann widerrufen → 410 an beiden Gast-Wegen.
        var list = (await owner.GetFromJsonAsync<JsonArray>($"/api/training-videos/{privateId}/shares"))!;
        Assert.Equal(token, Assert.Single(list)!["token"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/training-videos/{privateId}/shares/{shareId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await guest.GetAsync($"/api/training-videos/shared/{token}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await guest.GetAsync($"/api/training-videos/shared/{token}/video")).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonArray>($"/api/training-videos/{privateId}/shares"))!);

        // Abgelaufen → 410, unbekannt → 404.
        var second = (await (await owner.PostAsJsonAsync($"/api/training-videos/{privateId}/shares", new { days = 1 })).Content.ReadFromJsonAsync<JsonObject>())!;
        var token2 = second["token"]!.GetValue<string>();
        await using (var db = fixture.Schema.NewContext())
            await db.TrainingLessonShares.Where(s => s.Token == token2).ExecuteUpdateAsync(u => u.SetProperty(s => s.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Gone, (await guest.GetAsync($"/api/training-videos/shared/{token2}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/training-videos/shared/{new string('A', 43)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/training-videos/shared/{new string('A', 43)}/video")).StatusCode);
    }
}

public sealed class TrainingShareFixture() : MariaDbClassFixture("tvsh", withApp: true)
{
    public static readonly string VideoRoot = Path.Combine(Path.GetTempPath(), "rh-it-tvsh-" + Guid.NewGuid().ToString("N"));

    protected override ApiFactory CreateFactory(string connectionString)
    {
        Directory.CreateDirectory(VideoRoot);
        File.WriteAllText(Path.Combine(VideoRoot, "2026-10-27.mp4"), "0123456789abcdefghij");
        return new(connectionString, new Dictionary<string, string?>
        {
            ["TrainingVideos:VideoRoot"] = VideoRoot,
            [RateLimitScale.ConfigKey] = "10",
        });
    }
}
