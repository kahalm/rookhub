using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Erneuert den ZUGTEXT der Linien eines gespeicherten Chessable-Kurses (<c>BookSource.SourcePgn</c>) aus dem
/// geteilten piratechess-Linien-Cache — Grundlage von <see cref="StaleAction.Cache"/>. Reine Textarbeit, ohne
/// DB und Netz: den Cache fragt <see cref="ImportReprocessService"/>, den Import macht
/// <see cref="PgnImportService.ImportFileAsync"/>.
///
/// <para><b>Warum nur der Zugtext:</b> die Header tragen die Identität der Linie IM KURS. <c>Round</c> kommt aus
/// dem Import (Kapitel-Versatz beim Chunk-Import) und ergibt die LineId, an der Fortschritt und Statistik
/// hängen. Die Cache-Antwort entsteht dagegen aus einem Fake-Kapitel „x": sie zählt ab <c>001.001</c> und heißt
/// <c>[Event "x"]</c>. Ganze Blöcke zu übernehmen verschöbe jede LineId. Header, die der gespeicherte Block
/// noch nicht hat (<c>[ChessableColor]</c> seit piratechess v1.0.46), kommen dazu; vorhandene gewinnen.</para>
///
/// <para><b>Im Zweifel bleibt der Bestand</b> und wird gezählt: fehlt die Linie im Cache, trägt nur eine Seite
/// den Trainingsmarker <c>[%tqu</c> (falscher Modus), steht die Linie auf einer anderen Stellung oder hängt
/// dieselbe oid an mehreren Partien (Altlast des positionsbasierten Parsers bis RookHub 0.476).</para>
///
/// <para>Geschnitten wird an jedem <c>[Event </c> am Zeilenanfang — wie
/// <see cref="ChessableImportService.InsertChessableOids"/> und <see cref="ChessableTrainingStart.InsertColors"/>,
/// nur ohne Schnitt mitten in einem Kommentar (dort stünde der alte Zugtext halb im einen, halb im nächsten
/// Block, und das Ersetzen zerrisse die Partie). NICHT über <see cref="PgnParser.SplitGameBlocks"/>: dessen
/// Rohtext ist getrimmt und ohne verirrte Tag-Zeilen, ein Zusammensetzen daraus änderte auch die Partien, die
/// gar nicht erneuert werden. So bleibt alles außer dem ersetzten Zugtext Zeichen für Zeichen stehen.</para>
/// </summary>
public static class CachedSourceRebuild
{
    /// <summary>Ergebnis eines Laufs. <see cref="Total"/> zählt die Partien MIT oid; jede davon landet in
    /// genau einem der vier übrigen Zähler.</summary>
    /// <param name="Replaced">Zugtext aus dem Cache übernommen (auch wenn er gleich geblieben ist).</param>
    /// <param name="Missing">oid nicht im Cache — Partie unverändert.</param>
    /// <param name="ModeMismatch"><c>[%tqu</c> nur auf einer Seite — Partie unverändert.</param>
    /// <param name="Conflicts">andere Startstellung oder oid an mehreren Partien — Partie unverändert.</param>
    public sealed record Result(string Pgn, int Total, int Replaced, int Missing, int ModeMismatch, int Conflicts);

    /// <summary>Trainingsmarker, den piratechess im Modus <c>FirstKeyMove</c> setzt.</summary>
    private const string TrainingMarker = "[%tqu";

    /// <summary>Header, deren Wert in der Cache-Antwort aus dem Fake-Kapitel der Abfrage stammt (Kapitelname,
    /// Zählung, Linienname „x") und nichts über die Linie sagt — die kommen nie in den Bestand, auch wenn er
    /// sie nicht hat. Eine übernommene <c>Round</c> gäbe der Linie eine andere LineId.</summary>
    private static readonly HashSet<string> FakeChapterHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "Event", "Round", "White", "Black" };

    private static readonly Regex EventStart = new(@"(?<=(?:^|\n)[ \t]*)\[Event ", RegexOptions.Compiled);
    private static readonly Regex HeaderLine = new(@"^\[\s*([A-Za-z][A-Za-z0-9_]*)\s+""(.*)""\s*\]$", RegexOptions.Compiled);

    /// <summary>
    /// Modus, in dem der Linien-Cache die Linien dieses Kurses erzeugen muss: trägt das gespeicherte PGN
    /// irgendwo <c>[%tqu</c>, war es ein Buch-/Kursabruf (<c>FirstKeyMove</c>); sonst Repertoire-Format
    /// (<c>None</c>) — etwa ein aus einem Repertoire umgewandelter Kurs. Der darf keine Marker bekommen, sonst
    /// änderte sich sein Trainingsstart.
    /// </summary>
    public static string ModeFor(string? sourcePgn) =>
        sourcePgn != null && sourcePgn.Contains(TrainingMarker, StringComparison.OrdinalIgnoreCase)
            ? ChessableTrainingStart.MarkerMode
            : "None";

    /// <summary>Die oids aus den Headern der Partien, jede einmal, in Reihenfolge des Kurses. Eine oid, die nur
    /// im Kommentartext steht, zählt nicht.</summary>
    public static IReadOnlyList<string> OidsOf(string? sourcePgn)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(sourcePgn)) return result;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in Blocks(sourcePgn))
            if (block.Oid != null && seen.Add(block.Oid)) result.Add(block.Oid);
        return result;
    }

    /// <summary>
    /// Ersetzt je Partie mit <c>[ChessableOid]</c> den Zugtext durch den aus <paramref name="freshByOid"/>
    /// (oid → PGN der Linie, wie <see cref="ChessableProxyService.GetCachedLinePgnsAsync"/> es liefert). Wird
    /// nichts übernommen, kommt dieselbe Instanz von <paramref name="sourcePgn"/> zurück.
    /// </summary>
    public static Result Rebuild(string sourcePgn, IReadOnlyDictionary<string, string> freshByOid)
    {
        var blocks = Blocks(sourcePgn);
        var oidCount = blocks.Where(b => b.Oid != null)
            .GroupBy(b => b.Oid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        int total = 0, replaced = 0, missing = 0, modeMismatch = 0, conflicts = 0;
        var sb = new StringBuilder(sourcePgn.Length + 256);
        sb.Append(sourcePgn, 0, blocks.Count > 0 ? blocks[0].Start : sourcePgn.Length);

        for (var i = 0; i < blocks.Count; i++)
        {
            var old = blocks[i];
            string? replacement = null;
            if (old.Oid != null)
            {
                total++;
                var freshPgn = freshByOid.TryGetValue(old.Oid, out var text) ? text.Trim() : string.Empty;
                // Genau EINE Partie mit derselben oid — alles andere ist keine brauchbare Antwort und zählt
                // wie „nicht im Cache".
                var fresh = freshPgn.Length > 0 && Blocks(freshPgn) is [var only] && only.Oid == old.Oid ? only : null;
                if (fresh is null)
                    missing++;
                else if (oidCount[old.Oid] > 1)
                    conflicts++;
                else if (HasMarker(old.Moves(sourcePgn)) != HasMarker(fresh.Moves(freshPgn)))
                    modeMismatch++;
                else if (!SameStart(old.Header("FEN"), fresh.Header("FEN")))
                    conflicts++;
                else
                {
                    replacement = Replace(sourcePgn, old, freshPgn, fresh, isLast: i == blocks.Count - 1);
                    replaced++;
                }
            }
            sb.Append(replacement ?? sourcePgn.Substring(old.Start, old.End - old.Start));
        }

        return new Result(replaced > 0 ? sb.ToString() : sourcePgn, total, replaced, missing, modeMismatch, conflicts);
    }

    /// <summary>Neuer Text eines Blocks: seine Header unverändert (+ fehlende aus dem Cache), sein Leerraum
    /// zwischen Headern und Zügen und hinter den Zügen unverändert, dazwischen der Zugtext aus dem Cache.</summary>
    private static string Replace(string pgn, Block old, string freshText, Block fresh, bool isLast)
    {
        var sb = new StringBuilder(old.End - old.Start + freshText.Length);
        sb.Append(pgn, old.Start, old.HeaderEnd - old.Start);
        foreach (var h in fresh.Headers)
            if (!FakeChapterHeaders.Contains(h.Key) && old.Header(h.Key) == null)
                sb.Append('\n').Append(h.Line);

        string gap, tail;
        if (old.MovesStart < old.MovesEnd)
        {
            gap = pgn.Substring(old.HeaderEnd, old.MovesStart - old.HeaderEnd);
            tail = pgn.Substring(old.MovesEnd, old.End - old.MovesEnd);
        }
        else
        {
            // Block ohne Züge: der ganze Leerraum steht hinter den Zügen, davor die übliche Leerzeile.
            gap = "\n\n";
            tail = pgn.Substring(old.HeaderEnd, old.End - old.HeaderEnd);
        }
        // Ohne Zeilenumbruch klebte das nächste [Event an den letzten Zug — dann liest der Parser beide als
        // eine Partie.
        if (!isLast && !tail.Contains('\n')) tail += "\n\n";
        return sb.Append(gap).Append(fresh.Moves(freshText)).Append(tail).ToString();
    }

    private static bool HasMarker(string moves) => moves.Contains(TrainingMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Dieselbe Stellung, wenn Brett und Zugrecht gleich sind — Zugzähler dürfen abweichen. Keine
    /// oder eine leere FEN heißt Grundstellung (so schreibt piratechess zug-lose Seiten).</summary>
    private static bool SameStart(string? a, string? b)
    {
        static string Key(string? fen)
        {
            var parts = (string.IsNullOrWhiteSpace(fen)
                    ? "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w"
                    : fen).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[0] + " " + parts[1].ToLowerInvariant() : parts[0];
        }
        return Key(a) == Key(b);
    }

    /// <summary>Eine Partie als Positionen im Gesamttext. <c>HeaderEnd</c> liegt hinter dem letzten
    /// Header (vor dessen Zeilenumbruch); der Zugtext ist <c>[MovesStart, MovesEnd)</c> ohne Rand-Leerraum.</summary>
    private sealed record Block(int Start, int End, int HeaderEnd, int MovesStart, int MovesEnd,
        IReadOnlyList<(string Key, string Value, string Line)> Headers, string? Oid)
    {
        public string? Header(string key)
        {
            foreach (var h in Headers)
                if (string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase)) return h.Value;
            return null;
        }

        public string Moves(string pgn) => pgn.Substring(MovesStart, MovesEnd - MovesStart);
    }

    private static List<Block> Blocks(string pgn)
    {
        var blocks = new List<Block>();
        var starts = EventStart.Matches(pgn).Select(m => m.Index).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1] : pgn.Length;
            var headerEnd = ChessableImportService.HeaderEnd(pgn, start, end);
            if (headerEnd < 0) headerEnd = start;

            // Header-Zeilen getrimmt: piratechess rückt die Zeilen hinter dem oid-Header um 24 Leerzeichen ein.
            var headers = new List<(string, string, string)>();
            string? oid = null;
            foreach (var raw in pgn.Substring(start, headerEnd - start).Split('\n'))
            {
                var line = raw.Trim();
                var m = HeaderLine.Match(line);
                if (!m.Success) continue;
                headers.Add((m.Groups[1].Value, m.Groups[2].Value, line));
                if (oid == null && m.Groups[1].Value.Equals("ChessableOid", StringComparison.OrdinalIgnoreCase))
                    oid = m.Groups[2].Value;
            }

            var movesStart = headerEnd;
            while (movesStart < end && char.IsWhiteSpace(pgn[movesStart])) movesStart++;
            var movesEnd = end;
            while (movesEnd > movesStart && char.IsWhiteSpace(pgn[movesEnd - 1])) movesEnd--;

            blocks.Add(new Block(start, end, headerEnd, movesStart, movesEnd, headers, oid));
        }
        return blocks;
    }
}
