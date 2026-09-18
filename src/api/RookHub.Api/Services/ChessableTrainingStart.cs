using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Trägt den fehlenden Trainingsstart Chessable-stämmiger Linien nach: den Header
/// <c>[ChessableColor "white|black"]</c> für Linien, die weder einen <c>[%tqu]</c>-Marker noch die
/// Farbe tragen.
/// <para><b>Warum:</b> piratechess liefert das REPERTOIRE-PGN im Modus „None" — ohne Trainingsmarker,
/// denn ein Repertoire wird von vorne durchgespielt. Wandelt der Nutzer ein solches Repertoire in
/// einen KURS um, fehlt damit die Angabe, ob der erste Zug dem Trainierenden oder dem GEGNER gehört.
/// Chessables Partie-Kurse stellen die Aufgabe oft als „der Gegner hat gerade 10…Sd4 gespielt,
/// widerlege das": dort gehört er dem Gegner und wird vorgespielt. Ohne die Angabe galt die Linie als
/// „ab der FEN lösen", und im Kurs stand die falsche Seite am Zug (gemeldet 2026-09-18).</para>
/// <para>Für NEU geholte Kurse schreibt piratechess die Farbe selbst als Header. Für den Altbestand
/// wird sie hier aus dem geteilten Linien-Cache nachgezogen: dieselbe Linie im Modus „FirstKeyMove"
/// trägt den Marker, und aus dessen Position folgt die Farbe (siehe <see cref="SolverColorOf"/>).</para>
/// </summary>
internal static class ChessableTrainingStart
{
    /// <summary>Modus, in dem der Linien-Cache den Marker setzt (piratechess: ein <c>[%tqu]</c> am
    /// ersten Schlüsselzug der Solverfarbe).</summary>
    internal const string MarkerMode = "FirstKeyMove";

    internal const string ColorHeaderPrefix = "[ChessableColor \"";

    private static readonly Regex OidRegex = new("\\[ChessableOid \"([^\"]+)\"\\]", RegexOptions.Compiled);
    private static readonly Regex FenRegex = new("\\[FEN \"([^\"]+)\"\\]", RegexOptions.Compiled);
    private static readonly Regex EventSplitRegex = new(@"(?=\[Event )", RegexOptions.Compiled);

    /// <summary>
    /// Die oids der Linien, denen der Trainingsstart fehlt: mit <c>[ChessableOid]</c>, aber ohne
    /// <c>[%tqu]</c> im Zugtext und ohne <c>[ChessableColor]</c>. Nur diese müssen nachgefragt werden —
    /// ein gewöhnliches Nutzer-PGN ohne oids liefert eine leere Liste und löst keinen Abruf aus.
    /// </summary>
    internal static IReadOnlyList<string> OidsWithoutStart(string? pgn)
    {
        var result = new List<string>();
        foreach (var block in SplitGames(pgn))
        {
            var oid = OidRegex.Match(block);
            if (!oid.Success) continue;
            if (block.Contains(ColorHeaderPrefix, StringComparison.Ordinal)) continue;
            if (RepertoirePgnCleanup.MovetextOf(block).Contains("[%tqu", StringComparison.OrdinalIgnoreCase)) continue;
            var value = oid.Groups[1].Value;
            if (!result.Contains(value, StringComparer.Ordinal)) result.Add(value);
        }
        return result;
    }

    /// <summary>
    /// Solverfarbe eines Blocks MIT Marker. <see cref="PgnParser.FindTquMoveIndex"/> liefert den Index
    /// des letzten VORGESPIELTEN Halbzugs; der Löser zieht also bei <c>k+1</c>, und dessen Farbe folgt
    /// aus der Zugfarbe der FEN. <c>null</c>, wenn der Block keinen Marker oder keine FEN trägt — dann
    /// wird nichts geraten und die Linie bleibt, wie sie war.
    /// </summary>
    internal static string? SolverColorOf(string? block)
    {
        if (string.IsNullOrWhiteSpace(block)) return null;
        var fen = FenRegex.Match(block);
        if (!fen.Success) return null;
        var k = PgnParser.FindTquMoveIndex(RepertoirePgnCleanup.MovetextOf(block));
        if (k is null) return null;

        var parts = fen.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var whiteToMove = parts.Length < 2 || !parts[1].Equals("b", StringComparison.OrdinalIgnoreCase);
        var solverIsWhite = (k.Value + 1) % 2 == 0 ? whiteToMove : !whiteToMove;
        return solverIsWhite ? "white" : "black";
    }

    /// <summary>
    /// Trägt je Block mit passender oid den Header <c>[ChessableColor]</c> hinter dessen letztem Header
    /// ein — in place, der übrige Text bleibt Zeichen für Zeichen erhalten (gleiches Vorgehen wie
    /// <see cref="ChessableImportService.InsertChessableOids"/>). Blöcke ohne oid, ohne Treffer in
    /// <paramref name="colorByOid"/> oder mit bereits vorhandenem Farb-Header bleiben unangetastet.
    /// </summary>
    internal static string InsertColors(string pgn, IReadOnlyDictionary<string, string> colorByOid)
    {
        if (string.IsNullOrWhiteSpace(pgn) || colorByOid.Count == 0) return pgn;
        var first = pgn.IndexOf("[Event ", StringComparison.Ordinal);
        if (first < 0) return pgn;

        var starts = Regex.Matches(pgn[first..], @"\[Event ").Select(m => first + m.Index).ToList();
        var sb = new StringBuilder(pgn);
        // Von hinten einfügen, damit die vorderen Positionen gültig bleiben.
        for (var i = starts.Count - 1; i >= 0; i--)
        {
            var end = i + 1 < starts.Count ? starts[i + 1] : pgn.Length;
            var block = pgn[starts[i]..end];
            if (block.Contains(ColorHeaderPrefix, StringComparison.Ordinal)) continue;
            var oid = OidRegex.Match(block);
            if (!oid.Success || !colorByOid.TryGetValue(oid.Groups[1].Value, out var color)) continue;
            if (color is not ("white" or "black")) continue;   // nichts anderes in einen Header schreiben
            var at = ChessableImportService.HeaderEnd(pgn, starts[i], end);
            if (at >= 0) sb.Insert(at, $"\n{ColorHeaderPrefix}{color}\"]");
        }
        return sb.ToString();
    }

    private static IEnumerable<string> SplitGames(string? pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) yield break;
        var idx = pgn.IndexOf("[Event ", StringComparison.Ordinal);
        if (idx < 0) yield break;
        foreach (var part in EventSplitRegex.Split(pgn[idx..]))
            if (!string.IsNullOrWhiteSpace(part)) yield return part;
    }
}
