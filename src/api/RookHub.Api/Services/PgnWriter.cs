using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Die Mechanik des PGN-SCHREIBENS, einmal: Header-Werte escapen, eine Tag-Zeile bauen, aus einer
/// SAN-Liste den Zugtext mit Zugnummern setzen.
///
/// <para>Vier Stellen bauen PGN-Text, und jede hatte ihr eigenes <c>Escape</c> dabei
/// (<see cref="CoursePgnExporter"/>, <see cref="SharedLineService"/>, <see cref="SavedGameService"/>,
/// <see cref="ChessableReviewParser"/>). Drei davon meinten dasselbe: erst den Backslash, dann das
/// Anführungszeichen — in DIESER Reihenfolge, sonst verdoppelt der zweite Durchgang die Backslashes,
/// die der erste gerade gesetzt hat.</para>
///
/// <para>Was hier BEWUSST nicht vereinheitlicht wird, ist die FORM der Ausgabe: die vier schreiben
/// verschieden (mit und ohne <c>[Site]</c>, mit und ohne Zeilenumbruch am Ende, mit und ohne
/// Ergebnis-Token am Zugtext), und diese Unterschiede sind Bestand — sie stehen in geteilten Links
/// und in gespeicherten Partien. <see cref="MoveText"/> bekommt sie deshalb als Parameter statt sie
/// anzugleichen. <see cref="SavedGameService"/> schreibt seine Header-Werte weiterhin selbst: es
/// ESCAPED das Anführungszeichen nicht, sondern ersetzt es durch ein Apostroph — eine andere
/// Entscheidung, kein zweiter Versuch derselben.</para>
/// </summary>
public static class PgnWriter
{
    /// <summary>Ein Wert, wie er zwischen die Anführungszeichen einer PGN-Tag-Zeile darf: erst der
    /// Backslash, DANN das Anführungszeichen (umgekehrt verdoppelte der zweite Durchgang die gerade
    /// gesetzten Backslashes).</summary>
    public static string Escape(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Eine Tag-Zeile <c>[Name "Wert"]</c> samt Zeilenumbruch; der Wert wird escaped.</summary>
    public static string Tag(string name, string? value) => $"[{name} \"{Escape(value)}\"]\n";

    /// <summary>Macht Text kommentartauglich: die schließende Klammer ersetzen (sie würde den
    /// Kommentar beenden), Whitespace auf einfache Leerzeichen glätten.</summary>
    public static string CleanComment(string s)
        => string.Join(' ', s.Replace('}', ')').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Der Zugtext: Zugnummern, Kommentare, Ergebnis.
    /// </summary>
    /// <param name="sans">Die Halbzüge in SAN.</param>
    /// <param name="startFen">Ausgangsstellung; <c>null</c> = Grundstellung (Weiß am Zug, Zug 1).
    /// Aus ihr kommen die Seite am Zug und die Zugnummer — ab einer FEN mit Schwarz am Zug beginnt
    /// der Text also mit „12... Nf6 13. …".</param>
    /// <param name="comments">Kommentar HINTER dem Halbzug mit diesem Index; der Schlüssel
    /// <c>-1</c> ist die Einleitung vor dem ersten Zug (dieselbe Zählung wie
    /// <c>BookPuzzle.MoveComments</c>).</param>
    /// <param name="result">Ergebnis-Token am Ende. <c>null</c> heißt „keins" — der Text kommt dann
    /// OHNE abschließendes Leerzeichen zurück und der Aufrufer hängt sein Ende selbst an. Das ist
    /// kein Schönheitsunterschied: bei einer zug- und kommentarlosen Linie liefert der
    /// Kurs-Export so <c>" *"</c>, die anderen beiden <c>"*"</c>, und beides steht so in Bestand.</param>
    /// <param name="before">Roher Text (inklusive Klammern) unmittelbar VOR dem Halbzug mit diesem
    /// Index — heute der <c>[%tqu]</c>-Trainingsmarker des Kurs-Exports. Er wird NICHT durch
    /// <see cref="CleanComment"/> geschickt: er ist kein Prosa-Kommentar, sondern eine Auszeichnung.
    /// Ein Schwarz-Zug direkt dahinter bekommt seine Zugnummer mit „…" — ohne sie kann ein strenger
    /// PGN-Leser ihn nicht einordnen (gleiche Regel wie in piratechess).</param>
    public static string MoveText(
        IReadOnlyList<string> sans,
        string? startFen = null,
        IReadOnlyDictionary<int, string>? comments = null,
        string? result = "*",
        IReadOnlyDictionary<int, string>? before = null)
    {
        var parts = (startFen ?? string.Empty).Split(' ');
        bool white = parts.Length < 2 || parts[1] != "b";
        int no = parts.Length >= 6 && int.TryParse(parts[5], out var fm) && fm > 0 ? fm : 1;

        var sb = new StringBuilder();
        bool first = true;

        if (comments != null && comments.TryGetValue(-1, out var intro) && !string.IsNullOrWhiteSpace(intro))
            sb.Append($"{{{CleanComment(intro)}}} ");

        for (int i = 0; i < sans.Count; i++)
        {
            var markerHere = before != null && before.TryGetValue(i, out var marker) && !string.IsNullOrEmpty(marker);
            if (markerHere) sb.Append(before![i]).Append(' ');

            if (white) sb.Append($"{no}. {sans[i]} ");
            else { sb.Append(first || markerHere ? $"{no}... {sans[i]} " : $"{sans[i]} "); no++; }

            if (comments != null && comments.TryGetValue(i, out var cm) && !string.IsNullOrWhiteSpace(cm))
                sb.Append($"{{{CleanComment(cm)}}} ");

            white = !white;
            first = false;
        }

        return result is null ? sb.ToString().TrimEnd() : sb.Append(result).ToString();
    }
}
