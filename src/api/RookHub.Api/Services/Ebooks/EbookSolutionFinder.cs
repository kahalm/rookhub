using System.Text;
using System.Text.RegularExpressions;
using Chess;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Eine Textzeile (ein Spaltenabschnitt) einer Seite, in Lesereihenfolge; Koordinaten in PDF-Punkten, oben = 0.</summary>
public sealed record PageLine(int Page, int Column, double Y0, double Y1, double X0, double X1, string Text);

/// <summary>Gefundene Lösung: Züge als UCI und SAN (Hauptlinie), der Absatz als Text, Seite, Zugnummer der Aufgabe.</summary>
public sealed record FoundSolution(IReadOnlyList<string> Uci, IReadOnlyList<string> San, string Text, int Page, int FirstMoveNumber, char Side);

/// <summary>
/// Sucht zu jedem Diagramm die Lösung im Buchtext (0.771.0) — und nimmt NUR, was sich ab der Diagrammstellung regelgerecht
/// nachspielen lässt (Gera.Chess). Zwei Wege:
/// <list type="number">
/// <item>Nummerierter Lösungsteil: ein Absatz, der mit „31. “ beginnt (Zahl, Punkt, LEERZEICHEN — „31.Txh4“ ist ein Zug),
/// gehört zur Aufgabe 31; gesucht wird hinter der Diagrammseite (Woodpecker: „Solutions to Easy Exercises“).</item>
/// <item>Lösung direkt nach dem Diagramm (Grooten: „White to move“, darunter „43.Bd3!!“): die Zeilen unter dem Brett in
/// derselben Spalte und die nächste Seite.</item>
/// </list>
/// Hauptlinie: ab dem ersten Zug, dessen Nummer zur Seite am Zug passt („34...“ bei Schwarz), weiter mit jeder Zugnummer,
/// die genau die erwartete ist; dazwischenliegende Prosa und Nebenvarianten (falsche Nummer, Klammern) werden übersprungen.
/// Figurinen (SP-Fonts: ¢ £ ¦ ¥ ¤; Unicode ♔–♘; RookHub-Figurinen U+E024…) werden zu K Q R B N.
/// </summary>
public static class EbookSolutionFinder
{
    private static readonly Regex EntryStart = new(@"^\s*(\d{1,4})\.\s+(?=\D)", RegexOptions.Compiled);
    private static readonly Regex Token = new(
        @"\(|\)|(?<num>\d{1,3})\.(?<dots>\s*(?:\.\.\.?|…))?|(?<san>(?:O-O-O|O-O|0-0-0|0-0|[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](?:=?[QRBN])?))(?<ann>[+#]?[!?]*)(?=[^a-zA-Z0-9]|$)|(?<word>[A-Za-zÄÖÜäöüß]{2,})",
        RegexOptions.Compiled);

    /// <summary>Figurinen und Schachzeichen → ASCII-Notation.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text)
        {
            // Symbol-Fonts liefern Figurinen oft im Private-Use-Bereich U+F0xx (PdfPig, ToUnicode fehlt) → zurückfalten.
            var c = raw >= '\uF020' && raw <= '\uF0FF' ? (char)(raw - 0xF000) : raw;
            sb.Append(c switch
            {
                '¢' or '♔' or '♚' or '' => 'K',
                '£' or '♕' or '♛' or '' => 'Q',
                '¦' or '♖' or '♜' or '' => 'R',
                '¥' or '♗' or '♝' or '' => 'B',
                '¤' or '♘' or '♞' or '' => 'N',
                '†' => '+',
                '‡' => '#',
                ' ' => ' ',
                _ => c,
            });
        }
        return sb.ToString();
    }

    /// <summary>Alle nummerierten Absätze des Buchs: Nummer → (Seite, Text), in Lesereihenfolge.</summary>
    public static Dictionary<int, List<(int Page, string Text)>> NumberedEntries(IReadOnlyList<PageLine> lines)
    {
        var result = new Dictionary<int, List<(int, string)>>();
        int? current = null; int page = 0; var sb = new StringBuilder(); var count = 0;
        void Flush()
        {
            if (current is int n && sb.Length > 0)
            {
                if (!result.TryGetValue(n, out var list)) result[n] = list = new();
                list.Add((page, sb.ToString().Trim()));
            }
            sb.Clear(); current = null; count = 0;
        }
        foreach (var l in lines)
        {
            var m = EntryStart.Match(l.Text);
            if (m.Success)
            {
                Flush();
                current = int.Parse(m.Groups[1].Value);
                page = l.Page;
                sb.Append(l.Text[m.Length..]).Append(' ');
                continue;
            }
            if (current == null) continue;
            if (++count > 40 || sb.Length > 3000) { Flush(); continue; }
            sb.Append(l.Text).Append(' ');
        }
        Flush();
        return result;
    }

    /// <summary>
    /// Hauptlinie aus einem Text ab der Stellung <paramref name="fen"/>. <paramref name="sideKnown"/>=false: Zugrecht aus
    /// der ersten Zugnummer nehmen („34...“ = Schwarz). <c>null</c>, wenn kein erster Zug regelgerecht spielbar ist.
    /// </summary>
    public static FoundSolution? TryParse(string fen, bool sideKnown, string rawText, int page)
        => TryParseAll(fen, sideKnown, rawText, page, 1).FirstOrDefault();

    /// <summary>
    /// Alle spielbaren Lösungskandidaten eines Textes in Textreihenfolge (höchstens <paramref name="max"/>) — der erste ist
    /// <see cref="TryParse"/>; die weiteren braucht die Engine-Prüfung, wenn der erste Zug sich als Nebenvariante erweist.
    /// </summary>
    public static List<FoundSolution> TryParseAll(string fen, bool sideKnown, string rawText, int page, int max = 6)
    {
        var result = new List<FoundSolution>();
        var text = Normalize(rawText);
        var parts = fen.Split(' ');
        if (parts.Length < 2) return result;
        var tokens = Token.Matches(text).Cast<Match>().ToList();
        var seen = new HashSet<string>();

        // Startzüge: Zugnummer + Punkte passend zur Seite am Zug, direkt gefolgt von einem SAN.
        int? doubted = null;   // Nummer eines angezweifelten Startzugs — die echte Lösung hat dieselbe Nummer
        for (var i = 0; i < tokens.Count - 1 && result.Count < max; i++)
        {
            var t = tokens[i];
            if (!t.Groups["num"].Success || !tokens[i + 1].Groups["san"].Success) continue;
            var side = t.Groups["dots"].Success ? 'b' : 'w';
            if (sideKnown && side != parts[1][0]) continue;
            var num = int.Parse(t.Groups["num"].Value);
            if (result.Count == 0)
            {
                // Ein angezweifelter Startzug („36.Sxf7+?! sieht wie eine Kombination aus …") ist nicht die Lösung — weitersuchen.
                if (doubted != null && num != doubted) continue;
                if (tokens[i + 1].Groups["ann"].Value.Contains('?')) { doubted ??= num; continue; }
            }
            else if (num != result[0].FirstMoveNumber || side != result[0].Side) continue;   // Alternativen: derselbe Zug
            var startFen = $"{parts[0]} {side} {(parts.Length > 2 ? parts[2] : "-")} - 0 {num}";
            var line = Follow(startFen, tokens, i, num, side);
            if (line == null)
            {
                if (sideKnown && result.Count == 0) return result;   // die Nummer passte, der Zug nicht → kein Raten
                continue;
            }
            if (!seen.Add(line.Value.Uci[0])) continue;
            result.Add(new FoundSolution(line.Value.Uci, line.Value.San, Clip(text.Trim(), 3000), page, num, side));
        }
        return result;
    }

    private static (List<string> Uci, List<string> San)? Follow(string fen, List<Match> tokens, int start, int number, char side)
    {
        ChessBoard board;
        try { board = ChessBoard.LoadFromFen(fen); }
        catch { return null; }
        var expectNum = number; var expectSide = side;
        var skipping = false; var depth = 0; var sanCount = 0;
        for (var i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var v = t.Value;
            if (v == "(") { depth++; continue; }
            if (v == ")") { if (depth > 0) depth--; continue; }
            if (depth > 0) continue;
            if (t.Groups["num"].Success)
            {
                var n = int.Parse(t.Groups["num"].Value);
                var s = t.Groups["dots"].Success ? 'b' : 'w';
                // Eine Nummer, die genau der erwartete Zug ist, setzt die Hauptlinie fort; jede andere eröffnet eine
                // Nebenvariante (z. B. „31.Txg8+ …" als Alternative zu „31.Dxg5"), die übersprungen wird.
                skipping = !(n == expectNum && s == expectSide);
                continue;
            }
            if (t.Groups["word"].Success)
            {
                // Prosa unterbricht nur: die Hauptlinie geht beim nächsten passenden Zug weiter. Schwarz-Antwort ohne
                // Nummer nach Prosa kommt in Büchern als „17...Kxh7" — also erst wieder mit Nummer.
                if (sanCount > 0) skipping = true;
                continue;
            }
            if (!t.Groups["san"].Success || skipping) continue;
            var san = t.Groups["san"].Value.Replace('0', 'O');
            bool ok;
            try { ok = board.Move(san); }
            catch { ok = false; }
            if (!ok)
            {
                if (sanCount == 0) return null;
                break;
            }
            sanCount++;
            if (expectSide == 'b') expectNum++;
            expectSide = expectSide == 'w' ? 'b' : 'w';
            if (sanCount >= 60) break;
        }
        if (sanCount == 0) return null;
        var uci = board.ExecutedMoves.Select(PgnParser.ToUci).ToList();
        var sans = board.ExecutedMoves.Select(m => m.San ?? string.Empty).ToList();
        return (uci, sans);
    }

    /// <summary>
    /// Ordnet allen Diagrammen ihre Lösung zu. <paramref name="diagramsWithBottom"/>: Diagramm samt Seite, Spalte und
    /// Unterkante (PDF-Punkte), damit Weg 2 die Zeilen darunter findet.
    /// </summary>
    public static Dictionary<int, FoundSolution> Match(
        IReadOnlyList<(int Index, int Page, int Column, double Bottom, string Fen, bool SideKnown, string? Label)> diagrams,
        IReadOnlyList<PageLine> lines)
    {
        var entries = NumberedEntries(lines);
        var byPage = lines.GroupBy(l => l.Page).ToDictionary(g => g.Key, g => g.ToList());
        var result = new Dictionary<int, FoundSolution>();
        foreach (var d in diagrams)
        {
            FoundSolution? found = null;
            // Weg 1: nummerierter Absatz HINTER der Diagrammseite (der Lösungsteil), sonst irgendwo.
            if (d.Label != null && int.TryParse(d.Label, out var n) && entries.TryGetValue(n, out var cands))
                foreach (var (page, text) in cands.OrderBy(c => c.Page > d.Page ? 0 : 1).ThenBy(c => c.Page))
                {
                    if (page == d.Page) continue;   // die Aufgabe selbst steht auf der Diagrammseite
                    found = TryParse(d.Fen, d.SideKnown, text, page);
                    if (found != null) break;
                }
            // Weg 2: Text direkt unter dem Brett (gleiche Spalte), dann die nächste Seite — nur die ersten Zeilen.
            if (found == null)
            {
                var after = new List<PageLine>();
                if (byPage.TryGetValue(d.Page, out var same))
                    after.AddRange(same.Where(l => l.Column == d.Column && l.Y0 >= d.Bottom - 2).OrderBy(l => l.Y0).Take(12));
                if (after.Count < 6 && byPage.TryGetValue(d.Page + 1, out var next))
                    after.AddRange(next.OrderBy(l => l.Column).ThenBy(l => l.Y0).Take(6));
                // Nur bis zum nächsten Diagramm-Abschnitt: eine Zeile „X to move" oder eine neue Aufgabennummer beendet.
                var text = string.Join(' ', after.Select(l => l.Text));
                var cut = Regex.Match(text, @"\b(White|Black|Weiß|Schwarz) (to move|to play|am Zug)\b.*", RegexOptions.IgnoreCase);
                if (cut.Success && cut.Index > 0) text = text[..cut.Index];
                found = TryParse(d.Fen, d.SideKnown, text, d.Page);
            }
            if (found != null) result[d.Index] = found;
        }
        return result;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
