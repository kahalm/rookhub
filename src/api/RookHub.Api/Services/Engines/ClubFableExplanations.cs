using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Begründungen der automatischen Fehler-Vergleiche der Vereinspartien über Claude Fable (0.911.0, Wunsch 2026-11-08:
/// „mache dann (nur für Ligapartien ab dem 6.11.) auch die Analyse mit Fable über Token").
///
/// <list type="bullet">
/// <item><b>Für welche</b>: Zugvergleiche mit <see cref="MoveComparison.GameAnalysisId"/> (also die von
/// <see cref="ClubMistakeComparisons"/>), deren Vereinspartie AM ODER NACH <c>ClubMistakeComparisons:FableFrom</c>
/// (Vorgabe 2026-11-06) gespielt wurde — <see cref="GameDateAsync"/>: das Datum der zugeordneten Ligarunde
/// (<see cref="LeagueClubGame.LeagueTnr"/>/<see cref="LeagueClubGame.LeagueRound"/> → <see cref="LeagueRound.Date"/>), sonst das
/// Hochlade-Datum (<see cref="LeagueClubGame.CreatedAt"/>), sonst das Anlegen der Analyse. Die Partie selbst trägt nur das
/// Jahr. Ältere bleiben beim lokalen Modell.</item>
/// <item><b>Womit</b>: <see cref="AnthropicJsonClient"/> mit <c>:AnthropicModel</c> (claude-fable-5-1), Schlüssel
/// <c>:AnthropicApiKey</c>, sonst — ausdrücklich erlaubt — der Konto-Schlüssel <c>Anthropic:ApiKey</c>. Schalter
/// <c>:ExplainWithAnthropic</c> (Vorgabe AUS — die Dev-Stacks tragen denselben Konto-Schlüssel; <c>compose.vpn.yml</c> schaltet
/// es für Prod an); Schlüssel leer/Platzhalter = aus.</item>
/// <item><b>Kostenbremse</b>: höchstens <c>:AnthropicDailyCap</c> (200) Aufrufe je UTC-Tag (<see cref="TryTakeBudget"/>, auch
/// fehlgeschlagene und Wiederholungen; nach einem Neustart zählen die heute schon mit Fable fertig gewordenen Vergleiche),
/// <c>:AnthropicMaxTokens</c> (6000, Denken inklusive), <c>:AnthropicEffort</c> (low). Deckel, Fehler, Ablehnung oder ein Text
/// mit einem Zug, der in keiner Linie steht → das lokale Modell schreibt (nie ohne Begründung hängen bleiben).</item>
/// </list>
/// Singleton: hält Client und Tageszähler, die Datenbank kommt vom Aufrufer.
/// </summary>
public sealed class ClubFableExplanations
{
    public const string DefaultModel = "claude-fable-5-1";
    public static readonly DateOnly DefaultFrom = new(2026, 11, 6);
    public const int DefaultDailyCap = 200;
    public const int DefaultMaxTokens = 6000;
    public const int MaxTextLength = 3000;

    private readonly IClaudeJsonClient? _client;
    private readonly ILogger _logger;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private DateOnly _day;
    private int _used;

    public bool Enabled { get; }
    public DateOnly From { get; }
    public int DailyCap { get; }
    public int MaxTokens { get; }
    public string Model { get; }

    public ClubFableExplanations(IClaudeJsonClient? client, IConfiguration? config, ILogger<ClubFableExplanations> logger,
        Func<DateTime>? utcNow = null)
    {
        _client = client;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        Enabled = config?.GetValue<bool?>("ClubMistakeComparisons:ExplainWithAnthropic") ?? false;
        From = DateOnly.TryParse(config?["ClubMistakeComparisons:FableFrom"], CultureInfo.InvariantCulture, out var d) ? d : DefaultFrom;
        DailyCap = Math.Clamp(config?.GetValue<int?>("ClubMistakeComparisons:AnthropicDailyCap") ?? DefaultDailyCap, 0, 10_000);
        MaxTokens = Math.Clamp(config?.GetValue<int?>("ClubMistakeComparisons:AnthropicMaxTokens") ?? DefaultMaxTokens, 500, 32_000);
        Model = ModelOf(config);
    }

    /// <summary>Der Anthropic-Client nach Konfiguration (Schlüssel <c>:AnthropicApiKey</c>, sonst <c>Anthropic:ApiKey</c>).</summary>
    public static AnthropicJsonClient CreateClient(IConfiguration config, ILogger logger) =>
        new(SecretConfigCheck.Usable(config["ClubMistakeComparisons:AnthropicApiKey"]) ?? config["Anthropic:ApiKey"],
            ModelOf(config), logger, config["ClubMistakeComparisons:AnthropicEffort"]);

    private static string ModelOf(IConfiguration? config) =>
        config?["ClubMistakeComparisons:AnthropicModel"] is { Length: > 0 } m ? m.Trim() : DefaultModel;

    /// <summary>An, Schlüssel brauchbar.</summary>
    public bool Available => Enabled && _client is { IsConfigured: true };

    /// <summary>Gilt Fable für diesen Vergleich (Vereinspartie ab dem Stichtag)?</summary>
    public async Task<bool> AppliesAsync(AppDbContext db, MoveComparison c, CancellationToken ct = default)
        => Available && c.GameAnalysisId is int id && await GameDateAsync(db, id, ct) is { } date && date >= From;

    /// <summary>Spieldatum der Vereinspartie hinter einer Analyse (siehe Klassenkommentar); <c>null</c> = keine Vereinspartie.</summary>
    public static async Task<DateOnly?> GameDateAsync(AppDbContext db, int analysisId, CancellationToken ct = default)
    {
        var a = await db.GameAnalyses.AsNoTracking().Where(x => x.Id == analysisId)
            .Select(x => new { x.Origin, x.LeagueClubGameId, x.CreatedAt }).FirstOrDefaultAsync(ct);
        if (a is null || a.Origin != GameAnalysisOrigin.Club || a.LeagueClubGameId is not int gameId) return null;
        var g = await db.LeagueClubGames.AsNoTracking().Where(x => x.Id == gameId)
            .Select(x => new { x.LeagueTnr, x.LeagueRound, x.CreatedAt }).FirstOrDefaultAsync(ct);
        if (g is null) return null;
        if (g.LeagueTnr is int tnr && g.LeagueRound is int round
            && await db.LeagueRounds.AsNoTracking().Where(r => r.Tnr == tnr && r.Round == round && r.Date != null)
                .Select(r => r.Date).FirstOrDefaultAsync(ct) is { } roundDate)
            return roundDate;
        return DateOnly.FromDateTime(g.CreatedAt ?? a.CreatedAt);
    }

    /// <summary>Einen Aufruf vom Tagesdeckel nehmen. <paramref name="doneToday"/> = heute schon mit Fable fertige Vergleiche
    /// (nach einem Neustart weiß der Zähler sonst nichts davon).</summary>
    internal bool TryTakeBudget(int doneToday)
    {
        var today = DateOnly.FromDateTime(_utcNow());
        lock (_gate)
        {
            if (_day != today) { _day = today; _used = 0; }
            _used = Math.Max(_used, doneToday);
            if (_used >= DailyCap) return false;
            _used++;
            return true;
        }
    }

    /// <summary>Die Begründung von Fable, oder <c>null</c> (aus, Deckel, Fehler, unbrauchbar) — dann schreibt das lokale Modell.</summary>
    internal async Task<string?> ExplainAsync(AppDbContext db, MoveComparisonService.PairFacts facts, CancellationToken ct)
    {
        if (!Available) return null;
        var start = _utcNow().Date;
        var doneToday = await db.MoveComparisons.AsNoTracking().CountAsync(c => c.Model == Model && c.FinishedAt >= start, ct);
        var prompt = UserPrompt(facts);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!TryTakeBudget(doneToday))
            {
                _logger.LogInformation("Fable-Begründung: Tagesdeckel {Cap} erreicht — lokales Modell", DailyCap);
                return null;
            }
            var json = await _client!.CompleteJsonAsync("club-mistake-fable", SystemPrompt,
                attempt == 0 ? prompt : prompt + "\n\nWICHTIG: Deine letzte Antwort nannte einen Zug, der in keiner der Linien oben steht. Nenne NUR Züge aus den Linien oben.",
                Schema, MaxTokens, ct);
            var text = TextOf(json);
            if (text is null) return null;   // Fehler/Ablehnung/Deckel der Tokens: nicht noch einmal bezahlen
            // Gespeichert wird wie beim lokalen Modell in englischer SAN — die Anzeige setzt die Figurenbuchstaben der Sprache ein.
            // Merksatz (0.917.0): nur aus der kuratierten Liste, im festen Wortlaut — eine Kennung wird zum Satz, Erfundenes fällt weg.
            var english = ChessMaxims.Canonicalize(EnglishSan(text));
            if (GameMoveExplanationService.MentionsOnly(english, facts.AllowedMoves())) return english;
            _logger.LogInformation("Fable-Begründung verworfen (nennt einen fremden Zug)");
        }
        return null;
    }

    internal static readonly JsonNode Schema = JsonNode.Parse(
        """{"type":"object","properties":{"explanation":{"type":"string"}},"required":["explanation"],"additionalProperties":false}""")!;

    internal static string? TextOf(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("explanation", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()?.Trim() : null;
            return string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength ? null : text;
        }
        catch (JsonException) { return null; }
    }

    private static readonly Regex GermanPiece = new(@"(?<![A-Za-zÄÖÜäöüß0-9])([KDTLS])(?=[a-h]?[1-8]?x?[a-h][1-8])", RegexOptions.Compiled);
    private static readonly Regex GermanPromotion = new(@"=([DTLS])", RegexOptions.Compiled);

    /// <summary>Deutsche Figurenbuchstaben (K D T L S) in englische (K Q R B N) — nur für die Prüfung, ob der Text ausschließlich
    /// Züge aus den Linien nennt, und für die Ablage (englische SAN wie beim lokalen Modell; die Anzeige übersetzt zurück).</summary>
    internal static string EnglishSan(string text)
    {
        static string Map(string p) => p switch { "D" => "Q", "T" => "R", "L" => "B", "S" => "N", _ => p };
        var s = GermanPiece.Replace(text, m => Map(m.Groups[1].Value));
        return GermanPromotion.Replace(s, m => "=" + Map(m.Groups[1].Value));
    }

    /// <summary>Der Auftrag (Muster: die Erklärungen zu Partie 236 vom 08.11.).</summary>
    internal const string SystemPrompt =
        """
        Du bist ein erfahrener Schachtrainer und erklärst Klubspielern (Elo 1500–2000) einen Fehler aus ihrer eigenen Ligapartie.
        Die Engine hat den gespielten Zug (den schwächeren) mit dem besten Zug verglichen; du bekommst die Stellung, beide Züge,
        die Bewertungen, die besten Antworten des Gegners mit Engine-Linien und Tests, wie dieselben Antworten gegen den besten
        Zug ausgehen.

        Schreibe auf Deutsch, in zwei kurzen Absätzen und höchstens einem Merksatz, zusammen höchstens 170 Wörter:
        1. Was der gespielte Zug übersieht oder zulässt: nenne die konkrete Widerlegung aus den Linien (3 bis 6 Halbzüge, mit
           Zugnummern) und was sie bewirkt.
        2. Was der beste Zug stattdessen erreicht, ebenfalls mit einer kurzen Linie aus den Daten, und warum die Widerlegung
           dort nicht (so) funktioniert.
        3. Passt einer der Merksätze aus der Liste im Auftrag wirklich zu diesem Fehler, eine letzte Zeile
           „Merksatz: <Kennung>“ mit seiner Kennung (z. B. „Merksatz: raum-und-tausch“). Passt keiner genau, lass die Zeile
           weg. Erfinde nie einen eigenen Merksatz und schreibe den Wortlaut nicht selbst — er wird nach der Kennung eingesetzt.

        Regeln:
        - Nur, was die Linien und Bewertungen hergeben: keine eigenen Varianten, keine erfundenen Drohungen, Schläge oder
          Materialgewinne. Nenne nur Züge, die in den gegebenen Linien stehen.
        - Züge in deutscher Notation mit den Figurenbuchstaben K (König), D (Dame), T (Turm), L (Läufer), S (Springer), z. B.
          Sf3, Lxh7+, 0-0 — Bauernzüge ohne Buchstaben. Zugnummern wie „12.Sd3“ bzw. „12...f5“.
        - Bewertungen in Bauerneinheiten aus Sicht von Weiß mit Komma und Vorzeichen (z. B. +1,4); keine Centipawns, keine
          Prozente.
        - Ist der Unterschied klein (unter 0,5), sag das ehrlich und halte dich kurz — dann ist es eine Ungenauigkeit, kein Drama.
        - Sprich den Leser nicht direkt an; schreibe sachlich über Weiß und Schwarz.
        Gib das JSON-Objekt {"explanation": "..."} zurück; Absätze mit Leerzeile trennen.
        """;

    /// <summary>Die Fakten wie beim lokalen Modell (<see cref="MoveComparisonService.UserPrompt"/>, Bewertungen dort aus Sicht der
    /// Seite am Zug), dazu die Seite am Zug — der Text soll aus Sicht von Weiß bewerten.</summary>
    internal static string UserPrompt(MoveComparisonService.PairFacts f) =>
        MoveComparisonService.UserPrompt(f)
        + $"\n\nHinweis: Die Bewertungen oben sind aus Sicht des Spielers am Zug ({(f.MoverWhite ? "Weiß" : "Schwarz")}); "
        + "im Text bitte aus Sicht von Weiß angeben. Die Züge oben stehen in englischer Notation (N=S, B=L, R=T, Q=D)."
        + "\n\nMerksätze (nur diese, höchstens einer, per Kennung):\n" + ChessMaxims.PromptList();
}
