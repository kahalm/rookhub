using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;

namespace RookHub.Api.Tests;

/// <summary>CatalogController: die Owner-Endpoints (grants/requests/approve/decline) sind admin-only —
/// Nicht-Admins bekommen Forbid, OHNE dass der Service angefasst wird (das ternäre `IsAdmin ? … : Forbid()`
/// kurzschließt). Genau diese Sicherheits-Gate-Branch war unabgedeckt; die CatalogService-Logik + die
/// 404-Mappings sind separat abgedeckt. Da der Service im Forbid-Pfad nie aufgerufen wird, genügt null.</summary>
public class CatalogControllerTests
{
    private static CatalogController NonAdmin()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "test"));
        return new CatalogController(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    [Fact]
    public async Task GetGrants_ForbidsNonAdmin()
        => Assert.IsType<ForbidResult>((await NonAdmin().GetGrants()).Result);

    [Fact]
    public async Task SetGrants_ForbidsNonAdmin()
        => Assert.IsType<ForbidResult>((await NonAdmin().SetGrants(new CatalogGrantsDto { UserIds = new(), GroupIds = new() })).Result);

    [Fact]
    public async Task GetRequests_ForbidsNonAdmin()
        => Assert.IsType<ForbidResult>((await NonAdmin().GetRequests()).Result);

    [Fact]
    public async Task Approve_ForbidsNonAdmin()
        => Assert.IsType<ForbidResult>(await NonAdmin().Approve(1));

    [Fact]
    public async Task Decline_ForbidsNonAdmin()
        => Assert.IsType<ForbidResult>(await NonAdmin().Decline(1));
}
