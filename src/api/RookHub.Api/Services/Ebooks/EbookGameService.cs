using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Partien zu den Diagrammen eines Buchs (0.831.0, Partiensammlungen): der Leser zeigt im Brett-Modus „Partie“ die ganze
/// Partie ab der Diagrammstellung. Die Zuordnung entsteht außerhalb (Abgleich der Diagrammstellungen mit der
/// Partiedatenbank bzw. der PGN des Buchs) und kommt per <see cref="ImportAsync"/>; Lesen darf jeder, der das Buch sieht.
/// </summary>
public class EbookGameService
{
    private readonly AppDbContext _db;

    public EbookGameService(AppDbContext db) => _db = db;

    /// <summary>Die Partie zu einem Diagramm eines sichtbaren Buchs; Buch, Diagramm oder Partie unbekannt → 404.</summary>
    public async Task<EbookGameDto> GetForDiagramAsync(EbookCaller caller, bool manageAll, int ebookId, int diagramId, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        if (!await EbookAccess.Visible(_db, caller, manageAll).AnyAsync(b => b.Id == ebookId, ct))
            throw new NotFoundException("E-book not found.");
        var link = await _db.EbookDiagrams.Readable().Where(d => d.Id == diagramId && d.EbookId == ebookId && d.GameId != null)
            .Select(d => new { d.GamePly, d.Game })
            .FirstOrDefaultAsync(ct);
        if (link?.Game is not { } g) throw new NotFoundException("No game for this diagram.");
        var moves = g.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        return new EbookGameDto
        {
            Id = g.Id, White = g.White, Black = g.Black, Event = g.Event, Date = g.Date, Result = g.Result,
            Moves = moves, Ply = Math.Clamp(link.GamePly ?? 0, 0, moves.Count),
        };
    }

    /// <summary>
    /// Partien setzen (Verwalter): neue Partien anlegen und Diagramme darauf zeigen lassen. Eine Verknüpfung zählt nur,
    /// wenn das Diagramm zum Buch gehört, die Partie im selben Aufruf mitkommt und der Halbzug in der Partie liegt; sonst
    /// <see cref="EbookGamesImportResultDto.Skipped"/>. Mit <see cref="EbookGamesImportDto.Replace"/> fallen vorher alle
    /// Partien des Buchs weg (die Diagramme verlieren ihre Partie). Unbenutzte neue Partien werden nicht gespeichert.
    /// </summary>
    public async Task<EbookGamesImportResultDto> ImportAsync(int ebookId, EbookGamesImportDto dto, CancellationToken ct = default)
    {
        if (!await _db.Ebooks.AnyAsync(b => b.Id == ebookId, ct)) throw new NotFoundException("E-book not found.");
        var diagrams = await _db.EbookDiagrams.Where(d => d.EbookId == ebookId).ToDictionaryAsync(d => d.Id, ct);
        if (dto.Replace)
        {
            foreach (var d in diagrams.Values) { d.GameId = null; d.GamePly = null; }
            _db.EbookGames.RemoveRange(_db.EbookGames.Where(g => g.EbookId == ebookId));
        }

        var byKey = new Dictionary<string, (EbookGame Game, int Plies)>(StringComparer.Ordinal);
        foreach (var item in dto.Games)
        {
            var moves = item.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (moves.Length == 0 || byKey.ContainsKey(item.Key)) continue;
            byKey[item.Key] = (new EbookGame
            {
                EbookId = ebookId, White = Clip(item.White, 120), Black = Clip(item.Black, 120), Event = Clip(item.Event, 200),
                Date = Clip(item.Date, 20), Result = Clip(item.Result, 10), Moves = string.Join(' ', moves), Source = Clip(item.Source, 40),
            }, moves.Length);
        }

        var result = new EbookGamesImportResultDto();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in dto.Links)
        {
            if (!diagrams.TryGetValue(link.DiagramId, out var d) || !byKey.TryGetValue(link.GameKey, out var g)
                || link.Ply < 0 || link.Ply > g.Plies)
            {
                result.Skipped++;
                continue;
            }
            d.Game = g.Game;
            d.GamePly = link.Ply;
            used.Add(link.GameKey);
            result.Linked++;
        }
        foreach (var key in used) _db.EbookGames.Add(byKey[key].Game);
        result.Games = used.Count;
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private static string? Clip(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}
