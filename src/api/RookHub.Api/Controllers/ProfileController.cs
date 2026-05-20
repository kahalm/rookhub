using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : BaseApiController
{
    private readonly ProfileService _profileService;
    private readonly PlayerSearchService _playerSearchService;

    public ProfileController(ProfileService profileService, PlayerSearchService playerSearchService)
    {
        _profileService = profileService;
        _playerSearchService = playerSearchService;
    }

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

    [HttpGet("player-search")]
    public async Task<ActionResult<PlayerSearchResultDto>> SearchPlayers(
        [FromQuery] string lastName, [FromQuery] string? firstName)
    {
        if (string.IsNullOrWhiteSpace(lastName) || lastName.Trim().Length < 2)
            return BadRequest(new { message = "lastName must be at least 2 characters." });

        return Ok(await _playerSearchService.SearchAsync(lastName.Trim(), firstName?.Trim()));
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
