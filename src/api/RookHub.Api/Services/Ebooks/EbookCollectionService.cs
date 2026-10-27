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
/// Seit 0.861.0 kann ein Buch in mehreren Sammlungen stehen: <see cref="Ebook.CollectionId"/> = Hauptsammlung (dort steht
/// es als Ordner in der Liste), <see cref="Ebook.ExtraCollections"/> = weitere.
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
        foreach (var g in await _db.EbookCollectionLinks.GroupBy(l => l.CollectionId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            counts[g.Key] = counts.GetValueOrDefault(g.Key) + g.Count;
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

        var books = await _db.Ebooks.Where(b => ids.Contains(b.Id)).Include(b => b.ExtraCollections).ToListAsync(ct);
        if (books.Count == 0) throw new NotFoundException("E-book not found.");
        var before = books.Where(b => b.CollectionId != null).Select(b => b.CollectionId!.Value)
            .Concat(books.SelectMany(b => b.ExtraCollections).Select(l => l.CollectionId)).Distinct().ToList();
        foreach (var b in books)
        {
            if (target == null) RemoveFrom(b, dto.FromCollectionId);
            else if (dto.Additional && b.CollectionId != null) AddExtra(b, target);
            else
            {
                // Verschieben (bzw. erste Sammlung): Hauptsammlung wird das Ziel, weitere Sammlungen bleiben.
                foreach (var l in b.ExtraCollections.Where(l => l.CollectionId == target.Id).ToList()) { b.ExtraCollections.Remove(l); _db.EbookCollectionLinks.Remove(l); }
                b.Collection = target; b.CollectionId = target.Id;
            }
        }
        await _db.SaveChangesAsync(ct);
        await RemoveEmptyAsync(before, ct);
        if (target == null) return null;
        return new EbookCollectionDto { Id = target.Id, Name = target.Name, BookCount = await CountAsync(target.Id, ct) };
    }

    private void AddExtra(Ebook b, EbookCollection target)
    {
        if (target.Id != 0 && (b.CollectionId == target.Id || b.ExtraCollections.Any(l => l.CollectionId == target.Id))) return;
        var link = new EbookCollectionLink { Ebook = b, Collection = target };
        b.ExtraCollections.Add(link);
        _db.EbookCollectionLinks.Add(link);
    }

    /// <summary>Aus einer Sammlung (<paramref name="from"/>) bzw. allen (<c>null</c>) nehmen. Fällt die Hauptsammlung weg,
    /// rückt die erste weitere nach.</summary>
    private void RemoveFrom(Ebook b, int? from)
    {
        var drop = b.ExtraCollections.Where(l => from == null || l.CollectionId == from).ToList();
        foreach (var l in drop) { b.ExtraCollections.Remove(l); _db.EbookCollectionLinks.Remove(l); }
        if (from != null && b.CollectionId != from) return;
        var next = b.ExtraCollections.OrderBy(l => l.CollectionId).FirstOrDefault();
        if (next != null) { b.ExtraCollections.Remove(next); _db.EbookCollectionLinks.Remove(next); }
        b.Collection = null;
        b.CollectionId = next?.CollectionId;
    }

    /// <summary>Bücher einer Sammlung: als Haupt- oder weitere Sammlung.</summary>
    private Task<int> CountAsync(int id, CancellationToken ct)
        => _db.Ebooks.CountAsync(b => b.CollectionId == id || b.ExtraCollections.Any(l => l.CollectionId == id), ct);

    public async Task<EbookCollectionDto> RenameAsync(bool manageAll, int id, string name, CancellationToken ct = default)
    {
        if (!manageAll) throw new ForbiddenException("Only library managers can manage collections.");
        var c = await _db.EbookCollections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Collection not found.");
        name = name.Trim();
        if (name.Length == 0) throw new DomainValidationException("Name is required.");
        c.Name = name.Length > 200 ? name[..200] : name;
        await _db.SaveChangesAsync(ct);
        return new EbookCollectionDto { Id = c.Id, Name = c.Name, BookCount = await CountAsync(id, ct) };
    }

    /// <summary>Sammlung auflösen: die Bücher bleiben, stehen danach wieder einzeln in der Liste.</summary>
    public async Task DeleteAsync(bool manageAll, int id, CancellationToken ct = default)
    {
        if (!manageAll) throw new ForbiddenException("Only library managers can manage collections.");
        var c = await _db.EbookCollections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Collection not found.");
        // InMemory setzt den Fremdschlüssel nicht selbst zurück; steht ein Buch noch in weiteren Sammlungen, rückt die erste nach.
        foreach (var b in await _db.Ebooks.Where(b => b.CollectionId == id || b.ExtraCollections.Any(l => l.CollectionId == id))
                     .Include(b => b.ExtraCollections).ToListAsync(ct))
            RemoveFrom(b, id);
        _db.EbookCollections.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    private async Task RemoveEmptyAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var used = await _db.Ebooks.Where(b => b.CollectionId != null && ids.Contains(b.CollectionId.Value)).Select(b => b.CollectionId!.Value).Distinct().ToListAsync(ct);
        used.AddRange(await _db.EbookCollectionLinks.Where(l => ids.Contains(l.CollectionId)).Select(l => l.CollectionId).Distinct().ToListAsync(ct));
        var empty = await _db.EbookCollections.Where(c => ids.Contains(c.Id) && !used.Contains(c.Id)).ToListAsync(ct);
        if (empty.Count == 0) return;
        _db.EbookCollections.RemoveRange(empty);
        await _db.SaveChangesAsync(ct);
    }
}
