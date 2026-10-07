using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Trainingslinien gegen einen Gegner (Wunsch 2026-10-07): EIN Dienst hinter den beiden dünnen Endpunkten
/// <c>GET /api/prep/player/{id}/training-lines</c> (Spielervorbereitung) und <c>GET /api/league/player/{fide}/training-lines</c>
/// (LeagueHub, nur angemeldet). Er lädt das Repertoire des Nutzers (nur eigene mit <c>UseForExtension</c> — in der Oberfläche
/// „Für Extension und Vorbereitung verwenden"), bestimmt die Farbe je Kapitel wie der Trainer, baut den Stellungsgraphen
/// (<see cref="RepertoireReach.Build"/>) und reiht die Linien mit <see cref="OpponentTrainingLines.Rank"/> gegen die Partien,
/// die der Aufrufer liefert. Die Liste seiner Repertoires geht mit, damit die Karte mit EINEM Aufruf auskommt.
/// </summary>
public sealed class TrainingLinesService(AppDbContext db, RepertoireService repertoires, IConfiguration config)
{
    /// <summary>So viele Linien liefert eine Antwort ohne <c>take</c> (der Rest als <c>more</c>); einstellbar über <see cref="TakeKey"/>.</summary>
    public const int DefaultTake = 50;
    public const string TakeKey = "Prep:TrainingLines";
    /// <summary>Mehr gibt es auch mit <c>take</c> nicht — „Alle in dieser Reihenfolge trainieren" fragt so viele.</summary>
    public const int MaxTake = 5000;

    private int DefaultTakeFromConfig => int.TryParse(config[TakeKey], out var n) ? Math.Clamp(n, 1, MaxTake) : DefaultTake;

    public sealed record RepertoireRef(int Id, string Name);

    /// <summary>Die Repertoires des Nutzers, die die Vorbereitung nutzen darf: EIGENE (keine geteilten) mit <c>UseForExtension</c>.</summary>
    public Task<List<RepertoireRef>> RepertoiresAsync(int userId, CancellationToken ct) =>
        db.Repertoires.AsNoTracking().Where(r => r.UserId == userId && r.UseForExtension)
            .OrderBy(r => r.Name).ThenBy(r => r.Id).Select(r => new RepertoireRef(r.Id, r.Name)).ToListAsync(ct);

    /// <summary>
    /// Die Antwort <c>{ repertoires[{ id, name }], repertoire, color, colors, games, total, lines[…], more }</c>; <c>null</c> = das
    /// verlangte Repertoire gehört dem Nutzer nicht oder ist nicht freigegeben (→ 404). Ohne <paramref name="repertoireId"/> das
    /// erste der Liste; ohne <paramref name="color"/> die Farbe mit den meisten Linien. <paramref name="chapterColors"/> =
    /// eigene Farb-Festlegungen je Kapitel aus dem Trainer (JSON <c>{ "Kapitel": "w"|"b" }</c>), sonst die Auto-Erkennung.
    /// <paramref name="games"/> wird nur gerufen, wenn es überhaupt ein Repertoire gibt.
    /// </summary>
    public async Task<JsonObject?> LinesAsync(int userId, int? repertoireId, string? color, string? chapterColors, int? take,
        Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct)
    {
        var list = await RepertoiresAsync(userId, ct);
        var o = new JsonObject
        {
            ["repertoires"] = new JsonArray(list.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["name"] = r.Name }).ToArray()),
        };
        RepertoireRef? rep;
        if (repertoireId is { } want)
        {
            rep = list.FirstOrDefault(r => r.Id == want);
            if (rep is null) return null;
        }
        else rep = list.FirstOrDefault();
        if (rep is null)
        {
            o["repertoire"] = null; o["color"] = null; o["colors"] = new JsonArray(); o["games"] = 0;
            o["total"] = 0; o["lines"] = new JsonArray(); o["more"] = 0;
            return o;
        }

        var sections = Sections(await repertoires.GetCombinedPgnAsync(rep.Id, userId));
        var colors = ChapterColors(sections, ParseOverrides(chapterColors));
        var byColor = sections.Where(s => s.Moves.Count > 0)
            .GroupBy(s => colors.GetValueOrDefault(Chapter(s), 'w'))
            .ToDictionary(g => g.Key, g => g.ToList());
        var available = new[] { 'w', 'b' }.Where(byColor.ContainsKey).ToList();
        char pick = color is "w" or "b" && byColor.ContainsKey(color[0]) ? color[0]
            : available.OrderByDescending(c => byColor[c].Count).ThenBy(c => c == 'w' ? 0 : 1).FirstOrDefault('w');

        o["repertoire"] = rep.Id;
        o["color"] = pick.ToString();
        o["colors"] = new JsonArray(available.Select(c => (JsonNode)c.ToString()).ToArray());
        if (!byColor.TryGetValue(pick, out var mine))
        {
            o["games"] = 0; o["total"] = 0; o["lines"] = new JsonArray(); o["more"] = 0;
            return o;
        }

        var graph = RepertoireReach.Build(mine, pick);
        // Build überspringt Abschnitte mit kaputter [FEN] — die Kapitel laufen dann nicht mehr parallel zu Mainlines.
        var chapters = graph.Mainlines.Count == mine.Count ? mine.Select(Chapter).ToList() : new List<string>();
        var r = OpponentTrainingLines.Rank(graph, chapters, await games());
        var n = Math.Clamp(take ?? DefaultTakeFromConfig, 1, MaxTake);
        o["games"] = r.Games;
        o["total"] = r.Lines.Count;
        o["lines"] = new JsonArray(r.Lines.Take(n).Select(l => (JsonNode)new JsonObject
        {
            ["key"] = l.Key,
            ["end"] = l.End,
            ["start"] = l.StartFen,
            ["chapter"] = l.Chapter,
            ["moves"] = new JsonArray(l.Sans.Select(s => (JsonNode)s).ToArray()),
            ["probability"] = Math.Round(l.Probability, 6),
            ["reached"] = l.Reached,
            ["lastYear"] = l.LastYear,
            ["neverReached"] = l.NeverReached,
        }).ToArray());
        o["more"] = Math.Max(0, r.Lines.Count - n);
        return o;
    }

    // ── Partien von LeagueHub ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Partien eines Ligaspielers wie seine Karte: alle Brettpartien (fremde + Vereinspartien, ohne eigene Startstellung) und —
    /// nur für Spieler von LeagueHub, wie Baum und Profil — die Online-Partien nach <paramref name="filter"/>. Liest nur über
    /// <see cref="LeagueProfileStore"/>; dort ändert sich nichts.
    /// </summary>
    public async Task<List<OpponentTrainingLines.Game>> LeagueGamesAsync(string fide, LeagueProfileStore.TreeFilter filter, CancellationToken ct)
    {
        var cutoff = filter.Years is { } y ? DateTime.UtcNow.AddYears(-y) : (DateTime?)null;
        var result = new List<OpponentTrainingLines.Game>();
        if (filter.Board)
        {
            var (name, board) = await new LeagueProfileStore(db).GamesAsync(fide, ct);
            foreach (var g in board)
            {
                var c = LeagueProfileBuilder.ColorOf(g, fide, name);
                if (c is null) continue;
                if (g.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;
                int? year = g.Headers.TryGetValue("Date", out var d) && d.Length >= 4 && d[..4].All(char.IsDigit)
                    ? int.Parse(d[..4], CultureInfo.InvariantCulture) : null;
                if (cutoff is { } cut && (year is null || year < cut.Year)) continue;
                var moveText = PgnParser.SplitGames(g.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
                result.Add(new(PgnParser.ExtractMainlineSans(moveText), c == "w", year));
            }
        }
        if (filter.Online && (await db.LeaguePlayerProfiles.AnyAsync(p => p.FideId == fide, ct)
                              || await LeagueOnlineAccountService.LeagueKnowsAsync(db, fide, ct)))
        {
            foreach (var g in await LeagueProfileStore.OnlineGames(db, fide, filter, cutoff)
                         .Select(g => new { g.Line, g.White, g.PlayedAt }).ToListAsync(ct))
                result.Add(new(g.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries), g.White, g.PlayedAt.Year));
        }
        return result;
    }

    // ── Repertoire: Abschnitte und Farben wie im Trainer ─────────────────────────────────────────

    private static readonly Regex EventSplit = new(@"(?=\[Event\s)", RegexOptions.Compiled);
    private static readonly Regex InfoMarker = new(@"\[%info\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InfoWhite = new(@"^[ \t]*\[White[ \t]+""Info \| ", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Die Abschnitte des Repertoires OHNE Info-Linien (Erklärungen) — der Trainer fragt sie nicht ab
    /// (<c>isInfoLineGame</c> in <c>repertoire-info-line.util.ts</c>, dieselben zwei Merkmale).</summary>
    internal static List<ParsedSection> Sections(string pgn) =>
        EventSplit.Split(pgn ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s) && !InfoMarker.IsMatch(s) && !InfoWhite.IsMatch(s))
            .SelectMany(PgnMoveTree.ParseSections)
            .ToList();

    private static string Chapter(ParsedSection s) => (s.Black ?? "").Trim();

    /// <summary>
    /// Trainingsfarbe je Kapitel — der Spiegel von <c>chapterColorsOf</c> (<c>repertoire-color.util.ts</c>): die Seite, die in der
    /// Mehrheit der Linien eines Kapitels den letzten Halbzug zieht; Gleichstand → die Gegenseite der Wurzel. Eigene Festlegungen
    /// überschreiben das.
    /// </summary>
    internal static Dictionary<string, char> ChapterColors(IEnumerable<ParsedSection> sections, IReadOnlyDictionary<string, char> overrides)
    {
        var agg = new Dictionary<string, (int W, int B, char Root)>(StringComparer.Ordinal);
        foreach (var s in sections)
        {
            var root = s.StartFen?.Split(' ') is { Length: > 1 } p && p[1] == "b" ? 'b' : 'w';
            var ch = Chapter(s);
            if (!agg.TryGetValue(ch, out var a)) a = (0, 0, root);
            if (s.Moves.Count > 0)
            {
                var last = (s.Moves.Count - 1) % 2 == 0 ? root : root == 'w' ? 'b' : 'w';
                a = last == 'w' ? (a.W + 1, a.B, a.Root) : (a.W, a.B + 1, a.Root);
            }
            agg[ch] = a;
        }
        var res = agg.ToDictionary(x => x.Key, x => x.Value.W > x.Value.B ? 'w' : x.Value.B > x.Value.W ? 'b' : x.Value.Root == 'w' ? 'b' : 'w',
            StringComparer.Ordinal);
        foreach (var (k, v) in overrides) res[k] = v;
        return res;
    }

    /// <summary>Eigene Festlegungen aus der Adresse; Unlesbares zählt als „keine".</summary>
    internal static Dictionary<string, char> ParseOverrides(string? json)
    {
        var res = new Dictionary<string, char>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json) || json.Length > 20_000) return res;
        try
        {
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new())
                if (v is "w" or "b") res[k.Trim()] = v[0];
        }
        catch (JsonException) { }
        return res;
    }
}
