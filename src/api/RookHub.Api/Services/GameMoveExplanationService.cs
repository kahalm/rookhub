using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Warum war das ein Fehler?" (0.534.0): zu jedem Fehler einer analysierten Partie ein, zwei Sätze — geschrieben von
/// einem Sprachmodell auf EIGENER Hardware (<see cref="IClaudeJsonClient.IsLocal"/>; über Claude liefe es auf Kosten),
/// ausschließlich aus GEPRÜFTEN Fakten der Partie-Analyse (<see cref="GameMistakes.Find"/>: Stellung, gespielter Zug,
/// Bestzug mit Engine-Linie, Widerlegung, Gewinnchance vorher/nachher).
///
/// <para><b>Figurenbuchstaben</b> (0.541.1): gespeichert wird der Text in englischer Notation — so steht er in den
/// Fakten, und nur so lässt er sich gegen die Linien prüfen. Die Buchstaben der Sprache („Sf3" statt „Nf3") setzt ERST das
/// Lesen (<see cref="PieceLetters"/>): gespeichert umgestellt, würde ein zweites Umstellen im Französischen aus dem König
/// („R") einen Turm machen. Das Modell auf eigener Hardware stellt sie selbst praktisch nie um.</para>
/// <para><b>Gegen erfundene Züge</b> (<see cref="IsGrounded"/>): jeder Zug, den der Text in Notation nennt, muss in den
/// mitgegebenen Linien stehen — sonst wird einmal nachgefragt und danach verworfen. Ein lokales Modell rechnet nicht
/// verlässlich; eine Erklärung mit einem Zug, den es gar nicht gibt, wäre schlimmer als keine.</para>
/// <para>Der Text hängt an der ANALYSE (<see cref="GameMoveExplanation"/>) und gilt für alle, die die Partie sehen.
/// Erzeugt wird im Hintergrund, auf Wunsch des Besitzers, höchstens <see cref="MaxPerGame"/> je Partie und Sprache.</para>
/// <para><b>Aus der Sicht des Besitzers</b> (0.540.0): „du" ist immer er, auch bei den Fehlern seines Gegners — vorher
/// schrieb das Modell jeden Fehler an den, der ihn gemacht hatte („Your move 14. Nb5" für Weiß, obwohl der Besitzer
/// Schwarz spielte). Welche Seite er hatte, sagt <see cref="SavedGameService.DetermineOwnerSide"/>; unbekannt → neutral.</para>
/// </summary>
public sealed class GameMoveExplanationService
{
    /// <summary>Höchstens so viele Fehler je Partie — die schwersten (<see cref="GameMistakes.Worst"/>).</summary>
    public const int MaxPerGame = 15;

    /// <summary>So viele Anfragen gleichzeitig ans Modell (vLLM bündelt sie; einzeln ~5–10 s je Erklärung).</summary>
    public const int Parallel = 4;

    private readonly AppDbContext _db;
    private readonly IClaudeJsonClient _llm;
    private readonly GameExplanationJobs _jobs;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GameMoveExplanationService> _logger;

    public GameMoveExplanationService(AppDbContext db, IClaudeJsonClient llm, GameExplanationJobs jobs,
        IServiceScopeFactory scopes, ILogger<GameMoveExplanationService> logger)
    {
        _db = db;
        _llm = llm;
        _jobs = jobs;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Nur mit einem Modell auf eigener Hardware.</summary>
    public bool Available => _llm.IsConfigured && _llm.IsLocal;

    // ── Welche Analyse gehört zur Partie, aus wessen Sicht? ────────────────────────────────────────

    /// <summary>Die verknüpfte Analyse einer Partie und die Seite ihres Besitzers
    /// (<see cref="GameMoveExplanation.Viewpoint"/>: <c>white</c>, <c>black</c> oder leer).</summary>
    public sealed record ExplainedGame(int AnalysisId, string Viewpoint);

    /// <summary>Eine EIGENE Partie mit verknüpfter Analyse (oder <c>null</c>).</summary>
    public Task<ExplainedGame?> OwnGameAsync(int userId, int gameId, CancellationToken ct = default)
        => GameAsync(_db.SavedGames.Where(g => g.Id == gameId && g.UserId == userId), ct);

    /// <summary>Eine GETEILTE Partie mit vom Besitzer verknüpfter Analyse (oder <c>null</c>).</summary>
    public Task<ExplainedGame?> SharedGameAsync(string token, CancellationToken ct = default)
        => GameAsync(_db.SavedGames.Where(g => g.ShareToken == token), ct);

    private async Task<ExplainedGame?> GameAsync(IQueryable<SavedGame> query, CancellationToken ct)
    {
        var game = await query.AsNoTracking().Select(g => new SavedGame
        {
            UserId = g.UserId, Source = g.Source, White = g.White, Black = g.Black, OwnerSide = g.OwnerSide,
            GameAnalysisId = g.GameAnalysisId,
        }).FirstOrDefaultAsync(ct);
        if (game?.GameAnalysisId is not int id) return null;
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == game.UserId, ct);
        return new ExplainedGame(id, SavedGameService.DetermineOwnerSide(game, profile) ?? "");
    }

    // ── Lesen + Anstoßen ───────────────────────────────────────────────────────────────────────────

    public async Task<GameExplanationsDto> GetAsync(ExplainedGame? game, string lang, bool owner, CancellationToken ct = default)
    {
        lang = NormalizeLanguage(lang);
        var dto = new GameExplanationsDto { Available = Available, Language = lang };
        if (game == null) return dto;
        var id = game.AnalysisId;
        var analysis = await _db.GameAnalyses.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new { a.Status }).FirstOrDefaultAsync(ct);
        if (analysis == null) return dto;
        // Aus einer anderen Sicht geschrieben (Seite inzwischen festgelegt/geändert) = nicht mehr gültig.
        dto.Items = await _db.GameMoveExplanations.AsNoTracking()
            .Where(e => e.GameAnalysisId == id && e.Language == lang && e.Viewpoint == game.Viewpoint).OrderBy(e => e.Ply)
            .Select(e => new GameExplanationDto { Ply = e.Ply, Class = e.Class, Text = e.Text }).ToListAsync(ct);
        // Gespeichert in englischer Notation (so wird geprüft), gezeigt mit den Figurenbuchstaben der Sprache (0.541.1).
        foreach (var item in dto.Items) item.Text = PieceLetters.Convert(item.Text, "en", lang);
        dto.Running = _jobs.IsRunning(id, lang);
        dto.CanGenerate = owner && Available && !dto.Running && analysis.Status == GameAnalysisStatus.Done;
        return dto;
    }

    /// <summary>Erzeugen im Hintergrund anstoßen (idempotent: läuft schon eins, passiert nichts).</summary>
    public bool Start(ExplainedGame game, string lang)
    {
        lang = NormalizeLanguage(lang);
        var analysisId = game.AnalysisId;
        if (!Available || !_jobs.TryStart(analysisId, lang)) return false;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<GameMoveExplanationService>();
                await service.GenerateAsync(game, lang, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fehler-Erklärungen für Analyse {AnalysisId} ({Lang}) gescheitert", analysisId, lang);
            }
            finally
            {
                _jobs.Finish(analysisId, lang);
            }
        });
        return true;
    }

    /// <summary>Die fehlenden Erklärungen einer Analyse erzeugen; Rückgabe = neu gespeicherte.</summary>
    public async Task<int> GenerateAsync(ExplainedGame game, string lang, CancellationToken ct)
    {
        lang = NormalizeLanguage(lang);
        if (!Available) return 0;
        var (analysisId, viewpoint) = (game.AnalysisId, game.Viewpoint);
        var analysis = await _db.GameAnalyses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == analysisId, ct);
        if (analysis == null || analysis.Status != GameAnalysisStatus.Done) return 0;
        var positions = await _db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == analysisId).ToListAsync(ct);
        var existing = await _db.GameMoveExplanations.Where(e => e.GameAnalysisId == analysisId && e.Language == lang).ToListAsync(ct);
        // Aus einer anderen Sicht geschrieben: weg damit, sonst stünde der neue Text dem eindeutigen Index im Weg.
        var stale = existing.Where(e => e.Viewpoint != viewpoint).ToList();
        if (stale.Count > 0)
        {
            _db.GameMoveExplanations.RemoveRange(stale);
            await _db.SaveChangesAsync(ct);
        }
        var done = existing.Where(e => e.Viewpoint == viewpoint).Select(e => e.Ply).ToHashSet();
        var todo = GameMistakes.Worst(GameMistakes.Find(positions, analysis.PlyCount), MaxPerGame)
            .Where(f => !done.Contains(f.Ply)).ToList();
        if (todo.Count == 0) return 0;

        var system = SystemPrompt(lang);
        using var gate = new SemaphoreSlim(Parallel);
        var results = await Task.WhenAll(todo.Select(async flaw =>
        {
            await gate.WaitAsync(ct);
            try { return (flaw, text: await ExplainOneAsync(flaw, viewpoint, system, ct)); }
            finally { gate.Release(); }
        }));

        var saved = 0;
        foreach (var (flaw, text) in results.Where(r => r.text != null))
        {
            _db.GameMoveExplanations.Add(new GameMoveExplanation
            {
                GameAnalysisId = analysisId, Ply = flaw.Ply, Language = lang, Class = flaw.Class, Viewpoint = viewpoint, Text = text!,
                Model = _llm.TranslationModel,
            });
            saved++;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Fehler-Erklärungen für Analyse {AnalysisId} ({Lang}): {Saved} von {Todo} gespeichert",
            analysisId, lang, saved, todo.Count);
        return saved;
    }

    private async Task<string?> ExplainOneAsync(GameMistakes.Flaw flaw, string viewpoint, string system, CancellationToken ct)
    {
        var prompt = UserPrompt(flaw, viewpoint);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = await _llm.CompleteJsonAsync("explanation", system,
                attempt == 0 ? prompt : prompt + "\n\nIMPORTANT: your previous answer mentioned a move that is not in the lines above. Mention ONLY moves that appear in the lines above.",
                Schema, 1200, ct);
            var text = TextOf(json);
            if (text != null && IsGrounded(text, flaw)) return text;
            if (text != null) _logger.LogInformation("Fehler-Erklärung Halbzug {Ply} verworfen (nennt einen fremden Zug): {Text}", flaw.Ply, text);
        }
        return null;
    }

    // ── Prompt ─────────────────────────────────────────────────────────────────────────────────────

    private static readonly JsonNode Schema = JsonNode.Parse(
        """{"type":"object","properties":{"explanation":{"type":"string"}},"required":["explanation"],"additionalProperties":false}""")!;

    internal static string SystemPrompt(string lang) =>
        $"""
        You are a friendly, precise chess coach going through a game with the reader. A move in it was a mistake.
        Explain in {LanguageName(lang)}, in one or two short sentences (at most 45 words), WHY the played move was bad and
        WHAT the better move would have achieved. The last line of the facts says which side the reader played: write from the
        reader's point of view exactly as it says — "you" is always the reader, NOT automatically the side that moved.
        Address the reader informally where the language distinguishes (German "du", French "tu", …).
        Use ONLY the facts and lines given — never calculate your own variations and never mention a move that is not in
        the given lines. Write moves exactly as given, in English algebraic notation
        (e.g. Nf3, Bxh7+, O-O). No numbers of centipawns or percentages. Return the JSON object {"{"}"explanation": "..."{"}"}.
        """;

    /// <param name="viewpoint">Seite des Lesers (<c>white</c>/<c>black</c>, leer = unbekannt) — NICHT die Seite,
    /// die gezogen hat; die Fakten nennen deshalb die Farben statt „der Spieler".</param>
    internal static string UserPrompt(GameMistakes.Flaw f, string viewpoint)
    {
        var mover = f.White ? "White" : "Black";
        var other = f.White ? "Black" : "White";
        var number = MoveNumber(f.FenBefore) + (f.White ? "." : "...");
        var kind = f.Class switch
        {
            "inaccuracy" => "an inaccuracy (a small loss)",
            "mistake" => "a mistake",
            "blunder" => "a serious blunder",
            "miss" => $"a missed chance ({other} had just erred, and this move let the advantage slip)",
            _ => "a mistake",
        };
        var lines = new List<string>
        {
            $"Position before the move (FEN): {f.FenBefore}",
            $"{mover} played {number} {f.PlayedSan} — {kind}.",
            $"Winning chance for {mover}: {Math.Round(f.WinBefore)} % before, {Math.Round(f.WinAfter)} % after the move.",
            $"Engine evaluation from {mover}'s view: {f.EvalBefore} before, {f.EvalAfter} after the played move.",
        };
        if (f.BestSan != null)
            lines.Add($"Better for {mover} was {f.BestSan}. Engine line from the position before the move: {string.Join(' ', f.BestLine)}");
        if (f.Refutation.Count > 0)
            lines.Add($"{other}'s best answer to {f.PlayedSan}, with the engine line: {string.Join(' ', f.Refutation)}");
        lines.Add(Perspective(f.White, viewpoint));
        return string.Join('\n', lines);
    }

    /// <summary>Die letzte Zeile der Fakten: wer „du" ist.</summary>
    internal static string Perspective(bool moverWhite, string viewpoint)
    {
        var mover = moverWhite ? "White" : "Black";
        var other = moverWhite ? "Black" : "White";
        if (viewpoint is not ("white" or "black"))
            return "Reader: unknown side. Write neutrally about White and Black in the third person — do not use \"you\".";
        return (viewpoint == "white") == moverWhite
            ? $"Reader: played {mover} — this is the reader's OWN move. Address the reader as \"you\"; {other} is \"your opponent\"."
            : $"Reader: played {other} — this move was made by the reader's OPPONENT ({mover}), not by the reader. Address the "
              + $"reader as \"you\" and call {mover} \"your opponent\"; explain why the move was bad for your opponent and how "
              + "you can make use of it. Never call it \"your move\".";
    }

    private static string MoveNumber(string fen)
        => fen.Split(' ') is { Length: >= 6 } parts && int.TryParse(parts[5], out var n) ? n.ToString() : "?";

    private static string? TextOf(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("explanation", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()?.Trim() : null;
            return string.IsNullOrWhiteSpace(text) || text.Length > 1200 ? null : text;
        }
        catch (JsonException) { return null; }
    }

    // ── Prüfen ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Figurenzüge, Schlagzüge und Rochaden in englischer Notation. Bloße Felder („e4") bleiben außen vor —
    /// sie sind vom Feldnamen im Satz („der Springer auf e4") nicht zu unterscheiden.</summary>
    private static readonly Regex SanToken = new(
        @"(?<![A-Za-z0-9])(O-O-O|O-O|0-0-0|0-0|[KQRBN][a-h]?[1-8]?x?[a-h][1-8](?:=[QRBN])?|[a-h]x[a-h][1-8](?:=[QRBN])?)[+#!?]*(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    /// <summary>Nennt der Text nur Züge aus den mitgegebenen Linien (gespielter Zug, Bestlinie, Widerlegung)?</summary>
    internal static bool IsGrounded(string text, GameMistakes.Flaw f)
        => MentionsOnly(text, f.BestLine.Concat(f.Refutation).Append(f.PlayedSan));

    /// <summary>Nennt der Text (Figurenzüge, Schlagzüge, Rochaden) nur Züge aus <paramref name="allowed"/>? Auch für den
    /// Roast (<see cref="GameRoastService"/>).</summary>
    internal static bool MentionsOnly(string text, IEnumerable<string> allowed)
    {
        var set = new HashSet<string>(allowed.Select(Norm), StringComparer.Ordinal);
        foreach (Match m in SanToken.Matches(text))
            if (!set.Contains(Norm(m.Groups[1].Value))) return false;
        return true;
    }

    private static string Norm(string san) => san.TrimEnd('+', '#', '!', '?').Replace('0', 'O');

    // ── Sprache ────────────────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> Languages = new()
    {
        ["de"] = "German", ["en"] = "English", ["hr"] = "Croatian", ["hu"] = "Hungarian", ["fr"] = "French",
        ["es"] = "Spanish", ["it"] = "Italian", ["pl"] = "Polish", ["cs"] = "Czech", ["nl"] = "Dutch",
        ["pt"] = "Portuguese", ["ru"] = "Russian", ["uk"] = "Ukrainian", ["tr"] = "Turkish", ["sv"] = "Swedish",
        ["sl"] = "Slovenian", ["sk"] = "Slovak", ["ro"] = "Romanian", ["sr"] = "Serbian", ["bs"] = "Bosnian",
    };

    /// <summary>Ein bekanntes Kürzel, sonst Englisch.</summary>
    public static string NormalizeLanguage(string? lang)
    {
        var l = (lang ?? "").Trim().ToLowerInvariant();
        if (l.Length > 2) l = l[..2];
        return Languages.ContainsKey(l) ? l : "en";
    }

    internal static string LanguageName(string lang) => Languages.TryGetValue(lang, out var n) ? n : "English";
}

/// <summary>Welche Erklärungen gerade entstehen (Analyse + Sprache) — damit ein zweiter Klick nichts doppelt rechnet
/// und die Seite „läuft" zeigen kann. Nur Arbeitsspeicher: ein Neustart verwirft das Laufende, der nächste Klick
/// erzeugt nur, was noch fehlt.</summary>
public sealed class GameExplanationJobs
{
    private readonly ConcurrentDictionary<(int, string), byte> _running = new();
    public bool TryStart(int analysisId, string lang) => _running.TryAdd((analysisId, lang), 0);
    public void Finish(int analysisId, string lang) => _running.TryRemove((analysisId, lang), out _);
    public bool IsRunning(int analysisId, string lang) => _running.ContainsKey((analysisId, lang));
}
