using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Admin-Verwaltung der Bücher (Kurs-Quelle): Liste inkl. Gruppen-Freigaben, Gruppen-Zugriff
/// get/set, Metadaten-Update, Löschen mit explizitem Cascade-Cleanup (vormals inline im
/// AdminController). Buch nicht gefunden → <see cref="KeyNotFoundException"/> (404 mit Message).
/// </summary>
public class BookAdminService
{
    private readonly AppDbContext _db;

    public BookAdminService(AppDbContext db) => _db = db;

    public async Task<List<BookDto>> GetBooksAsync()
    {
        var books = await _db.Books
            .OrderBy(b => b.DisplayName)
            .Select(b => new BookDto
            {
                Id = b.Id,
                FileName = b.FileName,
                DisplayName = b.DisplayName,
                Difficulty = b.Difficulty,
                Rating = b.Rating,
                MinElo = b.MinElo,
                MaxElo = b.MaxElo,
                Tags = b.Tags,
                Description = b.Description,
                ForDaily = b.ForDaily,
                ForRandom = b.ForRandom,
                ForBlind = b.ForBlind,
                IsPublic = b.IsPublic,
                PublicSlug = b.PublicSlug,
                Kind = b.Kind,
                PuzzleCount = b.Puzzles.Count(),
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
            })
            .ToListAsync();

        // Gruppen-Freigaben pro Buch anhängen (eine Abfrage, dann mappen).
        var accessByBook = await _db.BookGroupAccesses
            .GroupBy(a => a.BookId)
            .Select(g => new { BookId = g.Key, GroupIds = g.Select(x => x.GroupId).ToList() })
            .ToDictionaryAsync(x => x.BookId, x => x.GroupIds);
        foreach (var dto in books)
            if (accessByBook.TryGetValue(dto.Id, out var ids))
                dto.AccessGroupIds = ids;

        return books;
    }

    /// <summary>Gruppen-Ids, die dieses Buch als Kurs sehen dürfen.</summary>
    public async Task<List<int>> GetBookGroupsAsync(int id)
    {
        if (!await _db.Books.AnyAsync(b => b.Id == id))
            throw new KeyNotFoundException("Book not found.");
        return await _db.BookGroupAccesses
            .Where(a => a.BookId == id)
            .Select(a => a.GroupId)
            .ToListAsync();
    }

    /// <summary>Setzt die vollständige Gruppen-Freigabe eines Buchs (ersetzt bestehende Einträge).</summary>
    public async Task<List<int>> SetBookGroupsAsync(int id, SetBookGroupsDto dto)
    {
        if (!await _db.Books.AnyAsync(b => b.Id == id))
            throw new KeyNotFoundException("Book not found.");

        var requested = (dto.GroupIds ?? new List<int>()).Distinct().ToList();
        // Nur existierende Gruppen zulassen (ungültige Ids ignorieren).
        var validIds = await _db.Groups.Where(g => requested.Contains(g.Id)).Select(g => g.Id).ToListAsync();

        var existing = await _db.BookGroupAccesses.Where(a => a.BookId == id).ToListAsync();
        var existingIds = existing.Select(a => a.GroupId).ToHashSet();

        _db.BookGroupAccesses.RemoveRange(existing.Where(a => !validIds.Contains(a.GroupId)));
        foreach (var gid in validIds.Where(gid => !existingIds.Contains(gid)))
            _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = id, GroupId = gid });

        await _db.SaveChangesAsync();
        return validIds;
    }

    public async Task<BookDto> UpdateBookAsync(int id, UpdateBookDto dto)
    {
        var book = await _db.Books.FindAsync(id)
            ?? throw new KeyNotFoundException("Book not found.");

        if (dto.DisplayName != null) book.DisplayName = dto.DisplayName;
        if (dto.Difficulty != null) book.Difficulty = dto.Difficulty;
        if (dto.Rating.HasValue) book.Rating = dto.Rating;
        if (dto.Tags != null) book.Tags = dto.Tags;
        if (dto.Description != null) book.Description = dto.Description;
        if (dto.ForDaily.HasValue) book.ForDaily = dto.ForDaily.Value;
        if (dto.ForRandom.HasValue) book.ForRandom = dto.ForRandom.Value;
        if (dto.ForBlind.HasValue) book.ForBlind = dto.ForBlind.Value;
        if (dto.IsPublic.HasValue) book.IsPublic = dto.IsPublic.Value;
        if (dto.PublicSlug != null) await ApplyPublicSlugAsync(book, dto.PublicSlug);
        if (dto.Kind.HasValue) book.Kind = dto.Kind.Value;
        book.MinElo = dto.MinElo;
        book.MaxElo = dto.MaxElo;
        book.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var count = await _db.BookPuzzles.CountAsync(bp => bp.BookId == id);
        return new BookDto
        {
            Id = book.Id,
            FileName = book.FileName,
            DisplayName = book.DisplayName,
            Difficulty = book.Difficulty,
            Rating = book.Rating,
            MinElo = book.MinElo,
            MaxElo = book.MaxElo,
            Tags = book.Tags,
            Description = book.Description,
            ForDaily = book.ForDaily,
            ForRandom = book.ForRandom,
            ForBlind = book.ForBlind,
            IsPublic = book.IsPublic,
            PublicSlug = book.PublicSlug,
            Kind = book.Kind,
            PuzzleCount = count,
            CreatedAt = book.CreatedAt,
            UpdatedAt = book.UpdatedAt,
        };
    }

    /// <summary>Top-Level-Routen/Prefixe, die NICHT als öffentlicher Kurz-Alias vergeben werden dürfen
    /// (sonst würde die Kurz-URL /{slug} eine echte Seite verdecken).</summary>
    private static readonly HashSet<string> _reservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "login","register","forgot-password","reset-password","dashboard","profile","friends",
        "repertoires","tournaments","puzzles","favorites","weekly","analysis","games","stats",
        "leaderboards","training-goals","notifications","messages","courses","chessable","admin",
        "t","g","help","install","privacy","impressum","account-deletion","api","assets","i18n",
    };

    /// <summary>Normalisiert + validiert den öffentlichen Kurz-Alias und setzt ihn (Leerstring = entfernen).
    /// Wirft <see cref="ArgumentException"/> (→ 400) bei ungültigem/reserviertem/vergebenem Alias.</summary>
    private async Task ApplyPublicSlugAsync(Book book, string raw)
    {
        var slug = raw.Trim().ToLowerInvariant();
        if (slug.Length == 0) { book.PublicSlug = null; return; }   // Leerstring → Alias entfernen
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z][a-z0-9-]{1,39}$") || slug.EndsWith("-") || slug.Contains("--"))
            throw new ArgumentException("Ungültiger Alias: nur Kleinbuchstaben, Ziffern und Bindestriche, 2–40 Zeichen, Beginn mit Buchstabe.");
        if (_reservedSlugs.Contains(slug))
            throw new ArgumentException($"Alias „{slug}“ ist reserviert.");
        if (await _db.Books.AnyAsync(b => b.Id != book.Id && b.PublicSlug == slug))
            throw new ArgumentException($"Alias „{slug}“ ist bereits vergeben.");
        book.PublicSlug = slug;
    }

    public async Task DeleteBookAsync(int id)
    {
        var book = await _db.Books.FindAsync(id)
            ?? throw new KeyNotFoundException("Book not found.");

        // Kurs-Daten (Fortschritt + gelöste Puzzles) und zugehörige Puzzles explizit entfernen.
        // FK-Cascade greift bei InMemory nicht; zudem hat CoursePuzzleResult eine Restrict-FK
        // auf BookPuzzle — EF löscht beim SaveChanges die Dependents (CoursePuzzleResult) vor
        // den Principals (BookPuzzle), sodass die Reihenfolge auch real korrekt ist.
        _db.CoursePuzzleResults.RemoveRange(_db.CoursePuzzleResults.Where(cr => cr.BookId == id));
        _db.CourseInfoViews.RemoveRange(_db.CourseInfoViews.Where(iv => iv.BookId == id));
        _db.CourseAttempts.RemoveRange(_db.CourseAttempts.Where(a => a.BookId == id));
        _db.CourseProgresses.RemoveRange(_db.CourseProgresses.Where(cp => cp.BookId == id));
        _db.CoursePins.RemoveRange(_db.CoursePins.Where(p => p.BookId == id));
        _db.CourseShares.RemoveRange(_db.CourseShares.Where(cs => cs.BookId == id));
        // Verknüpfungen in BEIDE Richtungen entfernen (LinkedBookId hat keinen Cascade-FK).
        _db.CourseLinks.RemoveRange(_db.CourseLinks.Where(l => l.BookId == id || l.LinkedBookId == id));
        _db.BookGroupAccesses.RemoveRange(_db.BookGroupAccesses.Where(a => a.BookId == id));
        // BookPuzzleAttempt hat (wie CoursePuzzleResult) eine Restrict-FK auf BookPuzzle →
        // die Versuche (Tagespuzzle-/Buch-Solves) explizit vor den Puzzles entfernen, sonst
        // schlägt SaveChanges bei einem Buch mit aufgezeichneten Solves mit FK-Fehler fehl.
        var puzzleIds = _db.BookPuzzles.Where(bp => bp.BookId == id).Select(bp => bp.Id);
        _db.BookPuzzleAttempts.RemoveRange(_db.BookPuzzleAttempts.Where(a => puzzleIds.Contains(a.BookPuzzleId)));
        // DailyPuzzle hat eine Restrict-FK auf BookPuzzle → Historie verfaellt
        // beim Buch-Loeschen mit. Sonst blockt der DB-Constraint die Loeschung.
        _db.DailyPuzzles.RemoveRange(_db.DailyPuzzles.Where(d => puzzleIds.Contains(d.BookPuzzleId)));
        var puzzles = _db.BookPuzzles.Where(bp => bp.BookId == id);
        _db.BookPuzzles.RemoveRange(puzzles);
        _db.Books.Remove(book);
        await _db.SaveChangesAsync();
    }
}
