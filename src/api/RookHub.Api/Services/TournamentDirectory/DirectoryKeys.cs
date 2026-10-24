using System.Security.Cryptography;
using System.Text;
using RookHub.Api.Models;
using RookHub.Api.Services.Tournaments;
using static RookHub.Api.Services.TournamentDirectory.DirectoryText;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Die abgeleiteten Schluessel eines Verzeichniseintrags: Gruppenschluessel (die Gruppen EINES
/// Turniers als eine Karte), Aenderungs-Hash (Termin und Ort, Grundlage der Aenderungsmeldung) und
/// die Termin-Darstellung in den Meldungen.
///
/// <para>Herausgezogen aus <see cref="TournamentDirectoryService"/> (Codereview 2026-09-29, Z8); dort
/// stehen dieselben Namen als Weiterleitung, damit bestehende Aufrufer unveraendert bleiben.</para>
/// </summary>
public static class DirectoryKeys
{
    /// <summary>
    /// Setzt Basisname und Gruppenschluessel aus den bereits gefuellten Feldern des Eintrags.
    /// Eine Stelle fuer beides, weil der Schluessel den Basisnamen braucht — und weil das
    /// Nachtragen im Altbestand (<see cref="TournamentGroupingBackfillService"/>) genau dieselbe
    /// Rechnung machen MUSS, sonst gruppiert der Bestand anders als der naechste Sweep.
    /// </summary>
    internal static void ApplyGrouping(TournamentDirectoryEntry entry)
    {
        entry.BaseName = Truncate(TournamentNameGrouping.BaseName(entry.Name), 500);
        entry.GroupKey = ComputeGroupKey(entry);
    }

    /// <summary>
    /// Schluessel, der die Gruppen EINES Turniers zusammenfasst: Basisname, Foederation, Termin und
    /// Ort muessen uebereinstimmen. Alle vier zusammen, weil der Name allein nicht reicht — ein
    /// Vereinsabend, der jede Woche „KK Bardejov" heisst, waere sonst ein einziger Eintrag.
    /// </summary>
    internal static string ComputeGroupKey(TournamentDirectoryEntry entry)
    {
        var payload = string.Join('|',
            GroupText(entry.BaseName ?? entry.Name),
            entry.Federation ?? "",
            entry.StartDate?.ToString("yyyy-MM-dd") ?? "",
            entry.EndDate?.ToString("yyyy-MM-dd") ?? "",
            GroupPlace(entry));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()[..32];
    }

    /// <summary>
    /// Der Ortsanteil des Gruppenschluessels: der VERORTETE Ort, wo es einen verlaesslichen gibt,
    /// sonst der normalisierte Ortstext.
    ///
    /// <para>UI-Sweep 2026-10-11 (r2-t-dup-cards): bis dahin zaehlte nur der ROHE Ortstext, und
    /// chess-results laesst ihn je Gruppe anders schreiben — „Otáhalova 1260 Varnsdorf" (Gruppe A
    /// und B) gegen „Varnsdorf Otáhalova 1260" (Gruppe C) desselben Turniers standen als zwei
    /// Karten in der Liste. Auf Prod (Stand 2026-10-11) betraf das 65 Turniere mit 133
    /// Teil-Schluesseln. Der Ortsname aus dem Lexikon ist fuer alle Schreibweisen derselbe.</para>
    ///
    /// <para>Verlaesslich heisst: ein Treffer auf Ortsebene (PLZ, Ort, von Hand, Quelle,
    /// Vereinsnamen). Ein Bundesland-Zentroid (<see cref="GeoSource.Region"/>) ist zu grob — zwei
    /// Turniere gleichen Namens am selben Tag in verschiedenen Orten EINES Landes waeren sonst eins;
    /// mehrdeutige Treffer tragen ohnehin keinen Ort. Bewusst der NAME und nicht gerundete
    /// Koordinaten: PLZ- und Ortstreffer derselben Stadt liegen ein paar Kilometer auseinander,
    /// und jede Rasterung hat Kanten, an denen dieselbe Stadt in zwei Zellen faellt.</para>
    ///
    /// <para>Falsches Zusammenfassen bleibt unwahrscheinlich: Basisname, Foederation, Beginn und
    /// Ende muessen weiterhin gleich sein. Ist nur EINE Gruppe verortet, bleiben die Karten getrennt
    /// (die Oberflaeche zeigt dann den Hinweis „weitere Gruppe?") — getrennt lassen ist der
    /// billigere Fehler.</para>
    ///
    /// <para>Der Schluessel haengt damit an der Verortung, und die laeuft an vielen Stellen NACH
    /// <see cref="ApplyGrouping"/>. Deshalb zieht <see cref="TournamentGroupingBackfillService"/>
    /// beim Start und am Ende jedes Nachtlaufs jeden abweichenden Schluessel nach.</para>
    /// </summary>
    internal static string GroupPlace(TournamentDirectoryEntry entry)
    {
        if (entry.Lat is not null
            && entry.GeoSource is GeoSource.PostalCode or GeoSource.City or GeoSource.Manual
                or GeoSource.Nominatim or GeoSource.TeamHint or GeoSource.SourceProvided
            && !string.IsNullOrWhiteSpace(entry.GeoPlaceName))
        {
            return GroupText(entry.GeoPlaceName);
        }
        return GroupText(entry.LocationText);
    }

    /// <summary>
    /// Der Textanteil des Gruppenschluessels.
    ///
    /// <para><c>GeoTextNormalizer.Normalize</c> wirft alles weg, was nach der Diakritika-Zerlegung
    /// nicht <c>[a-z0-9]</c> ist — bei kyrillischer, griechischer oder ostasiatischer Schrift ist
    /// das der GANZE Text. Zwei verschiedene bulgarische Turniere am selben Ort und Termin haetten
    /// damit denselben Schluessel bekommen und waeren in Liste und Kalender zu EINEM verschmolzen;
    /// eines davon waere unsichtbar geworden. Genau der Fehler, den der Klassenkommentar von
    /// <see cref="TournamentNameGrouping"/> als den teureren bezeichnet — die Rotation faehrt alle
    /// 261 Foederationen ab, darunter BUL, GRE, RUS, UKR, SRB, GEO, ARM, CHN, JPN, KOR.</para>
    ///
    /// <para>Bleibt nach dem Normalisieren nichts uebrig, entscheidet deshalb der Rohtext
    /// (getrimmt, kleingeschrieben). Der ist weniger robust gegen Schreibvarianten — aber
    /// „zusammengefasst, was nicht zusammengehoert" ist der schlimmere Ausgang als „getrennt
    /// gelassen, was zusammengehoert".</para>
    /// </summary>
    private static string GroupText(string? value)
    {
        var normalized = GeoTextNormalizer.Normalize(value);
        return normalized.Length > 0 ? normalized : (value ?? "").Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Hash ueber genau die Felder, deren Aenderung eine Meldung wert ist: Termin und Spielort.
    /// Teilnehmerzahl, Schiedsrichter oder ein neuer "Last update"-Zeitstempel bleiben bewusst
    /// draussen - sonst meldet jede wachsende Meldeliste eine "Aenderung".
    /// </summary>
    internal static string ComputeChangeHash(DateOnly? start, DateOnly? end, string? location)
    {
        var payload = string.Join('|',
            start?.ToString("yyyy-MM-dd") ?? "",
            end?.ToString("yyyy-MM-dd") ?? "",
            (location ?? "").Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()[..32];
    }

    internal static string? FormatRange(DateOnly? start, DateOnly? end)
    {
        if (start is null && end is null) return null;
        if (start is null) return end!.Value.ToString("yyyy-MM-dd");
        if (end is null || end == start) return start.Value.ToString("yyyy-MM-dd");
        return $"{start.Value:yyyy-MM-dd} - {end.Value:yyyy-MM-dd}";
    }
}
