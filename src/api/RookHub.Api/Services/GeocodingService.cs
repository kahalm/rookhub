using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public sealed record GeocodeResult(double Lat, double Lon, GeoSource Source, string PlaceName)
{
    /// <summary>Der Abschnitt des Ortstexts, aus dem dieser Ort kommt (Nachvollziehbarkeit).</summary>
    public string? SourceText { get; init; }
}

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
    /// <summary>Bis hierher gilt „derselbe Ort" — 24 Wiener Postleitzahlen sind kein Zweifelsfall.</summary>
    private const double SameTownKm = 5;

    /// <summary>Umkreis, in dem ein verlaesslich verortetes Turnier als Anker zaehlt.</summary>
    private const double DensityRadiusKm = 12;

    private readonly AppDbContext _db;

    public GeocodingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<GeocodeResult?> ResolveAsync(
        string? locationText, string? state, string? federation, CancellationToken ct = default)
    {
        var all = await ResolveManyAsync(locationText, state, federation, ct);
        return all.FirstOrDefault();
    }

    /// <summary>
    /// ALLE Spielorte eines Ortstexts — bei Ligen stehen dort mehrere („Mayrhofen, St.Veit",
    /// „Leoben; Klagenfurt; St. Veit; Graz"). Der erste ist der Haupt-Spielort.
    ///
    /// <para>Reihenfolge der Wege, absteigend nach Verlaesslichkeit:</para>
    /// <list type="number">
    /// <item><b>Postleitzahl.</b> Steht eine im Text, ist der Ort damit auf wenige Kilometer
    /// bestimmt. Stehen MEHRERE weit auseinander, sind es mehrere Spielorte. Adressen benutzen
    /// dieselben Trennzeichen wie Ortslisten („Halle 1, Eichetstrasse 29, 5020 Salzburg") —
    /// deshalb kommt dieser Weg VOR der Zerlegung: eine Adresse ist ein Ort, nicht drei.</item>
    /// <item><b>Ortsnamen je Abschnitt.</b> Erst ohne Postleitzahl wird am Komma zerlegt und
    /// jeder Abschnitt fuer sich aufgeloest.</item>
    /// <item><b>Regionsmitte</b> aus der Bundesland-Spalte, wenn nichts davon traegt.</item>
    /// </list>
    ///
    /// <para>Ein leeres Ergebnis heisst „nicht verortet". Ein Ergebnis mit
    /// <see cref="GeoSource.Ambiguous"/> heisst „Name gefunden, aber mehrdeutig" — auch dann gibt
    /// es keine Koordinaten, der Unterschied steht nur fuer die Arbeitsliste.</para>
    /// </summary>
    public async Task<List<GeocodeResult>> ResolveManyAsync(
        string? locationText, string? state, string? federation, CancellationToken ct = default)
    {
        var iso2 = FideCountryCodes.ToIso2(federation);

        var byPostal = await ResolveAllPostalCodesAsync(locationText, iso2, ct);
        if (byPostal.Count > 0) return byPostal;

        var bySegment = new List<GeocodeResult>();
        var ambiguous = false;
        foreach (var segment in GeoTextNormalizer.VenueSegments(locationText))
        {
            var hit = await ResolveByPlaceNameAsync(segment, iso2, ct);
            if (hit is null) continue;
            if (hit.Source == GeoSource.Ambiguous) { ambiguous = true; continue; }
            if (bySegment.Any(v => GeoDistance.Haversine(v.Lat, v.Lon, hit.Lat, hit.Lon) < SameTownKm)) continue;
            bySegment.Add(hit);
        }
        if (bySegment.Count > 0) return bySegment;

        var region = await ResolveByRegionAsync(state, iso2, ct);
        if (region is not null) return [region];

        // Kein Pin — aber der Unterschied zwischen „nichts gefunden" und „gefunden, aber
        // mehrdeutig" gehoert in die Arbeitsliste.
        return ambiguous
            ? [new GeocodeResult(0, 0, GeoSource.Ambiguous, "") { SourceText = locationText }]
            : [];
    }

    /// <summary>
    /// Alle im Text vorkommenden Postleitzahlen als Spielorte. Nahe beieinanderliegende werden
    /// zusammengefasst: „1010/1020 Wien" ist ein Ort, nicht zwei.
    /// </summary>
    private async Task<List<GeocodeResult>> ResolveAllPostalCodesAsync(
        string? locationText, string? iso2, CancellationToken ct)
    {
        if (iso2 is null) return [];

        var candidates = GeoTextNormalizer.PostalCandidates(locationText);
        if (candidates.Count == 0) return [];

        var matches = await _db.GeoPlaces.AsNoTracking()
            .Where(g => g.Country == iso2 && g.PostalCode != null && candidates.Contains(g.PostalCode))
            .ToListAsync(ct);
        if (matches.Count == 0) return [];

        // Steht der Ortsname des Treffers auch im Text („8051 Graz"), ist die Zuordnung sicher.
        // Ohne diese Bestaetigung gewinnt eine HAUSNUMMER, die zufaellig wie eine Postleitzahl
        // aussieht: „Wienerstrasse 351, 8051 Graz" hat mit 351 eine gueltige PLZ irgendwo sonst.
        var normalizedText = GeoTextNormalizer.Normalize(locationText);
        var textCandidates = GeoTextNormalizer.PlaceCandidates(locationText);
        var confirmed = matches.Where(m => Confirms(normalizedText, textCandidates, m.Name)).ToList();

        // Bestaetigte Treffer sind die Spielorte. Ist keiner bestaetigt, bleibt es bei EINEM: der
        // spaetesten Ziffernfolge im Text — in Adressen steht die Hausnummer vor der Postleitzahl.
        var pool = confirmed.Count > 0
            ? confirmed
            : [matches.OrderByDescending(m => candidates.IndexOf(m.PostalCode!)).First()];

        var result = new List<GeocodeResult>();
        // In der Reihenfolge des Textes, damit der erste genannte Ort der Haupt-Spielort ist.
        foreach (var code in candidates)
        {
            var forCode = pool.Where(m => m.PostalCode == code).ToList();
            if (forCode.Count == 0) continue;
            var chosen = forCode.OrderByDescending(m => m.Population).ThenBy(m => m.Id).First();
            if (result.Any(v => GeoDistance.Haversine(v.Lat, v.Lon, chosen.Lat, chosen.Lon) < SameTownKm)) continue;
            result.Add(new GeocodeResult(chosen.Lat, chosen.Lon, GeoSource.PostalCode, chosen.Name)
                { SourceText = locationText });
        }
        return result;
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
        // schlagen, sonst landet der Pin im falschen Ort.
        var winner = candidates.FirstOrDefault(c => matches.Any(m => m.NameNormalized == c));
        if (winner is null) return null;

        var group = matches.Where(m => m.NameNormalized == winner).ToList();
        return await ChooseAsync(group, locationText, ct);
    }

    /// <summary>
    /// Waehlt unter gleichnamigen Orten — und sagt „weiss nicht", wenn nichts entscheidet.
    ///
    /// <para>Bis 0.418.0 gewann hier schlicht die groesste Einwohnerzahl und bei Gleichstand der
    /// erste Treffer. Fuer Orte, die derselbe Name an weit auseinanderliegenden Stellen bezeichnet,
    /// ist das eine Muenze: gemessen am Dev-Stand 171 solche Eintraege, davon 29 mit einem
    /// nachweislich falschen Pin (bis 489 km daneben, „Muenster" gibt es 19-mal).</para>
    ///
    /// <para>Die Reihenfolge jetzt:</para>
    /// <list type="number">
    /// <item>Liegen alle Kandidaten dicht zusammen (&lt; <see cref="SameTownKm"/>), ist die Wahl
    /// gleichgueltig — „Wien" hat 24 Postleitzahl-Zeilen, alle in Wien. Dann entscheidet wie
    /// bisher die Einwohnerzahl.</item>
    /// <item>Sonst entscheidet die TURNIERDICHTE: wie viele Turniere mit VERLAESSLICHER Verortung
    /// (Postleitzahl oder von Hand gesetzt) liegen in der Naehe? Wo ein Schachklub Turniere
    /// austraegt, stehen mehrere. Bewusst nur verlaessliche Pins — zaehlte man alle mit, wuerden
    /// sich die falschen Pins selbst bestaetigen (an „Baernbach" nachgestellt: alle Pins waehlen
    /// den falschen Ort, verlaessliche den richtigen).</item>
    /// <item>Bleibt es unentschieden, gibt es KEINEN Ortstreffer. Der Aufrufer faellt dann auf die
    /// Regionsmitte zurueck oder laesst den Eintrag unverortet — mit
    /// <see cref="GeoSource.Ambiguous"/> als Vermerk, damit er in der Arbeitsliste auftaucht
    /// statt still falsch auf der Karte zu stehen.</item>
    /// </list>
    /// </summary>
    private async Task<GeocodeResult?> ChooseAsync(
        List<GeoPlace> group, string? sourceText, CancellationToken ct)
    {
        var byPopulation = group.OrderByDescending(m => m.Population).ThenBy(m => m.Id).First();
        if (group.Count == 1 || Spread(group) < SameTownKm)
            return new GeocodeResult(byPopulation.Lat, byPopulation.Lon, GeoSource.City, byPopulation.Name)
                { SourceText = sourceText };

        var anchors = await ReliableAnchorsAsync(ct);
        var scored = group
            .Select(m => (Place: m, Count: anchors.Count(a => GeoDistance.Haversine(m.Lat, m.Lon, a.Lat, a.Lon) <= DensityRadiusKm)))
            .OrderByDescending(x => x.Count)
            .ToList();

        // Ein Sieger nur, wenn er ALLEIN vorne liegt und ueberhaupt einen Anker hat.
        if (scored[0].Count > 0 && (scored.Count == 1 || scored[0].Count > scored[1].Count))
        {
            var best = scored[0].Place;
            return new GeocodeResult(best.Lat, best.Lon, GeoSource.City, best.Name) { SourceText = sourceText };
        }

        return new GeocodeResult(0, 0, GeoSource.Ambiguous, byPopulation.Name) { SourceText = sourceText };
    }

    /// <summary>Groesste Entfernung zwischen zwei Kandidaten in km.</summary>
    private static double Spread(List<GeoPlace> group)
    {
        var max = 0.0;
        for (var i = 0; i < group.Count; i++)
        {
            for (var j = i + 1; j < group.Count; j++)
            {
                max = Math.Max(max, GeoDistance.Haversine(
                    group[i].Lat, group[i].Lon, group[j].Lat, group[j].Lon));
            }
        }
        return max;
    }

    /// <summary>
    /// Die verlaesslich verorteten Turniere als Anker fuer die Dichte-Entscheidung. Einmal je
    /// Aufruf-Lebensdauer des Dienstes geladen (der Sweep laeuft ueber tausende Eintraege — je
    /// Eintrag zu fragen waere eine Abfrage je Eintrag).
    /// </summary>
    private List<(double Lat, double Lon)>? _anchors;

    private async Task<List<(double Lat, double Lon)>> ReliableAnchorsAsync(CancellationToken ct)
    {
        return _anchors ??= (await _db.TournamentDirectoryEntries.AsNoTracking()
            .Where(e => e.Lat != null && e.Lon != null
                        && (e.GeoSource == GeoSource.PostalCode || e.GeoSource == GeoSource.Manual))
            .Select(e => new { e.Lat, e.Lon })
            .ToListAsync(ct))
            .Select(x => (x.Lat!.Value, x.Lon!.Value))
            .ToList();
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

    /// <summary>
    /// Bestaetigt der Text den Ortsnamen einer gefundenen Postleitzahl?
    ///
    /// <para>Der einfache Fall ist die wortgleiche Nennung („8051 Graz"). Der zweite Fall ist die
    /// ABKUERZUNG: chess-results schreibt „9300 St.Veit", im Ortslexikon steht „St. Veit an der
    /// Glan". Wortgleich ist das nicht, aber der Text nennt einen Wortanfang des Namens — und
    /// zwar einen langen genug, dass es kein Zufall ist. Ohne diesen zweiten Fall verliert ein
    /// abgekuerzt genannter Spielort seine Postleitzahl und damit die verlaesslichste Verortung,
    /// die es fuer ihn gibt.</para>
    ///
    /// <para>Vier Zeichen als Untergrenze, damit „st" oder „bad" nicht auf jeden gleichnamigen
    /// Ort passt.</para>
    /// </summary>
    private static bool Confirms(string normalizedText, List<string> textCandidates, string placeName)
    {
        var name = GeoTextNormalizer.Normalize(placeName);
        if (name.Length == 0) return false;
        if (normalizedText.Contains(name)) return true;
        return textCandidates.Any(c => c.Length >= 4 && name.StartsWith(c, StringComparison.Ordinal));
    }
}
