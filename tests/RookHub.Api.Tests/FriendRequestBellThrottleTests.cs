using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Glocken-Flut über Freundschaftsanfragen und Challenges (Codereview 2026-09-29, N9-003). Jede Anfrage legte beim
/// Ziel eine Glocke samt Web-Push-Auftrag an; der Absender konnte die offene Anfrage selbst zurückziehen oder nach einer
/// Ablehnung sofort neu senden — nur der globale Deckel von 100/min je Adresse bremste (~50 Glocken je Minute gegen ein
/// beliebiges Konto). Jetzt: Rate-Limit je Konto für Anfrage und Challenge, und die Anfrage-Glocke klingelt je
/// (Absender, Empfänger) nicht erneut, solange die letzte ungelesen oder jünger als 24 h ist. Die Anfrage selbst bleibt
/// erlaubt (PD-080).
/// </summary>
public class FriendRequestBellThrottleTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FriendService _friends;

    public FriendRequestBellThrottleTests() => _friends = new FriendService(_db, new NotificationService(_db));

    public void Dispose() => _db.Dispose();

    private async Task<int> UserAsync(string name)
    {
        var u = new AppUser { Username = name, Email = $"{name}@test.com", PasswordHash = "x", Profile = new UserProfile() };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private Task<List<Notification>> RequestBellsAsync(int to)
        => _db.Notifications.Where(n => n.UserId == to && n.Type == NotificationType.FriendRequestReceived).ToListAsync();

    // ── Entprellung der Anfrage-Glocke ───────────────────────────────────────────────────────────

    /// <summary>Das gemeldete Fehlerbild: Senden → Zurückziehen → Senden … — vorher je Runde eine Glocke.</summary>
    [Fact]
    public async Task SendWithdrawSend_RingsOnce_ButEveryRequestIsStored()
    {
        var attacker = await UserAsync("mallory");
        var victim = await UserAsync("victim");

        for (var i = 0; i < 5; i++)
        {
            var f = await _friends.SendRequestAsync(attacker, victim);
            await _friends.RemoveFriendAsync(f.Id, attacker);
        }
        var last = await _friends.SendRequestAsync(attacker, victim);

        Assert.Single(await RequestBellsAsync(victim));
        Assert.Equal(FriendshipStatus.Pending, (await _db.Friendships.SingleAsync()).Status);
        Assert.Equal(last.Id, (await _db.Friendships.SingleAsync()).Id);
    }

    /// <summary>Ablehnen half nicht: die Declined-Zeile wird beim Neusenden gelöscht (PD-080 — das bleibt so), die
    /// Glocke kam jedes Mal neu. Auch wenn das Opfer die erste gelesen hat: innerhalb von 24 h keine zweite.</summary>
    [Fact]
    public async Task ResendAfterDecline_WithinQuietPeriod_NoNewBell_EvenIfTheFirstWasRead()
    {
        var sender = await UserAsync("alice");
        var addressee = await UserAsync("bob");

        var f = await _friends.SendRequestAsync(sender, addressee);
        foreach (var n in await RequestBellsAsync(addressee)) n.SeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _friends.DeclineRequestAsync(f.Id, addressee);

        await _friends.SendRequestAsync(sender, addressee);

        Assert.Single(await RequestBellsAsync(addressee));
        Assert.Equal(FriendshipStatus.Pending, (await _db.Friendships.SingleAsync()).Status);
    }

    /// <summary>Nach der Ruhefrist klingelt eine neue Anfrage wieder — gelesene Glocke, älter als 24 h.</summary>
    [Fact]
    public async Task ResendAfterQuietPeriod_RingsAgain()
    {
        var sender = await UserAsync("alice");
        var addressee = await UserAsync("bob");

        var f = await _friends.SendRequestAsync(sender, addressee);
        var first = Assert.Single(await RequestBellsAsync(addressee));
        first.CreatedAt = DateTime.UtcNow - FriendService.RequestBellQuietPeriod - TimeSpan.FromMinutes(1);
        first.SeenAt = DateTime.UtcNow.AddHours(-2);
        await _db.SaveChangesAsync();
        await _friends.DeclineRequestAsync(f.Id, addressee);

        await _friends.SendRequestAsync(sender, addressee);

        Assert.Equal(2, (await RequestBellsAsync(addressee)).Count);
    }

    /// <summary>Solange die letzte Glocke ungelesen ist, klingelt es nicht erneut — auch nach Tagen.</summary>
    [Fact]
    public async Task UnreadOldBell_StillSuppressesANewOne()
    {
        var sender = await UserAsync("alice");
        var addressee = await UserAsync("bob");

        var f = await _friends.SendRequestAsync(sender, addressee);
        var first = Assert.Single(await RequestBellsAsync(addressee));
        first.CreatedAt = DateTime.UtcNow.AddDays(-5);
        await _db.SaveChangesAsync();
        await _friends.RemoveFriendAsync(f.Id, sender);

        await _friends.SendRequestAsync(sender, addressee);

        Assert.Single(await RequestBellsAsync(addressee));
    }

    /// <summary>Entprellt wird je (Absender, Empfänger): ein anderer Absender — auch mit ähnlichem Namen — klingelt.</summary>
    [Fact]
    public async Task OtherSenders_StillRing()
    {
        var alice = await UserAsync("alice");
        var alice2 = await UserAsync("alice2");
        var addressee = await UserAsync("bob");
        var other = await UserAsync("carol");

        await _friends.SendRequestAsync(alice, addressee);
        await _friends.SendRequestAsync(alice2, addressee);
        await _friends.SendRequestAsync(alice, other);

        Assert.Equal(2, (await RequestBellsAsync(addressee)).Count);
        Assert.Single(await RequestBellsAsync(other));
    }

    // ── Rate-Limit je Konto ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(FriendController), nameof(FriendController.SendRequest))]
    [InlineData(typeof(ChallengeController), nameof(ChallengeController.Create))]
    public void BellTriggers_UseThePerAccountSocialPolicy(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;
        Assert.Equal(RateLimitPartitions.UserSocialPolicy,
            method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void SocialPolicy_IsRegisteredInProgram()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains($@"AddPolicy(""{RateLimitPartitions.UserSocialPolicy}"", ctx => RookHub.Api.Services.RateLimitPartitions.UserSocial(ctx, permitScale))", src);
    }

    /// <summary>Die 11. Anfrage in der Minute wird abgewiesen (429); ein zweites Konto hinter derselben Adresse (Verein,
    /// Schule) hat sein eigenes Fenster.</summary>
    [Fact]
    public void UserSocial_CapsOneAccount_ButNotItsNeighboursBehindTheSameAddress()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.UserSocial(c, 1));
        var permit = RateLimitPartitions.UserSocialPermitPerMinute;

        Assert.Equal(10, permit);
        Assert.Equal(permit, Acquired(limiter, Context(42), permit + 5));
        Assert.Equal(permit, Acquired(limiter, Context(43), permit));
    }

    private static HttpContext Context(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        return ctx;
    }

    private static int Acquired(PartitionedRateLimiter<HttpContext> limiter, HttpContext ctx, int tries)
    {
        var ok = 0;
        for (var i = 0; i < tries; i++)
        {
            using var lease = limiter.AttemptAcquire(ctx);
            if (lease.IsAcquired) ok++;
        }
        return ok;
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}
