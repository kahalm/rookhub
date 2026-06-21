using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Erzeugt vorberechnete, gestufte Lösungstipps (1=Motiv, 2=Figur/Bereich, 3=erster Zug) je Buch-Puzzle
/// und speichert sie sprach-keyed in <see cref="BookPuzzle.HintsJson"/>. Quellen: die VERIFIZIERTE
/// Lösungslinie (<see cref="BookPuzzle.Moves"/> ab <see cref="BookPuzzle.StartPly"/>), die Buch-Kommentare
/// und ein Stockfish-Signal (<see cref="StockfishAnalyzer"/>). Das LLM beschreibt nur die GEGEBENE Lösung
/// (kein Selbst-Lösen → kein Halluzinieren). Läuft asynchron im Import-/Reprocess-Pfad, idempotent.
/// </summary>
public class HintGenerationService
{
    /// <summary>Version des Tipp-Generators. Bei prompt-/format-relevanten Änderungen erhöhen → Reprocess
    /// erzeugt veraltete Tipps neu.</summary>
    public const int CurrentHintsVersion = 1;

    private static readonly string[] Languages = { "de", "en", "hr" };

    // Konkrete Felder/Rochade — in Stufe 1 + 2 NICHT erlaubt (Anti-Leak).
    private static readonly Regex CoordinatePattern = new(@"[a-h][1-8]|O-O", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IClaudeJsonClient _claude;
    private readonly StockfishAnalyzer _stockfish;
    private readonly ILogger<HintGenerationService> _logger;

    public HintGenerationService(AppDbContext db, IClaudeJsonClient claude, StockfishAnalyzer stockfish,
        ILogger<HintGenerationService> logger)
    {
        _db = db;
        _claude = claude;
        _stockfish = stockfish;
        _logger = logger;
    }

    /// <summary>True, wenn die Generierung möglich ist (API-Key gesetzt).</summary>
    public bool IsAvailable => _claude.IsConfigured;

    /// <summary>Generiert Tipps für mehrere Puzzles; isoliert Fehler je Puzzle. Liefert die Anzahl
    /// erfolgreich befüllter Puzzles.</summary>
    public async Task<int> GenerateForPuzzlesAsync(IEnumerable<int> ids, bool force = false, CancellationToken ct = default)
    {
        if (!_claude.IsConfigured) return 0;
        var done = 0;
        foreach (var id in ids.Distinct())
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (await GenerateForPuzzleAsync(id, force, ct)) done++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tipp-Generierung für Buch-Puzzle {Id} fehlgeschlagen.", id);
            }
        }
        if (done > 0) _logger.LogInformation("Tipps für {Count} Buch-Puzzles generiert.", done);
        return done;
    }

    /// <summary>Generiert Tipps für ein Puzzle. Idempotent: überspringt aktuelle Tipps, außer
    /// <paramref name="force"/>. Liefert true, wenn neue Tipps gespeichert wurden.</summary>
    public async Task<bool> GenerateForPuzzleAsync(int bookPuzzleId, bool force = false, CancellationToken ct = default)
    {
        if (!_claude.IsConfigured) return false;

        var bp = await _db.BookPuzzles.FirstOrDefaultAsync(p => p.Id == bookPuzzleId, ct);
        if (bp == null) return false;
        if (!force && bp.HintsVersion >= CurrentHintsVersion && !string.IsNullOrWhiteSpace(bp.HintsJson))
            return false;   // schon aktuell

        var setup = SetupMovesUci(bp.Moves, bp.StartPly);
        var solution = SolutionUci(bp.Moves, bp.StartPly);
        if (solution.Length == 0) return false;   // keine Lösung → nichts zu betipsen

        var engine = await _stockfish.AnalyzeAsync(bp.Fen, setup, ct);

        var result = new Dictionary<string, List<string>>();
        foreach (var lang in Languages)
        {
            if (ct.IsCancellationRequested) break;
            var json = await _claude.GenerateHintsJsonAsync(
                SystemPrompt(lang), BuildUserPrompt(bp, setup, solution, engine), ct);
            var hints = ParseAndValidate(json);
            if (hints != null) result[lang] = hints;
            else _logger.LogWarning("Tipp-Generierung: keine validen Tipps ({Lang}) für Puzzle {Id}.", lang, bp.Id);
        }

        if (result.Count == 0) return false;
        bp.HintsJson = JsonSerializer.Serialize(result);
        bp.HintsVersion = CurrentHintsVersion;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Lösungslinie zerlegen --------------------------------------------------------------------
    /// <summary>Setup-Züge (UCI) bis zur Lösungsstellung: erste (StartPly+1) Halbzüge; leer bei StartPly&lt;0
    /// (FEN ist bereits die Trainingsstellung).</summary>
    internal static string SetupMovesUci(string moves, int startPly)
    {
        var toks = Tokens(moves);
        var count = Math.Clamp(startPly + 1, 0, toks.Length);
        return string.Join(' ', toks.Take(count));
    }

    /// <summary>Lösungszüge (UCI) ab moves[StartPly+1].</summary>
    internal static string[] SolutionUci(string moves, int startPly)
    {
        var toks = Tokens(moves);
        var skip = Math.Clamp(startPly + 1, 0, toks.Length);
        return toks.Skip(skip).ToArray();
    }

    private static string[] Tokens(string? moves) =>
        (moves ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---- Validierung (Anti-Leak) ------------------------------------------------------------------
    /// <summary>Parst <c>{hint1,hint2,hint3}</c> und prüft: alle vorhanden+nichtleer; Stufe 1 &amp; 2
    /// enthalten KEINE konkreten Felder/Rochade (sonst verworfen → kein Leak ausspielen).</summary>
    internal static List<string>? ParseAndValidate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var h1 = root.TryGetProperty("hint1", out var e1) ? e1.GetString()?.Trim() : null;
            var h2 = root.TryGetProperty("hint2", out var e2) ? e2.GetString()?.Trim() : null;
            var h3 = root.TryGetProperty("hint3", out var e3) ? e3.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(h1) || string.IsNullOrEmpty(h2) || string.IsNullOrEmpty(h3)) return null;
            // Stufe 1 + 2 dürfen die Lösung nicht verraten.
            if (CoordinatePattern.IsMatch(h1) || CoordinatePattern.IsMatch(h2)) return null;
            return new List<string> { h1, h2, h3 };
        }
        catch { return null; }
    }

    // ---- Prompts ----------------------------------------------------------------------------------
    private static string SystemPrompt(string lang)
    {
        var language = lang switch { "de" => "German", "hr" => "Croatian", _ => "English" };
        return $$"""
            You write graded chess hints for a training puzzle. You are GIVEN the verified solution —
            never solve it yourself, only describe and grade it. Produce exactly three escalating hints:
            - hint1: the motif / idea only (e.g. "back-rank weakness"). NO concrete square, file, rank, piece move, or coordinate.
            - hint2: which piece or area delivers it, and why it works. Still NO concrete square or move (no coordinates like e4, no O-O).
            - hint3: the first move or key idea, may name the move (e.g. "Queen to h8"). Do NOT give the full variation.
            Keep each hint short (one sentence, encouraging). Write all three hints in {{language}}.
            Return only the JSON object {hint1,hint2,hint3}.
            """;
    }

    private string BuildUserPrompt(BookPuzzle bp, string setup, string[] solution, EngineHint? engine)
    {
        var sb = new StringBuilder();
        var sideToMove = SideToMoveAfterSetup(bp.Fen, setup);
        sb.AppendLine($"Start FEN: {bp.Fen}");
        if (!string.IsNullOrEmpty(setup)) sb.AppendLine($"Setup moves already played (UCI): {setup}");
        sb.AppendLine($"Side to move in the puzzle: {sideToMove}");
        sb.AppendLine($"Verified solution from this position (UCI): {string.Join(' ', solution)}");
        if (engine != null)
        {
            var forced = engine.MateIn is int m && m != 0 ? $" (forced mate in {Math.Abs(m)})" : "";
            sb.AppendLine($"Engine evaluation (side to move): {engine.EvalText}{forced}.");
        }
        if (!string.IsNullOrWhiteSpace(bp.Title)) sb.AppendLine($"Puzzle title: {bp.Title}");
        if (!string.IsNullOrWhiteSpace(bp.Tags)) sb.AppendLine($"Tags/themes: {bp.Tags}");
        var comments = CollectComments(bp);
        if (comments.Length > 0)
        {
            sb.AppendLine("Book annotations (human, may contain the idea):");
            sb.AppendLine(Truncate(comments, 1500));
        }
        return sb.ToString();
    }

    private static string SideToMoveAfterSetup(string fen, string setup)
    {
        var parts = fen.Split(' ');
        var whiteStart = parts.Length < 2 || parts[1] != "b";
        var setupCount = Tokens(setup).Length;
        var whiteToMove = setupCount % 2 == 0 ? whiteStart : !whiteStart;
        return whiteToMove ? "White" : "Black";
    }

    private static string CollectComments(BookPuzzle bp)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(bp.Comment)) sb.AppendLine(bp.Comment.Trim());
        if (!string.IsNullOrWhiteSpace(bp.MoveComments))
        {
            try
            {
                var mc = JsonSerializer.Deserialize<Dictionary<int, string>>(bp.MoveComments);
                if (mc != null)
                    foreach (var kv in mc.OrderBy(k => k.Key))
                        if (!string.IsNullOrWhiteSpace(kv.Value)) sb.AppendLine(kv.Value.Trim());
            }
            catch { /* defekte JSON-Kommentare ignorieren */ }
        }
        return sb.ToString().Trim();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
