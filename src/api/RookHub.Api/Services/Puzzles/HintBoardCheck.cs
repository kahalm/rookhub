using System.Text;
using System.Text.RegularExpressions;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Die Stellung, die der Löser eines Buch-Puzzles wirklich sieht (FEN nach dem Vorspiel bis <c>StartPly</c>), plus die
/// Figurenart(en), die in der Lösung durch Umwandlung entstehen. Grundlage für den Tipp-Prompt und für die Prüfung
/// der fertigen Tipps gegen das Brett (<see cref="HintBoardCheck"/>).
/// </summary>
internal sealed class HintBoard
{
    /// <summary>FEN der Löserstellung.</summary>
    public required string Fen { get; init; }
    public required bool WhiteToMove { get; init; }
    /// <summary>Figuren je Feld: Buchstabe wie in der FEN (Groß = Weiß).</summary>
    public required IReadOnlyList<(char Piece, string Square)> Pieces { get; init; }
    /// <summary>Figurenarten (q/r/b/n), in die in der Lösung umgewandelt wird.</summary>
    public required IReadOnlySet<char> Promotions { get; init; }

    public bool Has(bool white, char type) =>
        Pieces.Any(p => char.IsUpper(p.Piece) == white && char.ToLowerInvariant(p.Piece) == type);

    public bool HasAny(char type) => Has(true, type) || Has(false, type);
}

/// <summary>
/// Hält den Tipp-Generator an das Brett (1.007.7, Befund 23.11.2026: 43 von 6035 Buch-Puzzles nannten Figuren, die gar
/// nicht auf dem Brett stehen — Buch 32 gab dem Modell nur Grundstellung + 30 Vorspielzüge, es sah längst getauschte
/// Damen). <see cref="FromPuzzle"/> spielt das Vorspiel nach, <see cref="DescribeForPrompt"/> liefert Stellung und
/// Figurenliste für den Prompt, <see cref="FindInventedPiece"/> findet in einem fertigen Tipp (de/en/hr) eine
/// Figurenart, die keine Seite hat, oder eine Figur mit klarer Seitenangabe („deine“, „weiße“, „your“ …), die diese
/// Seite nicht hat. Ausnahmen wie im Analyse-Skript <c>detect.py</c>: Umwandlung („zur Dame“, „promote to a queen“,
/// Umwandlungen in der Lösung), Randbauer/„rook pawn“/„knight pawn“, Damenflügel/„queenside“.
/// </summary>
internal static class HintBoardCheck
{
    private const char Queen = 'q', Rook = 'r', Bishop = 'b', Knight = 'n';
    private static readonly char[] CheckedTypes = { Queen, Rook, Bishop, Knight };

    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static Regex R(string pattern) => new(pattern, Opts);

    // Figurenwörter je Sprache (Text vorher kleingeschrieben). Atomare Gruppe beim englischen „queen“, damit
    // „queen's side“ nicht über „queen“ + „'s side“ doch wieder trifft.
    private static readonly Dictionary<string, (char Type, Regex Word)[]> Words = new()
    {
        ["de"] = new[]
        {
            (Queen, R(@"\b(dame|damen|damenopfer|damenzug|damentausch)\b")),
            (Rook, R(@"\b(turm|türme|turms|turmes|türmen|turmopfer|turmzug|turmpaar)\b")),
            (Bishop, R(@"\b(läufer|läufers|läufern|läuferpaar|läuferopfer|läuferzug)\b")),
            (Knight, R(@"\b(springer|springers|springern|springeropfer|springerzug|springergabel)\b")),
        },
        ["en"] = new[]
        {
            (Queen, R(@"\b(?>queen's|queens|queen)\b(?![\s-]*(side|wing))")),
            (Rook, R(@"\b(?>rook's|rooks|rook)\b")),
            (Bishop, R(@"\b(?>bishop's|bishops|bishop)\b")),
            (Knight, R(@"\b(?>knight's|knights|knight)\b")),
        },
        ["hr"] = new[]
        {
            (Queen, R(@"\b(dama|dame|dami|damu|damom|kraljica|kraljice|kraljicu|kraljicom)\b")),
            (Rook, R(@"\b(top|topa|topu|topom|topovi|topova|topovima|kula|kule|kulu|kulom)\b")),
            (Bishop, R(@"\b(lovac|lovca|lovcu|lovcem|lovci|lovaca|lovcima|lovce)\b")),
            (Knight, R(@"\b(skakač|skakača|skakaču|skakačem|skakači|konj|konja|konju|konjem|konji|konje)\b")),
        },
    };

    // Umwandlung zur Dame irgendwo im Tipp → „Dame“ ist kein Figurenbezug.
    private static readonly Dictionary<string, Regex> QueenPromotion = new()
    {
        ["de"] = R(@"umwand|verwandl|neue dame|neuen dame|zur dame|zu einer dame|in eine dame"),
        ["en"] = R(@"promot|queening|new queen|queen the|to (a )?queen\b|queen(ing)? square"),
        ["hr"] = R(@"promoc|promovi|nov[aeu] dam|do dame|prema dami|\bu damu|polj[ae] dame"),
    };

    // Rand-/Turm-/Springerbauer und Vergleiche → kein Figurenbezug (Fenster um das Wort).
    private static readonly Dictionary<string, Regex> NonPiece = new()
    {
        ["de"] = R(@"randbauer|turm-?bauer|springer-?bauer|läufer-?bauer|damen-?bauer"),
        ["en"] = R(@"\b(rook|knight|bishop|queen)'?s?[- ](file[- ])?pawn"),
        ["hr"] = R(@"za razliku od|topovsk|skakačev|lovčev"),
    };

    // Flügel-/Seitenbegriffe direkt ab dem Wort (de; en per Lookahead im Wortmuster).
    private static readonly Regex GermanQueenWing = R(@"^damen[-\s]?(flügel|seite)");

    // Seitenangaben direkt vor dem Wort (höchstens ein Wort dazwischen, z. B. „deine starke Dame“).
    private static readonly Dictionary<string, Regex> Own = new()
    {
        ["de"] = R(@"\b(dein|deine|deinen|deinem|deiner|deines|eigene[nmrs]?)\s+(\w+\s+)?$"),
        ["en"] = R(@"\b(your|own)\s+(\w+\s+)?$"),
        ["hr"] = R(@"\b(tvoj|tvoja|tvoje|tvog|tvoga|tvoju|tvojom|tvojim|svoj|svoja|svoju|svojom|svojim|svog|svoga)\s+(\w+\s+)?$"),
    };

    private static readonly Dictionary<string, Regex> Opponent = new()
    {
        ["de"] = R(@"\b(gegnerische[nmrs]?|feindliche[nmrs]?)\s+(\w+\s+)?$"),
        ["en"] = R(@"\b(opponent's|enemy|opposing)\s+(\w+\s+)?$"),
        ["hr"] = R(@"\b(protivnikov[aeuo]?|protivničk[iaeuo]m?|neprijateljsk[iaeuo]m?)\s+(\w+\s+)?$"),
    };

    private static readonly Dictionary<string, Regex> White = new()
    {
        ["de"] = R(@"\b(weiße[nmrs]?)\s+$"),
        ["en"] = R(@"\b(white|white's)\s+$"),
        ["hr"] = R(@"\b(bijel[iaeuo]m?|bijelog|bijeloj)\s+$"),
    };

    private static readonly Dictionary<string, Regex> Black = new()
    {
        ["de"] = R(@"\b(schwarze[nmrs]?)\s+$"),
        ["en"] = R(@"\b(black|black's)\s+$"),
        ["hr"] = R(@"\b(crn[iaeuo]m?|crnog|crnoj)\s+$"),
    };

    // ---- Stellung des Lösers ------------------------------------------------------------------------

    /// <summary>Spielt das Vorspiel (<paramref name="setupUci"/>) ab <paramref name="startFen"/> nach. <c>null</c>, wenn
    /// ein Zug dort nicht geht oder die FEN unlesbar ist (dann bleibt es beim alten Prompt ohne Prüfung).</summary>
    public static HintBoard? FromPuzzle(string startFen, string setupUci, IEnumerable<string> solutionUci)
    {
        if (string.IsNullOrWhiteSpace(startFen)) return null;
        var fen = startFen.Trim();
        foreach (var uci in setupUci.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = MoveComparisonBoard.PlayUci(fen, uci);
            if (next is null) return null;
            fen = next;
        }
        var pieces = ParsePlacement(fen);
        if (pieces is null) return null;
        var promotions = solutionUci
            .Select(m => m.Trim().ToLowerInvariant())
            .Where(m => m.Length == 5 && "qrbn".Contains(m[4]))
            .Select(m => m[4])
            .ToHashSet();
        return new HintBoard { Fen = fen, WhiteToMove = FenFields.WhiteToMove(fen), Pieces = pieces, Promotions = promotions };
    }

    private static List<(char, string)>? ParsePlacement(string fen)
    {
        var rows = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split('/');
        if (rows is not { Length: 8 }) return null;
        var list = new List<(char, string)>();
        for (var r = 0; r < 8; r++)
        {
            var file = 0;
            foreach (var ch in rows[r])
            {
                if (char.IsDigit(ch)) { file += ch - '0'; continue; }
                if (file > 7 || "kqrbnpKQRBNP".IndexOf(ch) < 0) return null;
                list.Add((ch, $"{(char)('a' + file)}{8 - r}"));
                file++;
            }
        }
        return list;
    }

    /// <summary>Figurenliste einer Seite, z. B. „Kg1, Rb1, Rh1, Bc1, Bf1, Ne3, pawns c3 c4 e4 f3 g2 h2“ (englische
    /// Kürzel, der Prompt ist englisch).</summary>
    internal static string PieceList(HintBoard board, bool white)
    {
        var own = board.Pieces.Where(p => char.IsUpper(p.Piece) == white).ToList();
        var parts = new List<string>();
        foreach (var t in "kqrbn")
            parts.AddRange(own.Where(p => char.ToLowerInvariant(p.Piece) == t)
                .OrderBy(p => p.Square[0]).ThenBy(p => p.Square[1])
                .Select(p => $"{char.ToUpperInvariant(t)}{p.Square}"));
        var pawns = own.Where(p => char.ToLowerInvariant(p.Piece) == 'p')
            .OrderBy(p => p.Square[0]).ThenBy(p => p.Square[1]).Select(p => p.Square).ToList();
        parts.Add(pawns.Count > 0 ? $"pawns {string.Join(' ', pawns)}" : "no pawns");
        return string.Join(", ", parts);
    }

    private static string TypeName(char t) => t switch
    {
        Queen => "queen", Rook => "rook", Bishop => "bishop", Knight => "knight", _ => "piece",
    };

    /// <summary>Prompt-Block: Löserstellung, Seite am Zug, Figurenliste je Seite, fehlende Figurenarten und die
    /// Anweisung, nur Figuren aus der Liste zu nennen.</summary>
    internal static string DescribeForPrompt(HintBoard board)
    {
        var sb = new StringBuilder();
        var solver = board.WhiteToMove ? "White" : "Black";
        sb.AppendLine($"Position the solver sees (FEN after all setup moves): {board.Fen}");
        sb.AppendLine($"Side to move (the solver, \"you\" in the hints): {solver}");
        sb.AppendLine($"Pieces on the board — White: {PieceList(board, true)}; Black: {PieceList(board, false)}");
        var missing = new List<string>();
        foreach (var t in CheckedTypes)
        {
            var w = board.Has(true, t);
            var b = board.Has(false, t);
            if (!w && !b) missing.Add($"no {TypeName(t)} at all");
            else if (!w) missing.Add($"White has no {TypeName(t)}");
            else if (!b) missing.Add($"Black has no {TypeName(t)}");
        }
        if (missing.Count > 0) sb.AppendLine($"Missing pieces: {string.Join("; ", missing)}.");
        if (board.Promotions.Count > 0)
            sb.AppendLine($"The solution promotes a pawn to: {string.Join(", ", board.Promotions.Select(TypeName))}.");
        sb.AppendLine("IMPORTANT: Mention only pieces that appear in this list (or arise by promotion in the solution). "
            + "Never name a piece type a side does not have, and never call a piece \"yours\" or the opponent's unless that side has it.");
        return sb.ToString();
    }

    // ---- Prüfung gegen das Brett --------------------------------------------------------------------

    /// <summary>Erste erfundene Figur im Tipp (<c>(Wort, Grund)</c>), <c>null</c> = passt zum Brett bzw. Sprache
    /// unbekannt.</summary>
    internal static (string Word, string Reason)? FindInventedPiece(string hint, string lang, HintBoard board)
    {
        if (string.IsNullOrWhiteSpace(hint) || !Words.TryGetValue(lang, out var words)) return null;
        var low = hint.ToLowerInvariant();
        foreach (var (type, pattern) in words)
        {
            foreach (Match m in pattern.Matches(low))
            {
                if (lang == "de" && type == Queen && GermanQueenWing.IsMatch(low[m.Index..])) continue;
                if (board.Promotions.Contains(type) || (type == Queen && QueenPromotion[lang].IsMatch(low))) continue;
                var from = Math.Max(0, m.Index - 15);
                var to = Math.Min(low.Length, m.Index + m.Length + 14);
                if (NonPiece[lang].IsMatch(low[from..to])) continue;

                if (!board.HasAny(type))
                    return (m.Value, $"there is no {TypeName(type)} on the board at all");

                var pre = low[Math.Max(0, m.Index - 40)..m.Index];
                bool? side = null;
                if (Own[lang].IsMatch(pre)) side = board.WhiteToMove;
                else if (Opponent[lang].IsMatch(pre)) side = !board.WhiteToMove;
                else if (White[lang].IsMatch(pre)) side = true;
                else if (Black[lang].IsMatch(pre)) side = false;
                if (side is bool s && !board.Has(s, type))
                {
                    var who = s == board.WhiteToMove ? "the solver" : "the opponent";
                    return (m.Value, $"{(s ? "White" : "Black")} ({who}) has no {TypeName(type)}");
                }
            }
        }
        return null;
    }
}
