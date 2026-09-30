using System.Text;
using System.Text.Json.Nodes;
using Chess;

namespace RookHub.Api.Services.League;

/// <summary>
/// Spielerkarte aus allen Partien eines Spielers (Portierung von games_build.py): Partien zusammenführen
/// (gespeicherter Bestand + neu geholte chess-results-Partien, Dubletten über Datum + Nachnamen + Ergebnis),
/// dann das Eröffnungsprofil — erster eigener Zug bzw. Antwort auf 1.e4/1.d4/anderes, häufigste Zugfolgen
/// (6 Halbzüge) mit Score aus Sicht des Spielers, die letzten 8 Partien.
///
/// <para>Die Farbe ist bei Lumbra-Partien über die FIDE-ID eindeutig (WhiteFideId/BlackFideId), bei
/// chess-results-Partien (ohne FIDE-ID im Kopf) über den Nachnamen. Die Schreibweise 2022/23 kannte kein
/// Komma („Kruckenhauser Arthur") — daher erst am Komma, sonst am ersten Leerzeichen trennen.</para>
/// </summary>
public static class LeagueProfileBuilder
{
    public sealed record Game(Dictionary<string, string> Headers, string Raw, string Source);

    public static string LastName(string? n)
    {
        n = (n ?? "").Trim();
        return (n.Contains(',') ? n.Split(',')[0] : n.Split(' ')[0]).Trim().ToLowerInvariant();
    }

    private static string H(Game g, string k) => g.Headers.TryGetValue(k, out var v) ? v : "";

    /// <summary>Punkte aus Sicht der Farbe („w"/„s"); unbekanntes Ergebnis = <c>null</c>.</summary>
    public static double? Points(Game g, string color) => Pts(H(g, "Result"), color);

    private static (string, string, string, string) Key(Game g) =>
        (H(g, "Date").Length >= 10 ? H(g, "Date")[..10] : H(g, "Date"), LastName(H(g, "White")), LastName(H(g, "Black")), H(g, "Result"));

    public static List<Game> Parse(string? pgn, string source) =>
        string.IsNullOrWhiteSpace(pgn) ? new()
            : PgnParser.SplitGameBlocks(pgn).Select(b => new Game(b.Headers, b.Raw.Trim(), source)).ToList();

    /// <summary>Farbe des Spielers in der Partie („w"/„s") oder null, wenn er nicht mitspielt.</summary>
    public static string? ColorOf(Game g, string fide, string name)
    {
        if (H(g, "WhiteFideId") == fide) return "w";
        if (H(g, "BlackFideId") == fide) return "s";
        var last = LastName(name);
        if (last.Length == 0) return null;
        if (LastName(H(g, "White")) == last) return "w";
        if (LastName(H(g, "Black")) == last) return "s";
        return null;
    }

    /// <summary>Bestand + neue Partien zusammenführen; Reihenfolge: neueste zuerst.</summary>
    public static List<Game> Merge(IEnumerable<Game> stored, IEnumerable<Game> fresh)
    {
        var byKey = new Dictionary<(string, string, string, string), Game>();
        foreach (var g in stored) byKey.TryAdd(Key(g), g);
        foreach (var g in fresh)
        {
            var k = Key(g);
            if (byKey.TryGetValue(k, out var old))
            {
                // „beide" = Lumbra UND chess-results; eine dritte Quelle ändert daran nichts.
                if (old.Source == "Lumbra" && g.Source == "chess-results") byKey[k] = old with { Source = "beide" };
            }
            else byKey[k] = g;
        }
        return byKey.Values.OrderByDescending(g => H(g, "Date"), StringComparer.Ordinal).ToList();
    }

    /// <summary>Quelle einer gespeicherten Partie: Lumbra trägt FIDE-IDs im Kopf, chess-results nicht.</summary>
    /// <summary>Kopfzeile mit der Quelle einer nachträglich eingespielten Sammlung (z. B. „Mega" = ChessBase-Megabase,
    /// <c>POST /api/league/admin/games</c>) — sie trägt selbst FIDE-IDs und sähe sonst wie Lumbra aus.</summary>
    public const string SourceHeader = "LeagueSource";

    public static string StoredSource(Dictionary<string, string> headers) =>
        headers.TryGetValue(SourceHeader, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim()
        : headers.ContainsKey("WhiteFideId") || headers.ContainsKey("BlackFideId") ? "Lumbra" : "chess-results";

    /// <summary>Die ersten <paramref name="n"/> Halbzüge als SAN (Schreibweise von Gera.Chess wie in
    /// <see cref="GamePlies"/>). Bewusst NICHT über <c>GamePlies.Parse</c>: das spielt die GANZE Partie nach und
    /// verwirft sie komplett, sobald irgendein späterer Zug nicht spielbar ist — hier zählt nur die Eröffnung, und
    /// games_build.py behält den lesbaren Anfang (<c>sans()</c> bricht beim ersten Fehler ab, der Präfix bleibt).</summary>
    private static List<string> Sans(Game g, int n)
    {
        var result = new List<string>(n);
        var moveText = PgnParser.SplitGames(g.Raw).Select(x => x.MoveText).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(moveText)) return result;
        ChessBoard board;
        try
        {
            board = g.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)
                ? ChessBoard.LoadFromFen(fen.Trim()) : new ChessBoard();
        }
        catch { return result; }
        foreach (var san in PgnParser.ExtractMainlineSans(moveText).Take(n))
        {
            try
            {
                var legal = board.Moves(generateSan: true);
                if (!board.Move(san)) break;
                var uci = PgnParser.ToUci(board.ExecutedMoves[^1]);
                var hit = Array.Find(legal, m => PgnParser.ToUci(m) == uci);
                result.Add(string.IsNullOrEmpty(hit?.San) ? san : hit.San);
            }
            catch { break; }
        }
        return result;
    }

    private static string Line(IReadOnlyList<string> moves)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < moves.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append('.');
            sb.Append(moves[i]);
        }
        return sb.ToString();
    }

    private static double? Pts(string res, string color) => res switch
    {
        "1-0" => color == "w" ? 1 : 0,
        "0-1" => color == "w" ? 0 : 1,
        "1/2-1/2" => .5,
        _ => null,
    };

    /// <summary>Eine Partie fürs Eröffnungsprofil: die ersten Halbzüge (SAN), die Farbe des Spielers („w"/„s"), seine Punkte.</summary>
    public sealed record ProfileGame(List<string> Moves, string Color, double? Points);

    /// <summary>
    /// Die Abschnitte des Eröffnungsprofils (<c>white</c>, <c>black_e4</c>, <c>black_d4</c>, <c>black_other</c>) in
    /// <paramref name="o"/> — für die gespeicherte Karte (<see cref="Build"/>) und für die gefilterte Fassung
    /// (<see cref="LeagueProfileStore.ProfileAsync"/>, 0.617.0). Partien ohne Züge zählen nicht.
    /// </summary>
    public static void AddSections(JsonObject o, IReadOnlyList<ProfileGame> games)
    {
        var white = games.Where(x => x.Color == "w" && x.Moves.Count > 0).ToList();
        var bE4 = games.Where(x => x.Color == "s" && x.Moves.Count > 0 && x.Moves[0] == "e4").ToList();
        var bD4 = games.Where(x => x.Color == "s" && x.Moves.Count > 0 && x.Moves[0] == "d4").ToList();
        var bOth = games.Where(x => x.Color == "s" && x.Moves.Count > 0 && x.Moves[0] is not "e4" and not "d4").ToList();
        o["white"] = Stats(white, m => m[0]);
        o["black_e4"] = Stats(bE4, m => m.Count > 1 ? m[1] : null);
        o["black_d4"] = Stats(bD4, m => m.Count > 1 ? m[1] : null);
        o["black_other"] = Stats(bOth, m => m.Count > 1 ? Line(m.Take(2).ToList()) : null);
    }

    private static JsonObject Stats(List<ProfileGame> items, Func<List<string>, string?> firstKey, int depth = 6)
    {
        JsonArray Agg(Func<List<string>, string?> keyf)
        {
            var count = new Dictionary<string, int>();
            var sum = new Dictionary<string, double>();
            var k = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var (m, _, p) in items)
            {
                var key = keyf(m);
                if (key is null) continue;
                if (!count.ContainsKey(key)) order.Add(key);
                count[key] = count.GetValueOrDefault(key) + 1;
                if (p is not null) { sum[key] = sum.GetValueOrDefault(key) + p.Value; k[key] = k.GetValueOrDefault(key) + 1; }
            }
            // wie Pythons Counter.most_common: nach Häufigkeit, bei Gleichstand in Reihenfolge des ersten Auftretens
            return new JsonArray(order.Select((key, i) => (key, i)).OrderByDescending(x => count[x.key]).ThenBy(x => x.i).Take(5)
                .Select(x => (JsonNode)new JsonArray(x.key, count[x.key],
                    k.GetValueOrDefault(x.key) > 0 ? (int)Math.Round(100 * sum[x.key] / k[x.key], MidpointRounding.ToEven) : null))
                .ToArray());
        }
        return new JsonObject
        {
            ["n"] = items.Count,
            ["first"] = Agg(firstKey),
            ["lines"] = Agg(m => m.Count >= depth ? Line(m.Take(depth).ToList()) : null),
        };
    }

    /// <summary>Wie viele „letzte Partien" die Karte zeigt.</summary>
    public const int RecentCount = 8;

    /// <summary>Die letzten Partien des Spielers in der Reihenfolge der Karte (<c>recent</c>) — dieselbe Auswahl liefert
    /// <see cref="LeagueProfileStore.RecentAsync"/> samt PGN zum Nachspielen.</summary>
    /// <param name="color"><c>w</c>/<c>s</c> = nur die letzten Partien mit dieser Farbe (0.592.0 — die Karte filtert oben nach
    /// Farbe, und acht gemischte gefiltert ließen oft nur drei übrig); <c>null</c> = beide, wie auf der Karte.</param>
    public static List<(Game G, string Color)> Recent(string fide, string name, List<Game> games, string? color = null) =>
        games.Select(g => (G: g, C: ColorOf(g, fide, name))).Where(x => x.C is not null && (color is null || x.C == color))
            .Select(x => (x.G, x.C!)).Take(RecentCount).ToList();

    /// <summary>Eine Zeile der „letzten Partien" in der Form der Karte (<c>recent</c>).</summary>
    public static JsonObject RecentEntry(Game g, string color) => new()
    {
        ["date"] = H(g, "Date"), ["event"] = H(g, "Event"),
        ["vs"] = H(g, color == "w" ? "Black" : "White"), ["vs_elo"] = H(g, color == "w" ? "BlackElo" : "WhiteElo"),
        ["color"] = color, ["score"] = Pts(H(g, "Result"), color), ["opening"] = Line(Sans(g, 8).Take(4).ToList()),
    };

    /// <summary>Profil-JSON (gleiche Form wie die Python-Fassung) + zusammengeführtes PGN.</summary>
    public static (JsonObject Profile, string Pgn, int Count) Build(string fide, string name, List<Game> games)
    {
        var mine = games.Select(g => (G: g, C: ColorOf(g, fide, name))).Where(x => x.C is not null)
            .Select(x => (x.G, C: x.C!)).ToList();
        var mv = mine.Select(x => (x.G, M: Sans(x.G, 8), x.C)).ToList();
        var years = mine.Select(x => H(x.G, "Date")).Where(d => d.Length >= 4 && d[..4].All(char.IsDigit)).Select(d => d[..4]).ToList();
        var src = new JsonObject();
        foreach (var grp in mine.GroupBy(x => x.G.Source)) src[grp.Key] = grp.Count();
        var profile = new JsonObject
        {
            ["fide"] = fide, ["name"] = name, ["n"] = mine.Count, ["with_moves"] = mv.Count(x => x.M.Count > 0),
            ["years"] = years.Count > 0 ? new JsonArray(years.Min(StringComparer.Ordinal), years.Max(StringComparer.Ordinal)) : null,
            ["src"] = src,
        };
        AddSections(profile, mv.Select(x => new ProfileGame(x.M, x.C, Pts(H(x.G, "Result"), x.C))).ToList());
        profile["recent"] = new JsonArray(mv.Take(RecentCount).Select(x => (JsonNode)new JsonObject
            {
                ["date"] = H(x.G, "Date"), ["event"] = H(x.G, "Event"),
                ["vs"] = H(x.G, x.C == "w" ? "Black" : "White"), ["vs_elo"] = H(x.G, x.C == "w" ? "BlackElo" : "WhiteElo"),
                ["color"] = x.C, ["score"] = Pts(H(x.G, "Result"), x.C), ["opening"] = Line(x.M.Take(4).ToList()),
            }).ToArray());
        var pgn = string.Join("\n\n", mine.Select(x => x.G.Raw)) + (mine.Count > 0 ? "\n" : "");
        return (profile, pgn, mine.Count);
    }
}
