using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Lesezugriff aufs Turnierverzeichnis: Liste, Karte, Kalender, Detail. Gefuellt wird es vom
/// naechtlichen Sweep (<see cref="TournamentDirectoryService"/>); hier wird nichts gecrawlt.
/// </summary>
[ApiController]
[Route("api/tournament-directory")]
[Authorize]
public class TournamentDirectoryController : BaseApiController
{
    private const int MaxWindowDays = 3 * 366;

    private static readonly Regex FederationPattern = new(@"^[A-Za-z]{3}$", RegexOptions.Compiled);

    private readonly TournamentDirectoryQueryService _query;
    private readonly AppDbContext _db;

    public TournamentDirectoryController(TournamentDirectoryQueryService query, AppDbContext db)
    {
        _query = query;
        _db = db;
    }

    /// <summary>
    /// Turnierliste mit Zeitraum-, Umkreis- und Textfilter. Ohne <c>profileId</c> gelten die
    /// einzelnen Query-Parameter; mit <c>profileId</c> werden Mittelpunkt, Radius und Filter aus dem
    /// gespeicherten Suchprofil des Nutzers uebernommen (explizite Parameter stechen sie nicht aus -
    /// ein Profil soll reproduzierbar dasselbe liefern wie die naechtliche Benachrichtigung).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DirectoryPageDto>> Search(
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] double? lat = null, [FromQuery] double? lon = null, [FromQuery] int? radiusKm = null,
        [FromQuery] string? fed = null, [FromQuery] string? speed = null, [FromQuery] string? q = null,
        [FromQuery] bool weekendOnly = false, [FromQuery] int? minPlayers = null,
        [FromQuery] int? profileId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var parsed = await BuildQueryAsync(from, to, lat, lon, radiusKm, fed, speed, q,
            weekendOnly, minPlayers, profileId, page, pageSize, ct);
        if (parsed.Error is not null) return BadRequest(new { message = parsed.Error });

        var result = await _query.SearchAsync(parsed.Query!, ct);
        var subscribed = await SubscribedIdsAsync(result.Items.Select(i => i.Entry.ChessResultsId), ct);

        return Ok(new DirectoryPageDto
        {
            Items = result.Items
                .Select(i => DirectoryEntryDto.FromEntity(i.Entry, i.DistanceKm,
                    subscribed.Contains(i.Entry.ChessResultsId)))
                .ToList(),
            Total = result.Total,
            Truncated = result.Truncated,
        });
    }

    /// <summary>
    /// Kartenmarker fuer den sichtbaren Ausschnitt. <c>bbox</c> ist "minLat,minLon,maxLat,maxLon".
    /// </summary>
    [HttpGet("map")]
    public async Task<ActionResult<List<DirectoryEntryDto>>> Map(
        [FromQuery] string bbox,
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] string? fed = null, [FromQuery] string? speed = null, [FromQuery] string? q = null,
        [FromQuery] bool weekendOnly = false, [FromQuery] int? minPlayers = null,
        [FromQuery] int? profileId = null, [FromQuery] int limit = 2000,
        CancellationToken ct = default)
    {
        if (!TryParseBoundingBox(bbox, out var box))
            return BadRequest(new { message = "bbox must be minLat,minLon,maxLat,maxLon." });

        var parsed = await BuildQueryAsync(from, to, null, null, null, fed, speed, q,
            weekendOnly, minPlayers, profileId, 1, 1, ct);
        if (parsed.Error is not null) return BadRequest(new { message = parsed.Error });

        var pins = await _query.MapPinsAsync(parsed.Query!, box.MinLat, box.MaxLat, box.MinLon, box.MaxLon, limit, ct);
        return Ok(pins.Select(p => DirectoryEntryDto.FromEntity(p)).ToList());
    }

    /// <summary>
    /// Kalendermonat: je Tag die an diesem Tag LAUFENDEN Turniere. Ein mehrtaegiges Open steht
    /// deshalb an jedem seiner Tage - genau so, wie ein Kalender es zeigen soll.
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<List<DirectoryCalendarDayDto>>> Calendar(
        [FromQuery] int year, [FromQuery] int month,
        [FromQuery] double? lat = null, [FromQuery] double? lon = null, [FromQuery] int? radiusKm = null,
        [FromQuery] string? fed = null, [FromQuery] string? speed = null, [FromQuery] string? q = null,
        [FromQuery] bool weekendOnly = false, [FromQuery] int? minPlayers = null,
        [FromQuery] int? profileId = null,
        CancellationToken ct = default)
    {
        if (year is < 1990 or > 2100 || month is < 1 or > 12)
            return BadRequest(new { message = "year/month out of range." });

        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var parsed = await BuildQueryAsync(first.ToString("yyyy-MM-dd"), last.ToString("yyyy-MM-dd"),
            lat, lon, radiusKm, fed, speed, q, weekendOnly, minPlayers, profileId, 1, 200, ct);
        if (parsed.Error is not null) return BadRequest(new { message = parsed.Error });

        // Der Monat ist die Obergrenze: mehr als 200 gleichzeitig laufende Turniere im selben
        // Umkreis gibt es nicht, und eine Kalenderzelle zeigt ohnehin nur die ersten paar.
        var result = await _query.SearchAsync(parsed.Query! with { Page = 1, PageSize = 200 }, ct);
        var subscribed = await SubscribedIdsAsync(result.Items.Select(i => i.Entry.ChessResultsId), ct);

        var days = new List<DirectoryCalendarDayDto>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var items = result.Items
                .Where(i => Covers(i.Entry, day))
                .Select(i => DirectoryEntryDto.FromEntity(i.Entry, i.DistanceKm,
                    subscribed.Contains(i.Entry.ChessResultsId)))
                .ToList();
            days.Add(new DirectoryCalendarDayDto { Date = day, Items = items });
        }
        return Ok(days);
    }

    [HttpGet("{chessResultsId}")]
    public async Task<ActionResult<DirectoryEntryDto>> Get(string chessResultsId, CancellationToken ct)
    {
        if (!Regex.IsMatch(chessResultsId, @"^\d{1,10}$"))
            return BadRequest(new { message = "Invalid tournament id." });

        var entry = await _query.GetAsync(chessResultsId, ct);
        if (entry is null) return NotFound();

        var subscribed = await SubscribedIdsAsync([chessResultsId], ct);
        return Ok(DirectoryEntryDto.FromEntity(entry, null, subscribed.Contains(chessResultsId)));
    }

    /// <summary>Ortsvorschlaege (PLZ oder Name) fuer das Suchprofil-Formular.</summary>
    [HttpGet("places")]
    public async Task<ActionResult<List<GeoPlaceSuggestionDto>>> Places(
        [FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new List<GeoPlaceSuggestionDto>());

        var places = await _query.SuggestPlacesAsync(q, 10, ct);
        return Ok(places.Select(p => new GeoPlaceSuggestionDto
        {
            Label = p.PostalCode is null ? $"{p.Name} ({p.Country})" : $"{p.PostalCode} {p.Name} ({p.Country})",
            Country = p.Country,
            PostalCode = p.PostalCode,
            Lat = p.Lat,
            Lon = p.Lon,
        }).ToList());
    }

    // -----------------------------------------------------------------------

    private static bool Covers(TournamentDirectoryEntry entry, DateOnly day)
    {
        var start = entry.StartDate ?? entry.EndDate;
        var end = entry.EndDate ?? entry.StartDate;
        return start is not null && end is not null && day >= start && day <= end;
    }

    private async Task<HashSet<string>> SubscribedIdsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        var userId = GetUserId();
        var found = await _db.TournamentSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && list.Contains(s.CrawlerTournamentId))
            .Select(s => s.CrawlerTournamentId)
            .ToListAsync(ct);
        return found.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<(DirectorySearchQuery? Query, string? Error)> BuildQueryAsync(
        string? from, string? to, double? lat, double? lon, int? radiusKm,
        string? fed, string? speed, string? text, bool weekendOnly, int? minPlayers,
        int? profileId, int page, int pageSize, CancellationToken ct)
    {
        DateOnly? fromDate = null, toDate = null;
        if (from is not null && !TryParseIsoDate(from, out fromDate)) return (null, "from must be yyyy-MM-dd.");
        if (to is not null && !TryParseIsoDate(to, out toDate)) return (null, "to must be yyyy-MM-dd.");
        if (fromDate is { } f && toDate is { } t)
        {
            if (t < f) return (null, "to must not be before from.");
            if (t.DayNumber - f.DayNumber > MaxWindowDays) return (null, "Date window too large.");
        }

        if (fed is not null && !FederationPattern.IsMatch(fed.Trim()))
            return (null, "fed must be a 3-letter federation code.");

        TournamentSpeed? parsedSpeed = null;
        if (!string.IsNullOrWhiteSpace(speed))
        {
            if (!Enum.TryParse<TournamentSpeed>(speed.Trim(), ignoreCase: true, out var value))
                return (null, "speed must be one of standard, rapid, blitz, unknown.");
            parsedSpeed = value;
        }

        if (radiusKm is { } r && r is < 1 or > 2000) return (null, "radiusKm must be between 1 and 2000.");
        if (lat is { } la && la is < -90 or > 90) return (null, "lat out of range.");
        if (lon is { } lo && lo is < -180 or > 180) return (null, "lon out of range.");

        if (profileId is { } id)
        {
            var profile = await _db.TournamentSearchProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == GetUserId(), ct);
            if (profile is null) return (null, "Unknown search profile.");

            lat = profile.Lat;
            lon = profile.Lon;
            radiusKm = profile.RadiusKm;
            weekendOnly = profile.WeekendOnly;
            minPlayers = profile.MinPlayers;

            var profileFeds = TournamentDirectoryService.SplitCsv(profile.Federations);
            if (fed is null && profileFeds.Count == 1) fed = profileFeds[0];

            var profileSpeeds = TournamentDirectoryService.SplitCsv(profile.Speeds);
            if (parsedSpeed is null && profileSpeeds.Count == 1
                && Enum.TryParse<TournamentSpeed>(profileSpeeds[0], ignoreCase: true, out var single))
                parsedSpeed = single;
        }

        return (new DirectorySearchQuery
        {
            From = fromDate,
            To = toDate,
            Lat = lat,
            Lon = lon,
            RadiusKm = radiusKm,
            Federation = fed,
            Speed = parsedSpeed,
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            WeekendOnly = weekendOnly,
            MinPlayers = minPlayers,
            Page = page,
            PageSize = pageSize,
        }, null);
    }

    internal static bool TryParseBoundingBox(string? bbox,
        out (double MinLat, double MinLon, double MaxLat, double MaxLon) box)
    {
        box = default;
        var parts = (bbox ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;

        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }
        if (values[0] is < -90 or > 90 || values[2] is < -90 or > 90) return false;
        if (values[1] is < -180 or > 180 || values[3] is < -180 or > 180) return false;
        if (values[2] < values[0]) return false;

        box = (values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool TryParseIsoDate(string? text, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)) return false;
        date = parsed;
        return true;
    }
}
