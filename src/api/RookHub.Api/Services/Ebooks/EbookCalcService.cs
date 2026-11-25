using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Eigene Kalkulation am E-Book-Diagramm (1.019.0), je Nutzer und Diagramm (<see cref="EbookDiagramCalc"/>). Nur für
/// angemeldete Nutzer und nur der eigene Stand; das Diagramm muss in einem für den Nutzer sichtbaren Buch liegen. Der
/// Leser hält eine lokale Kopie als Zwischenspeicher, der Server gewinnt — außer das Gerät hat einen noch nicht
/// hochgeladenen, jüngeren Stand (<c>At</c> vergleicht die Gerätezeitpunkte: ein älterer überschreibt nie einen jüngeren).
/// </summary>
public class EbookCalcService
{
    /// <summary>Deckel des serialisierten Baums (ein Baum mit ein paar hundert Knoten braucht wenige KB).</summary>
    public const int MaxTreeLength = 64 * 1024;
    public const int MaxBulk = 50;

    private readonly AppDbContext _db;

    public EbookCalcService(AppDbContext db) => _db = db;

    public async Task<EbookCalcDto?> GetAsync(int userId, bool manageAll, int ebookId, int diagramId, CancellationToken ct = default)
    {
        await EnsureDiagramAsync(userId, manageAll, ebookId, diagramId, ct);
        return await _db.EbookDiagramCalcs.AsNoTracking()
            .Where(c => c.UserId == userId && c.DiagramId == diagramId)
            .Select(c => new EbookCalcDto { Tree = c.Tree, At = c.ClientAt })
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Speichert, wenn der Stand nicht älter ist als der gespeicherte → der danach gültige Stand.</summary>
    public async Task<EbookCalcDto> SetAsync(int userId, bool manageAll, int ebookId, int diagramId, EbookCalcDto dto, CancellationToken ct = default)
    {
        Validate(dto);
        await EnsureDiagramAsync(userId, manageAll, ebookId, diagramId, ct);
        var saved = await UpsertAsync(userId, diagramId, dto, ct);
        await _db.SaveChangesAsync(ct);
        return saved;
    }

    /// <summary>Mehrere Stände auf einmal (Abmelden): unbekannte/unsichtbare Diagramme und ungültige Einträge fallen still
    /// weg → Zahl der übernommenen.</summary>
    public async Task<int> SetManyAsync(int userId, bool manageAll, IReadOnlyList<EbookCalcBulkItemDto> items, CancellationToken ct = default)
    {
        if (items.Count > MaxBulk) throw new DomainValidationException($"At most {MaxBulk} entries per request.") { Code = "ebook_calc_invalid" };
        var ids = items.Select(i => i.DiagramId).Distinct().ToList();
        var visibleBooks = EbookAccess.Visible(_db, userId, manageAll).Select(b => b.Id);
        var visible = await _db.EbookDiagrams.Where(d => ids.Contains(d.Id) && visibleBooks.Contains(d.EbookId))
            .Select(d => d.Id).ToListAsync(ct);
        var n = 0;
        foreach (var item in items)
        {
            if (!visible.Contains(item.DiagramId) || !IsValid(item)) continue;
            await UpsertAsync(userId, item.DiagramId, item, ct);
            n++;
        }
        await _db.SaveChangesAsync(ct);
        return n;
    }

    private async Task<EbookCalcDto> UpsertAsync(int userId, int diagramId, EbookCalcDto dto, CancellationToken ct)
    {
        var row = _db.EbookDiagramCalcs.Local.FirstOrDefault(c => c.UserId == userId && c.DiagramId == diagramId)
                  ?? await _db.EbookDiagramCalcs.FirstOrDefaultAsync(c => c.UserId == userId && c.DiagramId == diagramId, ct);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var at = Math.Min(dto.At, nowMs + 86_400_000);   // falsch gestellte Uhr nagelt nichts fest
        if (row == null)
        {
            row = new EbookDiagramCalc { UserId = userId, DiagramId = diagramId, Tree = dto.Tree, ClientAt = at };
            _db.EbookDiagramCalcs.Add(row);
        }
        else if (at >= row.ClientAt)
        {
            row.Tree = dto.Tree;
            row.ClientAt = at;
            row.UpdatedAt = DateTime.UtcNow;
        }
        return new EbookCalcDto { Tree = row.Tree, At = row.ClientAt };
    }

    private async Task EnsureDiagramAsync(int userId, bool manageAll, int ebookId, int diagramId, CancellationToken ct)
    {
        var visible = EbookAccess.Visible(_db, userId, manageAll).Where(b => b.Id == ebookId).Select(b => b.Id);
        if (!await _db.EbookDiagrams.AnyAsync(d => d.Id == diagramId && d.EbookId == ebookId && visible.Contains(d.EbookId), ct))
            throw new NotFoundException("Diagram not found.");
    }

    private static bool IsValid(EbookCalcDto dto)
        => dto.Tree is { Length: > 0 and <= MaxTreeLength } && dto.At > 0;

    private static void Validate(EbookCalcDto dto)
    {
        if (!IsValid(dto)) throw new DomainValidationException("Invalid calculation (empty, too large or without timestamp).") { Code = "ebook_calc_invalid" };
    }
}
