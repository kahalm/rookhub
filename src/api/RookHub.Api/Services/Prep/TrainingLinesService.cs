using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
public sealed class TrainingLinesService(AppDbContext db, RepertoireService repertoires, IConfiguration config,
    ITrainingExplorer? explorer = null, IMemoryCache? cache = null, ILogger<TrainingLinesService>? logger = null,
    TrainingLinesContinuation? continuation = null)
{
    /// <summary>So lange bleibt eine fertige Reihung (samt Schätzung) im Speicher — je Nutzer, Gegner und Auswahl; ein geändertes
    /// Repertoire (<c>UpdatedAt</c>) rechnet sofort neu (Tempo, 2026-10-08: gemessen 21–22 s je Abfrage auf Prod).</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    /// <summary>Speicher-Bereich einer Abfrage: wer der Gegner ist (<c>prep:42:all:twin</c>, <c>league:1606921</c>) und mit welchem Filter.</summary>
    public static string Scope(string opponent, LeagueProfileStore.TreeFilter f) =>
        $"{opponent}|{f.Source}|{string.Join(',', f.Speeds)}|{f.Years}|{(f.OnlySure ? "sure" : "all")}";

    /// <summary>Ab so vielen weitergespielten Partien in einer Stellung zählen SEINE Züge; darunter schätzt der Lichess-Explorer.
    /// Vorgabe 1 — seine Züge haben Vorrang (Wunsch 2026-10-07: „mach seine züge immer oberste priorität - geschätzt nur wenn seine
    /// nicht reichen"). Einstellbar über <see cref="MinOwnKey"/>.</summary>
    public const int DefaultMinOwn = 1;
    public const string MinOwnKey = "Prep:TrainingMinOwnGames";
    private int MinOwn => int.TryParse(config[MinOwnKey], out var n) ? Math.Clamp(n, 1, 1000) : DefaultMinOwn;

    /// <summary>Harte Frist je Anfrage für die ganze Rechnung (Hotfix 2026-10-08: auf Prod 500 nach 60 s, weil nginx kappte);
    /// höchstens <see cref="MaxDeadlineSeconds"/>. Einstellbar über <see cref="DeadlineKey"/>.</summary>
    public const int DefaultDeadlineSeconds = 20;
    public const int MaxDeadlineSeconds = 30;
    public const string DeadlineKey = "Prep:TrainingDeadlineSeconds";
    /// <summary>Davon höchstens so lange für den Explorer (<see cref="ExplorerBudgetKey"/>).</summary>
    public const int DefaultExplorerSeconds = 12;
    public const string ExplorerBudgetKey = "Prep:TrainingExplorerSeconds";
    /// <summary>Höchstens so viele NOCH NICHT gespeicherte Explorer-Stellungen je Anfrage neu anfragen, die wichtigsten zuerst
    /// (gespeicherte zählen nicht dagegen, 0.725.2); der Rest bleibt offen (<see cref="MaxPositionsKey"/>).</summary>
    public const int DefaultMaxPositions = 1500;
    public const string MaxPositionsKey = "Prep:TrainingExplorerMaxPositions";

    private TimeSpan Deadline => TimeSpan.FromSeconds(
        int.TryParse(config[DeadlineKey], out var n) ? Math.Clamp(n, 1, MaxDeadlineSeconds) : DefaultDeadlineSeconds);
    private TimeSpan ExplorerBudget => TimeSpan.FromSeconds(
        int.TryParse(config[ExplorerBudgetKey], out var n) ? Math.Clamp(n, 0, MaxDeadlineSeconds) : DefaultExplorerSeconds);
    private int MaxPositions => int.TryParse(config[MaxPositionsKey], out var n) ? Math.Clamp(n, 0, 100_000) : DefaultMaxPositions;
    /// <summary>So viel Luft bleibt nach dem Explorer für Reihen und Antwort.</summary>
    private static readonly TimeSpan DeadlineReserve = TimeSpan.FromSeconds(2);

    /// <summary>So viele Linien liefert eine Antwort ohne <c>take</c> (der Rest als <c>more</c>); einstellbar über <see cref="TakeKey"/>.</summary>
    public const int DefaultTake = 50;
    public const string TakeKey = "Prep:TrainingLines";
    /// <summary>Mehr gibt es auch mit <c>take</c> nicht — „Alle in dieser Reihenfolge trainieren" fragt so viele.</summary>
    public const int MaxTake = 5000;

    private int DefaultTakeFromConfig => int.TryParse(config[TakeKey], out var n) ? Math.Clamp(n, 1, MaxTake) : DefaultTake;

    public sealed record RepertoireRef(int Id, string Name);

    /// <summary>Die Repertoires des Nutzers, die die Vorbereitung nutzt: EIGENE (keine geteilten) mit <c>UseForExtension</c>
    /// („Für Extension und Vorbereitung verwenden"), nach Name — das ist auch die Reihenfolge, in der bei gleicher Linie in
    /// mehreren Repertoires das erste gewinnt.</summary>
    public Task<List<RepertoireRef>> RepertoiresAsync(int userId, CancellationToken ct) =>
        db.Repertoires.AsNoTracking().Where(r => r.UserId == userId && r.UseForExtension)
            .OrderBy(r => r.Name).ThenBy(r => r.Id).Select(r => new RepertoireRef(r.Id, r.Name)).ToListAsync(ct);

    /// <summary>Eigene Farb-Festlegungen je Kapitel: <see cref="Flat"/> = <c>{ "Kapitel": "w" }</c> (gilt für ein EINZELN gewähltes
    /// Repertoire — so schickt sie der Trainer), <see cref="PerRepertoire"/> = <c>{ "7": { "Kapitel": "b" } }</c> (je Repertoire —
    /// so schickt sie die Karte für alle markierten).</summary>
    public sealed record ChapterOverrides(IReadOnlyDictionary<string, char> Flat, IReadOnlyDictionary<int, IReadOnlyDictionary<string, char>> PerRepertoire)
    {
        public static readonly ChapterOverrides None = new(new Dictionary<string, char>(), new Dictionary<int, IReadOnlyDictionary<string, char>>());

        public IReadOnlyDictionary<string, char> For(int repertoireId, bool single) =>
            PerRepertoire.TryGetValue(repertoireId, out var m) ? m : single ? Flat : None.Flat;
    }

    /// <summary>Was eine Anfrage gewählt hat: Repertoire (<c>null</c> = alle markierten), Farbe, eigene Kapitelfarben, Grenze.</summary>
    public sealed record Query(int? Repertoire, string? Color, ChapterOverrides ChapterColors, int? Take);

    /// <summary>Rumpf von <c>POST …/training-repertoire</c> — dieselbe Auswahl wie die Abfrage (Filter wie Profil/Baum; <c>all</c>/<c>twin</c>
    /// nur in der Spielervorbereitung). <c>chapterColors</c> in einer der beiden Formen von <see cref="ChapterOverrides"/>.</summary>
    /// <param name="Replace">Ein vorhandenes gleichnamiges eigenes Repertoire ersetzen; ohne → <see cref="RepertoireExistsException"/>
    /// (409 <c>exists</c>), damit die Seite erst nachfragt.</param>
    public sealed record CreateRequest(int? Repertoire, string? Color, JsonObject? ChapterColors, string? Source,
        string? Speeds, int? Years, bool? Unsure, bool? All, bool? Twin, bool? Replace = null)
    {
        public Query ToQuery() => new(Repertoire, Color, ParseOverrides(ChapterColors?.ToJsonString()), null);
    }

    /// <summary>Ein Abschnitt eines Repertoires: der unveränderte PGN-Text und was der Parser daraus macht.</summary>
    internal sealed record Section(string Raw, ParsedSection Parsed);

    /// <summary>Ein Abschnitt samt seinem Repertoire — so laufen die Linien aller markierten Repertoires in EINE Reihung.</summary>
    internal sealed record Source(RepertoireRef Rep, Section Section);

    /// <summary>Ein markiertes Repertoire mit den Farben seiner Kapitel (für die Auswahl der Karte).</summary>
    internal sealed record RepertoireInfo(RepertoireRef Rep, List<char> Colors);

    /// <summary>Das Ergebnis der Rechnung samt allem, was Liste und Anlegen brauchen. <see cref="Ranked"/> = <c>null</c>: für die
    /// Farbe gibt es keine Linien (oder gar kein markiertes Repertoire). <see cref="Mine"/> läuft parallel zu den Hauptvarianten.</summary>
    internal sealed record Computed(List<RepertoireInfo> Repertoires, int? Selected, char? Color, List<char> Colors,
        OpponentTrainingLines.Result? Ranked, List<Source> Mine, List<RepertoireRef> Sources, string? Band = null,
        bool ExplorerIncomplete = false, int ExplorerPending = 0, bool ExplorerRunning = false);

    /// <summary>
    /// Die gemeinsame Rechnung. Quellen: das gewählte Repertoire oder — ohne Wahl — ALLE markierten (Wunsch 2026-10-07: „nicht ein
    /// repertoir auswählen sondern die markierten verwenden"), je nur die Kapitel der Farbe; ihre Linien laufen in EINE Reihung.
    /// Gleiche Linie in mehreren Repertoires: das erste in <see cref="RepertoiresAsync"/>-Reihenfolge (Name) gewinnt.
    /// <paramref name="exclude"/>: Repertoire dieses Namens nicht als Quelle (das Ziel des Anlegens); bleibt dann keine Quelle,
    /// <see cref="SameRepertoireException"/>. <c>null</c> = das verlangte Repertoire gehört dem Nutzer nicht / ist nicht markiert.
    /// </summary>
    /// <param name="scope">Wer der Gegner ist und mit welchem Filter (z. B. <c>prep:42|all|board</c>) — mit ihm wird das Ergebnis
    /// <see cref="CacheFor"/> lang gehalten; <c>null</c> = nicht halten.</param>
    internal async Task<Computed?> ComputeAsync(int userId, Query q, Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct,
        string? exclude = null, Func<Task<int?>>? elo = null, string? scope = null)
    {
        if (scope is null || cache is null) return await ComputeCoreAsync(userId, q, games, ct, exclude, elo, null);
        // Schlüssel mit Stand (UpdatedAt) jedes markierten Repertoires: ändert sich eines oder die Markierung, rechnet es neu
        var stamps = await db.Repertoires.AsNoTracking().Where(r => r.UserId == userId && r.UseForExtension)
            .OrderBy(r => r.Id).Select(r => new { r.Id, r.UpdatedAt }).ToListAsync(ct);
        var overrides = JsonSerializer.Serialize(new
        {
            flat = q.ChapterColors.Flat.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value),
            per = q.ChapterColors.PerRepertoire.OrderBy(x => x.Key).Select(x => x.Key + ":" + string.Join(",",
                x.Value.OrderBy(y => y.Key, StringComparer.Ordinal).Select(y => y.Key + "=" + y.Value))),
        });
        var key = $"training-lines:{userId}:{scope}:{q.Repertoire}:{q.Color}:{exclude}:{overrides}:"
                  + string.Join(",", stamps.Select(x => $"{x.Id}@{x.UpdatedAt.Ticks}"));
        if (cache.TryGetValue(key, out Computed? hit) && hit is not null) return hit;
        var computed = await ComputeCoreAsync(userId, q, games, ct, exclude, elo, key);
        // Unvollständig NICHT halten: die nächste Anfrage kommt mit den inzwischen gespeicherten Stellungen weiter.
        if (computed is not null && !computed.ExplorerIncomplete) cache.Set(key, computed, CacheFor);
        return computed;
    }

    private async Task<Computed?> ComputeCoreAsync(int userId, Query q, Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct,
        string? exclude, Func<Task<int?>>? elo, string? cacheKey)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var list = await RepertoiresAsync(userId, ct);
        if (q.Repertoire is { } want && list.All(r => r.Id != want)) return null;

        // Alle markierten laden — die Karte braucht die Farben jedes Repertoires für die Auswahl.
        var byRep = new List<(RepertoireRef Rep, List<(Section Section, char Color)> Sections)>();
        foreach (var rep in list)
        {
            var sections = RawSections(await repertoires.GetCombinedPgnAsync(rep.Id, userId))
                // Abschnitte, die RepertoireReach.Build überspränge (kaputte [FEN]), gar nicht erst — sonst liefen Hauptvarianten,
                // Kapitel und PGN-Text auseinander.
                .Where(x => x.Parsed.Moves.Count > 0 && Loadable(x.Parsed)).ToList();
            var colors = ChapterColors(sections.Select(x => x.Parsed), q.ChapterColors.For(rep.Id, q.Repertoire == rep.Id));
            byRep.Add((rep, sections.Select(x => (x, colors.GetValueOrDefault(Chapter(x.Parsed), 'w'))).ToList()));
        }
        var parseMs = clock.ElapsedMilliseconds;
        var infos = byRep.Select(x => new RepertoireInfo(x.Rep,
            new[] { 'w', 'b' }.Where(c => x.Sections.Any(s => s.Color == c)).ToList())).ToList();

        var sources = byRep.Where(x => q.Repertoire is null || x.Rep.Id == q.Repertoire).ToList();
        if (exclude is not null && sources.Any(x => x.Rep.Name == exclude))
        {
            sources = sources.Where(x => x.Rep.Name != exclude).ToList();
            if (sources.Count == 0) throw new SameRepertoireException(exclude);
        }
        var counts = new[] { 'w', 'b' }.ToDictionary(c => c, c => sources.Sum(x => x.Sections.Count(s => s.Color == c)));
        var available = counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
        char? pick = q.Color is "w" or "b" && counts[q.Color[0]] > 0 ? q.Color[0]
            : available.Count == 0 ? null : available.OrderByDescending(c => counts[c]).ThenBy(c => c == 'w' ? 0 : 1).First();
        var refs = sources.Select(x => x.Rep).ToList();
        if (pick is not { } color) return new Computed(infos, q.Repertoire, null, available, null, [], refs);

        var mine = sources.SelectMany(x => x.Sections.Where(s => s.Color == color).Select(s => new Source(x.Rep, s.Section))).ToList();
        var graph = RepertoireReach.Build(mine.Select(x => x.Section.Parsed), color);
        var chapters = mine.Select(x => Chapter(x.Section.Parsed)).ToList();
        var t0 = clock.ElapsedMilliseconds;
        var gameList = await games();
        var gamesMs = clock.ElapsedMilliseconds - t0;
        t0 = clock.ElapsedMilliseconds;
        var analysis = OpponentTrainingLines.Analyze(graph, gameList);
        var countMs = clock.ElapsedMilliseconds - t0;
        if (explorer is null) return new Computed(infos, q.Repertoire, color, available, OpponentTrainingLines.Rank(graph, chapters, analysis, null), mine, refs);

        // Lücken schätzen: nur die Gegner-Stellungen, für die seine Partien nicht reichen, je einmal (Wunsch 2026-10-07).
        // Ohne lokalen Explorer keine Schätzung: seine Züge zählen weiter, Lücken bleiben ohne Quelle (Auffüllregel), nichts offen.
        var need = explorer.Available ? OpponentTrainingLines.NeedsExplorer(graph, analysis, MinOwn) : [];
        var opponentElo = (elo is null || !explorer.Available ? null : await elo()) ?? TrainingExplorer.DefaultElo;
        // Die wichtigsten Stellungen zuerst; gespeicherte immer mit, NEU angefragt höchstens MaxPositions (Hotfix 0.725.2)
        var ordered = Prioritize(graph, chapters, analysis, need, MinOwn);
        // Frist: was von der Gesamtfrist übrig ist (mit Reserve fürs Reihen), höchstens das Explorer-Budget
        var budget = Deadline - clock.Elapsed - DeadlineReserve;
        if (budget > ExplorerBudget) budget = ExplorerBudget;
        if (budget < TimeSpan.Zero) budget = TimeSpan.Zero;   // dann nur, was schon im Speicher liegt
        // Läuft für diese Rechnung schon die Fortsetzung im Hintergrund, nichts doppelt anfragen — nur den Speicher lesen.
        var background = cacheKey is not null && continuation?.IsRunning(cacheKey) == true;
        t0 = clock.ElapsedMilliseconds;
        TrainingExplorerResult? found = ordered.Count == 0 ? null
            : await explorer.StatsAsync(userId, ordered, opponentElo, background ? TimeSpan.Zero : budget, background ? 0 : MaxPositions, ct);
        var explorerMs = clock.ElapsedMilliseconds - t0;
        var pending = new HashSet<string>(found?.Pending ?? new HashSet<string>(), StringComparer.Ordinal);
        var estimate = new OpponentTrainingLines.Estimate(n => found?.Stats.GetValueOrDefault(n.Key), MinOwn, pending);
        t0 = clock.ElapsedMilliseconds;
        var ranked = OpponentTrainingLines.Rank(graph, chapters, analysis, estimate);
        var rankMs = clock.ElapsedMilliseconds - t0;
        // Messen statt raten (2026-10-08): wo geht die Zeit hin?
        logger?.LogInformation(
            "Trainingslinien: {Total} ms gesamt — Repertoires laden+parsen {Parse} ms ({Repertoires} Rep., {Sections} Abschnitte), "
            + "Partien laden {Games} ms ({GameCount}), Zählen {Count} ms, Explorer {Explorer} ms ({Positions} Stellungen, {Hits} Treffer, "
            + "aus Speicher {FromMemory}, neu angefragt {Asked}, {Pending} offen, davon {Capped} über dem Deckel, Budget {Budget} ms "
            + "erreicht: {BudgetHit}), Reihen {Rank} ms, {Lines} Linien, Anfrage abgebrochen: {Aborted}",
            clock.ElapsedMilliseconds, parseMs, list.Count, byRep.Sum(x => x.Sections.Count), gamesMs, gameList.Count, countMs,
            explorerMs, need.Count, found?.Stats.Count ?? 0, found?.FromMemory ?? 0, found?.Asked ?? 0, pending.Count, found?.Capped ?? 0,
            (long)budget.TotalMilliseconds, pending.Count > (found?.Capped ?? 0), rankMs, ranked.Lines.Count, ct.IsCancellationRequested);
        // Offen geblieben: im Hintergrund fertig rechnen lassen (höchstens einmal je Nutzer gleichzeitig)
        var running = background;
        if (pending.Count > 0 && cacheKey is not null && continuation is not null && !background)
            running = StartContinuation(userId, cacheKey, ordered, opponentElo, graph, chapters, analysis, infos, q.Repertoire, color, available,
                mine, refs);
        return new Computed(infos, q.Repertoire, color, available, ranked, mine, refs,
            explorer.Available ? TrainingExplorer.Band(opponentElo) : null, pending.Count > 0, pending.Count, running);
    }

    /// <summary>Startet die Fortsetzung: die restlichen Stellungen in Priorisierungs-Reihenfolge (8 gleichzeitig, eigenes Zeitlimit),
    /// danach die Reihung aus dem Speicher — vollständig, dann in den 15-min-Speicher unter <paramref name="cacheKey"/>.</summary>
    private bool StartContinuation(int userId, string cacheKey, List<RepertoireReach.Node> ordered, int elo, RepertoireReach.Graph graph,
        List<string> chapters, OpponentTrainingLines.Analysis analysis, List<RepertoireInfo> infos, int? selected, char color,
        List<char> available, List<Source> mine, List<RepertoireRef> refs)
    {
        var minOwn = MinOwn;
        var band = TrainingExplorer.Band(elo);
        return continuation!.TryStart(userId, cacheKey, async (sp, limit, token) =>
        {
            var ex = sp.GetRequiredService<ITrainingExplorer>();
            await ex.StatsAsync(userId, ordered, elo, limit, int.MaxValue, token, TrainingLinesContinuation.Parallelism);
            // jetzt alles aus dem Speicher: Reihung wie im Vordergrund
            var final = await ex.StatsAsync(userId, ordered, elo, TimeSpan.Zero, 0, CancellationToken.None);
            var estimate = new OpponentTrainingLines.Estimate(n => final.Stats.GetValueOrDefault(n.Key), minOwn, final.Pending);
            var ranked = OpponentTrainingLines.Rank(graph, chapters, analysis, estimate);
            var computed = new Computed(infos, selected, color, available, ranked, mine, refs, band, final.Pending.Count > 0, final.Pending.Count);
            if (!computed.ExplorerIncomplete) sp.GetService<IMemoryCache>()?.Set(cacheKey, computed, CacheFor);
            logger?.LogInformation("Trainingslinien: Fortsetzung {Key} — {Hits} Treffer, {Pending} offen", cacheKey, final.Stats.Count,
                final.Pending.Count);
        });
    }

    /// <summary>
    /// Die Explorer-Stellungen in der Reihenfolge, in der ihre Linien VORLÄUFIG stehen (Reihung nur aus seinen Partien: zuerst
    /// Linien mit Quelle, dann nach Präfix aus seinen Daten, dann die abweichenden) — je Linie ihre Lücken von vorn nach hinten.
    /// So kommen bei knapper Frist die Linien zuerst dran, die oben stehen werden. Nutzt nur <see cref="OpponentTrainingLines.Rank(RepertoireReach.Graph, IReadOnlyList{string}, OpponentTrainingLines.Analysis, OpponentTrainingLines.Estimate?)"/>.
    /// </summary>
    internal static List<RepertoireReach.Node> Prioritize(RepertoireReach.Graph graph, IReadOnlyList<string> chapters,
        OpponentTrainingLines.Analysis analysis, IReadOnlyList<RepertoireReach.Node> need, int minOwn)
    {
        if (need.Count == 0) return [];
        var wanted = need.ToDictionary(n => n.Key, StringComparer.Ordinal);
        var preliminary = OpponentTrainingLines.Rank(graph, chapters, analysis,
            new OpponentTrainingLines.Estimate(_ => null, minOwn, new HashSet<string>()));
        var order = new List<RepertoireReach.Node>(need.Count);
        foreach (var line in preliminary.Lines)
            foreach (var node in graph.Mainlines[line.Index])
                if (wanted.Remove(node.Key, out var hit)) order.Add(hit);
        order.AddRange(wanted.Values);                 // (Stellungen, die keine gezeigte Linie hat — der Vollständigkeit halber)
        return order;
    }

    /// <summary>
    /// Die Antwort <c>{ repertoires[{ id, name, colors }], repertoire, color, colors, games, total, lines[…], more }</c>; <c>null</c> = das
    /// verlangte Repertoire gehört dem Nutzer nicht oder ist nicht markiert (→ 404). <c>repertoire</c> = <c>null</c>: alle markierten.
    /// Ohne Farbe die mit den meisten Linien. Je Linie dazu <c>repertoireId</c>/<c>repertoireName</c>.
    /// <paramref name="games"/> wird nur gerufen, wenn es überhaupt Linien gibt.
    /// </summary>
    /// <param name="elo">Elo des Gegners für das Wertungsband der Schätzung (ohne: <see cref="TrainingExplorer.DefaultElo"/>).</param>
    public async Task<JsonObject?> LinesAsync(int userId, Query q, Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct,
        Func<Task<int?>>? elo = null, string? scope = null)
    {
        if (await ComputeAsync(userId, q, games, ct, elo: elo, scope: scope) is not { } c) return null;
        var o = new JsonObject
        {
            ["repertoires"] = new JsonArray(c.Repertoires.Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Rep.Id, ["name"] = r.Rep.Name,
                ["colors"] = new JsonArray(r.Colors.Select(x => (JsonNode)x.ToString()).ToArray()),
            }).ToArray()),
            ["repertoire"] = c.Selected,
            ["color"] = c.Color?.ToString(),
            ["colors"] = new JsonArray(c.Colors.Select(x => (JsonNode)x.ToString()).ToArray()),
        };
        var lines = c.Ranked?.Lines ?? [];
        var n = Math.Clamp(q.Take ?? DefaultTakeFromConfig, 1, MaxTake);
        o["games"] = c.Ranked?.Games ?? 0;
        o["ownGames"] = c.Ranked?.Games ?? 0;
        o["lichessBand"] = c.Band;
        o["explorerIncomplete"] = c.ExplorerIncomplete;
        o["explorerPending"] = c.ExplorerPending;
        o["explorerRunning"] = c.ExplorerRunning;
        o["total"] = lines.Count;
        o["lines"] = new JsonArray(lines.Take(n).Select(l => (JsonNode)new JsonObject
        {
            ["key"] = l.Key,
            ["end"] = l.End,
            ["start"] = l.StartFen,
            ["chapter"] = l.Chapter,
            ["repertoireId"] = c.Mine[l.Index].Rep.Id,
            ["repertoireName"] = c.Mine[l.Index].Rep.Name,
            ["moves"] = new JsonArray(l.Sans.Select(s => (JsonNode)s).ToArray()),
            ["probability"] = Math.Round(l.Probability, 6),
            ["reached"] = l.Reached,
            ["lastYear"] = l.LastYear,
            ["neverReached"] = l.NeverReached,
            ["matched"] = l.Matched,
            ["missing"] = l.Missing,
            ["prefixProbability"] = Math.Round(l.PrefixProbability, 6),
            ["prefixReached"] = l.PrefixReached,
            ["source"] = l.Source,
            ["ownMoves"] = l.OwnMoves,
            ["lichessMoves"] = l.LichessMoves,
            ["lichessFrom"] = l.LichessFrom,
            ["pending"] = l.Pending,
            ["deviationPly"] = l.DeviationPly,
            ["deviationSan"] = l.DeviationSan,
            ["deviationGames"] = l.DeviationGames,
        }).ToArray());
        o["more"] = Math.Max(0, lines.Count - n);
        return o;
    }

    // ── Trainings-Repertoire anlegen ─────────────────────────────────────────────────────────────

    /// <summary>So viele Linien kommen höchstens in ein Trainings-Repertoire — fest, auch wenn die Liste mehr zeigen darf.</summary>
    public const int MaxRepertoireLines = 50;

    public sealed record Created(int Id, string Name, int Lines, bool Replaced);

    /// <summary>Das Quell-Repertoire heißt selbst wie das Ziel („Prep: … Jahr") und ist die EINZIGE Quelle — Ersetzen würde es
    /// überschreiben → 400 <c>{ reason: "sameRepertoire" }</c>. (Unter mehreren Quellen wird es nur ausgenommen.)</summary>
    public sealed class SameRepertoireException(string name)
        : DomainValidationException($"Das gewählte Repertoire heißt selbst „{name}“ — es würde sich selbst überschreiben. Bitte ein anderes wählen oder es umbenennen.");

    /// <summary>Es gibt schon ein eigenes Repertoire mit dem Zielnamen und es wurde nicht <c>replace</c> verlangt → 409
    /// <c>{ reason: "exists", id, name }</c>; nichts ist geschrieben (Wunsch 2026-10-08: Rückfrage nur, wenn wirklich eins da ist).</summary>
    public sealed class RepertoireExistsException(int id, string name) : ConflictException($"Es gibt schon ein Repertoire „{name}“.")
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
    }

    /// <summary>Name des Trainings-Repertoires: „Prep: Huber, Franz 2026".</summary>
    public static string RepertoireName(string opponent, int year)
    {
        var name = $"Prep: {(string.IsNullOrWhiteSpace(opponent) ? "?" : opponent.Trim())} {year.ToString(CultureInfo.InvariantCulture)}";
        return name.Length <= 200 ? name : name[..200];
    }

    /// <summary>
    /// „Show me lines to train" (Wunsch 2026-10-07): legt dem Nutzer ein Repertoire „Prep: &lt;Gegner&gt; &lt;Jahr&gt;" an — die
    /// gereihten Linien aller markierten Repertoires der Farbe (bzw. des gewählten), höchstens <see cref="MaxRepertoireLines"/>
    /// quer über alle, in dieser Reihenfolge, je Linie der UNVERÄNDERTE PGN-Abschnitt aus ihrem Repertoire. Ein markiertes
    /// Repertoire mit dem Zielnamen (ein früher erzeugtes „Prep: …", angehakt) wird als Quelle ausgenommen; ist es die einzige,
    /// <see cref="SameRepertoireException"/>. Gibt es schon ein eigenes mit dem Namen, wird dessen Inhalt ersetzt (Id, Häkchen und
    /// Trainingsstand je Linien-Schlüssel bleiben). Angelegt und befüllt über <see cref="RepertoireService"/>.
    /// <c>null</c> = Repertoire fremd/nicht markiert; <see cref="DomainValidationException"/>, wenn es keine Linien gibt.
    /// </summary>
    /// <param name="replace">Ein gleichnamiges eigenes Repertoire ersetzen; sonst <see cref="RepertoireExistsException"/>.</param>
    public async Task<Created?> CreateRepertoireAsync(int userId, string opponent, Query q,
        Func<Task<List<OpponentTrainingLines.Game>>> games, CancellationToken ct, Func<Task<int?>>? elo = null, bool replace = false,
        string? scope = null)
    {
        var now = DateTime.UtcNow;
        var name = RepertoireName(opponent, now.Year);
        if (await ComputeAsync(userId, q, games, ct, exclude: name, elo: elo, scope: scope) is not { } c) return null;
        if (c.Sources.Count == 0) throw new DomainValidationException("Kein Repertoire ist für die Vorbereitung freigegeben.");
        if (c.Ranked is not { Lines.Count: > 0 } ranked) throw new DomainValidationException("Die markierten Repertoires haben für diese Farbe keine Linien.");

        var n = Math.Min(DefaultTakeFromConfig, MaxRepertoireLines);
        var picked = ranked.Lines.Take(n).ToList();
        var pgn = string.Join("\n\n", picked.Select(l => c.Mine[l.Index].Section.Raw.Trim())) + "\n";

        var colorText = c.Color == 'b' ? "Schwarz" : "Weiß";
        var sourceNames = string.Join(", ", c.Sources.Select(r => $"„{r.Name}“"));
        var description = $"Trainingslinien gegen {opponent} ({colorText}) aus {sourceNames}, "
                          + $"{now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}, {ranked.Games} Partien gezählt, "
                          + $"{picked.Count} von {ranked.Lines.Count} Linien.";
        if (description.Length > 1000) description = description[..997] + "…";
        var sourceIds = c.Sources.Select(r => r.Id).ToList();
        var kinds = await db.Repertoires.AsNoTracking().Where(r => sourceIds.Contains(r.Id)).Select(r => r.Kind).Distinct().ToListAsync(ct);
        var kind = kinds.Count == 1 ? kinds[0] : RookHub.Api.Models.RepertoireKind.None;

        var existing = await db.Repertoires.AsNoTracking().Where(r => r.UserId == userId && r.Name == name)
            .OrderBy(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        if (existing is { } there && !replace) throw new RepertoireExistsException(there, name);
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

    // ── Elo des Gegners (Wertungsband der Schätzung) ─────────────────────────────────────────────

    /// <summary>Spieler des Bestands: die jüngste Elo aus seinen Partien, sonst seine höchste.</summary>
    public async Task<int?> PrepEloAsync(int playerId, short? maxElo, CancellationToken ct) =>
        await PrepAccountSearch.LatestEloAsync(db, playerId, ct) ?? maxElo;

    /// <summary>Ligaspieler: die Elo aus der jüngsten Meldeliste (international, sonst national).</summary>
    public async Task<int?> LeagueEloAsync(string fide, CancellationToken ct) =>
        await db.LeaguePlayers.AsNoTracking().Where(p => p.FideId == fide && (p.EloI != null || p.EloN != null))
            .OrderByDescending(p => p.Tnr).Select(p => p.EloI ?? p.EloN).FirstOrDefaultAsync(ct);

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

    /// <summary>Eigene Festlegungen aus der Adresse bzw. dem Rumpf — flach (<c>{ "Kapitel": "w" }</c>) oder je Repertoire
    /// (<c>{ "7": { "Kapitel": "b" } }</c>), auch gemischt; Unlesbares zählt als „keine".</summary>
    internal static ChapterOverrides ParseOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 200_000) return ChapterOverrides.None;
        JsonObject? root;
        try { root = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return ChapterOverrides.None; }
        if (root is null) return ChapterOverrides.None;
        var flat = new Dictionary<string, char>(StringComparer.Ordinal);
        var per = new Dictionary<int, IReadOnlyDictionary<string, char>>();
        foreach (var (k, v) in root)
        {
            if (v is JsonObject inner && int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                var m = new Dictionary<string, char>(StringComparer.Ordinal);
                foreach (var (ck, cv) in inner) Put(m, ck, cv);
                per[id] = m;
            }
            else Put(flat, k, v);
        }
        return new ChapterOverrides(flat, per);

        static void Put(Dictionary<string, char> into, string chapter, JsonNode? value)
        {
            if (into.Count >= 2000 || value is not JsonValue jv || !jv.TryGetValue<string>(out var c) || c is not ("w" or "b")) return;
            into[chapter.Trim()] = c[0];
        }
    }
}
