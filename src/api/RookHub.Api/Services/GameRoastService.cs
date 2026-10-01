using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Roast my game" (0.535.0): ein frecher Kommentar zur EIGENEN Partie, vom Sprachmodell auf eigener Hardware
/// (<see cref="IClaudeJsonClient.IsLocal"/> — über Claude entstünden Kosten), aus GEPRÜFTEN Fakten: Kopfdaten, Ergebnis
/// aus Sicht des Spielers, Genauigkeit je Seite und die schlimmsten Züge samt besserem Zug (<see cref="GameMistakes"/>).
///
/// <para><b>Drei Stile</b> (Wunsch des Nutzers, 2026-09-25): <c>friendly</c> (freundlich), <c>cheeky</c> (frech) und
/// <c>russian</c> — der gnadenlose sowjetische Trainer, derb, mit Kraftausdrücken, hinterfragt offen die geistige
/// Kapazität des Spielers („free for all"). Eine Grenze steht auch dort im Auftrag: keine Angriffe auf Herkunft,
/// Religion, Geschlecht, Sexualität oder Behinderung — der Text kann geteilt werden und landet bei anderen.</para>
/// <para>Nur der Besitzer, nur mit fertiger Analyse, nie automatisch veröffentlicht. Genannte Züge müssen in der Partie
/// bzw. den Engine-Linien stehen (<see cref="GameMoveExplanationService.MentionsOnly"/>), sonst eine Nachfrage. Gespeichert in
/// englischer Notation, gezeigt mit den Figurenbuchstaben der Sprache (<see cref="PieceLetters"/>, 0.541.1).</para>
/// </summary>
public sealed class GameRoastService
{
    public static readonly string[] Styles = { "friendly", "cheeky", "russian" };

    /// <summary>Deckel je Nutzer und 24 h — die Spark ist geteilt, und „Neu würfeln" lädt zum Dauerklicken ein.</summary>
    public const int MaxPerDay = 60;

    /// <summary>Obergrenze des Textes; eine längere Antwort gilt als misslungen.</summary>
    public const int MaxLength = 2000;

    private readonly AppDbContext _db;
    private readonly IClaudeJsonClient _llm;
    private readonly SavedGameService _games;
    private readonly ILogger<GameRoastService> _logger;

    // Keine Sperrzeit (0.585.0): „Würfeln" ist ein Auftrag auf Zuruf, und dafür steht die Spark auch tagsüber bereit.
    // Zurückgestellt werden nur die automatischen Roasts nach der Analyse — das macht der Scheduler (GameReviewTexts).
    public GameRoastService(AppDbContext db, IClaudeJsonClient llm, SavedGameService games, ILogger<GameRoastService> logger)
    {
        _db = db;
        _llm = llm;
        _games = games;
        _logger = logger;
    }

    public bool Available => _llm.IsConfigured && _llm.IsLocal;

    /// <summary>Ergebnis eines Würfelns: der Text, oder ein Grund (<c>notConfigured</c>, <c>notFound</c>,
    /// <c>noAnalysis</c>, <c>invalidStyle</c>, <c>dailyLimit</c>, <c>failed</c>; beim automatischen
    /// Schreiben <c>exists</c>).</summary>
    public sealed record RoastResult(GameRoastDto? Roast, string? Reason);

    public async Task<GameRoastsDto?> GetAsync(int userId, int gameId, string? lang, CancellationToken ct = default)
    {
        var language = GameMoveExplanationService.NormalizeLanguage(lang);
        var game = await _db.SavedGames.AsNoTracking().Where(g => g.Id == gameId && g.UserId == userId)
            .Select(g => new { g.Id, g.GameAnalysisId }).FirstOrDefaultAsync(ct);
        if (game == null) return null;
        return new GameRoastsDto
        {
            Available = Available,
            HasAnalysis = game.GameAnalysisId is int aid
                && await _db.GameAnalyses.AnyAsync(a => a.Id == aid && a.Status == GameAnalysisStatus.Done, ct),
            Items = (await _db.GameRoasts.AsNoTracking().Where(r => r.SavedGameId == gameId && r.Language == language)
                .OrderBy(r => r.Style)
                .Select(r => new GameRoastDto { Style = r.Style, Language = r.Language, Text = r.Text, CreatedAt = r.CreatedAt })
                .ToListAsync(ct))
                .Select(r => { r.Text = PieceLetters.Convert(r.Text, "en", r.Language); return r; }).ToList(),
        };
    }

    /// <param name="automatic">Nach der Analyse von selbst geschrieben (<see cref="GameReviewTexts"/>): zählt nicht gegen
    /// <see cref="MaxPerDay"/> und überschreibt keinen vorhandenen Text.</param>
    public async Task<RoastResult> RoastAsync(int userId, int gameId, string? style, string? lang, CancellationToken ct = default,
        bool automatic = false)
    {
        if (!Available) return new(null, "notConfigured");
        style = (style ?? "").Trim().ToLowerInvariant();
        if (!Styles.Contains(style)) return new(null, "invalidStyle");
        var language = GameMoveExplanationService.NormalizeLanguage(lang);

        var detail = await _games.GetAsync(userId, gameId);
        if (detail == null) return new(null, "notFound");
        var analysisId = await _db.SavedGames.Where(g => g.Id == gameId).Select(g => g.GameAnalysisId).FirstOrDefaultAsync(ct);
        var analysis = analysisId is int aid
            ? await _db.GameAnalyses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aid && a.Status == GameAnalysisStatus.Done, ct)
            : null;
        if (analysis == null) return new(null, "noAnalysis");

        if (!automatic)
        {
            var since = DateTime.UtcNow.AddDays(-1);
            var today = await _db.GameRoasts.CountAsync(r => r.SavedGame!.UserId == userId && !r.Automatic && r.CreatedAt >= since, ct);
            if (today >= MaxPerDay) return new(null, "dailyLimit");
        }
        else if (await _db.GameRoasts.AnyAsync(r => r.SavedGameId == gameId && r.Language == language && r.Style == style, ct))
            return new(null, "exists");

        var positions = await _db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == analysis.Id)
            .OrderBy(p => p.Ply).ToListAsync(ct);
        var flaws = GameMistakes.Find(positions, analysis.PlyCount);
        var facts = Facts(detail, analysis, positions, flaws);
        var allowed = positions.Select(p => p.GameMoveSan)
            .Concat(flaws.SelectMany(f => f.BestLine.Concat(f.Refutation))).ToList();

        var text = await GroundedText.WriteAsync(_llm, "roast", SystemPrompt(style, language), facts,
            "\n\nIMPORTANT: mention only moves that appear above.", Schema, 1500, MaxLength, allowed,
            () => _logger.LogInformation("Roast für Partie {GameId} verworfen (fremder Zug)", gameId), ct);
        if (text == null) return new(null, "failed");

        var row = await _db.GameRoasts.FirstOrDefaultAsync(r => r.SavedGameId == gameId && r.Language == language && r.Style == style, ct);
        if (row == null)
        {
            row = new GameRoast { SavedGameId = gameId, Language = language, Style = style };
            _db.GameRoasts.Add(row);
        }
        row.Text = text;
        row.Automatic = automatic;
        row.Model = _llm.TranslationModel;
        row.CreatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(new GameRoastDto
        {
            Style = style, Language = language, Text = PieceLetters.Convert(text, "en", language), CreatedAt = row.CreatedAt,
        }, null);
    }

    // ── Auftrag ────────────────────────────────────────────────────────────────────────────────────

    private static readonly JsonNode Schema = JsonNode.Parse(
        """{"type":"object","properties":{"roast":{"type":"string"}},"required":["roast"],"additionalProperties":false}""")!;

    internal static string SystemPrompt(string style, string lang)
    {
        var tone = style switch
        {
            "friendly" => "Tone: warm and funny, gentle teasing like a good friend at the chess club; end on an encouraging note.",
            "cheeky" => "Tone: cheeky and sarcastic — witty jabs at the worst moves and some mockery, but not cruel.",
            _ => "Tone: you are an old, merciless Soviet chess trainer. Be brutally harsh, crude and insulting; swearing is "
                 + "allowed. Tear the play apart, openly question the player's mental capacity and chess brain, mock their "
                 + "decisions and compare their play to things far below chess. Hold nothing back about the chess and the "
                 + "thinking behind it — but never attack ethnicity, nationality, religion, gender, sexuality or disability.",
        };
        return $"""
            You write a short roast of a chess game for the player who played it — they asked for it. Write in
            {GameMoveExplanationService.LanguageName(lang)}, 3 to 6 sentences, at most 110 words, addressed to the player ("you").
            {tone}
            Use ONLY the facts given. Mention moves only exactly as given, in English algebraic notation (e.g. Nf3, Bxh7+);
            no centipawn numbers. Return the JSON object {"{"}"roast": "..."{"}"}.
            """;
    }

    internal static string Facts(SavedGameDetailDto game, GameAnalysis analysis, IReadOnlyList<GameAnalysisPosition> positions,
        IReadOnlyList<GameMistakes.Flaw> flaws)
    {
        var side = game.OwnerSide;
        var lines = new List<string>
        {
            GameFacts.Players(game) + $", {positions.Count} half-moves.",
            side is "white" or "black"
                ? $"The player played {side} and {Outcome(game.Result, side)}."
                : "Which side the player had is unknown — roast both sides.",
            GameFacts.Accuracy(analysis),
            $"Opening: {string.Join(' ', positions.Take(8).Select(p => p.GameMoveSan))}",
        };
        var own = side is "white" or "black" ? flaws.Where(f => f.White == (side == "white")).ToList() : flaws.ToList();
        var counts = own.GroupBy(f => f.Class).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}{(g.Count() > 1 ? "s" : "")}");
        lines.Add(own.Count == 0 ? "The player made no inaccuracies or worse." : $"The player's errors: {string.Join(", ", counts)}.");
        foreach (var f in GameMistakes.Worst(own, 5))
        {
            var number = (f.FenBefore.Split(' ') is { Length: >= 6 } parts ? parts[5] : "?") + (f.White ? "." : "...");
            lines.Add($"- {number} {f.PlayedSan} ({f.Class}, winning chance {Math.Round(f.WinBefore)} % → {Math.Round(f.WinAfter)} %)"
                + (f.BestSan != null ? $"; better was {f.BestSan}" : "")
                + (f.Refutation.Count > 0 ? $"; punished by {string.Join(' ', f.Refutation.Take(3))}" : ""));
        }
        return string.Join('\n', lines);
    }

    private static string Outcome(string? result, string side) => (result, side) switch
    {
        ("1-0", "white") or ("0-1", "black") => "won",
        ("1-0", "black") or ("0-1", "white") => "lost",
        ("1/2-1/2", _) => "drew",
        _ => "the result is open",
    };
}
