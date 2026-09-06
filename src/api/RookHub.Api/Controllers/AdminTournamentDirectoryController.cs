using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Wartung des Turnierverzeichnisses: Sweep von Hand ausloesen, Gazetteer importieren,
/// Geocoding-Qualitaet ansehen und einzelne Koordinaten korrigieren.
///
/// Gesamter Controller hinter <see cref="Permissions.TournamentsManage"/> - ein blosser
/// IsAdmin-Check im Methodenrumpf haette dieselbe Wirkung, aber die Berechtigung waere nicht
/// zuteilbar und der Endpunkt taeuchte in keiner Rechteuebersicht auf.
/// </summary>
[ApiController]
[Route("api/admin/tournament-directory")]
[Authorize]
[HasPermission(Permissions.TournamentsManage)]
public class AdminTournamentDirectoryController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly TournamentDirectoryService _directory;
    private readonly GazetteerImportService _gazetteer;
    private readonly GeocodingService _geocoding;

    public AdminTournamentDirectoryController(
        AppDbContext db,
        TournamentDirectoryService directory,
        GazetteerImportService gazetteer,
        GeocodingService geocoding)
    {
        _db = db;
        _directory = directory;
        _gazetteer = gazetteer;
        _geocoding = geocoding;
    }

    /// <summary>Zustand je Foederation plus die Geocoding-Quote - der Gesundheitsblick.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var sweeps = await _db.TournamentDirectorySweeps.AsNoTracking()
            .OrderByDescending(s => s.LastAttemptedAt)
            .ToListAsync(ct);

        var entries = _db.TournamentDirectoryEntries.AsNoTracking().Where(e => e.RemovedAt == null);
        var total = await entries.CountAsync(ct);
        var bySource = await entries
            .GroupBy(e => e.GeoSource)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Ok(new
        {
            totalEntries = total,
            geocoded = bySource.Where(b => b.Source != GeoSource.None).Sum(b => b.Count),
            byGeoSource = bySource.ToDictionary(b => b.Source.ToString(), b => b.Count),
            gazetteerPlaces = await _db.GeoPlaces.CountAsync(ct),
            sweeps = sweeps.Select(s => new
            {
                s.Federation, s.LastSweptAt, s.LastAttemptedAt, s.LastRowCount,
                s.LastError, s.ConsecutiveFailures
            }),
        });
    }

    /// <summary>
    /// Sweep fuer die angegebenen Foederationen sofort ausfuehren. Laeuft im Anfrageweg (der
    /// Aufrufer soll das Ergebnis sehen) - bei vielen Foederationen dauert das entsprechend,
    /// weil der Crawler seine Anfragen ohnehin serialisiert.
    /// </summary>
    [HttpPost("sweep")]
    public async Task<IActionResult> Sweep([FromBody] SweepRequest request, CancellationToken ct)
    {
        var federations = (request.Federations ?? [])
            .Select(f => f.Trim().ToUpperInvariant())
            .Where(f => f.Length == 3 && f.All(char.IsAsciiLetterUpper))
            .Distinct()
            .Take(20)
            .ToList();

        if (federations.Count == 0)
            return BadRequest(new { message = "Provide 1-20 three-letter federation codes." });

        var results = await _directory.RunSweepAsync(federations, ct);
        return Ok(results);
    }

    /// <summary>Postleitzahlen eines Landes (ISO-3166-1-alpha-2) aus dem GeoNames-Export laden.</summary>
    [HttpPost("gazetteer/postal/{iso2}")]
    public async Task<IActionResult> ImportPostal(string iso2, CancellationToken ct)
    {
        var result = await _gazetteer.ImportPostalCodesAsync(iso2, ct);
        return result.Error is null ? Ok(result) : StatusCode(502, result);
    }

    /// <summary>Weltweite Ortsliste (cities15000) laden - Grundlage fuer Laender ohne PLZ-Datensatz.</summary>
    [HttpPost("gazetteer/cities")]
    public async Task<IActionResult> ImportCities(CancellationToken ct)
    {
        var result = await _gazetteer.ImportCitiesAsync(ct);
        return result.Error is null ? Ok(result) : StatusCode(502, result);
    }

    /// <summary>Die Eintraege, die der Gazetteer nicht verorten konnte - Arbeitsliste fuer Korrekturen.</summary>
    [HttpGet("ungeocoded")]
    public async Task<IActionResult> Ungeocoded([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var entries = await _db.TournamentDirectoryEntries.AsNoTracking()
            .Where(e => e.RemovedAt == null && e.Lat == null)
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(e => new { e.ChessResultsId, e.Name, e.Federation, e.State, e.LocationText, e.StartDate })
            .ToListAsync(ct);

        return Ok(entries);
    }

    /// <summary>
    /// Koordinaten von Hand setzen. Die Quelle wird auf <see cref="GeoSource.Manual"/> gesetzt -
    /// damit ueberschreibt der naechtliche Sweep die Korrektur nicht wieder.
    /// </summary>
    [HttpPut("{chessResultsId}/coordinates")]
    public async Task<IActionResult> SetCoordinates(
        string chessResultsId, [FromBody] CoordinateInput input, CancellationToken ct)
    {
        if (input.Lat is < -90 or > 90 || input.Lon is < -180 or > 180)
            return BadRequest(new { message = "Coordinates out of range." });

        var entry = await _db.TournamentDirectoryEntries
            .FirstOrDefaultAsync(e => e.ChessResultsId == chessResultsId, ct);
        if (entry is null) return NotFound();

        entry.Lat = input.Lat;
        entry.Lon = input.Lon;
        entry.GeoSource = GeoSource.Manual;
        // Gekappt: die Spalte ist varchar(200), und eine zu lange Eingabe waere sonst ein 500er
        // statt einer gespeicherten Korrektur.
        var placeName = input.PlaceName?.Trim();
        entry.GeoPlaceName = placeName is { Length: > 200 } ? placeName[..200] : placeName;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { entry.ChessResultsId, entry.Lat, entry.Lon, GeoSource = entry.GeoSource.ToString() });
    }

    /// <summary>
    /// Alle noch nicht verorteten Eintraege erneut durch den Gazetteer schicken - nach einem
    /// frischen Import der eigentliche Nutzen: die Zeilen von gestern bekommen ihre Pins.
    /// </summary>
    [HttpPost("geocode-missing")]
    public async Task<IActionResult> GeocodeMissing([FromQuery] int limit = 1000, CancellationToken ct = default)
    {
        var entries = await _db.TournamentDirectoryEntries
            .Where(e => e.RemovedAt == null && e.Lat == null && e.GeoSource != GeoSource.Manual)
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 10000))
            .ToListAsync(ct);

        var resolved = 0;
        foreach (var entry in entries)
        {
            var result = await _geocoding.ResolveAsync(entry.LocationText, entry.State, entry.Federation, ct);
            if (result is null) continue;

            entry.Lat = result.Lat;
            entry.Lon = result.Lon;
            entry.GeoSource = result.Source;
            entry.GeoPlaceName = result.PlaceName;
            entry.UpdatedAt = DateTime.UtcNow;
            resolved++;
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { examined = entries.Count, resolved });
    }

    public class SweepRequest
    {
        public List<string>? Federations { get; set; }
    }

    public class CoordinateInput
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? PlaceName { get; set; }
    }
}
