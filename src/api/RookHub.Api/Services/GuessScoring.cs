using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die Wertung EINES geratenen Zuges in der Punktepartie — reine Funktion, keine Datenbank, keine
/// Engine: Kandidatenliste rein, <see cref="GuessGrade"/> raus. Damit ist sie ohne Infrastruktur
/// testbar (wie <see cref="PuzzleElo"/> oder <see cref="ChapterOrder"/>) und die Schwellen lassen
/// sich an echten Läufen nachjustieren, ohne irgendwo sonst etwas anzufassen.
///
/// <para><b>Bezugspunkt ist der PARTIEZUG, nicht der Engine-Zug.</b> Die Übung heißt „errate den
/// Zug"; die Engine urteilt nur darüber, wie gut die Alternative war. Deshalb kann ein Zug, der
/// besser ist als der des Meisters, mehr Punkte geben als der Partiezug selbst.</para>
///
/// <para><b>Alle Bewertungen kommen aus Sicht der Seite am Zug.</b> Der Broker liefert sie aus
/// WEISS-Sicht (siehe <c>mapRemoteLine</c> im Frontend) — die Umrechnung passiert beim Einlesen der
/// Analyse, nicht hier. Wer hier eine Weiß-Sicht-Bewertung hineingibt, bekommt für Schwarz das
/// Vorzeichen verkehrt und damit Unsinn.</para>
/// </summary>
public static class GuessScoring
{
    // ===== Schwellen (Bauerneinheiten) ======================================
    // Vorgabe des Betreibers (2026-09-05); die beiden mit „Lücke" markierten Werte waren dort nicht
    // festgelegt und sind als Startwert gesetzt — sie sind der erste Kandidat fürs Nachjustieren.

    /// <summary>Bis hierhin gilt ein anderer Zug als „praktisch gleich gut".</summary>
    public const double SimilarPawns = 0.1;

    /// <summary>Ab hier ist ein Zug „eindeutig besser" als der Partiezug.</summary>
    public const double ClearlyBetterPawns = 0.2;

    /// <summary>Mindestabstand ALLER Alternativen zum Partiezug, damit er als „einziger Zug" gilt.</summary>
    public const double OnlyMoveGapPawns = 1.0;

    /// <summary>LÜCKE der Vorgabe: ab wann ist ein Zug „deutlich schlechter" (Minuspunkte)?
    /// Startwert 1.0 — darunter (0.1 bis 1.0) gibt es 0 Punkte statt Abzug.</summary>
    public const double MuchWorsePawns = 1.0;

    /// <summary>Matt-Bewertungen werden auf eine sehr große Bauernzahl abgebildet, damit sich alles
    /// mit einer einzigen Zahl vergleichen lässt: jedes Matt schlägt jede Materialbewertung, und ein
    /// kürzeres Matt schlägt ein längeres (Matt in 3 = 997, Matt in 5 = 995).</summary>
    public const double MateBasePawns = 1000.0;

    /// <summary>Eine Engine-Bewertung: entweder Centipawns ODER Matt in N (N&lt;0 = man wird mattgesetzt).
    /// Immer aus Sicht der Seite am Zug.</summary>
    public readonly record struct Eval(int? Cp, int? MateIn)
    {
        public static Eval FromCp(int cp) => new(cp, null);
        public static Eval FromMate(int mateIn) => new(null, mateIn);

        /// <summary>Vergleichbare Zahl in Bauerneinheiten (Matt → sehr groß, siehe <see cref="MateBasePawns"/>).</summary>
        public double Pawns => MateIn is int m
            ? (m >= 0 ? MateBasePawns - Math.Min(m, 999) : -(MateBasePawns - Math.Min(-m, 999)))
            : (Cp ?? 0) / 100.0;
    }

    /// <summary>Ein Zug der Kandidatenliste (MultiPV) mit seiner Bewertung.</summary>
    public readonly record struct Candidate(string Uci, Eval Eval);

    /// <summary>Ergebnis einer Wertung — Stufe, Punkte und die Zahlen, aus denen sie entstanden sind
    /// (die Anzeige soll erklären können, WARUM es so viele Punkte gab).</summary>
    public readonly record struct GuessResult(
        GuessGrade Grade, int Points, double PlayedPawns, double GamePawns, bool PlayedWasListed);

    /// <summary>
    /// Bewertet <paramref name="playedUci"/> gegen den Partiezug <paramref name="gameUci"/>.
    /// </summary>
    /// <param name="candidates">MultiPV-Liste der Stellung (Protokoll-Maximum sind 5 Linien).
    /// Der Partiezug MUSS enthalten sein — sonst ist die Stellung nicht wertbar (siehe Rückgabe).</param>
    /// <returns><c>null</c>, wenn der Partiezug nicht in der Liste steht: dann fehlt der Bezugspunkt,
    /// und raten zu lassen wäre unfair. Solche Stellungen werden übersprungen, nicht mit 0 gewertet.</returns>
    public static GuessResult? Evaluate(IReadOnlyList<Candidate> candidates, string playedUci, string gameUci)
    {
        if (candidates is null || candidates.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(playedUci) || string.IsNullOrWhiteSpace(gameUci)) return null;

        var game = Find(candidates, gameUci);
        if (game is null) return null;
        var gamePawns = game.Value.Eval.Pawns;

        var played = Find(candidates, playedUci);
        bool listed = played is not null;

        // Steht der geratene Zug NICHT in der Liste, kennen wir seine Bewertung nicht — wir wissen
        // nur: er ist höchstens so gut wie der schlechteste gelistete Zug (sonst stünde er drin).
        // Diese Obergrenze wird gewertet, und die Stufe wird bei „ähnlich" gedeckelt: ein sechster
        // Zug kann in einer ruhigen Stellung durchaus brauchbar sein, aber „besser als der
        // Partiezug" ist er sicher nicht.
        var playedPawns = listed ? played!.Value.Eval.Pawns : candidates.Min(c => c.Eval.Pawns);

        var diff = playedPawns - gamePawns;   // > 0 = besser als der Partiezug

        GuessGrade grade;
        if (IsSameMove(playedUci, gameUci))
        {
            // Genau der Partiezug. War er der EINZIGE, der die Stellung hält, ist das mehr wert.
            var bestAlternative = candidates
                .Where(c => !IsSameMove(c.Uci, gameUci))
                .Select(c => (double?)c.Eval.Pawns)
                .DefaultIfEmpty(null)
                .Max();
            grade = bestAlternative is double alt && gamePawns - alt >= OnlyMoveGapPawns
                ? GuessGrade.OnlyMove
                : GuessGrade.GameMove;
        }
        else if (diff >= ClearlyBetterPawns) grade = GuessGrade.ClearlyBetter;
        else if (diff > SimilarPawns)        grade = GuessGrade.Better;
        else if (diff >= -SimilarPawns)      grade = GuessGrade.Similar;
        else if (-diff >= MuchWorsePawns)    grade = GuessGrade.MuchWorse;
        else                                 grade = GuessGrade.Worse;

        // Ungelistete Züge können nie besser als „ähnlich" sein (siehe oben).
        if (!listed && grade > GuessGrade.Similar) grade = GuessGrade.Similar;

        return new GuessResult(grade, GuessGrades.PointsFor(grade), playedPawns, gamePawns, listed);
    }

    private static Candidate? Find(IReadOnlyList<Candidate> candidates, string uci)
    {
        foreach (var c in candidates)
            if (IsSameMove(c.Uci, uci)) return c;
        return null;
    }

    /// <summary>UCI-Vergleich ohne Groß-/Kleinschreibung und ohne Leerzeichen — die Umwandlungsfigur
    /// gehört dazu (<c>e7e8q</c> ist nicht <c>e7e8n</c>).</summary>
    private static bool IsSameMove(string a, string b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
