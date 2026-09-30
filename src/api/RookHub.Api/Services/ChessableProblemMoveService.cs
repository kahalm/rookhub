using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Upsert-Senke für die „schwierigen Züge" aus Chessable (siehe <see cref="ChessableProblemMove"/>).
/// Die Extension schickt Batches aus zwei Quellen, die sich ergänzen: getList liefert je Linie nur
/// den Zähler (<c>nHard</c>), getGame die Zug-Details (<c>problemMoves.thisUser</c>) + lastReviewed.
/// Felder werden daher NUR überschrieben, wenn der Batch sie mitbringt (sonst bliebe nach einem
/// Kapitel-Listen-Update das Zug-Detail-JSON nicht erhalten bzw. umgekehrt).
/// </summary>
public class ChessableProblemMoveService
{
    /// <summary>Deckel je Batch — ein ganzer Kurs-Crawl kommt in mehreren Batches.</summary>
    public const int MaxEntriesPerBatch = 500;
    /// <summary>Deckel fürs Zug-Detail-JSON einer Linie (thisUser ist normal wenige hundert Bytes).</summary>
    public const int MaxJsonLength = 16_000;
    /// <summary>Wirksames Byte-Kontingent je Konto über alle drei Chessable-Roh-Senken
    /// (<see cref="ChessableSinkBytes.MaxUserBytes"/>); nur Tests setzen es klein. Vorher gab es hier gar keinen
    /// Mengendeckel — beliebige oids, je Batch 500 × 16 KB.</summary>
    internal long UserBytesCap { get; init; } = ChessableSinkBytes.MaxUserBytes;

    private readonly AppDbContext _db;
    private readonly ChessableSinkBytes _sinkBytes;

    public ChessableProblemMoveService(AppDbContext db, ChessableSinkBytes? sinkBytes = null)
    {
        _db = db;
        _sinkBytes = sinkBytes ?? new ChessableSinkBytes();
    }

    public async Task<int> UpsertBatchAsync(int userId, string bid,
        List<ChessableProblemMoveEntryDto> entries, CancellationToken ct = default)
    {
        var clean = (entries ?? new())
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Oid)
                && e.Oid.Trim().Length <= 32 && e.Oid.Trim().All(char.IsAsciiDigit))
            .GroupBy(e => e.Oid.Trim())
            .Select(g => g.Last())   // letzter Stand je oid gewinnt innerhalb des Batches
            .Take(MaxEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        var oids = clean.Select(e => e.Oid.Trim()).ToList();
        var existing = await _db.ChessableProblemMoves
            .Where(p => p.UserId == userId && p.Bid == bid && oids.Contains(p.Oid))
            .ToDictionaryAsync(p => p.Oid, ct);
        var budget = _sinkBytes.ForUser(_db, userId, UserBytesCap, ct);

        var now = DateTime.UtcNow;
        var written = 0;
        foreach (var e in clean)
        {
            var oid = e.Oid.Trim();
            existing.TryGetValue(oid, out var row);
            var json = e.ProblemMoves is { } pm ? NormalizeJson(pm) : null;
            var delta = (row is null ? ChessableSinkBytes.RowOverheadBytes : 0)
                + (json is null ? 0 : ChessableSinkBytes.Utf8(json) - ChessableSinkBytes.Utf8(row?.ProblemMovesJson));
            if (!await budget.TryTakeAsync(delta))
            {
                // Kontingent des Kontos erschöpft: keine neue Zeile; bei einer bestehenden laufen Zähler und
                // Datum weiter, nur das größere Zug-Detail nicht.
                if (row is null) continue;
                json = null;
            }
            if (row is null)
            {
                row = new ChessableProblemMove { UserId = userId, Bid = bid, Oid = oid };
                _db.ChessableProblemMoves.Add(row);
                existing[oid] = row;
            }
            if (e.NHard is int nh) row.NHard = Math.Clamp(nh, 0, 10_000);
            if (json is not null) row.ProblemMovesJson = json;
            if (e.LastReviewed is not null)
                row.LastReviewedAt = ParseLastReviewed(e.LastReviewed);
            row.UpdatedAt = now;
            written++;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            budget.Commit();
        }
        catch (DbUpdateException)
        {
            // Race auf dem Unique-Index (paralleler Flush desselben Users): Batch ist idempotent —
            // verwerfen, der nächste Flush bringt denselben Stand erneut.
            _db.ChangeTracker.Clear();
        }
        return written;
    }

    /// <summary>Validiert/normalisiert das thisUser-Objekt: muss ein JSON-OBJEKT sein (auch leer —
    /// „{}" löscht frühere Fehlzüge, die Linie läuft jetzt sauber). Zu groß/kaputt → null (ignorieren).</summary>
    internal static string? NormalizeJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var json = element.GetRawText();
        return json.Length <= MaxJsonLength ? json : null;
    }

    /// <summary>Chessables lastReviewed: "never" bzw. Unfug → null, sonst "yyyy-MM-dd HH:mm:ss" (UTC).</summary>
    internal static DateTime? ParseLastReviewed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("never", StringComparison.OrdinalIgnoreCase))
            return null;
        return DateTime.TryParse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt) ? dt : null;
    }
}
