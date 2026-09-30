using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Welche Form eine Verzeichnis-Kennung (<see cref="Models.TournamentDirectoryEntry.PublicId"/>)
/// haben darf — die EINE Pruefung fuer alles, was sie aus einer Adresse entgegennimmt (Detailseite,
/// Ausblenden, Falschmeldung).
///
/// <para>Die Formen, die die Erzeuger heute bauen:</para>
/// <list type="bullet">
/// <item>chess-results-Nummer: nur Ziffern (<c>1474416</c>, <see cref="TournamentDirectoryService"/>),</item>
/// <item>ein Buchstabe plus Nummer: FIDE-Kalender <c>f14805</c>
///   (<see cref="FideDirectorySweepService.PublicIdPrefix"/>), Ankuendigungskalender <c>k123</c>,</item>
/// <item>zwei Buchstaben plus Nummer der Quelle: <c>ie</c>, <c>hu</c>, <c>sk</c>, <c>en</c>, <c>fr</c>,
///   <c>ro</c>, <c>it</c>, <c>sl</c>,</item>
/// <item>zwei Buchstaben, Jahr, Bindestrich, Nummer: Polen <c>pl2026-4711</c>,</item>
/// <item>zwei Buchstaben plus 12 Hex-Zeichen (Kurzschluessel fuer Quellen ohne eigene Nummer):
///   <c>cz</c>, <c>nl</c>, <c>de</c>, <c>wl</c>, <c>ca</c>, <c>no</c>, <c>sc</c> (je <c>PublicIdOf</c>).</item>
/// </list>
///
/// <para><b>Warum hier und nicht mehr im Controller.</b> Dort stand bis 0.606.0
/// <c>^[a-z]?\d{1,10}$</c> — gebaut, als es nur chess-results und den FIDE-Kalender gab. Die
/// Verbandskalender kamen danach, und 16 ihrer Formen fielen durch: Detailseite, Ausblenden und
/// Melden antworteten 400 (Prod 2026-09-29: 2 811 Eintraege). <c>DirectoryPublicIdTests</c> haelt
/// jeden Erzeuger gegen diese Pruefung — wer eine Quelle mit einer neuen Form baut, sieht es dort.</para>
///
/// <para>Bewusst eng: keine Grossbuchstaben (die Erzeuger schreiben klein), kein <c>/</c>, kein
/// <c>.</c>, hoechstens <see cref="MaxLength"/> Zeichen wie die Spalte. Drei Buchstaben ohne Ziffer
/// („abc") oder ein ausgeschriebenes Kuerzel („fide-14805") sind keine Kennung.</para>
/// </summary>
public static class DirectoryPublicId
{
    /// <summary>Laenge der Spalte <c>PublicId</c> (<c>[MaxLength(24)]</c>).</summary>
    public const int MaxLength = 24;

    // [0-9] statt \d: \d nimmt in .NET jede Unicode-Ziffer (auch arabisch-indische). \z statt $:
    // $ liesse ein abschliessendes Zeilenende durch („123\n").
    private static readonly Regex Pattern = new(
        @"^(?:[a-z]{0,2}[0-9]{1,10}|[a-z]{2}[0-9]{4}-[0-9]{1,10}|[a-z]{2}[0-9a-f]{12})\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Hat <paramref name="id"/> die Form einer Verzeichnis-Kennung?</summary>
    public static bool IsValid(string? id) =>
        id is { Length: > 0 and <= MaxLength } && Pattern.IsMatch(id);
}
