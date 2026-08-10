using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// Reiner, DB-freier Konverter einer Chessable-<c>getReview</c>-Antwort in ein PGN im
/// <b>exakt gleichen Stil</b> wie <c>PirateChessLib.Game.GeneratePGN</c> (getGame-Weg).
///
/// <para>Warum PGN und nicht direkt <see cref="Models.BookPuzzle"/>-Felder: Der getGame-Weg läuft
/// <c>getGame → GeneratePGN → PGN → <see cref="PgnImportService.ImportFileAsync"/> → BookPuzzle</c>.
/// Erzeugt <c>getReview</c> DENSELBEN PGN-Stil und geht durch DENSELBEN <see cref="PgnImportService"/>,
/// ist die Linie garantiert identisch formatiert (UCI-Moves, AltMoves, MoveComments, MoveShapes,
/// StartPly, LineId) — kein Drift, kein zweiter Feld-Parser. Dieser Konverter baut daher NUR das PGN.</para>
///
/// <para>getReview liefert je Eintrag in <c>lesson.moves[]</c> einen VOLLZUG (move_white/move_black als
/// SAN, comment_*, drawing_*, alt_*, isKey, informational). Der Konverter flacht das zu Halbzügen ab und
/// setzt die Annotationen an denselben Stellen/Formaten wie GeneratePGN: <c>[%cal]</c> (Pfeile, Farbe
/// GROSS, Start+End aneinander), <c>[%csl]</c> (Kreise), <c>[%alt]</c> (SPACE-getrennte SAN — genau das
/// Format, das <see cref="PgnParser.ExtractAltMoves"/> mit <c>Split(' ')</c> erwartet), <c>[%tqu]</c> als
/// Kommentar-Vorspann VOR dem ersten Schlüsselzug der Solverfarbe (→ <c>StartPly</c>) und optional
/// <c>[%info]</c> vor dem ersten Zug (→ <c>IsInfoOnly</c>). <c>[%cal]/[%csl]/[%alt]</c> + Text stehen —
/// wie in GeneratePGN — zusammen in EINEM <c>{…}</c>-Block NACH dem Halbzug.</para>
/// </summary>
public static partial class ChessableReviewParser
{
    /// <summary>Standard-Grundstellung (Fallback-Initial-FEN).</summary>
    private const string StartPositionFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTagRegex();
    [GeneratedRegex(@"@@StartFEN@@(.+?)@@EndFEN@@")]
    private static partial Regex FenTagRegex();
    [GeneratedRegex(@"^([a-h]?x?[a-h][18])=?([nbrqNBRQ])$")]
    private static partial Regex PromotionRegex();

    /// <summary>Ergebnis der Konvertierung: identifizierende Chessable-IDs + Chessable-Stil-PGN.</summary>
    public record ReviewLine(string Oid, string Bid, string? ChapterTitle, string Pgn);

    private sealed class HalfMove
    {
        public string San = "";
        public bool IsWhite;
        public bool IsKey;
        public string? AfterJson;      // comment_white / comment_black (ResponseMove-JSON)
        public string? BeforeJson;     // comment_before_white / comment_before_black
        public List<Draw> Draws = new();
        public List<string> Alts = new();
    }

    private readonly record struct Draw(string Object, string Start, string End, string Color);

    /// <summary>
    /// Konvertiert eine <c>getReview</c>-JSON-Antwort in ein importierbares PGN im GeneratePGN-Stil.
    /// Liefert <c>null</c> bei unbrauchbarer Eingabe (leer/kaputtes JSON, keine Züge, nicht spielbare
    /// Hauptlinie, fehlende oid/bid) — wirft NIE.
    /// </summary>
    public static ReviewLine? TryConvert(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("lesson", out var lesson) || lesson.ValueKind != JsonValueKind.Object)
                return null;
            if (!lesson.TryGetProperty("moves", out var movesEl) || movesEl.ValueKind != JsonValueKind.Array)
                return null;

            var rows = movesEl.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object).ToList();
            if (rows.Count == 0) return null;
            var first = rows[0];

            // ---- identifizierende IDs (Zahlen im JSON → als String) ----
            var oid = ReadStr(first, "oid");
            var bid = ReadStr(first, "bid");
            if (string.IsNullOrEmpty(bid)) bid = ReadStr(first, "book_id");
            lesson.TryGetProperty("chapter", out var chapterEl);
            if (string.IsNullOrEmpty(bid) && chapterEl.ValueKind == JsonValueKind.Object)
                bid = ReadStr(chapterEl, "bid");
            if (string.IsNullOrEmpty(oid) || oid.Length > 32) return null;
            if (string.IsNullOrEmpty(bid) || bid.Length > 12) return null;

            var chapterTitle = ReadStr(first, "chapterTitle");
            if (string.IsNullOrEmpty(chapterTitle) && chapterEl.ValueKind == JsonValueKind.Object)
                chapterTitle = ReadStr(chapterEl, "title");
            var chapter = string.IsNullOrEmpty(chapterTitle) ? null : chapterTitle;

            var openingName = ReadStr(first, "opening_name");
            if (string.IsNullOrEmpty(openingName)) openingName = ReadStr(first, "book_name");

            // ---- Solverfarbe (steuert, welcher Schlüsselzug den Trainingsstart markiert) ----
            var studyCol = ReadStr(lesson, "studyCol");
            if (string.IsNullOrEmpty(studyCol)) studyCol = ReadStr(first, "studyCol");
            bool? solverIsWhite = studyCol.Equals("white", StringComparison.OrdinalIgnoreCase) ? true
                : studyCol.Equals("black", StringComparison.OrdinalIgnoreCase) ? false
                : (bool?)null;

            // ---- Initial-FEN (Stellung VOR dem ersten Zug = move_fen des ersten Eintrags) ----
            var initialFen = ReadStr(first, "move_fen");
            if (string.IsNullOrWhiteSpace(initialFen)) initialFen = StartPositionFen;

            // ---- Halbzüge einsammeln (leere Seite am Linienende überspringen) ----
            var halfMoves = new List<HalfMove>();
            foreach (var row in rows)
            {
                var isKey = ReadBool(row, "isKey");
                var mw = ReadStr(row, "move_white");
                if (mw.Length > 0)
                    halfMoves.Add(new HalfMove
                    {
                        San = mw, IsWhite = true, IsKey = isKey,
                        AfterJson = ReadStr(row, "comment_white"),
                        BeforeJson = ReadStr(row, "comment_before_white"),
                        Draws = ParseDraws(row, "drawing_white"),
                        Alts = ParseAltArray(ReadStr(row, "alt_white")),
                    });
                var mb = ReadStr(row, "move_black");
                if (mb.Length > 0)
                    halfMoves.Add(new HalfMove
                    {
                        San = mb, IsWhite = false, IsKey = isKey,
                        AfterJson = ReadStr(row, "comment_black"),
                        BeforeJson = ReadStr(row, "comment_before_black"),
                        Draws = ParseDraws(row, "drawing_black"),
                        Alts = ParseAltArray(ReadStr(row, "alt_black")),
                    });
            }
            if (halfMoves.Count == 0) return null;

            var isInfoLine = ReadBool(first, "informational");

            // ---- Hauptlinie über das Brett validieren + UCI je Halbzug (für die [%tqu]-uci) ----
            var (ucis, usedFen) = ReplayMainline(initialFen, halfMoves);
            if (ucis == null || usedFen == null) return null;   // nicht spielbar ⇒ unbrauchbar
            initialFen = usedFen;

            // ---- Trainingsstart: erster Schlüsselzug der Solverfarbe (wie GeneratePGN) ----
            var tquIndex = -1;
            for (var i = 0; i < halfMoves.Count; i++)
            {
                var hm = halfMoves[i];
                if (hm.IsKey && (solverIsWhite == null || solverIsWhite.Value == hm.IsWhite)) { tquIndex = i; break; }
            }
            var tquUci = tquIndex >= 0 && tquIndex < ucis.Count ? (ucis[tquIndex] ?? "") : "";

            // ---- Movetext bauen ----
            var (startFullMove, startWhite) = FenMoveState(initialFen);
            var sb = new StringBuilder();
            var moveNum = startFullMove;
            var prevWasWhite = false;   // wurde der Weiß-Halbzug DIESES Vollzugs unmittelbar davor gesetzt?
            for (var i = 0; i < halfMoves.Count; i++)
            {
                var hm = halfMoves[i];

                // Kommentar-Vorspann: [%info] (nur erster Zug) + [%tqu] (Trainingsstart) + Vor-Text.
                var before = new List<string>();
                if (i == 0 && isInfoLine) before.Add("[%info]");
                if (i == tquIndex) before.Add($"[%tqu \"En\",\"find the move\",\"\",\"\",\"{tquUci}\",\"\",10]");
                var beforeText = ExtractComment(hm.BeforeJson);
                if (!string.IsNullOrEmpty(beforeText)) before.Add(beforeText);
                if (before.Count > 0) sb.Append('{').Append(string.Join("\n", before)).Append("} ");

                // Vollzugnummer (vom PgnParser ohnehin verworfen, aber sauberer Chessable-Stil).
                if (hm.IsWhite) sb.Append(moveNum).Append(". ");
                else if (!prevWasWhite) sb.Append(moveNum).Append("... ");
                sb.Append(hm.San).Append(' ');

                // Annotation NACH dem Halbzug: [%cal][%csl][%alt] + Text in EINEM {…}-Block.
                var ann = new StringBuilder();
                AppendArrows(ann, hm.Draws);
                AppendCircles(ann, hm.Draws);
                AppendAlts(ann, hm.Alts, hm.San);
                var afterText = ExtractComment(hm.AfterJson);
                if (!string.IsNullOrEmpty(afterText)) ann.Append(afterText);
                if (ann.Length > 0) sb.Append('{').Append(ann).Append("} ");

                if (!hm.IsWhite) moveNum++;
                prevWasWhite = hm.IsWhite;
            }

            var oidTag = oid.All(char.IsAsciiDigit) ? $"[ChessableOid \"{oid}\"]\n" : "";
            var pgn =
                $"[Event \"Chessable\"]\n" +
                $"[Round \"{EscapeHeader(oid)}\"]\n" +
                $"[White \"{EscapeHeader(openingName)}\"]\n" +
                $"[Black \"{EscapeHeader(chapter)}\"]\n" +
                $"[FEN \"{EscapeHeader(initialFen)}\"]\n" +
                $"[Result \"*\"]\n" +
                oidTag +
                "\n" +
                sb.ToString().TrimEnd() + "\n";

            return new ReviewLine(oid, bid, chapter, pgn);
        }
        catch
        {
            return null; // jede Unwägbarkeit (kaputtes JSON etc.) ⇒ unbrauchbar, kein Wurf
        }
    }

    // ---- Brett-Replay: Hauptlinie validieren + UCI je Halbzug -------------------------------------
    private static (List<string?>? ucis, string? fen) ReplayMainline(string initialFen, List<HalfMove> halfMoves)
    {
        var (ucis, ok) = TryReplay(initialFen, halfMoves);
        if (ok) return (ucis, initialFen);
        // move_fen[0] gelegentlich verrutscht: als Rückfall die Grundstellung probieren.
        if (!FenBoardEquals(initialFen, StartPositionFen))
        {
            (ucis, ok) = TryReplay(StartPositionFen, halfMoves);
            if (ok) return (ucis, StartPositionFen);
        }
        return (null, null);
    }

    private static (List<string?> ucis, bool ok) TryReplay(string fen, List<HalfMove> halfMoves)
    {
        var ucis = new List<string?>(halfMoves.Count);
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            foreach (var hm in halfMoves)
            {
                var san = CleanSan(hm.San);
                if (san.Length == 0 || !board.Move(san)) return (ucis, false);
                ucis.Add(ToUci(board.ExecutedMoves[^1]));
            }
            return (ucis, true);
        }
        catch
        {
            return (ucis, false);
        }
    }

    private static string ToUci(Move m)
    {
        var u = m.OriginalPosition.ToString() + m.NewPosition.ToString();
        var ss = m.Parameter?.ShortStr;
        if (!string.IsNullOrEmpty(ss) && ss.StartsWith('=') && ss.Length >= 2)
            u += char.ToLowerInvariant(ss[1]);
        return u;
    }

    /// <summary>SAN für Gera.Chess bereinigen (0-0→O-O, Suffixe weg, Umwandlung → kanonisch "=Q").</summary>
    private static string CleanSan(string token)
    {
        var t = token.Trim();
        if (t.Length == 0) return "";
        t = t.Replace("0-0-0", "O-O-O").Replace("0-0", "O-O").TrimEnd('!', '?', '+', '#');
        var pm = PromotionRegex().Match(t);
        if (pm.Success) t = pm.Groups[1].Value + "=" + char.ToUpperInvariant(pm.Groups[2].Value[0]);
        return t;
    }

    // ---- FEN-Helfer -------------------------------------------------------------------------------
    private static (int fullMove, bool whiteToMove) FenMoveState(string fen)
    {
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var whiteToMove = parts.Length < 2 || parts[1] != "b";
        var fullMove = parts.Length >= 6 && int.TryParse(parts[5], out var n) && n > 0 ? n : 1;
        return (fullMove, whiteToMove);
    }

    private static bool FenBoardEquals(string a, string b)
        => a.Split(' ', 2)[0] == b.Split(' ', 2)[0];

    // ---- Annotationen -----------------------------------------------------------------------------
    private static void AppendArrows(StringBuilder ann, List<Draw> draws)
    {
        var arrows = draws.Where(d => d.Object == "arrow" && d.Start.Length > 0 && d.End.Length > 0).ToList();
        if (arrows.Count == 0) return;
        ann.Append("[%cal ");
        for (var i = 0; i < arrows.Count; i++)
        {
            if (i > 0) ann.Append(',');
            ann.Append(arrows[i].Color.ToUpperInvariant()).Append(arrows[i].Start).Append(arrows[i].End);
        }
        ann.Append(']');
    }

    private static void AppendCircles(StringBuilder ann, List<Draw> draws)
    {
        var circles = draws.Where(d => d.Object == "circle" && d.Start.Length > 0).ToList();
        if (circles.Count == 0) return;
        ann.Append("[%csl ");
        for (var i = 0; i < circles.Count; i++)
        {
            if (i > 0) ann.Append(',');
            ann.Append(circles[i].Color.ToUpperInvariant()).Append(circles[i].Start);
        }
        ann.Append(']');
    }

    private static void AppendAlts(StringBuilder ann, List<string> alts, string mainSan)
    {
        var list = alts.Where(a => !string.IsNullOrWhiteSpace(a) && a != mainSan).Distinct().ToList();
        if (list.Count > 0) ann.Append("[%alt ").Append(string.Join(" ", list)).Append(']');
    }

    private static List<Draw> ParseDraws(JsonElement row, string prop)
    {
        var list = new List<Draw>();
        if (!row.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var d in arr.EnumerateArray())
        {
            if (d.ValueKind != JsonValueKind.Object) continue;
            var obj = ReadStr(d, "object");
            if (obj != "arrow" && obj != "circle") continue;
            list.Add(new Draw(obj, ReadStr(d, "start"), ReadStr(d, "end"), ReadStr(d, "color")));
        }
        return list;
    }

    /// <summary>alt_white/alt_black ist ein JSON-String-Array ALS String (z. B. <c>["d4","Nf3"]</c>).</summary>
    private static List<string> ParseAltArray(string? altJsonString)
    {
        if (string.IsNullOrWhiteSpace(altJsonString)) return new();
        try
        {
            using var doc = JsonDocument.Parse(altJsonString);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new();
            var list = new List<string>();
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                {
                    var s = e.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
            return list;
        }
        catch
        {
            return new();
        }
    }

    // ---- Kommentar-Extraktion (ResponseMove-JSON → Klartext) --------------------------------------
    /// <summary>Extrahiert den Kommentar-Klartext (nur die Top-Level-„C"-Einträge in <c>data</c>) aus
    /// einem ResponseMove-JSON (<c>{"before":…,"after":…,"data":[…]}</c>). Varianten (Key „V") werden in
    /// Phase A NICHT als <c>(…)</c> ausgegeben. Leerer String/kein JSON/keine C-Einträge → <c>null</c>.</summary>
    private static string? ExtractComment(string? responseMoveJson)
    {
        if (string.IsNullOrWhiteSpace(responseMoveJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(responseMoveJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            var parts = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (ReadStr(item, "key") != "C") continue;
                if (!item.TryGetProperty("val", out var v) || v.ValueKind != JsonValueKind.String) continue;
                var cleaned = CleanCommentText(v.GetString() ?? "").Trim();
                if (cleaned.Length > 0) parts.Add(cleaned);
            }
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Chessable-Kommentar-Platzhalter/HTML entfernen (wie
    /// <c>JsonMoveItemList.ReplaceCommentStuff</c> im getGame-Weg). Kritisch: <c>{}</c> → <c>()</c>,
    /// sonst beendet ein „}" den PGN-<c>{…}</c>-Block vorzeitig.</summary>
    private static string CleanCommentText(string s)
    {
        s = s.Replace("@@StartBracket@@", "(").Replace("@@EndBracket@@", ")");
        s = FenTagRegex().Replace(s, "");
        s = s.Replace("@@StartBlockQuote@@", "").Replace("@@EndBlockQuote@@", "")
             .Replace("@@LinkStart@@", "").Replace("@@LinkEnd@@", "")
             .Replace("@@SANStart@@", "").Replace("@@SANEnd@@", "")
             .Replace("@@HeaderStart@@", "").Replace("@@HeaderEnd@@", "");
        s = s.Replace("<br/>", "").Replace("<br>", "")
             .Replace("</strong>", "").Replace("<strong>", "")
             .Replace("</bold>", "").Replace("<bold>", "");
        s = HtmlTagRegex().Replace(s, "");
        s = s.Replace('{', '(').Replace('}', ')');
        return s;
    }

    // ---- JSON-Lesehelfer --------------------------------------------------------------------------
    private static string ReadStr(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.TryGetInt64(out var l) ? l.ToString() : v.GetRawText(),
            _ => "",
        };
    }

    private static bool ReadBool(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Header-Wert PGN-sicher machen (Anführungszeichen/Backslash escapen, Zeilenumbrüche raus).</summary>
    private static string EscapeHeader(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }
}
