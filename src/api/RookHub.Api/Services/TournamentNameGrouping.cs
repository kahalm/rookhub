using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Erkennt Gruppen EINES Turniers, die chess-results als eigenstaendige Zeilen fuehrt
/// („Open Braunau 2026 A/B/C", „… (Gruppe A)").
///
/// Zwei Beobachtungen aus den echten Daten steuern das Vorgehen:
///  - chess-results KUERZT den Namen in der Trefferliste (gemessen bei ~50 Zeichen). Bei
///    „5° Torneo Internazionale Ortisei "ad Gredine" - Op" faellt der unterscheidende Zusatz
///    damit weg — die drei Gruppen haben schlicht denselben Namen. Gleicher Name plus gleicher
///    Termin und Ort ist also bereits das haeufigste Gruppen-Signal.
///  - Steht der Zusatz noch da, ist es ein KURZES Kuerzel am Ende (Buchstabe, Zahl, roemische
///    Ziffer, ggf. mit „Gruppe"/„Group"/„Gruppo" davor).
///
/// Bewusst konservativ: eine Wortmarke wie „Jugend" wird NICHT abgeschnitten. Zwei verschiedene
/// Turniere faelschlich zu verschmelzen versteckt eines davon — das ist teurer, als zwei Gruppen
/// getrennt stehen zu lassen. Die Gruppierung verlangt zusaetzlich identischen Termin, Ort und
/// Foederation (siehe <see cref="TournamentDirectoryService"/>).
/// </summary>
public static class TournamentNameGrouping
{
    /// <summary>Gruppen-Kuerzel in Klammern am Ende: „(A)", „(Gruppe B)", „(Gr. 2)".</summary>
    private static readonly Regex ParenthesisedMarker = new(
        @"\s*\(\s*(?:gruppe|gruppo|group|grupa|skupina|csoport|gr\.?|sez\.?|sezione)?\s*[a-h0-9]{1,2}\s*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>„… Gruppe A", „… Group 2", „… Skupina B" — mit ausgeschriebenem Wort davor.</summary>
    private static readonly Regex WordedMarker = new(
        @"[\s\-–]*(?:gruppe|gruppo|group|grupa|skupina|csoport|sezione)\s*[a-h0-9]{1,2}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>„… A-Turnier", „… Turnier B".</summary>
    private static readonly Regex TournamentLetter = new(
        @"[\s\-–]*(?:[a-h]\s*-\s*turnier|turnier\s*[a-h])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Nacktes Kuerzel am Ende: „… A", „… III", „… 2". Nur nach einem echten Wort davor, damit aus
    /// „Turnier A" nicht „Turnier" und aus einem einzeln stehenden „A" nicht der leere Name wird.
    /// </summary>
    private static readonly Regex BareMarker = new(
        @"(?<=\p{L}{2}|\p{Nd})[\s\-–]+(?:[A-H]|I{1,3}|IV|VI{0,3}|V)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Was nach dem Abschneiden uebrig bleiben KANN, ohne ein Turniername zu sein. „Gruppe A"
    /// wuerde sonst zu „Gruppe" — und alle „Gruppe X" beliebiger Veranstaltungen desselben Tages
    /// fielen in einen Topf.
    /// </summary>
    private static readonly Regex StubRemainder = new(
        @"^(?:gruppe|gruppo|group|grupa|skupina|csoport|sezione|sez|gr|turnier|tournament|torneo|open|cup)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Der Name ohne Gruppen-Kuerzel. Greift keine Regel, kommt der Name unveraendert zurueck —
    /// dann gruppieren nur wortgleiche Zeilen (der haeufige Fall bei gekuerzten Namen).
    /// </summary>
    public static string BaseName(string? name)
    {
        var value = Whitespace.Replace((name ?? string.Empty).Trim(), " ");
        if (value.Length == 0) return string.Empty;

        foreach (var pattern in new[] { ParenthesisedMarker, WordedMarker, TournamentLetter, BareMarker })
        {
            var stripped = pattern.Replace(value, string.Empty).TrimEnd(' ', '-', '–', ',', ':');
            // Nie auf einen Rumpf kuerzen, der nichts mehr aussagt.
            if (stripped.Length >= 4 && stripped.Length < value.Length && !StubRemainder.IsMatch(stripped))
                return stripped;
        }
        return value;
    }

    /// <summary>
    /// Was den Eintrag innerhalb seiner Gruppe unterscheidet („A", „Gruppe 2"). Leer, wenn der
    /// Name schon der Basisname ist — dann hat chess-results den Zusatz abgeschnitten und es
    /// gibt schlicht keine Beschriftung.
    /// </summary>
    public static string GroupLabel(string? name)
    {
        var value = Whitespace.Replace((name ?? string.Empty).Trim(), " ");
        var baseName = BaseName(value);
        if (baseName.Length == 0 || baseName.Length >= value.Length) return string.Empty;

        return value[baseName.Length..].Trim(' ', '-', '–', ',', ':', '(', ')');
    }
}
