using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Games;

/// <summary>Ein schon gerechnetes Ergebnis derselben Stellung.</summary>
internal sealed record CachedPosition(string CandidatesJson, string? EvalText, int Depth);

/// <summary>
/// Eine Stellung nur einmal rechnen (0.921.0, Wunsch 2026-11-09): bevor die Pumpe einen Auftrag anlegt, schaut sie in den
/// fertigen Stellungen anderer Stockfish-Analysen nach. Gemessen waren 18 % aller Stellungen doppelt gerechnet, bei einer
/// Partie, die zugleich gespeichert und Vereinspartie ist, alle.
///
/// <list type="bullet">
/// <item><b>Gleich</b> = Figuren, Seite am Zug, Rochaderechte, En-passant-Feld; die beiden Zugzähler zählen nicht.</item>
/// <item><b>Gut genug</b> = mindestens so tief und mit mindestens so vielen Linien wie verlangt. Eine Stellung mit weniger
/// legalen Zügen als Linien findet deshalb nichts und wird wie bisher gerechnet.</item>
/// <item><b>Nie</b> Lc0 oder Knotenziel, weder als Quelle noch als Ziel: deren Zahlen sind eine andere Aussage.</item>
/// </list>
/// Die Spalte <c>Fen</c> vergleicht ohne Rücksicht auf Groß-/Kleinschreibung, „K" und „k" wären für sie gleich — darum prüft
/// der zweite Schritt den Schlüssel im Speicher genau.
/// </summary>
internal static class GameAnalysisPositionCache
{
    private const int MaxRows = 20;

    /// <summary>Die FEN ohne Zugzähler, mit abschließendem Leerzeichen; <c>null</c> = keine vollständige FEN.</summary>
    internal static string? KeyOf(string fen)
    {
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 4 ? null : string.Join(' ', parts.Take(4)) + " ";
    }

    internal static bool Applies(GameAnalysis analysis) => analysis.EngineId == null && analysis.TargetNodes == null;

    internal static Task<CachedPosition?> FindAsync(AppDbContext db, GameAnalysis analysis, string fen, int depth, int multiPv,
        CancellationToken ct) =>
        !Applies(analysis) ? Task.FromResult<CachedPosition?>(null) : FindAsync(db, analysis.Id, fen, depth, multiPv, ct);

    /// <summary>Wie oben, ohne Ziel-Analyse (<paramref name="excludeAnalysisId"/> = deren Id, 0 = keine) — für die
    /// Vorausanalyse (<see cref="League.ClubBroadcastPreAnalysis"/>), die vor dem Einreihen nachsieht.
    /// <para>Zwei Quellen: fertige Stellungen anderer Stockfish-Analysen, danach (1.034.0) fertige Vorausanalysen laufender
    /// Ligapartien (<see cref="AnalysisJob.Prep"/>) — dieselben Regeln (Tiefe, Linien, nie Knotenziel), das Ergebnis wird wie
    /// beim Einsammeln eines Auftrags gelesen (<see cref="BrokerCandidates.Parse"/>, fremde Engine-Ausgabe zählt nicht).</para></summary>
    internal static async Task<CachedPosition?> FindAsync(AppDbContext db, int excludeAnalysisId, string fen, int depth, int multiPv,
        CancellationToken ct)
    {
        if (KeyOf(fen) is not { } key) return null;
        var rows = await db.GameAnalysisPositions.AsNoTracking()
            .Where(p => EF.Functions.Like(p.Fen, key + "%") && p.Depth >= depth && p.CandidatesJson != null
                && p.GameAnalysisId != excludeAnalysisId && p.GameAnalysis!.EngineId == null && p.GameAnalysis.TargetNodes == null)
            .Select(p => new { p.Fen, p.CandidatesJson, p.EvalText, p.Depth, p.Refined, p.GameAnalysis!.MultiPv, p.GameAnalysis.RefineMultiPv })
            .Take(MaxRows)
            .ToListAsync(ct);
        // Nie eine Quelle, die selbst zu wenige Linien trägt (fremde Engine-Ausgabe, Provider-Versatz 16./17.11.2026 —
        // GameAnalysisRepair.Classify): sonst kopierte die Pumpe die falschen Zahlen in frisch geöffnete Stellungen zurück.
        int? legal = null;
        var hit = rows.FirstOrDefault(r => KeyOf(r.Fen) == key && LineCount(r.CandidatesJson!) >= multiPv
            && LineCount(r.CandidatesJson!) >= Math.Min(GameAnalysisRepair.ExpectedLines(r.MultiPv, r.RefineMultiPv, r.Refined),
                legal ??= GameAnalysisRepair.LegalMoveCount(fen) ?? 0));
        if (hit is not null) return new CachedPosition(hit.CandidatesJson!, hit.EvalText, hit.Depth);
        return await FindPrepAsync(db, key, fen, depth, multiPv, ct);
    }

    /// <summary>Fertige Vorausanalyse derselben Stellung (1.034.0). Die Linienzahl zählt gegen min(verlangt, legale Züge) —
    /// eine Stellung mit zwei legalen Zügen liefert auch bei fünf verlangten Linien nur zwei.</summary>
    private static async Task<CachedPosition?> FindPrepAsync(AppDbContext db, string key, string fen, int depth, int multiPv,
        CancellationToken ct)
    {
        var rows = await db.AnalysisJobs.AsNoTracking()
            .Where(j => j.Prep && j.Status == AnalysisJobStatus.Done && EF.Functions.Like(j.Fen, key + "%")
                && j.ReachedDepth >= depth && j.MultiPv >= multiPv && j.TargetNodes == null && j.ResultJson != null)
            .OrderByDescending(j => j.ReachedDepth)
            .Select(j => new { j.Fen, j.ResultJson, j.ReachedDepth })
            .Take(MaxRows)
            .ToListAsync(ct);
        int? legal = null;
        foreach (var r in rows.Where(r => KeyOf(r.Fen) == key))
        {
            if (BrokerCandidates.Parse(r.ResultJson, fen) is not { Count: > 0 } candidates) continue;
            if (candidates.Count < Math.Min(multiPv, legal ??= GameAnalysisRepair.LegalMoveCount(fen) ?? 0)) continue;
            return new CachedPosition(BrokerCandidates.ToJson(candidates), BrokerCandidates.EvalTextOf(candidates), r.ReachedDepth);
        }
        return null;
    }

    /// <summary>
    /// Ein noch OFFENER Vorausanalyse-Auftrag derselben Stellung, mindestens so tief und mit mindestens so vielen Linien
    /// (1.034.0) — die Analyse der eingespielten Partie übernimmt ihn, statt einen zweiten anzulegen. Nur für Stapel-Analysen
    /// (Verein, Liga, Bestand) desselben Kontos: eine eigene Partie eines Nutzers rechnet auf seiner Engine und würde sonst auf
    /// die Haus-Engines warten. Getrackt, der Aufrufer ändert Stufe und speichert.
    /// </summary>
    internal static async Task<AnalysisJob?> FindOpenPrepAsync(AppDbContext db, GameAnalysis analysis, string fen, int depth, int multiPv,
        CancellationToken ct)
    {
        if (!Applies(analysis) || !GameAnalysisOrigins.IsBatch(analysis.Origin) || KeyOf(fen) is not { } key) return null;
        var rows = await db.AnalysisJobs
            .Where(j => j.Prep && j.UserId == analysis.UserId && EF.Functions.Like(j.Fen, key + "%")
                && (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Paused || j.Status == AnalysisJobStatus.Running)
                && j.TargetDepth >= depth && j.MultiPv >= multiPv && j.TargetNodes == null
                && !db.GameAnalysisPositions.Any(p => p.AnalysisJobId == j.Id))
            .Take(MaxRows)
            .ToListAsync(ct);
        return rows.FirstOrDefault(j => KeyOf(j.Fen) == key);
    }

    private static int LineCount(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException) { return 0; }
    }

    internal static void Apply(GameAnalysisPosition pos, CachedPosition hit)
    {
        pos.CandidatesJson = hit.CandidatesJson;
        pos.EvalText = hit.EvalText;
        pos.Depth = hit.Depth;
        pos.AnalyzedAt = DateTime.UtcNow;
    }
}
