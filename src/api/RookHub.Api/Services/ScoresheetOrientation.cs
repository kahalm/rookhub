namespace RookHub.Api.Services;

/// <summary>
/// Liegt das Formular-Foto quer oder auf dem Kopf? Abgelesen aus den KÄSTEN der ersten Lesung, ohne eigenen Modell-Aufruf
/// (gemeldet 2026-10-05 an LeagueHub-Formular 24: um 90° gedreht fotografiert, ohne EXIF-Drehung — die Lesung war
/// unsicher, der Ausschnitt je Zug ein schmaler, quer liegender Streifen). Auf einem aufrechten Formular steigen die
/// Zugnummern nach UNTEN, und Schwarz steht RECHTS neben Weiß; steigen sie nach links, ist das Foto um 90° gedreht.
/// Rein und ohne Bild.
/// </summary>
public static class ScoresheetOrientation
{
    /// <summary>So viele Paare „Zug n → n+1" bzw. „Weiß → Schwarz" braucht ein Urteil mindestens.</summary>
    public const int MinSteps = 6;
    public const int MinPairs = 4;

    /// <summary>
    /// Um wie viel Grad IM UHRZEIGERSINN das Foto der Seite <paramref name="page"/> zu drehen ist, damit es aufrecht steht:
    /// 0, 90, 180 oder 270. 0 auch, wenn die Kästen nicht reichen oder kein Urteil eindeutig ist — im Zweifel nie drehen.
    /// </summary>
    public static int Detect(ScoresheetTranscription t, int page = 1)
    {
        var pages = t.EntryPages();
        var boxes = t.Moves.Select((m, i) => (m, p: pages[i]))
            .Where(x => x.p == page && x.m.Box is { Count: 4 } b && b[2] > b[0] && b[3] > b[1])
            .Select(x => (x.m.MoveNumber, White: !string.Equals(x.m.Color, "b", StringComparison.OrdinalIgnoreCase),
                X: (x.m.Box![0] + x.m.Box[2]) / 2.0, Y: (x.m.Box[1] + x.m.Box[3]) / 2.0))
            .ToList();
        var byKey = boxes.GroupBy(b => (b.MoveNumber, b.White)).ToDictionary(g => g.Key, g => g.First());

        // Schritt von Zug n zu n+1 (dieselbe Farbe) — ein Spaltenwechsel ist ein Ausreißer, der Median schluckt ihn.
        var steps = byKey.Values.Where(b => byKey.ContainsKey((b.MoveNumber + 1, b.White)))
            .Select(b => { var n = byKey[(b.MoveNumber + 1, b.White)]; return (X: n.X - b.X, Y: n.Y - b.Y); }).ToList();
        var pairs = byKey.Values.Where(b => b.White && byKey.ContainsKey((b.MoveNumber, false)))
            .Select(b => { var s = byKey[(b.MoveNumber, false)]; return (X: s.X - b.X, Y: s.Y - b.Y); }).ToList();
        if (steps.Count < MinSteps || pairs.Count < MinPairs) return 0;

        var step = (X: Median(steps.Select(s => s.X)), Y: Median(steps.Select(s => s.Y)));
        var pair = (X: Median(pairs.Select(s => s.X)), Y: Median(pairs.Select(s => s.Y)));
        double Len((double X, double Y) v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (Len(step) < 1 || Len(pair) < 1) return 0;

        // Aufrecht: Schritt zeigt nach unten (0, +1), Weiß→Schwarz nach rechts (+1, 0). Jede Drehung um 90° im
        // Uhrzeigersinn macht aus (x, y) im Bild (y nach unten) den Vektor (−y, x).
        var best = 0;
        var bestScore = double.MinValue;
        for (var turns = 0; turns < 4; turns++)
        {
            var (s, w) = (step, pair);
            for (var k = 0; k < turns; k++) { s = (-s.Y, s.X); w = (-w.Y, w.X); }
            var score = s.Y / Len(s) + w.X / Len(w);
            if (score > bestScore) (best, bestScore) = (turns * 90, score);
        }
        // Beide Richtungen müssen deutlich passen (1,0 je Richtung = genau; 0,7 ≈ 45°) — sonst nicht drehen.
        return bestScore >= 1.4 ? best : 0;
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
