namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// Einstellungen der Markierung „Nur ein Zug“ (1.033.0, <c>LiveBoards:OnlyMove*</c>). Alle Werte in Bauern aus Sicht der Seite am Zug.
/// </summary>
public sealed class LiveOnlyMoveOptions
{
    /// <summary><c>LiveBoards:OnlyMoveGapPawns</c> (Vorgabe 1,0) — so viel muss der zweitbeste Zug schlechter sein als der beste
    /// (Wunsch 2026-11-26: „alle anderen z. B. 1 Punkt schlechter“).</summary>
    public double GapPawns { get; init; } = 1.0;
    /// <summary><c>LiveBoards:OnlyMoveIrrelevantPawns</c> (Vorgabe 3,0) — steht der zweitbeste Zug für den Ziehenden noch mindestens
    /// so gut (oder setzt er selbst matt), ist es egal (Präzisierung des Users: „Wenn der 2.-beste Zug noch +3 ist, dann ist alles
    /// wurst.“).</summary>
    public double IrrelevantPawns { get; init; } = 3.0;
    /// <summary><c>LiveBoards:OnlyMoveLostPawns</c> (Vorgabe 5,0) — steht schon der beste Zug bei −5 oder schlechter (bzw. wird der
    /// Ziehende matt), ist ohnehin alles verloren: keine Markierung.</summary>
    public double LostPawns { get; init; } = 5.0;
    /// <summary>Erst ab dieser Tiefe (= <c>LiveBoards:Engine:MinShowDepth</c>, Vorgabe 20) — flache Tiefen springen, die Markierung
    /// würde flackern.</summary>
    public int MinDepth { get; init; } = 20;

    public static LiveOnlyMoveOptions From(IConfiguration c, LiveEngineOptions engine)
    {
        var s = c.GetSection("LiveBoards");
        return new LiveOnlyMoveOptions
        {
            GapPawns = Math.Clamp(s.GetValue<double?>("OnlyMoveGapPawns") ?? 1.0, 0.1, 20),
            IrrelevantPawns = Math.Clamp(s.GetValue<double?>("OnlyMoveIrrelevantPawns") ?? 3.0, 0.1, 50),
            LostPawns = Math.Clamp(s.GetValue<double?>("OnlyMoveLostPawns") ?? 5.0, 0.1, 50),
            MinDepth = Math.Min(engine.MinShowDepth, engine.Depth),
        };
    }
}

/// <summary>
/// Regel „Nur ein Zug“ (1.033.0, Wunsch 2026-11-26: „auf den Brettern markieren, wenn es nur einen Zug gibt, der gut ist, und alle
/// anderen z. B. 1 Punkt schlechter“). Rein, getestet; der Server rechnet sie EINMAL je Bewertung (<see cref="BroadcastLiveHub"/>),
/// der Browser zeigt nur das Feld <c>onlyMove</c>.
/// <list type="number">
/// <item>Nur mit zweiter Linie (MultiPV 2 von Cloud oder Engine) und Tiefe ≥ <see cref="LiveOnlyMoveOptions.MinDepth"/>;
///   <c>[%eval]</c> aus dem Strom hat keine → nie. Nur ein legaler Zug → auch keine zweite Linie → nichts (erzwungen, nicht „gefunden“).</item>
/// <item>Beide Linien aus Sicht der Seite am Zug (Broker und Cloud liefern Weiß-Sicht → bei Schwarz am Zug Vorzeichen drehen).
///   Matt für den Ziehenden zählt als +∞, gegen ihn als −∞ (ein früheres Matt besser als ein späteres).</item>
/// <item>Zweitbester Zug ≥ <see cref="LiveOnlyMoveOptions.IrrelevantPawns"/> (+3) oder selbst Matt für den Ziehenden → egal, nichts.</item>
/// <item>Bester Zug ≤ −<see cref="LiveOnlyMoveOptions.LostPawns"/> (−5) oder Matt gegen den Ziehenden → alles verloren, nichts.</item>
/// <item>Sonst: Abstand bester − zweitbester ≥ <see cref="LiveOnlyMoveOptions.GapPawns"/> (1,0 Bauern) → „Nur ein Zug“.</item>
/// </list>
/// Entschieden in Bauern statt Gewinnprozent: der User denkt in „1 Punkt“, und die beiden Grenzen (+3 / −5) nehmen genau die
/// Stellungen heraus, in denen ein Bauernabstand nichts mehr bedeutet (gewonnen bzw. verloren) — das ist der Hauptvorteil, den
/// eine Gewinnprozent-Kurve hätte. Zwischen −5 und +3 ist ein Bauer praktisch überall eine echte Entscheidung.
/// </summary>
public static class LiveOnlyMove
{
    private const double MateScore = 100_000;

    /// <summary>Gilt „Nur ein Zug“ für diese Bewertung (Seite am Zug aus <paramref name="whiteToMove"/>)?</summary>
    public static bool Applies(LiveEval ev, bool whiteToMove, LiveOnlyMoveOptions o)
    {
        if (ev.Source == "broadcast" || !ev.HasSecond || (ev.Depth ?? 0) < o.MinDepth) return false;
        if (Score(ev.Cp, ev.Mate, whiteToMove) is not { } a || Score(ev.SecondCp, ev.SecondMate, whiteToMove) is not { } b) return false;
        var best = Math.Max(a, b);     // Zwischenstände der Engine können die Reihenfolge kurz vertauschen
        var second = Math.Min(a, b);
        if (second >= o.IrrelevantPawns * 100) return false;
        if (best <= -o.LostPawns * 100) return false;
        return best - second >= o.GapPawns * 100 - 1e-9;
    }

    /// <summary>Die Bewertung mit gesetztem (<c>true</c>) bzw. entferntem (<c>null</c>) <c>OnlyMove</c>.</summary>
    public static LiveEval Mark(LiveEval ev, bool whiteToMove, LiveOnlyMoveOptions o) =>
        ev with { OnlyMove = Applies(ev, whiteToMove, o) ? true : null };

    /// <summary>Weiß-Sicht → Sicht des Ziehenden; Matt in n als ±(100000 − n).</summary>
    internal static double? Score(int? cp, int? mate, bool whiteToMove)
    {
        var sign = whiteToMove ? 1 : -1;
        if (mate is { } m)
        {
            var mm = sign * m;
            // Matt 0 = der Ziehende ist matt (kommt praktisch nicht vor, dann gäbe es keine Linie).
            return mm > 0 ? MateScore - mm : -MateScore - mm;
        }
        return cp is { } c ? sign * c : null;
    }
}
