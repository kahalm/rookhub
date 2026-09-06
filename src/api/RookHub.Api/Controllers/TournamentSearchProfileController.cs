using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Gespeicherte Umkreise eines Nutzers ("Zuhause 100 km", "Ferienhaus Kaernten 50 km"). Sie
/// steuern sowohl die Ansicht als auch die naechtliche Benachrichtigung - deshalb liegen sie
/// serverseitig und nicht im Browser.
/// </summary>
[ApiController]
[Route("api/tournament-search-profiles")]
[Authorize]
public class TournamentSearchProfileController : BaseApiController
{
    /// <summary>Bremse gegen versehentliche Profil-Halden (jedes Profil kostet Sweep-Arbeit).</summary>
    private const int MaxProfilesPerUser = 20;

    private static readonly string[] AllowedSpeeds =
        [nameof(TournamentSpeed.Standard), nameof(TournamentSpeed.Rapid), nameof(TournamentSpeed.Blitz)];

    private readonly AppDbContext _db;
    private readonly ILogger<TournamentSearchProfileController> _log;

    public TournamentSearchProfileController(AppDbContext db, ILogger<TournamentSearchProfileController> log)
    {
        _db = db;
        _log = log;
    }

    [HttpGet]
    public async Task<ActionResult<List<DirectorySearchProfileDto>>> GetAll(CancellationToken ct)
    {
        var profiles = await _db.TournamentSearchProfiles.AsNoTracking()
            .Where(p => p.UserId == GetUserId())
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .ToListAsync(ct);

        return Ok(profiles.Select(DirectorySearchProfileDto.FromEntity).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<DirectorySearchProfileDto>> Create(
        [FromBody] SearchProfileInputDto input, CancellationToken ct)
    {
        var error = Validate(input);
        if (error is not null) return BadRequest(new { message = error });

        var userId = GetUserId();
        if (await _db.TournamentSearchProfiles.CountAsync(p => p.UserId == userId, ct) >= MaxProfilesPerUser)
            return BadRequest(new { message = $"At most {MaxProfilesPerUser} search profiles per user." });

        var profile = new TournamentSearchProfile { UserId = userId };
        Apply(input, profile);
        _db.TournamentSearchProfiles.Add(profile);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            return Conflict(new { message = "A search profile with this name already exists." });
        }

        return Ok(DirectorySearchProfileDto.FromEntity(profile));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DirectorySearchProfileDto>> Update(
        int id, [FromBody] SearchProfileInputDto input, CancellationToken ct)
    {
        var error = Validate(input);
        if (error is not null) return BadRequest(new { message = error });

        var profile = await _db.TournamentSearchProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == GetUserId(), ct);
        if (profile is null) return NotFound();

        Apply(input, profile);
        profile.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            return Conflict(new { message = "A search profile with this name already exists." });
        }

        return Ok(DirectorySearchProfileDto.FromEntity(profile));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        // Fremde Id -> 404 statt 403: die Existenz eines fremden Profils geht niemanden an.
        var profile = await _db.TournamentSearchProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == GetUserId(), ct);
        if (profile is null) return NotFound();

        _db.TournamentSearchProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // -----------------------------------------------------------------------

    private static string? Validate(SearchProfileInputDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "Name is required.";
        if (input.Lat is < -90 or > 90) return "lat out of range.";
        if (input.Lon is < -180 or > 180) return "lon out of range.";
        if (input.RadiusKm is < 1 or > 2000) return "radiusKm must be between 1 and 2000.";

        foreach (var speed in input.Speeds ?? [])
        {
            if (!AllowedSpeeds.Contains(speed, StringComparer.OrdinalIgnoreCase))
                return $"Unknown speed '{speed}'.";
        }
        foreach (var fed in input.Federations ?? [])
        {
            if (fed.Length != 3 || !fed.All(char.IsAsciiLetter))
                return $"Unknown federation '{fed}'.";
        }
        return null;
    }

    private static void Apply(SearchProfileInputDto input, TournamentSearchProfile profile)
    {
        profile.Name = input.Name.Trim();
        profile.PlaceQuery = input.PlaceQuery?.Trim();
        profile.Lat = input.Lat;
        profile.Lon = input.Lon;
        profile.RadiusKm = input.RadiusKm;
        profile.Federations = Join(input.Federations?.Select(f => f.ToUpperInvariant()));
        profile.Speeds = Join(input.Speeds);
        profile.WeekendOnly = input.WeekendOnly;
        profile.MinPlayers = input.MinPlayers is > 0 ? input.MinPlayers : null;
        profile.NotifyNew = input.NotifyNew;
        profile.SortOrder = input.SortOrder;
    }

    private static string? Join(IEnumerable<string>? values)
    {
        var list = (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct().ToList();
        return list.Count == 0 ? null : string.Join(',', list);
    }
}
