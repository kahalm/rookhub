using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>Was mit einem VERALTETEN Kurs/Repertoire (<c>ImportVersion &lt; ImportPipeline.CurrentVersion</c>)
/// überhaupt geschehen kann.</summary>
public enum StaleAction
{
    /// <summary>Frisch von Chessable holen — nur mit dem RookHub-EIGENEN Chessable-Weg.</summary>
    Refetch,
    /// <summary>Den Zugtext jeder Linie aus dem geteilten piratechess-Linien-Cache neu erzeugen (je oid,
    /// mit der AKTUELLEN piratechess-Logik), dann lokal aufbereiten — nur Kurse. Kein Chessable-Kontakt,
    /// kein Bearer, unabhängig von <c>Chessable:Enabled</c>; erledigt der „Aktualisieren"-Knopf.</summary>
    Cache,
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
    /// Fortschritts-Overlays). Steht er in der gespeicherten Quelle, kennt jede Linie ihre oid — dann kommt
    /// ein Chessable-Kurs aus dem Linien-Cache wieder auf den Stand der aktuellen piratechess-Logik, ein
    /// anderer Kurs per lokalem Re-Parse. Fehlt er, braucht ein Chessable-Kurs einen echten Abruf.</summary>
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

    /// <summary>
    /// Was mit einem veralteten KURS geschieht — die erste zutreffende Zeile gewinnt:
    /// <list type="table">
    /// <listheader><term>Fall</term><description>Bedingung → was der Lauf tut</description></listheader>
    /// <item><term><see cref="StaleAction.Refetch"/></term><description>eigener Chessable-Weg an, bid aus dem
    ///   Dateinamen lösbar, Quelle OHNE <c>[ChessableOid]</c> → Re-Fetch-Auftrag (nur so kommen die oids).</description></item>
    /// <item><term><see cref="StaleAction.Cache"/></term><description>Quelle MIT <c>[ChessableOid]</c> und ein
    ///   Chessable-Kurs → Zugtexte je oid aus dem Linien-Cache, dann lokal aufbereiten.</description></item>
    /// <item><term><see cref="StaleAction.Local"/></term><description>Quelle vorhanden (hochgeladenes PGN,
    ///   umgewandeltes Repertoire ohne oids, Chessable ohne eigenen Weg und ohne oids) → lokal aus der Quelle.</description></item>
    /// <item><term><see cref="StaleAction.Manual"/></term><description>keine Quelle → (!) in der Liste,
    ///   nur ein neuer Import hilft.</description></item>
    /// </list>
    /// Ein Chessable-Kurs mit oids geht bewusst IMMER über den Cache und nie mehr über <see cref="StaleAction.Local"/>:
    /// der lokale Weg war dort nur der Ersatz, solange es den Cache-Weg nicht gab. Ein lokaler Re-Parse brächte
    /// Änderungen an der PGN-Erzeugung in piratechess nie in den Kurs, setzte ihn aber auf die aktuelle
    /// Version — damit wäre er für den Cache-Weg verbrannt. Fällt piratechess aus, bleibt das Buch veraltet.
    /// </summary>
    /// <param name="chessableEnabled">Läuft der RookHub-EIGENE Chessable-Weg (<c>Chessable:Enabled</c>)?
    /// Ist er aus, wird KEIN Auftrag je abgearbeitet — dann gibt es kein <see cref="StaleAction.Refetch"/>.
    /// Den Cache-Weg sperrt der Schalter NICHT: der piratechess-Proxy läuft für die Extension-Wege ohnehin.</param>
    public static StaleAction ActionForBook(bool hasSource, bool sourceModern, string? tags, string fileName, bool chessableEnabled)
        => chessableEnabled && CanRefetch(tags, fileName) && !sourceModern ? StaleAction.Refetch
            : sourceModern && IsChessable(tags, fileName) ? StaleAction.Cache
            : hasSource ? StaleAction.Local
            : StaleAction.Manual;

    /// <summary>Repertoire: es gibt keine getrennte Quelle — das PGN IST die Quelle. Lokal heißt hier
    /// „nur auf die aktuelle Version setzen" (abgeleitete Daten wertet der Trainer live aus). Einen
    /// <see cref="StaleAction.Cache"/>-Fall gibt es hier (noch) nicht — siehe TODO.md.</summary>
    public static StaleAction ActionForRepertoire(bool isChessable, bool sourceModern, bool chessableEnabled)
        => !isChessable || sourceModern ? StaleAction.Local
            : chessableEnabled ? StaleAction.Refetch
            : StaleAction.Manual;
}
