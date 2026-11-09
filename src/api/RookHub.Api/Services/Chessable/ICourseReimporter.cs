namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Schmale Abstraktion fürs erneute Einreihen eines Chessable-Kurs-Imports — entkoppelt den
/// <see cref="ImportReprocessService"/> von der vollen <see cref="ChessableImportService"/>
/// (die diese Schnittstelle implementiert) und macht den Re-Fetch-Pfad testbar.
/// </summary>
public interface ICourseReimporter
{
    /// <param name="knownCached">Vorab bekannter Cache-Status des Kurses (aus einem Batch-Abruf), um den
    /// teuren Einzel-Cache-Check je Kurs zu sparen. null ⇒ der Reimporter ermittelt ihn selbst.</param>
    /// <param name="trustOwnership">true ⇒ die Eigentumsprüfung gegen die Chessable-Bibliothek überspringen.
    /// Für den Admin-Massen-Reprocess bereits im Bestand befindlicher Kurse: Chessables getHomeData listet
    /// nur einen Teil der Bibliothek (Home-Ansicht) → sonst würden eigene, längst importierte Kurse fälschlich
    /// als „nicht besessen" übersprungen. Admins dürfen ohnehin jeden Kurs holen, daher unbedenklich.</param>
    Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, int? targetRepertoireId = null, bool? knownCached = null, bool trustOwnership = false, CancellationToken ct = default);

    /// <summary>Alle im piratechess-DB-Cache vorliegenden Kurs-Bids auf einen Schlag (1 Aufruf statt
    /// N Einzel-Cache-Checks) — für den Massen-Reprocess, um die Lane-Klassifikation vorab zu füllen.</summary>
    Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default);
}
