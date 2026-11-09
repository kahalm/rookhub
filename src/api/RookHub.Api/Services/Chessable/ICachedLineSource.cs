namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Schmale Abstraktion über den geteilten piratechess-Linien-Cache (implementiert von
/// <see cref="ChessableProxyService"/>) — macht den Cache-Weg des <see cref="ImportReprocessService"/> testbar
/// wie <see cref="ICourseReimporter"/> den Re-Fetch-Weg.
/// </summary>
public interface ICachedLineSource
{
    /// <summary>Welche der oids liegen im Cache (nur Existenz, billig) — so, dass piratechess einen Import des Kurses
    /// <paramref name="bid"/> damit füllt. Weich: Fehler → leere Menge.</summary>
    Task<HashSet<string>> GetCachedLineOidsAsync(string bid, IReadOnlyCollection<string> oids, CancellationToken ct = default);

    /// <summary>PGN je gecachter oid des Kurses <paramref name="bid"/>, mit der AKTUELLEN piratechess-Logik erzeugt.
    /// Nicht gecachte oids fehlen; ein Verbindungsfehler WIRFT.</summary>
    Task<Dictionary<string, string>> GetCachedLinePgnsAsync(string bid, IEnumerable<string> oids, string mode = "None", CancellationToken ct = default);
}
