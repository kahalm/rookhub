using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Kurz erzählt" (0.541.0): die Partie als Nacherzählung in zwei, drei Sätzen — für die Link-Vorschau des Teilen-Links
/// (og:description, <see cref="Og.OgMetaService"/>) und oben auf der Partieseite. Geschrieben vom Sprachmodell auf eigener
/// Hardware (<see cref="IClaudeJsonClient.IsLocal"/>) aus GEPRÜFTEN Fakten, im selben Muster wie der Roast
/// (<see cref="GameRoastService"/>): Kopfdaten, Eröffnung, der Verlauf nach der Bewertungskurve (<see cref="Course"/>) und
/// die Wendepunkte (<see cref="GameMistakes"/>).
///
/// <para><b>Dritte Person</b>, mit den Namen der Spieler: den Text liest, wer den Link bekommt — nicht der Besitzer.
/// Genannte Züge müssen in der Partie bzw. den Engine-Linien stehen (<see cref="GameMoveExplanationService.MentionsOnly"/>),
/// sonst eine Nachfrage; geht auch die schief, gibt es keinen Text (die Vorschau fällt auf die alte Beschreibung zurück).
/// Geprüft wird in englischer Notation (so stehen die Züge in den Fakten); DANACH stellt <see cref="PieceLetters"/> die
/// Figurenbuchstaben auf die Sprache des Textes um — die deutsche Vorschau liest „Sf3", nicht „Nf3". Das Modell auf eigener
/// Hardware stellt sie selbst praktisch nie um.</para>
/// <para>Entsteht von selbst nach der Analyse (<see cref="GameReviewTexts"/>) und, für ältere Analysen, beim Öffnen der
/// eigenen Partie (<c>GET /api/games/{id}/recap</c>). Kein Knopf und kein Tagesdeckel: je Partie und Sprache EIN Aufruf.</para>
/// </summary>
public sealed class GameRecapService
{
    /// <summary>Obergrenze des Textes — länger schneiden die Plattformen die Beschreibung der Vorschau ohnehin ab.</summary>
    public const int MaxLength = 600;

    /// <summary>So viele Abschnitte des Verlaufs bekommt das Modell höchstens — eine wilde Partie wäre sonst eine Liste.</summary>
    public const int MaxCourseSegments = 8;

    private readonly AppDbContext _db;
    private readonly IClaudeJsonClient _llm;
    private readonly SavedGameService _games;
    private readonly ILogger<GameRecapService> _logger;

    public GameRecapService(AppDbContext db, IClaudeJsonClient llm, SavedGameService games, ILogger<GameRecapService> logger)
    {
        _db = db;
        _llm = llm;
        _games = games;
        _logger = logger;
    }

    public bool Available => _llm.IsConfigured && _llm.IsLocal;

    /// <summary>Ergebnis des Schreibens: der Text, oder ein Grund (<c>notConfigured</c>, <c>exists</c>, <c>notFound</c>,
    /// <c>noAnalysis</c>, <c>failed</c>).</summary>
    public sealed record RecapResult(string? Text, string? Reason);

    /// <summary>Die Nacherzählung, die gezeigt wird: in der Sprache der Partie (<see cref="SavedGame.ReviewLanguage"/> — in ihr
    /// entsteht sie), sonst die jüngste. Link-Vorschau und Partieseite zeigen so denselben Text.</summary>
    public static async Task<GameRecap?> CurrentAsync(AppDbContext db, int savedGameId, string? gameLanguage,
        CancellationToken ct = default)
    {
        var rows = await db.GameRecaps.AsNoTracking().Where(r => r.SavedGameId == savedGameId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToListAsync(ct);
        if (rows.Count == 0) return null;
        var language = string.IsNullOrWhiteSpace(gameLanguage) ? null : GameMoveExplanationService.NormalizeLanguage(gameLanguage);
        return rows.FirstOrDefault(r => r.Language == language) ?? rows[0];
    }

    /// <summary>Stand für die eigene Partieseite; <c>null</c>, wenn es die Partie nicht gibt oder sie fremd ist.</summary>
    public async Task<GameRecapDto?> GetAsync(int userId, int gameId, CancellationToken ct = default)
    {
        var game = await _db.SavedGames.AsNoTracking().Where(g => g.Id == gameId && g.UserId == userId)
            .Select(g => new { g.Id, g.GameAnalysisId, g.ReviewLanguage }).FirstOrDefaultAsync(ct);
        if (game == null) return null;
        var row = await CurrentAsync(_db, game.Id, game.ReviewLanguage, ct);
        return new GameRecapDto
        {
            Available = Available,
            HasAnalysis = game.GameAnalysisId is int aid
                && await _db.GameAnalyses.AnyAsync(a => a.Id == aid && a.Status == GameAnalysisStatus.Done, ct),
            Text = row?.Text,
            Language = row?.Language,
            CreatedAt = row?.CreatedAt,
        };
    }

    /// <summary>Die Nacherzählung schreiben.</summary>
    /// <param name="replace">Nach der Vertiefung: einen vorhandenen Text ersetzen — die genauere Rechnung kann den
    /// Wendepunkt verschieben. Sonst bleibt er, Grund <c>exists</c>.</param>
    public async Task<RecapResult> WriteAsync(int userId, int gameId, string? lang, bool replace = false,
        CancellationToken ct = default)
    {
        if (!Available) return new(null, "notConfigured");
        var language = GameMoveExplanationService.NormalizeLanguage(lang);
        if (!replace && await _db.GameRecaps.AnyAsync(r => r.SavedGameId == gameId && r.Language == language, ct))
            return new(null, "exists");

        var detail = await _games.GetAsync(userId, gameId);
        if (detail == null) return new(null, "notFound");
        var analysisId = await _db.SavedGames.Where(g => g.Id == gameId).Select(g => g.GameAnalysisId).FirstOrDefaultAsync(ct);
        var analysis = analysisId is int aid
            ? await _db.GameAnalyses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aid && a.Status == GameAnalysisStatus.Done, ct)
            : null;
        if (analysis == null) return new(null, "noAnalysis");

        var positions = await _db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == analysis.Id)
            .OrderBy(p => p.Ply).ToListAsync(ct);
        var flaws = GameMistakes.Find(positions, analysis.PlyCount);
        var facts = Facts(detail, analysis, positions, flaws);
        var allowed = positions.Select(p => p.GameMoveSan)
            .Concat(flaws.SelectMany(f => f.BestLine.Concat(f.Refutation))).ToList();

        string? text = null;
        var system = SystemPrompt(language);
        for (var attempt = 0; attempt < 2 && text == null; attempt++)
        {
            var json = await _llm.CompleteJsonAsync("recap", system,
                attempt == 0 ? facts : facts + "\n\nIMPORTANT: at most 50 words; mention only moves that appear above.", Schema, 800, ct);
            var candidate = TextOf(json);
            if (candidate != null && GameMoveExplanationService.MentionsOnly(candidate, allowed))
                text = PieceLetters.Convert(candidate, "en", language);
            else if (candidate != null) _logger.LogInformation("Nacherzählung für Partie {GameId} verworfen (fremder Zug)", gameId);
        }
        if (text == null) return new(null, "failed");

        var row = await _db.GameRecaps.FirstOrDefaultAsync(r => r.SavedGameId == gameId && r.Language == language, ct);
        if (row == null)
        {
            row = new GameRecap { SavedGameId = gameId, Language = language };
            _db.GameRecaps.Add(row);
        }
        row.Text = text;
        row.Model = _llm.TranslationModel;
        row.CreatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(text, null);
    }

    // ── Auftrag ────────────────────────────────────────────────────────────────────────────────────

    private static readonly JsonNode Schema = JsonNode.Parse(
        """{"type":"object","properties":{"recap":{"type":"string"}},"required":["recap"],"additionalProperties":false}""")!;

    internal static string SystemPrompt(string lang) => $"""
        You write a short recap of a chess game. It appears in the preview of a shared link and above the game, so people
        read it before they replay the game. Write in {GameMoveExplanationService.LanguageName(lang)}, 2 or 3 sentences, at
        most 50 words, in the third person: call the players by their names exactly as given (not "you").
        Tell the story along the course of the game given: how it went, the decisive moment, how it ended. Lively like a
        short match report, but factual and friendly — no insults, no emoji, no hashtags. When you refer to a side instead
        of a player, use the words for White and Black of the language you write in. Name the opening only if it is given
        — do not guess it from the moves.
        Use ONLY the facts given. Mention at most three moves — the decisive ones, never a list of opening moves — exactly
        as given, in English algebraic notation with their move number (e.g. 23...Qh4). You may mention the accuracy, but
        no engine evaluations and no winning chances.
        Return the JSON object {"{"}"recap": "..."{"}"}.
        """;

    internal static string Facts(SavedGameDetailDto game, GameAnalysis analysis, IReadOnlyList<GameAnalysisPosition> positions,
        IReadOnlyList<GameMistakes.Flaw> flaws)
    {
        var ordered = positions.OrderBy(p => p.Ply).ToList();
        var lines = new List<string>
        {
            $"White: {game.White ?? "?"}{Elo(game.WhiteElo)}, Black: {game.Black ?? "?"}{Elo(game.BlackElo)}, result {game.Result ?? "*"}"
                + (game.TimeControl is { Length: > 0 } tc ? $", time control {tc}" : "") + $", {(ordered.Count + 1) / 2} moves.",
        };
        if (OpeningName(game.Pgn) is { } opening) lines.Add($"Opening: {opening}.");
        lines.Add($"First moves: {string.Join(' ', ordered.Take(10).Select(p => MoveNumber(p) + p.GameMoveSan))}");
        lines.Add($"Accuracy: White {Pct(analysis.AccuracyWhite)}, Black {Pct(analysis.AccuracyBlack)}.");

        var course = Course(ordered, analysis.PlyCount);
        if (course.Count > 0)
        {
            lines.Add("Course of the game (engine evaluation after each move):");
            lines.AddRange(course.Select(s => "- " + s));
        }

        var turning = GameMistakes.Worst(flaws.Where(f => f.Class is "mistake" or "blunder" or "miss"), 3);
        if (turning.Count == 0) lines.Add("Neither side made a mistake or a blunder.");
        else
        {
            lines.Add("Turning points:");
            foreach (var f in turning)
                lines.Add($"- {MoveNumber(f.FenBefore, f.White)}{f.PlayedSan} by {(f.White ? "White" : "Black")} ({f.Class})"
                    + (f.BestSan != null ? $"; better was {MoveNumber(f.FenBefore, f.White)}{f.BestSan}" : "")
                    + (f.Refutation.Count > 0 && ordered.FirstOrDefault(p => p.Ply == f.Ply + 1) is { } next
                        ? $"; answered by {Numbered(next.Fen, f.Refutation.Take(3))}" : ""));
        }
        lines.Add(Ending(game, ordered));
        return string.Join('\n', lines);
    }

    // ── Verlauf nach der Kurve ──────────────────────────────────────────────────────────────────────

    /// <summary>Die Lage in fünf Stufen, aus der Gewinnchance von Weiß (Lichess-Formel, <see cref="GameAccuracy.WinPercent"/>):
    /// ab 80 % gewinnt Weiß, ab 60 % steht Weiß besser, bis 40 % ausgeglichen — gespiegelt für Schwarz.</summary>
    internal static int StandingOf(double whiteWin) => whiteWin >= 80 ? 2 : whiteWin >= 60 ? 1 : whiteWin > 40 ? 0 : whiteWin > 20 ? -1 : -2;

    private static string Label(int standing) => standing switch
    {
        2 => "White is winning",
        1 => "White is better",
        0 => "roughly equal",
        -1 => "Black is better",
        _ => "Black is winning",
    };

    /// <summary>
    /// Der Verlauf der Partie aus der Bewertungskurve: die Lage NACH jedem Halbzug (<see cref="StandingOf"/>), zu
    /// Abschnitten zusammengefasst — „after 13.Nxe5: Black is winning". Ein Abschnitt aus EINEM Halbzug zwischen zwei gleichen
    /// fällt weg: das ist der Schlagzug vor dem Zurückschlagen, die Kurve zuckt dort nur. Ungerechnete Stellungen übernehmen
    /// die Lage davor. Mehr als <see cref="MaxCourseSegments"/> Abschnitte: der Anfang und die letzten, dazwischen ein Hinweis.
    /// </summary>
    internal static List<string> Course(IReadOnlyList<GameAnalysisPosition> positions, int plyCount)
    {
        var ordered = positions.OrderBy(p => p.Ply).ToList();
        var n = Math.Max(0, Math.Min(plyCount, ordered.Count));
        var byPly = ordered.Where(p => p.Ply >= 0 && p.Ply < n).ToDictionary(p => p.Ply);
        if (byPly.Count == 0) return new();
        var rows = new Dictionary<int, GameEvalPlyDto>();
        foreach (var p in byPly.Values)
            if (GameEvals.PlyOf(p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson, p.Depth) is { } dto) rows[p.Ply] = dto;
        var final = GameEvals.FinalOf(rows.TryGetValue(n - 1, out var lastRow) ? lastRow : null, n);

        var runs = new List<(int Start, int Standing, int Length)>();
        int? previous = null;
        for (var i = 0; i < n; i++)
        {
            // Die Lage nach Halbzug i = die Bewertung der Stellung vor Halbzug i + 1; nach dem letzten die des gespielten Zugs.
            (int? Cp, int? Mate, bool WhiteToMove)? eval = i + 1 < n
                ? rows.TryGetValue(i + 1, out var r) && byPly.TryGetValue(i + 1, out var next) ? (r.Cp, r.Mate, WhiteToMove(next.Fen)) : null
                : final is not null && byPly.TryGetValue(i, out var last) ? (final.Cp, final.Mate, !WhiteToMove(last.Fen)) : null;
            int? standing = eval is { } e && GameAccuracy.WinPercent(e.Cp, e.Mate, e.WhiteToMove) is double w ? StandingOf(w) : previous;
            if (standing is not int s) continue;
            if (runs.Count > 0 && runs[^1].Standing == s) runs[^1] = runs[^1] with { Length = runs[^1].Length + 1 };
            else runs.Add((i, s, 1));
            previous = s;
        }
        for (var k = 1; k < runs.Count - 1; k++)
        {
            if (runs[k].Length != 1 || runs[k - 1].Standing != runs[k + 1].Standing) continue;
            runs[k - 1] = runs[k - 1] with { Length = runs[k - 1].Length + 1 + runs[k + 1].Length };
            runs.RemoveRange(k, 2);
            k--;
        }

        string Describe((int Start, int Standing, int Length) run, bool first)
        {
            if (first) return $"from the start: {Label(run.Standing)}";
            var p = byPly[run.Start];
            return $"after {MoveNumber(p)}{p.GameMoveSan}: {Label(run.Standing)}";
        }
        var lines = runs.Select((run, k) => Describe(run, k == 0)).ToList();
        if (lines.Count > MaxCourseSegments)
        {
            var tail = MaxCourseSegments - 2;
            lines = lines.Take(1).Append("(several more swings)").Concat(lines.Skip(lines.Count - tail)).ToList();
        }
        return lines;
    }

    // ── Kleinteile ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>„13." bzw. „13..." aus dem Zugzähler der Stellung VOR dem Halbzug.</summary>
    private static string MoveNumber(GameAnalysisPosition p) => MoveNumber(p.Fen, WhiteToMove(p.Fen));

    private static string MoveNumber(string fenBefore, bool white)
        => (fenBefore.Split(' ') is { Length: >= 6 } parts ? parts[5] : "?") + (white ? "." : "...");

    /// <summary>Eine Linie ab der Stellung mit Zugnummern, wie man sie schreibt: „8.Nf3 Qd8 9.Bd3", mit Schwarz am Zug
    /// „8...Qd8 9.Bd3" — das Modell übernimmt die Züge, wie sie dastehen, und soll sie mit Nummer nennen.</summary>
    internal static string Numbered(string fen, IEnumerable<string> sans)
    {
        var white = WhiteToMove(fen);
        var number = fen.Split(' ') is { Length: >= 6 } parts && int.TryParse(parts[5], out var n) ? n : 1;
        var tokens = new List<string>();
        foreach (var san in sans)
        {
            tokens.Add(white ? $"{number}.{san}" : tokens.Count == 0 ? $"{number}...{san}" : san);
            if (!white) number++;
            white = !white;
        }
        return string.Join(' ', tokens);
    }

    /// <summary>Wie die Partie endete: Matt auf dem Brett, sonst der Termination-Header (außer dem nichtssagenden
    /// „Normal" von Lichess), sonst nur das Ergebnis — ob aufgegeben oder auf Zeit, steht dann nirgends.</summary>
    internal static string Ending(SavedGameDetailDto game, IReadOnlyList<GameAnalysisPosition> ordered)
    {
        var last = ordered.LastOrDefault();
        if (last != null && last.GameMoveSan.EndsWith('#'))
            return $"The game ended in checkmate with {MoveNumber(last)}{last.GameMoveSan}.";
        if (PgnHeader(game.Pgn, "Termination") is { Length: > 0 } t && !t.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return $"How it ended: {t}.";
        return game.Result switch
        {
            "1-0" => "White won (no checkmate on the board — not stated whether by resignation or on time).",
            "0-1" => "Black won (no checkmate on the board — not stated whether by resignation or on time).",
            "1/2-1/2" => "The game was drawn.",
            _ => "The result is open.",
        };
    }

    /// <summary>Der Eröffnungsname: Lichess schreibt ihn in <c>[Opening]</c>, chess.com nur in die Adresse <c>[ECOUrl]</c>
    /// (<c>…/openings/Sicilian-Defense-Najdorf-Variation</c>).</summary>
    internal static string? OpeningName(string? pgn)
    {
        if (string.IsNullOrEmpty(pgn)) return null;
        if (PgnHeader(pgn, "Opening") is { Length: > 0 } opening && opening != "?") return opening;
        if (PgnHeader(pgn, "ECOUrl") is { Length: > 0 } url && url.LastIndexOf("/openings/", StringComparison.Ordinal) is var i and >= 0)
        {
            var slug = url[(i + "/openings/".Length)..].Split('?', '#')[0].Trim('/');
            var name = Uri.UnescapeDataString(slug).Replace('-', ' ').Trim();
            return name.Length > 0 ? name : null;
        }
        return null;
    }

    private static string? PgnHeader(string pgn, string tag)
    {
        var m = Regex.Match(pgn, "\\[" + Regex.Escape(tag) + "\\s+\"([^\"]*)\"\\]");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static bool WhiteToMove(string fen) => !(fen.Split(' ') is { Length: >= 2 } parts && parts[1] == "b");

    private static string Elo(int? elo) => elo is int e ? $" ({e})" : "";
    private static string Pct(double? v) => v is double d ? $"{Math.Round(d)} %" : "unknown";

    private static string? TextOf(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("recap", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()?.Trim() : null;
            return string.IsNullOrWhiteSpace(text) || text.Length > MaxLength ? null : text;
        }
        catch (JsonException) { return null; }
    }
}
