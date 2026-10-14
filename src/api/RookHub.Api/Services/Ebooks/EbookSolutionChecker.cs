using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Stockfish als finale Prüfung der Diagramm-Lösungen (0.772.0):
/// <list type="bullet">
/// <item><b>Buchlösung vorhanden:</b> der erste Zug wird bewertet. Ist er der beste oder gleichwertig gewinnend → bestätigt.
/// Sonst wird im selben Lösungsabsatz nach einem anderen Startzug derselben Nummer gesucht (der gelesene war eine
/// Nebenvariante, gemessen an Woodpecker Nr. 631 und 1076) → ersetzt; findet sich keiner → fraglich (Lösung bleibt, Leser warnt).</item>
/// <item><b>Keine Buchlösung:</b> steht die Seite am Zug entscheidend (Matt oder ≥ 2 Bauern), wird die Engine-Hauptvariante
/// die Lösung (Quelle <c>engine</c>) — die Problemsammlungen ohne gedruckte Lösung werden so lösbar; sonst „keine Aufgabe“.</item>
/// </list>
/// </summary>
public static class EbookSolutionChecker
{
    /// <summary>Ab hier zählt eine Stellung ohne Buchlösung als Aufgabe (cp für die Seite am Zug).</summary>
    public const int DecisiveCp = 200;
    /// <summary>So viel darf der Buchzug schlechter sein als der beste, ohne „fraglich“ zu werden.</summary>
    public const int ToleranceCp = 120;
    /// <summary>Ein Buchzug, der noch so deutlich gewinnt, ist auch dann richtig, wenn ein anderer schneller gewinnt.</summary>
    public const int StillWinningCp = 400;

    /// <summary>Ist der Buchzug (Bewertung danach, aus Sicht der ziehenden Seite) gegenüber dem besten vertretbar?</summary>
    public static bool BookMoveOk(int best, int book) =>
        best - book <= ToleranceCp || book >= Math.Min(best, StillWinningCp) && book >= StillWinningCp;

    /// <summary>Kann Stockfish die Stellung bewerten? Erkannte Diagramme haben oft das falsche Zugrecht (Gegner steht im
    /// Schach) — daran stürzt Stockfish ab, und vor 0.783.1 blieb dadurch die Prüfung ALLER Bücher am ersten hängen.</summary>
    public static bool IsLegal(string fen)
    {
        try { ChessBoard.LoadFromFen(fen); }
        catch { return false; }
        return EngineBroker.WorkSanitizer.IsEngineSafe(fen);
    }

    /// <summary>Prüft EIN Diagramm und setzt Lösung/Prüfstand/Bewertung daran.</summary>
    public static async Task CheckAsync(IPositionEvaluator engine, EbookDiagram d, int moveTimeMs, CancellationToken ct)
    {
        if (!IsLegal(d.Fen)) return;   // Studien ohne König u. Ä.: die Engine kann nichts sagen
        var root = await engine.EvaluateAsync(d.Fen, null, moveTimeMs, ct);
        if (root == null) { d.SolutionCheck = d.SolutionUci == null ? EbookSolutionCheck.NoPuzzle : EbookSolutionCheck.Unchecked; return; }
        d.EngineEval = root.Score;

        if (!string.IsNullOrEmpty(d.SolutionUci))
        {
            d.SolutionSource ??= "book";
            var first = d.SolutionUci.Split(' ')[0];
            if (await FirstMoveOk(engine, d.Fen, root, first, moveTimeMs, ct)) { d.SolutionCheck = EbookSolutionCheck.Confirmed; return; }
            var alternatives = string.IsNullOrEmpty(d.SolutionText)
                ? []
                : EbookSolutionFinder.TryParseAll(d.Fen, true, d.SolutionText, d.SolutionPage ?? d.Page).Where(c => c.Uci[0] != first);
            foreach (var alt in alternatives)
            {
                if (!await FirstMoveOk(engine, d.Fen, root, alt.Uci[0], moveTimeMs, ct)) continue;
                d.SolutionUci = Clip(string.Join(' ', alt.Uci), 1200);
                d.SolutionSan = Clip(string.Join(' ', alt.San), 1500);
                d.SolutionCheck = EbookSolutionCheck.Replaced;
                return;
            }
            d.SolutionCheck = EbookSolutionCheck.Doubtful;
            return;
        }

        if (root.Score < DecisiveCp) { d.SolutionCheck = EbookSolutionCheck.NoPuzzle; return; }
        var deep = await engine.EvaluateAsync(d.Fen, null, moveTimeMs * 3, ct) ?? root;
        d.EngineEval = deep.Score;
        var maxPlies = deep.IsMate ? Math.Min(15, 2 * (10000 - deep.Score) - 1) : 7;
        var (uci, san) = Replay(d.Fen, deep.Pv.Take(Math.Max(1, maxPlies)));
        if (uci.Count == 0) { d.SolutionCheck = EbookSolutionCheck.NoPuzzle; return; }
        d.SolutionUci = Clip(string.Join(' ', uci), 1200);
        d.SolutionSan = Clip(string.Join(' ', san), 1500);
        d.SolutionText = null;
        d.SolutionPage = null;
        d.SolutionSource = "engine";
        d.SolutionCheck = EbookSolutionCheck.Engine;
    }

    private static async Task<bool> FirstMoveOk(IPositionEvaluator engine, string fen, UciResult root, string move, int moveTimeMs, CancellationToken ct)
    {
        if (root.Pv.Count > 0 && root.Pv[0] == move) return true;
        var after = await engine.EvaluateAsync(fen, [move], moveTimeMs, ct);
        // Kein Score nach dem Zug = Matt/Patt auf dem Brett: Matt setzen ist immer richtig.
        if (after == null) return true;
        return BookMoveOk(root.Score, -after.Score);
    }

    /// <summary>UCI-Folge ab der Stellung nachspielen → UCI + SAN (bricht beim ersten unspielbaren Zug ab).</summary>
    internal static (List<string> Uci, List<string> San) Replay(string fen, IEnumerable<string> moves)
    {
        var uci = new List<string>(); var san = new List<string>();
        ChessBoard board;
        try { board = ChessBoard.LoadFromFen(fen); } catch { return (uci, san); }
        foreach (var m in moves)
        {
            try
            {
                // Den legalen Zug mit genau dieser UCI-Schreibweise suchen — deckt Umwandlung und Rochade ab.
                var legal = board.Moves().FirstOrDefault(mv => PgnParser.ToUci(mv) == m);
                if (legal == null || !board.Move(legal)) break;
            }
            catch { break; }
            var done = board.ExecutedMoves[^1];
            uci.Add(PgnParser.ToUci(done));
            san.Add(done.San ?? m);
        }
        return (uci, san);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Hintergrunddienst der Prüfung: ein Buch je Durchlauf, eine Stockfish-Sitzung je Buch.</summary>
public class EbookSolutionCheckWorker : PeriodicWorker
{
    private readonly EbookOptions _options;

    public EbookSolutionCheckWorker(IServiceScopeFactory scopeFactory, IOptions<EbookOptions> options, ILogger<EbookSolutionCheckWorker> logger)
        : base(scopeFactory, logger) => _options = options.Value;

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromSeconds(30));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(2));
    protected override bool Enabled => _options.EngineCheck;
    protected override void LogDisabled() => Logger.LogInformation("E-Book-Lösungsprüfung: per Ebooks:EngineCheck abgeschaltet");
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "E-Book-Lösungsprüfung: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var ebook = await db.Ebooks
            .Where(b => b.DiagramState == EbookDiagramState.Done && b.DiagramCount > 0 && b.SolutionCheckState == EbookDiagramState.Pending)
            .OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (ebook == null) return;
        var path = StockfishPath.Resolve(_options.StockfishPath);
        var engine = await UciEngineSession.StartAsync(path, Logger, ct);
        if (engine == null)
        {
            ebook.SolutionCheckState = EbookDiagramState.Failed;
            await db.SaveChangesAsync(ct);
            await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
            return;
        }
        var diagrams = await db.EbookDiagrams.Where(d => d.EbookId == ebook.Id).OrderBy(d => d.Page).ThenBy(d => d.Y).ToListAsync(ct);
        var n = 0;
        try
        {
            foreach (var d in diagrams)
            {
                ct.ThrowIfCancellationRequested();
                try { await EbookSolutionChecker.CheckAsync(engine, d, _options.EngineMoveTimeMs, ct); }
                catch (IOException ex)
                {
                    // Stockfish ist an dieser Stellung abgestürzt: Diagramm auslassen, Engine neu starten, weiter —
                    // sonst scheitert das Buch bei jedem Durchlauf an derselben Stelle und blockiert alle dahinter.
                    Logger.LogWarning(ex, "E-Book {EbookId}: Stockfish an Diagramm {DiagramId} abgestürzt ({Fen})", ebook.Id, d.Id, d.Fen);
                    await engine.DisposeAsync();
                    engine = await UciEngineSession.StartAsync(path, Logger, ct)
                        ?? throw new IOException("Stockfish startet nach Absturz nicht neu");
                }
                if (++n % 25 == 0) await db.SaveChangesAsync(ct);
            }
        }
        finally { await engine.DisposeAsync(); }
        ebook.SolutionCheckState = EbookDiagramState.Done;
        await db.SaveChangesAsync(ct);
        await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
        Logger.LogInformation("E-Book {EbookId}: Lösungen geprüft — {Confirmed} bestätigt, {Replaced} ersetzt, {Doubtful} fraglich, {Engine} von Stockfish",
            ebook.Id, diagrams.Count(d => d.SolutionCheck == EbookSolutionCheck.Confirmed), diagrams.Count(d => d.SolutionCheck == EbookSolutionCheck.Replaced),
            diagrams.Count(d => d.SolutionCheck == EbookSolutionCheck.Doubtful), diagrams.Count(d => d.SolutionCheck == EbookSolutionCheck.Engine));
    }
}
