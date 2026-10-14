using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Controllers.Accounts;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Accounts;

/// <summary>Gruppe in Gruppe (2026-10-14, Wunsch: „bei Gruppen auch Gruppen Gruppen zuweisen — dann sind alle User der
/// ersten Gruppe auch in der 2."): Mitglieder einer enthaltenen Gruppe zählen in der umfassenden mit — über mehrere
/// Stufen, für Rechte, Freigaben und Vereine; Kreise und „Everyone" sind ausgeschlossen.</summary>
public class GroupInclusionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GroupController _controller;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public GroupInclusionTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _controller = new GroupController(_db, new TrainingGoalService(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private async Task<int> GroupAsync(string name)
    {
        var r = Assert.IsType<OkObjectResult>(await _controller.Create(new CreateGroupDto { Name = name }));
        return Assert.IsType<GroupDto>(r.Value).Id;
    }

    private async Task<int> UserAsync(string name)
    {
        var u = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    /// <summary>Jugend ⊂ Verein ⊂ Tirol; „kid" steht nur in Jugend, „coach" direkt in Verein.</summary>
    private async Task<(int jugend, int verein, int tirol, int kid, int coach)> SeedAsync()
    {
        var (jugend, verein, tirol) = (await GroupAsync("Jugend"), await GroupAsync("Verein"), await GroupAsync("Tirol"));
        var (kid, coach) = (await UserAsync("kid"), await UserAsync("coach"));
        await _controller.AddMember(jugend, kid);
        await _controller.AddMember(verein, coach);
        Assert.IsType<NoContentResult>(await _controller.AddInclusion(verein, jugend));
        Assert.IsType<NoContentResult>(await _controller.AddInclusion(tirol, verein));
        return (jugend, verein, tirol, kid, coach);
    }

    private async Task<List<GroupMemberDto>> MembersAsync(int group) =>
        Assert.IsType<List<GroupMemberDto>>(Assert.IsType<OkObjectResult>(await _controller.GetMembers(group)).Value);

    [Fact]
    public async Task MembersOfAnIncludedGroup_CountInEveryEnclosingGroup_OverSeveralLevels()
    {
        var (jugend, verein, tirol, kid, coach) = await SeedAsync();

        Assert.Equal(new[] { jugend, verein, tirol }.ToHashSet(), await GroupMembership.GroupIdsOfAsync(_db, kid));
        Assert.Equal(new[] { verein, tirol }.ToHashSet(), await GroupMembership.GroupIdsOfAsync(_db, coach));

        var inTirol = await MembersAsync(tirol);
        Assert.Equal(["coach", "kid"], inTirol.Select(m => m.Username));
        Assert.All(inTirol, m => Assert.False(m.Direct));
        Assert.Equal(["Jugend"], inTirol.Single(m => m.Username == "kid").Via);
        Assert.Equal(["Verein"], inTirol.Single(m => m.Username == "coach").Via);

        var inVerein = await MembersAsync(verein);
        Assert.True(inVerein.Single(m => m.Username == "coach").Direct);
        Assert.False(inVerein.Single(m => m.Username == "kid").Direct);

        var groups = Assert.IsType<List<GroupDto>>(Assert.IsType<OkObjectResult>(await _controller.GetGroups()).Value);
        Assert.Equal(2, groups.Single(g => g.Id == tirol).MemberCount);
        Assert.Equal(1, groups.Single(g => g.Id == jugend).MemberCount);
        Assert.Equal([jugend], groups.Single(g => g.Id == verein).IncludedGroupIds);
    }

    [Fact]
    public async Task DirectAndInherited_IsListedOnce_AsDirect_WithTheInheritingGroup()
    {
        var (jugend, verein, _, kid, _) = await SeedAsync();
        await _controller.AddMember(verein, kid);
        var kidRow = Assert.Single(await MembersAsync(verein), m => m.Username == "kid");
        Assert.True(kidRow.Direct);
        Assert.Equal(["Jugend"], kidRow.Via);
        Assert.Equal(2, await GroupMembership.CountAsync(_db, verein));
        Assert.True(jugend > 0);
    }

    [Fact]
    public async Task Cycles_Self_AndEveryone_AreRejected()
    {
        var (jugend, verein, tirol, _, _) = await SeedAsync();
        var cycle = await _controller.AddInclusion(jugend, tirol);   // Tirol ⊃ Verein ⊃ Jugend ⊃ Tirol
        Assert.IsType<BadRequestObjectResult>(cycle);
        Assert.Equal("group_inclusion_cycle", ApiErrorCodesTests.CodeOf(cycle));
        Assert.IsType<BadRequestObjectResult>(await _controller.AddInclusion(verein, verein));

        var everyone = new Group { Name = "Everyone", IsEveryone = true };
        _db.Groups.Add(everyone);
        await _db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await _controller.AddInclusion(everyone.Id, verein));
        Assert.IsType<BadRequestObjectResult>(await _controller.AddInclusion(verein, everyone.Id));
        Assert.IsType<NotFoundObjectResult>(await _controller.AddInclusion(verein, 99999));

        Assert.IsType<NoContentResult>(await _controller.AddInclusion(tirol, jugend));   // doppelt, aber kein Kreis
        Assert.IsType<NoContentResult>(await _controller.AddInclusion(tirol, jugend));   // idempotent
        Assert.Equal(3, await _db.GroupInclusions.CountAsync());
    }

    [Fact]
    public async Task RoleOfTheOuterGroup_ReachesMembersOfTheInnerOne_AndUnlinkingTakesItAway()
    {
        await RoleSeeder.SeedAsync(_db);
        var (_, verein, tirol, kid, _) = await SeedAsync();
        var admin = new RoleAdminService(_db);
        var role = await admin.CreateAsync(new CreateRoleDto { Key = "tirol", Name = "Tirol", Permissions = [Permissions.LeagueView] });
        await admin.SetGroupRolesAsync(tirol, new SetUserRolesDto { RoleIds = [role.Id] });
        var resolver = new PermissionResolver(_db, _cache);

        Assert.True((await resolver.GetAsync(kid)).Has(Permissions.LeagueView));
        Assert.Contains(kid, await PermissionResolver.UserIdsWithPermissionAsync(_db, Permissions.LeagueView));

        Assert.IsType<NoContentResult>(await _controller.RemoveInclusion(tirol, verein));
        Assert.False((await resolver.GetAsync(kid)).Has(Permissions.LeagueView));
        Assert.Empty(await _db.GroupClosures.Where(c => c.AncestorId == tirol).ToListAsync());
        Assert.IsType<NotFoundObjectResult>(await _controller.RemoveInclusion(tirol, verein));
    }

    [Fact]
    public async Task BookShares_AndClubs_FollowTheInclusion()
    {
        var (_, verein, _, kid, _) = await SeedAsync();
        var book = new Book { FileName = "taktik.pgn", DisplayName = "Taktik Verein", IsPublic = false, Source = new BookSource() };
        _db.Books.Add(book);
        var club = new LeagueClub { Name = "SK Verein", TeamPrefix = "Verein", AnonName = "Verein" };
        _db.LeagueClubs.Add(club);
        await _db.SaveChangesAsync();
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = verein });
        _db.LeagueClubMembers.Add(new LeagueClubMember { ClubId = club.Id, GroupId = verein });
        await _db.SaveChangesAsync();

        Assert.True(await BookAccess.ReadableBy(_db, kid, false).AnyAsync(b => b.Id == book.Id));
        Assert.True(await CourseAccess.CanAccessAsync(_db, kid, book.Id, false));
        Assert.Contains(club.Id, await LeagueClubResolver.ClubIdsOfAsync(_db, kid));
    }

    [Fact]
    public async Task DeletingTheMiddleGroup_CutsTheChain()
    {
        var (_, verein, tirol, kid, _) = await SeedAsync();
        Assert.IsType<NoContentResult>(await _controller.Delete(verein));
        Assert.DoesNotContain(tirol, await GroupMembership.GroupIdsOfAsync(_db, kid));
        Assert.Empty(await _db.GroupInclusions.ToListAsync());
        Assert.Empty(await _db.GroupClosures.ToListAsync());
    }
}
