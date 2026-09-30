using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Namens-Schnappschuss in Benachrichtigungen gegen ECHTES MariaDB (A9-003): die Vorauswahl der Löschung ist ein
/// Teilstring-Filter auf DataJson — ob LIKE/LOCATE die JSON-Maskierung (Umlaut als ü, Backslash, Anführungszeichen)
/// und die LIKE-Sonderzeichen (_ und %) des Namens richtig behandelt, sieht man nur hier; InMemory vergleicht ordinal.
/// Dazu die Retention mit ExecuteDelete (InMemory kennt es nicht).
/// </summary>
public class NotificationRetentionSqlTests(NotificationRetentionSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<NotificationRetentionSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string? Name(Notification n)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(n.DataJson!)!.GetValueOrDefault("username");

    [MySqlFact]
    public async Task MentioningUsername_FindsEscapedNames_AndOnlyTheExactOnes()
    {
        string[] names = ["Jürgen_M", "50%\\off", "say \"hi\"", "Łukasz"];
        int recipient;
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "recipient", PasswordHash = "x" };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            recipient = user.Id;
            var service = new NotificationService(db);
            foreach (var name in names)
            {
                await service.CreateAsync(recipient, NotificationType.FriendRequestReceived,
                    new Dictionary<string, string> { ["username"] = name });
                // Ähnliche Namen: Groß/klein (Kollation!) und Präfix — dürfen nicht mit umbenannt werden.
                await service.CreateAsync(recipient, NotificationType.FriendRequestReceived,
                    new Dictionary<string, string> { ["username"] = name.ToUpperInvariant() + "x" });
            }
            await service.CreateAsync(recipient, NotificationType.FriendRequestReceived,
                new Dictionary<string, string> { ["username"] = "JÜRGEN_M" });
        }

        await using (var db = fixture.Schema.NewContext())
        {
            foreach (var name in names)
            {
                var hits = await NotificationService.MentioningUsernameAsync(db, name, exceptUserId: -1);
                var hit = Assert.Single(hits);
                Assert.Equal(name, Name(hit));
            }
            Assert.Empty(await NotificationService.MentioningUsernameAsync(db, "Jürgen_M", exceptUserId: recipient));
        }
    }

    [MySqlFact]
    public async Task Retention_DeletesSeenOldRows_AndReplacesNamesWithoutAccount()
    {
        var now = DateTime.UtcNow;
        var old = now - NotificationService.SeenRetention - TimeSpan.FromDays(1);
        await using (var db = fixture.Schema.NewContext())
        {
            var admin = new AppUser { Username = "admin", PasswordHash = "x" };
            var alive = new AppUser { Username = "Alive", PasswordHash = "x" };
            db.AppUsers.AddRange(admin, alive);
            await db.SaveChangesAsync();
            db.Notifications.AddRange(
                new Notification { UserId = admin.Id, Type = "seen-old", CreatedAt = old, SeenAt = old },
                new Notification { UserId = admin.Id, Type = "unseen-old", CreatedAt = old },
                new Notification { UserId = admin.Id, Type = "gone", DataJson = "{\"username\":\"J\\u00FCrgen\"}" },
                // Kollation ohne Groß/klein wie der Unique-Index: „alive" ist das Konto „Alive".
                new Notification { UserId = admin.Id, Type = "alive", DataJson = "{\"username\":\"alive\"}" });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Schema.NewContext())
            Assert.Equal((1, 1), await new NotificationService(db).RunRetentionAsync(now));

        await using (var check = fixture.Schema.NewContext())
        {
            var rows = await check.Notifications.OrderBy(n => n.Type).ToListAsync();
            Assert.Equal(new[] { "alive", "gone", "unseen-old" }, rows.Select(n => n.Type).ToArray());
            Assert.Equal("alive", Name(rows[0]));
            Assert.Equal(NotificationService.DeletedActorName, Name(rows[1]));
        }
    }
}

public sealed class NotificationRetentionSqlFixture() : MariaDbClassFixture("nret", withApp: false);
