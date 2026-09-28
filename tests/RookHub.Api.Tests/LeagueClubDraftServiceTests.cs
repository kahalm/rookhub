using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>Entwürfe der PGN-Importe (0.595.0): jede eingereichte Liste liegt online, bis sie importiert oder verworfen ist
/// — damit ein Verwalter fertigstellen kann, wenn der Einreicher abbricht (Wunsch 2026-09-28, analog zu den Formularen).</summary>
public class LeagueClubDraftServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTime _now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private LeagueClubDraftService Svc() => new(_db, () => _now);
    public void Dispose() => _db.Dispose();

    private const string Two = "[White \"A\"]\n[Black \"B\"]\n\n1. e4 e5 1-0\n\n[White \"C\"]\n[Black \"D\"]\n\n1. d4 d5 0-1\n";

    private async Task<(int owner, int other, int manager)> UsersAsync()
    {
        var a = new AppUser { Username = "patrik", PasswordHash = "x" };
        var b = new AppUser { Username = "fremd", PasswordHash = "x" };
        var c = new AppUser { Username = "verwalter", PasswordHash = "x" };
        _db.AppUsers.AddRange(a, b, c);
        await _db.SaveChangesAsync();
        return (a.Id, b.Id, c.Id);
    }

    [Fact]
    public async Task Lifecycle_Create_List_Save_Resume_Delete()
    {
        var (me, _, _) = await UsersAsync();
        var (d, reason) = await Svc().CreateAsync(me, null, Two, "datei", "liga.pgn");
        Assert.Null(reason);
        Assert.Equal((2, 0, "datei", "liga.pgn"), (d!.GameCount, d.ImportedCount, d.Source, d.Label));
        Assert.Null(d.Key);                                                       // mit Konto kein Schlüssel

        var actor = DraftActor.User(me, false);
        await Svc().SaveAsync(actor, d.Id, new LeagueClubDraftSaveRequest { State = "{\"x\":1}", Imported = [1, 1, 0, 999] });
        var mine = Assert.Single(await Svc().ListAsync(actor));
        Assert.Equal(1, mine.ImportedCount);
        var full = (await Svc().GetAsync(actor, d.Id))!;
        Assert.Equal((Two, "{\"x\":1}"), (full.Pgn, full.State));
        Assert.Equal([1], full.Imported);

        Assert.True(await Svc().DeleteAsync(actor, d.Id));
        Assert.Empty(_db.LeagueClubDrafts);                                       // Rohtext samt Namen weg
    }

    [Fact]
    public async Task Access_OwnerAndManagers_NotOthers_ManagersSeeAll_WithTheOwner()
    {
        var (me, other, manager) = await UsersAsync();
        var d = (await Svc().CreateAsync(me, null, Two, "text", null)).Draft!;
        Assert.Null(await Svc().GetAsync(DraftActor.User(other, false), d.Id));
        Assert.False(await Svc().DeleteAsync(DraftActor.User(other, false), d.Id));
        Assert.NotNull(await Svc().GetAsync(DraftActor.User(manager, true), d.Id));
        var all = Assert.Single(await Svc().ListAllAsync(manager));
        Assert.Equal(("patrik", false, false), (all.Owner, all.ViaShareLink, all.Mine));
        Assert.Empty(await Svc().ListAsync(DraftActor.User(manager, true)));      // „eigene" sind nur die eigenen

        // Wer fertigstellt, handelt für den Einreicher — Übersicht und Import rechnen mit SEINEM Profil.
        Assert.Equal((true, (int?)me), await Svc().ActingUserAsync(DraftActor.User(manager, true), d.Id));
        Assert.Equal((false, (int?)null), await Svc().ActingUserAsync(DraftActor.User(other, false), d.Id));
    }

    [Fact]
    public async Task WithoutAccount_TheKeyIsTheAccess_CappedPerAddress_AndManagersFinishForNobody()
    {
        var (_, _, manager) = await UsersAsync();
        var (d, _) = await Svc().CreateAsync(null, "ip-1", Two, "lichess", "https://lichess.org/study/abcdefgh");
        Assert.Matches("^[0-9a-f]{32}$", d!.Key!);
        Assert.NotNull(await Svc().GetAsync(DraftActor.Anonymous(d.Key), null));
        Assert.Null(await Svc().GetAsync(DraftActor.Anonymous("0000"), null));
        Assert.Single(await Svc().ListAsync(DraftActor.Anonymous(null), [d.Key!, "nix"]));
        Assert.True((await Svc().ListAllAsync(manager)).Single().ViaShareLink);
        Assert.Equal((true, (int?)null), await Svc().ActingUserAsync(DraftActor.User(manager, true), d.Id));

        for (var i = 1; i < LeagueClubDraftService.MaxOpenPerIp; i++) Assert.Null((await Svc().CreateAsync(null, "ip-1", Two, null, null)).Reason);
        Assert.Equal("tooManyDrafts", (await Svc().CreateAsync(null, "ip-1", Two, null, null)).Reason);
        Assert.Null((await Svc().CreateAsync(null, "ip-2", Two, null, null)).Reason);   // andere Adresse
    }

    [Fact]
    public async Task Untouched_ForTheRetention_IsGone()
    {
        var (me, _, _) = await UsersAsync();
        var d = (await Svc().CreateAsync(me, null, Two, null, null)).Draft!;
        _now += LeagueClubDraftService.Retention - TimeSpan.FromHours(1);
        await Svc().SaveAsync(DraftActor.User(me, false), d.Id, new LeagueClubDraftSaveRequest { Imported = [2] });   // Bewegung
        _now += TimeSpan.FromDays(2);
        Assert.Single(await Svc().ListAsync(DraftActor.User(me, false)));
        _now += LeagueClubDraftService.Retention;
        Assert.Empty(await Svc().ListAsync(DraftActor.User(me, false)));
        Assert.Empty(_db.LeagueClubDrafts);
    }
}
