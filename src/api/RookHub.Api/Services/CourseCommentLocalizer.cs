using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Liefert Kurs-Inhalte in einer gewuenschten Sprache aus (<c>?lang=</c>): ersetzt in fertigen DTOs die
/// Kommentare durch die Uebersetzung (<see cref="CommentSet.BookPuzzleId"/>) — aber NUR, wo der
/// Fingerabdruck der Uebersetzung (<see cref="CommentText.SourceHash"/>) zum Originaltext IM DTO passt.
/// Ist der Kommentar seit der Uebersetzung geaendert worden, bleibt das Original stehen: lieber Englisch
/// als eine Uebersetzung von etwas, das nicht mehr dasteht.
///
/// <para><b><c>lang</c> fehlt oder ist unbrauchbar → das DTO bleibt EXAKT, wie es war</b> (keine Abfrage,
/// kein Feld gesetzt): alte Clients, Offline-Kopien, Wochenpost, Tagespuzzle.</para>
///
/// <para><b>Titel und Kapitel werden NICHT ersetzt.</b> Der Kapitelname ist im Frontend ein SCHLUESSEL
/// (<c>?chapter=</c>, Kapitel-PGN, Umbenennen, Gruppieren); die Uebersetzung kommt deshalb als eigenes Feld
/// (<c>TitleLabel</c>/<c>ChapterLabel</c>/<c>Label</c>, <c>null</c> = keine), angezeigt wird
/// <c>label ?? name</c>.</para>
///
/// <para><b>Die Quellsprache selbst</b> (<see cref="Book.CommentLanguage"/>) als <c>lang</c> liefert immer das
/// Original — auch wenn es (nach einer Korrektur der Quellsprache) einen Satz dieser Sprache gibt.</para>
///
/// <para>Abfragen: je Aufruf eine kleine fuer die Quellsprache der Linien, eine fuer die vorhandenen Sprachen
/// und EINE fuer alle Texte der gewuenschten Sprache — nie eine je Linie.</para>
/// </summary>
public sealed class CourseCommentLocalizer
{
    private readonly AppDbContext _db;

    public CourseCommentLocalizer(AppDbContext db) => _db = db;

    // „de", „und", „pt-br" — mehr braucht es nicht; alles andere ist ein Tippfehler oder ein Versuch.
    private static readonly Regex LanguagePattern = new("^[a-z]{2,3}(-[a-z0-9]{2,4})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Das Sprachkuerzel klein und getrimmt, oder <c>null</c>, wenn es keines ist (leer, zu lang,
    /// fremde Zeichen) — dann wird <c>lang</c> ignoriert.</summary>
    public static string? NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        var l = lang.Trim().ToLowerInvariant();
        return l.Length <= 8 && LanguagePattern.IsMatch(l) ? l : null;
    }

    // ── Linien (Solver, Durchblaettern, Offline) ─────────────────────────────────────────────────

    /// <summary>Uebersetzt die Linien (<see cref="BookPuzzleDto"/>) nach <paramref name="lang"/>, soweit es
    /// aktuelle Uebersetzungen gibt, und setzt <c>CommentLanguage</c>/<c>CommentLanguages</c>/
    /// <c>CommentMachine</c>/<c>TitleLabel</c>/<c>ChapterLabel</c>.</summary>
    public async Task ApplyAsync(IList<BookPuzzleDto> lines, string? lang, CancellationToken ct = default)
    {
        var wanted = NormalizeLanguage(lang);
        if (wanted is null || lines.Count == 0) return;
        var loaded = await LoadAsync(lines.Select(l => l.Id).Distinct().ToList(), wanted, onlySlots: null, ct);
        foreach (var dto in lines)
        {
            if (!loaded.TryGetValue(dto.Id, out var t)) continue;
            var anyReplaced = false;

            if (Fresh(t, CourseTextSlots.Comment, dto.Comment) is { } comment)
            {
                dto.Comment = comment;
                anyReplaced = true;
            }
            if (dto.MoveComments is { Count: > 0 } moves)
            {
                var localized = new Dictionary<int, string>(moves.Count);
                foreach (var (ply, text) in moves)
                {
                    var hit = Fresh(t, ply, text);
                    localized[ply] = hit ?? text;
                    anyReplaced |= hit is not null;
                }
                dto.MoveComments = localized;
            }
            dto.TitleLabel = Fresh(t, CourseTextSlots.Title, dto.Title);
            dto.ChapterLabel = Fresh(t, CourseTextSlots.Chapter, dto.Chapter);
            anyReplaced |= dto.TitleLabel is not null || dto.ChapterLabel is not null;

            dto.CommentLanguage = anyReplaced ? wanted : t.Source;
            dto.CommentLanguages = t.Languages;
            dto.CommentMachine = anyReplaced && t.Machine;
        }
    }

    /// <summary>Dasselbe fuer EINE Linie (Einzel-/Naechste-/Zufalls-Linie im Buch, Kurs-„naechste").</summary>
    public Task ApplyAsync(BookPuzzleDto? line, string? lang, CancellationToken ct = default)
        => line is null ? Task.CompletedTask : ApplyAsync(new List<BookPuzzleDto> { line }, lang, ct);

    // ── Kapitellisten (Kursseite, Solver-Kapitel) ────────────────────────────────────────────────

    /// <summary>Solver-Kapitelliste (<c>GET /api/courses/{id}/chapters</c>): <see cref="CourseChapterDto.Label"/>.</summary>
    public async Task ApplyAsync(int bookId, IList<CourseChapterDto> chapters, string? lang,
        CancellationToken ct = default)
    {
        var wanted = NormalizeLanguage(lang);
        if (wanted is null || chapters.Count == 0) return;
        var labels = await ChapterLabelsAsync(bookId, wanted, ct);
        foreach (var c in chapters) c.Label = LabelFor(labels, c.Name);
    }

    /// <summary>Kurs-Detailseite (<c>GET /api/courses/{id}</c>): <see cref="CourseManageChapterDto.Label"/>.</summary>
    public async Task ApplyAsync(CourseDetailDto detail, string? lang, CancellationToken ct = default)
    {
        var wanted = NormalizeLanguage(lang);
        if (wanted is null || detail.Chapters.Count == 0) return;
        var labels = await ChapterLabelsAsync(detail.BookId, wanted, ct);
        foreach (var c in detail.Chapters) c.Label = LabelFor(labels, c.Name);
    }

    // ── Kalkulations-Modus ───────────────────────────────────────────────────────────────────────

    /// <summary>Stellungsliste + Kapitelsummen (<c>GET /api/calculations/books/{id}</c>) — nur Ueberschriften.</summary>
    public async Task ApplyAsync(CalcBookDto book, string? lang, CancellationToken ct = default)
    {
        var wanted = NormalizeLanguage(lang);
        if (wanted is null) return;
        if (book.Positions.Count > 0)
        {
            var loaded = await LoadAsync(book.Positions.Select(p => p.Id).Distinct().ToList(), wanted,
                [CourseTextSlots.Title, CourseTextSlots.Chapter], ct);
            foreach (var p in book.Positions)
            {
                if (!loaded.TryGetValue(p.Id, out var t)) continue;
                p.TitleLabel = Fresh(t, CourseTextSlots.Title, p.Title);
                p.ChapterLabel = Fresh(t, CourseTextSlots.Chapter, p.Chapter);
            }
        }
        if (book.Chapters.Count > 0)
        {
            var labels = await ChapterLabelsAsync(book.BookId, wanted, ct);
            foreach (var c in book.Chapters) c.Label = LabelFor(labels, c.Chapter);
        }
    }

    /// <summary>Anonyme Stellungen eines oeffentlichen Buchs (<c>GET /api/calculations/books/{id}/public</c>).</summary>
    public async Task ApplyAsync(CalcPublicBookDto book, string? lang, CancellationToken ct = default)
    {
        var wanted = NormalizeLanguage(lang);
        if (wanted is null || book.Positions.Count == 0) return;
        var loaded = await LoadAsync(book.Positions.Select(p => p.Id).Distinct().ToList(), wanted,
            [CourseTextSlots.Comment, CourseTextSlots.Title, CourseTextSlots.Chapter], ct);
        foreach (var p in book.Positions)
        {
            if (!loaded.TryGetValue(p.Id, out var t)) continue;
            var comment = Fresh(t, CourseTextSlots.Comment, p.Comment);
            if (comment is not null) p.Comment = comment;
            p.TitleLabel = Fresh(t, CourseTextSlots.Title, p.Title);
            p.ChapterLabel = Fresh(t, CourseTextSlots.Chapter, p.Chapter);
            var any = comment is not null || p.TitleLabel is not null || p.ChapterLabel is not null;
            p.CommentLanguage = any ? wanted : t.Source;
            p.CommentMachine = any && t.Machine;
        }
    }

    /// <summary>Eine Stellung (<c>GET /api/calculations/positions/{id}</c>).</summary>
    public async Task ApplyAsync(CalcPositionDto position, string? lang, CancellationToken ct = default)
    {
        var wanted = NormalizeLanguage(lang);
        if (wanted is null) return;
        var loaded = await LoadAsync([position.Id], wanted,
            [CourseTextSlots.Comment, CourseTextSlots.Title, CourseTextSlots.Chapter], ct);
        if (!loaded.TryGetValue(position.Id, out var t)) return;
        var comment = Fresh(t, CourseTextSlots.Comment, position.Comment);
        if (comment is not null) position.Comment = comment;
        position.TitleLabel = Fresh(t, CourseTextSlots.Title, position.Title);
        position.ChapterLabel = Fresh(t, CourseTextSlots.Chapter, position.Chapter);
        var any = comment is not null || position.TitleLabel is not null || position.ChapterLabel is not null;
        position.CommentLanguage = any ? wanted : t.Source;
        position.CommentMachine = any && t.Machine;
    }

    // ── Laden ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Was es zu einer Linie in der gewuenschten Sprache gibt.</summary>
    /// <param name="Source">Quellsprache des Kurses (<c>null</c> = nie bestimmt).</param>
    /// <param name="Languages">Vorhandene Sprachen der Linie, die QUELLE zuerst, danach alphabetisch.</param>
    /// <param name="Slots">Uebersetzte Texte je Stelle mit Fingerabdruck — leer, wenn die gewuenschte Sprache
    /// die Quelle ist.</param>
    /// <param name="Machine">Der Satz ist maschinell (<see cref="CommentOrigin.Machine"/>).</param>
    private sealed record LineTexts(string? Source, List<string> Languages,
        Dictionary<int, (string Text, string? Hash)> Slots, bool Machine);

    private async Task<Dictionary<int, LineTexts>> LoadAsync(List<int> ids, string lang, int[]? onlySlots,
        CancellationToken ct)
    {
        var sources = await _db.BookPuzzles.AsNoTracking()
            .Where(bp => ids.Contains(bp.Id))
            .Select(bp => new { bp.Id, Lang = bp.Book != null ? bp.Book.CommentLanguage : null })
            .ToListAsync(ct);
        var sets = await _db.CommentSets.AsNoTracking()
            .Where(s => s.BookPuzzleId != null && ids.Contains(s.BookPuzzleId.Value))
            .Select(s => new { LineId = s.BookPuzzleId!.Value, s.Id, s.Language, s.Origin })
            .ToListAsync(ct);

        var sourceById = sources.ToDictionary(s => s.Id, s => s.Lang);
        // Die gewuenschte Sprache IST die Quelle → keine Texte: das Original steht schon im DTO.
        var wantedSetIds = sets
            .Where(s => s.Language == lang && sourceById.GetValueOrDefault(s.LineId) != lang)
            .Select(s => s.Id).ToList();
        var textQuery = _db.CommentTexts.AsNoTracking().Where(t => wantedSetIds.Contains(t.CommentSetId));
        // Listen brauchen nur die Ueberschriften — die (langen) Zug-Kommentare bleiben dann in der Datenbank.
        if (onlySlots is not null) textQuery = textQuery.Where(t => onlySlots.Contains(t.Ply));
        var texts = wantedSetIds.Count == 0
            ? []
            : await textQuery.Select(t => new { t.CommentSetId, t.Ply, t.Text, t.SourceHash }).ToListAsync(ct);

        var textsBySet = texts.GroupBy(t => t.CommentSetId)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Ply)
                .ToDictionary(x => x.Key, x => (x.First().Text, x.First().SourceHash)));
        var setsByLine = sets.GroupBy(s => s.LineId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<int, LineTexts>();
        foreach (var id in ids)
        {
            if (!sourceById.TryGetValue(id, out var source)) continue;   // keine Kurs-Linie (mehr)
            var mine = setsByLine.GetValueOrDefault(id) ?? [];
            var languages = new List<string>();
            if (!string.IsNullOrWhiteSpace(source)) languages.Add(source);
            languages.AddRange(mine.Select(s => s.Language)
                .Where(l => !string.Equals(l, source, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.Ordinal));
            var chosen = mine.FirstOrDefault(s => wantedSetIds.Contains(s.Id));
            var slots = chosen is not null && textsBySet.TryGetValue(chosen.Id, out var bySlot)
                ? bySlot
                : new Dictionary<int, (string Text, string? Hash)>();
            result[id] = new LineTexts(source, languages, slots, chosen?.Origin == CommentOrigin.Machine);
        }
        return result;
    }

    /// <summary>Die Uebersetzung an <paramref name="slot"/>, wenn sie zu <paramref name="original"/> passt —
    /// sonst <c>null</c> (fehlt, veraltet oder es gibt kein Original).</summary>
    private static string? Fresh(LineTexts t, int slot, string? original)
    {
        if (string.IsNullOrWhiteSpace(original)) return null;
        if (!t.Slots.TryGetValue(slot, out var hit) || hit.Hash is null) return null;
        return string.Equals(hit.Hash, CourseTextHash.Of(original), StringComparison.Ordinal) ? hit.Text : null;
    }

    /// <summary>Uebersetzte Kapitelnamen eines Kurses (Fingerabdruck → Text) — aus der Kapitel-Stelle seiner
    /// Linien. Ist die gewuenschte Sprache die Quelle, gibt es keine.</summary>
    private async Task<Dictionary<string, string>> ChapterLabelsAsync(int bookId, string lang, CancellationToken ct)
    {
        var source = await _db.Books.AsNoTracking()
            .Where(b => b.Id == bookId).Select(b => b.CommentLanguage).FirstOrDefaultAsync(ct);
        if (source == lang) return new Dictionary<string, string>();
        var rows = await _db.CommentTexts.AsNoTracking()
            .Where(t => t.Ply == CourseTextSlots.Chapter && t.SourceHash != null
                        && t.CommentSet!.Language == lang && t.CommentSet.BookPuzzle!.BookId == bookId)
            .Select(t => new { t.SourceHash, t.Text })
            .Distinct()
            .ToListAsync(ct);
        return rows.GroupBy(r => r.SourceHash!).ToDictionary(g => g.Key, g => g.First().Text);
    }

    private static string? LabelFor(IReadOnlyDictionary<string, string> labels, string? name)
        => string.IsNullOrWhiteSpace(name) ? null : labels.GetValueOrDefault(CourseTextHash.Of(name));
}
