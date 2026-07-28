using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Kalkulations-Modus: der Nutzer bekommt reine Stellungen eines Buchs (FEN + optionaler
/// Kommentar) und legt zu jeder seinen EIGENEN Analysebaum an (<see cref="CalculationTree"/>).
/// Eine Lösung gibt es hier nicht — deshalb verlässt <see cref="BookPuzzle.Moves"/> in diesem
/// Pfad den Server nie vollständig: ausgeliefert wird höchstens der Vorlauf bis zum
/// Trainingsstart (<c>StartPly</c>), damit das Frontend die Aufgabenstellung nachstellen kann.
///
/// <para>Zugriff wird je Buch über <see cref="CourseAccess"/> erzwungen (kein Zugriff → 404 via
/// <see cref="KeyNotFoundException"/>). Der Modus ist bewusst nicht auf Bücher mit
/// <see cref="Book.IsCalculation"/> beschränkt — das Flag steuert nur, ob die Kursübersicht ihn
/// anbietet; per Direkt-Link darf jede zugängliche Buchstellung durchgerechnet werden.</para>
/// </summary>
public class CalculationService
{
    /// <summary>Obergrenze für einen gespeicherten Baum (Zeichen). Großzügig — ein sehr ausführlich
    /// kommentierter Baum mit hunderten Zügen bleibt deutlich darunter; schützt vor Müll-Uploads.</summary>
    public const int MaxTreeJsonLength = 262_144;

    private readonly AppDbContext _db;

    public CalculationService(AppDbContext db) => _db = db;

    private async Task EnsureBookAccessAsync(int userId, int bookId, bool isAdmin, CancellationToken ct = default)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct))
            throw new KeyNotFoundException("Book not found.");
    }

    /// <summary>Kopf + leichte Stellungsliste eines Buchs (ohne FEN/Kommentar/Züge) inkl. Markierung,
    /// zu welchen Stellungen der Nutzer schon einen Baum gespeichert hat. Reihenfolge wie im Kurs
    /// (Round → Id).</summary>
    public async Task<CalcBookDto> GetBookAsync(int userId, int bookId, bool isAdmin, CancellationToken ct = default)
    {
        await EnsureBookAccessAsync(userId, bookId, isAdmin, ct);

        var book = await _db.Books.Where(b => b.Id == bookId)
            .Select(b => new { b.Id, b.DisplayName, b.IsCalculation })
            .FirstAsync(ct);

        var positions = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => new CalcPositionListItemDto
            {
                Id = bp.Id,
                Round = bp.Round,
                Title = bp.Title,
                Chapter = bp.Chapter,
            })
            .ToListAsync(ct);

        var withTree = await _db.CalculationTrees
            .Where(t => t.UserId == userId && t.BookId == bookId)
            .Select(t => t.BookPuzzleId)
            .ToListAsync(ct);
        var treeIds = withTree.ToHashSet();
        foreach (var p in positions) p.HasTree = treeIds.Contains(p.Id);

        return new CalcBookDto
        {
            BookId = book.Id,
            DisplayName = book.DisplayName,
            IsCalculation = book.IsCalculation,
            Positions = positions,
        };
    }

    /// <summary>Eine Stellung inkl. eigenem Baum. Liefert NIE die Lösungszüge — nur den Vorlauf bis
    /// zum Trainingsstart (<c>StartPly</c>), sonst gar keine Züge.</summary>
    public async Task<CalcPositionDto> GetPositionAsync(int userId, int bookPuzzleId, bool isAdmin,
        CancellationToken ct = default)
    {
        var puzzle = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == bookPuzzleId, ct)
            ?? throw new KeyNotFoundException("Position not found.");
        var bookId = puzzle.BookId ?? throw new KeyNotFoundException("Position not found.");
        await EnsureBookAccessAsync(userId, bookId, isAdmin, ct);

        var tree = await _db.CalculationTrees
            .FirstOrDefaultAsync(t => t.UserId == userId && t.BookPuzzleId == bookPuzzleId, ct);

        return new CalcPositionDto
        {
            Id = puzzle.Id,
            BookId = bookId,
            Round = puzzle.Round,
            Title = puzzle.Title,
            Chapter = puzzle.Chapter,
            Fen = puzzle.Fen,
            SetupMoves = SetupMoves(puzzle),
            Comment = puzzle.Comment,
            TreeJson = tree?.TreeJson,
            TreeUpdatedAt = tree?.UpdatedAt,
        };
    }

    /// <summary>
    /// Züge von der Header-FEN bis zur Aufgabenstellung. Bei <c>StartPly &gt;= 0</c> (Trainingsstart
    /// mitten in der Partie) sind das die Halbzüge <c>0..StartPly</c> — also ausdrücklich NUR der
    /// Vorlauf, nie der ab <c>StartPly+1</c> beginnende Lösungsweg. Sonst leer.
    /// </summary>
    internal static string SetupMoves(BookPuzzle puzzle)
    {
        if (puzzle.StartPly < 0 || string.IsNullOrWhiteSpace(puzzle.Moves)) return string.Empty;
        var moves = puzzle.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', moves.Take(Math.Min(puzzle.StartPly + 1, moves.Length)));
    }

    /// <summary>Speichert (Upsert) den Analysebaum des Users zu einer Stellung.</summary>
    /// <exception cref="ArgumentException">Baum ist kein gültiges JSON oder zu groß (→ 400).</exception>
    public async Task<CalcTreeSavedDto> SaveTreeAsync(int userId, int bookPuzzleId, SaveCalcTreeDto dto,
        bool isAdmin, CancellationToken ct = default)
    {
        var json = dto.TreeJson ?? string.Empty;
        if (json.Length > MaxTreeJsonLength)
            throw new ArgumentException($"Tree too large (max {MaxTreeJsonLength} characters).");
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Tree must not be empty.");
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ArgumentException("Tree is not valid JSON."); }

        var puzzle = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == bookPuzzleId, ct)
            ?? throw new KeyNotFoundException("Position not found.");
        var bookId = puzzle.BookId ?? throw new KeyNotFoundException("Position not found.");
        await EnsureBookAccessAsync(userId, bookId, isAdmin, ct);

        var now = DateTime.UtcNow;
        var tree = await _db.CalculationTrees
            .FirstOrDefaultAsync(t => t.UserId == userId && t.BookPuzzleId == bookPuzzleId, ct);
        if (tree == null)
        {
            tree = new CalculationTree
            {
                UserId = userId,
                BookId = bookId,
                BookPuzzleId = bookPuzzleId,
                TreeJson = json,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.CalculationTrees.Add(tree);
        }
        else
        {
            tree.TreeJson = json;
            tree.UpdatedAt = now;
            // Altzeilen aus einer Zeit vor gesetztem BookId heilen (defensiv, kostet nichts).
            tree.BookId = bookId;
        }
        await _db.SaveChangesAsync(ct);
        return new CalcTreeSavedDto { BookPuzzleId = bookPuzzleId, UpdatedAt = tree.UpdatedAt };
    }

    /// <summary>Löscht den eigenen Baum zu einer Stellung. Idempotent (kein Baum → einfach nichts).</summary>
    public async Task DeleteTreeAsync(int userId, int bookPuzzleId, bool isAdmin, CancellationToken ct = default)
    {
        var tree = await _db.CalculationTrees
            .FirstOrDefaultAsync(t => t.UserId == userId && t.BookPuzzleId == bookPuzzleId, ct);
        if (tree == null) return;
        await EnsureBookAccessAsync(userId, tree.BookId, isAdmin, ct);
        _db.CalculationTrees.Remove(tree);
        await _db.SaveChangesAsync(ct);
    }
}
