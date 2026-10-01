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
    /// <summary>Wirksames Byte-Kontingent je Konto über alle drei Chessable-Roh-Senken
    /// (<see cref="ChessableSinkBytes.MaxUserBytes"/>); nur Tests setzen es klein. Der Zeilendeckel allein ließ
    /// 200 000 × 64 KB = 12,8 GB je Gratis-Konto zu. Ist es erschöpft, wird nichts mehr angehängt.</summary>
    internal long UserBytesCap { get; init; } = ChessableSinkBytes.MaxUserBytes;

    private readonly AppDbContext _db;
    private readonly ChessableSinkBytes _sinkBytes;

    public ChessableSessionMoveService(AppDbContext db, ChessableSinkBytes? sinkBytes = null)
    {
        _db = db;
        _sinkBytes = sinkBytes ?? new ChessableSinkBytes();
    }

    public async Task<int> AppendBatchAsync(int userId, string bid,
        List<ChessableSessionMoveEntryDto> entries, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var clean = (entries ?? new())
            // oid in kanonischer Form (ChessableIds, A3-013).
            .Where(e => e is not null)
            .Select(e => (Oid: ChessableIds.CanonicalOid(e.Oid?.Trim())!, Json: NormalizeJson(e.Moves)))
            .Where(x => x.Oid is not null && x.Json is not null)
            .Take(MaxEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        var budget = _sinkBytes.ForUser(_db, userId, UserBytesCap, ct);
        var stored = 0;
        foreach (var (oid, json) in clean)
        {
            if (!await budget.TryTakeAsync(ChessableSinkBytes.Utf8(json) + ChessableSinkBytes.RowOverheadBytes))
                continue;   // Kontingent des Kontos erschöpft
            _db.ChessableSessionMoves.Add(new ChessableSessionMove
            {
                UserId = userId, Bid = bid, Oid = oid, MovesJson = json!, CreatedAt = now,
            });
            stored++;
        }
        if (stored == 0) return 0;
        await _db.SaveChangesAsync(ct);
        budget.Commit();

        await TrimToCapAsync(userId, ct);
        return stored;
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
