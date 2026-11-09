using RookHub.Api.Data;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Anonyme „+“-Markierungen (<c>MarkedPositions</c>) und ihre Notiz-Fassungen (<c>MarkedPositionNoteRevisions</c>) ohne Konto
/// (R4-003, Entscheidung E7): dieselbe Frist und derselbe Gesamtdeckel wie die übrigen anonymen Senken
/// (<see cref="RetentionOptions.AnonymousDays"/>, <see cref="RetentionOptions.AnonymousCap"/>, siehe
/// <see cref="Puzzles.AnonymousSinkRetention"/>). Vorher blieben sie samt frei getippter Kommentare für immer — die Kennung
/// (<c>X-Visitor-Id</c>) ist frei wählbar, der Deckel je Kennung band also nichts. Wer sich anmeldet, übernimmt seine
/// Markierungen (<c>claim-session</c>); Zeilen mit Konto bleiben immer. Gemessen an <c>CreatedAt</c> (eine Markierung hat
/// keinen späteren Zeitstempel; eine neue Notiz-Fassung ist eine neue Zeile).
/// </summary>
public static class AnonymousMarkRetention
{
    public static async Task<int> PruneAsync(AppDbContext db, DateTime nowUtc, RetentionOptions o,
        int chunkSize = RetentionDelete.ChunkSize, CancellationToken ct = default)
    {
        var cutoff = nowUtc.AddDays(-o.AnonymousDays);
        var marks = db.MarkedPositions.Where(m => m.UserId == null);
        var removed = await RetentionDelete.AllAsync(db, marks.Where(m => m.CreatedAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.OverCapAsync(db, marks, m => m.CreatedAt, o.AnonymousCap, chunkSize, ct);

        var revisions = db.MarkedPositionNoteRevisions.Where(r => r.UserId == null);
        removed += await RetentionDelete.AllAsync(db, revisions.Where(r => r.CreatedAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.OverCapAsync(db, revisions, r => r.CreatedAt, o.AnonymousCap, chunkSize, ct);
        return removed;
    }
}
