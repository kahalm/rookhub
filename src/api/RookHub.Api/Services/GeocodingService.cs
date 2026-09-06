using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public sealed record GeocodeResult(double Lat, double Lon, GeoSource Source, string PlaceName);

/// <summary>
/// Loest den Freitext-Spielort eines Verzeichniseintrags in Koordinaten auf - gegen den lokalen
/// GeoNames-Gazetteer, nicht gegen einen Web-Dienst: Nominatims Nutzungsbedingungen verbieten
/// Massen-Geocoding, und ein naechtlicher Sweep ist genau das.
///
/// Reihenfolge, absteigend nach Genauigkeit:
///   1. Postleitzahl aus dem Text (trifft den Ort auf wenige Kilometer)
///   2. Ortsname aus dem Text, laengste Wortfolge zuerst, bei Mehrdeutigkeit der groesste Ort
///   3. Bundesland-Zentroid aus der State-Spalte (grob, aber besser als kein Pin)
/// Nichts davon getroffen -> null; der Eintrag bleibt ohne Koordinaten und taucht in keiner
/// Umkreissuche auf, statt irgendwo im Nirgendwo einen Pin zu setzen.
/// </summary>
public class GeocodingService
{
    private readonly AppDbContext _db;

    public GeocodingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<GeocodeResult?> ResolveAsync(
        string? locationText, string? state, string? federation, CancellationToken ct = default)
    {
        var iso2 = FideCountryCodes.ToIso2(federation);

        return await ResolveByPostalCodeAsync(locationText, iso2, ct)
            ?? await ResolveByPlaceNameAsync(locationText, iso2, ct)
            ?? await ResolveByRegionAsync(state, iso2, ct);
    }

    private async Task<GeocodeResult?> ResolveByPostalCodeAsync(string? locationText, string? iso2, CancellationToken ct)
    {
        if (iso2 is null) return null;   // ohne Land ist eine nackte Ziffernfolge wertlos

        var candidates = GeoTextNormalizer.PostalCandidates(locationText);
        if (candidates.Count == 0) return null;

        var matches = await _db.GeoPlaces.AsNoTracking()
            .Where(g => g.Country == iso2 && g.PostalCode != null && candidates.Contains(g.PostalCode))
            .ToListAsync(ct);
        if (matches.Count == 0) return null;

        // Steht der Ortsname des Treffers auch im Text ("5400 Hallein"), ist die Zuordnung sicher -
        // sonst koennte eine Hausnummer zufaellig wie eine PLZ aussehen. Die spaeteste passende
        // Ziffernfolge gewinnt, weil in Adressen die Hausnummer vor der PLZ steht.
        var normalizedText = GeoTextNormalizer.Normalize(locationText);
        var confirmed = matches.FirstOrDefault(m => normalizedText.Contains(GeoTextNormalizer.Normalize(m.Name)));
        var chosen = confirmed ?? matches
            .OrderByDescending(m => candidates.IndexOf(m.PostalCode!))
            .First();

        return new GeocodeResult(chosen.Lat, chosen.Lon, GeoSource.PostalCode, chosen.Name);
    }

    private async Task<GeocodeResult?> ResolveByPlaceNameAsync(string? locationText, string? iso2, CancellationToken ct)
    {
        var candidates = GeoTextNormalizer.PlaceCandidates(locationText);
        if (candidates.Count == 0) return null;

        var query = _db.GeoPlaces.AsNoTracking().Where(g => candidates.Contains(g.NameNormalized));
        if (iso2 is not null) query = query.Where(g => g.Country == iso2);

        var matches = await query.ToListAsync(ct);
        if (matches.Count == 0) return null;

        // PlaceCandidates liefert die laengsten Wortfolgen zuerst: "bad ischl" muss "ischl"
        // schlagen, sonst landet der Pin im falschen Ort. Innerhalb derselben Laenge entscheidet
        // die Einwohnerzahl (Wien vor einem gleichnamigen Weiler).
        var best = matches
            .OrderBy(m => candidates.IndexOf(m.NameNormalized))
            .ThenByDescending(m => m.Population)
            .First();

        return new GeocodeResult(best.Lat, best.Lon, GeoSource.City, best.Name);
    }

    private async Task<GeocodeResult?> ResolveByRegionAsync(string? state, string? iso2, CancellationToken ct)
    {
        if (iso2 is null || string.IsNullOrWhiteSpace(state) || state.Trim() == "-") return null;

        var normalized = GeoTextNormalizer.Normalize(state);
        if (normalized.Length < 3) return null;

        var region = await _db.GeoPlaces.AsNoTracking()
            .Where(g => g.Country == iso2 && g.Kind == GeoPlaceKind.Region && g.NameNormalized == normalized)
            .FirstOrDefaultAsync(ct);

        return region is null ? null : new GeocodeResult(region.Lat, region.Lon, GeoSource.Region, region.Name);
    }
}
