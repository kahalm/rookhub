namespace RookHub.Api.Services;

/// <summary>
/// Der Lehrplan der Kinderseite: welche Lichess-Puzzles als „besonders einfach" gelten und in welcher
/// Reihenfolge sie als Stufen serviert werden. Reine Rechnung ohne Datenbank — gefuellt wird die
/// Tabelle von <see cref="KidsPuzzleService.RebuildAsync"/>.
///
/// <para><b>Was „besonders einfach" heisst</b> (Vorgabe des Nutzers, 2026-09-27): aus den Puzzles mit
/// dem niedrigsten Rating die, deren Loesung 1 (hoechstens 2) eigene Zuege lang ist und bei denen wenig
/// Figuren auf dem Brett stehen. Innerhalb eines Themas zaehlt deshalb die Figurenzahl vor dem Rating
/// (<see cref="Score"/>): vier Figuren mit Rating 690 sind fuer ein Kind leichter als zwoelf mit 610.</para>
///
/// <para><b>Warum Themen-Stufen statt einer einzigen Rangliste</b>: fast alle Lichess-Puzzles unter 700
/// sind „Matt in 1" (Dev-Bestand: die ersten 600 der Rangliste waren es ausnahmslos). Eine reine
/// Rangliste waere 30 Stufen lang dasselbe. Der Lehrplan wechselt darum das Thema und fuehrt die
/// Zwei-Zug-Motive (Gabel, Spiess, Fesselung, …) erst ein, wenn die Ein-Zug-Aufgaben sitzen. Das Thema
/// steht an der Stufe und sagt dem Kind, was es suchen soll.</para>
///
/// <para>Ergebnis ist deterministisch (Gleichstand → <c>LichessId</c>), damit Dev und Prod aus demselben
/// Lichess-Bestand dieselbe Leiter bauen, obwohl ihre Puzzle-Ids verschieden sind.</para>
/// </summary>
public static class KidsCurriculum
{
    /// <summary>Stand des Lehrplans. Erhoehen, sobald sich Auswahl oder Stufenfolge aendert — der
    /// <see cref="KidsPuzzleSeeder"/> baut die Leiter dann beim naechsten Start neu.</summary>
    public const int Version = 1;

    public const int PuzzlesPerLevel = 10;

    /// <summary>Eine Stufe mit weniger Aufgaben faellt weg (Thema im Bestand zu duenn).</summary>
    public const int MinPuzzlesPerLevel = 3;

    // Vorfilter (auch in der DB-Abfrage): nur die leichtesten, gut erprobten Aufgaben.
    public const int MaxRating = 900;
    public const int MaxRatingDeviation = 90;
    public const int MinPopularity = 80;
    public const int MinPlays = 50;
    public const int MaxPieces = 12;

    /// <summary>Laengste Zugfolge, die in Frage kommt: Setup + 2 eigene Zuege + 1 Antwort, jeder Zug mit
    /// Umwandlungsbuchstaben („e7e8q") — 4 × 5 Zeichen + 3 Leerzeichen.</summary>
    public const int MaxMovesLength = 23;

    /// <summary>Aus wie vielen der leichtesten Aufgaben eines Themas je Stufe ausgewaehlt wird. Bei 4 greift
    /// jede Stufe ein Viertel ihres Abschnitts ab — die Stufen eines Themas werden also merklich schwerer,
    /// statt dass acht „Matt in 1"-Stufen alle bei vier Figuren haengen bleiben.</summary>
    public const int PoolFactor = 4;

    /// <summary>Die Themen. Die Schluessel gehen so an die Kinderseite (Uebersetzung dort).
    /// <c>mate1</c>/<c>promote</c> sind Ein-Zug-Aufgaben, alle anderen Zwei-Zug-Aufgaben.</summary>
    public static readonly IReadOnlyList<string> Themes =
        ["mate1", "promote", "capture", "fork", "skewer", "mate2", "pin", "discovered"];

    /// <summary>
    /// Die Stufenfolge: vier Ein-Zug-Stufen (Matt, Umwandeln), dann die freie Figur als erster
    /// Zwei-Zueger, ab Stufe 10 Gabel, Spiess, Matt in 2, Fesselung und Abzug im Wechsel.
    /// <para>„Freie Figur schlagen" gibt es nur als Zwei-Zueger: Lichess-Puzzles ohne Matt brauchen fast
    /// immer zwei eigene Zuege — im Dev-Bestand gab es unter Rating 900 genau EIN Ein-Zug-Puzzle, das eine
    /// Figur schlaegt, ohne mattzusetzen, aber 250 Zwei-Zueger mit ≤ 12 Figuren, deren erster Zug die
    /// ungedeckte Figur nimmt.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Levels =
    [
        "mate1", "mate1", "promote", "mate1", "capture", "mate1", "promote", "capture", "mate1", "fork",
        "mate1", "capture", "promote", "fork", "skewer", "mate2", "capture", "mate1", "fork", "mate2",
        "skewer", "pin", "capture", "mate1", "discovered", "fork", "mate2", "skewer", "promote", "pin",
        "capture", "fork", "discovered", "mate2", "mate1", "skewer", "pin", "fork", "discovered", "mate2",
    ];

    /// <summary>Themen, die ein Kind verwirren: en passant, Rochade als Loesung, Unterverwandlung.</summary>
    internal static readonly string[] ExcludedThemes = ["enPassant", "castling", "underPromotion"];

    /// <summary>Ein Puzzle aus dem Bestand, so weit die Auswahl es braucht.</summary>
    public sealed record Candidate(int PuzzleId, string LichessId, int Rating, int RatingDeviation,
        int Popularity, int NbPlays, string? Themes, string Fen, string Moves);

    /// <summary>Ein ausgewaehltes Puzzle mit seinem Platz in der Leiter.</summary>
    public sealed record Placement(int PuzzleId, int Level, int Position, string Theme, int PieceCount,
        int SolverMoves);

    /// <summary>Figuren in der FEN-Stellung (inkl. Koenige).</summary>
    public static int CountPieces(string fen)
    {
        var board = fen.Split(' ', 2)[0];
        var count = 0;
        foreach (var c in board)
            if (char.IsLetter(c)) count++;
        return count;
    }

    /// <summary>Leichter ist kleiner: Figurenzahl vor Rating.</summary>
    public static int Score(int pieces, int rating) => pieces * 40 + rating;

    /// <summary>
    /// Thema des Puzzles fuer die Kinder-Leiter oder <c>null</c>, wenn es nicht in Frage kommt.
    /// Lichess-Zugfolge: <c>moves[0]</c> ist der Zug des Gegners, der die Aufgabe stellt, danach wechseln
    /// Loeserzug und Antwort — 2 Zuege = 1 eigener, 4 Zuege = 2 eigene.
    /// </summary>
    public static string? Classify(Candidate c)
    {
        if (c.Rating > MaxRating || c.RatingDeviation > MaxRatingDeviation
            || c.Popularity < MinPopularity || c.NbPlays < MinPlays)
            return null;

        var moves = c.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (moves.Length is not (2 or 4)) return null;
        if (CountPieces(c.Fen) > MaxPieces) return null;

        var themes = (c.Themes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (ExcludedThemes.Any(themes.Contains)) return null;

        if (moves.Length == 2)
        {
            // Umwandeln nur, wenn der Loesungszug selbst eine Dame macht — das Thema allein koennte auch
            // eine Umwandlung des Gegners meinen.
            if (themes.Contains("promotion") && moves[1].Length == 5 && moves[1][4] == 'q') return "promote";
            if (themes.Contains("mateIn1")) return "mate1";
            return null;
        }

        // Zwei-Zueger: das spezifischere Motiv zuerst — eine Gabel, die nebenbei eine freie Figur nimmt,
        // ist fuer das Kind eine Gabel.
        if (themes.Contains("mateIn2")) return "mate2";
        if (themes.Contains("fork")) return "fork";
        if (themes.Contains("skewer")) return "skewer";
        if (themes.Contains("pin")) return "pin";
        if (themes.Contains("discoveredAttack")) return "discovered";
        if (themes.Contains("hangingPiece") && IsCapture(c.Fen, moves[0], moves[1])) return "capture";
        return null;
    }

    /// <summary>
    /// Stellt die Leiter zusammen: je Thema die leichtesten Aufgaben (<see cref="Score"/>), auf die
    /// Stufen dieses Themas verteilt, in der Reihenfolge von <see cref="Levels"/>. Stufen mit zu wenig
    /// Aufgaben fallen weg; die Nummern bleiben lueckenlos.
    /// </summary>
    public static List<Placement> Select(IEnumerable<Candidate> candidates)
    {
        var byGroup = candidates
            .Select(c => (Candidate: c, Group: Classify(c), Pieces: CountPieces(c.Fen)))
            .Where(x => x.Group is not null)
            .GroupBy(x => x.Group!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => Score(x.Pieces, x.Candidate.Rating))
                      .ThenBy(x => x.Candidate.LichessId, StringComparer.Ordinal)
                      .ToList());

        var levelsPerGroup = Levels.GroupBy(g => g).ToDictionary(g => g.Key, g => g.Count());
        var seenPerGroup = new Dictionary<string, int>();
        var result = new List<Placement>();
        var level = 0;

        foreach (var group in Levels)
        {
            var nth = seenPerGroup.GetValueOrDefault(group);
            seenPerGroup[group] = nth + 1;
            if (!byGroup.TryGetValue(group, out var ordered)) continue;

            var levelCount = levelsPerGroup[group];
            var pool = Math.Min(ordered.Count, PoolFactor * PuzzlesPerLevel * levelCount);
            // Abschnitt dieser Stufe im Pool — die Stufen eines Themas teilen ihn sich der Reihe nach.
            var from = nth * pool / levelCount;
            var to = (nth + 1) * pool / levelCount;
            var slice = ordered.GetRange(from, to - from);
            var picked = Spread(slice, PuzzlesPerLevel);
            if (picked.Count < MinPuzzlesPerLevel) continue;

            level++;
            for (var i = 0; i < picked.Count; i++)
            {
                var (c, _, pieces) = picked[i];
                result.Add(new Placement(c.PuzzleId, level, i, group, pieces,
                    c.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 2));
            }
        }
        return result;
    }

    /// <summary>Nimmt <paramref name="count"/> Eintraege gleichmaessig verteilt aus einer (sortierten)
    /// Liste — die Stufe deckt ihren ganzen Abschnitt ab, leichteste zuerst.</summary>
    private static List<T> Spread<T>(List<T> items, int count)
    {
        if (items.Count <= count) return items;
        var picked = new List<T>(count);
        for (var i = 0; i < count; i++)
            picked.Add(items[i * items.Count / count]);
        return picked;
    }

    /// <summary>
    /// Schlaegt <paramref name="solverMove"/> eine Figur — nach dem Stellungszug <paramref name="setupMove"/>?
    /// Genuegt ein Blick aufs Zielfeld: en passant ist ausgeschlossen (<see cref="ExcludedThemes"/>), und
    /// eine Rochade als Stellungszug versetzt den Turm, der hier nur als Ziel zaehlt.
    /// </summary>
    internal static bool IsCapture(string fen, string setupMove, string solverMove)
    {
        var board = ParseBoard(fen);
        if (board is null || setupMove.Length < 4 || solverMove.Length < 4) return false;

        var from = Square(setupMove, 0);
        var to = Square(setupMove, 2);
        if (from < 0 || to < 0) return false;
        var piece = board[from];
        board[to] = piece;
        board[from] = '.';
        // Rochade des Gegners: der Koenig springt zwei Linien, der Turm landet daneben.
        if (char.ToLowerInvariant(piece) == 'k' && Math.Abs(from % 8 - to % 8) == 2)
        {
            var rank = to / 8;
            var (rookFrom, rookTo) = to % 8 == 6 ? (rank * 8 + 7, rank * 8 + 5) : (rank * 8, rank * 8 + 3);
            board[rookTo] = board[rookFrom];
            board[rookFrom] = '.';
        }

        var target = Square(solverMove, 2);
        return target >= 0 && board[target] != '.';
    }

    /// <summary>64 Felder, a1 = 0 … h8 = 63; <c>'.'</c> = leer.</summary>
    private static char[]? ParseBoard(string fen)
    {
        var rows = fen.Split(' ', 2)[0].Split('/');
        if (rows.Length != 8) return null;
        var board = new char[64];
        for (var r = 0; r < 8; r++)
        {
            var file = 0;
            foreach (var c in rows[r])
            {
                if (char.IsDigit(c))
                {
                    for (var k = 0; k < c - '0' && file < 8; k++) board[(7 - r) * 8 + file++] = '.';
                }
                else if (file < 8)
                {
                    board[(7 - r) * 8 + file++] = c;
                }
            }
            if (file != 8) return null;
        }
        return board;
    }

    private static int Square(string uci, int offset)
    {
        var f = uci[offset] - 'a';
        var r = uci[offset + 1] - '1';
        return f is >= 0 and < 8 && r is >= 0 and < 8 ? r * 8 + f : -1;
    }
}
