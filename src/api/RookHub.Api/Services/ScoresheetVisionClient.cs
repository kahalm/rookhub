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

    /// <summary>
    /// Mehrseitiges Formular (0.600.0, bis <see cref="ScoresheetScanService.MaxPages"/> Fotos in Seitenreihenfolge) in
    /// EINEM Aufruf — das Modell sieht, wo Seite 2 weitermacht. Mit mehr als einer Seite verlangt das Schema je Zug die
    /// Seite (<see cref="ScoresheetPrompt.Schema"/>). Vorgabe: eine Seite geht an <see cref="ReadAsync"/>, mehr kann
    /// dieser Leser nicht (<c>multiPage</c>) — dots.ocr und die Test-Attrappen.
    /// </summary>
    Task<ScoresheetVisionResult> ReadPagesAsync(IReadOnlyList<byte[]> pages, string instructions, int maxTokens,
        CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full)
        => pages.Count == 1 ? ReadAsync(pages[0], instructions, maxTokens, ct, mode)
            : Task.FromResult(new ScoresheetVisionResult(null, "multiPage"));
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

    public Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens,
        CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full)
        => ReadPagesAsync(new[] { jpeg }, instructions, maxTokens, ct, mode);

    public async Task<ScoresheetVisionResult> ReadPagesAsync(IReadOnlyList<byte[]> pages, string instructions, int maxTokens,
        CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full)
    {
        if (_client == null) return new(null, "notConfigured");
        try
        {
            var transcribe = mode == ScoresheetReadMode.Transcribe;
            var plan = PlanThinking(Model, mode, Effort);
            var format = new JsonOutputFormat { Schema = ScoresheetPrompt.Schema(multiPage: pages.Count > 1) };
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
                        Content = Content(pages, instructions),
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

    /// <summary>Die Fotos in Seitenreihenfolge, dann der Auftrag. Bei mehreren Seiten steht vor jedem Foto „Page n:"
    /// — daran hängt die Seitennummer, die das Schema je Zug verlangt. Eine Seite bleibt wie bisher: Foto, Auftrag.</summary>
    internal static List<ContentBlockParam> Content(IReadOnlyList<byte[]> pages, string instructions)
    {
        var content = new List<ContentBlockParam>();
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages.Count > 1) content.Add(new TextBlockParam { Text = $"Page {i + 1}:" });
            content.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource { Data = Convert.ToBase64String(pages[i]), MediaType = MediaType.ImageJpeg },
            });
        }
        content.Add(new TextBlockParam { Text = instructions });
        return content;
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
        - Never leave out written moves: read EVERY column block to its end, also when the numbering or the order
          looks inconsistent after a correction. Doubts go into "remarks", the moves themselves stay in the list.

        For each half-move report exactly what is written (in the sheet's own notation), your reading as
        standard English SAN (K Q R B N, O-O, x for captures, + for check, =Q for promotion), up to three
        alternative readings (most likely first) when you are not sure, and a confidence.

        Also give "box": where the entry is on the photo, as [x0, y0, x1, y1] in PIXELS of the photo (the
        request states its size), origin top-left — a tight rectangle around the handwriting of that cell. For a
        corrected entry, the box of the entry you used.
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
          column blocks (for example moves 1-20 and 21-40, or 1-30 and 31-60 side by side), read the first block
          to its end, then the next — ALWAYS all blocks that contain moves.
        - Transcribe EVERY written move, even when the numbering or the order looks wrong (a correction, an arrow,
          a skipped or repeated number). Never drop entries because they seem inconsistent: whether the moves are
          legal and in the right order is checked afterwards. Where an arrow or mark moves an entry to another
          place, put it where the mark points. Put doubts into "remarks", not by leaving moves out.
        - "written" is exactly what stands in the cell, in the sheet's own notation. Piece letters differ by
          language: German K D T L S, Portuguese/Spanish R D T B/A C, French R D T F C, English K Q R B N.
          Pawns have no letter. Castling may be 0-0 or O-O.
        - "san" is the same move with the piece letter translated to English (K Q R B N). Change nothing else:
          do not move squares or add captures to make a move legal.
        - Ignore clock times, minutes, signatures and other marks. For a crossed-out entry use the replacement.
        - Stop at the last written move. Never invent moves.
        - confidence: "high" = clearly legible, "medium" = probably right, "low" = hard to read. For anything
          that is not clearly legible give up to three alternative readings, most likely first.
        - box: where the entry is on the photo, as [x0, y0, x1, y1] in PIXELS of the photo (the request states
          its size), origin top-left — a tight rectangle around the handwriting of that cell.
        """;

    /// <summary>Auftrag für die erste Lesung.</summary>
    /// <param name="languageHint">Code aus <see cref="ScoresheetNotation.Languages"/> oder <c>auto</c>.</param>
    /// <param name="photoSize">Maße des Bildes, das das Modell bekommt — die Kästen kommen in DIESEN Pixeln zurück.
    /// Um „0..1000" gebeten, lieferte Opus 5.5 am 2026-09-27 trotzdem Pixel des 1500×2000-Bildes (y bis 1790); mit
    /// genannter Größe und Pixeln ist die Einheit eindeutig, umgerechnet wird am Server
    /// (<see cref="ScoresheetTranscription.NormalizedBoxes"/>).</param>
    public static string FirstRead(string languageHint, (int Width, int Height)? photoSize = null)
        => FirstRead(languageHint, photoSize is { } s ? new[] { s } : Array.Empty<(int, int)>(), pageCount: 1);

    /// <summary>Wie oben, für ein Formular über mehrere Fotos (<paramref name="pageCount"/> &gt; 1): eine Partie, Seite 2
    /// macht weiter, wo Seite 1 aufhört, und jeder Zug nennt seine Seite. Mit einer Seite wörtlich der bisherige Auftrag.</summary>
    public static string FirstRead(string languageHint, IReadOnlyList<(int Width, int Height)> pageSizes, int pageCount)
    {
        var lang = ScoresheetNotation.Find(languageHint);
        var hint = lang == null
            ? "The notation language is unknown; determine it from the sheet."
            : $"The user says the sheet is written in {lang.Name} notation (pieces: K={lang.King} Q={lang.Queen} R={lang.Rook} B={lang.Bishop} N={lang.Knight}).";
        if (pageCount <= 1)
        {
            var size = pageSizes.Count > 0
                ? $" The photo is {pageSizes[0].Width} × {pageSizes[0].Height} pixels (width × height); give every \"box\" in pixels of this photo."
                : "";
            return hint + " Transcribe the scoresheet in the photo." + size;
        }
        var sizes = pageSizes.Count == pageCount
            ? " " + string.Join(", ", pageSizes.Select((s, i) => $"page {i + 1} is {s.Width} × {s.Height} pixels"))
              + " (width × height)."
            : "";
        return hint + $" The scoresheet of ONE game continues over {pageCount} photos, given in page order (\"Page 1:\" to"
            + $" \"Page {pageCount}:\"). Transcribe them as one move list: each page continues where the previous one ends —"
            + " also when its printed numbering starts again; a move pair may be split across two pages. Read the players,"
            + " event and result from whichever page has them."
            + sizes + " Give every move its \"page\" (the number of the photo it is written on) and its \"box\" in pixels"
            + " of that photo.";
    }

    /// <summary>Nachfrage, wenn die Züge ab einer Stelle nicht mehr legal aufgehen.</summary>
    public static string Repair(string languageHint, string previousJson, IReadOnlyList<string> acceptedSans,
        int stuckIndex, ScannedPly stuckPly, string fen, IReadOnlyList<string> legalMoves,
        (int Width, int Height)? photoSize = null)
        => Repair(languageHint, previousJson, acceptedSans, stuckIndex, stuckPly, fen, legalMoves,
            photoSize is { } s ? new[] { s } : Array.Empty<(int, int)>(), pageCount: 1);

    /// <summary>Nachfrage für ein Formular über <paramref name="pageCount"/> Fotos (siehe <see cref="FirstRead(string, IReadOnlyList{ValueTuple{int, int}}, int)"/>).</summary>
    public static string Repair(string languageHint, string previousJson, IReadOnlyList<string> acceptedSans,
        int stuckIndex, ScannedPly stuckPly, string fen, IReadOnlyList<string> legalMoves,
        IReadOnlyList<(int Width, int Height)> pageSizes, int pageCount)
    {
        var moveNo = stuckIndex / 2 + 1;
        var side = stuckIndex % 2 == 0 ? "White" : "Black";
        var accepted = acceptedSans.Count == 0
            ? "(none)"
            : Services.PgnWriter.MoveText(acceptedSans, result: null);
        return FirstRead(languageHint, pageSizes, pageCount) + $"""


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
    /// <param name="multiPage">Formular über mehrere Fotos: jeder Zug trägt zusätzlich <c>page</c> (1 = erstes Foto).
    /// Eine Seite bleibt ohne das Feld — die Referenz-Lesungen sollen nicht an einem Schema-Wechsel hängen.</param>
    public static Dictionary<string, JsonElement> Schema(bool multiPage = false)
    {
        var str = new { type = "string" };
        var moveProperties = JsonSerializer.SerializeToNode(new
        {
            moveNumber = new { type = "integer" },
            color = new { type = "string", @enum = new[] { "w", "b" } },
            written = str,
            san = str,
            alternatives = new { type = "array", items = str },
            confidence = new { type = "string", @enum = new[] { "high", "medium", "low" } },
            note = str,
            box = new
            {
                type = "array",
                items = new { type = "integer" },
                description = "[x0, y0, x1, y1] around the handwriting of this entry, in pixels of the photo, origin top-left",
            },
        })!.AsObject();
        var moveRequired = new List<string> { "moveNumber", "color", "written", "san", "alternatives", "confidence", "note", "box" };
        if (multiPage)
        {
            moveProperties["page"] = JsonSerializer.SerializeToNode(new
            {
                type = "integer",
                description = "number of the photo (page) this entry is written on, 1 = first photo",
            });
            moveRequired.Add("page");
        }
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
                        properties = moveProperties,
                        required = moveRequired,
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
