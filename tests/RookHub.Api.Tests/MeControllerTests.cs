using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

/// <summary>MeController.MyGroups: eigene Gruppen + implizite „Everyone"-Gruppe, ordinal sortiert,
/// fremde Gruppen ausgeschlossen.</summary>
public class MeControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    public MeControllerTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
    }
    public void Dispose() => _db.Dispose();

    private MeController ControllerFor(int userId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"));
        return new MeController(_db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private async Task Seed()
    {
        _db.AppUsers.AddRange(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" },
                              new AppUser { Id = 2, Username = "u2", PasswordHash = "x" });
        _db.Groups.AddRange(
            new Group { Id = 10, Name = "Trainer" },
            new Group { Id = 11, Name = "A-Team" },
            new Group { Id = 12, Name = "Other" },
            new Group { Id = 99, Name = "Everyone", IsEveryone = true });
        _db.UserGroups.AddRange(
            new UserGroup { UserId = 1, GroupId = 10 },
            new UserGroup { UserId = 1, GroupId = 11 },
            new UserGroup { UserId = 2, GroupId = 12 });   // fremde Gruppe
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task MyGroups_ReturnsOwnGroupsPlusEveryone_OrdinalSorted_ExcludesOthers()
    {
        await Seed();
        var res = Assert.IsType<OkObjectResult>(await ControllerFor(1).MyGroups());
        var groups = Assert.IsAssignableFrom<List<string>>(res.Value);
        Assert.Equal(new[] { "A-Team", "Everyone", "Trainer" }, groups);   // ordinal
        Assert.DoesNotContain("Other", groups);
    }

    [Fact]
    public async Task MyGroups_DoesNotDuplicateEveryone_WhenAlreadyMember()
    {
        await Seed();
        _db.UserGroups.Add(new UserGroup { UserId = 1, GroupId = 99 }); // explizit in Everyone
        await _db.SaveChangesAsync();
        var res = Assert.IsType<OkObjectResult>(await ControllerFor(1).MyGroups());
        var groups = Assert.IsAssignableFrom<List<string>>(res.Value);
        Assert.Single(groups, g => g == "Everyone");
    }

    [Fact]
    public async Task MyGroups_NoEveryoneGroupConfigured_ReturnsOnlyOwnGroups()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        _db.Groups.Add(new Group { Id = 10, Name = "Trainer" });
        _db.UserGroups.Add(new UserGroup { UserId = 1, GroupId = 10 });
        await _db.SaveChangesAsync();
        var res = Assert.IsType<OkObjectResult>(await ControllerFor(1).MyGroups());
        var groups = Assert.IsAssignableFrom<List<string>>(res.Value);
        Assert.Equal(new[] { "Trainer" }, groups);
    }
}
