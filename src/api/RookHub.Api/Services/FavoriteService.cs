using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Verwaltet die „geliebten"/favorisierten Puzzles eines Users (polymorph Standard/Buch).
/// Add/Remove sind idempotent; die Liste wird je Quelle mit Metadaten (Fen/Moves/Rating/Themes/Titel)
/// zum Nachspielen + Analysieren angereichert.
/// </summary>
public class FavoriteService
{
    private readonly AppDbContext _db;

    public FavoriteService(AppDbContext db) => _db = db;

    /// <summary>Favorisiert ein Puzzle (idempotent). Wirft <see cref="KeyNotFoundException"/>, wenn das
    /// Puzzle in der zur Quelle passenden Tabelle fehlt — oder eine Linie eines KALKULATIONSBUCHS ist
    /// (siehe <see cref="CalculationLines(AppDbContext)"/>; gleiche Meldung, kein Existenz-Orakel).</summary>
    public async Task<bool> AddAsync(int userId, PuzzleSource source, int puzzleId)
    {
        if (!await PuzzleExistsAsync(source, puzzleId))
            throw new KeyNotFoundException("Puzzle not found.");
        if (source == PuzzleSource.Book && await CalculationLines(_db).AnyAsync(p => p.Id == puzzleId))
            throw new KeyNotFoundException("Puzzle not found.");

        var exists = await _db.FavoritePuzzles
            .AnyAsync(f => f.UserId == userId && f.Source == source && f.PuzzleId == puzzleId);
        if (exists) return true;

        _db.FavoritePuzzles.Add(new FavoritePuzzle { UserId = userId, Source = source, PuzzleId = puzzleId });
        // Race auf den Unique-Index (UserId, Source, PuzzleId) → bereits favorisiert, alles gut.
        // Andere DB-Fehler dürfen NICHT als Erfolg durchgehen (siehe DbIdempotency).
        await _db.SaveIgnoringUniqueRaceAsync();
        return true;
    }

    /// <summary>Entfernt ein Favoriten-Puzzle (idempotent).</summary>
    public async Task<bool> RemoveAsync(int userId, PuzzleSource source, int puzzleId)
    {
        var row = await _db.FavoritePuzzles
            .FirstOrDefaultAsync(f => f.UserId == userId && f.Source == source && f.PuzzleId == puzzleId);
        if (row != null)
        {
            _db.FavoritePuzzles.Remove(row);
            await _db.SaveChangesAsync();
        }
        return false;
    }

    /// <summary>Ist ein konkretes Puzzle favorisiert? (für den Herz-Button im Solver).</summary>
    public Task<bool> ContainsAsync(int userId, PuzzleSource source, int puzzleId)
        => _db.FavoritePuzzles.AnyAsync(f => f.UserId == userId && f.Source == source && f.PuzzleId == puzzleId);

    /// <summary>Anzahl favorisierter Puzzles (Dashboard-Kachel).</summary>
    public Task<int> CountAsync(int userId)
        => _db.FavoritePuzzles.CountAsync(f => f.UserId == userId);

    /// <summary>Alle Favoriten des Users (neueste zuerst), angereichert mit Metadaten. Einträge, deren
    /// Puzzle nicht mehr existiert (z. B. neu importiertes Buch), werden ausgelassen.</summary>
    public async Task<List<FavoritePuzzleDto>> ListAsync(int userId, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = await _db.FavoritePuzzles
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .Select(f => new FavoritePuzzleDto
            {
                Id = f.Id,
                PuzzleId = f.PuzzleId,
                Source = f.Source.ToString(),
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();

        if (rows.Count == 0) return rows;

        var standardIds = rows.Where(r => r.Source == nameof(PuzzleSource.Standard)).Select(r => r.PuzzleId).Distinct().ToList();
        var bookIds = rows.Where(r => r.Source == nameof(PuzzleSource.Book)).Select(r => r.PuzzleId).Distinct().ToList();

        var standard = standardIds.Count == 0
            ? new Dictionary<int, (int Rating, string? Themes, string Fen, string Moves)>()
            : await _db.Puzzles.Where(p => standardIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Rating, p.Themes, p.Fen, p.Moves })
                .ToDictionaryAsync(p => p.Id, p => (Rating: p.Rating, Themes: (string?)p.Themes, Fen: p.Fen, Moves: p.Moves));

        var book = bookIds.Count == 0
            ? new Dictionary<int, (int Rating, string? Themes, string? Title, string Fen, string Moves)>()
            : await _db.BookPuzzles.Where(p => bookIds.Contains(p.Id))
                .Select(p => new { p.Id, p.BookRating, p.Tags, p.Title, p.Fen, p.Moves })
                .ToDictionaryAsync(p => p.Id, p => (p.BookRating ?? 0, (string?)p.Tags, (string?)p.Title, p.Fen, p.Moves));
        // Linien aus Kalkulationsbüchern: Zugfolge = Lösung → zurückhalten (auch für Favoriten von
        // früher bzw. aus einem erst später umgeschalteten Kurs). FEN/Titel bleiben wie bei
        // BookPuzzleService.GetByIdAsync.
        var calcIds = bookIds.Count == 0
            ? new HashSet<int>()
            : (await CalculationLines(_db).Where(p => bookIds.Contains(p.Id)).Select(p => p.Id).ToListAsync()).ToHashSet();

        var enriched = new List<FavoritePuzzleDto>(rows.Count);
        foreach (var r in rows)
        {
            if (r.Source == nameof(PuzzleSource.Book))
            {
                if (book.TryGetValue(r.PuzzleId, out var b))
                {
                    r.Rating = b.Item1; r.Themes = b.Item2; r.Title = b.Item3; r.Fen = b.Item4;
                    r.Moves = calcIds.Contains(r.PuzzleId) ? string.Empty : b.Item5;
                    enriched.Add(r);
                }
            }
            else
            {
                if (standard.TryGetValue(r.PuzzleId, out var s))
                {
                    r.Rating = s.Rating; r.Themes = s.Themes; r.Fen = s.Fen; r.Moves = s.Moves;
                    enriched.Add(r);
                }
            }
        }
        return enriched;
    }

    private Task<bool> PuzzleExistsAsync(PuzzleSource source, int puzzleId) => source switch
    {
        PuzzleSource.Book => _db.BookPuzzles.AnyAsync(p => p.Id == puzzleId),
        _ => _db.Puzzles.AnyAsync(p => p.Id == puzzleId)
    };

    /// <summary>
    /// Buch-Linien, die zu einem KALKULATIONSBUCH gehören (<see cref="Book.IsCalculation"/>; Altbestand
    /// ohne <see cref="BookPuzzle.BookId"/> über den Dateinamen, wie
    /// <c>BookPuzzleService.EnsureNotCalculationBookAsync</c>). Ihre <see cref="BookPuzzle.Moves"/> sind
    /// die Lösung, die den Server nicht verlässt (<see cref="CourseAccess.IsCalculationBookAsync"/>) —
    /// die Favoriten waren sonst ein Weg daran vorbei: <c>PuzzleId</c> ist polymorph und wurde nur auf
    /// Existenz geprüft, die Liste gab <c>Moves</c> ungefiltert zurück (Codereview 2026-09-29).
    /// <c>internal</c>, damit <c>FavoriteControllerTests</c> die SQL-Übersetzung prüft.
    /// </summary>
    internal static IQueryable<BookPuzzle> CalculationLines(AppDbContext db) =>
        db.BookPuzzles.Where(p => db.Books.Any(b => b.IsCalculation &&
            ((p.BookId != null && b.Id == p.BookId) || (p.BookId == null && b.FileName == p.BookFileName))));
}
