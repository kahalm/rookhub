using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

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
        _controller = new GroupController(_db);
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
}
