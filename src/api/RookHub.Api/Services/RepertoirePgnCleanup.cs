using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Bereinigt Altlasten in Chessable-Repertoire-PGNs, OHNE je eine Partie zu löschen. Ausgeblendet wird über den Header
/// <c>[RookHubHidden "…"]</c> direkt in der Partie: alle Leser gehen über <see cref="WithoutHidden"/> und überspringen
/// sie, und wer den Header entfernt, macht sie wieder sichtbar.
///
/// Hintergrund: Bis RookHub 0.476 / RepCheck 1.57 las der piratechess-Parser Browser-Uploads positionsbasiert. Schickte
/// die Extension nur einen Teil der Linien eines Kapitels, landete jede unter Namen und oid des ERSTEN Kapiteleintrags —
/// als Kopie einer schon vorhandenen Linie oder als Unikat mit falscher oid. Gemessen am 13.09. auf Prod: 2 von 205
/// Chessable-Repertoires. Gleiche Züge unter VERSCHIEDENEN oids sind dagegen von Chessable gewollte Wiederholungen
/// (666 Gruppen) und bleiben unangetastet, ebenso Dubletten ganz ohne oid (nicht entscheidbar).
///
/// Regeln, idempotent (ein zweiter Lauf ändert nichts):
///  1. Eine oid an Partien mit VERSCHIEDENEN Linien (Stellung + Hauptvariante) gehört genau einer davon. Welcher,
///     entscheidet in dieser Reihenfolge: (a) ihr Rohinhalt im geteilten Linien-Cache; (b) die Ausschlussregel — eine
///     Partie, deren Inhalt schon unter einer ANDEREN, eindeutigen oid steht, trägt sie nicht; (c) sonst die früheste
///     Partie (der Fehler hängte immer hinten an). Den übrigen wird die oid entfernt (<c>[RookHubRemovedOid]</c>); steht
///     ihr Inhalt schon in einer anderen sichtbaren Partie, werden sie ausgeblendet, sonst bleiben sie sichtbar.
///  2. Dieselben Züge in mehreren sichtbaren Partien, die zusammen GENAU EINE oid kennen: die früheste Partie bleibt und
///     trägt die oid, die übrigen werden ausgeblendet.
/// </summary>
public static class RepertoirePgnCleanup
{
    /// <summary>Stand der Regeln. Erhöhen, wenn eine Regel dazukommt — der Start-Job prüft dann alle Dateien neu.</summary>
    public const int CurrentVersion = 1;
    public const string HiddenTag = "RookHubHidden";
    public const string RemovedOidTag = "RookHubRemovedOid";

    public sealed record CleanupAction(int Game, string Line, string Action, string? Oid, string Detail);
    public sealed record CleanupResult(string Pgn, IReadOnlyList<CleanupAction> Actions);

    private static readonly Regex EventRegex = new(@"\[Event ", RegexOptions.Compiled);
    private static readonly Regex OidHeaderRegex = new(@"^\[ChessableOid ""([^""]+)""\]$", RegexOptions.Compiled);
    private static readonly Regex WhiteHeaderRegex = new(@"^\[White ""([^""]*)""\]$", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private sealed class Game
    {
        public int Index, Start, End;
        public int HeaderEnd = -1;            // absolute Position hinter dem letzten Header (vor dessen Zeilenumbruch)
        public string? Oid;
        public bool Hidden;
        public string Line = string.Empty;    // White-Header, nur für den Bericht
        public string Moves = string.Empty;   // header-freier Zugtext (Dedup-Signatur)
        private string? _key;
        // Arbeitszustand der Reparatur
        public string? EffectiveOid;
        public bool WillHide;
        public string? RemoveOid, AddOid, RemovedNote, HideReason;

        /// <summary>Linien-Identität: Startstellung + Hauptvariante; ohne Züge (Info-Seite) der ganze Zugtext.
        /// Kommentar-Unterschiede zwischen Parser-Versionen machen eine Linie so nicht zu einer anderen.</summary>
        public string Key(string pgn)
        {
            if (_key != null) return _key;
            var text = pgn.Substring(Start, End - Start);
            var sans = ChessableTrainedLineService.MainlineSans(text);
            return _key = sans.Count > 0
                ? (ChessableTrainedLineService.StartFenOf(text) ?? string.Empty) + "|" + string.Join(' ', sans)
                : "text|" + Moves;
        }
    }

    /// <summary>
    /// Zugtext eines PGN-Blocks — alles hinter den führenden Header- und Leerzeilen, whitespace-normiert — als
    /// Dedup-Signatur einer Linie. piratechess trennt Header und Züge seit dem <c>[ChessableOid]</c>-Header durch eine
    /// Zeile aus LEERZEICHEN; „alles nach der ersten Leerzeile" enthielt dadurch die Header, und eine neu geholte Linie
    /// glich ihrem alten Gegenstück nie (in einem echten Kurs 0 von 175, header-frei 175 von 175).
    /// </summary>
    public static string MovetextOf(string block)
    {
        var pos = 0;
        while (pos < block.Length)
        {
            var nl = block.IndexOf('\n', pos);
            var lineEnd = nl < 0 ? block.Length : nl;
            var line = block.AsSpan(pos, lineEnd - pos).Trim();
            if (line.Length > 0 && line[0] != '[') break;
            pos = nl < 0 ? block.Length : nl + 1;
        }
        return WhitespaceRegex.Replace(block[pos..], " ").Trim();
    }

    /// <summary>Trägt dieser Partie-Block den Ausblend-Header?</summary>
    public static bool IsHiddenGame(string block)
    {
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != '[') break;
            if (line.StartsWith("[" + HiddenTag + " ", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Das PGN ohne ausgeblendete Partien — für ALLE Leser (Ansicht, Training, Analyse, Download). Ohne
    /// Ausblend-Header kommt dieselbe Instanz zurück (kein Kopieren im Normalfall).</summary>
    public static string WithoutHidden(string? pgn)
    {
        if (string.IsNullOrEmpty(pgn)) return pgn ?? string.Empty;
        if (pgn.IndexOf("[" + HiddenTag + " ", StringComparison.Ordinal) < 0) return pgn;
        var games = Games(pgn);
        if (!games.Any(g => g.Hidden)) return pgn;
        var sb = new StringBuilder(pgn.Length);
        sb.Append(pgn, 0, games[0].Start);
        foreach (var g in games)
            if (!g.Hidden) sb.Append(pgn, g.Start, g.End - g.Start);
        return sb.ToString();
    }

    /// <summary>oids, die an sichtbaren Partien mit verschiedenen Linien hängen — für sie wird die Wahrheit gebraucht.</summary>
    public static HashSet<string> AmbiguousOids(string? pgn)
    {
        if (string.IsNullOrEmpty(pgn) || pgn.IndexOf("[ChessableOid ", StringComparison.Ordinal) < 0)
            return new HashSet<string>(StringComparer.Ordinal);
        return new HashSet<string>(AmbiguousOidsOf(pgn, Games(pgn)), StringComparer.Ordinal);
    }

    /// <summary>Wendet beide Regeln an. <paramref name="truthByOid"/>: oid → PGN ihrer Linie aus dem geteilten Cache
    /// (fehlende oids entscheiden die Ausweichregeln).</summary>
    public static CleanupResult Repair(string? pgn, IReadOnlyDictionary<string, string> truthByOid)
    {
        pgn ??= string.Empty;
        var actions = new List<CleanupAction>();
        var games = Games(pgn);
        if (games.Count < 2) return new CleanupResult(pgn, actions);
        bool Visible(Game g) => !g.Hidden && !g.WillHide;

        // ── Regel 1: mehrdeutige oids ────────────────────────────────────────
        var ambiguous = AmbiguousOidsOf(pgn, games);
        var ambiguousSet = new HashSet<string>(ambiguous, StringComparer.Ordinal);
        foreach (var oid in ambiguous)
        {
            var carriers = games.Where(g => Visible(g) && g.EffectiveOid == oid).ToList();
            if (carriers.Select(g => g.Key(pgn)).Distinct(StringComparer.Ordinal).Count() < 2) continue;

            List<Game> rightful;
            string basis;
            if (truthByOid.TryGetValue(oid, out var truth) && !string.IsNullOrWhiteSpace(truth))
            {
                rightful = carriers.Where(g => MatchesTruth(pgn, g, truth)).ToList();
                basis = "laut Linien-Cache";
                if (rightful.Count == 0) continue;   // Wahrheit passt zu keiner Partie → lieber nichts anfassen
            }
            else
            {
                var remaining = carriers.Where(g => !games.Any(o => o != g && Visible(o) && o.Moves == g.Moves
                    && o.EffectiveOid != null && o.EffectiveOid != oid && !ambiguousSet.Contains(o.EffectiveOid))).ToList();
                if (remaining.Count > 0 && remaining.Select(g => g.Key(pgn)).Distinct(StringComparer.Ordinal).Count() == 1)
                {
                    rightful = remaining;
                    basis = "Inhalt der übrigen steht schon unter einer anderen oid";
                }
                else
                {
                    var earliestKey = carriers.OrderBy(g => g.Index).First().Key(pgn);
                    rightful = carriers.Where(g => g.Key(pgn) == earliestKey).ToList();
                    basis = "früheste Partie mit dieser oid gilt als richtig";
                }
            }

            foreach (var g in carriers.Where(g => !rightful.Contains(g)))
            {
                g.RemoveOid = oid;
                g.RemovedNote = oid;
                g.EffectiveOid = null;
                var twin = games.FirstOrDefault(o => o != g && Visible(o) && o.Moves.Length > 0 && o.Moves == g.Moves);
                if (twin != null)
                {
                    g.WillHide = true;
                    g.HideReason = $"Kopie von Partie {twin.Index + 1}, trug fälschlich oid {oid}";
                    actions.Add(new(g.Index + 1, g.Line, "ausgeblendet", oid, $"{g.HideReason} ({basis})"));
                }
                else
                {
                    actions.Add(new(g.Index + 1, g.Line, "oid entfernt", oid, $"falsche oid, Inhalt einzigartig — bleibt sichtbar ({basis})"));
                }
            }
        }

        // ── Regel 2: dieselben Züge mit genau einer bekannten oid ────────────
        foreach (var grp in games.Where(g => Visible(g) && g.Moves.Length > 0).GroupBy(g => g.Moves, StringComparer.Ordinal))
        {
            var members = grp.OrderBy(g => g.Index).ToList();
            if (members.Count < 2) continue;
            var oids = members.Select(g => g.EffectiveOid).Where(o => o != null).Distinct(StringComparer.Ordinal).ToList();
            if (oids.Count != 1) continue;   // keine oid: nicht entscheidbar · mehrere: gewollte Wiederholung
            var oid = oids[0]!;
            var keep = members[0];
            if (keep.EffectiveOid == null)
            {
                keep.AddOid = oid;
                keep.EffectiveOid = oid;
                actions.Add(new(keep.Index + 1, keep.Line, "oid übernommen", oid, "von ihrer Kopie, die ausgeblendet wird"));
            }
            foreach (var g in members.Skip(1))
            {
                if (g.EffectiveOid != null) { g.RemoveOid = g.EffectiveOid; g.EffectiveOid = null; }
                g.WillHide = true;
                g.HideReason = $"Kopie von Partie {keep.Index + 1}";
                actions.Add(new(g.Index + 1, g.Line, "ausgeblendet", oid, g.HideReason));
            }
        }

        if (actions.Count == 0) return new CleanupResult(pgn, actions);

        // ── Anwenden: nur Header-Zeilen ändern, alles andere Zeichen für Zeichen übernehmen ──
        var sb = new StringBuilder(pgn.Length + actions.Count * 64);
        sb.Append(pgn, 0, games[0].Start);
        foreach (var g in games)
        {
            if ((g.RemoveOid == null && g.AddOid == null && !g.WillHide) || g.HeaderEnd < 0)
            {
                sb.Append(pgn, g.Start, g.End - g.Start);
                continue;
            }
            var lines = pgn.Substring(g.Start, g.HeaderEnd - g.Start).Split('\n').ToList();
            if (g.RemoveOid != null)
                lines.RemoveAll(l => l.Trim() == $"[ChessableOid \"{g.RemoveOid}\"]");
            if (g.AddOid != null) lines.Add($"[ChessableOid \"{g.AddOid}\"]");
            if (g.RemovedNote != null) lines.Add($"[{RemovedOidTag} \"{g.RemovedNote}\"]");
            if (g.WillHide) lines.Add($"[{HiddenTag} \"{Sanitize(g.HideReason)}\"]");
            sb.Append(string.Join('\n', lines));
            sb.Append(pgn, g.HeaderEnd, g.End - g.HeaderEnd);
        }
        return new CleanupResult(sb.ToString(), actions);
    }

    private static List<string> AmbiguousOidsOf(string pgn, List<Game> games) =>
        games.Where(g => !g.Hidden && g.Oid != null)
            .GroupBy(g => g.Oid!, StringComparer.Ordinal)
            .Where(grp => grp.Select(g => g.Key(pgn)).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(grp => grp.Key)
            .ToList();

    private static bool MatchesTruth(string pgn, Game g, string truthPgn)
    {
        var t = Games(truthPgn).FirstOrDefault();
        return t != null && (t.Moves == g.Moves || t.Key(truthPgn) == g.Key(pgn));
    }

    private static string Sanitize(string? text)
        => (text ?? string.Empty).Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');

    private static List<Game> Games(string pgn)
    {
        var games = new List<Game>();
        var first = pgn.IndexOf("[Event ", StringComparison.Ordinal);
        if (first < 0) return games;
        // Dieselben Schnittstellen wie ChessableImportService.SplitPgnGames: jedes „[Event " ab dem ersten.
        var starts = EventRegex.Matches(pgn, first).Select(m => m.Index).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var g = new Game { Index = i, Start = starts[i], End = i + 1 < starts.Count ? starts[i + 1] : pgn.Length };
            for (var pos = g.Start; pos < g.End;)
            {
                var nl = pgn.IndexOf('\n', pos, g.End - pos);
                var lineEnd = nl < 0 ? g.End : nl;
                var line = pgn.AsSpan(pos, lineEnd - pos).Trim();
                if (line.Length == 0 || line[0] != '[') break;
                var text = line.ToString();
                if (text.StartsWith("[" + HiddenTag + " ", StringComparison.Ordinal)) g.Hidden = true;
                var oid = OidHeaderRegex.Match(text);
                if (oid.Success) g.Oid ??= oid.Groups[1].Value;
                var white = WhiteHeaderRegex.Match(text);
                if (white.Success) g.Line = white.Groups[1].Value;
                g.HeaderEnd = pgn[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
                if (nl < 0) break;
                pos = nl + 1;
            }
            g.Moves = MovetextOf(pgn.Substring(g.Start, g.End - g.Start));
            g.EffectiveOid = g.Oid;
            games.Add(g);
        }
        return games;
    }
}
