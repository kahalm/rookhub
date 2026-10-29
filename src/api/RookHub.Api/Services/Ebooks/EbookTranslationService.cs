using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Wie ein Übersetzungslauf ausging.</summary>
public enum EbookTranslationOutcome
{
    Done,
    /// <summary>Noch nicht möglich (Text-PDF, Diagramme noch nicht erkannt) — <see cref="EbookTranslation.NotBefore"/> gesetzt.</summary>
    Waiting,
    NoText,
    Failed,
    /// <summary>Die Spark antwortet nicht — der Lauf hält an, das Fertige bleibt im Zwischenspeicher, die Zeile wartet.</summary>
    Unreachable,
    NotFound,
}

/// <summary>Die Spark antwortet nicht mehr (mehrere Transportfehler in Folge) — der Lauf hält an, statt Text als gescheitert zu buchen.</summary>
public sealed class EbookTranslationUnreachableException() : Exception("The text model is unreachable.");

/// <summary>
/// Verbindung Anfrage ↔ <see cref="EbookTranslationWorker"/> (Singleton, nur Arbeitsspeicher): wecken, wenn ein Admin ein Buch
/// vorzieht, und der LAUFENDE Lauf für die Übersicht (Fortschritt, Restdauer).
/// </summary>
public sealed class EbookTranslationSignal
{
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lock _gate = new();

    /// <summary>Der laufende Lauf: Zeile, Buch, Sprache, Zähler (live), Beginn.</summary>
    public sealed class Run
    {
        public int TranslationId { get; init; }
        public int EbookId { get; init; }
        public string Language { get; init; } = "";
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public int Total;
        public int Done;
        /// <summary>Stand bei Beginn dieses Laufs (für das Tempo: nur, was in DIESEM Lauf übersetzt wurde).</summary>
        public int DoneAtStart;
    }

    public Run? Current { get; private set; }

    public void Wake()
    {
        lock (_gate)
            if (_wake.CurrentCount == 0) _wake.Release();
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _wake.WaitAsync(timeout, ct);

    internal void Begin(Run run) { lock (_gate) Current = run; }

    internal void End(Run run) { lock (_gate) if (ReferenceEquals(Current, run)) Current = null; }
}

/// <summary>
/// Bücher ins Englische übersetzen (0.876.0, Wunsch des Nutzers 29.10.2026) — wie die Kurs-Übersetzung über das Sprachmodell
/// auf der EIGENEN Hardware (Spark), nur in den Sperr-freien Zeiten (<see cref="QuietHours"/>, der Worker hält sie ein).
///
/// <para><b>Reihenfolge</b> (<see cref="ClaimNextAsync"/>): von Hand vorgezogene zuerst; dann ALLE Bücher, die weder Englisch
/// noch Deutsch sind; erst wenn davon keines mehr dran ist, die deutschen (<see cref="LateSources"/>). Bücher in der
/// Zielsprache nie. Unklare/gemischte Sprache, kein Text (Scan) und anonyme Uploads gar nicht. Solange noch Bücher ohne
/// geprüfte Sprache da sind, beginnt die deutsche Runde nicht.</para>
///
/// <para><b>Quelle</b> ist das EPUB, das der Leser zeigt (<see cref="EbookFormats.Primary"/>: das echte vor dem Fließtext-EPUB).
/// Hat ein Buch nur ein Text-PDF, entsteht erst das Fließtext-EPUB (<see cref="EbookReflowService"/>) — geht das nicht (Scan
/// ohne Textebene), bleibt das Buch auf <see cref="EbookTranslationState.NoText"/> für Stufe 2 (OCR-Ebene, später).</para>
///
/// <para><b>Ein Lauf</b> (<see cref="RunAsync"/>): EPUB zerlegen (<see cref="EbookEpubTranslation"/>), JEDER verschiedene
/// Text geht nur EINMAL ans Modell, was der Zwischenspeicher schon hat gar nicht; Fuhren zu
/// <see cref="EbookTranslationOptions.ChunkChars"/> parallel (<see cref="EbookTranslationOptions.Parallel"/>) über den
/// <see cref="CommentTranslator"/> (Länge, Sprache, offenes Zitat, zweiter Versuch). Scheitert eine Fuhre, geht jeder ihrer
/// Texte einzeln noch einmal; was dann noch scheitert, bleibt im Buch im Original (<see cref="EbookTranslation.SegmentsFailed"/>).
/// Am Ende das übersetzte EPUB schreiben. Abbruch (Sperrzeit, Pause, Neustart) verliert nichts außer den laufenden Fuhren.</para>
/// </summary>
public class EbookTranslationService
{
    /// <summary>Wie lange ein Text-PDF wartet, bis seine Diagramme erkannt sind (dann erst das Fließtext-EPUB).</summary>
    public static readonly TimeSpan WaitForDiagrams = TimeSpan.FromHours(6);

    /// <summary>So oft wird der Fortschritt eines Laufs in die Datenbank geschrieben.</summary>
    public static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(20);

    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly IClaudeJsonClient _llm;
    private readonly CommentTranslator _translator;
    private readonly EbookTranslationOptions _options;
    private readonly EbookTranslationSignal _signal;
    private readonly EbookReflowService? _reflow;
    private readonly QuietHours? _quiet;
    private readonly ILogger<EbookTranslationService> _logger;

    public EbookTranslationService(AppDbContext db, EbookStorage storage, IClaudeJsonClient llm, IOptions<EbookTranslationOptions> options,
        EbookTranslationSignal signal, ILogger<EbookTranslationService> logger, EbookReflowService? reflow = null, QuietHours? quiet = null)
    {
        _db = db;
        _storage = storage;
        _llm = llm;
        _translator = new CommentTranslator(llm, logger);
        _options = options.Value;
        _signal = signal;
        _logger = logger;
        _reflow = reflow;
        _quiet = quiet;
    }

    /// <summary>Zielsprachen (leer = Dienst aus).</summary>
    public IReadOnlyList<string> Targets => _options.Targets();

    /// <summary>Eingeschaltet: Zielsprachen gesetzt und die beschreibbare Wurzel da (dorthin kommen die EPUBs).</summary>
    public bool IsEnabled => Targets.Count > 0 && _storage.Root != null;

    /// <summary>Übersetzt wird NUR über die eigene Hardware (Wunsch: „über Spark“) — nie über den Claude-Text-Schlüssel.</summary>
    public bool IsAvailable => _llm.IsConfigured && _llm.IsLocal;

    public bool IsUnreachable => _llm.IsUnreachable;

    /// <summary>Quellsprachen, die erst NACH allen anderen drankommen (für Englisch: Deutsch). Die Zielsprache selbst nie.</summary>
    public static IReadOnlyList<string> LateSources(string target) => new[] { "de", "en" }.Where(l => l != target).ToList();

    // ── Sprache erkennen ────────────────────────────────────────────────────────────────────────

    /// <summary>Für bis zu <paramref name="batch"/> Bücher ohne geprüfte Sprache die Sprache bestimmen (CPU, keine Spark) → Anzahl.</summary>
    public async Task<int> DetectLanguagesAsync(int batch, CancellationToken ct)
    {
        var books = await _db.Ebooks.Include(b => b.Files)
            .Where(b => b.LanguageSource == null && b.AnonymousSessionId == null)
            .OrderBy(b => b.Id).Take(batch).ToListAsync(ct);
        foreach (var book in books)
        {
            ct.ThrowIfCancellationRequested();
            EbookLanguageResult result;
            try { result = await Task.Run(() => DetectBook(book, ct), ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "E-Book {EbookId}: Sprache nicht bestimmbar", book.Id);
                result = EbookLanguageResult.Unclear;
            }
            book.Language = result.Language;
            book.LanguageSource = result.Source;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("E-Book {EbookId}: Sprache {Language} ({Source})", book.Id, result.Language ?? "-", result.Source);
        }
        return books.Count;
    }

    private EbookLanguageResult DetectBook(Ebook book, CancellationToken ct)
    {
        var epub = EbookFormats.Primary(book.Files.Where(f => f.Format == "epub"));
        if (epub != null && ExistingPath(book, epub) is { } epubPath)
        {
            var (paragraphs, hint) = EbookLanguageDetector.ReadEpub(epubPath);
            return EbookLanguageDetector.Detect(paragraphs, hint);
        }
        var pdf = book.Files.Where(f => f.Format == "pdf").OrderBy(f => f.Derived).FirstOrDefault();
        if (pdf != null && ExistingPath(book, pdf) is { } pdfPath)
            return EbookLanguageDetector.Detect(EbookLanguageDetector.ReadPdf(pdfPath, ct));
        return EbookLanguageResult.NoText;
    }

    private string? ExistingPath(Ebook book, EbookFile file)
    {
        try
        {
            var path = _storage.ResolveFile(book, file);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is DomainValidationException or InvalidOperationException) { return null; }
    }

    // ── Reihe ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Die Bücher, die für <paramref name="target"/> noch übersetzt werden — ohne Zeile oder mit wartender
    /// (die Zeit-Sperre <see cref="EbookTranslation.NotBefore"/> berücksichtigt, wenn <paramref name="now"/> gesetzt ist).</summary>
    private IQueryable<Ebook> Candidates(string target, DateTime? now) =>
        _db.Ebooks.Where(b => b.AnonymousSessionId == null && b.Language != null && b.Language != target
                              && (b.LanguageSource == EbookLanguageDetector.SourceText || b.LanguageSource == EbookLanguageDetector.SourceManual)
                              && !_db.EbookTranslations.Any(t => t.EbookId == b.Id && t.Language == target
                                  && (t.State != EbookTranslationState.Pending || (now != null && t.NotBefore != null && t.NotBefore > now))));

    /// <summary>
    /// Das nächste Buch nehmen (Zeile auf <see cref="EbookTranslationState.Running"/>) → Zeilen-Id, <c>null</c> = nichts zu tun.
    /// Vorgezogene vor allem; dann je Zielsprache die erste Runde (weder Ziel- noch späte Sprache), dann die späten Sprachen
    /// in ihrer Reihenfolge (für Englisch: Deutsch) — die erst, wenn die erste Runde leer ist und jedes Buch geprüft wurde.
    /// </summary>
    public async Task<int?> ClaimNextAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var targets = Targets;
        if (targets.Count == 0) return null;

        var prioritized = await _db.EbookTranslations
            .Where(t => targets.Contains(t.Language) && t.State == EbookTranslationState.Pending && t.Priority
                        && (t.NotBefore == null || t.NotBefore <= now))
            .OrderBy(t => t.UpdatedAt).ThenBy(t => t.Id).FirstOrDefaultAsync(ct);
        if (prioritized != null) return await MarkRunningAsync(prioritized, ct);

        var undetected = await _db.Ebooks.AnyAsync(b => b.LanguageSource == null && b.AnonymousSessionId == null, ct);
        foreach (var target in targets)
        {
            var late = LateSources(target);
            var candidates = Candidates(target, now);
            int? next = await candidates.Where(b => !late.Contains(b.Language!)).OrderBy(b => b.Id).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
            if (next == null && !undetected)
            {
                foreach (var lang in late)
                {
                    next = await candidates.Where(b => b.Language == lang).OrderBy(b => b.Id).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
                    if (next != null) break;
                }
            }
            if (next is not { } ebookId) continue;
            var row = await _db.EbookTranslations.FirstOrDefaultAsync(t => t.EbookId == ebookId && t.Language == target, ct);
            if (row == null)
            {
                row = new EbookTranslation { EbookId = ebookId, Language = target };
                _db.EbookTranslations.Add(row);
            }
            return await MarkRunningAsync(row, ct);
        }
        return null;
    }

    private async Task<int> MarkRunningAsync(EbookTranslation row, CancellationToken ct)
    {
        row.State = EbookTranslationState.Running;
        row.NotBefore = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return row.Id;
    }

    /// <summary>Beim Start: Läufe, die beim Herunterfahren mitten drin waren, wieder auf wartend.</summary>
    public async Task<int> RequeueInterruptedAsync(CancellationToken ct)
    {
        var rows = await _db.EbookTranslations.Where(t => t.State == EbookTranslationState.Running).ToListAsync(ct);
        foreach (var r in rows) { r.State = EbookTranslationState.Pending; r.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>Abgebrochener Lauf (Sperrzeit, Pause, Spark weg) — zurück in die Reihe, der Zwischenspeicher hält das Fertige.</summary>
    public async Task RequeueAsync(int translationId, CancellationToken ct)
    {
        var row = await _db.EbookTranslations.FirstOrDefaultAsync(t => t.Id == translationId, ct);
        if (row == null || row.State != EbookTranslationState.Running) return;
        row.State = EbookTranslationState.Pending;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(int translationId, string error, CancellationToken ct)
    {
        var row = await _db.EbookTranslations.FirstOrDefaultAsync(t => t.Id == translationId, ct);
        if (row == null) return;
        row.State = EbookTranslationState.Failed;
        row.Error = Clip(error, 500);
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ── Ein Lauf ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Buch übersetzen (die Zeile steht auf Running). Bricht über <paramref name="ct"/> ab (Sperrzeit/Pause/Ende).</summary>
    public async Task<EbookTranslationOutcome> RunAsync(int translationId, CancellationToken ct)
    {
        var row = await _db.EbookTranslations.FirstOrDefaultAsync(t => t.Id == translationId, ct);
        if (row == null) return EbookTranslationOutcome.NotFound;
        var ebook = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == row.EbookId, ct);
        if (ebook == null)
        {
            _db.EbookTranslations.Remove(row);
            await _db.SaveChangesAsync(ct);
            return EbookTranslationOutcome.NotFound;
        }

        var (source, outcome) = await EnsureSourceAsync(ebook, row, ct);
        if (source == null) return outcome;
        var sourcePath = _storage.ResolveFile(ebook, source);

        using var zip = ZipFile.OpenRead(sourcePath);
        EpubTranslationPlan plan;
        try { plan = EbookEpubTranslation.Plan(zip); }
        catch (Exception ex) when (ex is EpubTranslationException or InvalidDataException or System.Xml.XmlException)
        {
            await FailAsync(row, "The EPUB is not readable: " + ex.Message, ct);
            return EbookTranslationOutcome.Failed;
        }

        var cache = EbookTranslationCache.Open(_storage.ResolvePath(EbookSource.Upload, EbookStorage.TranslationCachePath(ebook.Id, row.Language)));
        var unique = plan.UniqueTexts();
        var todo = unique.Where(t => !cache.Contains(t)).ToList();
        row.SourceFileId = source.Id;
        row.SourceHash = plan.SourceHash;
        row.SegmentsTotal = unique.Count;
        row.SegmentsDone = unique.Count - todo.Count;
        row.SegmentsFailed = 0;
        row.Error = null;
        row.Runs++;
        row.StartedAt = DateTime.UtcNow;
        row.Model = _translator.ModelName;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("E-Book {EbookId} → {Lang}: {Todo} von {Total} Texten offen ({Segments} Blöcke)",
            ebook.Id, row.Language, todo.Count, unique.Count, plan.SegmentCount);

        var run = new EbookTranslationSignal.Run
        {
            TranslationId = row.Id, EbookId = ebook.Id, Language = row.Language, Total = unique.Count,
            Done = row.SegmentsDone, DoneAtStart = row.SegmentsDone,
        };
        _signal.Begin(run);
        var failed = new ConcurrentBag<string>();
        try
        {
            var from = ebook.Language;
            var saveGate = new SemaphoreSlim(1, 1);
            var lastSave = DateTime.UtcNow;
            await Parallel.ForEachAsync(Chunks(todo), new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(_options.Parallel, 1, 64), CancellationToken = ct },
                async (chunk, c) =>
                {
                    var done = await TranslateTextsAsync(chunk, from, row.Language, ebook.Id, failed, c);
                    cache.Add(done);
                    Interlocked.Add(ref run.Done, done.Count);
                    if (DateTime.UtcNow - lastSave < ProgressEvery || !await saveGate.WaitAsync(0, c)) return;
                    try
                    {
                        lastSave = DateTime.UtcNow;
                        row.SegmentsDone = Volatile.Read(ref run.Done);
                        row.UpdatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync(c);
                    }
                    finally { saveGate.Release(); }
                });
        }
        catch (EbookTranslationUnreachableException)
        {
            await SaveProgressAsync(row, run, CancellationToken.None);
            _logger.LogWarning("E-Book {EbookId} → {Lang}: Spark antwortet nicht — Lauf angehalten", ebook.Id, row.Language);
            return EbookTranslationOutcome.Unreachable;
        }
        catch (OperationCanceledException)
        {
            await SaveProgressAsync(row, run, CancellationToken.None);
            throw;
        }
        finally
        {
            _signal.End(run);
        }

        // Übersetztes EPUB schreiben (Zwischenspeicher → Blöcke; was fehlt oder nicht passt, bleibt im Original).
        var relative = EbookStorage.TranslatedEpubPath(ebook.Id, row.Language);
        var target = _storage.ResolvePath(EbookSource.Upload, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".part";
        int rejected;
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                rejected = EbookEpubTranslation.Write(zip, plan, fs, row.Language, cache.Get, ebook.Language);
            File.Move(tmp, target, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { /* egal */ }
            throw;
        }

        row.State = EbookTranslationState.Done;
        row.Priority = false;
        row.RelativePath = relative;
        row.SizeBytes = new FileInfo(target).Length;
        row.SegmentsDone = unique.Count(cache.Contains);
        row.SegmentsFailed = failed.Distinct().Count() + rejected;
        row.CompletedAt = row.VerifiedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("E-Book {EbookId} → {Lang}: fertig, {Done}/{Total} Texte, {Failed} im Original, {Bytes} Bytes",
            ebook.Id, row.Language, row.SegmentsDone, row.SegmentsTotal, row.SegmentsFailed, row.SizeBytes);
        return EbookTranslationOutcome.Done;
    }

    private async Task SaveProgressAsync(EbookTranslation row, EbookTranslationSignal.Run run, CancellationToken ct)
    {
        try
        {
            row.SegmentsDone = Volatile.Read(ref run.Done);
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "E-Book-Übersetzung {Id}: Fortschritt nicht gespeichert", row.Id);
        }
    }

    /// <summary>Das Quell-EPUB — bei einem Text-PDF ohne EPUB erst das Fließtext-EPUB bauen. <c>null</c> + Ausgang, wenn keines geht.</summary>
    private async Task<(EbookFile? File, EbookTranslationOutcome Outcome)> EnsureSourceAsync(Ebook ebook, EbookTranslation row, CancellationToken ct)
    {
        var epub = EbookFormats.Primary(ebook.Files.Where(f => f.Format == "epub"));
        if (epub != null && ExistingPath(ebook, epub) != null) return (epub, EbookTranslationOutcome.Done);
        if (_reflow == null || !EbookReflowService.Eligible(ebook))
        {
            await SetStateAsync(row, EbookTranslationState.NoText, "No EPUB and no text PDF.", ct);
            return (null, EbookTranslationOutcome.NoText);
        }
        if (ebook.DiagramState == EbookDiagramState.Pending)
        {
            // Das Fließtext-EPUB braucht die erkannten Diagramme (Brettbilder an ihrer Stelle) — später wieder.
            row.State = EbookTranslationState.Pending;
            row.NotBefore = DateTime.UtcNow + WaitForDiagrams;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return (null, EbookTranslationOutcome.Waiting);
        }
        try
        {
            await _reflow.ConvertAsync(ebook.Id, ct);
        }
        catch (DomainValidationException ex) when (ex.Code == ApiErrorCodes.EbookReflowUnsupported)
        {
            await SetStateAsync(row, EbookTranslationState.NoText, ex.Message, ct);
            return (null, EbookTranslationOutcome.NoText);
        }
        epub = EbookFormats.Primary(ebook.Files.Where(f => f.Format == "epub"));
        if (epub == null || ExistingPath(ebook, epub) == null)
        {
            await FailAsync(row, "The reflow produced no EPUB.", ct);
            return (null, EbookTranslationOutcome.Failed);
        }
        _logger.LogInformation("E-Book {EbookId}: Fließtext-EPUB für die Übersetzung gebaut", ebook.Id);
        return (epub, EbookTranslationOutcome.Done);
    }

    private Task FailAsync(EbookTranslation row, string error, CancellationToken ct) => SetStateAsync(row, EbookTranslationState.Failed, error, ct);

    private async Task SetStateAsync(EbookTranslation row, EbookTranslationState state, string? error, CancellationToken ct)
    {
        row.State = state;
        row.Error = error == null ? null : Clip(error, 500);
        row.Priority = false;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (state == EbookTranslationState.Failed)
            _logger.LogWarning("E-Book-Übersetzung {Id} (Buch {EbookId}) gescheitert: {Error}", row.Id, row.EbookId, error);
    }

    /// <summary>Fuhren: höchstens <see cref="EbookTranslationOptions.ChunkChars"/> Zeichen und <see cref="EbookTranslationOptions.ChunkItems"/> Texte.</summary>
    internal IEnumerable<List<string>> Chunks(List<string> texts)
    {
        var maxChars = Math.Max(200, _options.ChunkChars);
        var maxItems = Math.Max(1, _options.ChunkItems);
        var current = new List<string>();
        var size = 0;
        foreach (var t in texts)
        {
            if (current.Count > 0 && (size + t.Length > maxChars || current.Count >= maxItems))
            {
                yield return current;
                current = [];
                size = 0;
            }
            current.Add(t);
            size += t.Length;
        }
        if (current.Count > 0) yield return current;
    }

    /// <summary>
    /// Eine Fuhre übersetzen → Modelltext → Übersetzung (nur passende: Platzhalter vollständig). Scheitert die Fuhre oder passen
    /// einzelne Antworten nicht, geht jeder betroffene Text EINZELN noch einmal; was dann scheitert, landet in <paramref name="failed"/>.
    /// </summary>
    private async Task<Dictionary<string, string>> TranslateTextsAsync(List<string> texts, string? from, string to, int ebookId,
        ConcurrentBag<string> failed, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = texts.Select((t, i) => (i, t)).ToList();
        var answer = await _translator.TranslateAsync(items, from, to, TranslationSubject.EbookText, ebookId, ct);
        if (answer == null && _translator.IsUnreachable) throw new EbookTranslationUnreachableException();
        var retry = new List<string>();
        for (var i = 0; i < texts.Count; i++)
        {
            if (answer != null && answer.TryGetValue(i, out var t) && EbookTextSegmenter.SamePlaceholders(texts[i], t))
                result[texts[i]] = t.Trim();
            else retry.Add(texts[i]);
        }
        if (retry.Count == 0) return result;
        if (texts.Count == 1)
        {
            failed.Add(texts[0]);
            return result;
        }
        foreach (var single in retry)
            foreach (var (k, v) in await TranslateTextsAsync([single], from, to, ebookId, failed, ct))
                result[k] = v;
        return result;
    }

    // ── Prüfen, ob die Quelle sich geändert hat ─────────────────────────────────────────────────

    /// <summary>
    /// Für bis zu <paramref name="batch"/> fertige Übersetzungen (am längsten nicht geprüfte zuerst): ist das Quell-EPUB noch
    /// dasselbe? Anderes EPUB (neue Datei, Fließtext-EPUB neu gebaut) → zurück auf wartend; der Zwischenspeicher macht das
    /// billig, nur geänderter Text geht ans Modell. → Zahl der zurückgestellten.
    /// </summary>
    public async Task<int> VerifyAsync(int batch, CancellationToken ct)
    {
        var rows = await _db.EbookTranslations.Where(t => t.State == EbookTranslationState.Done)
            .OrderBy(t => t.VerifiedAt ?? DateTime.MinValue).ThenBy(t => t.Id).Take(batch).ToListAsync(ct);
        var outdated = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var ebook = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == row.EbookId, ct);
            var epub = ebook == null ? null : EbookFormats.Primary(ebook.Files.Where(f => f.Format == "epub"));
            var path = ebook != null && epub != null ? ExistingPath(ebook, epub) : null;
            string? hash = null;
            if (path != null)
            {
                try
                {
                    using var zip = ZipFile.OpenRead(path);
                    hash = await Task.Run(() => EbookEpubTranslation.SourceHash(zip), ct);
                }
                catch (Exception ex) when (ex is EpubTranslationException or InvalidDataException or IOException) { hash = null; }
            }
            if (epub == null || hash == null || epub.Id != row.SourceFileId || hash != row.SourceHash)
            {
                row.State = EbookTranslationState.Pending;
                row.NotBefore = null;
                outdated++;
                _logger.LogInformation("E-Book {EbookId} → {Lang}: Quelle geändert, Übersetzung wird erneuert", row.EbookId, row.Language);
            }
            row.VerifiedAt = DateTime.UtcNow;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return outdated;
    }

    /// <summary>Fertige Übersetzungen eines Buchs als veraltet markieren (Fließtext-EPUB neu gebaut).</summary>
    public static async Task MarkOutdatedAsync(AppDbContext db, int ebookId, CancellationToken ct)
    {
        var rows = await db.EbookTranslations.Where(t => t.EbookId == ebookId && t.State == EbookTranslationState.Done).ToListAsync(ct);
        foreach (var r in rows) { r.State = EbookTranslationState.Pending; r.UpdatedAt = DateTime.UtcNow; }
    }

    // ── Verwaltung ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Buch vorziehen (oder neu übersetzen lassen): Zeile auf wartend + Vorrang. Buch in der Zielsprache → 400.</summary>
    public async Task<EbookTranslationItemDto> EnqueueAsync(int ebookId, string language, CancellationToken ct)
    {
        var lang = (language ?? "").Trim().ToLowerInvariant();
        if (lang.Length != 2 || !lang.All(char.IsAsciiLetterLower)) throw new DomainValidationException("Unknown target language.");
        var ebook = await _db.Ebooks.FirstOrDefaultAsync(b => b.Id == ebookId, ct) ?? throw new NotFoundException("E-book not found.");
        if (ebook.Language == lang) throw new DomainValidationException("The book is already in that language.");
        var row = await _db.EbookTranslations.FirstOrDefaultAsync(t => t.EbookId == ebookId && t.Language == lang, ct);
        if (row == null)
        {
            row = new EbookTranslation { EbookId = ebookId, Language = lang };
            _db.EbookTranslations.Add(row);
        }
        if (row.State != EbookTranslationState.Running) row.State = EbookTranslationState.Pending;
        row.Priority = true;
        row.NotBefore = null;
        row.Error = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _signal.Wake();
        return Item(row, ebook);
    }

    /// <summary>Gescheiterte und unvollständige (Texte im Original) wieder in die Reihe → Anzahl.</summary>
    public async Task<int> RetryFailedAsync(CancellationToken ct)
    {
        var rows = await _db.EbookTranslations
            .Where(t => t.State == EbookTranslationState.Failed || (t.State == EbookTranslationState.Done && t.SegmentsFailed > 0))
            .ToListAsync(ct);
        foreach (var r in rows)
        {
            r.State = EbookTranslationState.Pending;
            r.NotBefore = null;
            r.Error = null;
            r.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        if (rows.Count > 0) _signal.Wake();
        return rows.Count;
    }

    public async Task<EbookTranslationStatusDto> StatusAsync(CancellationToken ct)
    {
        var dto = new EbookTranslationStatusDto
        {
            Enabled = IsEnabled,
            ModelAvailable = IsAvailable,
            Languages = Targets.ToList(),
            QuietNow = _quiet?.IsQuietNow() == true,
            QuietUntil = _quiet?.QuietUntil()?.UtcDateTime,
        };
        var books = await _db.Ebooks.Where(b => b.AnonymousSessionId == null)
            .GroupBy(b => new { b.Language, b.LanguageSource }).Select(g => new { g.Key.Language, g.Key.LanguageSource, Count = g.Count() })
            .ToListAsync(ct);
        dto.BookLanguages = books
            .GroupBy(b => b.LanguageSource == null ? "pending"
                : b.LanguageSource == EbookLanguageDetector.SourceNoText ? "notext"
                : b.Language ?? "und")
            .Select(g => new EbookLanguageCountDto { Language = g.Key, Books = g.Sum(x => x.Count) })
            .OrderByDescending(x => x.Books).ThenBy(x => x.Language).ToList();

        var rows = await _db.EbookTranslations.GroupBy(t => new { t.Language, t.State })
            .Select(g => new { g.Key.Language, g.Key.State, Count = g.Count() }).ToListAsync(ct);
        foreach (var target in Targets.Union(rows.Select(r => r.Language)).Distinct())
        {
            int Count(EbookTranslationState s) => rows.Where(r => r.Language == target && r.State == s).Sum(r => r.Count);
            var late = LateSources(target);
            var candidates = Candidates(target, null);
            dto.Targets.Add(new EbookTranslationCountsDto
            {
                Language = target,
                Done = Count(EbookTranslationState.Done),
                Pending = Count(EbookTranslationState.Pending) + Count(EbookTranslationState.Running),
                Failed = Count(EbookTranslationState.Failed),
                NoText = Count(EbookTranslationState.NoText),
                Remaining = await candidates.CountAsync(ct),
                RemainingFirstRound = await candidates.CountAsync(b => !late.Contains(b.Language!), ct),
            });
        }

        if (_signal.Current is { } run)
        {
            var title = await _db.Ebooks.Where(b => b.Id == run.EbookId).Select(b => new { b.Title, b.Language }).FirstOrDefaultAsync(ct);
            var done = Volatile.Read(ref run.Done);
            var elapsed = DateTime.UtcNow - run.StartedAt;
            var rate = elapsed.TotalMinutes >= 1 && done > run.DoneAtStart ? (done - run.DoneAtStart) / elapsed.TotalMinutes : 0;
            dto.Running = new EbookTranslationRunDto
            {
                EbookId = run.EbookId, Title = title?.Title ?? "", Language = run.Language, SourceLanguage = title?.Language,
                SegmentsDone = done, SegmentsTotal = run.Total, StartedAt = run.StartedAt,
                EtaMinutes = rate > 0 ? (int)Math.Ceiling((run.Total - done) / rate) : null,
            };
        }

        var failed = await _db.EbookTranslations.Include(t => t.Ebook)
            .Where(t => t.State == EbookTranslationState.Failed).OrderByDescending(t => t.UpdatedAt).Take(20).ToListAsync(ct);
        dto.Failed = failed.Select(t => Item(t, t.Ebook)).ToList();

        var next = await _db.EbookTranslations.Include(t => t.Ebook)
            .Where(t => t.State == EbookTranslationState.Pending).OrderByDescending(t => t.Priority).ThenBy(t => t.UpdatedAt).Take(10).ToListAsync(ct);
        dto.Next = next.Select(t => Item(t, t.Ebook)).ToList();
        if (dto.Next.Count < 10 && Targets.Count > 0)
        {
            var target = Targets[0];
            var late = LateSources(target);
            var queued = await Candidates(target, DateTime.UtcNow)
                .Where(b => !_db.EbookTranslations.Any(t => t.EbookId == b.Id && t.Language == target))
                .OrderBy(b => b.Language != null && late.Contains(b.Language) ? 1 : 0).ThenBy(b => b.Id)
                .Take(10 - dto.Next.Count).Select(b => new { b.Id, b.Title, b.Language }).ToListAsync(ct);
            dto.Next.AddRange(queued.Select(b => new EbookTranslationItemDto
            {
                EbookId = b.Id, Title = b.Title, Language = target, SourceLanguage = b.Language, State = "queued",
            }));
        }
        return dto;
    }

    private static EbookTranslationItemDto Item(EbookTranslation t, Ebook? ebook) => new()
    {
        EbookId = t.EbookId,
        Title = ebook?.Title ?? "",
        Language = t.Language,
        SourceLanguage = ebook?.Language,
        State = t.State.ToString().ToLowerInvariant(),
        Priority = t.Priority,
        SegmentsDone = t.SegmentsDone,
        SegmentsTotal = t.SegmentsTotal,
        SegmentsFailed = t.SegmentsFailed,
        Error = t.Error,
        CompletedAt = t.CompletedAt,
    };

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
