using RookHub.Api.Services.Courses;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Reine Regeln, wohin ein Chessable-Import schreibt: Ziel (Buch/Repertoire), Parser-Modus, Dateinamen,
/// Anzeigename und Link der Benachrichtigung. Aus <see cref="ChessableImportService"/> herausgelöst
/// (Zerlegung Z3) — vorher standen dieselben Ausdrücke an jeder Stelle einzeln.
/// </summary>
public static class ChessableImportTargets
{
    /// <summary>„book" bleibt „book", alles andere ist „repertoire".</summary>
    internal static string Normalize(string? target) => target == "book" ? "book" : "repertoire";

    /// <summary>piratechess-Modus: Buch → erster Key-Zug trainierbar, Repertoire → reines PGN.</summary>
    internal static string ParseMode(string target) => target == "book" ? "FirstKeyMove" : "None";

    /// <summary>Pro-User-eindeutiger Dateiname des Kurs-Buchs.</summary>
    internal static string BookFileName(int userId, string bid) => $"chessable-u{userId}-{bid}.pgn";

    /// <summary>Stabiler Dateiname der Repertoire-PGN-Datei eines Kurses.</summary>
    internal static string RepertoireFileName(string bid) => $"chessable-{bid}.pgn";

    /// <summary>Kursname oder „Chessable {bid}" (ungekürzt).</summary>
    internal static string NameOrFallback(string? courseName, string bid)
        => !string.IsNullOrWhiteSpace(courseName) ? courseName! : $"Chessable {bid}";

    /// <summary>Kursname oder „Chessable {bid}", auf 200 Zeichen gekürzt (Spaltenbreite).</summary>
    internal static string DisplayName(string? courseName, string bid)
        => Trunc(string.IsNullOrWhiteSpace(courseName) ? $"Chessable {bid}" : courseName!, 200);

    /// <summary>Ziel-Ansicht der Fertig-Benachrichtigung.</summary>
    internal static string ResultLink(string target) => target == "book" ? "/courses" : "/repertoires";

    /// <summary>Traegt ein Buch noch den automatisch aus dem Dateinamen abgeleiteten Anzeigenamen
    /// (leer, der Dateiname selbst oder ohne Endung)? Dann darf der Kursname ihn ersetzen.</summary>
    internal static bool IsFileNameDerived(string? displayName, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return true;
        var file = fileName ?? string.Empty;
        return displayName == file
            || displayName == Path.GetFileNameWithoutExtension(file)
            || displayName == PgnImportService.CleanDisplayName(file);
    }

    internal static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;
}
