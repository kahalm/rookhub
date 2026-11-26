using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Services.League;

/// <summary>
/// Vorausanalyse laufender Ligapartien der EIGENEN Mannschaft (1.034.0, Wunsch 2026-11-26: „die Analyse mit Tiefe 30 + 5 Linien,
/// die ich brauche, wenn Hintergrund-Engines frei sind, schon während der Bundesliga machen — nur für Schwaz“). Anlass: am
/// 26.11. kamen sechs Bundesliga-Partien fast gleichzeitig in die Vereins-Datenbank, ihre rund 600 Stellungen mit Tiefe 30 und
/// fünf Linien brauchten auf den Hintergrund-Engines Stunden.
///
/// <list type="bullet">
/// <item><b>Welche Partien</b>: dieselbe Auswahl wie das Einspielen in die Vereins-Datenbank (<see cref="ClubBroadcastGames.Involves"/>:
/// <c>WhiteTeam</c>/<c>BlackTeam</c> gehört einer Mannschaft eines Vereins), aber noch ohne Ergebnis (<c>*</c>), Standardschach
/// ab der Grundstellung, nicht älter als gestern. Kein eigener Lichess-Abruf: der schnelle Takt der Übertragungen
/// (<see cref="LeagueBroadcastLiveScheduler"/>, alle 10 min) holt das PGN der laufenden Runde ohnehin, unabhängig davon, ob
/// jemand die Live-Bretter offen hat — <see cref="LeagueBroadcastImport"/> reicht die laufenden Partien hierher.</item>
/// <item><b>Was</b>: jede Stellung der Partie (vor jedem Halbzug und die aktuelle) als Hintergrundauftrag mit Tiefe und Linien der
/// späteren Vertiefung der Vereinspartie (<see cref="GameAnalysisDefaults.ClubRefineDepth"/>, <see cref="GameAnalysisDefaults.MultiPv"/>)
/// — das deckt den ersten Durchgang (Tiefe 20) gleich mit. Nur Stockfish (Hintergrund-Engines des Analyse-Kontos, wie
/// <see cref="MasterAnalysisScheduler"/>). Übersprungen: Stellungen mit Vorausanalyse und Stellungen, die der Stellungs-Cache
/// schon gut genug kennt.</item>
/// <item><b>Vorrang</b>: Stufe <see cref="JobPriority.LivePrep"/>, unter allen anderen; als Stapel-Arbeit verdrängt sie nichts, und
/// die Live-Bretter (<see cref="LiveEngineLane"/>) gehen ohnehin vor jede Stufe. Innerhalb der Stufe der älteste zuerst — alle
/// Stellungen werden später gebraucht.</item>
/// <item><b>Übernahme</b>: fertige Vorausanalysen sind Quelle des <see cref="GameAnalysisPositionCache"/>; noch offene übernimmt
/// die Analyse der eingespielten Partie (<see cref="GameAnalysisPositionCache.FindOpenPrepAsync"/>), statt doppelt zu rechnen.</item>
/// <item><b>Aufräumen</b>: Vorausanalysen, die älter als <c>LiveBoards:PreAnalysis:RetentionHours</c> (Vorgabe 168) sind, löscht
/// <see cref="CleanupAsync"/> (vom schnellen Takt aufgerufen).</item>
/// </list>
/// Schalter <c>LiveBoards:PreAnalysis:Enabled</c> (Vorgabe an); aus, wenn die Analyse der Vereinspartien selbst aus ist
/// (<c>MasterAnalysis:Enabled=false</c>) — dann nähme niemand das Ergebnis ab.
/// </summary>
public sealed class ClubBroadcastPreAnalysis
{
    /// <summary>Höchstens so viele neue Aufträge je Einspielen — eine Bremse gegen eine kaputte Übertragung, kein Taktgeber.</summary>
    public const int MaxNewPerRun = 1000;

    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly ILogger _logger;
    private readonly int? _configuredOwner;

    public ClubBroadcastPreAnalysis(AppDbContext db, AnalysisJobService jobs, ILogger<ClubBroadcastPreAnalysis> logger, IConfiguration? config = null)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
        Enabled = (config?.GetValue<bool?>("LiveBoards:PreAnalysis:Enabled") ?? true)
            && (config?.GetValue<bool?>("MasterAnalysis:Enabled") ?? true);
        Retention = TimeSpan.FromHours(Math.Clamp(config?.GetValue<int?>("LiveBoards:PreAnalysis:RetentionHours") ?? 168, 6, 24 * 60));
        _configuredOwner = config?.GetValue<int?>("MasterAnalysis:OwnerUserId");
    }

    public bool Enabled { get; }
    public TimeSpan Retention { get; }

    /// <summary>Tiefe der Vorausanalyse = Tiefe der Vertiefung einer Vereinspartie (eine Angabe, nicht zwei).</summary>
    public static int Depth => GameAnalysisDefaults.ClubRefineDepth;
    /// <summary>Linien der Vorausanalyse = Linien der Vereinspartie-Analyse und ihrer Vertiefung.</summary>
    public static int Lines => GameAnalysisDefaults.MultiPv;

    /// <summary>Die LAUFENDEN Partien eines Übertragungs-PGN (Ergebnis <c>*</c>, Standard ab der Grundstellung, mindestens ein
    /// Zug), mit den Kopfzeilen der Übertragung und der Hauptvariante als SAN — das Gegenstück zu
    /// <see cref="LeagueBroadcastImport.CleanGames"/> für die fertigen.</summary>
    public static List<LeagueBroadcastGame> OngoingGames(string pgn)
    {
        var list = new List<LeagueBroadcastGame>();
        foreach (var b in PgnParser.SplitGameBlocks(pgn))
        {
            var h = new Dictionary<string, string>(b.Headers, StringComparer.OrdinalIgnoreCase);
            if ((h.GetValueOrDefault("Result") ?? "*").Trim() != "*") continue;
            if (h.TryGetValue("Variant", out var v) && !v.Equals("Standard", StringComparison.OrdinalIgnoreCase)) continue;
            if (h.ContainsKey("FEN") || h.GetValueOrDefault("SetUp") == "1") continue;
            var moveText = PgnParser.SplitGames(b.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
            var sans = PgnParser.ExtractMainlineSans(moveText).ToList();
            if (sans.Count == 0) continue;
            h["Date"] = LeagueBroadcastImport.DateOf(h.GetValueOrDefault("Date")) ?? LeagueBroadcastImport.DateOf(h.GetValueOrDefault("UTCDate")) ?? "????.??.??";
            list.Add(new LeagueBroadcastGame(h, sans));
        }
        return list;
    }

    /// <summary>Die Stellungen einer Partie, wie die Partie-Analyse sie anlegt (<see cref="GamePlies.Parse"/>: vor jedem Halbzug,
    /// dazu die aktuelle, wenn dort noch gezogen werden kann). Nicht nachspielbar → leer.</summary>
    public static List<string> PositionsOf(IReadOnlyList<string> sans)
    {
        List<string> legal;
        try { legal = SavedGameService.LegalSans(sans); }
        catch (ArgumentException) { return new(); }
        if (legal.Count == 0 || legal.Count > GameAnalysisDefaults.MaxPlies) return new();
        var parsed = GamePlies.Parse("[Result \"*\"]\n\n" + PgnWriter.MoveText(legal, null, null, "*"), GameAnalysisDefaults.MaxPlies);
        if (parsed is not { } p || p.Plies.Count == 0) return new();
        var fens = p.Plies.Select(x => x.Fen).ToList();
        if (GameAnalysisText.FinalPositionOf(p.Plies[^1]) is { } last) fens.Add(last);
        return fens;
    }

    /// <summary>
    /// Laufende Partien einer Übertragung: die der eigenen Mannschaft eines Vereins vorausrechnen lassen. → neu angelegte Aufträge.
    /// Wirft nicht wegen einzelner Partien; ohne Analyse-Konto (keine Hintergrund-Engine) passiert nichts.
    /// </summary>
    public async Task<int> EnqueueAsync(IReadOnlyList<LeagueBroadcastGame> ongoing, CancellationToken ct, DateTime? nowUtc = null)
    {
        if (!Enabled || ongoing.Count == 0 || !ongoing.Any(g => g.H("WhiteTeam") != null || g.H("BlackTeam") != null)) return 0;
        var now = nowUtc ?? DateTime.UtcNow;
        var clubs = await _db.LeagueClubs.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        var yesterday = DateOnly.FromDateTime(now).AddDays(-1);
        // Nicht älter als gestern: ein nie abgeschlossenes „*“ einer früheren Runde stünde sonst bei jedem Takt wieder da.
        var mine = ongoing.Where(g => clubs.Any(c => ClubBroadcastGames.Involves(c, g))
                && (ClubPgnText.FullDateOf(g.H("Date")) is not { } d || d >= yesterday))
            .ToList();
        if (mine.Count == 0) return 0;
        if (await MasterAnalysisScheduler.OwnerAsync(_db, _configuredOwner, ct) is not { } owner) return 0;

        // Schon vorgemerkt (offen oder fertig, nicht gescheitert) — über den Schlüssel ohne Zugzähler, wie der Cache.
        var since = now - Retention;
        var taken = (await _db.AnalysisJobs.AsNoTracking()
                .Where(j => j.Prep && j.Status != AnalysisJobStatus.Failed && j.CreatedAt >= since)
                .Select(j => j.Fen).ToListAsync(ct))
            .Select(GameAnalysisPositionCache.KeyOf).Where(k => k != null).ToHashSet(StringComparer.Ordinal);
        var created = 0;
        foreach (var g in mine)
        {
            var fens = PositionsOf(g.Sans);
            var title = $"Vorausanalyse · {g.H("White") ?? "?"} – {g.H("Black") ?? "?"}";
            if (title.Length > 160) title = title[..160];
            for (var i = 0; i < fens.Count && created < MaxNewPerRun; i++)
            {
                ct.ThrowIfCancellationRequested();
                var fen = fens[i];
                if (GameAnalysisPositionCache.KeyOf(fen) is not { } key || !taken.Add(key)) continue;
                if (await GameAnalysisPositionCache.FindAsync(_db, 0, fen, Depth, Lines, ct) is not null) continue;
                try
                {
                    await _jobs.CreateAsync(owner.UserId, new CreateAnalysisJobRequest
                    {
                        Fen = fen, TargetDepth = Depth, MultiPv = Lines, Title = $"{title} · {i / 2 + 1}{(i % 2 == 0 ? "w" : "s")}",
                    }, ct, prep: true);
                    created++;
                }
                catch (ArgumentException ex)
                {
                    _logger.LogDebug(ex, "Vorausanalyse: Stellung {Fen} nicht einreihbar", fen);
                }
                catch (InvalidOperationException ex)
                {
                    // Keine Hintergrund-Engine — beim nächsten Takt wieder.
                    _logger.LogInformation("Vorausanalyse: nichts eingereiht ({Reason})", ex.Message);
                    return created;
                }
            }
        }
        if (created > 0)
            _logger.LogInformation("Vorausanalyse: {Count} Stellung(en) aus {Games} laufenden Partie(n) der eigenen Mannschaft eingereiht",
                created, mine.Count);
        return created;
    }

    /// <summary>Vorausanalysen älter als <see cref="Retention"/> löschen — fertige (ihre Partie ist längst übernommen) wie nie
    /// gerechnete. Ein von einer Analyse übernommener Auftrag hängt an deren Stellung und bleibt. → wie viele gelöscht.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct, DateTime? nowUtc = null)
    {
        var cut = (nowUtc ?? DateTime.UtcNow) - Retention;
        var old = await _db.AnalysisJobs
            .Where(j => j.Prep && j.CreatedAt < cut && !_db.GameAnalysisPositions.Any(p => p.AnalysisJobId == j.Id))
            .Take(2000).ToListAsync(ct);
        if (old.Count == 0) return 0;
        foreach (var j in old.Where(j => j.Status == AnalysisJobStatus.Running)) _jobs.Interrupt(j.Id);
        _db.AnalysisJobs.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Vorausanalyse: {Count} alte Aufträge aufgeräumt", old.Count);
        return old.Count;
    }
}
