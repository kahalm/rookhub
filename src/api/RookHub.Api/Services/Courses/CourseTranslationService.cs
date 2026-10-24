using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Uebersetzt die Texte eines KURSES (Zug-Kommentare, Linien-Einleitung, Linien-Titel, Kapitelnamen) in
/// eine weitere Sprache — je Linie ein <see cref="CommentSet"/> mit <see cref="CommentSet.BookPuzzleId"/>.
///
/// <para><b>Die Quelle bleibt die Linie</b> (<see cref="BookPuzzle.Comment"/>, <see cref="BookPuzzle.MoveComments"/>,
/// <see cref="BookPuzzle.Title"/>, <see cref="BookPuzzle.Chapter"/>): Aufbereitung und naechtliches Aktualisieren
/// ueberschreiben genau diese Felder, einen Quell-Satz gibt es fuer Kurse nicht. Jede Uebersetzung merkt sich
/// den Fingerabdruck ihrer Vorlage (<see cref="CommentText.SourceHash"/>) — daran haengt alles Weitere:</para>
/// <list type="bullet">
/// <item><b>Nur was fehlt oder veraltet ist, geht ans Modell.</b> Ein zweiter Lauf ohne Aenderung kostet
/// keinen Aufruf; ein geaenderter Kommentar kostet genau diesen einen Text.</item>
/// <item><b>Erst nachschlagen, dann uebersetzen.</b> Derselbe Text ist oft schon uebersetzt — in einem
/// anderen Import desselben Chessable-Kurses, einer <c>_firstkey</c>-Kopie, einem gleichnamigen Kapitel.
/// Dann wird kopiert.</item>
/// <item><b>Gleicher Text, ein Aufruf.</b> In mehr als der Haelfte der Linien steht <see cref="BookPuzzle.Comment"/>
/// woertlich auch als Einleitung in <c>MoveComments[-1]</c> — ans Modell geht er einmal, beide Stellen
/// bekommen dieselbe Uebersetzung.</item>
/// <item><b>Ein Fehlschlag schreibt fuer DIESE Linie nichts</b> (wie bei Partien) — der Lauf zaehlt sie und
/// macht mit der naechsten weiter; der naechste Lauf holt sie nach.</item>
/// <item><b>Kapitelnamen in EINER Fuhre je Kurs</b>, damit dasselbe Kapitel ueberall gleich heisst; jede
/// Linie traegt die Uebersetzung ihres Kapitels danach selbst (Stelle <see cref="CourseTextSlots.Chapter"/>).</item>
/// </list>
///
/// <para>Die Linie wird OHNE <c>Book.Source</c> geladen (nur eine Projektion der Textfelder) — siehe
/// <c>BookSourceIncludeGuardTests</c>.</para>
///
/// <para>Parallel: <c>CourseTranslation:Parallel</c> (Vorgabe <see cref="DefaultParallel"/>) Linien gleichzeitig,
/// je mit eigenem Scope und damit eigenem <see cref="AppDbContext"/>. vLLM buendelt gleichzeitige Anfragen,
/// eine einzelne nutzt nur einen Bruchteil des Durchsatzes.</para>
/// </summary>
public class CourseTranslationService
{
    /// <summary>So viele Linien gleichzeitig, wenn <c>CourseTranslation:Parallel</c> nichts sagt.</summary>
    public const int DefaultParallel = 4;

    /// <summary>Alle so viele Linien geht ein Zwischenstand an den Aufrufer.</summary>
    public const int ProgressEvery = 10;

    /// <summary>Stichprobe fuer die Quellsprache: die ersten so vielen kommentierten Linien …</summary>
    public const int LanguageSampleLines = 200;

    /// <summary>… und hoechstens so viele Zeichen daraus.</summary>
    public const int LanguageSampleChars = 20_000;

    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CourseTranslationService> _logger;
    private readonly CommentTranslator _translator;
    private readonly int _parallel;

    public CourseTranslationService(AppDbContext db, IClaudeJsonClient llm, IServiceScopeFactory scopes,
        IConfiguration config, ILogger<CourseTranslationService> logger)
    {
        _db = db;
        _scopes = scopes;
        _logger = logger;
        _translator = new CommentTranslator(llm, logger);
        _parallel = Math.Clamp(config.GetValue("CourseTranslation:Parallel", DefaultParallel), 1, 64);
    }

    /// <summary>Ein Text-Modell ist konfiguriert.</summary>
    public bool IsAvailable => _translator.IsAvailable;

    /// <summary>Womit uebersetzt wird — steht am Satz.</summary>
    public string ModelName => _translator.ModelName;

    // ── Quelle ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die uebersetzbaren Texte einer Linie, je Stelle (<see cref="CourseTextSlots"/>): <c>MoveComments</c>
    /// (-1 und ab 0), <c>Comment</c> → -2, <c>Title</c> → -3, <c>Chapter</c> → -4. Leere Texte fallen weg.
    /// Dieselbe Rechnung benutzen Lauf, Linie und die Vorauswahl der offenen Arbeit.
    /// </summary>
    public static Dictionary<int, CourseLineSlot> SourceSlots(string? title, string? chapter, string? comment,
        string? moveCommentsJson)
    {
        var slots = new Dictionary<int, CourseLineSlot>();
        void Add(int slot, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var text = CourseTextHash.Normalize(raw);
            if (text.Length > 0) slots[slot] = new CourseLineSlot(slot, text, CourseTextHash.Of(text));
        }
        foreach (var (ply, text) in BookPuzzleService.ParseMoveComments(moveCommentsJson) ?? new Dictionary<int, string>())
            if (ply >= CourseTextSlots.Intro) Add(ply, text);
        Add(CourseTextSlots.Comment, comment);
        Add(CourseTextSlots.Title, title);
        Add(CourseTextSlots.Chapter, chapter);
        return slots;
    }

    /// <summary>
    /// Die Quellsprache des Kurses (<see cref="Book.CommentLanguage"/>) — beim ersten Mal ueber eine Stichprobe
    /// bestimmt (die ersten <see cref="LanguageSampleLines"/> kommentierten Linien in Kursreihenfolge, hoechstens
    /// <see cref="LanguageSampleChars"/> Zeichen) und festgehalten; nicht bestimmbar → <c>"und"</c>.
    /// </summary>
    /// <returns><c>null</c>, wenn es das Buch nicht gibt.</returns>
    public async Task<string?> EnsureSourceLanguageAsync(int bookId, CancellationToken ct = default)
    {
        // Getrackt, aber OHNE Source (Tabellensplitting): geschrieben wird nur CommentLanguage.
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct);
        if (book is null) return null;
        if (!string.IsNullOrWhiteSpace(book.CommentLanguage)) return book.CommentLanguage;

        var sample = await _db.BookPuzzles.AsNoTracking()
            .Where(bp => bp.BookId == bookId && (bp.Comment != null || bp.MoveComments != null))
            .InReadingOrder()
            .Take(LanguageSampleLines)
            .Select(bp => new { bp.Comment, bp.MoveComments })
            .ToListAsync(ct);
        var lang = DetectLanguage(sample.SelectMany(s =>
            new[] { s.Comment }.Concat(
                (IEnumerable<string?>?)BookPuzzleService.ParseMoveComments(s.MoveComments)?.Values ?? [])));

        // Ueber den Entry gesetzt, nicht ueber die Eigenschaft: so ist die Spalte auch dann als geaendert
        // markiert, wenn der Kontext ohne automatische Aenderungserkennung laeuft (tools/LibraryImport).
        _db.Entry(book).Property(b => b.CommentLanguage).CurrentValue = lang;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Kurs {BookId}: Quellsprache der Kommentare {Lang}.", bookId, lang);
        return lang;
    }

    /// <summary>Die Sprache einer Menge Kurs-Texte: die ERSTE, die <see cref="CommentLanguage.Detect"/> nennt
    /// (ein Kurs hat EINE Quellsprache), sonst <c>"und"</c>.</summary>
    public static string DetectLanguage(IEnumerable<string?> texts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (sb.Length + t.Length + 1 > LanguageSampleChars)
            {
                sb.Append(t, 0, Math.Max(0, LanguageSampleChars - sb.Length));
                break;
            }
            sb.Append(t).Append(' ');
        }
        var detected = CommentLanguage.Detect(sb.ToString());
        return detected?.Split(',')[0] ?? "und";
    }

    // ── Eine Linie ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uebersetzt EINE Linie nach <paramref name="target"/> — nur, was fehlt oder veraltet ist. Ohne das
    /// Kapitel-Woerterbuch eines Kurs-Laufs geht der Kapitelname mit den uebrigen Texten der Linie.
    /// </summary>
    public async Task<LineTranslationResult> TranslateLineAsync(int bookPuzzleId, string target,
        CancellationToken ct = default)
    {
        var to = CourseCommentLocalizer.NormalizeLanguage(target);
        if (to is null) return new LineTranslationResult(LineTranslationStatus.NotFound);
        var bookId = await _db.BookPuzzles.AsNoTracking()
            .Where(bp => bp.Id == bookPuzzleId).Select(bp => (int?)(bp.BookId ?? 0)).FirstOrDefaultAsync(ct);
        if (bookId is null) return new LineTranslationResult(LineTranslationStatus.NotFound);
        var source = bookId > 0 ? await EnsureSourceLanguageAsync(bookId.Value, ct) ?? "und" : "und";
        if (source == to) return new LineTranslationResult(LineTranslationStatus.SameLanguage);
        return await TranslateLineCoreAsync(bookPuzzleId, to, source, chapterByHash: null, ct);
    }

    /// <summary>Der Kern je Linie — in einem Kurs-Lauf je Linie in einem EIGENEN Scope aufgerufen.</summary>
    /// <param name="chapterByHash">Kapitel-Woerterbuch des Laufs (Fingerabdruck → Uebersetzung). Gesetzt, kommt
    /// der Kapitelname NUR daraus; fehlt er dort, bleibt die Stelle offen.</param>
    /// <param name="ownedHashes">Phase 1 des Kurs-Laufs: nur Stellen mit diesen Fingerabdruecken werden angefasst,
    /// die uebrigen gehoeren einer anderen Linie und bleiben offen (<see cref="LineTranslationResult.Deferred"/>).
    /// <c>null</c> = alle.</param>
    internal async Task<LineTranslationResult> TranslateLineCoreAsync(int lineId, string target, string source,
        IReadOnlyDictionary<string, string>? chapterByHash, CancellationToken ct,
        IReadOnlySet<string>? ownedHashes = null)
    {
        // Schritt 1: Linie und vorhandenen Satz laden, offene Stellen und Reste bestimmen.
        var work = await LoadLineWorkAsync(lineId, target, ct);
        if (work is null) return new LineTranslationResult(LineTranslationStatus.NotFound);
        var kept = work.Slots.Count - work.Open.Count;
        if (work.Open.Count == 0 && work.Orphans.Count == 0)
            return new LineTranslationResult(LineTranslationStatus.NothingToDo, Kept: kept);

        // Schritt 2: nachschlagen — Kapitel-Woerterbuch, Besitz in Phase 1, Wiederverwendung per Fingerabdruck.
        var assigned = new Dictionary<int, string>();
        var open = work.Open;
        var (reused, pending) = TakeChapterFromDictionary(open, chapterByHash, assigned);
        var deferred = 0;
        if (ownedHashes is not null) (open, deferred) = SplitOwned(open, ownedHashes);
        var (reusedTexts, toModel) = await AssignReusableAsync(open, target, assigned, ct);
        reused += reusedTexts;

        // Schritt 3: der Rest geht ans Modell.
        var translated = 0;
        if (toModel.Count > 0)
        {
            if (!_translator.IsAvailable) return new LineTranslationResult(LineTranslationStatus.NotConfigured);
            var modelCount = await TranslateGroupsAsync(toModel, source, target, lineId, assigned, ct);
            if (modelCount is null) return new LineTranslationResult(LineTranslationStatus.Failed);
            translated = modelCount.Value;
        }

        // Abgebrochen (Sperrzeit, Dienst stoppt)? Dann wird nichts mehr geschrieben — der naechste Lauf
        // ueberspringt ohnehin alles, was schon fertig ist.
        ct.ThrowIfCancellationRequested();

        // Nichts zu schreiben (etwa nur ein Kapitelname offen, den das Woerterbuch des Laufs nicht hat).
        if (assigned.Count == 0 && work.Orphans.Count == 0)
            return new LineTranslationResult(LineTranslationStatus.NothingToDo, kept, reused, translated, 0, pending,
                deferred);

        // Schritt 4: schreiben.
        if (!await WriteLineAsync(work, lineId, target, source, assigned, translated, ct))
            return new LineTranslationResult(LineTranslationStatus.Failed);
        return new LineTranslationResult(LineTranslationStatus.Written, kept, reused, translated, work.Orphans.Count,
            pending, deferred);
    }

    /// <summary>Eine Linie im Uebersetzungs-Kern: ihre Quell-Stellen, der vorhandene Satz in der Zielsprache
    /// (getrackt) mit seinen Texten je Stelle, die offenen Stellen (fehlend/veraltet) und die Reste
    /// verschwundener Vorlagen.</summary>
    internal sealed record LineWork(Dictionary<int, CourseLineSlot> Slots, CommentSet? Set,
        Dictionary<int, CommentText> Existing, List<CourseLineSlot> Open, List<CommentText> Orphans);

    /// <summary>Schritt 1: Linie (nur die Textfelder, ohne <c>Book.Source</c>) und vorhandenen Satz laden.
    /// <c>null</c> = die Linie gibt es nicht.</summary>
    private async Task<LineWork?> LoadLineWorkAsync(int lineId, string target, CancellationToken ct)
    {
        var line = await _db.BookPuzzles.AsNoTracking()
            .Where(bp => bp.Id == lineId)
            .Select(bp => new { bp.Title, bp.Chapter, bp.Comment, bp.MoveComments })
            .FirstOrDefaultAsync(ct);
        if (line is null) return null;
        var slots = SourceSlots(line.Title, line.Chapter, line.Comment, line.MoveComments);

        var set = await _db.CommentSets.Include(s => s.Texts)
            .FirstOrDefaultAsync(s => s.BookPuzzleId == lineId && s.Language == target, ct);
        var existing = set?.Texts.GroupBy(t => t.Ply).ToDictionary(g => g.Key, g => g.First())
                       ?? new Dictionary<int, CommentText>();
        var (open, orphans) = OpenSlots(slots, existing);
        return new LineWork(slots, set, existing, open, orphans);
    }

    /// <summary>Offen = Stellen ohne Uebersetzung oder mit veraltetem Fingerabdruck; Reste = vorhandene Texte,
    /// deren Stelle es in der Quelle nicht mehr gibt. Rein.</summary>
    internal static (List<CourseLineSlot> Open, List<CommentText> Orphans) OpenSlots(
        IReadOnlyDictionary<int, CourseLineSlot> slots, IReadOnlyDictionary<int, CommentText> existing)
    {
        var open = slots.Values
            .Where(s => !(existing.TryGetValue(s.Slot, out var have) && have.SourceHash == s.Hash))
            .ToList();
        var orphans = existing.Values.Where(t => !slots.ContainsKey(t.Ply)).ToList();
        return (open, orphans);
    }

    /// <summary>
    /// Kapitelname aus dem Woerterbuch des Laufs — dort ist er fuer den ganzen Kurs EINMAL uebersetzt. Nimmt
    /// die offene Kapitel-Stelle aus <paramref name="open"/> heraus (nur mit Woerterbuch) und traegt den Treffer
    /// in <paramref name="assigned"/> ein; fehlt er im Woerterbuch, bleibt sie offen (<c>Pending</c>). Rein.
    /// </summary>
    internal static (int Reused, int Pending) TakeChapterFromDictionary(List<CourseLineSlot> open,
        IReadOnlyDictionary<string, string>? chapterByHash, Dictionary<int, string> assigned)
    {
        if (chapterByHash is null || open.FirstOrDefault(s => s.Slot == CourseTextSlots.Chapter) is not { } chapter)
            return (0, 0);
        open.Remove(chapter);
        if (!chapterByHash.TryGetValue(chapter.Hash, out var label)) return (0, 1);
        assigned[chapter.Slot] = label;
        return (1, 0);
    }

    /// <summary>
    /// Phase 1 des Kurs-Laufs: was einer anderen Linie gehoert, bleibt fuer Phase 2 offen — so geht jeder
    /// verschiedene Text je Lauf genau EINMAL ans Modell, auch wenn Nachbarlinien gleichzeitig laufen. Rein.
    /// </summary>
    internal static (List<CourseLineSlot> Open, int Deferred) SplitOwned(List<CourseLineSlot> open,
        IReadOnlySet<string> ownedHashes)
        => (open.Where(s => ownedHashes.Contains(s.Hash)).ToList(), open.Count(s => !ownedHashes.Contains(s.Hash)));

    /// <summary>Gleiche Texte zusammenfassen und Vorhandenes per Fingerabdruck wiederverwenden. Gibt die Zahl der
    /// so belegten Stellen und die Gruppen zurueck, die ans Modell muessen.</summary>
    private async Task<(int Reused, List<IGrouping<string, CourseLineSlot>> ToModel)> AssignReusableAsync(
        List<CourseLineSlot> open, string target, Dictionary<int, string> assigned, CancellationToken ct)
    {
        var groups = open.GroupBy(s => s.Hash).ToList();
        var reusable = await ReusableAsync(groups.Select(g => g.Key).ToList(), target, ct);
        var reused = 0;
        var toModel = new List<IGrouping<string, CourseLineSlot>>();
        foreach (var g in groups)
        {
            if (reusable.TryGetValue(g.Key, out var text))
            {
                foreach (var s in g) assigned[s.Slot] = text;
                reused += g.Count();
            }
            else toModel.Add(g);
        }
        return (reused, toModel);
    }

    /// <summary>Schritt 3: je gleichem Text EIN Eintrag ans Modell; die Antwort gilt fuer alle Stellen der Gruppe.
    /// <c>null</c> = das Modell hat abgebrochen/verworfen.</summary>
    private async Task<int?> TranslateGroupsAsync(List<IGrouping<string, CourseLineSlot>> toModel, string source,
        string target, int lineId, Dictionary<int, string> assigned, CancellationToken ct)
    {
        var items = toModel.Select(g => (Key: g.Max(s => s.Slot), g.First().Text)).OrderBy(i => i.Key).ToList();
        var result = await _translator.TranslateAsync(items, source, target, TranslationSubject.CourseLine,
            lineId, ct);
        if (result is null) return null;
        var translated = 0;
        foreach (var g in toModel)
        {
            var text = result[g.Max(s => s.Slot)];
            foreach (var s in g) assigned[s.Slot] = text;
            translated += g.Count();
        }
        return translated;
    }

    /// <summary>Schritt 4: Satz anlegen bzw. Texte aktualisieren, Reste entfernen, speichern. <c>false</c> =
    /// Schreibkonflikt, nichts geschrieben.</summary>
    private async Task<bool> WriteLineAsync(LineWork work, int lineId, string target, string source,
        Dictionary<int, string> assigned, int translated, CancellationToken ct)
    {
        var (slots, set) = (work.Slots, work.Set);
        var now = DateTime.UtcNow;
        if (set is null)
        {
            set = new CommentSet
            {
                BookPuzzleId = lineId,
                Language = target,
                Origin = CommentOrigin.Machine,
                TranslatedFrom = source,
                Model = ModelName,
                Status = CommentSetStatus.Ready,
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var (slot, text) in assigned.OrderBy(a => a.Key))
                set.Texts.Add(new CommentText { Ply = slot, Text = text, SourceHash = slots[slot].Hash });
            _db.CommentSets.Add(set);
        }
        else UpdateSet(set, work, source, assigned, translated, now);

        // Ausdruecklich: laeuft der Kontext ohne automatische Aenderungserkennung, saehe SaveChanges die
        // geaenderten Texte sonst nicht.
        _db.ChangeTracker.DetectChanges();
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // Zwei Laeufe auf derselben Linie (eindeutiger Index (BookPuzzleId, Language)) — der Verlierer
            // schreibt nichts; der naechste Lauf sieht den Satz des Gewinners.
            _logger.LogWarning(ex, "Kurs-Linie {Id} nach {Lang}: Schreibkonflikt, nichts geschrieben.", lineId, target);
            return false;
        }
    }

    /// <summary>Vorhandenen Satz nachziehen: Texte aktualisieren/ergaenzen, Reste entfernen; bleibt nichts uebrig,
    /// geht der Satz ganz. Nur im Change-Tracker — gespeichert wird in <see cref="WriteLineAsync"/>.</summary>
    private void UpdateSet(CommentSet set, LineWork work, string source, Dictionary<int, string> assigned,
        int translated, DateTime now)
    {
        var (slots, existing, orphans) = (work.Slots, work.Existing, work.Orphans);
        foreach (var (slot, text) in assigned)
        {
            if (existing.TryGetValue(slot, out var row))
            {
                row.Text = text;
                row.SourceHash = slots[slot].Hash;
            }
            else
                _db.CommentTexts.Add(new CommentText
                {
                    CommentSetId = set.Id, Ply = slot, Text = text, SourceHash = slots[slot].Hash,
                });
        }
        // Vorlage verschwunden → Uebersetzung weg. Bleibt vom Satz nichts uebrig, geht er ganz.
        _db.CommentTexts.RemoveRange(orphans);
        var remaining = existing.Count - orphans.Count + assigned.Keys.Count(k => !existing.ContainsKey(k));
        if (remaining == 0) _db.CommentSets.Remove(set);
        else
        {
            set.UpdatedAt = now;
            set.TranslatedFrom = source;
            if (translated > 0) set.Model = ModelName;
        }
    }

    /// <summary>
    /// Vorhandene Uebersetzungen je Fingerabdruck in der Zielsprache (nur Kurs-Saetze) — die JUENGSTE je Text.
    /// Zwei Schritte, damit ein haeufiger Text (ein Kapitelname in 2 000 Linien eines Doppel-Imports) nicht
    /// 2 000-mal gelesen wird: erst die Id je Fingerabdruck, dann genau diese Zeilen.
    /// </summary>
    private async Task<Dictionary<string, string>> ReusableAsync(IReadOnlyCollection<string> hashes, string target,
        CancellationToken ct)
    {
        if (hashes.Count == 0) return new Dictionary<string, string>();
        var ids = await _db.CommentTexts.AsNoTracking()
            .Where(t => t.SourceHash != null && hashes.Contains(t.SourceHash)
                        && t.CommentSet!.BookPuzzleId != null && t.CommentSet.Language == target)
            .GroupBy(t => t.SourceHash)
            .Select(g => g.Max(t => t.Id))
            .ToListAsync(ct);
        if (ids.Count == 0) return new Dictionary<string, string>();
        var rows = await _db.CommentTexts.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.SourceHash, t.Text })
            .ToListAsync(ct);
        return rows.Where(r => r.SourceHash != null)
            .GroupBy(r => r.SourceHash!)
            .ToDictionary(g => g.Key, g => g.First().Text);
    }

    // ── Ein Kurs ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uebersetzt einen ganzen Kurs nach <paramref name="target"/>: (1) Quellsprache bestimmen, falls noch
    /// offen — Ziel = Quelle → nichts zu tun; (2) die offene Arbeit bestimmen (Linien, deren Satz fehlt, einen
    /// veralteten/fehlenden Text hat oder Reste einer verschwundenen Vorlage traegt); (3) die dabei fehlenden
    /// Kapitelnamen in EINER Fuhre uebersetzen (Wiederverwendung zuerst); (4) die Linien in Kursreihenfolge,
    /// <c>CourseTranslation:Parallel</c> gleichzeitig, in ZWEI Phasen; (5) zu Beginn und alle <see cref="ProgressEvery"/>
    /// Linien einen Zwischenstand melden.
    ///
    /// <para><b>Zwei Phasen, damit jeder verschiedene Text je Lauf genau EINMAL ans Modell geht.</b> Linien mit
    /// gemeinsamem Zuganfang tragen dieselben Kommentare und liegen in Kursreihenfolge direkt nebeneinander — sie
    /// landen also gleichzeitig im Parallel-Fenster, und die Wiederverwendung greift erst nach dem Speichern. Auf
    /// Prod nachgerechnet (Parallel 4): Buch 36 hat 1,90 Mio. Zeichen, davon nur 0,25 Mio. verschieden — ohne
    /// Phasen gingen 0,36 Mio. ans Modell. Phase 1: jeder offene Fingerabdruck GEHOERT der ersten Linie (in
    /// Kursreihenfolge), in der er offen ist; jede Linie uebersetzt nur ihre eigenen, die uebrigen Stellen bleiben
    /// offen — parallel ueberschneidungsfrei. Phase 2: alle Linien mit uebrig gebliebenen offenen Stellen normal;
    /// das ist praktisch nur Wiederverwendung, ein Modellaufruf faellt nur noch an, wo die Besitzer-Linie in
    /// Phase 1 gescheitert ist.</para>
    ///
    /// <para><b>Zaehlung</b>: eine Linie zaehlt, sobald sie fertig ist — in Phase 1, wenn danach nichts mehr offen
    /// ist, sonst mit ihrem Ergebnis in Phase 2. Am Ende gilt <c>LinesDone + LinesFailed = LinesTotal</c>.</para>
    ///
    /// <para><b>Abbruch</b> ueber <paramref name="ct"/>: laufende Linien werden verworfen (nichts geschrieben),
    /// es fliegt eine <see cref="OperationCanceledException"/>. Weil alles inkrementell ist, ueberspringt der
    /// naechste Lauf das Fertige.</para>
    /// </summary>
    /// <param name="parallel">Ueberschreibt <c>CourseTranslation:Parallel</c> (Werkzeug: <c>--parallel</c>).</param>
    public async Task<CourseTranslationRun> TranslateCourseAsync(int bookId, string target,
        Func<CourseTranslationProgress, CancellationToken, Task>? onProgress = null, int? parallel = null,
        CancellationToken ct = default)
    {
        var to = CourseCommentLocalizer.NormalizeLanguage(target);
        if (to is null) return new CourseTranslationRun(CourseTranslationRunStatus.InvalidLanguage, null);
        if (!_translator.IsAvailable) return new CourseTranslationRun(CourseTranslationRunStatus.NotConfigured, null);

        var source = await EnsureSourceLanguageAsync(bookId, ct);
        if (source is null) return new CourseTranslationRun(CourseTranslationRunStatus.NotFound, null);
        if (source == to) return new CourseTranslationRun(CourseTranslationRunStatus.SameLanguage, source);

        var (open, neededChapters) = await OpenWorkAsync(bookId, to, ct);
        if (open.Count == 0) return new CourseTranslationRun(CourseTranslationRunStatus.NothingToDo, source);

        var chapters = await ChapterDictionaryAsync(bookId, source, to, neededChapters, ct);
        var chaptersMissing = neededChapters.Keys.Count(h => !chapters.ContainsKey(h));

        // Ist das Modell weg (Verbindung abgelehnt, Strom verstummt), haelt der Lauf an: am 30.09.2026 verbuchte ein
        // Auftrag in zehn Minuten Spark-Ausfall 252 von 264 Linien als gescheitert und galt danach als fertig.
        using var reach = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var unreachable = false;
        async Task<bool> ModelGoneAsync(LineTranslationResult result)
        {
            if (!IsFailure(result) || !_translator.IsUnreachable) return false;
            unreachable = true;
            await reach.CancelAsync();
            return true;
        }
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(parallel ?? _parallel, 1, 64),
            CancellationToken = reach.Token,
        };
        _logger.LogInformation(
            "Kurs {BookId} nach {Lang}: {Lines} Linien offen, {Chapters} Kapitelnamen ({Missing} ohne Uebersetzung), {Parallel} parallel.",
            bookId, to, open.Count, neededChapters.Count, chaptersMissing, options.MaxDegreeOfParallelism);

        var tally = new RunTally(open.Count, onProgress);
        // Gleich zu Beginn ein Zwischenstand: der Auftrag nennt die Zahl der offenen Linien, bevor die erste fertig ist.
        await tally.ReportAsync(ct);

        try
        {
            await RunPhasesAsync(bookId, to, source, open, chapters, options, tally, ModelGoneAsync);
        }
        catch (OperationCanceledException) when (unreachable && !ct.IsCancellationRequested)
        {
            // Der eigene Abbruch: unten als „nicht erreichbar" melden.
        }
        if (unreachable)
        {
            _logger.LogWarning(
                "Kurs {BookId} nach {Lang}: Modell nicht erreichbar — angehalten nach {Done} von {Lines} Linien, nichts gilt als gescheitert.",
                bookId, to, tally.Done, open.Count);
            return new CourseTranslationRun(CourseTranslationRunStatus.Unreachable, source, open.Count, tally.Done, 0,
                neededChapters.Count, chaptersMissing);
        }

        // Wer weder fertig gezaehlt noch in Phase 2 offen war (etwa eine inzwischen geloeschte Linie), hat nichts mehr zu tun.
        foreach (var line in open)
            if (!tally.IsCounted(line.Id)) await tally.CountAsync(line.Id, ok: true, ct);

        await tally.ReportAsync(ct);
        _logger.LogInformation("Kurs {BookId} nach {Lang}: {Done} Linien fertig, {Failed} gescheitert.",
            bookId, to, tally.Done, tally.Failed);
        return new CourseTranslationRun(CourseTranslationRunStatus.Done, source, open.Count, tally.Done, tally.Failed,
            neededChapters.Count, chaptersMissing);
    }

    /// <summary>
    /// Phase 1: jeder offene Fingerabdruck gehoert der ERSTEN Linie (Kursreihenfolge), in der er offen ist; je
    /// Linie die Fingerabdruecke, die ihr gehoeren (Linien ohne eigenen fallen weg). Rein.
    /// </summary>
    internal static List<(int Id, IReadOnlySet<string> Owned)> PlanPhase1(IReadOnlyList<OpenLine> open)
    {
        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in open)
            foreach (var hash in line.OpenHashes)
                owners.TryAdd(hash, line.Id);
        return open
            .Select(l => (l.Id, Owned: (IReadOnlySet<string>)l.OpenHashes.Where(h => owners[h] == l.Id)
                .ToHashSet(StringComparer.Ordinal)))
            .Where(x => x.Owned.Count > 0)
            .ToList();
    }

    /// <summary>Die beiden Phasen eines Kurs-Laufs (siehe <see cref="TranslateCourseAsync"/>), parallel nach
    /// <paramref name="options"/>. <paramref name="modelGoneAsync"/> meldet, ob das Modell weg ist (dann bricht er den
    /// Lauf ueber das Token der Optionen ab und die Linie zaehlt nicht).</summary>
    private async Task RunPhasesAsync(int bookId, string to, string source, List<OpenLine> open,
        IReadOnlyDictionary<string, string> chapters, ParallelOptions options, RunTally tally,
        Func<LineTranslationResult, Task<bool>> modelGoneAsync)
    {
        var phase1 = PlanPhase1(open);
        await Parallel.ForEachAsync(phase1, options, async (item, token) =>
        {
            var result = await RunLineAsync(item.Id, to, source, chapters, item.Owned, token);
            if (await modelGoneAsync(result)) return;
            // Fertig ist die Linie nur, wenn nichts mehr offen ist; gescheiterte und halbe gehen in Phase 2.
            if (!IsFailure(result) && result.Remaining == 0) await tally.CountAsync(item.Id, ok: true, token);
        });

        // Phase 2: was noch offen ist — nach Phase 1 fast nur noch Wiederverwendung.
        var initial = open.Select(l => l.Id).ToHashSet();
        var (stillOpen, _) = await OpenWorkAsync(bookId, to, options.CancellationToken);
        var phase2 = stillOpen.Select(l => l.Id).Where(id => initial.Contains(id) && !tally.IsCounted(id)).ToList();
        await Parallel.ForEachAsync(phase2, options, async (lineId, token) =>
        {
            var result = await RunLineAsync(lineId, to, source, chapters, null, token);
            if (await modelGoneAsync(result)) return;
            await tally.CountAsync(lineId, ok: !IsFailure(result), token);
        });
    }

    private static bool IsFailure(LineTranslationResult r)
        => r.Status is LineTranslationStatus.Failed or LineTranslationStatus.NotConfigured;

    /// <summary>Eine Linie in einem EIGENEN Scope (eigener DbContext). Ein Fehler beendet den Lauf nicht; ein Abbruch schon.</summary>
    private async Task<LineTranslationResult> RunLineAsync(int lineId, string target, string source,
        IReadOnlyDictionary<string, string> chapters, IReadOnlySet<string>? owned, CancellationToken token)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<CourseTranslationService>();
            return await service.TranslateLineCoreAsync(lineId, target, source, chapters, token, owned);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Eine Linie darf den Lauf nicht beenden (Datenbank kurz weg, kaputte Zeile).
            _logger.LogWarning(ex, "Kurs-Linie {Id} nach {Lang}: Fehler, weiter mit der naechsten.", lineId, target);
            return new LineTranslationResult(LineTranslationStatus.Failed);
        }
    }

    /// <summary>Zaehlt jede Linie genau einmal (fertig oder gescheitert) und meldet alle <see cref="ProgressEvery"/>
    /// Linien einen Zwischenstand — die Meldungen laufen nacheinander, auch wenn die Linien parallel fertig werden.</summary>
    private sealed class RunTally(int total, Func<CourseTranslationProgress, CancellationToken, Task>? onProgress)
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _counted = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _done, _failed, _processed;

        public int Done => Volatile.Read(ref _done);
        public int Failed => Volatile.Read(ref _failed);
        public bool IsCounted(int lineId) => _counted.ContainsKey(lineId);

        public async Task CountAsync(int lineId, bool ok, CancellationToken ct)
        {
            if (!_counted.TryAdd(lineId, 0)) return;
            if (ok) Interlocked.Increment(ref _done);
            else Interlocked.Increment(ref _failed);
            if (Interlocked.Increment(ref _processed) % ProgressEvery == 0) await ReportAsync(ct);
        }

        public async Task ReportAsync(CancellationToken ct)
        {
            if (onProgress is null) return;
            await _gate.WaitAsync(ct);
            try { await onProgress(new CourseTranslationProgress(total, Done, Failed), ct); }
            finally { _gate.Release(); }
        }
    }

    /// <summary>
    /// Die offene Arbeit eines Kurses in Kursreihenfolge — der Fingerabdruck wird in C# verglichen (je Kurs
    /// hoechstens ein paar tausend Linien). Je Linie die Fingerabdruecke ihrer offenen Stellen (ohne Kapitel), dazu
    /// die Kapitelnamen (Fingerabdruck → Text), deren Stelle in mindestens einer dieser Linien offen ist.
    /// </summary>
    internal async Task<(List<OpenLine> Lines, Dictionary<string, string> Chapters)> OpenWorkAsync(int bookId,
        string target, CancellationToken ct)
    {
        var lines = await _db.BookPuzzles.AsNoTracking()
            .Where(bp => bp.BookId == bookId)
            .InReadingOrder()
            .Select(bp => new { bp.Id, bp.Title, bp.Chapter, bp.Comment, bp.MoveComments })
            .ToListAsync(ct);
        var have = (await _db.CommentTexts.AsNoTracking()
                .Where(t => t.CommentSet!.Language == target && t.CommentSet.BookPuzzle!.BookId == bookId)
                .Select(t => new { LineId = t.CommentSet!.BookPuzzleId!.Value, t.Ply, t.SourceHash })
                .ToListAsync(ct))
            .GroupBy(t => t.LineId)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Ply).ToDictionary(x => x.Key, x => x.First().SourceHash));

        var open = new List<OpenLine>();
        var chapters = new Dictionary<string, string>();
        foreach (var line in lines)
        {
            var slots = SourceSlots(line.Title, line.Chapter, line.Comment, line.MoveComments);
            have.TryGetValue(line.Id, out var mine);
            bool Fresh(CourseLineSlot s) => mine is not null && mine.TryGetValue(s.Slot, out var h) && h == s.Hash;
            var stale = slots.Values.Where(s => !Fresh(s)).ToList();
            var orphan = mine is not null && mine.Keys.Any(p => !slots.ContainsKey(p));
            if (stale.Count == 0 && !orphan) continue;
            open.Add(new OpenLine(line.Id, stale
                .Where(s => s.Slot != CourseTextSlots.Chapter)
                .Select(s => s.Hash)
                .Distinct(StringComparer.Ordinal)
                .ToList()));
            if (stale.FirstOrDefault(s => s.Slot == CourseTextSlots.Chapter) is { } chapter)
                chapters.TryAdd(chapter.Hash, chapter.Text);
        }
        return (open, chapters);
    }

    /// <summary>
    /// Das Kapitel-Woerterbuch eines Laufs (Fingerabdruck → Uebersetzung): erst nachschlagen, der Rest in EINER
    /// Fuhre ans Modell — so heisst dasselbe Kapitel im ganzen Kurs gleich. Scheitert die Fuhre, fehlen diese
    /// Kapitel im Woerterbuch; die Linien werden trotzdem uebersetzt, ihr Kapitel-Slot bleibt offen.
    /// </summary>
    private async Task<Dictionary<string, string>> ChapterDictionaryAsync(int bookId, string source, string target,
        IReadOnlyDictionary<string, string> needed, CancellationToken ct)
    {
        var byHash = await ReusableAsync(needed.Keys.ToList(), target, ct);
        var missing = needed.Where(n => !byHash.ContainsKey(n.Key)).ToList();
        if (missing.Count == 0) return byHash;

        var items = missing.Select((m, i) => (Key: i, m.Value)).ToList();
        var translated = await _translator.TranslateAsync(items, source, target, TranslationSubject.CourseChapters,
            bookId, ct);
        if (translated is not null)
            foreach (var (i, text) in translated)
                if (i >= 0 && i < missing.Count) byHash[missing[i].Key] = text;
        return byHash;
    }
}
