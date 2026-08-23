using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Append-Senke für die Sitzungszüge aus Chessables Session-Report (siehe
/// <see cref="ChessableSessionMove"/>). ANDERS als die „schwierigen Züge" bewusst KEIN Upsert:
/// jeder Trainingsdurchlauf einer Linie ist ein eigener Datenpunkt (Fehlversuche, Overstudy,
/// Alternativen) — Auswertung offen, darum wird die Historie behalten. Ein per-User-Deckel
/// trimmt opportunistisch die ältesten Zeilen, damit die Tabelle nicht unbegrenzt wächst.
/// </summary>
public class ChessableSessionMoveService
{
    /// <summary>Deckel je Batch — der Client bündelt ohnehin nur wenige Linien je Flush.</summary>
    public const int MaxEntriesPerBatch = 200;
    /// <summary>Deckel fürs Zug-Array einer Linie (typisch wenige KB je Durchlauf).</summary>
    public const int MaxJsonLength = 64_000;
    /// <summary>Per-User-Gesamtdeckel; darüber fliegen die ältesten Zeilen raus.
    /// Kein const, damit Tests nicht 200k Zeilen anlegen müssen.</summary>
    public int MaxRowsPerUser { get; init; } = 200_000;

    private readonly AppDbContext _db;

    public ChessableSessionMoveService(AppDbContext db) => _db = db;

    public async Task<int> AppendBatchAsync(int userId, string bid,
        List<ChessableSessionMoveEntryDto> entries, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var clean = (entries ?? new())
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Oid)
                && e.Oid.Trim().Length <= 32 && e.Oid.Trim().All(char.IsAsciiDigit))
            .Select(e => (Oid: e.Oid.Trim(), Json: NormalizeJson(e.Moves)))
            .Where(x => x.Json is not null)
            .Take(MaxEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        foreach (var (oid, json) in clean)
        {
            _db.ChessableSessionMoves.Add(new ChessableSessionMove
            {
                UserId = userId, Bid = bid, Oid = oid, MovesJson = json!, CreatedAt = now,
            });
        }
        await _db.SaveChangesAsync(ct);

        await TrimToCapAsync(userId, ct);
        return clean.Count;
    }

    /// <summary>Hält den per-User-Bestand unter <see cref="MaxRowsPerUser"/> (älteste zuerst raus).
    /// Best-effort — ein Fehler hier darf den erfolgreichen Append nicht kippen.</summary>
    private async Task TrimToCapAsync(int userId, CancellationToken ct)
    {
        try
        {
            var count = await _db.ChessableSessionMoves.CountAsync(s => s.UserId == userId, ct);
            if (count <= MaxRowsPerUser) return;
            var overflow = await _db.ChessableSessionMoves
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.Id)
                .Take(count - MaxRowsPerUser)
                .ToListAsync(ct);
            _db.ChessableSessionMoves.RemoveRange(overflow);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>Validiert/normalisiert den moves-Block: muss ein nicht-leeres JSON-ARRAY sein
    /// (ein leerer Durchlauf trägt nichts). Zu groß/kaputt/fehlend → null (ignorieren).</summary>
    internal static string? NormalizeJson(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } arr) return null;
        if (arr.GetArrayLength() == 0) return null;
        var json = arr.GetRawText();
        return json.Length <= MaxJsonLength ? json : null;
    }
}
