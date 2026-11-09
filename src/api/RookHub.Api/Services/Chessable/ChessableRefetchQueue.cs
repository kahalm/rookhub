using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// ZENTRALES Einreihen von Chessable-Re-Fetch-Jobs für Kurse UND Repertoires des <see cref="ImportReprocessService"/>
/// (aus ihm herausgezogen, 2026-11-09). Hier liegen — an EINER Stelle statt je Pfad dupliziert — alle Vorsichtsmaßnahmen:
/// EINMALIGER Batch-Cache-Abruf statt teurem Einzel-Check je Kurs; kein Abbruch am Request-Token (Wegnavigieren darf das
/// Einreihen nicht killen → <see cref="CancellationToken.None"/>); Backoff für nicht-cachebare (truncated) Kurse, damit sie
/// Chessable nicht bei jedem Lauf neu fluten; Admin-Eigentums-Bypass (getHomeData listet nur einen Teil der Bibliothek).
/// Dedup gegen bereits laufende Importe steckt in <c>EnqueueReimportAsync</c>.
/// </summary>
/// <param name="chessableEnabled">Läuft der RookHub-eigene Chessable-Weg (<c>Chessable:Enabled</c>)?</param>
internal sealed class ChessableRefetchQueue(AppDbContext db, ICourseReimporter chessableImport, bool chessableEnabled)
{
    /// <summary>Wie lange ein nicht-cachebarer (truncated) Kurs nach einem erfolglosen Re-Fetch im
    /// automatischen Massen-Reprocess übersprungen wird, bevor er erneut versucht wird. Ein solcher
    /// Kurs wird von piratechess nicht gecacht und würde sonst bei JEDEM „Update all" komplett neu von
    /// Chessable geholt (Hunderte Line-Fetches → Block-Risiko). Ein gezielter Admin-Re-Import umgeht das.</summary>
    private static readonly TimeSpan IncompleteRefetchBackoff = TimeSpan.FromHours(24);

    /// <summary>Kandidat für einen Chessable-Re-Fetch — Kurs ODER Repertoire.</summary>
    internal readonly record struct RefetchCandidate(int OwnerId, string Bid, string Target, string Name, int? TargetRepertoireId);

    /// <summary>Reiht <paramref name="candidates"/> ein und zählt Enqueued/Skipped in <paramref name="result"/>.</summary>
    public async Task EnqueueAsync(IReadOnlyList<RefetchCandidate> candidates, bool isAdmin, ReprocessResultDto result, IReadOnlySet<string>? knownCachedBids = null)
    {
        if (candidates.Count == 0) return;
        if (!chessableEnabled)
        {
            // Sicherheitsnetz: ohne die eigenen Lanes würde ein angelegter Auftrag nie abgearbeitet — er
            // stünde als „läuft" in der Liste und blockierte über die Dedup-Regel jeden späteren Versuch.
            result.Skipped += candidates.Count;
            return;
        }

        // Cache-Status ALLER Kandidaten EINMAL en bloc (1 piratechess-Aufruf) statt je Kurs ein teurer
        // Einzel-Check, der den ganzen Cache-Blob lädt+entpackt. Der Aufrufer kann die Menge bereits
        // ermittelt haben (Repertoire-Reprocess braucht sie schon für die Bearer-lose Cache-Entscheidung).
        var cachedBids = knownCachedBids ?? await chessableImport.GetCachedBidsAsync(CancellationToken.None);

        foreach (var c in candidates)
        {
            var cached = cachedBids.Contains(c.Bid);
            // Backoff: ein nicht-cachebarer (truncated) Kurs würde bei jedem Lauf komplett neu von Chessable
            // geholt. Wurde er kürzlich schon (erfolglos = weiterhin nicht gecacht) geholt, jetzt überspringen.
            if (!cached)
            {
                var lastDone = await db.ChessableImports
                    .Where(i => i.Bid == c.Bid && i.Status == ChessableImportStatus.Completed && i.CompletedAt != null)
                    .OrderByDescending(i => i.CompletedAt)
                    .Select(i => i.CompletedAt)
                    .FirstOrDefaultAsync(CancellationToken.None);
                if (lastDone.HasValue && lastDone.Value > DateTime.UtcNow - IncompleteRefetchBackoff)
                {
                    result.Skipped++;
                    continue;
                }
            }

            // trustOwnership=isAdmin: getHomeData listet nur einen Teil der Bibliothek → sonst würden eigene,
            // längst importierte Kurse fälschlich als „nicht besessen" abgewiesen; Admins dürfen ohnehin jeden
            // Kurs holen (Nicht-Admin bleibt geprüft = v0.203.9-Schutz gegen Cached-Content-Diebstahl).
            var importId = await chessableImport.EnqueueReimportAsync(
                c.OwnerId, c.Bid, c.Target, c.Name, targetRepertoireId: c.TargetRepertoireId,
                knownCached: cached, trustOwnership: isAdmin, ct: CancellationToken.None);
            if (importId != null) result.Enqueued++;
            else result.Skipped++;
        }
    }
}
