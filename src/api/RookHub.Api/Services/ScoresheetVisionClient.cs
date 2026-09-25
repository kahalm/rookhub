using System.Text.Json;
using Anthropic.Models.Messages;

namespace RookHub.Api.Services;

/// <summary>Antwort des Modells: das JSON, oder ein Grund, warum es keins gibt.</summary>
/// <param name="Json">Die Lesung nach dem Schema aus <see cref="ScoresheetPrompt.Schema"/>.</param>
/// <param name="Error"><c>notConfigured</c>, <c>refused</c>, <c>truncated</c> oder <c>failed</c>.</param>
/// <param name="InputTokens">Verbrauchte Eingabe-Tokens — auch bei einem Fehler, soweit die API sie gemeldet hat.</param>
/// <param name="OutputTokens">Verbrauchte Ausgabe-Tokens inklusive Nachdenken.</param>
public sealed record ScoresheetVisionResult(string? Json, string? Error, int InputTokens = 0, int OutputTokens = 0);

/// <summary>Wie gelesen wird.</summary>
public enum ScoresheetReadMode
{
    /// <summary>Mit Nachdenken und dem vollen Auftrag (<see cref="ScoresheetPrompt.System"/>: Partie im Kopf mitspielen).</summary>
    Full,

    /// <summary>
    /// Ohne Nachdenken, nur abschreiben (<see cref="ScoresheetPrompt.TranscribeSystem"/>) — der Rückfall, wenn eine
    /// Lesung mit Nachdenken am Deckel abgeschnitten wurde: bei einem langen, verbesserten Formular (60 Züge,
    /// Streichungen, Pfeile) dachte Claude am 25.09. auf Prod 64 000 Tokens lang nach und schrieb kein einziges Zeichen
    /// Antwort (1,63 $). Die Legalität prüft ohnehin der Auflöser.
    /// </summary>
    Transcribe,
}

/// <summary>
/// Liest ein Partieformular vom Foto — hinter einem Interface, damit <see cref="ScoresheetScanService"/>
/// ohne echten API-Aufruf testbar ist (dasselbe Muster wie <see cref="IClaudeJsonClient"/>).
/// </summary>
public interface IScoresheetVisionClient
{
    /// <summary>True, wenn ein API-Key konfiguriert ist (<c>Anthropic:ApiKey</c>).</summary>
    bool IsConfigured { get; }

    /// <summary>Welches Modell liest — gehört an die gespeicherte Einlesung.</summary>
    string Model { get; }

    /// <summary>Bild + Auftrag → JSON nach <see cref="ScoresheetPrompt.Schema"/>.</summary>
    /// <param name="maxTokens">Antwort-Deckel (Nachdenken + JSON) — aus dem Budget (<see cref="ScoresheetBudget.Allowance"/>).</param>
    /// <param name="mode">Mit Nachdenken (Vorgabe) oder nur abschreiben — siehe <see cref="ScoresheetReadMode"/>.</param>
    Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens, CancellationToken ct = default,
        ScoresheetReadMode mode = ScoresheetReadMode.Full);
}

/// <summary>Echte Implementierung über die offizielle Anthropic-C#-SDK (Bild + structured output).</summary>
public class ClaudeScoresheetVisionClient : IScoresheetVisionClient
{
    private readonly Anthropic.AnthropicClient? _client;
    private readonly ILogger<ClaudeScoresheetVisionClient> _logger;

    public ClaudeScoresheetVisionClient(IConfiguration config, ILogger<ClaudeScoresheetVisionClient> logger)
    {
        _logger = logger;
        // Handschrift lesen UND die Partie im Kopf mitspielen ist die schwerste Aufgabe, die RookHub einem
        // Modell stellt — deshalb das große Modell mit Nachdenken, nicht das Übersetzungsmodell.
        // Vorgabe seit 0.533.2: Opus 5.5 — mit „nur abschreiben" (Scoresheet:Thinking, Vorgabe aus) die Referenz des
        // Modellvergleichs vom 25.09.2026 (siehe CLAUDE.md, Partieformular einlesen).
        Model = config["Anthropic:ScoresheetModel"] ?? "claude-opus-5-5";
        // low|medium|high|xhigh|max — leer = die Vorgabe des Modells. Bei Opus 5.5 ist effort der EINZIGE Hebel gegen
        // zu langes Nachdenken (abschalten lässt es sich dort nicht).
        Effort = string.IsNullOrWhiteSpace(config["Anthropic:ScoresheetEffort"]) ? null
            : config["Anthropic:ScoresheetEffort"]!.Trim().ToLowerInvariant();
        var key = config["Anthropic:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key))
            _client = new Anthropic.AnthropicClient { ApiKey = key };
    }

    public bool IsConfigured => _client != null;

    public string Model { get; }

    /// <summary>Eingestellter Denkaufwand (<c>Anthropic:ScoresheetEffort</c>), <c>null</c> = Vorgabe des Modells.</summary>
    public string? Effort { get; }

    /// <summary>Wie ein Aufruf nachdenkt: abgeschaltet, und mit welchem <c>effort</c>.</summary>
    internal sealed record ThinkingPlan(bool Disabled, string? Effort);

    /// <summary>
    /// Nachdenken je Modell und Modus. „Nur abschreiben" schaltet es ab — außer bei Modellen, bei denen das nicht
    /// geht (Opus 5.5, Fable/Mythos: Nachdenken immer an): dort wird es mit <c>effort: low</c> so klein wie möglich.
    /// Opus 5 erlaubt das Abschalten nur bis <c>effort: high</c> — ein eingestelltes <c>xhigh</c>/<c>max</c> fällt dann weg.
    /// </summary>
    internal static ThinkingPlan PlanThinking(string model, ScoresheetReadMode mode, string? effort)
    {
        if (mode != ScoresheetReadMode.Transcribe) return new(false, effort);
        var alwaysOn = model.StartsWith("claude-opus-5-5", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("claude-fable", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("claude-mythos", StringComparison.OrdinalIgnoreCase);
        if (alwaysOn) return new(false, "low");
        return new(true, effort is "xhigh" or "max" ? null : effort);
    }

    private static Anthropic.Models.Messages.Effort? EffortOf(string? effort) => effort switch
    {
        "low" => Anthropic.Models.Messages.Effort.Low,
        "medium" => Anthropic.Models.Messages.Effort.Medium,
        "high" => Anthropic.Models.Messages.Effort.High,
        "xhigh" => Anthropic.Models.Messages.Effort.Xhigh,
        "max" => Anthropic.Models.Messages.Effort.Max,
        _ => null,
    };

    public async Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens,
        CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full)
    {
        if (_client == null) return new(null, "notConfigured");
        try
        {
            var transcribe = mode == ScoresheetReadMode.Transcribe;
            var plan = PlanThinking(Model, mode, Effort);
            var format = new JsonOutputFormat { Schema = ScoresheetPrompt.Schema() };
            var outputConfig = EffortOf(plan.Effort) is { } effort
                ? new OutputConfig { Format = format, Effort = effort }
                : new OutputConfig { Format = format };
            var parameters = new MessageCreateParams
            {
                Model = Model,
                MaxTokens = Math.Clamp(maxTokens, 1024, ScoresheetBudget.MaxOutputTokens),
                System = transcribe ? ScoresheetPrompt.TranscribeSystem : ScoresheetPrompt.System,
                Thinking = plan.Disabled ? new ThinkingConfigDisabled() : new ThinkingConfigAdaptive(),
                OutputConfig = outputConfig,
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = new List<ContentBlockParam>
                        {
                            new ImageBlockParam
                            {
                                Source = new Base64ImageSource
                                {
                                    Data = Convert.ToBase64String(jpeg),
                                    MediaType = MediaType.ImageJpeg,
                                },
                            },
                            new TextBlockParam { Text = instructions },
                        },
                    },
                ],
            };

            // Gestreamt: mit Nachdenken und einer langen Partie kann die Antwort Minuten brauchen — ohne
            // Stream liefe sie in den HTTP-Timeout der SDK. Eingesammelt wird nur der Text (das Nachdenken
            // kommt ohnehin leer) und der Stoppgrund.
            var text = new System.Text.StringBuilder();
            string? stop = null;
            int input = 0, output = 0;
            try
            {
                await foreach (var ev in _client.Messages.CreateStreaming(parameters, ct))
                {
                    if (ev.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var delta))
                        text.Append(delta.Text);
                    else if (ev.TryPickStart(out var start))
                        input = (int)start.Message.Usage.InputTokens;
                    else if (ev.TryPickDelta(out var messageDelta))
                    {
                        // Die Ausgabe-Tokens kommen kumuliert mit dem letzten Delta (inklusive Nachdenken).
                        output = (int)messageDelta.Usage.OutputTokens;
                        // Vergleich über die Gleichheit mit der API-Zeichenkette, NICHT über ToString(): das lieferte
                        // am 10er-Testsatz für ein abgeschnittenes Ende nicht „max_tokens" — die Einlesung meldete
                        // „failed" statt „truncated", und dieselbe Lücke hätte eine Ablehnung verschluckt.
                        if (messageDelta.Delta.StopReason is { } reason)
                            stop = reason == "refusal" ? "refusal" : reason == "max_tokens" ? "max_tokens" : "end";
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Abgebrochen mitten im Strom: was bis dahin gemeldet wurde, ist verbraucht und wird verbucht.
                _logger.LogWarning(ex, "Formular-Lesung via Claude abgebrochen.");
                return new(null, "failed", input, output);
            }
            if (stop == "refusal")
            {
                _logger.LogWarning("Formular-Lesung abgelehnt (refusal).");
                return new(null, "refused", input, output);
            }
            if (stop == "max_tokens")
            {
                _logger.LogWarning("Formular-Lesung am Token-Deckel abgeschnitten.");
                return new(null, "truncated", input, output);
            }
            var json = text.ToString();
            return string.IsNullOrWhiteSpace(json) ? new(null, "failed", input, output) : new(json, null, input, output);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Formular-Lesung via Claude fehlgeschlagen.");
            return new(null, "failed");
        }
    }
}

/// <summary>Auftrag und Antwortschema für die Formular-Lesung (reine Texte, testbar).</summary>
public static class ScoresheetPrompt
{
    public const string System =
        """
        You read photographed chess scoresheets (handwritten game records) and turn them into a game.

        Work like a strong club player who has to type up a messy scoresheet:
        - Look at the WHOLE sheet first before committing to any single move. Play the game on a board in your
          head from the first move. Later moves regularly settle earlier ones: a piece that later moves from a
          square tells you where it went before; a capture on a square tells you something stood there; a
          check tells you where the king is.
        - Every move you output must be legal in the position reached by the moves before it. If what is
          written cannot be legal, find the most plausible legal move the writer meant (a misread letter, a
          wrong file, a missing capture sign, a move written in the wrong row) and lower the confidence.
        - The sheet may use any notation language. Piece letters differ: German K D T L S, French R D T F C,
          Spanish/Italian R D T A C, Dutch K D T L P, Polish K H W G S, Czech K D V S J, Hungarian K V B F H,
          Russian Кр Ф Л С К, English K Q R B N, figurines are also common. Pawns have no letter. Long notation
          (e2-e4, Sg1-f3) and castling as 0-0 / 0-0-0 occur.
        - Scoresheets contain corrections: crossed-out entries (use the replacement), arrows moving an entry
          to another cell, a move written in the opponent's column, skipped rows, rows written twice. The
          numbers printed on the form can therefore be off — number the moves as they were actually played,
          starting at 1.
        - Ignore clock times, minutes and other marks next to the moves, and signatures.
        - Stop at the last move that is actually written. Do not invent moves. If the last entry is partly
          illegible, give your best guess with low confidence and alternatives.

        For each half-move report exactly what is written (in the sheet's own notation), your reading as
        standard English SAN (K Q R B N, O-O, x for captures, + for check, =Q for promotion), up to three
        alternative readings (most likely first) when you are not sure, and a confidence.
        """;

    /// <summary>
    /// Auftrag für kleinere, offene Vision-Modelle (Qwen3-VL auf eigener Hardware): NUR abschreiben. Das große
    /// Modell spielt die Partie im Kopf mit und „repariert" dabei; ein 30-B-Modell tut das schlechter, als es liest,
    /// und seine Reparaturen verdecken, was wirklich dasteht. Die Legalität prüft ohnehin der Auflöser.
    /// </summary>
    public const string TranscribeSystem =
        """
        You transcribe photographed chess scoresheets (handwritten game records). Copy, do not correct.

        - Read the move table row by row: move number, White's move, Black's move. If the sheet has several
          column blocks (for example moves 1-30 and 31-60 side by side), read the first block to its end, then
          the next.
        - "written" is exactly what stands in the cell, in the sheet's own notation. Piece letters differ by
          language: German K D T L S, Portuguese/Spanish R D T B/A C, French R D T F C, English K Q R B N.
          Pawns have no letter. Castling may be 0-0 or O-O.
        - "san" is the same move with the piece letter translated to English (K Q R B N). Change nothing else:
          do not move squares or add captures to make a move legal.
        - Ignore clock times, minutes, signatures and other marks. For a crossed-out entry use the replacement.
        - Stop at the last written move. Never invent moves.
        - confidence: "high" = clearly legible, "medium" = probably right, "low" = hard to read. For anything
          that is not clearly legible give up to three alternative readings, most likely first.
        """;

    /// <summary>Auftrag für die erste Lesung.</summary>
    /// <param name="languageHint">Code aus <see cref="ScoresheetNotation.Languages"/> oder <c>auto</c>.</param>
    public static string FirstRead(string languageHint)
    {
        var lang = ScoresheetNotation.Find(languageHint);
        var hint = lang == null
            ? "The notation language is unknown; determine it from the sheet."
            : $"The user says the sheet is written in {lang.Name} notation (pieces: K={lang.King} Q={lang.Queen} R={lang.Rook} B={lang.Bishop} N={lang.Knight}).";
        return hint + " Transcribe the scoresheet in the photo.";
    }

    /// <summary>Nachfrage, wenn die Züge ab einer Stelle nicht mehr legal aufgehen.</summary>
    public static string Repair(string languageHint, string previousJson, IReadOnlyList<string> acceptedSans,
        int stuckIndex, ScannedPly stuckPly, string fen, IReadOnlyList<string> legalMoves)
    {
        var moveNo = stuckIndex / 2 + 1;
        var side = stuckIndex % 2 == 0 ? "White" : "Black";
        var accepted = acceptedSans.Count == 0
            ? "(none)"
            : Services.PgnWriter.MoveText(acceptedSans, result: null);
        return FirstRead(languageHint) + $"""


            This is a second look. Your previous transcription was:
            {previousJson}

            Checking it on a board, the moves up to here work out as:
            {accepted}
            but then {side}'s move {moveNo} (you wrote "{stuckPly.San}", read from "{stuckPly.Written}") cannot be
            played: it is not legal in the position {fen}
            Legal moves there are: {string.Join(' ', legalMoves)}

            Look at the sheet again around this move. Often an EARLIER entry was misread and only becomes
            impossible here, or a row was skipped or an entry moved by an arrow. Return the complete corrected
            transcription from move 1.
            """;
    }

    /// <summary>Das Antwortschema (structured output). Alle Felder Pflicht — „nicht vorhanden" ist ein leerer
    /// String, weil strukturierte Ausgaben keine optionalen Felder kennen.</summary>
    public static Dictionary<string, JsonElement> Schema()
    {
        var str = new { type = "string" };
        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                notationLanguage = new
                {
                    type = "string",
                    @enum = ScoresheetNotation.Languages.Select(l => l.Code).Append("unknown").ToArray(),
                },
                @event = str,
                site = str,
                date = str,
                dateIso = new { type = "string", description = "YYYY-MM-DD or empty" },
                round = str,
                white = str,
                black = str,
                result = new { type = "string", @enum = new[] { "1-0", "0-1", "1/2-1/2", "*" } },
                moves = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            moveNumber = new { type = "integer" },
                            color = new { type = "string", @enum = new[] { "w", "b" } },
                            written = str,
                            san = str,
                            alternatives = new { type = "array", items = str },
                            confidence = new { type = "string", @enum = new[] { "high", "medium", "low" } },
                            note = str,
                        },
                        required = new[] { "moveNumber", "color", "written", "san", "alternatives", "confidence", "note" },
                        additionalProperties = false,
                    },
                },
                remarks = str,
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[]
            {
                "notationLanguage", "event", "site", "date", "dateIso", "round", "white", "black", "result", "moves", "remarks",
            }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }
}
