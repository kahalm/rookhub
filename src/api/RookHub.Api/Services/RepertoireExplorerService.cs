using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
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
/// </summary>
public class RepertoireExplorerService
{
    /// <summary>Speicherdauer — Zughäufigkeiten ändern sich über Monate kaum.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(90);

    private const int MaxHoles = 300;
    /// <summary>So viele Verbindungsfehler in einem Aufruf, dann wird dort aufgehört.</summary>
    private const int MaxFailures = 3;
    private const int CacheChunk = 500;

    private readonly AppDbContext _db;
    private readonly RepertoireService _repertoires;
    private readonly LichessExplorerClient _client;
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
        AppDbContext db, RepertoireService repertoires, LichessExplorerClient client, LichessExplorerGate gate,
        EncryptionService encryption, IConfiguration config, ILogger<RepertoireExplorerService> logger,
        TimeProvider? time = null)
    {
        _db = db;
        _repertoires = repertoires;
        _client = client;
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

        var cached = await LoadCacheAsync(query,
            graphs.SelectMany(g => g.OpponentBranches).Select(n => n.Key).Distinct(), ct);

        var dto = new ExplorerAnalysisResultDto();
        var clock = Stopwatch.StartNew();
        string? token = null;
        var tokenResolved = false;
        var stop = false;
        var failures = 0;

        async Task<ExplorerPositionStats?> Stats(RepertoireReach.Node node)
        {
            if (cached.TryGetValue(node.Key, out var hit)) return hit;
            if (stop || clock.Elapsed >= Budget) return null;
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

        var holes = new List<RepertoireHoleDto>();
        var frequencies = new Dictionary<string, double>(StringComparer.Ordinal);
        var threshold = req.ThresholdPercent / 100.0;
        foreach (var graph in graphs)
        {
            var r = await RepertoireReach.EvaluateAsync(graph, Stats, threshold, ct);
            dto.PositionsAnalyzed += r.Analyzed;
            dto.PositionsPending += r.Pending;
            if (req.IncludeHoles)
                holes.AddRange(r.Holes.Select(h => ToDto(h, graph.Color)));
            foreach (var (key, p) in r.LineFrequencies)
                if (!frequencies.TryGetValue(key, out var have) || p > have) frequencies[key] = p;
        }

        dto.Complete = dto.PositionsPending == 0;
        if (dto.RateLimited && _gate.BlockedFor is { } left) dto.RetryAfterSeconds = (int)Math.Ceiling(left.TotalSeconds);
        dto.Holes = holes.OrderByDescending(h => h.Frequency).ThenByDescending(h => h.Share).Take(MaxHoles).ToList();
        if (req.IncludeLineFrequencies) dto.LineFrequencies = frequencies;
        return dto;
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
