using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Loest einen mehrdeutigen Spielort ueber die VEREINSNAMEN des Turniers auf.
///
/// <para><b>Das Problem, das nur so zu loesen ist.</b> chess-results kuerzt den Spielort ab, und
/// die Abkuerzung kann zufaellig exakt der Name eines ANDEREN Ortes sein. Nachgestellt an
/// tnr1405166 („1. Frauenbundesliga AUT"): der Ortstext sagt „Mayrhofen, St.Veit", im Ortslexikon
/// heisst genau ein Eintrag „St. Veit" — und der liegt in TIROL. Kaernten steht dort als „St. Veit
/// an der Glan" und kommt in der Kandidatenliste gar nicht vor; aus dem Namen allein ist der
/// Fehler also nicht erkennbar, und er sieht dabei nicht einmal mehrdeutig aus. Die Ausschreibung
/// belegt Kaernten (Runden 5-7 „St. Veit/Glan"), und die Vereinsliste des Turniers nennt „SV ASKOE
/// St. Veit/Glan".</para>
///
/// <para><b>Warum das nur MIT Beleg passiert.</b> Die Kandidaten werden hier auf laengere Namen
/// erweitert (alles, was mit dem gesuchten Namen beginnt) — das erzeugt zwangslaeufig mehr
/// Mehrdeutigkeit. Deshalb wird ein Pin ausschliesslich dann gesetzt oder geaendert, wenn ein
/// Vereinsname ein UNTERSCHEIDENDES Wort des laengeren Namens enthaelt („glan"). Ohne Beleg
/// bleibt alles, wie es war. Ein Fehlgriff braucht damit einen Verein, der den falschen Ort im
/// Namen traegt — deutlich unwahrscheinlicher als der Zufall, den wir hier reparieren.</para>
///
/// <para>Ein Durchgang kostet EINEN Seitenabruf je Turnier und ist deshalb gedeckelt; der Vermerk
/// <see cref="TournamentDirectoryEntry.TeamHintCheckedAt"/> verhindert, dass dieselben Seiten
/// jede Nacht erneut geholt werden.</para>
/// </summary>
public class VenueDisambiguationService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VenueDisambiguationService> _log;

    /// <summary>Woerter, die keinen Ort unterscheiden — „an der", „im", „bei" stehen ueberall.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "an", "am", "im", "in", "auf", "bei", "ob", "der", "die", "das", "dem", "den", "des",
        "ober", "unter", "nieder", "sankt", "st", "bad", "neu", "alt", "gross", "klein",
    };

    public VenueDisambiguationService(
        AppDbContext db, IHttpClientFactory httpClientFactory,
        ILogger<VenueDisambiguationService> log)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    /// <summary>Ergebnis eines Durchgangs — fuer Log und Admin-Antwort.</summary>
    public sealed record DisambiguationResult(int Checked, int Resolved, int Failed);

    /// <summary>
    /// Nimmt die naechsten unaufgeloesten Eintraege vor. „Unaufgeloest" heisst: als mehrdeutig
    /// vermerkt (kein Pin) ODER ueber einen Ortsnamen verortet, zu dem es laengere Namen gibt —
    /// das ist der Abkuerzungs-Fall, der nicht mehrdeutig AUSSIEHT.
    /// </summary>
    public async Task<DisambiguationResult> RunAsync(int limit, CancellationToken ct = default)
    {
        // Nur Turniere, die noch nicht vorbei sind: ein Seitenabruf fuer einen Pin, den niemand
        // mehr sucht, ist verschwendet. (Erster Durchgang lief nach StartDate aufsteigend — und
        // damit ausgerechnet die aeltesten, laengst gespielten Turniere ab.)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var candidates = await _db.TournamentDirectoryEntries
            .Include(e => e.Venues)
            .Where(e => e.RemovedAt == null
                        && e.LocationText != null
                        && e.TeamHintCheckedAt == null
                        // Die Vereinsliste steht auf der chess-results-Turnierseite — ohne
                        // Nummer dort ist sie nicht erreichbar (FIDE-Eintraege).
                        && e.ChessResultsId != null
                        && ((e.EndDate ?? e.StartDate) == null || (e.EndDate ?? e.StartDate) >= today)
                        && (e.GeoSource == GeoSource.Ambiguous || e.GeoSource == GeoSource.City))
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

        var resolved = 0;
        var failed = 0;
        foreach (var entry in candidates)
        {
            entry.TeamHintCheckedAt = DateTime.UtcNow;
            try
            {
                if (await TryRefineAsync(entry, ct)) resolved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Ein Turnier, dessen Seite nicht zu holen ist, darf den Durchgang nicht beenden.
                failed++;
                _log.LogWarning(ex, "Vereinsnamen von {Id} nicht abrufbar", entry.ChessResultsId);
            }
        }
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Spielort-Aufloesung: {Checked} geprueft, {Resolved} aufgeloest, {Failed} Fehler",
            candidates.Count, resolved, failed);
        return new DisambiguationResult(candidates.Count, resolved, failed);
    }

    private async Task<bool> TryRefineAsync(TournamentDirectoryEntry entry, CancellationToken ct)
    {
        var iso2 = FideCountryCodes.ToIso2(entry.Federation);
        if (iso2 is null) return false;

        // JEDEN Ortsabschnitt pruefen, nicht nur den ersten. Der erste Durchgang sah bei
        // „Mayrhofen, St.Veit" nur „Mayrhofen" an — und ausgerechnet das ist der eindeutige Teil;
        // der Fehler steckt im zweiten.
        List<string>? teams = null;
        var changed = false;

        foreach (var segment in GeoTextNormalizer.VenueSegments(entry.LocationText))
        {
            var searched = await FirstMatchingNameAsync(segment, iso2, ct);
            if (searched is null) continue;

            var widened = await _db.GeoPlaces.AsNoTracking()
                .Where(g => g.Country == iso2 && g.NameNormalized.StartsWith(searched))
                .ToListAsync(ct);

            // Nur ein LAENGERER Name bringt etwas Neues. Ohne diese Bedingung wird fuer jeden
            // mehrfach vorkommenden Ortsnamen („Mayrhofen" gibt es viermal, alle gleich benannt)
            // eine Turnierseite geholt, die nichts entscheiden kann.
            if (!widened.Any(w => w.NameNormalized.Length > searched.Length)) continue;

            teams ??= await FetchTeamNamesAsync(entry.ChessResultsId!, ct);
            if (teams.Count == 0) return false;

            var pick = PickByTeamHint(widened, searched, teams);
            if (pick is null) continue;

            ApplyPick(entry, segment, pick);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Uebernimmt den gefundenen Ort — im passenden Spielort (erkannt am Textabschnitt, aus dem er
    /// stammt) und, wenn das der Hauptort ist, auch in den Koordinaten des Eintrags selbst.
    /// </summary>
    private void ApplyPick(TournamentDirectoryEntry entry, string segment, GeoPlace pick)
    {
        var name = pick.Name.Length <= 200 ? pick.Name : pick.Name[..200];

        var venue = entry.Venues.FirstOrDefault(v =>
            string.Equals(v.SourceText, segment, StringComparison.OrdinalIgnoreCase));
        if (venue is not null)
        {
            venue.Lat = pick.Lat;
            venue.Lon = pick.Lon;
            venue.Name = name;
            venue.GeoSource = GeoSource.TeamHint;
        }

        // Der Eintrag traegt den HAUPT-Spielort. Er wandert mit, wenn genau der gemeint war —
        // oder wenn es gar keine Spielort-Liste gibt (Einzelort).
        var isPrimary = venue is null || venue.Ordinal == 0;
        if (isPrimary)
        {
            entry.Lat = pick.Lat;
            entry.Lon = pick.Lon;
            entry.GeoPlaceName = name;
            entry.GeoSource = GeoSource.TeamHint;
        }
        entry.UpdatedAt = DateTime.UtcNow;

        _log.LogInformation("Spielort von {Id} ueber Vereinsnamen auf {Place} gesetzt",
            entry.ChessResultsId, pick.Name);
    }

    /// <summary>Der erste Ortsname aus dem Text, den das Lexikon kennt (normalisiert).</summary>
    private async Task<string?> FirstMatchingNameAsync(string? locationText, string iso2, CancellationToken ct)
    {
        foreach (var segment in GeoTextNormalizer.VenueSegments(locationText))
        {
            var candidates = GeoTextNormalizer.PlaceCandidates(segment);
            if (candidates.Count == 0) continue;

            var hit = await _db.GeoPlaces.AsNoTracking()
                .Where(g => g.Country == iso2 && candidates.Contains(g.NameNormalized))
                .Select(g => g.NameNormalized)
                .ToListAsync(ct);
            var best = candidates.FirstOrDefault(c => hit.Contains(c));
            if (best is not null) return best;
        }
        return null;
    }

    /// <summary>
    /// Waehlt unter den erweiterten Kandidaten den, dessen UNTERSCHEIDENDE Woerter in einem
    /// Vereinsnamen vorkommen. `null`, wenn keiner oder mehrere gleich gut passen — dann bleibt
    /// alles, wie es war.
    ///
    /// <para>Unterscheidend sind die Woerter, die der laengere Name gegenueber dem gesuchten
    /// hinzufuegt, ohne Fuellwoerter: „st veit an der glan" gegen „st veit" ergibt „glan".</para>
    /// </summary>
    internal static GeoPlace? PickByTeamHint(
        List<GeoPlace> candidates, string searchedName, IReadOnlyList<string> teamNames)
    {
        var searchedWords = searchedName.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var haystack = teamNames.Select(GeoTextNormalizer.Normalize).ToList();

        var scored = candidates
            .Select(place => (Place: place, Hits: DistinctiveWords(place, searchedWords)
                .Count(word => haystack.Any(team => ContainsWord(team, word)))))
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ToList();

        if (scored.Count == 0) return null;
        if (scored.Count > 1 && scored[0].Hits == scored[1].Hits
            && !SamePlace(scored[0].Place, scored[1].Place)) return null;
        return scored[0].Place;
    }

    private static List<string> DistinctiveWords(GeoPlace place, HashSet<string> searchedWords) =>
        place.NameNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !searchedWords.Contains(w) && !Filler.Contains(w))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Ganzes Wort, nicht Teilzeichenkette — „glan" darf nicht in „glanegg" treffen.</summary>
    private static bool ContainsWord(string haystack, string word) =>
        haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(word, StringComparer.Ordinal);

    /// <summary>Zwei Kandidaten am praktisch selben Ort (verschiedene Postleitzahlen einer Stadt).</summary>
    private static bool SamePlace(GeoPlace a, GeoPlace b) =>
        GeoDistance.Haversine(a.Lat, a.Lon, b.Lat, b.Lon) < 5;

    /// <summary>
    /// Holt die Vereinsnamen ueber den Crawler. Derselbe benannte Client wie der naechtliche
    /// Sweep — Basis-Adresse, Schluessel und das laengere Zeitlimit haengen schon dort
    /// (siehe Program.cs), und die Wartezeit hinter dem Rate-Limiter des Crawlers ist dieselbe.
    /// </summary>
    private async Task<List<string>> FetchTeamNamesAsync(string chessResultsId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(TournamentDirectoryService.CrawlerClientName);

        using var response = await client.GetAsync(
            $"/api/tournament-search/teams?id={Uri.EscapeDataString(chessResultsId)}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
}
