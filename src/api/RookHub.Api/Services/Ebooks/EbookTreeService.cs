using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Eröffnungsbaum eines Buchs (0.881.0): die Linien eines Eröffnungsbuchs als Zugbaum ab der Grundstellung. Gesetzt von
/// außen (<c>PUT /api/admin/ebooks/{id}/tree</c>), gelesen vom Leser im Brett-Modus „Baum“ — lesen darf jeder, der das Buch
/// sieht (wie die Diagramme). Format: JSON-Feld aus <c>[parentIndex, san]</c>, Eintrag i = Knoten i+1, Knoten 0 =
/// Grundstellung, <c>parentIndex</c> zeigt immer auf einen früheren Knoten. Die Züge prüft der Server NICHT auf Legalität
/// (das macht der Leser beim Aufbau mit chess.js; ein illegaler Zug kappt nur seinen Teilbaum).
/// </summary>
public class EbookTreeService
{
    public const int MaxNodes = 200_000;
    public const int MaxSanLength = 10;

    private readonly AppDbContext _db;

    public EbookTreeService(AppDbContext db) => _db = db;

    /// <summary>Der Baum eines sichtbaren Buchs als JSON-Feld (so wie gespeichert); Buch unsichtbar oder ohne Baum → 404.</summary>
    public async Task<string> GetNodesJsonAsync(EbookCaller caller, bool manageAll, int ebookId, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        if (!await EbookAccess.Visible(_db, caller, manageAll).AnyAsync(b => b.Id == ebookId, ct))
            throw new NotFoundException("E-book not found.");
        return await _db.EbookTrees.Where(t => t.EbookId == ebookId).Select(t => t.Nodes).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("This e-book has no opening tree.");
    }

    /// <summary>Baum setzen (ersetzt einen vorhandenen) → Zahl der Knoten ohne Grundstellung. Unbekanntes Buch → 404,
    /// ungültiger Rumpf → 400 <c>ebook_tree_invalid</c>.</summary>
    public async Task<int> SetAsync(int ebookId, EbookTreeDto dto, CancellationToken ct = default)
    {
        var json = Normalize(dto);
        if (!await _db.Ebooks.AnyAsync(b => b.Id == ebookId, ct)) throw new NotFoundException("E-book not found.");
        var tree = await _db.EbookTrees.FirstOrDefaultAsync(t => t.EbookId == ebookId, ct);
        if (tree == null) _db.EbookTrees.Add(tree = new EbookTree { EbookId = ebookId });
        tree.Nodes = json.Json;
        tree.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return json.Count;
    }

    /// <summary>Baum entfernen; ohne Baum ebenfalls 204 (idempotent), unbekanntes Buch → 404.</summary>
    public async Task DeleteAsync(int ebookId, CancellationToken ct = default)
    {
        if (!await _db.Ebooks.AnyAsync(b => b.Id == ebookId, ct)) throw new NotFoundException("E-book not found.");
        var tree = await _db.EbookTrees.FirstOrDefaultAsync(t => t.EbookId == ebookId, ct);
        if (tree == null) return;
        _db.EbookTrees.Remove(tree);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Prüft den Rumpf und schreibt ihn kompakt (<c>[[0,"e4"],[1,"e5"]]</c>).</summary>
    internal static (string Json, int Count) Normalize(EbookTreeDto? dto)
    {
        var nodes = dto?.Nodes ?? throw Invalid("nodes is required.");
        if (nodes.Count == 0) throw Invalid("The tree has no moves.");
        if (nodes.Count > MaxNodes) throw Invalid($"At most {MaxNodes} nodes are allowed.");
        var sb = new StringBuilder(nodes.Count * 12);
        sb.Append('[');
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null || n.Count != 2) throw Invalid($"Node {i + 1}: expected [parentIndex, san].");
            if (n[0].ValueKind != JsonValueKind.Number || !n[0].TryGetInt32(out var parent))
                throw Invalid($"Node {i + 1}: parentIndex must be an integer.");
            // Eintrag i ist Knoten i+1 → der Elternknoten muss davor liegen (0 = Grundstellung).
            if (parent < 0 || parent > i) throw Invalid($"Node {i + 1}: parentIndex {parent} must refer to an earlier node.");
            var san = n[1].ValueKind == JsonValueKind.String ? n[1].GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(san) || san.Length > MaxSanLength)
                throw Invalid($"Node {i + 1}: SAN must be 1–{MaxSanLength} characters.");
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(parent).Append(',').Append(JsonSerializer.Serialize(san)).Append(']');
        }
        sb.Append(']');
        return (sb.ToString(), nodes.Count);
    }

    private static DomainValidationException Invalid(string message) => new(message) { Code = ApiErrorCodes.EbookTreeInvalid };
}
