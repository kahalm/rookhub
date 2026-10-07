using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
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

    /// <summary>Was eine Anfrage gewählt hat: Repertoire, Farbe, eigene Kapitelfarben, Grenze.</summary>
    public sealed record Query(int? Repertoire, string? Color, IReadOnlyDictionary<string, char> ChapterColors, int? Take);

    /// <summary>Rumpf von <c>POST …/training-repertoire</c> — dieselbe Auswahl wie die Abfrage (Filter wie Profil/Baum; <c>all</c>/<c>twin</c>
    /// nur in der Spielervorbereitung).</summary>
    public sealed record CreateRequest(int? Repertoire, string? Color, Dictionary<string, string>? ChapterColors, string? Source,
        string? Speeds, int? Years, bool? Unsure, bool? All, bool? Twin)
    {
        public Query ToQuery() => new(Repertoire, Color, Overrides(ChapterColors), null);
    }

    /// <summary>Eigene Festlegungen als Wörterbuch; Unbrauchbares fällt weg.</summary>
    internal static Dictionary<string, char> Overrides(IReadOnlyDictionary<string, string>? raw)
    {
        var res = new Dictionary<string, char>(StringComparer.Ordinal);
        foreach (var (k, v) in raw ?? new Dictionary<string, string>())
            if (v is "w" or "b" && res.Count < 2000) res[k.Trim()] = v[0];
        return res;
    }

    /// <summary>Ein Abschnitt des Repertoires: der unveränderte PGN-Text und was der Parser daraus macht.</summary>
    internal sealed record Section(string Raw, ParsedSection Parsed);

    /// <summary>Das Ergebnis der Rechnung samt allem, was Liste und Anlegen brauchen. <see cref="Rep"/> = <c>null</c>: der
    /// Nutzer hat kein freigegebenes Repertoire; <see cref="Ranked"/> = <c>null</c>: für die Farbe gibt es keine Linien.</summary>
    internal sealed record Computed(List<RepertoireRef> Repertoires, RepertoireRef? Rep, char? Color, List<char> Colors,
        OpponentTrainingLines.Result? Ranked, List<Section> Mine);

    /// <summary>Die gemeinsame Rechnung; <c>null</c> = das verlangte Repertoire gehört dem Nutzer nicht oder ist nicht freigegeben.</summary>
    internal async Task<Computed?> ComputeAsync(int userId, Query q, Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct)
    {
        var list = await RepertoiresAsync(userId, ct);
        RepertoireRef? rep;
        if (q.Repertoire is { } want)
        {
            rep = list.FirstOrDefault(r => r.Id == want);
            if (rep is null) return null;
        }
        else rep = list.FirstOrDefault();
        if (rep is null) return new Computed(list, null, null, [], null, []);

        var sections = RawSections(await repertoires.GetCombinedPgnAsync(rep.Id, userId));
        var colors = ChapterColors(sections.Select(x => x.Parsed), q.ChapterColors);
        // Abschnitte, die RepertoireReach.Build überspränge (kaputte [FEN]), gar nicht erst mitnehmen — so laufen
        // Hauptvarianten, Kapitel und PGN-Text parallel.
        var byColor = sections.Where(x => x.Parsed.Moves.Count > 0 && Loadable(x.Parsed))
            .GroupBy(x => colors.GetValueOrDefault(Chapter(x.Parsed), 'w'))
            .ToDictionary(g => g.Key, g => g.ToList());
        var available = new[] { 'w', 'b' }.Where(byColor.ContainsKey).ToList();
        char pick = q.Color is "w" or "b" && byColor.ContainsKey(q.Color[0]) ? q.Color[0]
            : available.OrderByDescending(c => byColor[c].Count).ThenBy(c => c == 'w' ? 0 : 1).FirstOrDefault('w');
        if (!byColor.TryGetValue(pick, out var mine)) return new Computed(list, rep, pick, available, null, []);

        var graph = RepertoireReach.Build(mine.Select(x => x.Parsed), pick);
        var chapters = mine.Select(x => Chapter(x.Parsed)).ToList();
        return new Computed(list, rep, pick, available, OpponentTrainingLines.Rank(graph, chapters, await games()), mine);
    }

    /// <summary>
    /// Die Antwort <c>{ repertoires[{ id, name }], repertoire, color, colors, games, total, lines[…], more }</c>; <c>null</c> = das
    /// verlangte Repertoire gehört dem Nutzer nicht oder ist nicht freigegeben (→ 404). Ohne Repertoire das erste der Liste; ohne
    /// Farbe die mit den meisten Linien. <see cref="Query.ChapterColors"/> = eigene Farb-Festlegungen je Kapitel aus dem Trainer.
    /// <paramref name="games"/> wird nur gerufen, wenn es überhaupt Linien gibt.
    /// </summary>
    public async Task<JsonObject?> LinesAsync(int userId, Query q, Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct)
    {
        if (await ComputeAsync(userId, q, games, ct) is not { } c) return null;
        var o = new JsonObject
        {
            ["repertoires"] = new JsonArray(c.Repertoires.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["name"] = r.Name }).ToArray()),
            ["repertoire"] = c.Rep?.Id,
            ["color"] = c.Color?.ToString(),
            ["colors"] = new JsonArray(c.Colors.Select(x => (JsonNode)x.ToString()).ToArray()),
        };
        var lines = c.Ranked?.Lines ?? [];
        var n = Math.Clamp(q.Take ?? DefaultTakeFromConfig, 1, MaxTake);
        o["games"] = c.Ranked?.Games ?? 0;
        o["total"] = lines.Count;
        o["lines"] = new JsonArray(lines.Take(n).Select(l => (JsonNode)new JsonObject
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
            ["matched"] = l.Matched,
            ["missing"] = l.Missing,
            ["prefixProbability"] = Math.Round(l.PrefixProbability, 6),
            ["prefixReached"] = l.PrefixReached,
        }).ToArray());
        o["more"] = Math.Max(0, lines.Count - n);
        return o;
    }

    // ── Trainings-Repertoire anlegen ─────────────────────────────────────────────────────────────

    /// <summary>So viele Linien kommen höchstens in ein Trainings-Repertoire — fest, auch wenn die Liste mehr zeigen darf.</summary>
    public const int MaxRepertoireLines = 50;

    public sealed record Created(int Id, string Name, int Lines, bool Replaced);

    /// <summary>Das Quell-Repertoire heißt selbst wie das Ziel („Prep: … Jahr") — Ersetzen würde die Quelle überschreiben → 400
    /// <c>{ reason: "sameRepertoire" }</c>.</summary>
    public sealed class SameRepertoireException(string name)
        : DomainValidationException($"Das gewählte Repertoire heißt selbst „{name}“ — es würde sich selbst überschreiben. Bitte ein anderes wählen oder es umbenennen.");

    /// <summary>Name des Trainings-Repertoires: „Prep: Huber, Franz 2026".</summary>
    public static string RepertoireName(string opponent, int year)
    {
        var name = $"Prep: {(string.IsNullOrWhiteSpace(opponent) ? "?" : opponent.Trim())} {year.ToString(CultureInfo.InvariantCulture)}";
        return name.Length <= 200 ? name : name[..200];
    }

    /// <summary>
    /// „Show me lines to train" (Wunsch 2026-10-07): legt dem Nutzer ein Repertoire „Prep: &lt;Gegner&gt; &lt;Jahr&gt;" an — die
    /// gereihten Linien (höchstens <see cref="MaxRepertoireLines"/>, Grenze sonst wie die Liste) in dieser Reihenfolge, je Linie
    /// der UNVERÄNDERTE PGN-Abschnitt der Quelle (Kopfzeilen, Kapitel, Kommentare, Varianten). Gibt es schon ein eigenes mit
    /// genau diesem Namen, wird dessen Inhalt ersetzt (Id, Freigabe-Häkchen und Trainingsstand je Linien-Schlüssel bleiben).
    /// Angelegt und befüllt über <see cref="RepertoireService"/> (Grenzen, PGN-Prüfung, Caches wie beim Hochladen).
    /// <c>null</c> = Repertoire fremd/nicht freigegeben; <see cref="DomainValidationException"/>, wenn es keine Linien gibt.
    /// </summary>
    /// <param name="opponentAfter">Name des Gegners, wenn er erst beim Laden der Partien feststeht (LeagueHub).</param>
    public async Task<Created?> CreateRepertoireAsync(int userId, string opponent, Query q,
        Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct, Func<string>? opponentAfter = null)
    {
        if (await ComputeAsync(userId, q, games, ct) is not { } c) return null;
        if (opponentAfter is not null) opponent = opponentAfter();
        if (c.Rep is null) throw new DomainValidationException("Kein Repertoire ist für die Vorbereitung freigegeben.");
        if (c.Ranked is not { Lines.Count: > 0 } ranked) throw new DomainValidationException("Das Repertoire hat für diese Farbe keine Linien.");

        var n = Math.Min(DefaultTakeFromConfig, MaxRepertoireLines);
        var picked = ranked.Lines.Take(n).ToList();
        var pgn = string.Join("\n\n", picked.Select(l => c.Mine[l.Index].Raw.Trim())) + "\n";

        var now = DateTime.UtcNow;
        var name = RepertoireName(opponent, now.Year);
        if (string.Equals(c.Rep.Name, name, StringComparison.Ordinal)) throw new SameRepertoireException(name);
        var colorText = c.Color == 'b' ? "Schwarz" : "Weiß";
        var description = $"Trainingslinien gegen {opponent} aus „{c.Rep.Name}“ ({colorText}), "
                          + $"{now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}, {ranked.Games} Partien gezählt, "
                          + $"{picked.Count} von {ranked.Lines.Count} Linien.";
        if (description.Length > 1000) description = description[..1000];
        var kind = await db.Repertoires.AsNoTracking().Where(r => r.Id == c.Rep.Id).Select(r => r.Kind).FirstAsync(ct);

        var existing = await db.Repertoires.AsNoTracking().Where(r => r.UserId == userId && r.Name == name)
            .OrderBy(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        int id;
        if (existing is { } old)
        {
            id = old;
            foreach (var fileId in await db.RepertoireFiles.AsNoTracking().Where(f => f.RepertoireId == id).Select(f => f.Id).ToListAsync(ct))
                await repertoires.DeleteFileAsync(id, fileId, userId);
            await repertoires.UpdateAsync(id, userId, new UpdateRepertoireDto { Description = description, Kind = kind });
        }
        else
        {
            id = (await repertoires.CreateAsync(userId, new CreateRepertoireDto
            {
                Name = name, Description = description, Kind = kind, UseForExtension = false,
            })).Id;
        }
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn));
        await repertoires.UploadFileAsync(id, userId, "prep-training.pgn", stream);
        return new Created(id, name, picked.Count, existing is not null);
    }

    // ── Partien von LeagueHub ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Partien eines Ligaspielers wie seine Karte: alle Brettpartien (fremde + Vereinspartien, ohne eigene Startstellung) und —
    /// nur für Spieler von LeagueHub, wie Baum und Profil — die Online-Partien nach <paramref name="filter"/>. Liest nur über
    /// <see cref="LeagueProfileStore"/>; dort ändert sich nichts.
    /// </summary>
    public async Task<(string Name, List<OpponentTrainingLines.Game> Games)> LeagueGamesAsync(string fide, LeagueProfileStore.TreeFilter filter,
        CancellationToken ct)
    {
        var cutoff = filter.Years is { } y ? DateTime.UtcNow.AddYears(-y) : (DateTime?)null;
        var result = new List<OpponentTrainingLines.Game>();
        // der Name auch ohne Brettpartien (Online-Filter) — für den Namen des Trainings-Repertoires
        var (name, board) = await new LeagueProfileStore(db).GamesAsync(fide, ct);
        if (filter.Board)
        {
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
        return (name, result);
    }

    // ── Repertoire: Abschnitte und Farben wie im Trainer ─────────────────────────────────────────

    private static readonly Regex EventSplit = new(@"(?=\[Event\s)", RegexOptions.Compiled);
    private static readonly Regex InfoMarker = new(@"\[%info\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InfoWhite = new(@"^[ \t]*\[White[ \t]+""Info \| ", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Die Abschnitte des Repertoires OHNE Info-Linien (Erklärungen) — der Trainer fragt sie nicht ab
    /// (<c>isInfoLineGame</c> in <c>repertoire-info-line.util.ts</c>, dieselben zwei Merkmale).</summary>
    internal static List<ParsedSection> Sections(string pgn) => RawSections(pgn).Select(x => x.Parsed).ToList();

    /// <summary>Wie <see cref="Sections"/>, je Abschnitt mit seinem unveränderten PGN-Text.</summary>
    internal static List<Section> RawSections(string pgn) =>
        EventSplit.Split(pgn ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s) && !InfoMarker.IsMatch(s) && !InfoWhite.IsMatch(s))
            .SelectMany(raw => PgnMoveTree.ParseSections(raw).Select(p => new Section(raw, p)))
            .ToList();

    /// <summary>Nimmt <see cref="RepertoireReach.Build"/> diesen Abschnitt? (Eine unbrauchbare <c>[FEN]</c> lässt es ihn auslassen.)</summary>
    private static bool Loadable(ParsedSection s)
    {
        if (s.StartFen is null) return true;
        try { Chess.ChessBoard.LoadFromFen(s.StartFen); return true; }
        catch { return false; }
    }

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
        try { return Overrides(JsonSerializer.Deserialize<Dictionary<string, string>>(json)); }
        catch (JsonException) { }
        return res;
    }
}
