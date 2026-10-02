namespace RookHub.Api.Services;

/// <summary>
/// Kleinteile der PGN-Zerlegung, die JEDER Zerleger des Servers gleich beantworten muss. Die
/// Zerleger selbst bleiben getrennt (eigene Semantik, siehe <see cref="PgnMoveTree"/>); was hier
/// steht, lag vorher als eigene Kopie in mehreren von ihnen und war auseinandergelaufen.
/// </summary>
public static class PgnTokens
{
    /// <summary>
    /// Schneidet von EINER Zeile den „;"-Zeilenkommentar ab (PGN-Norm: „;" bis Zeilenende) — aber
    /// nur außerhalb eines „{…}"-Kommentars, dort ist „;" gewöhnlicher Text. Umgekehrt zählt eine
    /// „{" HINTER dem „;" nicht als Kommentaranfang.
    ///
    /// <para>Muss ZEILENWEISE laufen, bevor ein Zerleger die Zeilen zusammenfügt: danach gibt es kein
    /// Zeilenende mehr, und „;…" fraß bis 0.624.0 im Repertoire-Parser die ganze restliche Partie
    /// (<c>1. e4 ; Königsbauer⏎1... e5 2. Nf3</c> ergab nur <c>e4</c>), während der Partie-Upload
    /// die Kommentarwörter als Züge las und dieselbe Datei als „illegal" ablehnte
    /// (Codereview 2026-09-29, A6-009).</para>
    /// </summary>
    /// <param name="line">Eine Zeile Movetext (ohne Zeilenumbruch).</param>
    /// <param name="braceDepth">Offene „{"-Klammern VOR dieser Zeile — der Rückgabewert der vorigen Zeile.</param>
    /// <returns>Der Zeilenteil vor dem Kommentar und die offenen „{"-Klammern an seinem Ende (nie unter 0;
    /// eine verirrte „}" wird nicht verschleppt).</returns>
    public static (string Text, int BraceDepth) StripLineComment(string line, int braceDepth)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '{') braceDepth++;
            else if (ch == '}') { if (braceDepth > 0) braceDepth--; }
            else if (ch == ';' && braceDepth == 0) return (line[..i], braceDepth);
        }
        return (line, braceDepth);
    }

    /// <summary>
    /// Ist das Token ein Partie-Ergebnis am Ende des Movetexts? EINE Liste fuer alle Zerleger
    /// (<see cref="PgnParser"/>, <see cref="PgnMoveTree"/>, <see cref="PermissiveSan"/>,
    /// <see cref="ReconstructionChain"/>, <see cref="LibraryGameReader"/>).
    ///
    /// <para>Bis 0.624.0 stand sie in fuenf eigenen Listen, und nur zwei kannten das nackte „1/2":
    /// <see cref="LibraryGameReader"/> zaehlte es als Halbzug (fuehrende Ziffer = „12.e4") — PlyCount
    /// eins zu hoch, anderer MovesHash, dieselbe Partie mit „1/2-1/2" blieb als Dublette unerkannt —,
    /// und <see cref="ReconstructionChain"/> machte daraus einen Zug, an dem das Teil scheiterte
    /// (Codereview 2026-09-29, N11-004). „½-½" schreibt Handschrift und manche Exporte.</para>
    ///
    /// <para><b>Altbestand im Rohbestand:</b> Zeilen, die VOR dieser Korrektur eingelesen wurden,
    /// behalten ihren alten MovesHash/PlyCount — der Import (<c>tools/LibraryImport import</c>)
    /// fasst bestehende Zeilen nie an. Eine betroffene Quelldatei deshalb NICHT zur Reparatur neu
    /// einlesen: der Griff „schon bekannt" ist der GESPEICHERTE alte Hash, die Partie kaeme mit dem
    /// neuen Hash ein zweites Mal hinein, und <c>dedupe</c> ordnete die alte Zeile nie zu. Reparatur:
    /// <c>rehash-results</c> (rechnet die Zugspalten nach, <see cref="LibraryGameReader.RehashBareResult"/>),
    /// danach <c>dedupe</c>.</para>
    ///
    /// <para>Bewusst NICHT hier: die Listen, die den <c>[Result]</c>-HEADER pruefen
    /// (<c>SavedGameService.AllowedResults</c>, <c>LeagueClubService.Results</c>) — dort ist „1/2"
    /// kein gueltiger Wert: beim Einlesen setzen beide ihn auf „*" zurueck, eine Korrektur ueber
    /// <c>LeagueClubService.UpdateAsync</c> wird mit „invalidResult" abgelehnt.</para>
    /// </summary>
    public static bool IsResultToken(ReadOnlySpan<char> token)
        => token is "1-0" or "0-1" or "1/2-1/2" or "1/2" or "*" or "½-½";

    /// <summary>
    /// Entfernt alle Varianten „(…)" samt beliebig tiefer Schachtelung — mit einem Tiefenzaehler,
    /// nicht mit einem Regex. Jede entfernte Variante wird zu EINEM Leerzeichen (sonst klebten
    /// „e4(d4)e5" die Nachbarzuege zu „e4e5" zusammen), eine verirrte „)" ebenso; eine nie
    /// geschlossene „(" nimmt den Rest des Textes mit, wie bei jedem PGN-Leser. Kommentare
    /// („{…}") muss der Aufrufer VORHER entfernen — eine Klammer im Kommentar ist keine Variante.
    ///
    /// <para><see cref="ReconstructionChain.SplitMoves"/> entfernte bis 0.624.0 per
    /// <c>\([^()]*\)</c> nur die INNERSTEN Paare, einmal: aus „(1... c5 (1... e6) 2. Nf3)" blieben
    /// die Tokens „(1..." und „Nf3)" stehen und wurden als erster illegaler Zug gemeldet
    /// (Codereview 2026-09-29, N3-008). <see cref="PgnParser"/> hatte die Zaehler-Fassung schon,
    /// nur ohne das Leerzeichen.</para>
    /// </summary>
    public static string RemoveVariations(string s)
    {
        if (s.IndexOf('(') < 0 && s.IndexOf(')') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        int depth = 0;
        foreach (char c in s)
        {
            if (c == '(') { if (depth++ == 0) sb.Append(' '); }
            else if (c == ')') { if (depth > 0) depth--; else sb.Append(' '); }
            else if (depth == 0) sb.Append(c);
        }
        return sb.ToString();
    }
}
