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
}
