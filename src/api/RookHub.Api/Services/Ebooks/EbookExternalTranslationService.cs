using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Einspeisepunkt für EXTERNE Übersetzer (Wunsch 09.11.2026: Claude-Agenten übersetzen Bücher, die die Spark noch nicht
/// geschafft hat). Derselbe Weg wie der Worker (<see cref="EbookTranslationService"/>), nur dass die Texte statt an die
/// Spark über die API hinaus- und wieder hereingehen:
///
/// <list type="number">
/// <item><b>Beanspruchen</b> (<see cref="ClaimAsync"/>): Zeile auf <c>Running</c>, <c>Model = external:&lt;name&gt;</c> —
/// der Worker nimmt nur <c>Pending</c> und lässt sie damit liegen; Quelle wie im Worker
/// (<see cref="EbookTranslationService.EnsureSourceAsync"/>: echtes EPUB, sonst Fließtext-EPUB aus dem Text-PDF, sonst
/// <c>NoText</c>), Zerlegen mit <see cref="EbookEpubTranslation.Plan"/>; jeder verschiedene Modelltext einmal als
/// <see cref="EbookTranslationSegment"/>. Was der Zwischenspeicher (ein früherer Spark-Lauf) schon hat, ist vorbelegt.</item>
/// <item><b>Zurückgeben</b> (<see cref="PutAsync"/>): dieselben Prüfungen wie für Spark-Antworten
/// (<see cref="EbookTextSegmenter.SamePlaceholders"/>, Einsetzen samt Verschachtelung <see cref="EbookTextSegmenter.Apply"/>,
/// offenes Zitat, Länge, Sprache wie <see cref="CommentTranslator"/>) — je Text statt je Fuhre. Gültige landen in der
/// Tabelle UND im Zwischenspeicher: gibt der Übersetzer auf, macht die Spark dort weiter.</item>
/// <item><b>Zusammenbauen</b> (<see cref="AssembleAsync"/>): <see cref="EbookTranslationService.WriteTranslatedEpubAsync"/>
/// — derselbe Code wie der Worker, samt Figurenbuchstaben (<see cref="EbookNotationLetters"/>). Danach sind die Texte
/// überflüssig (der Zwischenspeicher hält sie) und werden gelöscht.</item>
/// <item><b>Freigeben</b> (<see cref="ReleaseAsync"/>): Zeile zurück auf <c>Pending</c>, Texte weg.</item>
/// </list>
/// </summary>
public class EbookExternalTranslationService
{
    /// <summary>Höchstens so viele Texte je Abruf bzw. je Rückgabe.</summary>
    public const int MaxBatch = 500;

    private static readonly Regex TranslatorName = new(@"^[A-Za-z0-9._@\-]{1,80}$", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"<\s*/?\s*[tT]\d{1,4}\s*/?\s*>", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly EbookTranslationService _translations;
    private readonly EbookTranslationSignal _signal;
    private readonly ILogger<EbookExternalTranslationService> _logger;

    public EbookExternalTranslationService(AppDbContext db, EbookStorage storage, EbookTranslationService translations,
        EbookTranslationSignal signal, ILogger<EbookExternalTranslationService> logger)
    {
        _db = db;
        _storage = storage;
        _translations = translations;
        _signal = signal;
        _logger = logger;
    }

    // ── Beanspruchen ────────────────────────────────────────────────────────────────────────────

    public async Task<ExternalTranslationInfoDto> ClaimAsync(ExternalTranslationClaimDto dto, CancellationToken ct)
    {
        if (_storage.Root == null) throw new DomainValidationException("The e-book library is not configured.") { Code = ApiErrorCodes.EbooksNotConfigured };
        var lang = (dto.Language ?? "").Trim().ToLowerInvariant();
        if (lang.Length != 2 || !lang.All(char.IsAsciiLetterLower)) throw new DomainValidationException("Unknown target language.");
        var translator = string.IsNullOrWhiteSpace(dto.Translator) ? "claude" : dto.Translator.Trim();
        if (!TranslatorName.IsMatch(translator)) throw new DomainValidationException("Translator: letters, digits and . _ - @ only, at most 80.");
        var model = EbookTranslationService.ExternalModelPrefix + translator;

        var ebook = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == dto.EbookId, ct)
                    ?? throw new NotFoundException("E-book not found.");
        if (ebook.AnonymousSessionId != null) throw new DomainValidationException("Anonymous uploads are not translated.");
        if (ebook.Language == lang) throw new DomainValidationException("The book is already in that language.");
        if (ebook.LanguageSource == EbookLanguageDetector.SourceNoText) throw new DomainValidationException("The book has no text layer (scan).");

        var row = await _db.EbookTranslations.FirstOrDefaultAsync(t => t.EbookId == ebook.Id && t.Language == lang, ct);
        if (row != null)
        {
            if (row.State == EbookTranslationState.Running && row.Model == model)
                return await InfoAsync(row, ebook, withPrompt: false, ct);   // derselbe Übersetzer noch einmal: nichts neu zerlegen
            if (row.State == EbookTranslationState.Running)
                throw new ConflictException(EbookTranslationService.IsExternalClaim(row)
                    ? "Another external translator holds this book." : "The Spark is translating this book right now.");
            if (row.State == EbookTranslationState.Done) throw new ConflictException("The book is already translated into that language.");
        }
        else
        {
            row = new EbookTranslation { EbookId = ebook.Id, Language = lang };
            _db.EbookTranslations.Add(row);
        }
        // Sofort sperren — der Worker nimmt nur Pending.
        row.State = EbookTranslationState.Running;
        row.Model = model;
        row.Priority = false;
        row.NotBefore = null;
        row.Error = null;
        row.Runs++;
        row.StartedAt = DateTime.UtcNow;
        row.CompletedAt = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var (source, outcome) = await _translations.EnsureSourceAsync(ebook, row, ct);
        if (source == null)
            throw new ConflictException(outcome switch
            {
                EbookTranslationOutcome.Waiting => "The text PDF waits for its diagram recognition; try again later.",
                EbookTranslationOutcome.NoText => "The book has no usable text (no EPUB, no text PDF).",
                _ => "The source EPUB could not be prepared.",
            }) { Details = new { state = row.State.ToString().ToLowerInvariant(), row.NotBefore } };

        EpubTranslationPlan plan;
        try
        {
            using var zip = ZipFile.OpenRead(_storage.ResolveFile(ebook, source));
            plan = EbookEpubTranslation.Plan(zip);
        }
        catch (Exception ex) when (ex is EpubTranslationException or InvalidDataException or System.Xml.XmlException or IOException)
        {
            await FailAsync(row, "The EPUB is not readable: " + ex.Message, ct);
            throw new ConflictException("The source EPUB is not readable.");
        }

        var old = await _db.EbookTranslationSegments.Where(s => s.EbookTranslationId == row.Id).ToListAsync(ct);
        _db.EbookTranslationSegments.RemoveRange(old);
        var cache = _translations.OpenCache(ebook.Id, lang);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        var prefilled = 0;
        var now = DateTime.UtcNow;
        foreach (var doc in plan.Documents)
            foreach (var seg in doc.Document.Segments)
            {
                if (!seen.Add(seg.ModelText)) continue;
                var cached = cache.Get(seg.ModelText);
                if (cached != null && EbookTextSegmenter.Apply(seg, cached) == null) cached = null;
                if (cached != null) prefilled++;
                _db.EbookTranslationSegments.Add(new EbookTranslationSegment
                {
                    EbookTranslationId = row.Id, Index = index++, DocumentPath = Clip(doc.Path, 300),
                    Source = seg.ModelText, Target = cached, UpdatedAt = now,
                });
            }
        row.SourceFileId = source.Id;
        row.SourceHash = plan.SourceHash;
        row.SegmentsTotal = index;
        row.SegmentsDone = prefilled;
        row.SegmentsFailed = 0;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("E-Book {EbookId} → {Lang}: extern beansprucht von {Translator}, {Total} Texte ({Prefilled} aus dem Zwischenspeicher)",
            ebook.Id, lang, translator, index, prefilled);

        var info = await InfoAsync(row, ebook, withPrompt: false, ct);
        if (EbookNotationLetters.For(ebook.Language, lang, plan.Documents.Select(d => d.Document.Rebuild(_ => null, out _))) != null)
            info.Notes.Insert(0, $"The book uses {ebook.Language} piece letters; they are converted to English when the EPUB is assembled. Leave every move and marker exactly as it is.");
        return info;
    }

    // ── Lesen ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Die laufenden externen Ansprüche (und beim Zusammenbauen gescheiterte).</summary>
    public async Task<List<ExternalTranslationInfoDto>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.EbookTranslations.Include(t => t.Ebook)
            .Where(t => t.Model != null && t.Model.StartsWith(EbookTranslationService.ExternalModelPrefix)
                        && (t.State == EbookTranslationState.Running || t.State == EbookTranslationState.Failed))
            .OrderBy(t => t.Id).ToListAsync(ct);
        var list = new List<ExternalTranslationInfoDto>();
        foreach (var r in rows) list.Add(await InfoAsync(r, r.Ebook, withPrompt: false, ct));
        return list;
    }

    public async Task<ExternalTranslationInfoDto> GetAsync(int id, CancellationToken ct)
    {
        var row = await RowAsync(id, ct);
        var ebook = await _db.Ebooks.FirstOrDefaultAsync(b => b.Id == row.EbookId, ct);
        return await InfoAsync(row, ebook, withPrompt: true, ct);
    }

    public async Task<ExternalTranslationSegmentPageDto> SegmentsAsync(int id, int offset, int limit, bool onlyOpen, CancellationToken ct)
    {
        await RowAsync(id, ct);
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, MaxBatch);
        var q = _db.EbookTranslationSegments.AsNoTracking().Where(s => s.EbookTranslationId == id);
        if (onlyOpen) q = q.Where(s => s.Target == null);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(s => s.Index).Skip(offset).Take(limit)
            .Select(s => new ExternalTranslationSegmentDto
            {
                Index = s.Index, Source = s.Source, Target = onlyOpen ? null : s.Target, Document = s.DocumentPath,
            }).ToListAsync(ct);
        return new ExternalTranslationSegmentPageDto { Total = total, Offset = offset, Items = items };
    }

    // ── Zurückgeben ─────────────────────────────────────────────────────────────────────────────

    public async Task<ExternalTranslationPutResultDto> PutAsync(int id, List<ExternalTranslationTargetDto>? items, CancellationToken ct)
    {
        items ??= [];
        if (items.Count > MaxBatch) throw new DomainValidationException($"At most {MaxBatch} translations per request.");
        var row = await RowAsync(id, ct);
        if (!EbookTranslationService.IsExternalClaim(row)) throw new ConflictException("This translation is not claimed externally.");
        var ebook = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == row.EbookId, ct)
                    ?? throw new NotFoundException("E-book not found.");

        var wanted = items.Select(i => i.Index).Distinct().ToList();
        var segments = await _db.EbookTranslationSegments.Where(s => s.EbookTranslationId == id && wanted.Contains(s.Index))
            .ToDictionaryAsync(s => s.Index, ct);
        var blocks = LoadBlocks(ebook, row);
        var result = new ExternalTranslationPutResultDto();
        var seen = new HashSet<int>();
        var accepted = new List<KeyValuePair<string, string>>();
        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            if (!seen.Add(item.Index)) { Reject(result, item.Index, "duplicate", "The index appears twice in this request."); continue; }
            if (!segments.TryGetValue(item.Index, out var seg)) { Reject(result, item.Index, "unknown_index", "No text with this index."); continue; }
            var target = (item.Target ?? "").Trim();
            var problem = Check(seg.Source, target, row.Language, blocks);
            if (problem != null) { Reject(result, item.Index, problem.Value.Reason, problem.Value.Message); continue; }
            seg.Target = target;
            seg.UpdatedAt = now;
            accepted.Add(new(seg.Source, target));
            result.Accepted++;
        }
        await _db.SaveChangesAsync(ct);
        // Auch in den Zwischenspeicher: gibt der Übersetzer auf, nimmt die Spark das Fertige mit.
        if (accepted.Count > 0) _translations.OpenCache(row.EbookId, row.Language).Add(accepted);
        row.SegmentsDone = await _db.EbookTranslationSegments.CountAsync(s => s.EbookTranslationId == id && s.Target != null, ct);
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        result.SegmentsDone = row.SegmentsDone;
        result.SegmentsTotal = row.SegmentsTotal;
        return result;
    }

    private static void Reject(ExternalTranslationPutResultDto r, int index, string reason, string message)
        => r.Rejected.Add(new ExternalTranslationRejectDto { Index = index, Reason = reason, Message = message });

    /// <summary>
    /// Eine Übersetzung prüfen wie eine Spark-Antwort → <c>null</c> = passt, sonst Grund. <paramref name="blocks"/>: die Blöcke
    /// des Quell-EPUB je Modelltext (für das Einsetzen samt Verschachtelung); <c>null</c> = Quelle gerade nicht lesbar, dann nur
    /// die Platzhalter-Prüfung (das Zusammenbauen prüft noch einmal).
    /// </summary>
    internal static (string Reason, string Message)? Check(string source, string target, string language,
        IReadOnlyDictionary<string, List<EbookTextSegment>>? blocks)
    {
        if (target.Length == 0) return ("empty", "The translation is empty.");
        if (!EbookTextSegmenter.SamePlaceholders(source, target))
            return ("placeholders", "Every marker <tN>, </tN>, <tN/> of the source must appear exactly once, pairs opening before closing.");
        if (blocks != null && blocks.TryGetValue(source, out var list) && list.Any(b => EbookTextSegmenter.Apply(b, target) == null))
            return ("nesting", "The markers are nested differently than in the source (a pair <tN>…</tN> must not cross another).");
        if (CommentTranslator.EndsInsideQuote(target) && !CommentTranslator.EndsInsideQuote(source))
            return ("open_quote", "The translation ends inside an open quotation.");
        if (source.Length >= CommentTranslator.MinProseCharsForLengthCheck && target.Length < source.Length * CommentTranslator.MinLengthShare)
            return ("too_short", $"The translation has {target.Length * 100 / source.Length} % of the source length — text is missing.");
        var plain = Placeholder.Replace(target, " ");
        if (plain.Length >= CommentTranslator.MinProseCharsForLengthCheck && CommentLanguage.MarkersOf(language).Length > 0)
        {
            var detected = CommentLanguage.Detect(plain);
            if (detected != null && !string.Equals(detected.Split(',')[0], language, StringComparison.Ordinal))
                return ("wrong_language", $"The translation reads as {detected}, not {language}.");
        }
        return null;
    }

    // ── Zusammenbauen ───────────────────────────────────────────────────────────────────────────

    public async Task<ExternalTranslationAssembleResultDto> AssembleAsync(int id, ExternalTranslationAssembleDto dto, CancellationToken ct)
    {
        var row = await RowAsync(id, ct);
        if (!EbookTranslationService.IsExternalClaim(row)) throw new ConflictException("This translation is not claimed externally.");
        var ebook = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == row.EbookId, ct)
                    ?? throw new NotFoundException("E-book not found.");
        var source = ebook.Files.FirstOrDefault(f => f.Id == row.SourceFileId);
        var path = source == null ? null : _storage.ResolveFile(ebook, source);
        if (path == null || !File.Exists(path))
        {
            await FailAsync(row, "The source EPUB of the claim is gone — release and claim again.", ct);
            throw new ConflictException("The source EPUB of the claim is gone — release and claim again.");
        }

        var segments = await _db.EbookTranslationSegments.Where(s => s.EbookTranslationId == id).OrderBy(s => s.Index).ToListAsync(ct);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var plan = EbookEpubTranslation.Plan(zip);
            if (plan.SourceHash != row.SourceHash)
            {
                await FailAsync(row, "The source EPUB changed since the claim — release and claim again.", ct);
                throw new ConflictException("The source EPUB changed since the claim — release and claim again.");
            }

            // Was beim Einsetzen nicht passt (sollte nach der Prüfung beim PUT nicht vorkommen), zählt wie fehlend.
            var bySource = segments.Where(s => s.Target != null).ToDictionary(s => s.Source, StringComparer.Ordinal);
            var misfit = new HashSet<EbookTranslationSegment>();
            foreach (var block in plan.Documents.SelectMany(d => d.Document.Segments))
                if (bySource.TryGetValue(block.ModelText, out var s) && EbookTextSegmenter.Apply(block, s.Target!) == null)
                    misfit.Add(s);
            var missing = segments.Count(s => s.Target == null);
            if (!dto.AllowMissing && (missing > 0 || misfit.Count > 0))
            {
                foreach (var s in misfit) { s.Target = null; s.UpdatedAt = DateTime.UtcNow; }
                row.SegmentsDone -= misfit.Count;
                row.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                throw new ConflictException($"{missing + misfit.Count} texts are still open — translate them or assemble with allowMissing.")
                {
                    Details = new { missing, misfit = misfit.Select(s => s.Index).OrderBy(i => i).Take(MaxBatch).ToList() },
                };
            }

            var map = bySource.Where(kv => !misfit.Contains(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Target!, StringComparer.Ordinal);
            var rejected = await _translations.WriteTranslatedEpubAsync(ebook, row, zip, plan, t => map.GetValueOrDefault(t));
            row.SegmentsTotal = segments.Count;
            row.SegmentsDone = map.Count;
            row.SegmentsFailed = missing + misfit.Count + rejected;
            row.Error = null;
            if (!string.IsNullOrWhiteSpace(dto.Title)) row.Title = Clip(dto.Title.Trim(), 300);
            row.CompletedAt = row.VerifiedAt = DateTime.UtcNow;
            row.UpdatedAt = DateTime.UtcNow;
            // Die Texte werden nicht mehr gebraucht: der Zwischenspeicher hält jede Übersetzung (für ein späteres Erneuern).
            _db.EbookTranslationSegments.RemoveRange(segments);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("E-Book {EbookId} → {Lang}: extern übersetzt ({Model}), {Done}/{Total} Texte, {Failed} im Original",
                ebook.Id, row.Language, row.Model, row.SegmentsDone, row.SegmentsTotal, row.SegmentsFailed);
            return new ExternalTranslationAssembleResultDto
            {
                TranslationId = row.Id, State = row.State.ToString().ToLowerInvariant(), SegmentsTotal = row.SegmentsTotal,
                SegmentsDone = row.SegmentsDone, SegmentsFailed = row.SegmentsFailed, RelativePath = row.RelativePath, SizeBytes = row.SizeBytes,
            };
        }
        catch (Exception ex) when (ex is EpubTranslationException or InvalidDataException or System.Xml.XmlException or IOException)
        {
            _logger.LogWarning(ex, "E-Book {EbookId} → {Lang}: externes Zusammenbauen gescheitert", ebook.Id, row.Language);
            await FailAsync(row, "Assembling failed: " + ex.Message, CancellationToken.None);
            throw new ConflictException("Assembling the EPUB failed — see the error of the translation.");
        }
    }

    // ── Freigeben ───────────────────────────────────────────────────────────────────────────────

    public async Task ReleaseAsync(int id, CancellationToken ct)
    {
        var row = await RowAsync(id, ct);
        if (!EbookTranslationService.IsExternalClaim(row)) throw new ConflictException("This translation is not claimed externally.");
        _db.EbookTranslationSegments.RemoveRange(await _db.EbookTranslationSegments.Where(s => s.EbookTranslationId == id).ToListAsync(ct));
        row.State = EbookTranslationState.Pending;
        row.Model = null;
        row.Error = null;
        row.NotBefore = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _signal.Wake();
        _logger.LogInformation("E-Book {EbookId} → {Lang}: externer Anspruch freigegeben", row.EbookId, row.Language);
    }

    // ── Hilfen ──────────────────────────────────────────────────────────────────────────────────

    private async Task<EbookTranslation> RowAsync(int id, CancellationToken ct)
        => await _db.EbookTranslations.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Translation not found.");

    private async Task FailAsync(EbookTranslation row, string error, CancellationToken ct)
    {
        row.State = EbookTranslationState.Failed;
        row.Error = Clip(error, 500);
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ExternalTranslationInfoDto> InfoAsync(EbookTranslation row, Ebook? ebook, bool withPrompt, CancellationToken ct)
    {
        var external = row.Model != null && row.Model.StartsWith(EbookTranslationService.ExternalModelPrefix, StringComparison.Ordinal);
        var open = await _db.EbookTranslationSegments.CountAsync(s => s.EbookTranslationId == row.Id && s.Target == null, ct);
        var dto = new ExternalTranslationInfoDto
        {
            TranslationId = row.Id, EbookId = row.EbookId, Title = ebook?.Title ?? "", Language = row.Language,
            SourceLanguage = ebook?.Language, State = row.State.ToString().ToLowerInvariant(),
            Translator = external ? row.Model![EbookTranslationService.ExternalModelPrefix.Length..] : null,
            SegmentsTotal = row.SegmentsTotal, SegmentsDone = row.SegmentsDone, SegmentsOpen = open, SegmentsFailed = row.SegmentsFailed,
            Error = row.Error, StartedAt = row.StartedAt, CompletedAt = row.CompletedAt,
        };
        if (row.State == EbookTranslationState.Running && row.SegmentsDone > 0 && open < row.SegmentsTotal)
            dto.Notes.Add($"{row.SegmentsTotal - open} of {row.SegmentsTotal} texts are already translated (earlier run or previous upload).");
        if (ebook?.Language == null) dto.Notes.Add("The source language of the book is unknown — translate from whatever language a text is in.");
        if (withPrompt)
        {
            dto.SystemPrompt = CommentTranslator.SystemPrompt(ebook?.Language, row.Language, TranslationSubject.EbookText);
            dto.Instructions = Instructions(row.Language);
        }
        return dto;
    }

    private static string Instructions(string language) => $"""
        Fetch texts with GET …/segments?offset=&limit=&onlyOpen=true (at most {MaxBatch}); every item is {"{"} index, source {"}"},
        exactly the text the Spark would get. Translate each source into {CommentTranslator.LanguageName(language)} following the
        system prompt, and send PUT …/segments with [{"{"} index, target {"}"}, …] (at most {MaxBatch} per request; sending an index
        again overwrites it). Each target is checked like a Spark answer: every marker <tN>, </tN>, <tN/> exactly once and nested
        as in the source, not empty, no open quotation at the end, at least 70 % of the source length for texts of 150+ characters,
        and long texts must read as the target language. Rejected items come back with index, reason and message — fix and resend.
        A text that needs no translation (names only) may be sent back unchanged. When all texts are done: POST …/assemble
        ({"{"} allowMissing: false, title: "<book title in the target language>" {"}"}); allowMissing: true keeps open texts in the original.
        """;

    /// <summary>Plan-Blöcke je Modelltext für die Verschachtelungsprüfung beim PUT — ein Buch auf einmal im Speicher (die Rückgaben
    /// eines Übersetzers kommen hintereinander), neu gelesen, wenn Übersetzung oder Quelle wechseln.</summary>
    private IReadOnlyDictionary<string, List<EbookTextSegment>>? LoadBlocks(Ebook ebook, EbookTranslation row)
    {
        var source = ebook.Files.FirstOrDefault(f => f.Id == row.SourceFileId);
        if (source == null || row.SourceHash == null) return null;
        string path;
        try { path = _storage.ResolveFile(ebook, source); }
        catch (Exception ex) when (ex is DomainValidationException or InvalidOperationException) { return null; }
        if (!File.Exists(path)) return null;
        var key = (row.Id, row.SourceHash, path);
        lock (BlocksGate)
            if (_blocksKey == key && _blocks != null) return _blocks;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var plan = EbookEpubTranslation.Plan(zip);
            if (plan.SourceHash != row.SourceHash) return null;
            var map = new Dictionary<string, List<EbookTextSegment>>(StringComparer.Ordinal);
            foreach (var block in plan.Documents.SelectMany(d => d.Document.Segments))
            {
                if (!map.TryGetValue(block.ModelText, out var list)) map[block.ModelText] = list = [];
                list.Add(block);
            }
            lock (BlocksGate) { _blocksKey = key; _blocks = map; }
            return map;
        }
        catch (Exception ex) when (ex is EpubTranslationException or InvalidDataException or System.Xml.XmlException or IOException)
        {
            return null;
        }
    }

    private static readonly Lock BlocksGate = new();
    private static (int, string, string)? _blocksKey;
    private static Dictionary<string, List<EbookTextSegment>>? _blocks;

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
