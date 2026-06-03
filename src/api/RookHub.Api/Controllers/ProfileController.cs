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
    private readonly DiscordLinkService _discordLink;

    public ProfileController(ProfileService profileService, PlayerSearchService playerSearchService, DiscordLinkService discordLink)
    {
        _profileService = profileService;
        _playerSearchService = playerSearchService;
        _discordLink = discordLink;
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

    /// <summary>
    /// Verknüpft das Discord-Konto anhand eines vom schach-bot signierten Tokens (`?dl=`-Param).
    /// 400 bei ungültigem/abgelaufenem Token oder deaktiviertem Feature, 409 bei Kollision.
    /// </summary>
    [HttpPost("discord/link")]
    public async Task<ActionResult<ProfileDto>> LinkDiscord([FromBody] LinkDiscordDto dto)
    {
        var identity = _discordLink.Verify(dto.Token);
        if (identity == null)
            return BadRequest(new { message = "Invalid or expired Discord link token." });

        try
        {
            return Ok(await _profileService.LinkDiscordAsync(GetUserId(), identity.Id, identity.Username));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("discord")]
    public async Task<ActionResult<ProfileDto>> UnlinkDiscord()
    {
        try
        {
            return Ok(await _profileService.UnlinkDiscordAsync(GetUserId()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Löscht den eigenen Account (DSGVO): Identität/PII werden anonymisiert, die Solve-Statistik
    /// bleibt anonym erhalten. Verlangt das aktuelle Passwort zur Bestätigung (401 bei falschem).
    /// </summary>
    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountDto dto)
    {
        try
        {
            await _profileService.DeleteAccountAsync(GetUserId(), dto?.Password ?? string.Empty);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Password is incorrect." });
        }
    }

    [HttpGet("{username}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicProfileDto>> GetPublicProfile(string username)
    {
        try
        {
            return Ok(await _profileService.GetPublicProfileByUsernameAsync(username));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
