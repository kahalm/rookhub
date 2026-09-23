using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Lochfinder und Linien-Häufigkeiten eines Repertoires aus dem Lichess-Explorer
/// (<c>POST /api/repertoires/{id}/explorer-analysis</c>). Die Rechnung steht in
/// <see cref="RepertoireReach"/>; hier liegen Zugriff, Speicher, Token und Zeitbudget.
///
/// <para><b>Zeitbudget statt Hintergrundauftrag:</b> ein Aufruf fragt höchstens
/// <see cref="Budget"/> lang neue Stellungen ab und antwortet dann mit dem, was er hat
/// (<c>Complete = false</c>). Alles Abgefragte liegt in <see cref="LichessExplorerCacheEntry"/>, der
/// nächste Aufruf rechnet es in Millisekunden nach und macht an der Grenze weiter. So kommt der
/// Client ohne Auftragsverwaltung zu einem Fortschrittsbalken, und ein abgebrochener Lauf ist nicht
/// verloren.</para>
///
/// <para><b>Token:</b> der Explorer verlangt seit 2025 eine Anmeldung. Zuerst gilt der Server-Token
/// (<c>LichessExplorer:Token</c>), sonst der Lichess-Token, den der Nutzer für die externe Engine
/// hinterlegt hat — der Explorer nimmt jeden gültigen Token, ohne besonderen Scope.</para>
///
/// <para><b>Zwei Quellen:</b> <c>online</c> (explorer.lichess.ovh — Token, Drossel, Datenbank-Speicher)
/// und <c>local</c> (<see cref="LocalExplorerClient"/> — nichts davon; die Stellungen einer
/// Tiefenschicht gehen gleichzeitig raus, <see cref="LocalParallelism"/>, und liegen nur kurz im
/// Arbeitsspeicher, damit die nächste Runde sie nicht erneut holt).</para>
/// </summary>
public class RepertoireExplorerService
{
    /// <summary>Speicherdauer — Zughäufigkeiten ändern sich über Monate kaum.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(90);

    private const int MaxHoles = 300;
    /// <summary>So viele Verbindungsfehler in einem Aufruf, dann wird dort aufgehört.</summary>
    private const int MaxFailures = 3;
    private const int CacheChunk = 500;
    /// <summary>So viele gleichzeitige Anfragen an den lokalen Explorer.</summary>
    public const int LocalParallelism = 8;
    private static readonly TimeSpan LocalMemoryTtl = TimeSpan.FromHours(1);

    /// <summary>So lange darf eine Schicht der lokalen Quelle mindestens laufen, auch wenn das Budget
    /// fast aufgebraucht ist — sonst käme die letzte Schicht einer Runde nie zu einer Antwort.</summary>
    public TimeSpan LocalLayerFloor { get; set; } = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _db;
    private readonly RepertoireService _repertoires;
    private readonly LichessExplorerClient _client;
    private readonly LocalExplorerClient _local;
    private readonly IMemoryCache _memory;
    private readonly LichessExplorerGate _gate;
    private readonly EncryptionService _encryption;
    private readonly IConfiguration _config;
    private readonly ILogger<RepertoireExplorerService> _logger;
    private readonly TimeProvider _time;

    /// <summary>Wie lange ein Aufruf neue Stellungen abfragt. Die erste Abfrage einer Runde geht immer
    /// raus (auch wenn sie auf die Leitung warten muss) — jede Runde bringt also Fortschritt, sonst
    /// hielte der Client den Lauf für festgefahren. Reverse-Proxy und nginx schneiden erst nach 60 s ab.</summary>
    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(20);

    public RepertoireExplorerService(
        AppDbContext db, RepertoireService repertoires, LichessExplorerClient client, LocalExplorerClient local,
        IMemoryCache memory, LichessExplorerGate gate, EncryptionService encryption, IConfiguration config,
        ILogger<RepertoireExplorerService> logger, TimeProvider? time = null)
    {
        _db = db;
        _repertoires = repertoires;
        _client = client;
        _local = local;
        _memory = memory;
        _gate = gate;
        _encryption = encryption;
        _config = config;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <exception cref="KeyNotFoundException">Repertoire fehlt oder ist für den Nutzer nicht lesbar.</exception>
    /// <exception cref="ArgumentException">Ungültige Auswahl (Datenbank, Elo, Bedenkzeit, Schwelle, Farbe).</exception>
    public async Task<ExplorerAnalysisResultDto> AnalyzeAsync(
        int userId, int repertoireId, ExplorerAnalysisRequestDto req, CancellationToken ct)
    {
        var query = ExplorerQuery.Create(req.Database, req.Ratings, req.Speeds);
        var useLocal = UseLocal(req.Source);
        if (double.IsNaN(req.ThresholdPercent) || req.ThresholdPercent < 0.1 || req.ThresholdPercent > 50)
            throw new ArgumentException("Die Schwelle muss zwischen 0,1 und 50 % liegen.");
        char? onlyColor = req.Color switch
        {
            null or "" => null,
            "w" => 'w',
            "b" => 'b',
            _ => throw new ArgumentException("Farbe muss \"w\" oder \"b\" sein."),
        };

        var pgn = await _repertoires.GetCombinedPgnAsync(repertoireId, userId);
        var colors = req.ChapterColors ?? new Dictionary<string, string>();
        var graphs = PgnMoveTree.ParseSections(pgn)
            .Where(s => s.Moves.Count > 0)
            .GroupBy(s => colors.TryGetValue((s.Black ?? "").Trim(), out var c) && c == "b" ? 'b' : 'w')
            .Where(grp => onlyColor is null || grp.Key == onlyColor)
            .OrderBy(grp => grp.Key == 'w' ? 0 : 1)
            .Select(grp => RepertoireReach.Build(grp, grp.Key))
            .ToList();

        var dto = new ExplorerAnalysisResultDto();
        var clock = Stopwatch.StartNew();
        var threshold = req.ThresholdPercent / 100.0;
        if (useLocal)
        {
            await EvaluateLocalAsync(graphs, query, threshold, req, dto, clock, ct);
            return dto;
        }

        var cached = await LoadCacheAsync(query,
            graphs.SelectMany(g => g.OpponentBranches).Select(n => n.Key).Distinct(), ct);
        string? token = null;
        var tokenResolved = false;
        var stop = false;
        var failures = 0;

        async Task<ExplorerPositionStats?> Stats(RepertoireReach.Node node)
        {
            if (cached.TryGetValue(node.Key, out var hit)) return hit;
            if (stop || req.CachedOnly || clock.Elapsed >= Budget) return null;
            if (_gate.BlockedFor is not null) { dto.RateLimited = true; stop = true; return null; }
            if (!tokenResolved)
            {
                token = await ResolveTokenAsync(userId, ct);
                tokenResolved = true;
            }
            if (token is null) { dto.TokenMissing = true; stop = true; return null; }

            var (status, stats) = await _client.FetchAsync(node.Fen, query, token, ct);
            switch (status)
            {
                case ExplorerFetchStatus.Ok:
                    cached[node.Key] = stats!;
                    await StoreAsync(query.CachePrefix + node.Key, stats!, ct);
                    return stats;
                case ExplorerFetchStatus.RateLimited:
                    dto.RateLimited = true; stop = true; return null;
                case ExplorerFetchStatus.Unauthorized:
                    dto.TokenInvalid = true; stop = true; return null;
                default:
                    if (++failures >= MaxFailures) { dto.FetchFailed = true; stop = true; }
                    return null;
            }
        }

        await CollectAsync(graphs, Stats, null, threshold, req, dto, ct);
        if (dto.RateLimited && _gate.BlockedFor is { } left) dto.RetryAfterSeconds = (int)Math.Ceiling(left.TotalSeconds);
        return dto;
    }

    /// <summary><c>online</c> (Vorgabe) oder <c>local</c> — letzteres nur, wenn eingerichtet.</summary>
    private bool UseLocal(string? source)
    {
        var local = source switch
        {
            null or "" or "online" => false,
            "local" => true,
            _ => throw new ArgumentException("Quelle muss \"online\" oder \"local\" sein."),
        };
        if (local && !_local.IsConfigured)
            throw new ArgumentException("Der lokale Explorer ist auf diesem Server nicht eingerichtet.");
        return local;
    }

    private static readonly System.Text.RegularExpressions.Regex FenPattern = new(
        @"^[1-8pnbrqkPNBRQK]{1,8}(/[1-8pnbrqkPNBRQK]{1,8}){7} [wb] (-|[KQkq]{1,4}) (-|[a-h][36])( \d{1,3} \d{1,4})?$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Zugstatistik EINER Stellung für den Eröffnungs-Explorer des Analysebretts
    /// (<c>GET /api/explorer/position</c>). Dieselbe Datenstrecke wie der Lochfinder: online mit
    /// Token, Leitung und Datenbank-Speicher, lokal mit Arbeitsspeicher. Ein Speicher-Eintrag ohne
    /// Ergebnis-Aufteilung (vor 0.504.0 geschrieben) wird dabei neu geholt und überschrieben.
    /// </summary>
    /// <exception cref="ArgumentException">Keine FEN, unbekannte Quelle oder Auswahl.</exception>
    public async Task<ExplorerPositionResultDto> PositionAsync(
        int userId, string? fen, string? source, ExplorerQuery query, CancellationToken ct)
    {
        fen = (fen ?? "").Trim();
        if (fen.Length > 100 || !FenPattern.IsMatch(fen)) throw new ArgumentException("Keine gültige FEN.");
        var useLocal = UseLocal(source);
        var key = RepertoireReach.Key(fen);
        var dto = new ExplorerPositionResultDto { Source = useLocal ? "local" : "online", Database = query.Database };

        ExplorerPositionStats? stats;
        if (useLocal)
        {
            var memoryKey = "explorer:local:" + query.CachePrefix + key;
            if (!_memory.TryGetValue<ExplorerPositionStats>(memoryKey, out stats) || stats is null || !stats.HasResults)
            {
                stats = await _local.FetchAsync(fen, query, ct);
                if (stats is not null) _memory.Set(memoryKey, stats, LocalMemoryTtl);
            }
            if (stats is null) { dto.Status = "failed"; return dto; }
        }
        else
        {
            var cached = await LoadCacheAsync(query, new[] { key }, ct);
            if (!cached.TryGetValue(key, out stats) || !stats.HasResults)
            {
                if (_gate.BlockedFor is { } blocked) return RateLimited(dto, blocked);
                var token = await ResolveTokenAsync(userId, ct);
                if (token is null) { dto.Status = "tokenMissing"; return dto; }
                var (status, fetched) = await _client.FetchAsync(fen, query, token, ct);
                switch (status)
                {
                    case ExplorerFetchStatus.Ok:
                        stats = fetched!;
                        await StoreAsync(query.CachePrefix + key, stats, ct);
                        break;
                    case ExplorerFetchStatus.RateLimited:
                        return RateLimited(dto, _gate.BlockedFor ?? LichessExplorerGate.RateLimitPause);
                    case ExplorerFetchStatus.Unauthorized:
                        dto.Status = "tokenInvalid"; return dto;
                    default:
                        dto.Status = "failed"; return dto;
                }
            }
        }

        dto.Total = stats.Total;
        dto.White = stats.White ?? 0;
        dto.Draws = stats.Draws ?? 0;
        dto.Black = stats.Black ?? 0;
        dto.Opening = stats.Opening;
        dto.Eco = stats.Eco;
        dto.Moves = stats.Moves.Select(m => new ExplorerPositionMoveDto
        {
            Uci = m.Uci, San = m.San, Games = m.Games,
            White = m.White ?? 0, Draws = m.Draws ?? 0, Black = m.Black ?? 0,
            AverageRating = m.AverageRating, Opening = m.Opening, Eco = m.Eco,
        }).ToList();
        return dto;
    }

    /// <summary>So lange bleiben die Partien einer Stellung im Arbeitsspeicher — beide Quellen.</summary>
    private static readonly TimeSpan GamesMemoryTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// Eine Handvoll Partien, die diese Stellung erreicht haben (<c>GET /api/explorer/games</c>) —
    /// der Client fragt die Stellung NACH einem Zug, das sind die Partien mit diesem Zug
    /// (Zugumstellungen eingeschlossen). Nur im Arbeitsspeicher (eine Stunde), nicht in der Datenbank:
    /// man sieht sie sich gezielt an, und die Liste der jüngsten Partien veraltet ohnehin.
    /// </summary>
    public async Task<ExplorerGamesResultDto> GamesAsync(
        int userId, string? fen, string? source, ExplorerQuery query, CancellationToken ct)
    {
        fen = (fen ?? "").Trim();
        if (fen.Length > 100 || !FenPattern.IsMatch(fen)) throw new ArgumentException("Keine gültige FEN.");
        var useLocal = UseLocal(source);
        var dto = new ExplorerGamesResultDto();
        var memoryKey = $"explorer:games:{(useLocal ? "local" : "online")}:{query.CachePrefix}{RepertoireReach.Key(fen)}";

        if (!_memory.TryGetValue<List<ExplorerGame>>(memoryKey, out var games) || games is null)
        {
            if (useLocal)
            {
                games = await _local.FetchGamesAsync(fen, query, ct);
                if (games is null) { dto.Status = "failed"; return dto; }
            }
            else
            {
                if (_gate.BlockedFor is { } blocked) return RateLimited(dto, blocked);
                var token = await ResolveTokenAsync(userId, ct);
                if (token is null) { dto.Status = "tokenMissing"; return dto; }
                var (status, fetched) = await _client.FetchGamesAsync(fen, query, token, ct);
                switch (status)
                {
                    case ExplorerFetchStatus.Ok: games = fetched!; break;
                    case ExplorerFetchStatus.RateLimited:
                        return RateLimited(dto, _gate.BlockedFor ?? LichessExplorerGate.RateLimitPause);
                    case ExplorerFetchStatus.Unauthorized: dto.Status = "tokenInvalid"; return dto;
                    default: dto.Status = "failed"; return dto;
                }
            }
            _memory.Set(memoryKey, games, GamesMemoryTtl);
        }

        // Lokale Meisterpartien stammen aus der Lumbra-Datenbank — ihre Kennung gibt es auf lichess.org nicht.
        var linkable = !(useLocal && query.Database == ExplorerQuery.Masters);
        dto.Games = games.Select(g => new ExplorerGameDto
        {
            Id = g.Id, White = g.White, WhiteRating = g.WhiteRating, Black = g.Black, BlackRating = g.BlackRating,
            Winner = g.Winner, Date = g.Month ?? g.Year?.ToString(), Speed = g.Speed,
            Url = linkable ? "https://lichess.org/" + Uri.EscapeDataString(g.Id) : null,
        }).ToList();
        return dto;
    }

    private static ExplorerGamesResultDto RateLimited(ExplorerGamesResultDto dto, TimeSpan left)
    {
        dto.Status = "rateLimited";
        dto.RetryAfterSeconds = (int)Math.Ceiling(left.TotalSeconds);
        return dto;
    }

    private static ExplorerPositionResultDto RateLimited(ExplorerPositionResultDto dto, TimeSpan left)
    {
        dto.Status = "rateLimited";
        dto.RetryAfterSeconds = (int)Math.Ceiling(left.TotalSeconds);
        return dto;
    }

    /// <summary>Welche Quellen es gibt — die Oberfläche zeigt „lokal" nur, wenn eingerichtet.</summary>
    public ExplorerSourcesDto Sources() => new()
    {
        Online = true,
        Local = _local.IsConfigured,
        LocalRatings = LocalExplorerClient.LocalRatings.ToList(),
        LocalSpeeds = LocalExplorerClient.LocalSpeeds.ToList(),
    };

    /// <summary>
    /// Lokale Quelle: je Tiefenschicht alle benötigten Stellungen gleichzeitig holen.
    /// <para><b>Ein Ausreißer ist kein Ausfall.</b> Während eines Imports kompaktiert der lokale
    /// Explorer auf der Platte (gemessen: Median 88 ms, p99 5,9 s, Spitze 11 s). Eine gescheiterte oder
    /// zu langsame Stellung bleibt deshalb nur OFFEN — die nächste Runde fragt sie erneut — und
    /// <c>FetchFailed</c> heißt erst: in diesem Aufruf kam gar keine Antwort. Eine Schicht darf die
    /// Runde außerdem höchstens bis zum Budget ziehen (mindestens <see cref="LocalLayerFloor"/>), damit
    /// die Antwort vor dem 60-s-Schnitt des Reverse-Proxys ankommt.</para>
    /// </summary>
    private async Task EvaluateLocalAsync(
        List<RepertoireReach.Graph> graphs, ExplorerQuery query, double threshold, ExplorerAnalysisRequestDto req,
        ExplorerAnalysisResultDto dto, Stopwatch clock, CancellationToken ct)
    {
        var fetched = new ConcurrentDictionary<string, ExplorerPositionStats>(StringComparer.Ordinal);
        // Je Aufruf höchstens EIN Versuch je Stellung — ein Ausreißer wartet auf die nächste Runde,
        // statt als Nachzügler gleich noch einmal angefragt zu werden.
        var attempted = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        var failures = 0;
        var answered = 0;
        string MemoryKey(string nodeKey) => "explorer:local:" + query.CachePrefix + nodeKey;

        async Task Prefetch(IReadOnlyList<RepertoireReach.Node> nodes)
        {
            var missing = nodes.Where(n => !fetched.ContainsKey(n.Key) && !attempted.ContainsKey(n.Key)).ToList();
            foreach (var n in missing.ToList())
                if (_memory.TryGetValue<ExplorerPositionStats>(MemoryKey(n.Key), out var hit) && hit is not null)
                {
                    fetched[n.Key] = hit;
                    missing.Remove(n);
                }
            // Antwortet der Explorer gar nicht, nicht jede weitere Schicht dagegen laufen lassen.
            if (missing.Count == 0 || req.CachedOnly || clock.Elapsed >= Budget || (failures >= MaxFailures && answered == 0)) return;

            var left = Budget - clock.Elapsed;
            using var layer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            layer.CancelAfter(left > LocalLayerFloor ? left : LocalLayerFloor);
            try
            {
                await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = LocalParallelism, CancellationToken = layer.Token },
                    async (n, token) =>
                    {
                        attempted[n.Key] = true;
                        var stats = await _local.FetchAsync(n.Fen, query, token);
                        if (stats is null) { Interlocked.Increment(ref failures); return; }
                        Interlocked.Increment(ref answered);
                        fetched[n.Key] = stats;
                        _memory.Set(MemoryKey(n.Key), stats, LocalMemoryTtl);
                    });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Zeit der Schicht um: was fehlt, bleibt offen und kommt in der nächsten Runde dran.
            }
        }

        async Task<ExplorerPositionStats?> Stats(RepertoireReach.Node node)
        {
            if (fetched.TryGetValue(node.Key, out var hit)) return hit;
            // Nachzügler einer Schicht (Zugumstellung innerhalb derselben Tiefe): einzeln holen.
            await Prefetch(new[] { node });
            return fetched.TryGetValue(node.Key, out hit) ? hit : null;
        }

        await CollectAsync(graphs, Stats, Prefetch, threshold, req, dto, ct);
        if (failures > 0 && answered == 0) dto.FetchFailed = true;
    }

    /// <summary>Rechnet alle Farb-Graphen durch und trägt Löcher, Zähler und Häufigkeiten ein.</summary>
    private static async Task CollectAsync(
        List<RepertoireReach.Graph> graphs, Func<RepertoireReach.Node, Task<ExplorerPositionStats?>> stats,
        Func<IReadOnlyList<RepertoireReach.Node>, Task>? prefetch, double threshold,
        ExplorerAnalysisRequestDto req, ExplorerAnalysisResultDto dto, CancellationToken ct)
    {
        var holes = new List<RepertoireHoleDto>();
        var frequencies = new Dictionary<string, double>(StringComparer.Ordinal);
        var positions = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var graph in graphs)
        {
            var r = await RepertoireReach.EvaluateAsync(graph, stats, threshold, ct, prefetch);
            dto.PositionsAnalyzed += r.Analyzed;
            dto.PositionsPending += r.Pending;
            if (req.IncludeHoles)
                holes.AddRange(r.Holes.Select(h => ToDto(h, graph.Color)));
            // Eine Stellung kann in Kapiteln BEIDER Farben stehen — es gilt der größere Wert.
            MergeMax(frequencies, r.LineFrequencies);
            if (req.IncludePositionFrequencies) MergeMax(positions, r.PositionFrequencies);
        }

        dto.Complete = dto.PositionsPending == 0;
        dto.Holes = holes.OrderByDescending(h => h.Frequency).ThenByDescending(h => h.Share).Take(MaxHoles).ToList();
        if (req.IncludeLineFrequencies) dto.LineFrequencies = frequencies;
        if (req.IncludePositionFrequencies) dto.PositionFrequencies = positions;
    }

    private static void MergeMax(Dictionary<string, double> into, Dictionary<string, double> from)
    {
        foreach (var (key, p) in from)
            if (!into.TryGetValue(key, out var have) || p > have) into[key] = p;
    }

    private static RepertoireHoleDto ToDto(RepertoireReach.Hole h, char color)
    {
        var (startFen, path) = RepertoireReach.PathTo(h.Position);
        return new RepertoireHoleDto
        {
            Color = color.ToString(),
            Fen = h.Position.Fen,
            StartFen = startFen,
            Path = path,
            San = h.Move.San,
            Uci = h.Move.Uci,
            Share = h.Share,
            Games = h.Move.Games,
            PositionGames = h.PositionGames,
            Frequency = h.Frequency,
            Opening = h.Move.Opening,
            Eco = h.Move.Eco,
        };
    }

    /// <summary>Server-Token, sonst der für die externe Engine hinterlegte Lichess-Token des Nutzers.</summary>
    private async Task<string?> ResolveTokenAsync(int userId, CancellationToken ct)
    {
        var server = _config["LichessExplorer:Token"];
        if (!string.IsNullOrWhiteSpace(server)) return server.Trim();
        var cred = await _db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        var own = cred is null ? null : _encryption.TryDecrypt(cred.EncryptedToken);
        return string.IsNullOrWhiteSpace(own) ? null : own;
    }

    /// <summary>Frische Speicher-Einträge der Stellungen, in Portionen (ein IN mit tausenden Werten
    /// wäre für MariaDB zu lang). Schlüssel im Ergebnis = Stellungs-Schlüssel ohne Präfix.</summary>
    private async Task<Dictionary<string, ExplorerPositionStats>> LoadCacheAsync(
        ExplorerQuery query, IEnumerable<string> positionKeys, CancellationToken ct)
    {
        var result = new Dictionary<string, ExplorerPositionStats>(StringComparer.Ordinal);
        var freshSince = _time.GetUtcNow().UtcDateTime - CacheTtl;
        var prefix = query.CachePrefix;
        foreach (var chunk in positionKeys.Select(k => prefix + k).Chunk(CacheChunk))
        {
            var rows = await _db.LichessExplorerCacheEntries.AsNoTracking()
                .Where(e => chunk.Contains(e.CacheKey) && e.FetchedAt >= freshSince)
                .Select(e => new { e.CacheKey, e.Json })
                .ToListAsync(ct);
            foreach (var row in rows)
                if (ExplorerPositionStats.FromJson(row.Json) is { } stats)
                    result[row.CacheKey[prefix.Length..]] = stats;
        }
        return result;
    }

    private async Task StoreAsync(string cacheKey, ExplorerPositionStats stats, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var row = await _db.LichessExplorerCacheEntries.FirstOrDefaultAsync(e => e.CacheKey == cacheKey, ct);
        if (row is null)
            _db.LichessExplorerCacheEntries.Add(new LichessExplorerCacheEntry { CacheKey = cacheKey, Json = stats.ToJson(), FetchedAt = now });
        else
        {
            row.Json = stats.ToJson();
            row.FetchedAt = now;
        }
        // Zwei Läufe, die dieselbe Stellung gleichzeitig holen: der zweite Eintrag ist überflüssig, kein Fehler.
        await _db.SaveIgnoringUniqueRaceAsync(_logger, ct);
    }
}
