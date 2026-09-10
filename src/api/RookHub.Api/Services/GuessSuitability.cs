namespace RookHub.Api.Services;

/// <summary>
/// Wie gut taugt eine Partie des Rohbestands als PUNKTEPARTIE? Eine Zahl von 0 bis 100, aus den
/// Merkmalen berechnet, die beim Einlesen ohnehin anfallen.
///
/// <para><b>Wozu ueberhaupt eine Note:</b> 130 000 Partien kann niemand durchsehen, und die Engine
/// kann sie erst recht nicht alle rechnen (bei Tiefe 20 grob eine halbe Stunde je Partie). Die Note
/// ist der Vorschlag, in welcher Reihenfolge man hinsieht — sie entscheidet nichts, sie sortiert.</para>
///
/// <para><b>Was zaehlt, und warum in dieser Reihenfolge.</b> Die Punktepartie laeuft eine Partie
/// Halbzug fuer Halbzug entlang und haelt an kommentierten Zuegen an. Was sie braucht, ist deshalb
/// nicht die tiefste Analyse, sondern die durchgaengigste ERKLAERUNG:</para>
/// <list type="number">
/// <item><b>Dichte der Kommentare</b> (40): wie viele Halbzuege ueberhaupt etwas sagen. Eine Partie
/// mit drei langen Absaetzen am Schluss ist als Punktepartie stumm.</item>
/// <item><b>Substanz je Kommentar</b> (20): „gut!" ist kein Kommentar. Gemessen an der mittleren
/// Laenge, gesaettigt bei rund 150 Zeichen — darueber wird es nicht mehr besser, nur laenger.</item>
/// <item><b>Laenge der Partie</b> (15): unter 40 Halbzuegen ist die Uebung vorbei, bevor sie
/// anfaengt; ueber 120 wird sie zur Sitzung.</item>
/// <item><b>Entschiedene Partie</b> (10): im kuratierten Bestand uebernimmt man die Seite des
/// GEWINNERS. Beim Remis gibt es keinen, und die Seitenwahl faellt auf die Bewertung zurueck.</item>
/// <item><b>Staerke der Spieler</b> (10): „Meisterpartie" ist der Anspruch.</item>
/// <item><b>Nebenvarianten</b> (5): ein Zeichen fuer ernsthafte Arbeit, aber eben nur ein Zeichen —
/// gesehen bekommt der Spielende sie nie.</item>
/// </list>
///
/// <para>Die Gewichte sind eine ANNAHME und keine Messung. Sie stehen deshalb hier an einer Stelle
/// und die Note in einer eigenen Spalte: aendert sich die Annahme, wird der Bestand einmal neu
/// bewertet und niemand muss die Abfragen anfassen.</para>
/// </summary>
public static class GuessSuitability
{
    /// <summary>Unter so vielen Halbzuegen gibt eine Partie als Uebung nichts her.</summary>
    public const int ShortestUseful = 40;

    /// <summary>Ab hier wird aus der Uebung eine Sitzung.</summary>
    public const int LongestComfortable = 120;

    /// <summary>Ab dieser mittleren Kommentarlaenge gibt es die vollen Punkte fuer Substanz.</summary>
    private const int RichCommentChars = 150;

    public static int Score(int? plyCount, int? commentedPlies, int? commentChars, int? variationCount,
        string? result, int? whiteElo, int? blackElo)
    {
        var plies = plyCount ?? 0;
        if (plies <= 0) return 0;

        var commented = Math.Clamp(commentedPlies ?? 0, 0, plies);

        // 1. Dichte — wie oft die Partie etwas sagt. Ein Drittel der Halbzuege ist schon sehr viel,
        //    deshalb ist dort die volle Punktzahl erreicht und nicht erst bei jedem Zug.
        var density = Math.Min(1.0, commented / (double)plies / 0.33);
        var score = 40.0 * density;

        // 2. Substanz — mittlere Laenge der Kommentare.
        if (commented > 0)
        {
            var perComment = (commentChars ?? 0) / (double)commented;
            score += 20.0 * Math.Min(1.0, perComment / RichCommentChars);
        }

        // 3. Laenge der Partie: im Fenster volle Punkte, ausserhalb linear fallend.
        score += 15.0 * LengthFit(plies);

        // 4. Entschieden oder Remis.
        score += result is "1-0" or "0-1" ? 10.0 : 3.0;

        // 5. Staerke — die kleinere der beiden Zahlen, denn eine Seite allein macht keine Partie.
        score += 10.0 * StrengthFit(whiteElo, blackElo);

        // 6. Nebenvarianten, gesaettigt bei zwanzig.
        score += 5.0 * Math.Min(1.0, (variationCount ?? 0) / 20.0);

        return (int)Math.Round(Math.Clamp(score, 0, 100));
    }

    /// <summary>1.0 im Fenster, sonst linear fallend — bei der halben Untergrenze bzw. dem
    /// Doppelten der Obergrenze ist nichts mehr uebrig.</summary>
    private static double LengthFit(int plies)
    {
        if (plies >= ShortestUseful && plies <= LongestComfortable) return 1.0;
        if (plies < ShortestUseful)
            return Math.Max(0, (plies - ShortestUseful / 2.0) / (ShortestUseful / 2.0));
        return Math.Max(0, 1.0 - (plies - LongestComfortable) / (double)LongestComfortable);
    }

    /// <summary>Ohne Zahlen bleibt es beim halben Punkt: eine Partie ohne Elo-Angabe ist nicht
    /// schwach, sie ist unbekannt — und alte Meisterpartien tragen nie eine.</summary>
    private static double StrengthFit(int? whiteElo, int? blackElo)
    {
        if (whiteElo is null && blackElo is null) return 0.5;
        var lower = Math.Min(whiteElo ?? blackElo!.Value, blackElo ?? whiteElo!.Value);
        return lower switch
        {
            >= 2600 => 1.0,
            >= 2400 => 0.85,
            >= 2200 => 0.6,
            >= 2000 => 0.4,
            _ => 0.2,
        };
    }
}
