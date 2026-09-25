using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Formular-Lesung über <b>dots.ocr</b> (rednote-hilab, 1,7 B Parameter, Layout + Texterkennung in einem Modell,
/// ausgeliefert über vLLM mit OpenAI-kompatibler Schnittstelle). Wie <see cref="OpenAiScoresheetVisionClient"/> bisher
/// nur im Testwerkzeug <c>tools/ScoresheetBench</c> benutzt.
/// </summary>
/// <remarks>
/// dots.ocr ist ein DOKUMENT-Leser, kein Schach-Leser: es bekommt immer denselben Layout-Auftrag (wörtlich aus
/// <c>dots_ocr/utils/prompts.py</c>, <c>prompt_layout_all_en</c> — auf genau diesen Wortlaut ist das Modell trainiert)
/// und liefert eine Liste von Layout-Elementen, Tabellen als HTML. Der Zug-Teil daraus ist
/// <see cref="DotsOcrLayout.ToTranscriptionJson"/>. Der Auftrag der Lese-Schleife (erste Lesung, Nachfrage) wird
/// deshalb IGNORIERT — eine Nachfrage ergäbe dieselbe Lesung noch einmal; der Aufrufer liest mit einem Durchgang.
/// </remarks>
public sealed class DotsOcrScoresheetVisionClient : IScoresheetVisionClient
{
    /// <summary><c>prompt_layout_all_en</c> aus dots.ocr, Zeichen für Zeichen (samt Einrückung und Zeilenende).</summary>
    public const string LayoutPrompt =
        """
        Please output the layout information from the PDF image, including each layout element's bbox, its category, and the corresponding text content within the bbox.

        1. Bbox format: [x1, y1, x2, y2]

        2. Layout Categories: The possible categories are ['Caption', 'Footnote', 'Formula', 'List-item', 'Page-footer', 'Page-header', 'Picture', 'Section-header', 'Table', 'Text', 'Title'].

        3. Text Extraction & Formatting Rules:
            - Picture: For the 'Picture' category, the text field should be omitted.
            - Formula: Format its text as LaTeX.
            - Table: Format its text as HTML.
            - All Others (Text, Title, etc.): Format their text as Markdown.

        4. Constraints:
            - The output text must be the original text from the image, with no translation.
            - All layout elements must be sorted according to human reading order.

        5. Final Output: The entire output must be a single JSON object.

        """;

    /// <summary>Ohne diese Marke setzt vLLM v1 einen Zeilenumbruch vor den Auftrag (Hinweis im dots.ocr-Quelltext).</summary>
    public const string ImageMarker = "<|img|><|imgpad|><|endofimg|>";

    public sealed record Settings(string BaseUrl, string Model, string? ApiKey = null, int MaxTokensCap = 16384);

    private readonly HttpClient _http;
    private readonly Settings _settings;
    private readonly ILogger _logger;

    public DotsOcrScoresheetVisionClient(HttpClient http, Settings settings, ILogger logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.BaseUrl) && !string.IsNullOrWhiteSpace(_settings.Model);

    public string Model => _settings.Model;

    /// <summary>Die letzte Rohantwort (Layout-JSON) — fürs Testwerkzeug, das sie zum Nachsehen ablegt.</summary>
    public string? LastRaw { get; private set; }

    public async Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return new(null, "notConfigured");
        var body = new JsonObject
        {
            ["model"] = _settings.Model,
            ["max_tokens"] = Math.Clamp(maxTokens, 256, Math.Max(256, _settings.MaxTokensCap)),
            // Die Werte aus dots_ocr/model/inference.py.
            ["temperature"] = 0.1,
            ["top_p"] = 0.9,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        OpenAiChat.ImagePart(jpeg),
                        new JsonObject { ["type"] = "text", ["text"] = ImageMarker + LayoutPrompt },
                    },
                },
            },
        };
        var reply = await OpenAiChat.SendAsync(_http, _settings.BaseUrl, _settings.ApiKey, body, ct);
        LastRaw = reply.Content;
        if (reply.Error != null)
        {
            _logger.LogWarning("Formular-Lesung via dots.ocr fehlgeschlagen: {Error}", reply.Error);
            return new(null, "failed", reply.InputTokens, reply.OutputTokens);
        }
        // Auch eine abgeschnittene Antwort kann die Zugtabelle schon vollständig enthalten (sie steht meist vor
        // Unterschriften und Fußzeilen) — erst wenn darin keine Züge stehen, heißt es „abgeschnitten".
        var json = DotsOcrLayout.ToTranscriptionJson(reply.Content);
        if (json != null) return new(json, null, reply.InputTokens, reply.OutputTokens);
        return new(null, reply.FinishReason == "length" ? "truncated" : "noMoves", reply.InputTokens, reply.OutputTokens);
    }
}

/// <summary>
/// Aus der Layout-Antwort von dots.ocr die Züge eines Partieformulars: rein, ohne Netz, testbar.
/// </summary>
/// <remarks>
/// Ein Formular hat eine oder mehrere Zugtabellen, oft zwei Blöcke nebeneinander (1–30 | 31–60), manchmal mit
/// Zeit-Spalten. Die Regel dafür: eine NUMMERN-Spalte ist eine Spalte, deren Zahlen Zeile für Zeile um eins steigen
/// (eine Zeit-Spalte besteht auch aus Zahlen, steigt aber nicht gleichmäßig). Jede Nummern-Spalte beginnt einen
/// Block; darin sind die ersten zwei Spalten, die keine Zeit-Spalten sind, Weiß und Schwarz. Fehlt eine Nummer
/// (nicht erkannt), folgt sie aus dem Versatz der Spalte. Ohne jede Nummern-Spalte werden die Spalten paarweise
/// gelesen und die Zeilen durchgezählt; ohne jede Tabelle Zeilen der Form „12. Sf3 Sc6".
/// </remarks>
public static class DotsOcrLayout
{
    /// <summary>Ein Layout-Element: Kategorie (<c>Table</c>, <c>Text</c> …) und Text (Tabellen als HTML).</summary>
    public sealed record Element(string Category, string Text);

    private static readonly Regex RowRx = new(@"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CellRx = new(@"<t[dh]\b([^>]*)>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ColspanRx = new(@"colspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRx = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex StrikeRx = new(@"~~[^~]*~~", RegexOptions.Compiled);
    private static readonly Regex IntRx = new(@"^(\d{1,3})\.?$", RegexOptions.Compiled);
    private static readonly Regex TimeRx = new(@"^\d{1,3}(?:[:.'’h]\d{0,2})?['’]?(?:min)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MoveLikeRx = new(@"[a-hA-H][1-8]|^[0O]\s*-\s*[0O]", RegexOptions.Compiled);
    private static readonly Regex ResultRx = new(@"(?<![\d/])(1\s*-\s*0|0\s*-\s*1|1/2\s*-\s*1/2|½\s*-\s*½)(?![\d/])", RegexOptions.Compiled);
    private static readonly Regex TextLineRx = new(@"^\s*(\d{1,3})\s*[.)]?\s*([^\s.]\S*)(?:\s+(\S+))?", RegexOptions.Compiled);

    private const string WhiteLabels = "White|Weiß|Weiss|Brancas|Branco|Blancas|Blanco|Blancs|Blanc|Bianco|Wit|Białe|Bílý|Világos";
    private const string BlackLabels = "Black|Schwarz|Pretas|Preto|Negras|Negro|Noirs|Noir|Nero|Zwart|Czarne|Černý|Sötét";
    private static readonly Regex WhiteLineRx = LabelLine(WhiteLabels);
    private static readonly Regex BlackLineRx = LabelLine(BlackLabels);
    private static readonly Regex AnyLabelRx = new($@"^\W*(?:{WhiteLabels}|{BlackLabels}|N[º°o]\.?|#|Time|Zeit|Tempo|Tiempo|Temps|Czas|Idő)\W*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex LabelLine(string labels)
        => new($@"^\W*(?:{labels})\b\W*[:：]\s*(.+)$|^\W*(?:{labels})\W*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Die Transkription als JSON im Format von <see cref="ScoresheetTranscription"/>, oder <c>null</c>,
    /// wenn die Antwort keine Züge hergibt.</summary>
    public static string? ToTranscriptionJson(string? raw)
    {
        var elements = ParseElements(raw);
        if (elements.Count == 0) return null;

        var moves = new SortedDictionary<int, (string White, string Black)>();
        string? result = null;
        var basis = 0;
        foreach (var table in elements.Where(e => e.Category.Equals("Table", StringComparison.OrdinalIgnoreCase)))
        {
            var rows = ParseTable(table.Text);
            foreach (var (n, w, b) in MovesOfTable(rows, basis, ref result)) Put(moves, n, w, b);
            if (moves.Count > 0) basis = Math.Max(basis, moves.Keys.Max());
        }
        if (moves.Count == 0)
            foreach (var e in elements.Where(e => !e.Category.Equals("Table", StringComparison.OrdinalIgnoreCase)))
                foreach (var line in e.Text.Split('\n'))
                {
                    var m = TextLineRx.Match(line);
                    if (!m.Success || !int.TryParse(m.Groups[1].Value, out var n) || n is < 1 or > 300) continue;
                    var w = CleanMove(m.Groups[2].Value, ref result);
                    var b = m.Groups[3].Success ? CleanMove(m.Groups[3].Value, ref result) : "";
                    if (MoveLike(w)) Put(moves, n, w, MoveLike(b) ? b : "");
                }
        if (moves.Count == 0) return null;

        var entries = new List<ScoresheetTranscription.Entry>();
        foreach (var (n, (w, b)) in moves)
        {
            if (w.Length > 0) entries.Add(Entry(n, "w", w));
            if (b.Length > 0) entries.Add(Entry(n, "b", b));
        }
        if (entries.Count == 0) return null;

        foreach (var e in elements) result ??= ResultIn(e.Text);
        var transcription = new ScoresheetTranscription
        {
            NotationLanguage = "unknown",
            White = Player(elements, WhiteLineRx) ?? "",
            Black = Player(elements, BlackLineRx) ?? "",
            Result = result ?? "*",
            Moves = entries,
            Remarks = "dots.ocr",
        };
        return JsonSerializer.Serialize(transcription, ScoresheetScanService.Json);
    }

    private static ScoresheetTranscription.Entry Entry(int n, string color, string written) => new()
    {
        MoveNumber = n,
        Color = color,
        Written = written,
        San = null, // dots.ocr deutet nicht — der Auflöser liest den Eintrag in der Sprache des Formulars
        Alternatives = new(),
        Confidence = "medium", // kein Maß der Sicherheit in der Antwort; „medium" markiert allein nichts
        Note = "",
    };

    private static void Put(SortedDictionary<int, (string White, string Black)> moves, int n, string w, string b)
    {
        if (n < 1 || n > 300 || (w.Length == 0 && b.Length == 0)) return;
        if (moves.TryGetValue(n, out var old))
        {
            // Dieselbe Nummer zweimal (zwei Tabellen überlappen): die Zeile mit mehr Zügen gewinnt.
            var oldScore = (MoveLike(old.White) ? 1 : 0) + (MoveLike(old.Black) ? 1 : 0);
            var newScore = (MoveLike(w) ? 1 : 0) + (MoveLike(b) ? 1 : 0);
            if (newScore <= oldScore) return;
        }
        moves[n] = (w, b);
    }

    // ── Layout-JSON ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Die Layout-Elemente — aus gültigem JSON (Liste oder Objekt, beliebig tief), und wenn die Antwort
    /// abgeschnitten ist, die vollständigen Elemente bis dahin.</summary>
    public static List<Element> ParseElements(string? raw)
    {
        var list = new List<Element>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        var text = raw.Trim();
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var nl = text.IndexOf('\n', fence);
            var close = nl < 0 ? -1 : text.IndexOf("```", nl, StringComparison.Ordinal);
            if (nl >= 0) text = close > nl ? text[(nl + 1)..close] : text[(nl + 1)..];
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            Collect(doc.RootElement, list);
            return list;
        }
        catch (JsonException)
        {
            // Abgeschnitten: jedes vollständige Objekt der obersten Ebene einzeln.
            foreach (var chunk in TopLevelObjects(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(chunk);
                    Collect(doc.RootElement, list);
                }
                catch (JsonException) { /* unvollständig */ }
            }
            return list;
        }
    }

    private static void Collect(JsonElement node, List<Element> into)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray()) Collect(item, into);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String)
        {
            var text = node.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            into.Add(new Element(cat.GetString() ?? "", text));
            return;
        }
        foreach (var prop in node.EnumerateObject()) Collect(prop.Value, into);
    }

    private static IEnumerable<string> TopLevelObjects(string text)
    {
        int depth = 0, start = -1;
        bool inString = false, escape = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
                if (depth == 0 && start >= 0) yield return text[start..(i + 1)];
            }
        }
    }

    // ── Tabellen ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>HTML-Tabelle → Zeilen mit Zelltexten; <c>colspan</c> hält die Spalten mit leeren Zellen ausgerichtet.</summary>
    public static List<List<string>> ParseTable(string html)
    {
        var rows = new List<List<string>>();
        foreach (Match tr in RowRx.Matches(html ?? ""))
        {
            var cells = new List<string>();
            foreach (Match td in CellRx.Matches(tr.Groups[1].Value))
            {
                cells.Add(CleanCell(td.Groups[2].Value));
                var span = ColspanRx.Match(td.Groups[1].Value);
                if (span.Success && int.TryParse(span.Groups[1].Value, out var n))
                    for (var k = 1; k < Math.Min(n, 20); k++) cells.Add("");
            }
            if (cells.Count > 0) rows.Add(cells);
        }
        return rows;
    }

    private static string CleanCell(string html)
    {
        var text = Regex.Replace(html, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(TagRx.Replace(text, " "));
        text = text.Replace("**", "").Replace(' ', ' ');
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static List<(int N, string White, string Black)> MovesOfTable(List<List<string>> rows, int basis, ref string? result)
    {
        var found = new List<(int, string, string)>();
        if (rows.Count == 0) return found;
        var width = rows.Max(r => r.Count);
        string At(int r, int c) => c < rows[r].Count ? rows[r][c] : "";

        var numberCols = new List<(int Col, int Offset)>();
        var timeCols = new HashSet<int>();
        for (var c = 0; c < width; c++)
        {
            var nonEmpty = Enumerable.Range(0, rows.Count).Count(r => At(r, c).Length > 0);
            var ints = Enumerable.Range(0, rows.Count)
                .Select(r => (Row: r, Value: IntOf(At(r, c))))
                .Where(x => x.Value > 0).ToList();
            if (nonEmpty > 0 && Enumerable.Range(0, rows.Count).Count(r => TimeRx.IsMatch(At(r, c))) * 10 >= nonEmpty * 6)
                timeCols.Add(c);
            if (ints.Count < 2 || ints.Count * 10 < nonEmpty * 6) continue;
            var offset = ints.GroupBy(x => x.Value - x.Row).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
            if (offset.Count() < 2 || offset.Count() * 2 < ints.Count) continue;
            numberCols.Add((c, offset.Key));
            timeCols.Remove(c);
        }

        if (numberCols.Count > 0)
        {
            for (var k = 0; k < numberCols.Count; k++)
            {
                var (col, offset) = numberCols[k];
                var end = k + 1 < numberCols.Count ? numberCols[k + 1].Col : width;
                var moveCols = Enumerable.Range(col + 1, Math.Max(0, end - col - 1)).Where(c => !timeCols.Contains(c)).Take(2).ToList();
                if (moveCols.Count == 0) continue;
                for (var r = 0; r < rows.Count; r++)
                {
                    var (w, b) = Pair(At(r, moveCols[0]), moveCols.Count > 1 ? At(r, moveCols[1]) : null, ref result);
                    var written = IntOf(At(r, col));
                    // Ohne erkannte Nummer zählt die Zeile nur, wenn darin ein Zug steht — sonst wäre die Kopfzeile
                    // „Nº | Weiß | Schwarz" des zweiten Blocks ein Zug 30.
                    if (written <= 0 && !MoveLike(w) && !MoveLike(b)) continue;
                    found.Add((written > 0 ? written : r + offset, w, b));
                }
            }
            return found;
        }

        // Keine Nummern: Spalten paarweise, Zeilen mit einem Zug durchgezählt, Block für Block.
        var cols = Enumerable.Range(0, width).Where(c => !timeCols.Contains(c)
            && Enumerable.Range(0, rows.Count).Any(r => At(r, c).Length > 0)).ToList();
        var dataRows = Enumerable.Range(0, rows.Count).Where(r => cols.Any(c => MoveLike(At(r, c)))).ToList();
        for (var k = 0; k * 2 < cols.Count; k++)
            for (var j = 0; j < dataRows.Count; j++)
            {
                var r = dataRows[j];
                var (w, b) = Pair(At(r, cols[2 * k]), 2 * k + 1 < cols.Count ? At(r, cols[2 * k + 1]) : null, ref result);
                if (MoveLike(w) || MoveLike(b)) found.Add((basis + k * dataRows.Count + j + 1, w, b));
            }
        return found;
    }

    /// <summary>Weiß und Schwarz aus zwei Zellen — oder aus EINER, wenn der Block nur eine Zugspalte hat („e4 e5").</summary>
    private static (string White, string Black) Pair(string white, string? black, ref string? result)
    {
        if (black != null) return (CleanMove(white, ref result), CleanMove(black, ref result));
        var parts = white.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("", ""),
            1 => (CleanMove(parts[0], ref result), ""),
            _ => (CleanMove(parts[0], ref result), CleanMove(parts[1], ref result)),
        };
    }

    /// <summary>Ein Zell-Eintrag als Zug: Durchgestrichenes weg (wenn daneben etwas steht), Zeiten weg, ein Ergebnis
    /// („1-0") wird zum Ergebnis der Partie statt zu einem Zug.</summary>
    private static string CleanMove(string cell, ref string? result)
    {
        var text = cell.Trim();
        var unstruck = StrikeRx.Replace(text, " ").Trim();
        if (unstruck.Length > 0) text = unstruck;
        text = text.Replace("~~", "");
        var r = ResultIn(text);
        if (r != null)
        {
            result ??= r;
            text = ResultRx.Replace(text, " ").Trim();
        }
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1)
        {
            var kept = tokens.Where(t => !TimeRx.IsMatch(t)).ToList();
            if (kept.Count > 0) tokens = kept.ToArray();
        }
        else if (tokens.Length == 1 && TimeRx.IsMatch(tokens[0]))
            return "";
        return string.Join(' ', tokens);
    }

    private static int IntOf(string cell)
    {
        var m = IntRx.Match(cell.Trim());
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is >= 1 and <= 300 ? n : 0;
    }

    private static bool MoveLike(string? text) => !string.IsNullOrWhiteSpace(text) && MoveLikeRx.IsMatch(text);

    private static string? ResultIn(string text)
    {
        var m = ResultRx.Match(text ?? "");
        if (!m.Success) return null;
        var v = Regex.Replace(m.Value, @"\s+", "");
        return v == "½-½" ? "1/2-1/2" : v;
    }

    // ── Kopfdaten ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Spielername: „Weiß: Müller" in einem Textelement oder einer Zelle, oder die Zelle hinter einer
    /// Zelle, in der nur „Weiß" steht (dann darf sie weder selbst eine Überschrift noch ein Zug sein).</summary>
    private static string? Player(List<Element> elements, Regex label)
    {
        foreach (var e in elements)
        {
            if (e.Category.Equals("Table", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var row in ParseTable(e.Text))
                {
                    // „Nº | Weiß | Schwarz" ist die Kopfzeile der Zugtabelle, keine Namenszeile.
                    if (row.Any(c => BareLabel(WhiteLineRx, c)) && row.Any(c => BareLabel(BlackLineRx, c))) continue;
                    for (var i = 0; i < row.Count; i++)
                    {
                        var m = label.Match(row[i]);
                        if (!m.Success) continue;
                        if (m.Groups[1].Success && Plausible(m.Groups[1].Value)) return m.Groups[1].Value.Trim();
                        var next = row.Skip(i + 1).FirstOrDefault(c => c.Length > 0);
                        if (next != null && Plausible(next)) return next;
                    }
                }
                continue;
            }
            foreach (var line in e.Text.Split('\n'))
            {
                var m = label.Match(line.Replace("**", "").Trim());
                if (m.Success && m.Groups[1].Success && Plausible(m.Groups[1].Value)) return m.Groups[1].Value.Trim();
            }
        }
        return null;
    }

    private static bool BareLabel(Regex label, string cell)
    {
        var m = label.Match(cell);
        return m.Success && !m.Groups[1].Success;
    }

    private static bool Plausible(string name)
    {
        var n = name.Trim();
        return n.Length is > 1 and <= 80 && !AnyLabelRx.IsMatch(n) && !MoveLike(n) && IntOf(n) == 0 && n.Any(char.IsLetter);
    }
}
