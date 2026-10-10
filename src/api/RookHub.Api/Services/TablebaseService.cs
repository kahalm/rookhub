using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Endspiel-Datenbank (Syzygy, bis 7 Steine) über die öffentliche Schnittstelle von Lichess (0.729.0, Wunsch 2026-10-10:
/// „bei der Live-Analyse ab 7 Steinen Lichess um die Wahrheit fragen — parallel Stockfish, falls keine Antwort kommt").
///
/// <para>Die Engine schätzt im Endspiel (ein Remis als +4, ein Gewinn als +1,5); die Tablebase kennt das Ergebnis genau.
/// Der Browser fragt HIER und nicht direkt bei Lichess: die CSP des Frontends erlaubt nur die eigene Herkunft, und so gibt es
/// EINEN Zwischenspeicher und EINE Leitung für alle Nutzer. Lichess drosselt; deshalb höchstens eine Anfrage je
/// <see cref="MinInterval"/>, nach einem 429 <see cref="RateLimitPause"/> Ruhe, und jede Antwort bleibt
/// <see cref="CacheFor"/> im Speicher (Endspiel-Stellungen wiederholen sich beim Durchklicken ständig).</para>
///
/// <para>Die Ergebnisse der ZÜGE liefert Lichess aus Sicht des GEGNERS (Stellung nach dem Zug) — hier umgedreht, damit
/// <c>moves[].category</c> wie <c>category</c> aus Sicht der Seite am Zug gilt.</para>
/// </summary>
public sealed class TablebaseService
{
    public const int MaxPieces = 7;
    public const string ClientName = "lichess-tablebase";
    public const string DefaultBaseUrl = "https://tablebase.lichess.ovh/";
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    private readonly TablebaseGate _gate;
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TablebaseService> _logger;
    private readonly Func<DateTime> _now;

    public TablebaseService(TablebaseGate gate, IHttpClientFactory http, IMemoryCache cache, ILogger<TablebaseService> logger,
        Func<DateTime>? now = null)
    {
        _gate = gate;
        _http = http;
        _cache = cache;
        _logger = logger;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Steine auf dem Brett (beide Könige mitgezählt) — aus dem Brett-Teil der FEN; -1 = unlesbar.</summary>
    public static int PieceCount(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return -1;
        var board = fen.Trim().Split(' ')[0];
        if (board.Count(c => c == '/') != 7) return -1;
        return board.Count(char.IsLetter);
    }

    public async Task<TablebaseResultDto> LookupAsync(string? fen, CancellationToken ct = default)
    {
        fen = fen?.Trim();
        var pieces = PieceCount(fen);
        if (pieces < 0 || !AnalysisJobService.IsLegalFen(fen!)) return new TablebaseResultDto { Status = "invalid" };
        if (pieces > MaxPieces) return new TablebaseResultDto { Status = "tooManyPieces" };

        // Zugzähler (Feld 6) ändert am Ergebnis nichts, der Halbzugzähler (50-Züge-Regel) schon.
        var key = "tb:" + string.Join(' ', fen!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5));
        if (_cache.TryGetValue<TablebaseResultDto>(key, out var hit) && hit is not null) return hit;

        if (_now() < _gate.PausedUntil) return new TablebaseResultDto { Status = "rateLimited" };
        await _gate.Lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue<TablebaseResultDto>(key, out hit) && hit is not null) return hit;
            var wait = _gate.NextAllowed - _now();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _gate.NextAllowed = _now() + MinInterval;

            var client = _http.CreateClient(ClientName);
            using var res = await client.GetAsync("standard?fen=" + Uri.EscapeDataString(fen), ct);
            if ((int)res.StatusCode == 429)
            {
                _gate.PausedUntil = _now() + RateLimitPause;
                _logger.LogWarning("Tablebase: Lichess antwortete 429 — {Pause} Pause", RateLimitPause);
                return new TablebaseResultDto { Status = "rateLimited" };
            }
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogInformation("Tablebase: Lichess antwortete {Code}", (int)res.StatusCode);
                return new TablebaseResultDto { Status = "unavailable" };
            }
            var dto = Parse(await res.Content.ReadAsStringAsync(ct));
            _cache.Set(key, dto, CacheFor);
            return dto;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogInformation("Tablebase: keine Antwort von Lichess ({Error})", ex.Message);
            return new TablebaseResultDto { Status = "unavailable" };
        }
        finally { _gate.Lock.Release(); }
    }

    /// <summary>Antwort von Lichess → DTO; Zug-Ergebnisse auf die Sicht der ziehenden Seite gedreht.</summary>
    internal static TablebaseResultDto Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var dto = new TablebaseResultDto
        {
            Status = "ok",
            Category = Str(r, "category") ?? "unknown",
            Dtz = Int(r, "dtz"),
            Dtm = Int(r, "dtm"),
            Checkmate = Bool(r, "checkmate"),
            Stalemate = Bool(r, "stalemate"),
            InsufficientMaterial = Bool(r, "insufficient_material"),
        };
        if (r.TryGetProperty("moves", out var moves) && moves.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in moves.EnumerateArray())
            {
                var uci = Str(m, "uci");
                if (uci is null) continue;
                dto.Moves.Add(new TablebaseMoveDto
                {
                    Uci = uci,
                    San = Str(m, "san") ?? uci,
                    Category = Flip(Str(m, "category") ?? "unknown"),
                    Dtz = Int(m, "dtz") is int z ? -z : null,
                    Dtm = Int(m, "dtm") is int d ? -d : null,
                    Zeroing = Bool(m, "zeroing"),
                    Checkmate = Bool(m, "checkmate"),
                    Stalemate = Bool(m, "stalemate"),
                });
            }
        }
        return dto;
    }

    /// <summary>Ergebnis aus Sicht der anderen Seite.</summary>
    internal static string Flip(string category) => category switch
    {
        "win" => "loss",
        "loss" => "win",
        "cursed-win" => "blessed-loss",
        "blessed-loss" => "cursed-win",
        "maybe-win" => "maybe-loss",
        "maybe-loss" => "maybe-win",
        _ => category,
    };

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>Die EINE Leitung zur Tablebase (Singleton): eine Anfrage zur Zeit, Abstand, Pause nach 429.</summary>
public sealed class TablebaseGate
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public DateTime NextAllowed { get; set; } = DateTime.MinValue;
    public DateTime PausedUntil { get; set; } = DateTime.MinValue;
}
