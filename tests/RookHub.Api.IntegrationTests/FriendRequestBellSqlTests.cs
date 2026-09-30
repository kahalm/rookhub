using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Entprellung der Anfrage-Glocke gegen ECHTES MariaDB (Codereview N9-003): der Absender steckt als
/// <c>data.username</c> im JSON, gefiltert wird per Teilstring in SQL. Ob Maskierung (Umlaut als ü) und die
/// LIKE-Sonderzeichen eines Namens (<c>_</c>) richtig behandelt werden, sieht man nur hier; InMemory vergleicht ordinal.
/// </summary>
public class FriendRequestBellSqlTests(FriendRequestBellSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<FriendRequestBellSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [MySqlFact]
    public async Task SendWithdrawSend_RingsOnce_AndALookalikeNameDoesNotSilenceIt()
    {
        int sender, lookalike, addressee;
        await using (var db = fixture.Schema.NewContext())
        {
            var s = new AppUser { Username = "Jürgen_M", Email = "j@test.invalid", PasswordHash = "x" };
            var l = new AppUser { Username = "JürgenXM", Email = "x@test.invalid", PasswordHash = "x" };
            var a = new AppUser { Username = "bob", Email = "b@test.invalid", PasswordHash = "x" };
            db.AppUsers.AddRange(s, l, a);
            await db.SaveChangesAsync();
            (sender, lookalike, addressee) = (s.Id, l.Id, a.Id);
        }

        // Zuerst der Doppelgänger: wäre „_" im Suchtext ein LIKE-Platzhalter, hielte seine Glocke die von „Jürgen_M" auf.
        await using (var db = fixture.Schema.NewContext())
            await new FriendService(db, new NotificationService(db)).SendRequestAsync(lookalike, addressee);
        for (var i = 0; i < 3; i++)
        {
            await using var db = fixture.Schema.NewContext();
            var friends = new FriendService(db, new NotificationService(db));
            var f = await friends.SendRequestAsync(sender, addressee);
            await friends.RemoveFriendAsync(f.Id, sender);
        }

        await using var check = fixture.Schema.NewContext();
        var bells = await check.Notifications
            .Where(n => n.UserId == addressee && n.Type == NotificationType.FriendRequestReceived)
            .Select(n => n.DataJson!)
            .ToListAsync();
        Assert.Equal(2, bells.Count);
        Assert.Single(bells, b => b.Contains("J\\u00FCrgen_M"));
        Assert.Single(bells, b => b.Contains("J\\u00FCrgenXM"));
    }
}

public sealed class FriendRequestBellSqlFixture() : MariaDbClassFixture("frbell", withApp: false);
