namespace RookHub.Api.Services.Games;

/// <summary>
/// Stellungs-Taktik für „Brilliant" und „Great" (0.854.0) — der SPIEGEL von <c>features/games/move-tactics.util.ts</c>
/// (<c>sacrificedPiece</c>, <c>capturable</c>, <c>hangingOn</c>, <c>exchangeGain</c>, <c>capturesPiece</c>). Die Regeln und
/// ihre Herkunft (freechess, Prod-Partien) stehen dort; hier dieselben Schritte über <see cref="TacticsBoard"/> statt
/// chess.js, damit der Server die Zug-Klassen für Listen vorrechnen kann (<see cref="GameMoveClasses"/>).
///
/// <para><b>Beide Seiten bekommen Tests mit LITERALEN Stellungen und Ergebnissen</b> (<c>MoveTacticsTests</c> ↔
/// <c>move-tactics.util.spec.ts</c>). Wer hier eine Regel ändert, ändert sie drüben mit — und zählt
/// <see cref="GameMoveClasses.Version"/> hoch, damit der Erntelauf alles neu rechnet. Wirft nie: eine unlesbare Stellung
/// heißt „kein Opfer".</para>
/// </summary>
public static class MoveTactics
{
    /// <summary>Eine geopferte Figur: Feld („e6") und Art (klein: n, b, r, q).</summary>
    public sealed record Sacrifice(string Square, char Piece);

    /// <summary>Figurenwerte wie freechess: Bauer 1, Leichtfigur 3, Turm 5, Dame 9, König ∞.</summary>
    public static double PieceValue(char piece) => char.ToLowerInvariant(piece) switch
    {
        'p' => 1, 'n' => 3, 'b' => 3, 'r' => 5, 'q' => 9, _ => double.PositiveInfinity,
    };

    /// <summary>Umwandlungszug (fünftes Zeichen qrbn)?</summary>
    public static bool IsPromotion(string? uci) =>
        uci is { Length: 5 } && TacticsBoard.SquareIndex(uci[..2]) is not null && TacticsBoard.SquareIndex(uci[2..4]) is not null
        && "qrbnQRBN".Contains(uci[4]);

    /// <summary>Steht die Seite am Zug im Schach? Unlesbar = nein.</summary>
    public static bool InCheck(string? fen) => TacticsBoard.TryParse(fen)?.InCheck() ?? false;

    /// <summary>Schlägt der Zug eine GEGNERISCHE Figur (Leichtfigur oder mehr)? Rochade e1h1 trifft den eigenen Turm → nein.</summary>
    public static bool CapturesPiece(string? fenBefore, string? uci)
    {
        if (TacticsBoard.TryParse(fenBefore) is not { } b || uci is not { Length: >= 4 } || TacticsBoard.SquareIndex(uci[2..4]) is not int to)
            return false;
        return b.At(to) is char t && TacticsBoard.IsWhite(t) != b.WhiteToMove && PieceValue(t) >= 3 && PieceValue(t) < double.PositiveInfinity;
    }

    /// <summary>Wert der mit dem Zug geschlagenen Figur: 0 ohne Schlagen, en passant der Bauer; <c>null</c> unlesbar.</summary>
    internal static double? CapturedOn(TacticsBoard before, string? uci)
    {
        if (uci is not { Length: >= 4 } || TacticsBoard.SquareIndex(uci[..2]) is not int from || TacticsBoard.SquareIndex(uci[2..4]) is not int to)
            return null;
        if (before.At(from) is not char moved) return null;
        if (before.At(to) is char target) return TacticsBoard.IsWhite(target) != TacticsBoard.IsWhite(moved) ? PieceValue(target) : 0;
        if (char.ToLowerInvariant(moved) == 'p' && from % 8 != to % 8) return 1;
        return 0;
    }

    /// <summary>Hängt die Figur auf <paramref name="square"/> NACH dem Zug? (Regeln a–e wie im Client.)</summary>
    public static bool IsPieceHanging(string fenBefore, string fenAfter, string square) =>
        TacticsBoard.TryParse(fenBefore) is { } b && TacticsBoard.TryParse(fenAfter) is { } a
        && TacticsBoard.SquareIndex(square) is int sq && HangingOn(b, a, sq);

    /// <summary>Das Opfer eines Zuges: die teuerste eigene Figur (Springer bis Dame), die danach hängt, bei der der Gegner
    /// MEHR gewinnt als geschlagen wurde, und die sich wirklich nehmen lässt. <c>null</c> sonst.</summary>
    public static Sacrifice? SacrificedPiece(string? fenBefore, string? fenAfter, string? uci)
    {
        if (TacticsBoard.TryParse(fenBefore) is not { } before || TacticsBoard.TryParse(fenAfter) is not { } after) return null;
        if (CapturedOn(before, uci) is not double captured) return null;
        var mover = before.WhiteToMove;

        var hanging = new List<(int Square, char Piece)>();
        foreach (var (sq, p) in after.Pieces())
        {
            if (TacticsBoard.IsWhite(p) != mover) continue;
            var type = char.ToLowerInvariant(p);
            if (type is 'p' or 'k') continue;
            if (PieceValue(p) <= captured) continue;
            if (HangingOn(before, after, sq) && ExchangeLossOn(after, sq) > captured) hanging.Add((sq, type));
        }
        if (hanging.Count == 0 || !Capturable(after, hanging)) return null;
        var top = hanging[0];
        foreach (var h in hanging) if (PieceValue(h.Piece) > PieceValue(top.Piece)) top = h;
        return new Sacrifice(TacticsBoard.SquareName(top.Square), top.Piece);
    }

    /// <summary>Lässt sich wenigstens eines der hängenden Stücke nehmen, ohne dass das Schlagen anderswo mindestens so viel
    /// kostet (Köder) — und ein Opfer unter Turmwert erlaubt danach kein Matt in einem Zug?</summary>
    private static bool Capturable(TacticsBoard after, List<(int Square, char Piece)> sacrifices)
    {
        var top = sacrifices.Max(s => PieceValue(s.Piece));
        var moves = after.LegalMoves();
        foreach (var sac in sacrifices)
        {
            foreach (var capture in moves)
            {
                if (capture.To != sac.Square || capture.Captured is null) continue;
                var test = after.Apply(capture);
                var capturerWhite = TacticsBoard.IsWhite(capture.Piece);
                var baitTaken = false;
                foreach (var (sq, p) in test.Pieces())
                {
                    if (TacticsBoard.IsWhite(p) != capturerWhite) continue;
                    var type = char.ToLowerInvariant(p);
                    if (type is 'p' or 'k' || sq == capture.To) continue;
                    if (PieceValue(p) >= top && HangingOn(after, test, sq)) { baitTaken = true; break; }
                }
                if (baitTaken) continue;
                if (PieceValue(sac.Piece) >= 5 || !test.HasMateInOne()) return true;
            }
        }
        return false;
    }

    /// <summary>Static Exchange Evaluation über aufsteigend sortierte Werte (wie im Client).</summary>
    public static double ExchangeGain(double target, IReadOnlyList<double> capturers, IReadOnlyList<double> owners)
    {
        if (capturers.Count == 0) return 0;
        var rest = capturers.Skip(1).ToList();
        return Math.Max(0, target - ExchangeGain(capturers[0], owners, rest));
    }

    private static double ExchangeLossOn(TacticsBoard after, int sq)
    {
        if (after.At(sq) is not char piece) return 0;
        var (attackers, defenders) = InfluenceOn(after, sq, TacticsBoard.IsWhite(piece));
        return ExchangeGain(PieceValue(piece), attackers.Select(PieceValue).Order().ToList(), defenders.Select(PieceValue).Order().ToList());
    }

    /// <summary>Angreifer und Verteidiger einer Figur der Farbe <paramref name="white"/>: ist der Gegner am Zug, seine LEGALEN
    /// Schlagzüge — plus der König, der im Abtausch zuletzt schlägt (0.853.0, Partie 228: 22.Sxe6) —, sonst Pseudo.</summary>
    private static (List<char> Attackers, List<char> Defenders) InfluenceOn(TacticsBoard after, int sq, bool white)
    {
        var defenders = after.Attackers(sq, white).Select(s => after.At(s)!.Value).ToList();
        if (after.WhiteToMove == white)
            return (after.Attackers(sq, !white).Select(s => after.At(s)!.Value).ToList(), defenders);
        var seen = new HashSet<int>();
        var attackers = new List<char>();
        foreach (var m in after.LegalMoves())
        {
            if (m.To != sq || m.Captured is null || !seen.Add(m.From)) continue;
            attackers.Add(m.Piece);
        }
        if (!attackers.Any(a => char.ToLowerInvariant(a) == 'k'))
        {
            foreach (var s in after.Attackers(sq, !white))
                if (after.At(s) is char k && char.ToLowerInvariant(k) == 'k') { attackers.Add(k); break; }
        }
        return (attackers, defenders);
    }

    private static bool HangingOn(TacticsBoard before, TacticsBoard after, int sq)
    {
        if (after.At(sq) is not char piece) return false;
        var white = TacticsBoard.IsWhite(piece);
        if (!after.IsAttacked(sq, !white)) return false;
        var value = PieceValue(piece);
        var (attackers, defenders) = InfluenceOn(after, sq, white);

        // a) Abtausch: das Geschlagene war mindestens so viel wert.
        var previous = before.At(sq);
        if (previous is char prev && TacticsBoard.IsWhite(prev) != white && PieceValue(prev) >= value) return false;

        // b) Turm gegen Leichtfigur, einmal von einer Leichtfigur gedeckt.
        if (char.ToLowerInvariant(piece) == 'r' && previous is char pr && PieceValue(pr) == 3
            && attackers.Count == 1 && PieceValue(attackers[0]) == 3) return false;

        // c) Ein billigerer Angreifer gewinnt Material.
        if (attackers.Any(a => PieceValue(a) < value)) return true;

        // d) Überzahl der Angreifer.
        if (attackers.Count > defenders.Count)
        {
            var cheapest = attackers.Min(PieceValue);
            if (value < cheapest && defenders.Any(d => PieceValue(d) < cheapest)) return false;
            if (defenders.Any(d => char.ToLowerInvariant(d) == 'p')) return false;
            return true;
        }
        return false;
    }
}
