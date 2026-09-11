namespace RookHub.Api.Services;

/// <summary>
/// Welche Seite uebernimmt man in einer Punktepartie, wenn man es nicht selbst sagt? Die des
/// GEWINNERS — im kuratierten Bestand ist genau das der Sinn der Uebung.
///
/// <para>Eine reine Funktion, weil die Frage an ZWEI Stellen beantwortet wird: beim Starten einer
/// Sitzung (dort liegen die Kandidaten der letzten Stellung vor) und in der Bestandsliste, die
/// vorher sagen soll, welche Farbe man spielt (dort steht nur die kurze Bewertungs-Zeichenkette).
/// Zwei Fassungen derselben Regel waeren die erste Stelle, an der Liste und Brett auseinanderlaufen.</para>
/// </summary>
public static class GuessSides
{
    /// <summary>Ab dieser Bauerndifferenz gilt eine Stellung als entschieden — darunter sagt sie
    /// nichts darueber, wer die Partie gewonnen hat.</summary>
    public const double DecisivePawns = 1.5;

    /// <summary>
    /// Vorrang hat das ERGEBNIS. Fehlt es, entscheidet die BEWERTUNG der letzten gerechneten
    /// Stellung: eine aufgegebene Partie steht dort klar auf einer Seite. Das ist kein Sonderfall,
    /// sondern der Normalfall bei unseren Meisterpartien — sie kommen aus einem Buch, das in die
    /// Kopfzeile nur <c>*</c> schreibt (Capablancas <i>Chess Fundamentals</i> nennt die Ergebnisse
    /// nur im Fliesstext). Sagt auch die Bewertung nichts Deutliches, bleibt es bei Weiss.
    /// </summary>
    /// <param name="result">PGN-Ergebnis („1-0", „0-1", „1/2-1/2", „*").</param>
    /// <param name="lastPly">Halbzug der letzten gerechneten Stellung; <c>null</c> = keine.</param>
    /// <param name="pawns">Deren Bewertung AUS SICHT DER SEITE AM ZUG; <c>null</c> = unbekannt.</param>
    public static bool WinnerWhite(string? result, int? lastPly, double? pawns)
    {
        switch (result?.Trim())
        {
            case "1-0": return true;
            case "0-1": return false;
        }

        if (lastPly is not int ply || pawns is not double value) return true;
        if (Math.Abs(value) < DecisivePawns) return true;

        // Bei geradem Halbzug ist Weiss am Zug — eine positive Bewertung gehoert also ihm.
        var whiteToMove = ply % 2 == 0;
        return value > 0 ? whiteToMove : !whiteToMove;
    }

    /// <summary>
    /// Die kurze Bewertungs-Zeichenkette einer Stellung („+0.35", „-1.2", „#3", „#-2") als Bauern.
    /// <c>null</c>, wenn nichts Lesbares drinsteht.
    ///
    /// <para>Ein Matt wird auf eine sehr grosse Bauernzahl abgebildet — dieselbe Uebersetzung wie in
    /// <see cref="GuessScoring"/>, damit „Matt in 3" auch hier jede Materialbewertung schlaegt.</para>
    /// </summary>
    public static double? PawnsFromEvalText(string? evalText)
    {
        var text = evalText?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (text[0] == '#')
        {
            if (!int.TryParse(text.AsSpan(1), out var mateIn) || mateIn == 0) return null;
            var magnitude = GuessScoring.MateBasePawns - Math.Abs(mateIn);
            return mateIn > 0 ? magnitude : -magnitude;
        }

        return double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pawns)
            ? pawns
            : null;
    }
}
