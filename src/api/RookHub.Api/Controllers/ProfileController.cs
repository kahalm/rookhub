using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly ProfileService _profileService;

    public ProfileController(ProfileService profileService) => _profileService = profileService;

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<ProfileDto>> GetMyProfile()
    {
        try
        {
            return Ok(await _profileService.GetProfileAsync(GetUserId()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        try
        {
            return Ok(await _profileService.UpdateProfileAsync(GetUserId(), dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{username}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProfileDto>> GetPublicProfile(string username)
    {
        try
        {
            return Ok(await _profileService.GetProfileByUsernameAsync(username));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
