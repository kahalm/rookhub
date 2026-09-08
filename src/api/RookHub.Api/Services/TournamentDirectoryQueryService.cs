using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public sealed record DirectorySearchQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public int? RadiusKm { get; init; }
    public string? Federation { get; init; }
    public TournamentSpeed? Speed { get; init; }

    /// <summary>
    /// Mehrere Foederationen bzw. Bedenkzeiten — so, wie ein Suchprofil sie fuehrt. Frueher wurde
    /// ein Profil mit MEHR als einem Wert beim Filtern schlicht ignoriert, waehrend die
    /// naechtliche Meldung die ganze Liste auswertete: die Ansicht zeigte dann Blitzturniere aus
    /// Italien, gemeldet wurde nur AUT/GER-Standard. Ein Profil muss beides gleich beantworten.
    /// </summary>
    public IReadOnlyList<string>? Federations { get; init; }
    public IReadOnlyList<TournamentSpeed>? Speeds { get; init; }
    public string? Text { get; init; }
    public bool WeekendOnly { get; init; }
    public int? MinPlayers { get; init; }
    public bool IncludeCancelled { get; init; }

    /// <summary>
    /// Wessen ausgeblendete Turniere herausgehalten werden sollen. <c>null</c> = niemandes (die
    /// Verwaltungs-Abfragen).
    /// </summary>
    public int? ForUserId { get; init; }

    /// <summary>
    /// Die vom Nutzer ausgeblendeten Turniere MITanzeigen — der Schalter in der Filterleiste.
    /// Ohne ihn waeren sie unwiederbringlich weg, und niemand wuesste, was er einmal
    /// weggeklickt hat.
    /// </summary>
    public bool IncludeIgnored { get; init; }

    /// <summary>Einzel und/oder Mannschaft. Leer = beides, und auch das noch Ungeklaerte.</summary>
    public IReadOnlyList<TournamentKind>? Kinds { get; init; }

    /// <summary>
    /// Gesuchte Alters-/Nachwuchsklassen. <c>None</c> = keine Einschraenkung. Ein Turnier passt,
    /// wenn es MINDESTENS eine der gesuchten Klassen fuehrt — „U12" soll die „U8-U18"-Meisterschaft
    /// finden, nicht nur reine U12-Turniere.
    /// </summary>
    public TournamentAgeGroups AgeGroups { get; init; } = TournamentAgeGroups.None;

    /// <summary>Gesuchte Geschlechtsklassen. Leer = alle.</summary>
    public IReadOnlyList<TournamentGender>? Genders { get; init; }

    /// <summary>
    /// Nur Turniere OHNE Jugendmerkmal im Namen. Seniorenturniere bleiben sichtbar — Seniorenschach
    /// ist Erwachsenenschach.
    /// </summary>
    public bool AdultsOnly { get; init; }

    /// <summary>Saisonwettbewerbe ausblenden (siehe TournamentClassifier.LooksLikeLeague).</summary>
    public bool HideLeagues { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Ein Listeneintrag. <paramref name="Members"/> sind die weiteren Gruppen desselben Turniers
/// (A/B/C) — der Haupteintrag steht in <paramref name="Entry"/>, die Liste enthaelt ALLE Gruppen
/// inklusive ihm, damit die Anzeige sie beschriften und verlinken kann.
/// </summary>
public sealed record DirectoryGroupItem(
    TournamentDirectoryEntry Entry,
    double? DistanceKm,
    IReadOnlyList<TournamentDirectoryEntry> Members);

public sealed record DirectorySearchResult(
    List<DirectoryGroupItem> Items, int Total, bool Truncated);

/// <summary>
/// Lesende Abfragen aufs Turnierverzeichnis.
///
/// Die Umkreissuche laeuft zweistufig: die Datenbank liefert ueber den Lat/Lon-Index nur die
/// Bounding-Box, die exakte Distanz und die Sortierung entstehen danach im Speicher. Grund ist
/// nicht Bequemlichkeit - <c>Math.Acos</c>/<c>Cos</c>/<c>Sin</c> uebersetzt der MySQL-Provider nicht
/// verlaesslich, und die Unit-Tests laufen gegen EF InMemory, wo so etwas nie auffaellt.
/// </summary>
public class TournamentDirectoryQueryService
{
    /// <summary>
    /// Obergrenze der Bounding-Box-Treffer, die in den Speicher geholt werden. Wird sie erreicht,
    /// meldet das Ergebnis <c>Truncated</c> - die Anzeige kann dann zum Verkleinern des Radius raten,
    /// statt eine stillschweigend unvollstaendige Liste zu zeigen.
    /// </summary>
    internal int MaxMaterialized { get; set; } = 5000;

    private readonly AppDbContext _db;

    public TournamentDirectoryQueryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DirectorySearchResult> SearchAsync(DirectorySearchQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var filtered = ApplyFilters(_db.TournamentDirectoryEntries.AsNoTracking(), query);

        var radius = query.RadiusKm;
        if (query.Lat is not { } lat || query.Lon is not { } lon || radius is not > 0)
        {
            // Gruppieren IN SQL, damit die Seitennavigation ueber Turniere zaehlt und nicht ueber
            // Gruppen: ein viergruppiges Open darf eine Seite nicht zu einem Viertel fuellen.
            // Zeilen aus der Zeit vor der Gruppierung haben noch keinen Schluessel. Sie duerfen
            // NICHT alle in einen Topf fallen (NULL == NULL waere genau das) — bis der naechste
            // Sweep sie nachtraegt, steht jede fuer sich.
            var groups = filtered
                .GroupBy(e => e.GroupKey ?? "id:" + e.Id)
                .Select(g => new { Key = g.Key, Start = g.Min(x => x.StartDate), PrimaryId = g.Min(x => x.Id) });

            var total = await groups.CountAsync(ct);
            var keys = await groups
                .OrderBy(g => g.Start).ThenBy(g => g.PrimaryId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            var items = await LoadGroupsAsync(filtered, keys.Select(k => k.Key).ToList(),
                keys.Select(k => k.PrimaryId).ToList(), null, ct);
            return new DirectorySearchResult(items, total, false);
        }

        var box = GeoDistance.BoundingBox(lat, lon, radius.Value);
        // Ein Turnier zaehlt, wenn EINER seiner Spielorte in der Box liegt. Bei Ligen nennt
        // chess-results mehrere („Mayrhofen, St.Veit") — mit dem Hauptort allein faende die
        // Umkreissuche ein Turnier nicht, das zur Haelfte vor der Haustuer stattfindet.
        var candidates = await filtered
            .Where(e => (e.Lat != null && e.Lon != null
                         && e.Lat >= box.MinLat && e.Lat <= box.MaxLat
                         && e.Lon >= box.MinLon && e.Lon <= box.MaxLon)
                        || e.Venues.Any(v => v.Lat >= box.MinLat && v.Lat <= box.MaxLat
                                             && v.Lon >= box.MinLon && v.Lon <= box.MaxLon))
            .Include(e => e.Venues)
            .Include(e => e.RoundDates)
            .Include(e => e.Sources)
            .Take(MaxMaterialized + 1)
            .ToListAsync(ct);

        var truncated = candidates.Count > MaxMaterialized;
        if (truncated) candidates.RemoveAt(candidates.Count - 1);

        // Die Entfernung ist die zum NAECHSTEN Spielort — danach fragt „was ist in meiner Naehe".
        var withDistance = candidates
            .Select(e => (Entry: e, Distance: NearestVenueKm(e, lat, lon)))
            .Where(x => x.Distance is not null && x.Distance <= radius.Value)
            .Select(x => (x.Entry, Distance: x.Distance!.Value))
            .ToList();

        // Im Umkreis liegen die Zeilen ohnehin im Speicher — hier ist Gruppieren in C# billiger
        // als eine zweite Abfrage, und die Distanz haengt schon an jeder Zeile.
        var grouped = withDistance
            .GroupBy(x => x.Entry.GroupKey ?? $"id:{x.Entry.Id}")
            .Select(g =>
            {
                var members = g.OrderBy(x => x.Entry.ChessResultsId, StringComparer.Ordinal)
                    .Select(x => x.Entry).ToList();
                // Hauptvertreter ist die KLEINSTE Id — dieselbe Regel wie im Weg ohne Umkreis
                // (dort `g.Min(x => x.Id)`). Sonst traegt dasselbe Turnier je nach gesetztem
                // Radius eine andere chessResultsId, und ein Link aus der einen Ansicht zeigt in
                // der anderen ins Leere. (Nach ChessResultsId zu sortieren waere ausserdem eine
                // Text-Sortierung von Zahlen: „1000" kaeme vor „999".)
                var primary = members.MinBy(m => m.Id)!;
                return new DirectoryGroupItem(primary, Math.Round(g.Min(x => x.Distance), 1), members);
            })
            .OrderBy(x => x.Entry.StartDate).ThenBy(x => x.DistanceKm)
            .ToList();

        return new DirectorySearchResult(
            grouped.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            grouped.Count,
            truncated);
    }

    /// <summary>
    /// Laedt zu den ausgewaehlten Gruppen-Schluesseln ALLE Mitglieder in EINER Abfrage (nicht je
    /// Gruppe eine) und ordnet sie den Haupteintraegen zu.
    /// </summary>
    private async Task<List<DirectoryGroupItem>> LoadGroupsAsync(
        IQueryable<TournamentDirectoryEntry> filtered, List<string> keys, List<int> primaryIds,
        Func<TournamentDirectoryEntry, double?>? distance, CancellationToken ct)
    {
        if (keys.Count == 0) return [];

        // Spielorte UND Spieltermine mitladen: die Karte zeichnet je Ort einen Pin, und der
        // Kalender entscheidet ueber die Termine, an welchen Tagen ein Turnier ueberhaupt steht.
        // Ohne die Termine faellt er stillschweigend auf „ganzer Zeitraum" zurueck — genau die
        // Auskunft, die bei einer Liga falsch ist.
        var rows = await filtered
            .Where(e => keys.Contains(e.GroupKey ?? "id:" + e.Id))
            .Include(e => e.Venues)
            .Include(e => e.RoundDates)
            .Include(e => e.Sources)
            .ToListAsync(ct);
        var byKey = rows.GroupBy(e => e.GroupKey ?? "id:" + e.Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ChessResultsId, StringComparer.Ordinal).ToList());

        var items = new List<DirectoryGroupItem>(primaryIds.Count);
        foreach (var (key, primaryId) in keys.Zip(primaryIds))
        {
            if (!byKey.TryGetValue(key, out var members) || members.Count == 0) continue;
            var primary = members.FirstOrDefault(m => m.Id == primaryId) ?? members[0];
            items.Add(new DirectoryGroupItem(primary, distance?.Invoke(primary), members));
        }
        return items;
    }

    /// <summary>
    /// Kartenmarker: nur die Zeilen mit Koordinaten in der sichtbaren Box, hart gedeckelt. Ohne
    /// Deckel wuerde ein herausgezoomtes Europa zehntausende Pins in eine Antwort packen.
    /// </summary>
    public async Task<List<DirectoryGroupItem>> MapPinsAsync(
        DirectorySearchQuery query, double minLat, double maxLat, double minLon, double maxLon,
        int limit = 2000, CancellationToken ct = default)
    {
        var rows = await ApplyFilters(_db.TournamentDirectoryEntries.AsNoTracking(), query)
            .Where(e => (e.Lat != null && e.Lon != null
                         && e.Lat >= minLat && e.Lat <= maxLat
                         && e.Lon >= minLon && e.Lon <= maxLon)
                        || e.Venues.Any(v => v.Lat >= minLat && v.Lat <= maxLat
                                             && v.Lon >= minLon && v.Lon <= maxLon))
            .Include(e => e.Venues)
            .OrderBy(e => e.StartDate)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToListAsync(ct);

        // Gruppiert wie die LISTE, mit demselben Schluessel und derselben Wahl des
        // Hauptvertreters (kleinste Id). Ohne das widersprechen sich die beiden Ansichten
        // desselben Bestandes: die Liste zeigt „24th ASEAN+ Age-Group Championships" als EINE
        // Zeile, die Karte legte 19 Punkte uebereinander auf denselben Spielort. Am Dev-Stand
        // gemessen: 2952 verortete Zeilen sind 2344 Turniere — 608 Punkte waren Gruppen
        // desselben Turniers, und auf einem Punkt allein 54 statt 3.
        //
        // Die Mitglieder kommen aus dem AUSSCHNITT, nicht aus dem ganzen Bestand: das spart die
        // zweite Abfrage mit einer IN-Liste ueber bis zu 2000 Schluessel. Gruppen eines Turniers
        // teilen sich seinen Spielort, liegen also ohnehin miteinander im Bild; nur ein Turnier
        // mit Gruppen an VERSCHIEDENEN Orten kann am Bildrand eine kleinere Gruppenzahl zeigen
        // als die Liste.
        return rows
            .GroupBy(e => e.GroupKey ?? $"id:{e.Id}")
            .Select(g =>
            {
                var members = g.OrderBy(m => m.ChessResultsId, StringComparer.Ordinal).ToList();
                return new DirectoryGroupItem(members.MinBy(m => m.Id)!, null, members);
            })
            .OrderBy(i => i.Entry.StartDate)
            .ToList();
    }

    /// <summary>
    /// Ein Turnier SAMT seiner Gruppen. Die Liste fasst A/B/C zu einem Eintrag zusammen — die
    /// Detailansicht muss dasselbe Turnier zeigen, sonst widerspricht der Deep-Link aus einer
    /// Benachrichtigung der Liste, aus der er stammt.
    /// </summary>
    public async Task<DirectoryGroupItem?> GetAsync(string publicId, CancellationToken ct = default)
    {
        var entry = await _db.TournamentDirectoryEntries.AsNoTracking()
            .Include(e => e.Venues)
            .Include(e => e.RoundDates)
            .Include(e => e.Sources)
            .FirstOrDefaultAsync(e => e.PublicId == publicId, ct);
        if (entry is null) return null;

        var members = entry.GroupKey is null
            ? [entry]
            : await _db.TournamentDirectoryEntries.AsNoTracking()
                .Where(e => e.GroupKey == entry.GroupKey)
                .ToListAsync(ct);

        // Angezeigt wird die angefragte Gruppe; die Mitglieder liefern Name, Summe und Verlinkung.
        return new DirectoryGroupItem(
            entry, null, members.OrderBy(m => m.ChessResultsId, StringComparer.Ordinal).ToList());
    }

    /// <summary>Ortsvorschlaege fuers Suchprofil-Formular: Postleitzahl oder Ortsname.</summary>
    /// <summary>
    /// Der naechstgelegene Ort zu einem Koordinatenpaar — die Umkehrung der Ortssuche.
    ///
    /// <para>Gebraucht fuer den Browser-Standort: der liefert Koordinaten, im Ortsfeld soll aber
    /// ein NAME stehen. Aufgeloest wird gegen den lokalen Gazetteer, nicht gegen einen Web-Dienst:
    /// die Koordinaten eines Nutzers sind das Letzte, was diese Anwendung nach draussen geben
    /// sollte, und offline ginge es ohnehin nicht.</para>
    ///
    /// <para>Erst eine Bounding-Box in SQL (laeuft auf dem Lat/Lon-Index), dann die exakte
    /// Distanz in C# — dieselbe Zweiteilung wie in der Umkreissuche, weil der MySQL-Provider
    /// `Math.Acos`/`Cos`/`Sin` nicht verlaesslich uebersetzt. Die Box waechst in Schritten, damit
    /// ein Standort in duenn besiedelter Gegend nicht ohne Antwort bleibt, ein Standort in der
    /// Stadt aber nicht halb Europa materialisiert.</para>
    /// </summary>
    public async Task<GeoPlace?> NearestPlaceAsync(double lat, double lon, CancellationToken ct = default)
    {
        foreach (var radiusKm in NearestSearchRadiiKm)
        {
            var box = GeoDistance.BoundingBox(lat, lon, radiusKm);
            var candidates = await _db.GeoPlaces.AsNoTracking()
                .Where(g => g.Lat >= box.MinLat && g.Lat <= box.MaxLat
                            && g.Lon >= box.MinLon && g.Lon <= box.MaxLon)
                .Take(MaxMaterialized)
                .ToListAsync(ct);
            if (candidates.Count == 0) continue;

            var nearest = candidates
                .Select(g => (Place: g, Distance: GeoDistance.Haversine(lat, lon, g.Lat, g.Lon)))
                .Where(x => x.Distance <= radiusKm)
                // Bei gleicher Entfernung der groessere Ort: „Wien" ist die brauchbarere Antwort
                // als ein gleich weit entfernter Weiler am Stadtrand.
                .OrderBy(x => x.Distance).ThenByDescending(x => x.Place.Population)
                .FirstOrDefault();
            if (nearest.Place is not null) return nearest.Place;
        }
        return null;
    }

    /// <summary>Suchradien fuer den naechsten Ort, aufsteigend — der erste Treffer gewinnt.</summary>
    private static readonly int[] NearestSearchRadiiKm = [10, 50, 200];

    public async Task<List<GeoPlace>> SuggestPlacesAsync(string term, int limit = 10, CancellationToken ct = default)
    {
        term = term.Trim();
        if (term.Length < 2) return [];

        // Reine Ziffern koennen nur eine PLZ sein, alles andere ein Ortsname - das haelt die
        // Abfrage auf einem der beiden Indizes statt auf einem OR ueber beide.
        if (term.All(char.IsAsciiDigit))
        {
            return await _db.GeoPlaces.AsNoTracking()
                .Where(g => g.PostalCode != null && g.PostalCode.StartsWith(term))
                .OrderBy(g => g.PostalCode).ThenBy(g => g.Name)
                .Take(limit)
                .ToListAsync(ct);
        }

        var normalized = GeoTextNormalizer.Normalize(term);
        return await _db.GeoPlaces.AsNoTracking()
            .Where(g => g.NameNormalized.StartsWith(normalized))
            // Exakte Treffer zuerst, danach die groessten Orte - "Wien" soll nicht hinter
            // "Wiener Neudorf" landen.
            .OrderByDescending(g => g.NameNormalized == normalized)
            .ThenByDescending(g => g.Population)
            .ThenBy(g => g.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    private IQueryable<TournamentDirectoryEntry> ApplyFilters(
        IQueryable<TournamentDirectoryEntry> source, DirectorySearchQuery query)
    {
        if (!query.IncludeCancelled)
            source = source.Where(e => e.RemovedAt == null);

        // Als EXISTS-Unterabfrage und nicht als materialisierte Liste: wer viel wegklickt, hat
        // sonst irgendwann einen Filter mit hunderten Nummern in jeder Abfrage.
        if (query.ForUserId is { } userId && !query.IncludeIgnored)
        {
            source = source.Where(e => !_db.TournamentDirectoryIgnores
                .Any(i => i.UserId == userId && i.PublicId == e.PublicId));
        }

        // Ueberlappung statt Enthaltensein: ein zehntaegiges Open, das in den Zeitraum
        // hineinragt, gehoert in den Kalender - auch wenn es davor begann.
        if (query.From is { } from)
            source = source.Where(e => (e.EndDate ?? e.StartDate) == null || (e.EndDate ?? e.StartDate) >= from);
        if (query.To is { } to)
            source = source.Where(e => (e.StartDate ?? e.EndDate) == null || (e.StartDate ?? e.EndDate) <= to);

        if (!string.IsNullOrWhiteSpace(query.Federation))
        {
            var fed = query.Federation.Trim().ToUpperInvariant();
            source = source.Where(e => e.Federation == fed);
        }
        else if (query.Federations is { Count: > 0 } feds)
        {
            var list = feds.Select(f => f.Trim().ToUpperInvariant()).ToList();
            source = source.Where(e => e.Federation != null && list.Contains(e.Federation));
        }

        if (query.Speed is { } speed)
            source = source.Where(e => e.Speed == speed);
        else if (query.Speeds is { Count: > 0 } speeds)
        {
            var list = speeds.ToList();
            source = source.Where(e => list.Contains(e.Speed));
        }

        if (query.Kinds is { Count: > 0 } kinds)
        {
            var list = kinds.ToList();
            // „EINZEL" schliesst das Nicht-Eingeordnete MIT ein. Aus der Quelle kommt nur die
            // Auskunft „ist eine MANNSCHAFTS-Turnierart" (chess-results `art=2|3`); alles andere
            // ist Einzel — und `Unknown` heisst genau: bei diesem Eintrag lief die
            // Mannschafts-Abfrage noch nicht (Rotationswoche) oder sie fiel aus. Fuer den
            // Suchenden ist das kein eigener Fall, und ihn als eigenen zu fuehren hiess: „Einzel"
            // liefert eine halb leere Liste, obwohl der Bestand voll davon ist.
            //
            // In der SPALTE bleibt `Unknown` bewusst stehen: ein Netzausfall darf den Bestand
            // nicht auf „Einzel" umschreiben (siehe TournamentDirectoryService.Apply).
            if (list.Contains(TournamentKind.Individual) && !list.Contains(TournamentKind.Unknown))
                list.Add(TournamentKind.Unknown);
            source = source.Where(e => list.Contains(e.Kind));
        }

        if (query.Genders is { Count: > 0 } genders)
        {
            var list = genders.ToList();
            source = source.Where(e => list.Contains(e.Gender));
        }

        // Bitweises UND in SQL: die Klassen liegen als Bitfeld in EINER Spalte, und ein Turnier
        // passt, wenn sich die gesuchten mit den gefuehrten UEBERSCHNEIDEN. EF InMemory rechnet das
        // im Speicher aus und wuerde einen Uebersetzungsfehler verschlucken — deshalb steht die
        // Abfrage zusaetzlich in QueryTranslationTests.
        if (query.AgeGroups != TournamentAgeGroups.None)
        {
            var wanted = query.AgeGroups;
            source = source.Where(e => (e.AgeGroups & wanted) != TournamentAgeGroups.None);
        }

        if (query.AdultsOnly)
        {
            const TournamentAgeGroups youth = TournamentClassifier.YouthMask;
            source = source.Where(e => (e.AgeGroups & youth) == TournamentAgeGroups.None);
        }

        if (query.HideLeagues)
            source = source.Where(e => !e.IsLeague);

        if (query.MinPlayers is { } min)
            source = source.Where(e => e.PlayerCount >= min);

        if (query.WeekendOnly)
            source = source.Where(e => e.StartsOnWeekend);

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            source = source.Where(e => e.Name.Contains(text)
                                       || (e.LocationText != null && e.LocationText.Contains(text))
                                       || (e.Organizer != null && e.Organizer.Contains(text)));
        }

        return source;
    }

    /// <summary>
    /// Entfernung zum NAECHSTEN Spielort — `null`, wenn das Turnier nicht verortet ist. Die
    /// Koordinaten am Eintrag sind der Hauptort und werden mitgezaehlt; die Tabelle traegt nur
    /// Turniere mit MEHREREN Orten.
    /// </summary>
    private static double? NearestVenueKm(TournamentDirectoryEntry entry, double lat, double lon)
    {
        double? best = entry.Lat is { } la && entry.Lon is { } lo
            ? GeoDistance.Haversine(lat, lon, la, lo)
            : null;
        foreach (var venue in entry.Venues)
        {
            var d = GeoDistance.Haversine(lat, lon, venue.Lat, venue.Lon);
            if (best is null || d < best) best = d;
        }
        return best;
    }
}
