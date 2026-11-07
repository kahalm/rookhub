using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Trainingsstand einer Chessable-Linie aus den Mitschnitten der RepCheck-Extension (0.885.0, Wunsch 2026-11-07: „bei jeder line
/// dazuschreiben wann ich die das letztemal auf chessable trainiert habe und auf welcher stufe das war", „wie oft der schon
/// falsch war", „link zur chessable line", „ungelernte alternativen brauchst du mir nicht bringen").
///
/// <para><b>Quellen</b> (je Nutzer, Linie = Chessable-<c>oid</c> aus <c>[ChessableOid "…"]</c> — eine Varianten-Id ist bei Chessable
/// kursübergreifend eindeutig, deshalb ohne <c>bid</c>):
/// <list type="bullet">
/// <item><c>ChessableSessionMoves</c> (append-only, eine Zeile je Trainingsdurchlauf): jüngstes <c>CreatedAt</c> = zuletzt trainiert;
/// Stufe = NIEDRIGSTE <c>level</c> der eigenen, nicht-informativen Züge dieses Durchlaufs (<c>practiceMode</c> = Trainingsfarbe,
/// wo vorhanden); <c>wrong[]</c> je <c>mid</c> über alle Durchläufe summiert.</item>
/// <item><c>ChessableReviewLines</c> (getReview-Rohantwort, letzter Stand) nur als Rückfall für die Stufe: je Zeile
/// (= voller Zug, <c>mid</c>) <c>level</c> für Weiß und <c>level_b</c> für Schwarz (Fixture <c>getReview-sample.json</c>:
/// <c>studyCol "white"</c> → <c>level</c> gesetzt, <c>level_b</c> null), dieselbe Minimum-Regel, „Stand vom <c>UpdatedAt</c>".</item>
/// </list></para>
///
/// <para><b>mid ↔ Halbzug:</b> <c>mid</c> zählt die Zeilen der Linie (je Zeile ein Weiß- und ein Schwarzzug, <c>fullMove0Based</c>);
/// der Halbzug ab Linienbeginn ist <c>2·mid</c> (Weiß) bzw. <c>2·mid+1</c> (Schwarz), eins weniger, wenn die Linie mit Schwarz am Zug
/// beginnt (dann ist die erste Zeile ohne Weißzug).</para>
/// </summary>
public sealed class ChessableLineHistory(AppDbContext db)
{
    /// <summary>Ein falscher Zug an einer Stelle: wie oft (über alle Durchläufe) und zuletzt womit.</summary>
    public sealed record Miss(int Count, string? Last);

    /// <param name="LastTrained">Jüngster Durchlauf (UTC) oder <c>null</c>.</param>
    /// <param name="Level">Linienstufe oder <c>null</c>.</param>
    /// <param name="LevelAsOf">Gesetzt, wenn die Stufe NUR aus <c>ChessableReviewLines</c> stammt (deren <c>UpdatedAt</c>).</param>
    /// <param name="Misses">Halbzug-Index ab Linienbeginn (0 = erster Zug) → Fehler dort.</param>
    public sealed record Status(string Oid, DateTime? LastTrained, int? Level, DateTime? LevelAsOf, IReadOnlyDictionary<int, Miss> Misses)
    {
        public bool Known => LastTrained is not null || Level is not null;
    }

    /// <summary>Eine Linie, wie sie die Abfrage braucht: oid, Trainingsfarbe, beginnt die Linie mit Schwarz am Zug?</summary>
    public sealed record LineRef(string Oid, char Color, bool StartsWithBlack);

    /// <summary>So viele oids je <c>IN</c>.</summary>
    private const int Chunk = 500;

    /// <summary>Link auf die Linie bei Chessable (Format wie die Extension sie kennt, <c>RememberedPositionService</c>).</summary>
    public static string VariationUrl(string oid) => $"https://www.chessable.com/variation/{oid}/";

    /// <summary>Kurs-Link (Rückfall, wenn nur der Kurs bekannt ist).</summary>
    public static string CourseUrl(string bid) => $"https://www.chessable.com/course/{bid}/";

    /// <summary>Chessable-oid eines PGN-Abschnitts, nur numerisch (sonst <c>null</c> — kein Link, keine Abfrage).</summary>
    public static string? OidOf(string? section)
    {
        var oid = section is null ? null : ChessablePgnBlocks.OidOf(section);
        return oid is { Length: > 0 and <= 32 } && oid.All(char.IsAsciiDigit) ? oid : null;
    }

    private static readonly Regex AlternativePart = new(@"^\s*alternative\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Ist die Linie auf Chessable eine ALTERNATIVE? Erkannt am Namen: ein durch „|" getrennter Teil des White- oder Black-Headers
    /// (bei Chessable-Importen <c>opening_name</c> bzw. Kapitel) beginnt mit „Alternative" (Groß/klein egal), z. B.
    /// „12 | Steinitz ohne f2-f4 | Alternative 7...a5 #1".
    /// </summary>
    public static bool IsAlternative(string? white, string? black) =>
        HasAlternativePart(white) || HasAlternativePart(black);

    private static bool HasAlternativePart(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Split('|').Any(p => AlternativePart.IsMatch(p));

    /// <summary>Stand aller Linien in EINER Abfrage je Tabelle (Portionen à 500 oids); Linien ohne jeden Mitschnitt fehlen.</summary>
    public async Task<Dictionary<string, Status>> LoadAsync(int userId, IReadOnlyCollection<LineRef> lines, CancellationToken ct)
    {
        var result = new Dictionary<string, Status>(StringComparer.Ordinal);
        var byOid = lines.GroupBy(l => l.Oid, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (byOid.Count == 0) return result;

        var sessions = new List<(string Oid, string Json, DateTime At)>();
        foreach (var chunk in byOid.Keys.Chunk(Chunk))
            foreach (var row in await db.ChessableSessionMoves.AsNoTracking()
                         .Where(s => s.UserId == userId && chunk.Contains(s.Oid))
                         .Select(s => new { s.Oid, s.MovesJson, s.CreatedAt }).ToListAsync(ct))
                sessions.Add((row.Oid, row.MovesJson, row.CreatedAt));

        foreach (var group in sessions.GroupBy(s => s.Oid, StringComparer.Ordinal))
        {
            var line = byOid[group.Key];
            var ordered = group.OrderBy(s => s.At).ToList();
            var misses = new Dictionary<int, Miss>();
            foreach (var s in ordered)
                foreach (var e in Entries(s.Json))
                {
                    if (!Own(e, line.Color) || e.Wrong.Count == 0 || e.Mid is not { } mid) continue;
                    var ply = Ply(mid, line.Color, line.StartsWithBlack);
                    if (ply < 0) continue;
                    var before = misses.GetValueOrDefault(ply);
                    misses[ply] = new Miss((before?.Count ?? 0) + e.Wrong.Count, e.Wrong[^1]);
                }
            var latest = ordered[^1];
            result[group.Key] = new Status(group.Key, latest.At, LevelOf(Entries(latest.Json), line.Color), null, misses);
        }

        // Rückfall Stufe: getReview-Stand für Linien ohne Durchlauf (bzw. ohne Stufe darin)
        var withoutLevel = byOid.Keys.Where(o => result.GetValueOrDefault(o)?.Level is null).ToList();
        foreach (var chunk in withoutLevel.Chunk(Chunk))
            foreach (var row in await db.ChessableReviewLines.AsNoTracking()
                         .Where(r => r.UserId == userId && chunk.Contains(r.Oid))
                         .OrderBy(r => r.UpdatedAt)
                         .Select(r => new { r.Oid, r.Json, r.UpdatedAt }).ToListAsync(ct))
            {
                if (ReviewLevel(row.Json, byOid[row.Oid].Color) is not { } level) continue;
                var had = result.GetValueOrDefault(row.Oid);
                result[row.Oid] = new Status(row.Oid, had?.LastTrained, level, had?.LastTrained is null ? row.UpdatedAt : null,
                    had?.Misses ?? new Dictionary<int, Miss>());
            }
        return result;
    }

    /// <summary>Fehler an einer Zeile in einer Farbe, über alle Durchläufe summiert (Repertoire-Trainer: der Client kennt Farbe und
    /// Startseite der Linie und rechnet <c>mid</c> selbst in den Halbzug um).</summary>
    public sealed record RawMiss(int Mid, string? Mode, int Count, string? Last);

    /// <summary>Fehler je Linie (oid) für den Repertoire-Trainer — EINE Abfrage (Portionen à 500 oids); nur Linien mit Fehlern.</summary>
    public async Task<Dictionary<string, List<RawMiss>>> MissesByOidAsync(int userId, IReadOnlyCollection<string> oids, CancellationToken ct)
    {
        var result = new Dictionary<string, List<RawMiss>>(StringComparer.Ordinal);
        foreach (var chunk in oids.Distinct(StringComparer.Ordinal).Chunk(Chunk))
        {
            var rows = await db.ChessableSessionMoves.AsNoTracking()
                .Where(s => s.UserId == userId && chunk.Contains(s.Oid))
                .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
                .Select(s => new { s.Oid, s.MovesJson }).ToListAsync(ct);
            foreach (var group in rows.GroupBy(r => r.Oid, StringComparer.Ordinal))
            {
                var acc = new Dictionary<(int, string?), RawMiss>();
                foreach (var row in group)
                    foreach (var e in Entries(row.MovesJson))
                    {
                        if (e.Wrong.Count == 0 || e.Mid is not { } mid) continue;
                        var mode = string.IsNullOrEmpty(e.Mode) ? null : char.ToLowerInvariant(e.Mode[0]).ToString();
                        var before = acc.GetValueOrDefault((mid, mode));
                        acc[(mid, mode)] = new RawMiss(mid, mode, (before?.Count ?? 0) + e.Wrong.Count, e.Wrong[^1]);
                    }
                if (acc.Count > 0) result[group.Key] = acc.Values.OrderBy(m => m.Mid).ToList();
            }
        }
        return result;
    }

    /// <summary>Alle numerischen oids eines PGN (beliebig viele Abschnitte).</summary>
    public static List<string> OidsIn(string? pgn) =>
        string.IsNullOrEmpty(pgn) ? []
            : ChessablePgnBlocks.ChessableOidRegex.Matches(pgn).Select(m => m.Groups[1].Value)
                .Where(o => o.Length <= 32 && o.All(char.IsAsciiDigit)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Welche der oids mindestens einen Durchlauf haben, in dem die Linie NICHT übersprungen wurde (kein Zug mit
    /// <c>alternativeSkipped</c>) — eine Alternative gilt dann als gelernt.</summary>
    public async Task<HashSet<string>> TrainedAsync(int userId, IReadOnlyCollection<string> oids, CancellationToken ct)
    {
        var trained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in oids.Distinct(StringComparer.Ordinal).Chunk(Chunk))
            foreach (var row in await db.ChessableSessionMoves.AsNoTracking()
                         .Where(s => s.UserId == userId && chunk.Contains(s.Oid))
                         .Select(s => new { s.Oid, s.MovesJson }).ToListAsync(ct))
            {
                if (trained.Contains(row.Oid)) continue;
                var entries = Entries(row.MovesJson);
                if (entries.Count > 0 && !entries.Any(e => e.AlternativeSkipped)) trained.Add(row.Oid);
            }
        return trained;
    }

    // ── Rohdaten ─────────────────────────────────────────────────────────────────────────────────

    internal sealed record Entry(int? Mid, string? Mode, int? Level, bool Informational, bool AlternativeSkipped, List<string> Wrong);

    /// <summary>Halbzug ab Linienbeginn zu Zeile <paramref name="mid"/> in Farbe <paramref name="color"/>.</summary>
    internal static int Ply(int mid, char color, bool startsWithBlack) => 2 * mid + (color == 'b' ? 1 : 0) - (startsWithBlack ? 1 : 0);

    private static bool Own(Entry e, char color) => e.Mode is null || e.Mode.Length == 0 || char.ToLowerInvariant(e.Mode[0]) == color;

    /// <summary>Niedrigste Stufe der eigenen, nicht-informativen Züge; <c>null</c> ohne Stufe.</summary>
    internal static int? LevelOf(IEnumerable<Entry> entries, char color)
    {
        int? min = null;
        foreach (var e in entries)
            if (Own(e, color) && !e.Informational && e.Level is { } l) min = min is null ? l : Math.Min(min.Value, l);
        return min;
    }

    /// <summary>Einträge eines <c>MovesJson</c> — tolerant: Unlesbares ergibt eine leere Liste, fehlende Felder <c>null</c>.</summary>
    internal static List<Entry> Entries(string? json)
    {
        var list = new List<Entry>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var wrong = new List<string>();
                if (e.TryGetProperty("wrong", out var w) && w.ValueKind == JsonValueKind.Array)
                    foreach (var x in w.EnumerateArray())
                        if (MoveText(x) is { Length: > 0 } t) wrong.Add(t);
                list.Add(new Entry(Int(e, "mid"), Str(e, "practiceMode"), Int(e, "level"), Bool(e, "informational"),
                    Bool(e, "alternativeSkipped"), wrong));
            }
        }
        catch (JsonException) { /* opak — dann eben nichts */ }
        return list;
    }

    /// <summary>Stufe aus einer getReview-Antwort: je Zeile <c>level</c> (Weiß) bzw. <c>level_b</c> (Schwarz), nur Zeilen mit einem
    /// Zug dieser Farbe, ohne informative; das Minimum.</summary>
    internal static int? ReviewLevel(string? json, char color)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("lesson", out var lesson)) root = lesson;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("moves", out var moves) || moves.ValueKind != JsonValueKind.Array)
                return null;
            int? min = null;
            foreach (var m in moves.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object || Bool(m, "informational")) continue;
                var move = Str(m, color == 'b' ? "move_black" : "move_white");
                if (string.IsNullOrWhiteSpace(move)) continue;
                if (Int(m, color == 'b' ? "level_b" : "level") is { } l) min = min is null ? l : Math.Min(min.Value, l);
            }
            return min;
        }
        catch (JsonException) { return null; }
    }

    private static string? MoveText(JsonElement x) => x.ValueKind switch
    {
        JsonValueKind.String => x.GetString()?.Trim(),
        JsonValueKind.Object => (Str(x, "san") ?? Str(x, "move") ?? Str(x, "uci"))?.Trim(),
        JsonValueKind.Number => x.GetRawText(),
        _ => null,
    };

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        } : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True
                                              || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i != 0));

    // ── PGN-Abschnitt beschriften ────────────────────────────────────────────────────────────────

    /// <summary>„7…Sc6"-Schreibweise: Zugnummer des Halbzugs + der falsche Zug (wie mitgeschnitten).</summary>
    internal static string MoveLabel(int ply, bool startsWithBlack, int startMoveNumber, string move)
    {
        var abs = ply + (startsWithBlack ? 1 : 0);                  // Halbzüge ab „Weiß am Zug" der Startzugnummer
        var number = startMoveNumber + abs / 2;
        return abs % 2 == 0 ? $"{number}.{move}" : $"{number}…{move}";
    }

    /// <summary>Text des Anfangskommentars (ohne Klammern).</summary>
    internal static string Summary(Status? s, string? oid)
    {
        var text = new StringBuilder("Chessable: ");
        if (s is null || !s.Known) text.Append("noch nicht trainiert");
        else if (s.LastTrained is { } at)
        {
            text.Append("zuletzt trainiert ").Append(at.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            if (s.Level is { } l) text.Append(", Stufe ").Append(l.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            text.Append("Stufe ").Append(s.Level!.Value.ToString(CultureInfo.InvariantCulture));
            if (s.LevelAsOf is { } asOf) text.Append(" (Stand vom ").Append(asOf.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).Append(')');
        }
        if (oid is not null) text.Append(" — ").Append(VariationUrl(oid));
        return text.ToString();
    }

    /// <summary>
    /// Schreibt den Stand in EINEN PGN-Abschnitt, ohne sonst etwas zu ändern: Kopfzeilen <c>[ChessableLastTrained "yyyy.MM.dd"]</c>,
    /// <c>[ChessableLevel "n"]</c> (nur wenn bekannt) und <c>[ChessableUrl "…"]</c> (nur mit oid) hinter die letzte Kopfzeile, den
    /// Anfangskommentar vor den ersten Zug und je Fehlerstelle <c>{Chessable: hier 3× falsch (zuletzt 7…Sc6)}</c> vor den Zug.
    /// Züge, Varianten und vorhandene Kommentare bleiben — der Linien-Schlüssel ändert sich nicht.
    /// </summary>
    internal static string Annotate(string section, Status? s, string? oid, bool startsWithBlack, int startMoveNumber)
    {
        var (headerEnd, moveStart) = SplitHeader(section);
        var headers = new StringBuilder();
        if (s?.LastTrained is { } at) headers.Append($"[ChessableLastTrained \"{at.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)}\"]\n");
        if (s?.Level is { } level) headers.Append($"[ChessableLevel \"{level.ToString(CultureInfo.InvariantCulture)}\"]\n");
        if (oid is not null) headers.Append($"[ChessableUrl \"{VariationUrl(oid)}\"]\n");

        var inserts = new SortedDictionary<int, string>();
        inserts[-1] = "{" + Summary(s, oid) + "} ";
        if (s is not null)
            foreach (var (ply, miss) in s.Misses)
                if (miss.Count > 0)
                    inserts[ply] = "{Chessable: hier " + miss.Count.ToString(CultureInfo.InvariantCulture) + "× falsch"
                                   + (miss.Last is { Length: > 0 } last ? " (zuletzt " + MoveLabel(ply, startsWithBlack, startMoveNumber, Clean(last)) + ")" : "")
                                   + "} ";

        var moves = InsertBeforePlies(section[moveStart..], inserts);
        return section[..headerEnd] + headers + section[headerEnd..moveStart] + moves;
    }

    private static string Clean(string move) => move.Replace("{", "").Replace("}", "");

    /// <summary>Ende der Kopfzeilen (Index hinter dem Zeilenumbruch der letzten <c>[…]</c>-Zeile) und Beginn der Züge.</summary>
    private static (int HeaderEnd, int MoveStart) SplitHeader(string section)
    {
        var pos = 0;
        var headerEnd = 0;
        while (pos < section.Length)
        {
            var eol = section.IndexOf('\n', pos);
            var line = (eol < 0 ? section[pos..] : section[pos..eol]).Trim();
            if (line.StartsWith('[')) { headerEnd = eol < 0 ? section.Length : eol + 1; pos = headerEnd; continue; }
            if (line.Length == 0) { pos = eol < 0 ? section.Length : eol + 1; continue; }
            break;
        }
        // Züge beginnen beim ersten Nicht-Leerraum hinter den Kopfzeilen
        var start = headerEnd;
        while (start < section.Length && char.IsWhiteSpace(section[start])) start++;
        return (headerEnd, Math.Max(headerEnd, start));
    }

    /// <summary>Fügt Texte vor den Halbzügen der Hauptvariante ein (Schlüssel = Halbzug-Index, −1 = vor allem). Kommentare,
    /// Varianten, NAGs und Zugnummern bleiben unverändert; eine Einfügung kommt vor die Zugnummer, die unmittelbar vor dem Zug steht.</summary>
    internal static string InsertBeforePlies(string moves, IReadOnlyDictionary<int, string> inserts)
    {
        var anchors = MainlineAnchors(moves);
        var sb = new StringBuilder(moves);
        for (var ply = anchors.Count - 1; ply >= 0; ply--)
            if (inserts.TryGetValue(ply, out var text)) sb.Insert(anchors[ply], text);
        if (inserts.TryGetValue(-1, out var first)) sb.Insert(0, first);
        return sb.ToString();
    }

    private static readonly Regex MoveNumber = new(@"^\d+\.+$", RegexOptions.Compiled);

    /// <summary>Je Halbzug der Hauptvariante die Einfügestelle (Beginn seiner Zugnummer, wenn sie direkt davor steht, sonst des Zugs).</summary>
    internal static List<int> MainlineAnchors(string moves)
    {
        var anchors = new List<int>();
        var depth = 0;
        int? number = null;
        var i = 0;
        while (i < moves.Length)
        {
            var c = moves[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '{')
            {
                var end = moves.IndexOf('}', i);
                i = end < 0 ? moves.Length : end + 1;
                number = null;
                continue;
            }
            if (c == ';')
            {
                var end = moves.IndexOf('\n', i);
                i = end < 0 ? moves.Length : end;
                number = null;
                continue;
            }
            if (c == '(') { depth++; i++; number = null; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); i++; number = null; continue; }
            var k = i;
            while (k < moves.Length && !char.IsWhiteSpace(moves[k]) && moves[k] is not ('{' or '(' or ')' or ';')) k++;
            var token = moves[i..k];
            if (depth == 0)
            {
                if (MoveNumber.IsMatch(token)) number ??= i;
                else if (token.StartsWith('$') || token is "*" or "1-0" or "0-1" or "1/2-1/2") number = null;
                else
                {
                    anchors.Add(number ?? i);
                    number = null;
                }
            }
            i = k;
        }
        return anchors;
    }
}
