using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class GroupControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GroupController _controller;

    public GroupControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new GroupController(_db, new TrainingGoalService(_db));
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync(string username)
    {
        var u = new AppUser { Username = username, Email = $"{username}@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private async Task<GroupDto> CreateGroupAsync(string name)
    {
        var r = await _controller.Create(new CreateGroupDto { Name = name }) as OkObjectResult;
        return Assert.IsType<GroupDto>(r!.Value);
    }

    [Fact]
    public async Task Create_And_List()
    {
        var dto = await CreateGroupAsync("A-Team");
        Assert.Equal("A-Team", dto.Name);

        var list = (await _controller.GetGroups() as OkObjectResult)!.Value as List<GroupDto>;
        Assert.Single(list!);
        Assert.Equal(0, list![0].MemberCount);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsBadRequest()
    {
        await CreateGroupAsync("Dup");
        var r = await _controller.Create(new CreateGroupDto { Name = "Dup" });
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsBadRequest()
    {
        var r = await _controller.Create(new CreateGroupDto { Name = "  " });
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task AddRemoveMember_IsIdempotent_AndCounted()
    {
        var g = await CreateGroupAsync("G");
        var u = await CreateUserAsync("alice");

        Assert.IsType<NoContentResult>(await _controller.AddMember(g.Id, u.Id));
        Assert.IsType<NoContentResult>(await _controller.AddMember(g.Id, u.Id)); // idempotent
        Assert.Equal(1, await _db.UserGroups.CountAsync());

        var members = ((await _controller.GetMembers(g.Id) as OkObjectResult)!.Value as List<GroupMemberDto>)!;
        Assert.Single(members);
        Assert.Equal("alice", members[0].Username);

        var groups = (await _controller.GetGroups() as OkObjectResult)!.Value as List<GroupDto>;
        Assert.Equal(1, groups![0].MemberCount);

        Assert.IsType<NoContentResult>(await _controller.RemoveMember(g.Id, u.Id));
        Assert.Equal(0, await _db.UserGroups.CountAsync());
    }

    [Fact]
    public async Task AddMember_UnknownUserOrGroup_NotFound()
    {
        var g = await CreateGroupAsync("G");
        Assert.IsType<NotFoundObjectResult>(await _controller.AddMember(g.Id, 9999));
        Assert.IsType<NotFoundObjectResult>(await _controller.AddMember(9999, 1));
    }

    [Fact]
    public async Task Delete_RemovesGroupAndMemberships()
    {
        var g = await CreateGroupAsync("G");
        var u = await CreateUserAsync("bob");
        await _controller.AddMember(g.Id, u.Id);

        Assert.IsType<NoContentResult>(await _controller.Delete(g.Id));
        Assert.Equal(0, await _db.Groups.CountAsync());
        Assert.Equal(0, await _db.UserGroups.CountAsync());
    }

    private async Task<Group> CreateEveryoneAsync()
    {
        var g = new Group { Name = "Everyone", IsEveryone = true, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Everyone_MemberCountIsTotalUsers_AndListedFirst()
    {
        await CreateUserAsync("alice");
        await CreateUserAsync("bob");
        await CreateEveryoneAsync();
        await CreateGroupAsync("Z-Team");

        var list = (await _controller.GetGroups() as OkObjectResult)!.Value as List<GroupDto>;
        Assert.True(list![0].IsEveryone);           // Everyone zuerst
        Assert.Equal(2, list[0].MemberCount);        // = alle Nutzer, obwohl keine UserGroups-Zeilen
    }

    [Fact]
    public async Task Everyone_GetMembers_ReturnsAllUsers_WithoutRows()
    {
        await CreateUserAsync("alice");
        await CreateUserAsync("bob");
        var e = await CreateEveryoneAsync();

        var members = (await _controller.GetMembers(e.Id) as OkObjectResult)!.Value as List<GroupMemberDto>;
        Assert.Equal(2, members!.Count);
        Assert.Equal(0, await _db.UserGroups.CountAsync());
    }

    [Fact]
    public async Task Everyone_CannotBeModifiedOrDeletedOrMemberEdited()
    {
        var u = await CreateUserAsync("alice");
        var e = await CreateEveryoneAsync();

        Assert.IsType<BadRequestObjectResult>(await _controller.Update(e.Id, new UpdateGroupDto { Name = "Nope" }));
        Assert.IsType<BadRequestObjectResult>(await _controller.Delete(e.Id));
        Assert.IsType<BadRequestObjectResult>(await _controller.AddMember(e.Id, u.Id));
        Assert.IsType<BadRequestObjectResult>(await _controller.RemoveMember(e.Id, u.Id));
        Assert.Equal(0, await _db.UserGroups.CountAsync());
    }
}
