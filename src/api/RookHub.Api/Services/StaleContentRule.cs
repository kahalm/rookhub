using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>Was mit einem VERALTETEN Kurs/Repertoire (<c>ImportVersion &lt; ImportPipeline.CurrentVersion</c>)
/// überhaupt geschehen kann.</summary>
public enum StaleAction
{
    /// <summary>Frisch von Chessable holen — nur mit dem RookHub-EIGENEN Chessable-Weg.</summary>
    Refetch,
    /// <summary>Aus der hier gespeicherten Quelle neu aufbereiten bzw. (Repertoire) auf die aktuelle
    /// Version setzen. Kein Netz, erledigt der „Aktualisieren"-Knopf.</summary>
    Local,
    /// <summary>Weder noch — ein SHOWSTOPPER: nur ein neuer Abruf über die RepCheck-Erweiterung
    /// (bzw. ein PGN-Re-Upload) bringt diesen Eintrag auf den aktuellen Stand. Solche Einträge tragen
    /// in der Liste ein (!) statt im Banner als anonyme Zahl zu stehen.</summary>
    Manual,
}

/// <summary>
/// EINE Regel dafür, was mit einem veralteten Kurs/Repertoire möglich ist — benutzt von
/// <see cref="ImportReprocessService"/> (Status + Lauf) UND von den Listen (<see cref="CourseService"/>,
/// <see cref="RepertoireService"/>) für die (!)-Markierung. Laufen die auseinander, verspricht die
/// Oberfläche etwas, das der Lauf dann überspringt (gemeldet 2026-09-20: „1 Kurs kann aktualisiert
/// werden" ließ sich nicht abräumen).
/// </summary>
public static partial class StaleContentRule
{
    /// <summary>Jüngster quell-abhängiger Marker (piratechess ≥ v1.0.39, Grundlage der
    /// Fortschritts-Overlays). Steht er in der gespeicherten Quelle, holt ein lokaler Re-Parse alles;
    /// fehlt er, braucht es einen echten Abruf.</summary>
    public const string ModernMarker = "[ChessableOid";

    public static bool HasModernMarkers(string? pgn) =>
        pgn != null && pgn.Contains(ModernMarker, StringComparison.Ordinal);

    public static bool IsChessable(string? tags, string fileName) =>
        (tags ?? string.Empty).Contains("chessable", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("chessable-", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^chessable-u\d+-(.+)\.pgn$", RegexOptions.IgnoreCase)]
    private static partial Regex ChessableBidRegex();

    public static bool TryParseBid(string fileName, out string bid)
    {
        var m = ChessableBidRegex().Match(fileName);
        bid = m.Success ? m.Groups[1].Value : string.Empty;
        return m.Success;
    }

    /// <summary>Ein Buch ist überhaupt per Chessable-Re-Fetch holbar (als Chessable-Import erkennbar
    /// UND bid aus dem Dateinamen lösbar).</summary>
    public static bool CanRefetch(string? tags, string fileName) =>
        IsChessable(tags, fileName) && TryParseBid(fileName, out _);

    /// <param name="chessableEnabled">Läuft der RookHub-EIGENE Chessable-Weg (<c>Chessable:Enabled</c>)?
    /// Ist er aus, wird KEIN Auftrag je abgearbeitet — dann gibt es kein <see cref="StaleAction.Refetch"/>.</param>
    public static StaleAction ActionForBook(bool hasSource, bool sourceModern, string? tags, string fileName, bool chessableEnabled)
        => chessableEnabled && CanRefetch(tags, fileName) && !sourceModern ? StaleAction.Refetch
            : hasSource ? StaleAction.Local
            : StaleAction.Manual;

    /// <summary>Repertoire: es gibt keine getrennte Quelle — das PGN IST die Quelle. Lokal heißt hier
    /// „nur auf die aktuelle Version setzen" (abgeleitete Daten wertet der Trainer live aus).</summary>
    public static StaleAction ActionForRepertoire(bool isChessable, bool sourceModern, bool chessableEnabled)
        => !isChessable || sourceModern ? StaleAction.Local
            : chessableEnabled ? StaleAction.Refetch
            : StaleAction.Manual;
}
