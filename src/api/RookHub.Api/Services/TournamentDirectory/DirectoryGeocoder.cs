using RookHub.Api.Data;
using RookHub.Api.Models;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Verortet einen Eintrag der chess-results-Turniersuche ueber das Ortslexikon — Haupt-Spielort am
/// Eintrag, alle weiteren in <see cref="TournamentDirectoryEntry.Venues"/>.
///
/// <para>Herausgezogen aus <see cref="TournamentDirectoryService"/> (Codereview 2026-09-29, Z8).</para>
/// </summary>
public sealed class DirectoryGeocoder
{
    private readonly AppDbContext _db;
    private readonly GeocodingService _geocoding;

    public DirectoryGeocoder(AppDbContext db, GeocodingService geocoding)
    {
        _db = db;
        _geocoding = geocoding;
    }

    /// <summary>
    /// Verortet einen Eintrag — mit ALLEN Spielorten, die im Ortstext stehen.
    ///
    /// <para>Die Koordinaten am Eintrag bleiben der HAUPT-Spielort (der erste): Kalender, Detail
    /// und der Bounding-Box-Index haengen daran. Die vollstaendige Liste steht in
    /// <see cref="TournamentDirectoryEntry.Venues"/>, und die Umkreissuche fragt sie — sonst faende
    /// sie ein Liga-Turnier nicht, das zur Haelfte vor der Haustuer stattfindet.</para>
    /// </summary>
    public async Task GeocodeAsync(TournamentDirectoryEntry entry, CancellationToken ct)
    {
        // Eine von Hand gesetzte Koordinate nie ueberschreiben - sie ist die Korrektur eines
        // Fehlgriffs und wuerde sonst jede Nacht zurueckfallen. Ebenso eine von der QUELLE
        // mitgelieferte: sie ist genauer als alles, was das Ortslexikon daraus machen kann.
        // Dieselbe Schutzliste wie `TournamentDirectoryMaintenanceService.GeocodeMissingAsync`.
        if (entry.GeoSource is GeoSource.Manual or GeoSource.SourceProvided) return;

        // Ein ueber die VEREINSNAMEN bewiesener Pin (`VenueDisambiguationService`) wird hier
        // trotzdem neu verortet — der Ortstext hat sich geaendert, der Beweis galt dem alten. Aber
        // die Vereinsnamen-Aufloesung muss danach WIEDER laufen duerfen: `TeamHintCheckedAt` haelt
        // sie sonst fuer immer fern, und ein „St.Veit" -> „St.Veit, Mehrzweckhalle", das das
        // Ortslexikon wieder mehrdeutig findet, kostete den Pin endgueltig (Karte, Umkreissuche).
        // Ebenso ein Beweis an einem NEBEN-Spielort: `ApplyPick` vermerkt ihn dann nur am Spielort,
        // `entry.GeoSource` bleibt City — genau der Ausgangsfall „Mayrhofen, St.Veit", und bei
        // Ligen kommen im Lauf der Saison Spielorte dazu, der Ortstext aendert sich dort am haeufigsten.
        if (entry.GeoSource == GeoSource.TeamHint
            || entry.Venues.Any(v => v.GeoSource == GeoSource.TeamHint))
            entry.TeamHintCheckedAt = null;

        var results = await _geocoding.ResolveManyAsync(
            entry.LocationText, entry.State, entry.Federation, ct);

        // Mehrdeutig heisst: KEINE Koordinaten, aber ein Vermerk fuer die Arbeitsliste.
        var located = results.Where(r => r.Source != GeoSource.Ambiguous).ToList();
        var primary = located.FirstOrDefault();

        entry.Lat = primary?.Lat;
        entry.Lon = primary?.Lon;
        entry.GeoPlaceName = primary is null ? null : Truncate(primary.PlaceName, 200);
        entry.GeoSource = primary?.Source
            ?? (results.Any(r => r.Source == GeoSource.Ambiguous) ? GeoSource.Ambiguous : GeoSource.None);

        ReplaceVenues(entry, located);
    }

    /// <summary>
    /// Setzt die Spielorte neu. Vorhandene Zeilen werden geloescht und neu geschrieben statt
    /// abgeglichen: es sind hoechstens eine Handvoll je Turnier, und ein Abgleich ueber Namen
    /// waere aufwendiger als der Neuaufbau.
    /// </summary>
    private void ReplaceVenues(TournamentDirectoryEntry entry, List<GeocodeResult> located)
    {
        if (entry.Venues.Count > 0) _db.TournamentDirectoryVenues.RemoveRange(entry.Venues);
        entry.Venues = [];

        // Ein einzelner Spielort braucht keine Zeile — er steht schon am Eintrag. Die Tabelle
        // traegt nur, was dort NICHT abbildbar ist.
        if (located.Count < 2) return;

        for (var i = 0; i < located.Count; i++)
        {
            entry.Venues.Add(new TournamentDirectoryVenue
            {
                Ordinal = i,
                Name = Truncate(located[i].PlaceName, 200),
                SourceText = located[i].SourceText is { } t ? Truncate(t, 300) : null,
                Lat = located[i].Lat,
                Lon = located[i].Lon,
                GeoSource = located[i].Source,
            });
        }
    }
}
