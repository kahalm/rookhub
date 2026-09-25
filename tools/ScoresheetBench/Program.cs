using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// ScoresheetBench — Testinterface für „Partieformular einlesen".
//
//   dotnet run --project tools/ScoresheetBench -- <testset-ordner> [Optionen]
//
//   --out <ordner>        Ausgabe (results.json, report.md, NN.answer.json); Vorgabe ./scoresheet-bench-out
//   --only 01,05          nur diese Belege
//   --resolver-only       KEIN Modell-Aufruf: die Abschrift (NN.formular.txt) ist die Eingabe → misst nur den
//                         Auflöser (kostenlos). Portugiesische Belege werden dafür in portugiesische Buchstaben
//                         zurückübersetzt, damit auch die Notations-Zuordnung mitläuft.
//   --max-usd 8           harter Deckel für den GANZEN Lauf; ein Aufruf startet nur, wenn sein ungünstigster
//                         Fall noch passt (dieselbe Regel wie in RookHub)
//   --model <id>          Vorgabe claude-opus-5; bei openai/dots das erste Modell, das der Server meldet
//   --replay <ordner>     KEIN Modell-Aufruf: die gespeicherten Antworten (NN.answer.json eines früheren Laufs)
//                         werden neu aufgelöst — misst Auflöser-Änderungen an echten Modell-Lesungen, kostenlos
//
// Andere Leser (eigene Hardware, OpenAI-kompatible Schnittstelle, z. B. vLLM auf dem DGX Spark):
//   --provider claude|openai|dots   Vorgabe claude. openai = ein Vision-Modell wie Qwen3-VL (liest UND deutet),
//                         dots = dots.ocr (reiner Dokument-Leser: Tabelle → Einträge, ein Durchgang, keine Nachfrage)
//   --endpoint <url>      Basis bis einschließlich /v1, z. B. https://spark.example/v1 (Pflicht für openai/dots)
//   --key-env <NAME>      Name der Umgebungsvariable mit dem Schlüssel (z. B. SPARK_API_KEY) — der Schlüssel
//                         selbst steht nie auf der Kommandozeile und wird nie ausgegeben
//   --max-tokens 16384    Antwort-Deckel für openai/dots (hängt am --max-model-len des Servers, nicht an Geld)
//   --prompt transcribe|full   openai: nur abschreiben (Vorgabe) oder der volle Claude-Auftrag (Partie mitspielen)
//   --no-schema           openai: ohne response_format (für Server ohne Grammatik-Steuerung)
//   --timeout 900         Sekunden je Aufruf
//   --edge 2000           längste Bildkante in Pixeln
//   --list-models         nur die Modelle am --endpoint auflisten (Verbindungstest; braucht keinen Testordner)
//
// Der Testordner enthält je Beleg NN.png|jpg (Formular), NN.pgn (Soll = gespielte Partie), NN.formular.txt (was
// auf DIESEM Formular steht) und belege.json. Der Claude-Schlüssel kommt aus ANTHROPIC_API_KEY.
// ─────────────────────────────────────────────────────────────────────────────────────────────────

var argv = args.ToList();
string Opt(string name, string fallback)
{
    var i = argv.IndexOf(name);
    return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : fallback;
}
var provider = Opt("--provider", "claude").ToLowerInvariant();
var endpoint = Opt("--endpoint", "");
var keyEnv = Opt("--key-env", "");
var localMaxTokens = int.Parse(Opt("--max-tokens", "16384"), CultureInfo.InvariantCulture);
var promptKind = Opt("--prompt", "transcribe").ToLowerInvariant();
var useSchema = !argv.Contains("--no-schema");
var timeoutSec = int.Parse(Opt("--timeout", "900"), CultureInfo.InvariantCulture);
var edge = int.Parse(Opt("--edge", ScoresheetScanService.ModelEdge.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
if (provider is not ("claude" or "openai" or "dots"))
{
    Console.Error.WriteLine($"Unbekannter --provider „{provider}“ (claude|openai|dots).");
    return 2;
}
string? localKey = null;
if (keyEnv.Length > 0)
{
    localKey = Environment.GetEnvironmentVariable(keyEnv);
    if (string.IsNullOrWhiteSpace(localKey))
    {
        Console.Error.WriteLine($"Umgebungsvariable {keyEnv} ist leer.");
        return 3;
    }
}
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
if (argv.Contains("--list-models"))
{
    if (endpoint.Length == 0) { Console.Error.WriteLine("--list-models braucht --endpoint."); return 2; }
    var ids = await ModelIds(http, endpoint, localKey);
    if (ids == null) return 4;
    foreach (var id in ids) Console.WriteLine(id);
    return 0;
}
if (argv.Count == 0 || argv[0].StartsWith("--"))
{
    Console.Error.WriteLine("Aufruf: ScoresheetBench <testset-ordner> [--out DIR] [--only 01,02] [--resolver-only] [--max-usd N] [--model ID]\n" +
                            "        [--provider claude|openai|dots --endpoint URL --key-env NAME] — Einzelheiten oben in Program.cs");
    return 2;
}
var dir = argv[0];
var outDir = Opt("--out", Path.Combine(Directory.GetCurrentDirectory(), "scoresheet-bench-out"));
var only = Opt("--only", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
var resolverOnly = argv.Contains("--resolver-only");
var maxUsd = decimal.Parse(Opt("--max-usd", "8"), CultureInfo.InvariantCulture);
var model = Opt("--model", provider == "claude" ? "claude-opus-5" : "");
var replayDir = Opt("--replay", "");
Directory.CreateDirectory(outDir);

var belege = JsonSerializer.Deserialize<List<BelegInfo>>(File.ReadAllText(Path.Combine(dir, "belege.json")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
if (only.Count > 0) belege = belege.Where(b => only.Contains(b.Beleg)).ToList();

IScoresheetVisionClient? vision = null;
var budget = new ScoresheetBudget(null);
var local = provider != "claude";
Func<string?>? lastRaw = null;
if (!resolverOnly && replayDir.Length == 0 && local)
{
    if (endpoint.Length == 0)
    {
        Console.Error.WriteLine($"--provider {provider} braucht --endpoint.");
        return 2;
    }
    if (model.Length == 0)
    {
        var ids = await ModelIds(http, endpoint, localKey);
        if (ids == null || ids.Count == 0) { Console.Error.WriteLine("Kein Modell am Endpunkt."); return 4; }
        model = ids[0];
        Console.WriteLine($"Modell: {model}" + (ids.Count > 1 ? $" (weitere: {string.Join(", ", ids.Skip(1))})" : ""));
    }
    if (provider == "dots")
    {
        var dots = new DotsOcrScoresheetVisionClient(http,
            new DotsOcrScoresheetVisionClient.Settings(endpoint, model, localKey, localMaxTokens), NullLogger.Instance);
        vision = dots;
        lastRaw = () => dots.LastRaw;
    }
    else
    {
        var system = promptKind == "full" ? ScoresheetPrompt.System : ScoresheetPrompt.TranscribeSystem;
        var open = new OpenAiScoresheetVisionClient(http,
            new OpenAiScoresheetVisionClient.Settings(endpoint, model, localKey, system, useSchema, MaxTokensCap: localMaxTokens),
            NullLogger.Instance);
        vision = open;
        lastRaw = () => open.LastRaw;
    }
}
else if (!resolverOnly && replayDir.Length == 0)
{
    var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine("ANTHROPIC_API_KEY fehlt — ohne Schlüssel geht nur --resolver-only.");
        return 3;
    }
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Anthropic:ApiKey"] = key,
        ["Anthropic:ScoresheetModel"] = model,
    }).Build();
    vision = new ClaudeScoresheetVisionClient(config, NullLogger<ClaudeScoresheetVisionClient>.Instance);
}

long spentMicro = 0;
var maxMicro = (long)(maxUsd * 1_000_000m);
var results = new List<Result>();

foreach (var b in belege)
{
    var r = new Result { Beleg = b.Beleg, Notation = b.Notation, Quelle = b.Quelle };
    results.Add(r);
    var truthMoves = Truth(File.ReadAllText(Path.Combine(dir, b.Beleg + ".pgn")), out var truthNote);
    var truth = truthMoves.Select(t => t.San).ToList();
    var sheet = SheetTokens(File.ReadAllText(Path.Combine(dir, b.Beleg + ".formular.txt")));
    r.SollHalbzuege = truth.Count;
    r.SollHinweis = truthNote;
    r.FormularEintraege = sheet.Count;
    // Sprache: ausdrücklich aus belege.json („sprache": Code oder auto), sonst aus der Notation geraten.
    var lang = b.Sprache ?? (b.Notation.StartsWith("port", StringComparison.OrdinalIgnoreCase) ? "pt"
        : b.Notation.StartsWith("deut", StringComparison.OrdinalIgnoreCase) ? "de" : "en");
    var sw = Stopwatch.StartNew();

    ScoresheetResolution? resolution;
    List<ScannedPly> scanned;
    if (resolverOnly)
    {
        scanned = sheet.Select(t => new ScannedPly(lang == "pt" ? ToPortuguese(t) : t, null)).ToList();
        resolution = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(lang)));
    }
    else if (replayDir.Length > 0)
    {
        var answerPath = Path.Combine(replayDir, b.Beleg + ".answer.json");
        if (!File.Exists(answerPath)) { r.Fehler = "keine gespeicherte Antwort"; continue; }
        var json0 = File.ReadAllText(answerPath);
        var t0 = ScoresheetTranscription.Parse(json0);
        if (t0 == null) { r.Fehler = "Antwort nicht lesbar"; continue; }
        scanned = t0.Scanned();
        r.ModellSprache = t0.NotationLanguage;
        r.ModellEintraege = scanned.Count;
        var eff = ScoresheetReader.EffectiveLanguage(lang, t0.NotationLanguage);
        resolution = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(eff)));
        File.WriteAllText(Path.Combine(outDir, b.Beleg + ".answer.json"), json0);
        Reading(r, scanned, sheet, truthMoves, lang);
    }
    else
    {
        var jpeg = ScoresheetImage.Prepare(File.ReadAllBytes(Path.Combine(dir, b.Bild)), edge);
        if (jpeg == null) { r.Fehler = "Bild nicht lesbar"; continue; }
        var reader = new ScoresheetReader(vision!);
        var outcome = await reader.ReadAsync(jpeg, lang, CancellationToken.None,
            beforeCall: _ =>
            {
                // Eigene Hardware kostet kein Geld je Aufruf — der Deckel ist das Kontextfenster des Servers.
                if (local) return Task.FromResult(new CallAllowance(localMaxTokens, null));
                // Derselbe Gedanke wie in RookHub: der Antwort-Deckel kommt aus dem, was vom Lauf-Deckel übrig ist.
                var left = maxMicro - spentMicro - budget.WorstCaseMicroUsd(0);
                var tokens = (int)Math.Min(ScoresheetBudget.MaxOutputTokens, Math.Max(0, left / budget.OutputUsdPerMTok));
                return Task.FromResult(tokens < ScoresheetBudget.MinOutputTokens
                    ? new CallAllowance(0, "benchBudget") : new CallAllowance(tokens, null));
            },
            afterCall: (input, output, _) =>
            {
                r.InputTokens += input;
                r.OutputTokens += output;
                if (local) return Task.CompletedTask;
                var cost = budget.CostMicroUsd(input, output);
                r.KostenUsd += cost / 1_000_000m;
                spentMicro += cost;
                return Task.CompletedTask;
            },
            maxRounds: provider == "dots" ? 1 : ScoresheetReader.MaxRounds);
        if (lastRaw?.Invoke() is { } raw)
            File.WriteAllText(Path.Combine(outDir, b.Beleg + (provider == "dots" ? ".dots.txt" : ".raw.txt")), raw);
        r.Runden = outcome.Rounds;
        r.Fehler = outcome.Error;
        if (outcome.Json != null) File.WriteAllText(Path.Combine(outDir, b.Beleg + ".answer.json"), outcome.Json);
        resolution = outcome.Resolution;
        scanned = outcome.Transcription?.Scanned() ?? new();
        r.ModellSprache = outcome.Transcription?.NotationLanguage;
        r.ModellEintraege = scanned.Count;
        if (outcome.Transcription != null) Reading(r, scanned, sheet, truthMoves, lang);
    }
    sw.Stop();
    r.Sekunden = Math.Round(sw.Elapsed.TotalSeconds, 1);
    if (resolution == null) { Console.WriteLine($"{b.Beleg}: {r.Fehler}"); continue; }

    // AUFLÖSEN: das Endergebnis gegen die gespielte Partie.
    var got = resolution.Plies.Select(p => p.San).ToList();
    WritePlyTable(Path.Combine(outDir, b.Beleg + ".plies.tsv"), resolution, scanned, sheet, truthMoves,
        new ScoresheetResolver.Options(ScoresheetNotation.Find(r.ModellSprache is { } ms && ScoresheetNotation.Find(ms) != null ? ms : lang)));
    r.Halbzuege = got.Count;
    bool Hit(int i) => i < got.Count && i < truthMoves.Count && resolution.Plies[i].Uci == truthMoves[i].Uci;
    r.Richtig = Enumerable.Range(0, Math.Min(got.Count, truth.Count)).Count(Hit);
    r.RichtigAnteil = Share(r.Richtig, truth.Count);
    var first = Enumerable.Range(0, Math.Max(got.Count, truth.Count))
        .FirstOrDefault(i => i >= got.Count || i >= truth.Count || !Hit(i), -1);
    r.ErsteAbweichung = first < 0 ? null : Label(first, first < truth.Count ? truth[first] : "—", first < got.Count ? got[first] : "—");
    r.Unsicher = resolution.Plies.Count(p => p.Uncertain);
    r.UnsicherUndFalsch = Enumerable.Range(0, Math.Min(got.Count, truth.Count))
        .Count(i => resolution.Plies[i].Uncertain && !Hit(i));
    r.FalschOhneMarke = Enumerable.Range(0, Math.Min(got.Count, truth.Count))
        .Count(i => !resolution.Plies[i].Uncertain && !Hit(i));
    if (first >= 0 && first < got.Count && first < truth.Count)
    {
        var opts = resolution.Plies[first].Options;
        r.ErsteAbweichungMarkiert = resolution.Plies[first].Uncertain;
        r.WahrheitUnterLesarten = opts != null && opts.Any(o => o.Uci == truthMoves[first].Uci);
        r.LesartenDort = opts?.Select(o => $"{o.San} ({o.Reach})").ToList();
    }
    r.Offen = resolution.Unresolved.Count;
    r.GeratenOderRepariert = resolution.Plies.Count(p => p.Match is ScoresheetResolver.Matches.Fuzzy or ScoresheetResolver.Matches.Guess);
    if (!resolverOnly)
        r.AufloeserRettet = Enumerable.Range(0, Math.Min(Math.Min(got.Count, truth.Count), scanned.Count))
            .Count(i => UciOf(truthMoves[i].FenBefore, scanned[i].San ?? "") != truthMoves[i].Uci && Hit(i));
    Console.WriteLine($"{b.Beleg}: {r.Richtig}/{truth.Count} richtig, erste Abweichung {r.ErsteAbweichung ?? "keine"}, " +
                      $"{r.Unsicher} unsicher, {r.Offen} offen, {r.Sekunden}s" + (resolverOnly ? "" : $", {r.KostenUsd:0.000} $"));
}

var json = JsonSerializer.Serialize(results, new JsonSerializerOptions
{
    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
});
File.WriteAllText(Path.Combine(outDir, "results.json"), json);
File.WriteAllText(Path.Combine(outDir, "report.md"), Report(results, resolverOnly,
    replayDir.Length > 0 ? $"Replay von {replayDir}" : local ? $"{provider}: {model} @ {endpoint}" : model, spentMicro));
Console.WriteLine($"→ {Path.Combine(outDir, "report.md")}" + (resolverOnly ? "" : $" · Kosten gesamt {spentMicro / 1_000_000m:0.000} $"));
return 0;

// ── Hilfen ───────────────────────────────────────────────────────────────────────────────────────

/// <summary>Die Modelle eines OpenAI-kompatiblen Servers (<c>GET /models</c>), oder <c>null</c> mit Meldung.</summary>
static async Task<List<string>?> ModelIds(HttpClient http, string endpoint, string? key)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.TrimEnd('/') + "/models");
    if (!string.IsNullOrWhiteSpace(key))
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
    try
    {
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"{endpoint}/models: HTTP {(int)response.StatusCode}");
            return null;
        }
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(m => m.GetProperty("id").GetString() ?? "").Where(id => id.Length > 0).ToList();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{endpoint}/models: {ex.GetType().Name}: {ex.Message}");
        return null;
    }
}

/// <summary>Lesen: die Rohausgabe gegen die Abschrift (HCS: der gelesene Eintrag, dort steht, was WIRKLICH dasteht;
/// portugiesisch: die SAN-Lesart, dort ist die Abschrift die gespielte Partie) — und die SAN-Lesart des Modells gegen
/// die gespielte Partie, als ZUG verglichen (Sd7 = Sbd7).</summary>
static void Reading(Result r, List<ScannedPly> scanned, List<string> sheet, List<TruthMove> truth, string lang)
{
    var read = lang == "pt" ? scanned.Select(p => p.San ?? "").ToList() : scanned.Select(p => p.Written).ToList();
    r.LesenGenau = Share(IndexMatches(read, sheet), sheet.Count);
    r.LesenLcs = Share(Lcs(read.Select(Norm).ToList(), sheet.Select(Norm).ToList()), sheet.Count);
    var right = Enumerable.Range(0, Math.Min(scanned.Count, truth.Count))
        .Count(i => UciOf(truth[i].FenBefore, scanned[i].San ?? "") == truth[i].Uci);
    r.ModellSanRichtig = Share(right, truth.Count);
}

/// <summary>Der Zug, den eine SAN in dieser Stellung meint (von–nach), oder <c>null</c>.</summary>
static string? UciOf(string fen, string san)
{
    if (string.IsNullOrWhiteSpace(san)) return null;
    try
    {
        var legal = Chess.ChessBoard.LoadFromFen(fen).Moves(generateSan: true);
        var key = ScoresheetNotation.Key(san);
        var exact = legal.Where(m => ScoresheetNotation.Key(m.San ?? "") == key).ToList();
        if (exact.Count == 1) return GamePlies.ToUci(exact[0]);
        var loose = legal.Where(m => ScoresheetNotation.LooseKey(ScoresheetNotation.Key(m.San ?? "")) == ScoresheetNotation.LooseKey(key)).ToList();
        return loose.Count == 1 ? GamePlies.ToUci(loose[0]) : null;
    }
    catch { return null; }
}

static List<TruthMove> Truth(string pgn, out string? note)
{
    note = null;
    // Ein Null-Zug („--" — in der Quelle fehlt ein Zug) ist kein Schach mehr: ausgewertet wird bis davor.
    var cut = pgn.IndexOf(" -- ", StringComparison.Ordinal);
    var usable = cut >= 0 ? pgn[..cut] + " *" : pgn;
    usable = System.Text.RegularExpressions.Regex.Replace(usable, @"\s\d+\.\s*\*$", " *"); // hängende Zugnummer
    var parsed = GamePlies.Parse(usable, 600);
    var plies = parsed?.Plies.Select(p => new TruthMove(p.San, p.Uci, p.Fen)).ToList() ?? new List<TruthMove>();
    if (cut >= 0)
        note = $"Soll-PGN enthält einen Null-Zug („--“) — ausgewertet bis Halbzug {plies.Count}";
    return plies;
}

static List<string> SheetTokens(string text)
{
    var tokens = new List<string>();
    foreach (var raw in text.Split('\n'))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        foreach (var t in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (t.EndsWith('.') && t.TrimEnd('.').All(char.IsDigit)) continue; // Zugnummer
            tokens.Add(t);
        }
    }
    return tokens;
}

/// <summary>Englische SAN in portugiesische Buchstaben (C=Springer, B=Läufer, T=Turm, D=Dame, R=König).</summary>
static string ToPortuguese(string san)
{
    if (san.Length == 0 || san.StartsWith("O-")) return san;
    var map = new Dictionary<char, char> { ['N'] = 'C', ['B'] = 'B', ['R'] = 'T', ['Q'] = 'D', ['K'] = 'R' };
    var sb = new StringBuilder(san);
    if (map.TryGetValue(sb[0], out var p)) sb[0] = p;
    var eq = san.IndexOf('=');
    if (eq >= 0 && eq + 1 < sb.Length && map.TryGetValue(sb[eq + 1], out var q)) sb[eq + 1] = q;
    return sb.ToString();
}

/// <summary>Je Halbzug: Formular-Abschrift, was das Modell las (Eintrag/SAN), Soll, Ergebnis, Art, unsicher, Lesarten.</summary>
static void WritePlyTable(string path, ScoresheetResolution r, List<ScannedPly> scanned, List<string> sheet,
    List<TruthMove> truthMoves, ScoresheetResolver.Options options)
{
    var truth = truthMoves.Select(t => t.San).ToList();
    // Kosten des SOLL-Wegs je Halbzug (gegen den Eintrag derselben Nummer) — zeigt, warum die Suche anders entschied.
    var sollCost = new List<string>();
    var board = new Chess.ChessBoard();
    for (var i = 0; i < truth.Count; i++)
    {
        var legal = board.Moves(generateSan: true).FirstOrDefault(m => SameMove(m.San ?? "", truth[i]));
        if (legal == null) break;
        var uci = GamePlies.ToUci(legal);
        sollCost.Add(i < scanned.Count ? Fmt(ScoresheetResolver.Score(scanned[i], legal.San!, uci, options)) : "");
        board.Move(legal);
    }
    static string Fmt((double Cost, string Match) x) => double.IsPositiveInfinity(x.Cost) ? "∞" : $"{x.Cost:0.0} {x.Match}";
    var sb = new StringBuilder("ply\tzug\tabschrift\tmodell_eintrag\tmodell_san\tsoll\tergebnis\tok\tart\tunsicher\tlesarten\tsoll_kosten\n");
    for (var i = 0; i < Math.Max(r.Plies.Count, truth.Count); i++)
    {
        var p = i < r.Plies.Count ? r.Plies[i] : null;
        var w = p?.W;
        string At(IReadOnlyList<string> l, int? k) => k is int x && x >= 0 && x < l.Count ? l[x] : "";
        var soll = i < truth.Count ? truth[i] : "";
        sb.Append(i).Append('\t').Append($"{i / 2 + 1}{(i % 2 == 0 ? "." : "…")}").Append('\t')
          .Append(At(sheet, w)).Append('\t')
          .Append(w is int wi && wi < scanned.Count ? scanned[wi].Written : "").Append('\t')
          .Append(w is int wj && wj < scanned.Count ? scanned[wj].San ?? "" : "").Append('\t')
          .Append(soll).Append('\t').Append(p?.San ?? "").Append('\t')
          .Append(p != null && i < truthMoves.Count && p.Uci == truthMoves[i].Uci ? "✓" : "✗").Append('\t')
          .Append(p?.Match ?? "").Append('\t').Append(p?.Uncertain == true ? "!" : "").Append('\t')
          .Append(p?.Options == null ? "" : string.Join(" ", p.Options.Select(o => $"{o.San}({o.Reach})"))).Append('\t')
          .Append(i < sollCost.Count ? sollCost[i] : "")
          .Append('\n');
    }
    foreach (var sk in r.Skipped) sb.Append($"# übersprungen: Eintrag {sk.W} „{sk.Written}“ nach Halbzug {sk.AfterPly}\n");
    if (r.Unresolved.Count > 0) sb.Append("# offen: ").Append(string.Join(' ', r.Unresolved)).Append('\n');
    File.WriteAllText(path, sb.ToString());
}

static string Norm(string s) => ScoresheetNotation.Key(s).ToLowerInvariant();

static bool SameMove(string a, string b) => Norm(a) == Norm(b) && a.Length > 0;

static int IndexMatches(IReadOnlyList<string> a, IReadOnlyList<string> b)
    => Enumerable.Range(0, Math.Min(a.Count, b.Count)).Count(i => SameMove(a[i], b[i]));

static int Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
{
    var dp = new int[a.Count + 1, b.Count + 1];
    for (var i = 1; i <= a.Count; i++)
        for (var j = 1; j <= b.Count; j++)
            dp[i, j] = a[i - 1] == b[j - 1] && a[i - 1].Length > 0 ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
    return dp[a.Count, b.Count];
}

static double Share(int n, int of) => of == 0 ? 0 : Math.Round(100.0 * n / of, 1);

static string Label(int ply, string soll, string ist)
    => $"{ply / 2 + 1}{(ply % 2 == 0 ? "." : "…")} Soll {soll}, gelesen {ist}";

static string Report(List<Result> rs, bool resolverOnly, string model, long spentMicro)
{
    var sb = new StringBuilder();
    sb.AppendLine(resolverOnly
        ? "# ScoresheetBench — nur Auflöser (Abschrift als Eingabe, kein Modell)"
        : $"# ScoresheetBench — voller Lauf ({model})");
    sb.AppendLine();
    sb.AppendLine(resolverOnly
        ? "| Beleg | Notation | Soll | richtig | erste Abweichung | unsicher | falsch ohne Marke | Wahrheit unter Lesarten | offen | Zeit |"
        : "| Beleg | Notation | Soll | Lesen (Index / LCS) | Modell-SAN richtig | aufgelöst richtig | erste Abweichung | unsicher | falsch ohne Marke | Wahrheit unter Lesarten | Runden | Zeit | Kosten |");
    sb.AppendLine(resolverOnly
        ? "|---|---|---|---|---|---|---|---|---|---|"
        : "|---|---|---|---|---|---|---|---|---|---|---|---|---|");
    foreach (var r in rs)
    {
        var wahr = r.WahrheitUnterLesarten switch { true => "ja", false => "nein", null => "—" };
        if (resolverOnly)
            sb.AppendLine($"| {r.Beleg} | {r.Notation} | {r.SollHalbzuege} | {r.Richtig} ({r.RichtigAnteil} %) | {r.ErsteAbweichung ?? "keine"} | {r.Unsicher} | {r.FalschOhneMarke} | {wahr} | {r.Offen} | {r.Sekunden} s |");
        else
            sb.AppendLine($"| {r.Beleg} | {r.Notation} | {r.SollHalbzuege} | {r.LesenGenau} % / {r.LesenLcs} % | {r.ModellSanRichtig} % | {r.Richtig} ({r.RichtigAnteil} %) | {r.ErsteAbweichung ?? (r.Fehler != null ? "Fehler: " + r.Fehler : "keine")} | {r.Unsicher} | {r.FalschOhneMarke} | {wahr} | {r.Runden} | {r.Sekunden} s | {r.KostenUsd:0.000} $ |");
    }
    var total = rs.Sum(r => r.SollHalbzuege);
    var right = rs.Sum(r => r.Richtig);
    sb.AppendLine();
    sb.AppendLine($"Gesamt: {right} von {total} Halbzügen richtig ({Share(right, total)} %)" +
                  (resolverOnly ? "" : $", Kosten {spentMicro / 1_000_000m:0.000} $"));
    foreach (var r in rs.Where(r => r.SollHinweis != null)) sb.AppendLine($"- Beleg {r.Beleg}: {r.SollHinweis}");
    return sb.ToString();
}

sealed record TruthMove(string San, string Uci, string FenBefore);

sealed class BelegInfo
{
    public string Beleg { get; set; } = "";
    public string Bild { get; set; } = "";
    public string Quelle { get; set; } = "";
    public string Notation { get; set; } = "";
    public string? Sprache { get; set; }
    public int Halbzuege { get; set; }
    public int? FormularWeichtAbVonPgn { get; set; }
}

sealed class Result
{
    public string Beleg { get; set; } = "";
    public string Notation { get; set; } = "";
    public string Quelle { get; set; } = "";
    public int SollHalbzuege { get; set; }
    public string? SollHinweis { get; set; }
    public int FormularEintraege { get; set; }
    public int? ModellEintraege { get; set; }
    public string? ModellSprache { get; set; }
    public double? LesenGenau { get; set; }
    public double? LesenLcs { get; set; }
    public double? ModellSanRichtig { get; set; }
    public int Halbzuege { get; set; }
    public int Richtig { get; set; }
    public double RichtigAnteil { get; set; }
    public string? ErsteAbweichung { get; set; }
    public bool? ErsteAbweichungMarkiert { get; set; }
    public bool? WahrheitUnterLesarten { get; set; }
    public List<string>? LesartenDort { get; set; }
    public int Unsicher { get; set; }
    public int UnsicherUndFalsch { get; set; }
    public int FalschOhneMarke { get; set; }
    public int Offen { get; set; }
    public int GeratenOderRepariert { get; set; }
    public int? AufloeserRettet { get; set; }
    public int Runden { get; set; }
    public double Sekunden { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal KostenUsd { get; set; }
    public string? Fehler { get; set; }
}
