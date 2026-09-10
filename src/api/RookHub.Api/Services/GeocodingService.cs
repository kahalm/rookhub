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

        // Adresse oder Ortsliste? Eine ZIFFER im Text entscheidet das ueberraschend zuverlaessig:
        // eine Liste von Spielorten nennt Ortsnamen („Mayrhofen / St. Veit/Glan", „Bad Haering/
        // Schwaz/Jenbach/Absam/Kufstein"), eine Adresse hat eine Hausnummer. Am Dev-Stand
        // nachgemessen: von 262 als mehrortig erkannten Eintraegen hatten 191 eine Ziffer — und
        // waren durchweg Adressen, die die Zerlegung zerschnitten hat („Festsaal der Gemeinde
        // Schwarzach, Marktplatz 4, Schwarzach" wurde zu ZWEI Spielorten desselben Ortes). Die 71
        // ohne Ziffer waren echte Listen.
        var looksLikeAddress = locationText?.Any(char.IsDigit) == true;

        var bySegment = new List<GeocodeResult>();
        var ambiguous = false;
        foreach (var segment in GeoTextNormalizer.VenueSegments(locationText))
        {
            // Der Schraegstrich ist zweideutig (siehe ResolveSlashJoinedNameAsync): erst wird
            // geprueft, ob eine Wortfolge UEBER ihn hinweg einen Ort benennt — dann ist es einer.
            // Nur wenn nicht, wird zerlegt.
            var joined = segment.Contains('/', StringComparison.Ordinal)
                ? await ResolveSlashJoinedNameAsync(segment, iso2, ct)
                : null;
            var parts = joined is null && segment.Contains('/', StringComparison.Ordinal)
                ? GeoTextNormalizer.SlashParts(segment)
                : [segment];

            foreach (var part in parts)
            {
                var hit = joined ?? await ResolveByPlaceNameAsync(part, iso2, ct);
                if (hit is null) continue;
                if (hit.Source == GeoSource.Ambiguous) { ambiguous = true; continue; }
                if (bySegment.Any(v => GeoDistance.Haversine(v.Lat, v.Lon, hit.Lat, hit.Lon) < SameTownKm)) continue;
                bySegment.Add(hit);
            }
        }

        // Bei einer Adresse gilt der LETZTE Treffer: in „Rathauskeller, Hauptplatz 40,
        // Haid/Ansfelden" und „Karl-Marx-Schule, Plauen, Forststr. 60" steht vorn das Gebaeude
        // und die Strasse, der Ort weiter hinten. Zerlegt wird trotzdem — nur eben zu EINEM
        // Spielort; ohne die Zerlegung bildete die Kandidatensuche wieder Wortfolgen ueber die
        // Kommas hinweg.
        if (looksLikeAddress && bySegment.Count > 1) return [bySegment[^1]];
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
        var transcribedText = GeoTextNormalizer.NormalizeTranscribed(locationText, iso2);
        var textCandidates = GeoTextNormalizer.PlaceCandidatePairs(locationText, iso2);
        var confirmed = matches
            .Where(m => Confirms(normalizedText, transcribedText, textCandidates, m))
            .ToList();

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
        // BEIDE Schreibweisen: „Muenchen" im Text findet „München" im Lexikon nur ueber die
        // Umschrift-Spalte, ein kyrillischer Ortstext ausschliesslich darueber.
        var pairs = GeoTextNormalizer.PlaceCandidatePairs(locationText, iso2);
        if (pairs.Count == 0) return null;

        var normalized = pairs.Select(p => p.Normalized).Where(n => n.Length > 0).ToList();
        var transcribed = pairs.Select(p => p.Transcribed).Where(n => n.Length > 0).ToList();

        var query = _db.GeoPlaces.AsNoTracking().Where(
            g => normalized.Contains(g.NameNormalized) || transcribed.Contains(g.NameTranscribed));
        if (iso2 is not null) query = query.Where(g => g.Country == iso2);

        var matches = await query.ToListAsync(ct);
        if (matches.Count == 0) return null;

        // PlaceCandidatePairs liefert die laengsten Wortfolgen zuerst: "bad ischl" muss "ischl"
        // schlagen, sonst landet der Pin im falschen Ort.
        var index = pairs.FindIndex(p => matches.Any(m => Hits(m, p)));
        if (index < 0) return null;

        var winner = pairs[index];
        var group = matches.Where(m => Hits(m, winner)).ToList();
        return await ChooseAsync(group, locationText, ct);
    }

    /// <summary>
    /// Beschreibt ein Abschnitt MIT Schraegstrich einen einzigen Ort?
    ///
    /// <para>Der Schraegstrich ist zweideutig: „Schwaz/Jenbach/Kufstein" sind drei Spielorte,
    /// „St. Veit/Glan" und „Frankfurt/M" sind einer — dort kuerzt chess-results die Bindewoerter
    /// des amtlichen Namens weg („St. Veit AN DER Glan", „Frankfurt AM Main").</para>
    ///
    /// <para>Entschieden wird das ueber Wortfolgen, die den Schraegstrich UEBERSPANNEN: nur sie
    /// pruefen die Frage „ist das ein Name". Eine Wortfolge, die ganz innerhalb eines Teils
    /// liegt, beweist nichts — „schwaz" trifft, aber „Schwaz/Jenbach/Kufstein" ist deswegen
    /// nicht ein Ort. Genau daran ist die erste Fassung dieser Pruefung gescheitert.</para>
    ///
    /// <para><c>null</c> heisst „kein ueberspannender Treffer" — dann wird zerlegt.</para>
    /// </summary>
    private async Task<GeocodeResult?> ResolveSlashJoinedNameAsync(
        string segment, string? iso2, CancellationToken ct)
    {
        var parts = GeoTextNormalizer.SlashParts(segment)
            .Select(p => $" {GeoTextNormalizer.Normalize(p)} ")
            .ToList();
        if (parts.Count < 2) return null;

        var spanning = GeoTextNormalizer.PlaceCandidates(segment)
            .Where(c => !parts.Any(p => p.Contains($" {c} ", StringComparison.Ordinal)))
            .Take(MaxAbbreviationLookups)
            .ToList();
        if (spanning.Count == 0) return null;

        var exact = await Materialize(
            _db.GeoPlaces.AsNoTracking().Where(g => spanning.Contains(g.NameNormalized)), iso2, ct);
        var winner = spanning.FirstOrDefault(c => exact.Any(m => m.NameNormalized == c));
        if (winner is not null)
            return await ChooseAsync(exact.Where(m => m.NameNormalized == winner).ToList(), segment, ct);

        foreach (var candidate in spanning)
        {
            var abbreviated = await ResolveAbbreviatedNameAsync(candidate, iso2, ct);
            if (abbreviated is not null) return await ChooseAsync(abbreviated, segment, ct);
        }
        return null;
    }

    private async Task<List<GeoPlace>> Materialize(
        IQueryable<GeoPlace> query, string? iso2, CancellationToken ct)
    {
        if (iso2 is not null) query = query.Where(g => g.Country == iso2);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Wie viele Kandidaten hoechstens als ABKUERZUNG nachgeschlagen werden. Jeder kostet eine
    /// Abfrage; die laengsten stehen vorn, und ab dem vierten wird es nicht mehr besser.
    /// </summary>
    private const int MaxAbbreviationLookups = 4;

    /// <summary>Kuerzeste Laenge eines Wortanfangs, mit dem gesucht wird — „st" allein traefe zu viel.</summary>
    private const int MinAbbreviationPrefix = 4;

    /// <summary>
    /// Ein ABGEKUERZTER Ortsname: „St. Veit/Glan" fuer „St. Veit an der Glan", „Frankfurt/M" fuer
    /// „Frankfurt am Main". chess-results laesst die Bindewoerter weg, und der so entstandene
    /// Name steht in keinem Lexikon.
    ///
    /// <para>Gesucht wird mit allen Woertern AUSSER dem letzten als Wortanfang — das ist der
    /// Teil, der unverkuerzt bleibt („st veit", „frankfurt") — und die Kandidaten werden danach
    /// Wort fuer Wort geprueft (siehe <c>GeoTextNormalizer.DescribesSamePlace</c>).</para>
    ///
    /// <para>Warum das noetig ist: „Vereinstreff St. Veit/Glan" wurde am Schraegstrich zerlegt,
    /// „St. Veit" gibt es im Lexikon genau EINMAL — in Tirol — und der Eintrag sah damit
    /// vollkommen eindeutig aus. Der Pin sass 250 km entfernt im falschen Bundesland, und
    /// nichts daran war als Zweifelsfall erkennbar (tnr1351833).</para>
    /// </summary>
    private async Task<List<GeoPlace>?> ResolveAbbreviatedNameAsync(
        string candidate, string? iso2, CancellationToken ct)
    {
        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return null;

        var prefix = string.Join(' ', words[..^1]);
        if (prefix.Length < MinAbbreviationPrefix) return null;

        var query = _db.GeoPlaces.AsNoTracking().Where(g => g.NameNormalized.StartsWith(prefix));
        if (iso2 is not null) query = query.Where(g => g.Country == iso2);

        var group = (await query.Take(MaxAbbreviationCandidates).ToListAsync(ct))
            .Where(g => GeoTextNormalizer.DescribesSamePlace(candidate, g.NameNormalized))
            .ToList();

        return group.Count > 0 ? group : null;
    }

    /// <summary>Deckel fuer die Wortanfangs-Suche — ein kurzer Anfang trifft sonst halbe Laender.</summary>
    private const int MaxAbbreviationCandidates = 200;

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
        // Eine REGION ist kein Spielort. Steht in derselben Namensgruppe auch nur EIN Ort, haben
        // die Regionszeilen hier nichts zu entscheiden — sie sind die Mitte eines Gebiets und
        // liegen zwangslaeufig woanders als die Stadt, nach der es benannt ist.
        //
        // Aufgefallen am 2026-09-10 in Weissrussland: 63 der 100 Eintraege mit kyrillischem
        // Ortstext standen auf „mehrdeutig", obwohl Stadt und Postleitzahl-Zeile auf denselben
        // Punkt zeigen. „Витебск" fand drei Zeilen — Postleitzahl und Stadt bei 55,190/30,205 und
        // die GLEICHNAMIGE Oblast-Mitte 78 km entfernt. Damit war die Streuung groesser als eine
        // Stadt, die Turnierdichte entschied nicht, und es gab keinen Pin. Bei „Гродно" waren es
        // 90 km. In Russland, Weissrussland und der Ukraine heissen die Gebiete nach ihrer
        // Hauptstadt, der Fall ist dort also die Regel und nicht die Ausnahme.
        //
        // Sichtbar wurde das erst durch die Umschrift (0.456.0): vorher trugen die kyrillischen
        // Regionszeilen einen LEEREN Suchnamen und konnten gar nicht gefunden werden. Der
        // Rueckfall auf die Regionsmitte bleibt erhalten — er laeuft ueber das Feld `state`
        // (ResolveByRegionAsync) und ueber Gruppen, die NUR aus Regionen bestehen.
        if (group.Count > 1 && group.Any(g => g.Kind != GeoPlaceKind.Region))
            group = [.. group.Where(g => g.Kind != GeoPlaceKind.Region)];

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
        var transcribed = GeoTextNormalizer.NormalizeTranscribed(state, iso2);
        // Ein kyrillischer Regionsname ergibt in der ersten Form NICHTS — dann traegt die zweite.
        if (normalized.Length < 3 && transcribed.Length < 3) return null;

        var region = await _db.GeoPlaces.AsNoTracking()
            .Where(g => g.Country == iso2 && g.Kind == GeoPlaceKind.Region
                        && (g.NameNormalized == normalized || g.NameTranscribed == transcribed))
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
    /// <summary>
    /// Steht der Ortsname des Treffers auch im Text? Geprueft in BEIDEN Schreibweisen — „Muenchen"
    /// im Text bestaetigt „München" im Lexikon nur ueber die Umschrift.
    /// </summary>
    private static bool Confirms(string normalizedText, string transcribedText,
        List<(string Normalized, string Transcribed)> textCandidates, GeoPlace hit)
    {
        var name = GeoTextNormalizer.Normalize(hit.Name);
        var transcribedName = hit.NameTranscribed.Length > 0
            ? hit.NameTranscribed
            : GeoTextNormalizer.NormalizeTranscribed(hit.Name, hit.Country);

        // Ein Name OHNE BUCHSTABEN bestaetigt nichts. Die russischen Postleitzahl-Zeilen heissen
        // teils „Москва 194" — in der ersten Suchform bleibt davon die nackte Zahl `194` uebrig,
        // weil Kyrillisch dort wegfaellt. Wuerde die als Ortsname gelten, bestaetigte sie im Text
        // eine HAUSNUMMER, und der Postleitzahl-Weg haette genau den Fehler, gegen den seine
        // Bestaetigung gebaut ist. (Hinweis der zweiten Instanz beim Einspielen der russischen
        // Postleitzahlen, 2026-09-09.)
        var usable = HasLetter(name) ? name : "";
        var usableTranscribed = HasLetter(transcribedName) ? transcribedName : "";

        // Ein Treffer OHNE vergleichbaren Namen kann die Bestaetigung nie verdienen — bis 0.456.0
        // war das stillschweigend ein Nein, und fuer jede Schrift, die die Umschrift nicht abdeckt
        // (Georgisch, Armenisch, Hebraeisch) blieb der Postleitzahl-Weg damit wirkungslos. Dann
        // entscheidet die LAENGE der Ziffernfolge: eine Hausnummer hat selten vier Stellen, eine
        // Postleitzahl fast immer.
        if (usable.Length == 0 && usableTranscribed.Length == 0)
            return (hit.PostalCode?.Length ?? 0) >= 4;

        if (usable.Length > 0 && normalizedText.Contains(usable)) return true;
        if (usableTranscribed.Length > 0 && transcribedText.Contains(usableTranscribed)) return true;

        return textCandidates.Any(c =>
            (c.Normalized.Length >= 4 && usable.Length > 0
                && usable.StartsWith(c.Normalized, StringComparison.Ordinal))
            || (c.Transcribed.Length >= 4 && usableTranscribed.Length > 0
                && usableTranscribed.StartsWith(c.Transcribed, StringComparison.Ordinal)));
    }

    private static bool HasLetter(string value) => value.Any(char.IsAsciiLetter);

    /// <summary>Trifft dieser Kandidat den Eintrag — in der einen oder der anderen Schreibweise?</summary>
    private static bool Hits(GeoPlace place, (string Normalized, string Transcribed) candidate) =>
        (candidate.Normalized.Length > 0 && place.NameNormalized == candidate.Normalized)
        || (candidate.Transcribed.Length > 0 && place.NameTranscribed == candidate.Transcribed);
}
