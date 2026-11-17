using System.Text.Json;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Games;

/// <summary>Warum eine Stellung neu gerechnet werden soll (<see cref="GameAnalysisRepair.Classify"/>).</summary>
public enum RepairFinding
{
    /// <summary>In Ordnung, oder nicht beurteilbar (FEN nicht ladbar, JSON unlesbar, Matt/Patt).</summary>
    None,
    /// <summary><c>CandidatesJson = "[]"</c>, obwohl es legale Züge gibt.</summary>
    Empty,
    /// <summary>Weniger Kandidaten als min(verlangte Linien, legale Züge) — das alte <c>Parse</c> hat fremde Linien nur
    /// herausgefiltert, der Rest war die Bewertung einer anderen Stellung.</summary>
    MissingLines,
}

/// <summary>Ein Aufruf der Reparatur (<see cref="GameAnalysisRepair.RunAsync"/>).</summary>
/// <param name="Apply"><c>false</c> (Vorgabe) = Trockenlauf, ändert nichts.</param>
/// <param name="AfterPositionId">Cursor: nur Stellungen mit größerer Id (0 = von vorn).</param>
/// <param name="MaxAnalyses">Höchstens so viele Analysen mit Funden je Aufruf (die Pumpe nimmt sie danach in ihrem Takt).</param>
/// <param name="MaxScan">Höchstens so viele Stellungen werden je Aufruf gelesen.</param>
/// <param name="DropMistakeComparisons">Die Zugvergleiche zu den Fehlern (Vereinspartien) der reparierten Analysen löschen —
/// sie entstehen nach dem Neurechnen neu (Tiefe 30, ggf. Fable-Begründung = Kosten). Vorgabe aus.</param>
public sealed record GameAnalysisRepairRequest(bool Apply = false, int AfterPositionId = 0, int MaxAnalyses = 50,
    int MaxScan = 5000, bool DropMistakeComparisons = false);

/// <summary>Ein Fund (Beispiel in der Antwort).</summary>
public sealed record GameAnalysisRepairSample(int PositionId, int GameAnalysisId, int Ply, string Finding, int Lines, int Expected);

/// <summary>Antwort der Reparatur.</summary>
/// <param name="NextCursor"><c>AfterPositionId</c> für den nächsten Aufruf; <c>null</c> = am Ende der Tabelle.</param>
public sealed record GameAnalysisRepairResult(bool Applied, int Scanned, int FoundEmpty, int FoundMissingLines,
    int SkippedTerminal, int PositionsReopened, int AnalysesReopened, int ExplanationsDeleted, int MistakeComparisonsDeleted,
    int? NextCursor, IReadOnlyList<GameAnalysisRepairSample> Samples);

/// <summary>
/// Reparatur der Stellungen, die der Provider-Versatz (Nacht 16./17.11.2026 und früher, seit 04.10.) verdorben hat
/// (0.990.0): Stellungen mit <c>[]</c> und Stellungen mit zu wenigen Kandidaten werden wieder geöffnet wie bei
/// <see cref="GameAnalysisPump.RestartAsync"/> — aber besitzerübergreifend, nur die Funde, seitenweise.
///
/// <para><b>Fund-Regeln</b> (<see cref="Classify"/>): <c>[]</c> bei mindestens einem legalen Zug; oder weniger Kandidaten als
/// min(verlangte Linien, legale Züge). Verlangt sind <see cref="GameAnalysis.MultiPv"/>, bei einer vertieften Stellung
/// (<see cref="GameAnalysisPosition.Refined"/>) das Kleinere aus <see cref="GameAnalysis.MultiPv"/> und
/// <see cref="GameAnalysis.RefineMultiPv"/> (welcher Durchgang stehen blieb, weiß die Stellung nicht). Ausgeschlossen:
/// Matt/Patt (kein legaler Zug), nicht ladbare FEN, unlesbares JSON, offene Aufträge, gescheiterte Analysen und für die
/// Zähl-Regel Lc0/Knotenaufträge (<see cref="GameAnalysis.EngineId"/>/<see cref="GameAnalysis.TargetNodes"/>: deren Linienzahl
/// ist eine andere Aussage). Positions-Cache-Kopien tragen nie weniger Linien als verlangt
/// (<see cref="GameAnalysisPositionCache"/>), Übernahmen beim Korrigieren kommen aus derselben Analyse.</para>
///
/// <para><b>Wiederöffnen</b>: Kandidaten, Tiefe, Bewertung, Knotenstufen leer, Fehlversuche 0, nicht vertieft; die Analyse
/// zurück auf Pending (war sie schon unterwegs, bleibt ihr Status), Genauigkeit und Zugklassen leer (beides rechnet die Pumpe
/// beim Abschluss neu), Vertiefung offen. Die Fehler-Erklärungen der betroffenen Züge (Halbzug der Stellung und davor)
/// werden gelöscht — sie entstehen beim Abschluss bzw. über „Fehler erklären lassen“ neu. Angestoßen wird nichts: die Pumpe
/// nimmt Pending-Analysen in ihrem Takt (Deckel je Partie und Konto), die Warteschlange flutet nicht.</para>
/// </summary>
public sealed class GameAnalysisRepair(AppDbContext db, AnalysisJobService jobs, ILogger<GameAnalysisRepair> logger)
{
    public const int MaxSamples = 10;
    public const int MaxAnalysesLimit = 500;
    public const int MaxScanLimit = 20_000;

    /// <summary>Legale Züge der Stellung; <c>null</c> = FEN nicht ladbar.</summary>
    internal static int? LegalMoveCount(string fen)
    {
        try { return ChessBoard.LoadFromFen(fen).Moves(generateSan: false).Length; }
        catch { return null; }
    }

    /// <summary>Zahl der Kandidaten; <c>null</c> = kein JSON-Array.</summary>
    internal static int? LineCount(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Verlangte Linien der Stellung (vor dem Deckel durch die legalen Züge).</summary>
    internal static int ExpectedLines(int multiPv, int? refineMultiPv, bool refined)
        => Math.Clamp(refined && refineMultiPv is int r ? Math.Min(multiPv, r) : multiPv, 1, EngineBroker.EngineProtocol.MaxMultiPv);

    /// <summary>Die Fund-Regel (siehe Klasse). <paramref name="countRule"/> = false für Lc0/Knotenaufträge.</summary>
    internal static (RepairFinding Finding, int Lines, int Expected) Classify(string? candidatesJson, string fen, int multiPv,
        int? refineMultiPv, bool refined, bool countRule)
    {
        if (string.IsNullOrEmpty(candidatesJson)) return (RepairFinding.None, 0, 0);
        if (LineCount(candidatesJson) is not { } lines) return (RepairFinding.None, 0, 0);
        if (LegalMoveCount(fen) is not { } legal || legal == 0) return (RepairFinding.None, lines, 0);
        var expected = Math.Min(ExpectedLines(multiPv, refineMultiPv, refined), legal);
        if (lines == 0) return (RepairFinding.Empty, 0, expected);
        if (countRule && lines < expected) return (RepairFinding.MissingLines, lines, expected);
        return (RepairFinding.None, lines, expected);
    }

    private sealed record Row(int Id, int GameAnalysisId, int Ply, string Fen, string? CandidatesJson, bool Refined,
        int MultiPv, int? RefineMultiPv, bool CountRule);

    /// <summary>Einen Abschnitt prüfen und (bei <see cref="GameAnalysisRepairRequest.Apply"/>) die Funde wieder öffnen.</summary>
    public async Task<GameAnalysisRepairResult> RunAsync(GameAnalysisRepairRequest req, int adminUserId, CancellationToken ct = default)
    {
        var maxAnalyses = Math.Clamp(req.MaxAnalyses, 1, MaxAnalysesLimit);
        var maxScan = Math.Clamp(req.MaxScan, 1, MaxScanLimit);
        // Seitenweise am Primärschlüssel entlang: kein Aufruf liest die ganze Tabelle. Nur gerechnete Stellungen ohne offenen
        // Auftrag in nicht gescheiterten Analysen.
        var rows = await db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.Id > req.AfterPositionId && p.CandidatesJson != null && p.AnalysisJobId == null
                && p.GameAnalysis!.Status != GameAnalysisStatus.Failed)
            .OrderBy(p => p.Id)
            .Take(maxScan)
            .Select(p => new Row(p.Id, p.GameAnalysisId, p.Ply, p.Fen, p.CandidatesJson, p.Refined,
                p.GameAnalysis!.MultiPv, p.GameAnalysis.RefineMultiPv,
                p.GameAnalysis.EngineId == null && p.GameAnalysis.TargetNodes == null))
            .ToListAsync(ct);

        int scanned = 0, empty = 0, missing = 0, terminal = 0;
        int? cursor = rows.Count < maxScan ? null : rows[^1].Id;
        var found = new Dictionary<int, List<Row>>();
        var samples = new List<GameAnalysisRepairSample>();
        foreach (var r in rows)
        {
            var (finding, lines, expected) = Classify(r.CandidatesJson, r.Fen, r.MultiPv, r.RefineMultiPv, r.Refined, r.CountRule);
            if (finding == RepairFinding.None && lines == 0 && r.CandidatesJson == "[]") terminal++;
            if (finding != RepairFinding.None && !found.ContainsKey(r.GameAnalysisId) && found.Count >= maxAnalyses)
            {
                cursor = r.Id - 1;   // diese Analyse kommt im nächsten Aufruf dran
                break;
            }
            scanned++;
            if (finding == RepairFinding.None) continue;
            if (finding == RepairFinding.Empty) empty++; else missing++;
            if (!found.TryGetValue(r.GameAnalysisId, out var list)) found[r.GameAnalysisId] = list = [];
            list.Add(r);
            if (samples.Count < MaxSamples)
                samples.Add(new GameAnalysisRepairSample(r.Id, r.GameAnalysisId, r.Ply, finding.ToString(), lines, expected));
        }

        int reopened = 0, explanations = 0, comparisons = 0;
        if (req.Apply && found.Count > 0)
            (reopened, explanations, comparisons) = await ReopenAsync(found, req.DropMistakeComparisons, ct);

        // Audit: wer, was, wie viel — strukturiert in ES/Kibana.
        logger.LogWarning(
            "Admin {AdminUserId}: Partieanalysen-Reparatur {Mode} ab Stellung {After} — {Scanned} geprüft, {Empty} leer, "
            + "{Missing} mit zu wenigen Linien, {Positions} Stellungen in {Analyses} Analysen geöffnet, {Explanations} Erklärungen "
            + "und {Comparisons} Zugvergleiche gelöscht, weiter ab {Next}",
            adminUserId, req.Apply ? "ANGEWANDT" : "Trockenlauf", req.AfterPositionId, scanned, empty, missing, reopened,
            req.Apply ? found.Count : 0, explanations, comparisons, cursor);

        return new GameAnalysisRepairResult(req.Apply, scanned, empty, missing, terminal, reopened, req.Apply ? found.Count : 0,
            explanations, comparisons, cursor, samples);
    }

    private async Task<(int Positions, int Explanations, int Comparisons)> ReopenAsync(Dictionary<int, List<Row>> found,
        bool dropComparisons, CancellationToken ct)
    {
        var ids = found.Keys.ToList();
        var positionIds = found.Values.SelectMany(l => l.Select(r => r.Id)).ToHashSet();
        var analyses = await db.GameAnalyses.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var positions = await db.GameAnalysisPositions.Where(p => positionIds.Contains(p.Id)).ToListAsync(ct);
        var plies = new Dictionary<int, HashSet<int>>();
        var reopened = 0;
        foreach (var pos in positions)
        {
            // Zwischen Lesen und Schreiben angefasst (die Pumpe hat einen Auftrag angelegt)? Dann nicht.
            if (pos.AnalysisJobId != null || pos.CandidatesJson == null) continue;
            pos.CandidatesJson = null;
            pos.AnalyzedAt = null;
            pos.EvalText = null;
            pos.Depth = 0;
            pos.NodeStepsJson = null;
            pos.FailedAttempts = 0;
            pos.Refined = false;
            reopened++;
            if (!plies.TryGetValue(pos.GameAnalysisId, out var set)) plies[pos.GameAnalysisId] = set = [];
            set.Add(pos.Ply);
            set.Add(pos.Ply - 1);   // die Erklärung eines Zuges liest auch die Stellung danach
        }

        var comparisons = 0;
        var now = DateTime.UtcNow;
        foreach (var a in analyses.Where(a => plies.ContainsKey(a.Id)))
        {
            if (a.Status == GameAnalysisStatus.Done) a.Status = GameAnalysisStatus.Pending;
            a.FinishedAt = null;
            a.LastError = null;
            a.AccuracyWhite = null;
            a.AccuracyBlack = null;
            a.MoveClasses = null;
            a.MoveClassesVersion = null;
            if (a.RefineDepth != null) a.RefinedAt = null;
            a.UpdatedAt = now;
            if (dropComparisons) comparisons += await ClubMistakeComparisons.DeleteForAnalysisAsync(db, jobs, a, ct);
        }

        var explanationIds = new List<int>();
        foreach (var (analysisId, set) in plies)
        {
            var list = set.ToList();
            explanationIds.AddRange(await db.GameMoveExplanations.Where(x => x.GameAnalysisId == analysisId && list.Contains(x.Ply))
                .Select(x => x.Id).ToListAsync(ct));
        }
        if (explanationIds.Count > 0)
            db.GameMoveExplanations.RemoveRange(await db.GameMoveExplanations.Where(x => explanationIds.Contains(x.Id)).ToListAsync(ct));

        await db.SaveChangesAsync(ct);
        return (reopened, explanationIds.Count, comparisons);
    }
}
