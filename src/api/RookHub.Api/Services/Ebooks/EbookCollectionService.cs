using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Sammlungen der E-Book-Bibliothek (0.787.0): mehrere Bücher erscheinen in der Liste als EIN Ordner (z. B. alle Hefte des
/// British Chess Magazine). Anlegen, Zuordnen, Umbenennen, Auflösen nur für Verwalter (<c>ebooks.manage</c>); eine
/// Sammlung ohne Bücher verschwindet von selbst. Das Zusammenfassen in der Liste macht <see cref="EbookService.ListAsync"/>.
/// </summary>
public class EbookCollectionService
{
    private readonly AppDbContext _db;

    public EbookCollectionService(AppDbContext db) => _db = db;

    /// <summary>Alle Sammlungen mit der Zahl ihrer Bücher (für die Auswahl im Zuordnen-Dialog).</summary>
    public async Task<List<EbookCollectionDto>> ListAsync(bool manageAll, CancellationToken ct = default)
    {
        if (!manageAll) throw new ForbiddenException("Only library managers can manage collections.");
        var counts = await _db.Ebooks.Where(b => b.CollectionId != null).GroupBy(b => b.CollectionId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await _db.EbookCollections.OrderBy(c => c.Name).ToListAsync(ct))
            .Select(c => new EbookCollectionDto { Id = c.Id, Name = c.Name, BookCount = counts.GetValueOrDefault(c.Id) }).ToList();
    }

    /// <summary>Bücher einer bestehenden oder neuen Sammlung zuordnen bzw. (beides leer) herausnehmen → betroffene Sammlung.</summary>
    public async Task<EbookCollectionDto?> AssignAsync(bool manageAll, EbookCollectionAssignDto dto, CancellationToken ct = default)
    {
        if (!manageAll) throw new ForbiddenException("Only library managers can manage collections.");
        var ids = dto.EbookIds.Distinct().ToList();
        if (ids.Count == 0) throw new DomainValidationException("No books selected.");
        EbookCollection? target = null;
        var newName = dto.NewName?.Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            target = await _db.EbookCollections.FirstOrDefaultAsync(c => c.Name == newName, ct);
            if (target == null) { target = new EbookCollection { Name = newName.Length > 200 ? newName[..200] : newName }; _db.EbookCollections.Add(target); }
        }
        else if (dto.CollectionId != null)
            target = await _db.EbookCollections.FirstOrDefaultAsync(c => c.Id == dto.CollectionId, ct)
                ?? throw new NotFoundException("Collection not found.");

        var books = await _db.Ebooks.Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        if (books.Count == 0) throw new NotFoundException("E-book not found.");
        var before = books.Where(b => b.CollectionId != null).Select(b => b.CollectionId!.Value).Distinct().ToList();
        foreach (var b in books) { b.Collection = target; b.CollectionId = target?.Id; }
        await _db.SaveChangesAsync(ct);
        await RemoveEmptyAsync(before, ct);
        if (target == null) return null;
        return new EbookCollectionDto { Id = target.Id, Name = target.Name, BookCount = await _db.Ebooks.CountAsync(b => b.CollectionId == target.Id, ct) };
    }

    public async Task<EbookCollectionDto> RenameAsync(bool manageAll, int id, string name, CancellationToken ct = default)
    {
        if (!manageAll) throw new ForbiddenException("Only library managers can manage collections.");
        var c = await _db.EbookCollections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Collection not found.");
        name = name.Trim();
        if (name.Length == 0) throw new DomainValidationException("Name is required.");
        c.Name = name.Length > 200 ? name[..200] : name;
        await _db.SaveChangesAsync(ct);
        return new EbookCollectionDto { Id = c.Id, Name = c.Name, BookCount = await _db.Ebooks.CountAsync(b => b.CollectionId == id, ct) };
    }

    /// <summary>Sammlung auflösen: die Bücher bleiben, stehen danach wieder einzeln in der Liste.</summary>
    public async Task DeleteAsync(bool manageAll, int id, CancellationToken ct = default)
    {
        if (!manageAll) throw new ForbiddenException("Only library managers can manage collections.");
        var c = await _db.EbookCollections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Collection not found.");
        // InMemory setzt den Fremdschlüssel nicht selbst zurück.
        foreach (var b in await _db.Ebooks.Where(b => b.CollectionId == id).ToListAsync(ct)) b.CollectionId = null;
        _db.EbookCollections.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    private async Task RemoveEmptyAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var used = await _db.Ebooks.Where(b => b.CollectionId != null && ids.Contains(b.CollectionId.Value)).Select(b => b.CollectionId!.Value).Distinct().ToListAsync(ct);
        var empty = await _db.EbookCollections.Where(c => ids.Contains(c.Id) && !used.Contains(c.Id)).ToListAsync(ct);
        if (empty.Count == 0) return;
        _db.EbookCollections.RemoveRange(empty);
        await _db.SaveChangesAsync(ct);
    }
}
