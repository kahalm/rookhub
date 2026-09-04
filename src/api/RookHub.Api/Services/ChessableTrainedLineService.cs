using Chess;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Auf Chessable trainiert → in RookHub gelernt": eine auf chessable.com abgeschlossene Linie
/// (identifiziert über Kurs-<c>bid</c> + Varianten-<c>oid</c>, gemeldet von der RepCheck-Extension)
/// wird in den RookHub-Gegenstücken des Kurses nachgezogen:
///
/// <list type="bullet">
/// <item><b>Kurs (Buch)</b>: die Linie gilt als gelöst (idempotenter <see cref="CoursePuzzleResult"/>;
/// bewusst OHNE <see cref="CourseAttempt"/> — die Trainingszeit steckt schon in
/// <c>ChessableActivities</c>, ein 0-Sekunden-Versuch würde nur die Statistik verwässern).</item>
/// <item><b>Repertoire-Trainer (SR)</b>: eine noch nicht gelernte Linie gilt als „einmal gelernt"
/// (Stufe 1), eine FÄLLIGE Linie rückt eine Stufe vor — Chessable-Training ersetzt das
/// RookHub-Review (expliziter User-Wunsch). Nicht fällige Linien bleiben unangetastet, damit
/// sich Intervalle nicht doppelt strecken.</item>
/// </list>
///
/// <para><b>Linien-Identität im Repertoire</b>: Der SR-Zustand hängt am <c>CardKey</c> = Hash der
/// normalisierten SAN-Zugfolge, den normal das FRONTEND berechnet
/// (<c>repertoire-line-key.util.ts</c>). Hier wird derselbe Hash serverseitig gespiegelt
/// (<see cref="LineKeyFromSans"/> — cyrb53, base36, Präfix "l"); der Spiegel MUSS mit dem Frontend
/// identisch bleiben (Vektoren-Test <c>ChessableTrainedLineServiceTests</c>). Die Linie selbst wird
/// über den PGN-Header <c>[ChessableOid "…"]</c> des Repertoire-PGNs gefunden.</para>
/// </summary>
public class ChessableTrainedLineService
{
    private readonly AppDbContext _db;
    private readonly RepertoireTrainingService _training;

    public ChessableTrainedLineService(AppDbContext db, RepertoireTrainingService training)
    {
        _db = db;
        _training = training;
    }

    public async Task<ChessableLineTrainedResultDto> MarkTrainedAsync(
        int userId, string bid, string oid, CancellationToken ct = default)
    {
        var result = new ChessableLineTrainedResultDto();

        // ===== 1) Kurs (Buch): Linie als gelöst markieren =====================
        var bookFile = $"chessable-u{userId}-{bid}.pgn";
        var puzzle = await _db.BookPuzzles
            .Where(bp => bp.BookFileName == bookFile && bp.ChessableOid == oid && !bp.IsInfoOnly)
            .Select(bp => new { bp.Id, bp.BookId })
            .FirstOrDefaultAsync(ct);
        if (puzzle is not null && puzzle.BookId is int bookId)
        {
            var already = await _db.CoursePuzzleResults
                .AnyAsync(r => r.UserId == userId && r.BookPuzzleId == puzzle.Id, ct);
            if (!already)
            {
                _db.CoursePuzzleResults.Add(new CoursePuzzleResult
                {
                    UserId = userId,
                    BookPuzzleId = puzzle.Id,
                    BookId = bookId,
                    SolvedAt = DateTime.UtcNow,
                    TimeSeconds = 0,
                });
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { _db.ChangeTracker.Clear(); } // Unique-Race: schon markiert
                result.CourseLineMarked = true;
            }
            result.CourseLineFound = true;
        }

        // ===== 2) Repertoire-Trainer: SR nachziehen ===========================
        var repFile = $"chessable-{bid}.pgn";
        var reps = await _db.Repertoires
            .Where(r => r.UserId == userId
                && (r.ChessableCourseId == bid || r.Files.Any(f => f.FileName == repFile)))
            .Select(r => r.Id)
            .ToListAsync(ct);

        foreach (var repId in reps)
        {
            // NUR Dateien laden, die den Marker überhaupt enthalten. Vorher gingen die vollständigen
            // LONGTEXTs ALLER Dateien des Repertoires über die Leitung (ein importierter Kurs = mehrere
            // MB), je gemeldeter Linie — obwohl höchstens eine Datei den oid trägt. Der Filter läuft
            // serverseitig (`LOCATE`), der Transfer entfällt für alles Übrige.
            var marker = $"[ChessableOid \"{oid}\"]";
            var contents = await _db.RepertoireFiles
                .Where(f => f.RepertoireId == repId && f.PgnContent.Contains(marker))
                .Select(f => f.PgnContent)
                .ToListAsync(ct);

            foreach (var content in contents)
            {
                var (sans, startFen) = MainlineForOid(content, oid);
                if (sans is null || sans.Count == 0) continue;
                // ZWEI Schlüssel-Kandidaten: der textuell normalisierte (bisheriges Verhalten, hält
                // bestehende Karten auffindbar) und der über das Brett kanonisierte (die Schreibweise,
                // die chess.js im Frontend erzeugt — nur der trifft eine Linie mit lang-algebraischen
                // Tokens wie „Nf3e5"). Gesucht wird unter beiden; ANGELEGT wird unter dem
                // brett-kanonischen, weil genau den der Trainer später berechnet.
                var textKey = LineKeyFromSans(sans);
                var boardSans = BoardCanonicalSans(sans, startFen);
                var boardKey = boardSans is null ? null : LineKeyFromSans(boardSans);
                var keys = boardKey is null || boardKey == textKey
                    ? new[] { textKey }
                    : new[] { boardKey, textKey };

                var card = await _db.RepertoireCardStates.AsNoTracking()
                    .Where(c => c.UserId == userId && c.RepertoireId == repId && keys.Contains(c.CardKey))
                    .OrderByDescending(c => c.CardKey == keys[0])   // vorhandene Karte gewinnt
                    .FirstOrDefaultAsync(ct);
                var lineKey = card?.CardKey ?? boardKey ?? textKey;

                // Neu/nie gelernt → „einmal gelernt" (Stufe 1). Fällig → +1 Stufe. Sonst: Finger weg
                // (nicht fällige Linien nicht doppelt strecken); pausierte respektieren die Pause.
                var now = DateTime.UtcNow;
                var actionable = card is null
                    || card.Level <= 0
                    || (card.InPool && !card.Paused && card.DueAt <= now);
                if (card is { Paused: true }) actionable = false;
                if (!actionable) { result.RepertoireLinesSkipped++; continue; }

                var state = await _training.ReviewLineAsync(userId, repId,
                    new LineReviewRequest { LineKey = lineKey, Correct = true }, ct);
                if (state is not null) result.RepertoireLinesAdvanced++;
                break;   // Linie in dieser Datei gefunden — restliche Dateien desselben Reps sparen
            }
        }

        return result;
    }

    // ===== PGN: Spiel mit passender ChessableOid finden + Mainline-SANs ziehen =====

    private static readonly Regex CommentRegex = new(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex NagRegex = new(@"\$\d+", RegexOptions.Compiled);
    private static readonly Regex MoveNumberRegex = new(@"^\d+\.+$", RegexOptions.Compiled);
    private static readonly HashSet<string> ResultTokens = new() { "1-0", "0-1", "1/2-1/2", "*" };

    /// <summary>Mainline-SANs des Spiels mit Header <c>[ChessableOid "oid"]</c> — Varianten in
    /// Klammern werden übersprungen (die SR-Linie ist die Hauptlinie des Spiels), Kommentare/NAGs/
    /// Zugnummern entfernt, Suffix-Annotationen gestrippt (Spiegel von <c>normSan</c>).</summary>
    internal static List<string>? MainlineSansForOid(string? pgn, string oid)
        => MainlineForOid(pgn, oid).Sans;

    /// <summary>Wie <see cref="MainlineSansForOid"/>, gibt aber auch die Start-FEN des Abschnitts
    /// mit — die braucht die Brett-Kanonisierung (<see cref="BoardCanonicalSans"/>).</summary>
    internal static (List<string>? Sans, string? StartFen) MainlineForOid(string? pgn, string oid)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return (null, null);
        var marker = $"[ChessableOid \"{oid}\"]";
        // NUR das Fenster um den Marker schneiden, statt das GANZE PGN in alle [Event-Abschnitte zu
        // zerlegen: ein importierter Chessable-Kurs bringt mehrere MB und tausende Linien mit, und
        // die Extension meldet JEDE trainierte Linie einzeln — der Regex-Split erzeugte je Meldung
        // tausende Strings, um genau einen Abschnitt zu benutzen.
        var at = pgn.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return (null, null);
        var start = LastEventHeaderBefore(pgn, at);
        var next = NextEventHeaderFrom(pgn, at);
        var section = next < 0 ? pgn[start..] : pgn[start..next];
        return (MainlineSans(section), StartFenOf(section));
    }

    /// <summary>Index des letzten <c>[Event …</c>-Headers VOR <paramref name="before"/> (0, wenn keiner).</summary>
    private static int LastEventHeaderBefore(string pgn, int before)
    {
        for (var i = pgn.LastIndexOf("[Event", before, StringComparison.Ordinal); i >= 0;
             i = i == 0 ? -1 : pgn.LastIndexOf("[Event", i - 1, StringComparison.Ordinal))
        {
            if (IsEventHeaderAt(pgn, i)) return i;
        }
        return 0;
    }

    /// <summary>Index des nächsten <c>[Event …</c>-Headers ab <paramref name="from"/> (-1, wenn keiner).</summary>
    private static int NextEventHeaderFrom(string pgn, int from)
    {
        for (var i = pgn.IndexOf("[Event", from, StringComparison.Ordinal); i >= 0;
             i = pgn.IndexOf("[Event", i + 1, StringComparison.Ordinal))
        {
            if (IsEventHeaderAt(pgn, i)) return i;
        }
        return -1;
    }

    /// <summary>Wie der frühere Split-Ausdruck <c>(?=\[Event\s)</c>: nach „[Event" MUSS ein
    /// Leerzeichen folgen — sonst wäre „[EventDate" ein Abschnittsanfang.</summary>
    private static bool IsEventHeaderAt(string pgn, int i)
        => i + 6 < pgn.Length && char.IsWhiteSpace(pgn[i + 6]);

    internal static List<string> MainlineSans(string section)
    {
        // Header abtrennen (Zeilen [Tag "…"]), dann Movetext säubern.
        var sb = new System.Text.StringBuilder();
        foreach (var raw in section.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) continue;
            sb.Append(line).Append(' ');
        }
        var movetext = NagRegex.Replace(CommentRegex.Replace(sb.ToString(), " "), " ");

        var sans = new List<string>();
        var depth = 0;
        foreach (var token in movetext.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token;
            // Klammern kleben oft am Token („(3.Nc3" / „dxe4)").
            while (t.StartsWith('(')) { depth++; t = t[1..]; }
            var closes = 0;
            while (t.EndsWith(')')) { closes++; t = t[..^1]; }
            if (depth == 0 && t.Length > 0)
            {
                // ERST kanonisieren, DANN entscheiden: `IsMoveToken` prüft das erste Zeichen, und
                // eine Rochade mit Nullen („0-0") fiele sonst durchs Raster und würde still ganz
                // aus der Zugliste fallen — das ändert die LÄNGE der Liste und damit den Hash
                // komplett (die Karte wäre unauffindbar, `MarkTrainedAsync` legte eine Phantom-
                // Karte an). Nach `CanonicalSan` heißt sie „O-O" und passiert die Prüfung.
                var clean = CanonicalSan(t);
                if (clean.Length > 0 && IsMoveToken(clean)) sans.Add(clean);
            }
            depth = Math.Max(0, depth - closes);
        }
        return sans;
    }

    private static readonly Regex FenHeaderRegex =
        new(@"^\[FEN\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    // Lange algebraische Notation: [Figur]<von><-|x><nach>[=P]  („e2e4", „Nf3xe5", „Kh8-g8")
    private static readonly Regex LongAlgRegex =
        new(@"^([KQRBN]?)([a-h][1-8])[-x]?([a-h][1-8])=?([QRBN])?$", RegexOptions.Compiled);

    /// <summary>
    /// Dieselbe Zugfolge, aber in der SAN-Schreibweise, die auch <b>chess.js</b> im Frontend liefert
    /// — die Züge werden dazu auf einem Brett NACHGESPIELT und die vom Brett erzeugte SAN genommen.
    /// <para>Grund: der Linien-Schlüssel entsteht im Frontend aus <c>chess.history()</c>, hier aus
    /// ROHEN PGN-Tokens. <see cref="CanonicalSan"/> gleicht nur TEXTUELL an (Suffixe, „0-0", „c8Q");
    /// jede Schreibweise, die erst über die STELLUNG auflösbar ist, driftet weiter: „Nf3e5" (lang
    /// algebraisch) bleibt „Nf3e5", chess.js schreibt „Nxe5" — anderer Hash, die Karte des Trainers
    /// wird nicht gefunden, und der Fortschritt rückt still nicht vor, während eine unerreichbare
    /// Phantom-Karte entsteht. Dass solche Tokens in diesen PGNs vorkommen, belegt der Reparatur-Code
    /// des Frontends (<c>repertoire-tree.util.ts</c>), der genau dafür existiert.</para>
    /// <para><c>null</c>, wenn die Stellung oder ein Zug nicht auflösbar ist (dann bleibt es beim
    /// textuellen Schlüssel) — bewusst KEIN Teilergebnis: ein kürzeres Präfix wäre eine andere Linie.</para>
    /// </summary>
    internal static List<string>? BoardCanonicalSans(IReadOnlyList<string> rawSans, string? startFen = null)
    {
        if (rawSans.Count == 0) return null;
        ChessBoard board;
        try
        {
            board = string.IsNullOrWhiteSpace(startFen) ? new ChessBoard() : ChessBoard.LoadFromFen(startFen);
        }
        catch { return null; }

        var sans = new List<string>(rawSans.Count);
        foreach (var raw in rawSans)
        {
            var token = CanonicalSan(raw);
            if (token.Length == 0) return null;
            Move? found = null;
            foreach (var m in board.Moves(generateSan: true))
            {
                if (Matches(m, token)) { found = m; break; }
            }
            if (found is null) return null;
            sans.Add(string.IsNullOrEmpty(found.San) ? token : CanonicalSan(found.San));
            try { board.Move(found); } catch { return null; }
        }
        return sans;
    }

    /// <summary>Passt ein legaler Zug zu diesem Token? Erlaubt die kurze SAN des Bretts, reine
    /// UCI-Form („e2e4") und lange algebraische Notation mit Figur/Trennzeichen („Nf3xe5").</summary>
    private static bool Matches(Move m, string token)
    {
        if (!string.IsNullOrEmpty(m.San) && CanonicalSan(m.San) == token) return true;
        var uci = m.OriginalPosition.ToString() + m.NewPosition.ToString();
        var promo = m.Parameter?.ShortStr;
        var promoChar = !string.IsNullOrEmpty(promo) && promo.StartsWith('=') && promo.Length >= 2
            ? char.ToLowerInvariant(promo[1]) : '\0';
        if (promoChar != '\0') uci += promoChar;
        if (string.Equals(uci, token, StringComparison.OrdinalIgnoreCase)) return true;

        var la = LongAlgRegex.Match(token);
        if (!la.Success) return false;
        var candidate = la.Groups[2].Value + la.Groups[3].Value;
        if (la.Groups[4].Success) candidate += char.ToLowerInvariant(la.Groups[4].Value[0]);
        return string.Equals(candidate, uci, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Start-FEN eines PGN-Abschnitts (<c>[FEN "…"]</c>), sonst <c>null</c> = Grundstellung.</summary>
    internal static string? StartFenOf(string section)
    {
        var m = FenHeaderRegex.Match(section ?? string.Empty);
        var fen = m.Success ? m.Groups[1].Value.Trim() : null;
        return string.IsNullOrWhiteSpace(fen) ? null : fen;
    }

    private static bool IsMoveToken(string token)
    {
        if (MoveNumberRegex.IsMatch(token) || ResultTokens.Contains(token)) return false;
        // Ein Punkt kommt in echter SAN nie vor: das filtert die Annotation „e.p." (die sonst
        // wegen des führenden 'e' als Zug durchginge und die Liste verlängert) ebenso wie
        // angeklebte Zugnummern („3.Nc3"). Letztere trennen piratechess-PGNs ohnehin mit
        // Leerzeichen ab; chess.js liefert im Frontend in beiden Fällen keinen Extra-Zug.
        if (token.Contains('.')) return false;
        var c = token[0];
        return (c >= 'a' && c <= 'h') || c is 'K' or 'Q' or 'R' or 'B' or 'N' or 'O';
    }

    // ===== Spiegel des Frontend-Linien-Schlüssels (repertoire-line-key.util.ts) =====
    // 'l' + cyrb53(normalisierte SANs, ' '-getrennt) in Basis 36. MUSS mit dem Frontend identisch
    // bleiben — sonst findet der Server fremde CardKeys nicht (Vektoren-Test hält das fest).

    internal static string LineKeyFromSans(IEnumerable<string> sans)
    {
        var norm = string.Join(' ', sans.Select(CanonicalSan));
        return "l" + ToBase36(Cyrb53(norm));
    }

    private static readonly Regex PromotionSanRegex =
        new(@"^([a-h]?x?[a-h][18])=?([nbrqNBRQ])$", RegexOptions.Compiled);

    /// <summary>
    /// SAN so schreiben, wie chess.js sie im Frontend liefert — der Schlüssel wird dort aus
    /// <c>chess.history()</c> gebildet, hier aus ROHEN PGN-Tokens. Ohne Angleichung driften beide
    /// auseinander, sobald das PGN nicht-kanonisch schreibt: Chessable notiert Umwandlungen ohne
    /// „=" (<c>c8Q</c>, in Prod in 10 Repertoire-Dateien belegt), chess.js macht daraus <c>c8=Q</c>
    /// → anderer Hash → „auf Chessable trainiert" findet die Karte nicht und der SR-Fortschritt
    /// rückt still nicht vor. Dieselbe Normalisierung steht im Frontend in
    /// <c>repertoire-tree.util.ts#normSan</c>; auf bereits kanonischer Eingabe ist sie ein No-op,
    /// bestehende gespeicherte Schlüssel bleiben also gültig.
    /// </summary>
    internal static string CanonicalSan(string san)
    {
        var s = (san ?? string.Empty).TrimEnd('+', '#', '!', '?').Trim();
        if (s.Length == 0) return s;
        if (s == "0-0") return "O-O";                 // PGN erlaubt Nullen, chess.js schreibt Buchstaben
        if (s == "0-0-0") return "O-O-O";
        var m = PromotionSanRegex.Match(s);
        return m.Success ? m.Groups[1].Value + "=" + char.ToUpperInvariant(m.Groups[2].Value[0]) : s;
    }

    private static ulong Cyrb53(string str, int seed = 0)
    {
        unchecked
        {
            var h1 = (int)0xdeadbeef ^ seed;
            var h2 = 0x41c6ce57 ^ seed;
            foreach (var ch in str)
            {
                h1 = Imul(h1 ^ ch, unchecked((int)2654435761));
                h2 = Imul(h2 ^ ch, 1597334677);
            }
            h1 = Imul(h1 ^ (int)((uint)h1 >> 16), unchecked((int)2246822507));
            h1 ^= Imul(h2 ^ (int)((uint)h2 >> 13), unchecked((int)3266489909));
            h2 = Imul(h2 ^ (int)((uint)h2 >> 16), unchecked((int)2246822507));
            h2 ^= Imul(h1 ^ (int)((uint)h1 >> 13), unchecked((int)3266489909));
            return 4294967296UL * (ulong)(h2 & 2097151) + (uint)h1;
        }
    }

    private static int Imul(int a, int b) => unchecked(a * b);

    private static string ToBase36(ulong value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0) return "0";
        var sb = new System.Text.StringBuilder();
        while (value > 0)
        {
            sb.Insert(0, digits[(int)(value % 36)]);
            value /= 36;
        }
        return sb.ToString();
    }
}
