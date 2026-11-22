using Chess;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Geprüfte Bauernstruktur-Fakten für die Fehler-Erklärungen (1.001.1, Befund Vereinspartie 261, Fehler 21...exf3 statt
/// 21...h5, 22.11.2026: in der Linie 21...h5 22.Te1 Te8 23.Tbd1 a5 24.Lf1 Tad8 25.fxe4 Txd1 26.Txd1 fxe4 bekommt Schwarz
/// einen isolierten e-Bauern, der Text sagte nur „steht ausgeglichen“ — der Nutzer: „warum ist das kein Problem für Schwarz?
/// Der Isolani schaut doch sehr schwach aus.“ Der Grund: e4 ist ein Freibauer, und Weiß hat als Ausgleich die Mehrheit am
/// Damenflügel). Rein aus dem Figurenfeld der FEN berechnet:
/// <list type="bullet">
/// <item>isoliert (kein eigener Bauer auf den Nachbarlinien), doppelt (zwei eigene auf einer Linie), rückständig (die eigenen
/// Bauern der Nachbarlinien stehen alle weiter vorn, und das Feld davor deckt ein gegnerischer Bauer), Freibauer (kein
/// gegnerischer Bauer vor ihm auf der eigenen oder einer Nachbarlinie) — auch „isolated but passed“;</item>
/// <item>beim Isolani bzw. rückständigen Bauern: halboffene Linie (kein gegnerischer Bauer auf seiner Linie), und ob das Feld
/// vor ihm von einer gegnerischen Figur besetzt ist (blockiert);</item>
/// <item>Bauernmehrheiten je Flügel (Damenflügel a–d, Königsflügel e–h).</item>
/// </list>
/// „Auffällig“ heißt: eine dieser Eigenschaften ist am Ende der Linie NEU gegenüber der Ausgangsstellung (gleiche Farbe,
/// gleiche Linie); nur dann stehen die Bauernfakten samt Mehrheiten da.
/// </summary>
public static class PawnStructure
{
    /// <summary>Die Eigenschaften eines Bauern.</summary>
    public sealed record Flags(bool Isolated, bool Doubled, bool Backward, bool Passed, bool HalfOpen, string? BlockedBy);

    private static List<(bool White, int File, int Rank)> Pawns(BoardGeometry geo)
    {
        var result = new List<(bool, int, int)>();
        foreach (var white in new[] { true, false })
            foreach (var (piece, sq) in geo.Pieces(white))
                if (char.ToLowerInvariant(piece) == 'p') result.Add((white, sq[0] - 'a', sq[1] - '1'));
        return result;
    }

    /// <summary>Die Eigenschaften des Bauern auf <paramref name="square"/>, <c>null</c>, wenn dort keiner steht.</summary>
    internal static Flags? Of(BoardGeometry geo, string square)
    {
        var piece = geo.PieceAt(square);
        if (char.ToLowerInvariant(piece) != 'p') return null;
        return Of(geo, Pawns(geo), char.IsUpper(piece), square[0] - 'a', square[1] - '1');
    }

    private static Flags Of(BoardGeometry geo, List<(bool White, int File, int Rank)> pawns, bool white, int file, int rank)
    {
        var dir = white ? 1 : -1;
        bool Ahead(int r) => white ? r > rank : r < rank;
        var own = pawns.Where(p => p.White == white).ToList();
        var enemy = pawns.Where(p => p.White != white).ToList();
        var neighbours = own.Where(p => Math.Abs(p.File - file) == 1).ToList();
        var isolated = neighbours.Count == 0;
        var doubled = own.Count(p => p.File == file) > 1;
        var passed = !enemy.Any(p => Math.Abs(p.File - file) <= 1 && Ahead(p.Rank));
        var stop = rank + dir;
        var stopAttackedByPawn = enemy.Any(p => Math.Abs(p.File - file) == 1 && p.Rank == stop + dir);
        var backward = !isolated && !passed && neighbours.All(p => Ahead(p.Rank)) && stopAttackedByPawn;
        var halfOpen = !enemy.Any(p => p.File == file);
        string? blockedBy = null;
        if (stop is >= 0 and <= 7)
        {
            var stopSquare = $"{(char)('a' + file)}{(char)('1' + stop)}";
            var blocker = geo.PieceAt(stopSquare);
            if (blocker != '\0' && char.IsUpper(blocker) != white && char.ToLowerInvariant(blocker) != 'p')
                blockedBy = $"the {(white ? "black" : "white")} {ExplanationFacts.PieceName(char.ToLowerInvariant(blocker))} on {stopSquare}";
        }
        return new Flags(isolated, doubled, backward, passed, halfOpen, blockedBy);
    }

    private static string Side(bool white) => white ? "White" : "Black";

    /// <summary>
    /// Die auffälligen Bauernfakten der Stellung <paramref name="endFen"/> gegenüber <paramref name="startFen"/> — leer, wenn
    /// keine Bauernschwäche bzw. kein Freibauer neu entstanden ist. Etwa „Black's pawn e4 is isolated but passed; White has a
    /// 3-vs-2 queenside majority; Black has a 3-vs-2 kingside majority“.
    /// </summary>
    public static List<string> Notable(string startFen, string endFen)
    {
        var facts = new List<string>();
        if (BoardGeometry.FromFen(startFen) is not { } start || BoardGeometry.FromFen(endFen) is not { } end) return facts;
        var before = Pawns(start);
        var flagsBefore = before.Select(p => (p.White, p.File, F: Of(start, before, p.White, p.File, p.Rank))).ToList();
        bool Had(bool white, int file, Func<Flags, bool> flag) => flagsBefore.Any(x => x.White == white && x.File == file && flag(x.F));
        var pawns = Pawns(end);
        var doubledSaid = new HashSet<(bool, int)>();
        foreach (var p in pawns.OrderBy(p => p.White ? 0 : 1).ThenBy(p => p.File).ThenBy(p => p.Rank))
        {
            var f = Of(end, pawns, p.White, p.File, p.Rank);
            var square = $"{(char)('a' + p.File)}{(char)('1' + p.Rank)}";
            var newIsolated = f.Isolated && !Had(p.White, p.File, x => x.Isolated);
            var newPassed = f.Passed && !Had(p.White, p.File, x => x.Passed);
            var newBackward = f.Backward && !Had(p.White, p.File, x => x.Backward);
            var newDoubled = f.Doubled && !Had(p.White, p.File, x => x.Doubled);
            if (newDoubled && doubledSaid.Add((p.White, p.File)))
                facts.Add($"{Side(p.White)} has doubled pawns on the {(char)('a' + p.File)}-file");
            if (!newIsolated && !newPassed && !newBackward) continue;
            string text;
            if (f.Isolated && f.Passed) text = "is isolated but passed";
            else if (f.Isolated) text = "is isolated" + (f.HalfOpen ? " on a half-open file" : "");
            else if (f.Backward) text = "is backward" + (f.HalfOpen ? " on a half-open file" : "");
            else text = "is passed";
            if (f.BlockedBy is { } b) text += $" and blockaded by {b}";
            facts.Add($"{Side(p.White)}'s pawn {square} {text}");
        }
        if (facts.Count == 0) return facts;
        foreach (var (name, files) in new[] { ("queenside", (Lo: 0, Hi: 3)), ("kingside", (Lo: 4, Hi: 7)) })
        {
            var w = pawns.Count(p => p.White && p.File >= files.Lo && p.File <= files.Hi);
            var b = pawns.Count(p => !p.White && p.File >= files.Lo && p.File <= files.Hi);
            if (w != b) facts.Add($"{Side(w > b)} has a {Math.Max(w, b)}-vs-{Math.Min(w, b)} {name} majority");
        }
        return facts;
    }

    /// <summary>
    /// Das Faktum zur Bauernstruktur am Ende einer Linie ab <paramref name="fen"/> (verglichen mit <paramref name="startFen"/>,
    /// der Stellung vor dem Zug): „pawn structure at the end of this line (after 26...fxe4): …“, oder <c>null</c>.
    /// </summary>
    public static string? AtLineEnd(string startFen, string fen, IReadOnlyList<string> sans)
    {
        if (sans.Count == 0) return null;
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var played = 0;
            foreach (var san in sans)
            {
                var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
                if (move is null) break;
                board.Move(move);
                played++;
            }
            if (played == 0) return null;
            var facts = Notable(startFen, board.ToFen());
            if (facts.Count == 0) return null;
            var parts = fen.Split(' ');
            var number = parts.Length >= 6 && int.TryParse(parts[5], out var n) ? n : 1;
            var p = played - 1 + (FenFields.WhiteToMove(fen) ? 0 : 1);
            var label = p % 2 == 0 ? $"{number + p / 2}.{sans[played - 1]}" : $"{number + p / 2}...{sans[played - 1]}";
            return $"pawn structure at the end of this line (after {label}): {string.Join("; ", facts)}";
        }
        catch
        {
            return null;
        }
    }
}
