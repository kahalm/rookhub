using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Der „rote Faden" einer Partie-Zusammenfassung (Wunsch 2026-11-25: „der rote Faden in der Summary wäre ganz oben toll — und in der
/// Vorschau vom Link"). Die EINE Stelle, die ihn aus dem Markdown zieht: die Seite stellt ihn nach oben (Feld <c>redThread</c> der
/// Zusammenfassungs-Antworten, <c>docs/api/partie-zusammenfassung.md</c>), die Link-Vorschau nimmt ihn als <c>og:description</c>
/// (<c>docs/api/link-vorschau.md</c>).
/// <para>Treffer ist nur ein Absatz, der (1) der LETZTE Absatz eines Hauptabschnitts der Partie ist (<c>## Die Partie</c>, ältere Texte
/// <c>## Zusammenfassung</c>; englisch <c>## The game</c>/<c>## Summary</c>) — so schreibt ihn die Anweisung des Watchers seit jeher, (2) keinen
/// Partiezug mit Zugnummer enthält (<c>12.Sd3</c>, <c>12...b6</c>) und (3) den roten Faden im ERSTEN Satz nennt: „Der rote Faden …",
/// „Zwei rote Fäden …", „Roter Faden aus schwarzer Sicht …", „Drei Fäden ziehen sich durch die Partie …", „Durch die ganze Partie zieht sich …" (alle Fassungen von Prod,
/// 25.11.: <c>SummaryRedThreadTests</c>). Ein „zweiter"/„erster" Faden allein ist kein Treffer (er gehört zu einem Absatz davor).
/// Kein Treffer → <c>null</c>; die Seite zeigt den Text dann unverändert.</para>
/// </summary>
public static class SummaryRedThread
{
    /// <summary>Hauptabschnitte, deren letzter Absatz der rote Faden sein darf.</summary>
    private static readonly Regex GameSection = new(@"^##\s+(?:Die\s+Partie|Partieverlauf|Zusammenfassung|The\s+game|Summary)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Ein Partiezug mit Zugnummer, deutsch oder englisch: <c>12.Sd3</c>, <c>12. Sd3</c>, <c>12...b6</c>, <c>12…b6</c>, <c>9.O-O</c>.</summary>
    private static readonly Regex NumberedMove = new(@"\b\d{1,3}\s*(?:\.\.\.|…|\.)\s*(?:O-O(?:-O)?|0-0(?:-0)?|[KDTLSQRBN]?[a-h]?[1-8]?x?[a-h][1-8])",
        RegexOptions.CultureInvariant);

    /// <summary>Der rote Faden im Satz: „rote(r/n) Faden/Fäden", „Fäden ziehen sich …", „Drei Motive ziehen sich durch die Partie",
    /// „Durch die ganze Partie zieht sich …", englisch „common/red thread".</summary>
    private static readonly Regex Phrase = new(@"\brote[nr]?\s+F[aä]den?\b|\bF[aä]den?\s+(?:zieh|zog)|\bzieh(?:t|en)\s+sich\s+durch\b|\bdurch\s+(?:die\s+ganze\s+)?Partie\s+zieh(?:t|en)\s+sich\b|\b(?:common|red|main)\s+threads?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>„Der zweite rote Faden …" — Fortsetzung eines Absatzes davor, allein nach oben gestellt ergäbe er keinen Sinn.</summary>
    private static readonly Regex Ordinal = new(@"\b(?:erste|zweite|dritte|vierte|first|second|third)[nr]?\s+(?:(?:rote[nr]?|red|common)\s+)?(?:F[aä]den?|threads?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Zeilen, mit denen eine Liste, ein Zitat, eine Tabelle oder eine Trennlinie beginnt (wie <c>parseSummary</c> der Seite).</summary>
    private static readonly Regex NotParagraph = new(@"^(?:[-*+]\s|\d+[.)]\s|>|\||(?:-{3,}|\*{3,}|_{3,})$)", RegexOptions.CultureInvariant);

    /// <summary>Satzende für den „ersten Satz" (Doppelpunkt zählt nicht: „Der rote Faden: …").</summary>
    private static readonly Regex SentenceEnd = new(@"[.!?](?:\s|$)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Der Absatz mit dem roten Faden, Zeile für Zeile wie im Markdown (jede Zeile ohne Leerraum an den Enden, mit <c>\n</c>
    /// verbunden), oder <c>null</c>. Die Seite entfernt genau diese Zeilen an der alten Stelle.
    /// </summary>
    public static string? Extract(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string? best = null;
        var inGameSection = false;
        List<string>? lastPara = null;   // letzter Absatz des laufenden Abschnitts (null, wenn danach etwas anderes kam)
        var para = new List<string>();

        void EndSection()
        {
            if (inGameSection && lastPara is { Count: > 0 } && IsRedThread(lastPara)) best = string.Join("\n", lastPara);
            lastPara = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0)
            {
                if (para.Count > 0) { lastPara = para; para = []; }
                continue;
            }
            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                if (para.Count > 0) para = [];
                lastPara = null;   // ein Diagramm o. Ä. hinter dem Absatz: der ist nicht mehr der letzte
                var end = i + 1;
                while (end < lines.Length && !lines[end].Trim().StartsWith("```", StringComparison.Ordinal)) end++;
                if (end >= lines.Length)
                {
                    // wie die Seite (F2-018): ein offener Zaun endet an der nächsten Leerzeile
                    end = i + 1;
                    while (end < lines.Length && lines[end].Trim().Length > 0) end++;
                }
                i = end;
                continue;
            }
            if (t.StartsWith('#'))
            {
                if (para.Count > 0) { lastPara = para; para = []; }
                // Eine Unterüberschrift (###) beendet den Abschnitt nicht, aber ihr Absatz davor ist dann nicht mehr der letzte.
                if (t.StartsWith("###", StringComparison.Ordinal)) { lastPara = null; continue; }
                EndSection();
                inGameSection = GameSection.IsMatch(t);
                continue;
            }
            // Listen, Zitate, Tabellen, Trennlinien sind keine Absätze
            if (para.Count == 0 && NotParagraph.IsMatch(t))
            {
                lastPara = null;
                continue;
            }
            para.Add(t);
        }
        if (para.Count > 0) lastPara = para;
        EndSection();
        return best;
    }

    private static bool IsRedThread(List<string> para)
    {
        var text = string.Join(" ", para);
        if (NumberedMove.IsMatch(text)) return false;
        var end = SentenceEnd.Match(text);
        var first = end.Success ? text[..end.Index] : text;
        return Phrase.IsMatch(first) && !Ordinal.IsMatch(first);
    }

    /// <summary>
    /// Der Absatz als schlichter Text für Vorschau und Beschreibung: ohne <c>**</c>/<c>*</c>/<c>_</c>/<c>`</c>, Links als ihr Text,
    /// Zeilen mit Leerzeichen verbunden — auf <paramref name="max"/> Zeichen gekürzt (am Wortende, mit „…").
    /// </summary>
    public static string Plain(string paragraph, int max = 200)
    {
        var s = paragraph.Replace('\n', ' ');
        s = Regex.Replace(s, @"\[([^\]\n]+)\]\([^()\s]+\)", "$1");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
        s = Regex.Replace(s, @"`([^`]+)`", "$1");
        s = Regex.Replace(s, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "$1");
        s = Regex.Replace(s, @"(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)", "$1");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length <= max) return s;
        var cut = s[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > max / 2) cut = cut[..space];
        return cut.TrimEnd(' ', ',', ';', ':', '–', '-') + "…";
    }
}
