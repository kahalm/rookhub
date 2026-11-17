using Chess;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Engines;

/// <summary>Wie das Lesen des Streams endete.</summary>
internal enum StreamEnd
{
    /// <summary>Stream zu Ende, abgebrochen (Ziel, Live-Vorrang, Löschen, Shutdown) oder abgerissen.</summary>
    Ended,
    /// <summary>Der Stillstands-Wächter hat ausgelöst: keine frische Datenzeile binnen der Frist.</summary>
    Silent,
    /// <summary>Eine Zeile gehörte zu einer ANDEREN Stellung (<see cref="BrokerCandidates.MismatchOf(string?, string)"/>):
    /// der Lauf ist abgebrochen, nichts davon darf bleiben (<see cref="JobStreamRun.DiscardAsync"/>).</summary>
    Foreign,
}

/// <summary>
/// EIN Streaming-Lauf eines Auftrags (Zerlegung Z11, vorher der Rumpf von <c>AnalysisJobWorker.RunJobAsync</c>): liest
/// den ndjson-Stream des Brokers, führt den laufenden Stand (Anzeige über <see cref="AnalysisJobLive"/>, Zwischenstände
/// einer Knotenanalyse, Ergebnis-Zeilen), sichert in Abständen und bewacht den Stillstand. Was danach mit dem Auftrag
/// geschieht, entscheidet <see cref="AnalysisJobRunEnd"/> aus <see cref="Facts"/>.
/// </summary>
internal sealed class JobStreamRun
{
    private readonly AppDbContext _db;
    private readonly AnalysisJob _job;
    private readonly string _engineId;
    private readonly AnalysisJobLive _live;
    private readonly ILogger _logger;
    private readonly TimeSpan _persistInterval;
    private readonly TimeSpan _firstLineTimeout;
    private readonly TimeSpan _stallTimeout;
    /// <summary>Knotenauftrag: Zwischenstände je Schwelle mitschreiben — dieselben Zeilen, keine Mehrrechnung.</summary>
    private readonly NodeStepRecorder? _recorder;
    private DateTime _lastPersist;
    private string? _pendingLine;
    private int _pendingDepth;
    private string? _lastLine;
    /// <summary>Legale Züge der Auftragsstellung — einmal erzeugt, jede Zeile wird dagegen geprüft (null = FEN nicht ladbar,
    /// dann prüft der Broker ohnehin nichts).</summary>
    private readonly Move[]? _legal;
    /// <summary>Gespeicherter Stand beim Start — ein fremder Lauf wird darauf zurückgesetzt.</summary>
    private readonly (string? Result, int Depth, string? Eval, string? Steps, int Seconds) _atStart;

    /// <summary>Beginn des Laufs (nach den Kopfzeilen).</summary>
    internal DateTime RunStart { get; }
    /// <summary>Knoten des VORIGEN Laufs (seine letzte Zeile steht noch im Ergebnis) — siehe <see cref="AnalysisJobStream.StoppedEarlyAgain"/>.</summary>
    internal long? PreviousNodes { get; }
    internal int DepthAtStart { get; }
    internal long CurrentNodes { get; private set; }
    internal bool NodeGoalReached { get; private set; }
    internal bool EngineFinished { get; private set; }
    internal int CurrentDepth { get; private set; }
    internal int CurrentNps { get; private set; }
    /// <summary>Mindestens eine FRISCHE Datenzeile kam an (Wiederholungen sind Lebenszeichen).</summary>
    internal bool GotData { get; private set; }
    internal StreamTally Tally { get; } = new();
    /// <summary>Warum eine Zeile nicht zur Stellung des Auftrags gehörte (Provider-Versatz); <c>null</c> = alles passte.</summary>
    internal string? ForeignReason { get; private set; }

    internal JobStreamRun(AppDbContext db, AnalysisJob job, string engineId, AnalysisJobLive live, ILogger logger,
        long snapshotStep, TimeSpan persistInterval, TimeSpan firstLineTimeout, TimeSpan stallTimeout)
    {
        _db = db;
        _job = job;
        _engineId = engineId;
        _live = live;
        _logger = logger;
        _persistInterval = persistInterval;
        _firstLineTimeout = firstLineTimeout;
        _stallTimeout = stallTimeout;

        RunStart = DateTime.UtcNow;
        _atStart = (job.ResultJson, job.ReachedDepth, job.EvalText, job.NodeStepsJson, job.SecondsSpent);
        try { _legal = ChessBoard.LoadFromFen(job.Fen).Moves(generateSan: false); }
        catch { _legal = null; }
        // Anzeige-Stand ohne Datenbank: die Zeit läuft ab HIER, auch wenn die Engine noch schweigt.
        _live.Start(job.Id, job.UserId, job.SecondsSpent, RunStart);
        _lastPersist = RunStart;
        // Knotenauftrag: die Suche beginnt bei Tiefe 1 von vorn, ein Rest-Stand aus einem früheren Lauf
        // (andere Zählung, kalte Hashtabelle) würde sonst alle flacheren Zeilen verwerfen.
        PreviousNodes = job.TargetNodes is not null && job.ResultJson is { } previous
            ? AnalysisJobStream.NodesOf(previous) : null;
        if (job.TargetNodes is not null) job.ReachedDepth = 0;
        DepthAtStart = job.ReachedDepth;
        _recorder = job.TargetNodes is not null && snapshotStep > 0
            ? new NodeStepRecorder(snapshotStep, NodeSteps.Parse(job.NodeStepsJson)) : null;
        _pendingDepth = job.ReachedDepth;
    }

    /// <summary>
    /// Den Stream lesen, bis er endet. Wächter gegen STILLSTAND: bis zur ersten Datenzeile gilt die kurze Frist (ein
    /// Provider, der gar nicht rechnet, soll den Slot nicht halten); danach die lange, und sie wird bei jeder FRISCHEN
    /// Zeile neu gestellt. Eine zeichengleich wiederholte Zeile stellt sie NICHT neu — das ist das Lebenszeichen des
    /// Providers, nicht die Engine. Früher wurde der Wächter nach der ersten Zeile für immer entschärft; eine Engine, die
    /// danach hängen blieb, hielt ihren Auftrag und (über die Ein-Partie-Pumpe) die ganze Warteschlange unbegrenzt fest.
    /// </summary>
    /// <param name="ct">Token des Laufs (Live-Vorrang, Löschen, Pause, Shutdown).</param>
    internal async Task<StreamEnd> ConsumeAsync(Stream? ndjson, CancellationToken ct)
    {
        using var silence = new CancellationTokenSource(_firstLineTimeout);
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct, silence.Token);
        try
        {
            await AnalysisJobStream.ConsumeAsync(ndjson!, (line, depth) => OnLineAsync(line, depth, silence, streamCts),
                streamCts.Token, Tally);
        }
        catch (OperationCanceledException) when (ForeignReason is not null)
        {
            return StreamEnd.Foreign;
        }
        catch (OperationCanceledException) when (silence.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return StreamEnd.Silent;
        }
        catch (OperationCanceledException) { /* Pause (Live), Löschung oder Shutdown */ }
        catch (IOException) when (ForeignReason is not null)
        {
            return StreamEnd.Foreign;
        }
        catch (IOException ex)
        {
            // Die Zahlen entscheiden die Ursachenfrage: kam bis zuletzt Verkehr, ist es KEIN
            // Untätigkeits-Timeout; war die Leitung stumm, schon. Deshalb beides getrennt —
            // Datenzeilen und Lebenszeichen (Leerzeilen) haben unterschiedliche Aussagekraft.
            var now = DateTime.UtcNow;
            _logger.LogWarning(ex,
                "AnalysisJob {JobId}: Stream abgerissen nach {RunSeconds:F0} s — {DataLines} Datenzeilen, "
                + "{Heartbeats} Lebenszeichen, {OtherLines} sonstige; letzte Datenzeile vor {DataGap} s, "
                + "letztes Byte vor {AnyGap} s (Engine {EngineId}, Tiefe {Depth}/{Target})",
                _job.Id, (now - RunStart).TotalSeconds, Tally.DataLines, Tally.Heartbeats, Tally.OtherLines,
                Fmt(Tally.DataGapSeconds(now)), Fmt(Tally.AnyGapSeconds(now)), _engineId,
                CurrentDepth, _job.TargetDepth);
        }
        return ForeignReason is not null ? StreamEnd.Foreign : StreamEnd.Ended;
    }

    /// <summary>Eine Zeile mit Tiefe: erst prüfen, ob sie überhaupt zu dieser Stellung gehört (sonst Abbruch, nichts
    /// sichern), dann Stand nachführen, ggf. sichern, beim Knotenziel selbst abbrechen.</summary>
    private async Task OnLineAsync(string line, int depth, CancellationTokenSource silence, CancellationTokenSource streamCts)
    {
        var job = _job;
        if (ForeignReason is not null) return;
        // Provider-Versatz (17.11.2026): die Zeile gehört zur VORIGEN Suche des Providers. VOR jedem Sichern geprüft —
        // eine fremde Zeile darf weder Tiefe noch Ergebnis noch Zwischenstand werden.
        if (_legal is not null && BrokerCandidates.MismatchOf(line, _legal) is { } foreign)
        {
            ForeignReason = foreign;
            streamCts.Cancel();
            return;
        }
        // Nur eine FRISCHE Zeile stellt den Wächter neu — die Wiederholung ist ein Lebenszeichen.
        if (!StreamTally.IsRepeat(line, _lastLine)) { GotData = true; silence.CancelAfter(_stallTimeout); }
        _lastLine = line;
        // Laufender Stand IMMER mitschreiben — auch wenn die Zeile flacher ist als das Ergebnis.
        // Nach einer Fortsetzung rechnet die Engine erst wieder von Tiefe 1 hoch; ohne das stünde
        // die Anzeige minutenlang still (keine Tiefe, kein Tempo, nicht einmal die Zeit lief mit).
        CurrentDepth = depth;
        CurrentNps = AnalysisJobStream.NpsOf(line) ?? CurrentNps;
        CurrentNodes = AnalysisJobStream.NodesOf(line) ?? CurrentNodes;
        if (AnalysisJobStream.HasBestMove(line)) EngineFinished = true;
        if (_recorder is not null) AnalysisJobWorker.ObserveStep(_recorder, line, job.Fen);
        _live.Update(job.Id, CurrentDepth, CurrentNps, nodes: CurrentNodes);
        var keep = AnalysisJobStream.ShouldPersist(depth, job.ReachedDepth);
        if (keep) { _pendingLine = line; _pendingDepth = depth; }
        var now = DateTime.UtcNow;
        if ((keep && depth > job.ReachedDepth) || now - _lastPersist >= _persistInterval)
        {
            if (_recorder is { Dirty: true }) job.NodeStepsJson = NodeSteps.ToJson(_recorder.Snapshot());
            var rest = await PersistProgressAsync(_db, job, _pendingLine, _pendingDepth, now - _lastPersist,
                                                  CurrentDepth, CurrentNps);
            _lastPersist = now - rest; _pendingLine = null;   // angebrochene Sekunde mitnehmen
            // Ziel/Linien können sich unterdessen geändert haben (Service in eigenem Scope).
            await _db.Entry(job).ReloadAsync(CancellationToken.None);
        }
        // Knotenauftrag: Selbst-Abbruch, sobald das Ziel da ist — die Zeile steht oben schon als
        // pendingLine und wird nach dem Stream gesichert. (Die Engine bekommt `nodes` mit und endet
        // meist selbst; das hier ist der Riegel für eine, die das Limit überzieht.)
        if (job.TargetNodes is { } nodeTarget && CurrentNodes >= nodeTarget)
        {
            NodeGoalReached = true;
            streamCts.Cancel();
        }
        // BEWUSST kein Selbst-Abbruch bei erreichter Zieltiefe (gilt für TIEFENaufträge): die Engine bekommt `depth` als
        // Limit mitgeschickt und beendet den Stream selbst. Bräche der Worker schon bei der
        // ERSTEN Zeile der Zieltiefe ab, trüge nur die Hauptvariante diese Tiefe — die Linien
        // 2..K blieben eine Iteration flacher (jede pv hat ihre eigene Tiefe).
    }

    /// <summary>Nach dem Stream: Ende protokollieren, Zwischenstände abschließen, den letzten Stand sichern und den
    /// Auftrag frisch laden (Ziel/Linien können sich unterdessen geändert haben).</summary>
    internal async Task FinishAsync()
    {
        var job = _job;
        var endedAt = DateTime.UtcNow;
        // Auch das saubere Ende protokollieren — ohne Vergleichsmaßstab sagen die Abriss-Zahlen nichts.
        _logger.LogInformation(
            "AnalysisJob {JobId}: Lauf beendet nach {RunSeconds:F0} s — {DataLines} Datenzeilen, "
            + "{Heartbeats} Lebenszeichen, {OtherLines} sonstige; letzte Datenzeile vor {DataGap} s "
            + "(Engine {EngineId}, Tiefe {Depth}/{Target})",
            job.Id, (endedAt - RunStart).TotalSeconds, Tally.DataLines, Tally.Heartbeats, Tally.OtherLines,
            Fmt(Tally.DataGapSeconds(endedAt)), _engineId, CurrentDepth, job.TargetDepth);

        if (_recorder is not null)
        {
            _recorder.Finish(job.TargetNodes);
            if (_recorder.Dirty) job.NodeStepsJson = NodeSteps.ToJson(_recorder.Snapshot());
        }
        await PersistProgressAsync(_db, job, _pendingLine, _pendingLine is null ? job.ReachedDepth : _pendingDepth,
                                   DateTime.UtcNow - _lastPersist, CurrentDepth, CurrentNps);

        await _db.Entry(job).ReloadAsync(CancellationToken.None);
    }

    /// <summary>
    /// Nach einem fremden Lauf (<see cref="StreamEnd.Foreign"/>) statt <see cref="FinishAsync"/>: den Auftrag auf den Stand
    /// vom Start zurücksetzen — Ergebnis, Tiefe, Bewertungstext, Knoten-Zwischenstände und Rechenzeit. Auch Zeilen, die
    /// vor der Erkennung noch unauffällig waren, stammen aus demselben fremden Lauf.
    /// </summary>
    internal async Task DiscardAsync()
    {
        var job = _job;
        _logger.LogInformation(
            "AnalysisJob {JobId}: Lauf verworfen nach {RunSeconds:F0} s — fremde Ausgabe ({Reason}), {DataLines} Datenzeilen "
            + "(Engine {EngineId})", job.Id, (DateTime.UtcNow - RunStart).TotalSeconds, ForeignReason, Tally.DataLines, _engineId);
        (job.ResultJson, job.ReachedDepth, job.EvalText, job.NodeStepsJson, job.SecondsSpent) = _atStart;
        job.CurrentDepth = 0; job.CurrentNps = 0;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        await _db.Entry(job).ReloadAsync(CancellationToken.None);
    }

    /// <summary>Die Tatsachen für <see cref="AnalysisJobRunEnd.ClassifyRunEnd"/> — nach <see cref="FinishAsync"/>
    /// bzw. <see cref="DiscardAsync"/>.</summary>
    internal RunFacts Facts(bool cancelledByUs, bool liveOnEngine, TimeSpan fruitlessMinRuntime) => new(
        _job.TargetNodes, _job.TargetDepth, _job.ReachedDepth, DepthAtStart,
        NodeGoalReached, CurrentNodes, EngineFinished, PreviousNodes,
        DirectEngine: _engineId.StartsWith(ExternalEngineRegistration.IdPrefix, StringComparison.Ordinal),
        CancelledByUs: cancelledByUs, LiveOnEngine: liveOnEngine, GotData: GotData,
        RunTime: DateTime.UtcNow - RunStart, fruitlessMinRuntime, _job.FruitlessAttempts,
        ForeignOutput: ForeignReason is not null);

    /// <summary>Sekunden auf eine Stelle, „—" wenn es den Messwert nicht gibt (nie eine Zeile gesehen).</summary>
    private static string Fmt(double? seconds) => seconds is { } v ? v.ToString("F1") : "—";

    /// <summary>Ergebnis + Rechenzeit sichern. Gibt zurück, wie viel Zeit NICHT verbucht wurde (der Bruchteil
    /// unter einer Sekunde) — der Aufrufer schiebt ihn ins nächste Intervall, sonst summierte sich bei jedem
    /// Persist ein verlorener Rest, und in der ersten Sekunden-Salve flacher Tiefen ginge fast alles verloren.</summary>
    private static async Task<TimeSpan> PersistProgressAsync(AppDbContext db, AnalysisJob job, string? line, int depth,
        TimeSpan elapsed, int currentDepth = 0, int currentNps = 0)
    {
        if (currentDepth > 0) job.CurrentDepth = currentDepth;
        if (currentNps > 0) job.CurrentNps = currentNps;
        if (line is not null)
        {
            job.ResultJson = line;
            job.ReachedDepth = Math.Max(job.ReachedDepth, depth);
            job.EvalText = AnalysisJobService.EvalTextOf(line);   // Listen zeigen nur diesen Wert, ohne die Roh-Zeile zu laden
        }
        var whole = (int)Math.Max(0, elapsed.TotalSeconds);
        job.SecondsSpent += whole;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return elapsed > TimeSpan.Zero ? elapsed - TimeSpan.FromSeconds(whole) : TimeSpan.Zero;
    }
}
