using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace RookHub.Api.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (claim is null || !int.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("User ID claim is missing or invalid.");
        return userId;
    }
}
