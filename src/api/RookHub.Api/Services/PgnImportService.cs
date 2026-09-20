using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Serverseitiges Parsen von ChessBase-/Standard-PGN-Dateien zu Buch-Puzzles und deren Persistenz.
/// Repliziert das Verhalten von rookhub/scripts/import_books.py:
/// FEN-Header + komplette Hauptvariante als UCI + erster Kommentar; SAN→UCI via Gera.Chess.
/// <para>Die reine (DB-freie) Parsing-Mechanik liegt in <see cref="PgnParser"/>; diese Klasse ist der
/// Orchestrator: <see cref="ParsePgn"/> baut aus den Parser-Bausteinen die Puzzles, <see cref="ImportFileAsync"/>
/// legt Book + BookPuzzles an bzw. aktualisiert sie in-place.</para>
/// </summary>
public class PgnImportService
{
    private readonly AppDbContext _db;
    private readonly IBackgroundTaskQueue? _bgQueue;

    // bgQueue ist optional: per DI injiziert (reiht nach Import die Tipp-Generierung ein); bei direkter
    // Instanziierung (Tests) null → kein Enqueue.
    public PgnImportService(AppDbContext db, IBackgroundTaskQueue? bgQueue = null)
    {
        _db = db;
        _bgQueue = bgQueue;
    }

    /// <summary>Entfernt PGN-Suffixe für den Anzeigenamen (wie schach-bot _clean_book_name).</summary>
    public static string CleanDisplayName(string fileName)
    {
        if (fileName.EndsWith("_firstkey.pgn", StringComparison.OrdinalIgnoreCase))
            return fileName[..^"_firstkey.pgn".Length];
        if (fileName.EndsWith(".pgn", StringComparison.OrdinalIgnoreCase))
            return fileName[..^".pgn".Length];
        return fileName;
    }

    /// <summary>Ein aus der PGN extrahiertes Puzzle (DB-frei, daher gut testbar).</summary>
    public record ParsedPuzzle(
        string LineId, string Round, string Fen, string Moves, int StartPly,
        string? Title, string? Chapter, string? Comment,
        Dictionary<int, string>? MoveComments = null, bool IsInfoOnly = false,
        Dictionary<int, List<PgnParser.MoveShape>>? MoveShapes = null,
        Dictionary<int, List<string>>? AltMoves = null,
        string? ChessableOid = null);

    /// <summary>
    /// Ergebnis eines PGN-Parses: extrahierte Puzzles + Anzahl der Spiele, die wegen
    /// fehlender/ungültiger Felder verworfen wurden (kein FEN/Round, keine spielbare
    /// Mainline, Grundstellung ohne Trainingsmarker etc.).
    /// </summary>
    public record ParseResult(List<ParsedPuzzle> Puzzles, int Invalid);

    /// <summary>
    /// Parst einen PGN-Text in eine Liste von Puzzles. Reine Funktion (kein DB-Zugriff).
    /// Ungültige/unparsebare Einträge werden übersprungen und in <see cref="ParseResult.Invalid"/>
    /// gezählt, nicht geworfen.
    /// </summary>
    /// <param name="keepCommentOnlyAsInfo">Wenn true (Buch-/Kurs-Import): zug-lose Linien MIT Erklärtext
    /// (Chessable-Intro-/Info-Seiten) werden nicht verworfen, sondern als Info-Linie behalten — mit der
    /// ECHTEN Stellung aus dem [FEN]-Header (Züge leer), <c>IsInfoOnly</c>, damit der Text + die richtige
    /// Stellung beim sequenziellen Durcharbeiten/Durchsehen erscheinen (kein Quiz, nicht in Random/Daily).
    /// Default false (z. B. Wochenpost = index-basiert, unverändert).</param>
    /// <param name="playFromStartPosition">Wenn true (EIGENE Kurse des Nutzers: hochgeladenes PGN,
    /// umgewandeltes Repertoire): Linien aus der GRUNDSTELLUNG ohne Trainingsmarker werden nicht
    /// verworfen, sondern spielbar — sie sind das Repertoire des Nutzers. Der Trainingsstart ergibt
    /// sich dann aus der Seite, der das Repertoire gehört (siehe <see cref="StartPlyForRepertoire"/>).
    /// Default false: fuer die globalen Puzzle-Buecher bleibt eine ganze Partie ohne Aufgabe
    /// weiterhin kein Puzzle.</param>
    public static ParseResult ParsePgn(string fileName, string pgnText, bool keepCommentOnlyAsInfo = false,
        bool playFromStartPosition = false)
    {
        var result = new List<ParsedPuzzle>();
        // Repertoire-Linien aus der Grundstellung: erst sammeln, denn ihr Trainingsstart haengt an
        // der MEHRHEIT aller Linien der Datei und nicht an der einzelnen Linie.
        var fromStart = new List<ParsedPuzzle>();
        var invalid = 0;
        foreach (var (headers, moveText) in PgnParser.SplitGames(pgnText))
        {
            var fen = headers.GetValueOrDefault("FEN", "").Trim();
            var round = headers.GetValueOrDefault("Round", "").Trim();
            // Chessable-oid (von piratechess als [ChessableOid] mitgegeben) → eindeutige Zuordnung
            // importierte Linie ↔ Chessable-Linie für die Fortschritts-Overlays. null wenn nicht vorhanden.
            var oidHdr = headers.GetValueOrDefault("ChessableOid", "").Trim();
            var chessableOid = string.IsNullOrEmpty(oidHdr) ? null : PgnParser.Truncate(oidHdr, 32);
            // Solverfarbe der Chessable-Linie ([ChessableColor], piratechess ab v1.0.46). Sie trägt den
            // Trainingsstart in PGNs OHNE [%tqu] (Repertoire-Modus) — siehe StartPlyFromSolverColor.
            var solverColor = headers.GetValueOrDefault("ChessableColor", "").Trim();
            // Skip-Regeln wie import_books.py
            if (string.IsNullOrEmpty(fen) || fen == "?") { invalid++; continue; }
            if (string.IsNullOrEmpty(round) || round == "?") { invalid++; continue; }

            var comment = PgnParser.ExtractFirstComment(moveText);
            // foldAllVariations: jede Variante landet (mit Zugnummern) im Kommentar ihres Zugs → das
            // Frontend macht die Züge dort klickbar. Sonst gingen Varianten ohne eigenen Zug-Kommentar verloren.
            var moveComments = PgnParser.ExtractMoveComments(moveText, foldAllVariations: true);
            var moveShapes = PgnParser.ExtractMoveShapes(moveText);
            // Von Chessable geduldete Alternativzüge (softFail → [%alt]) je Halbzug als UCI. Anknüpfpunkt
            // ist die Stellung VOR dem jeweiligen Hauptzug (siehe ExtractAltMoves). Nur für die echte
            // Puzzle-Linie sinnvoll (die Info-Linie unten nutzt eine synthetische FEN/Zugliste).
            var altMoves = PgnParser.ExtractAltMoves(fen, moveText);
            // Info-/Erklärlinie? piratechess setzt [%info] für Chessable-IsInfo-Linien (kein [%tqu]).
            // Solche Linien werden nicht abgefragt, sondern nur durchgeklickt → IsInfoOnly markieren.
            var isInfoOnly = moveText.Contains("[%info", StringComparison.OrdinalIgnoreCase);
            var uci = PgnParser.TryExtractUciMainline(fen, moveText);
            if (uci == null || uci.Count == 0)
            {
                // Zug-lose Linie mit Erklärtext (Chessable-Intro-/Info-Seite): nicht verwerfen, sondern
                // als Info-Linie behalten (IsInfoOnly, nur sequenziell zum Durchklicken / im Durchsehen).
                // Die ECHTE Stellung aus dem [FEN]-Header übernehmen (Moves leer) — z. B. eine
                // „⏲Exercise #N - Introduction"/„Evaluate 11…Nxe5"-Seite zeigt so die tatsächliche
                // Aufgabenstellung statt der Grundstellung. Zwei Ausprägungen kommen vor:
                //  • reiner Kommentar ohne Zug-Token → `comment` (erster Kommentar) trägt den Text;
                //  • Chessable-Kapitel-Intro `{[%info]} 1. -- {Text}` → NULL-Zug `--`, der erste
                //    Kommentar ist nur der leere [%info]-Marker, der Text steht im ZWEITEN (Zug-)
                //    Kommentar. Daher robust den ersten NICHT-leeren Kommentar nehmen.
                var infoText = !string.IsNullOrEmpty(comment) ? comment : PgnParser.FirstNonEmptyComment(moveText);
                if (keepCommentOnlyAsInfo && (isInfoOnly || !string.IsNullOrEmpty(infoText)))
                {
                    var iw = headers.GetValueOrDefault("White", "").Trim();
                    var ib = headers.GetValueOrDefault("Black", "").Trim();
                    // Ist die FEN nur ILLEGAL (Chessable-Muster-Diagramm ohne König), sind die Demo-
                    // Züge trotzdem im PGN — permissiv nach UCI auflösen, damit die Info-Linie
                    // durchklickbar wird (das Frontend spielt sie ohne Legalitätsprüfung nach).
                    var infoUci = PgnParser.TryExtractUciMainlinePermissive(fen, moveText);
                    result.Add(new ParsedPuzzle(
                        LineId: PgnParser.Truncate($"{fileName}:{round}", 300),
                        Round: PgnParser.Truncate(round, 20),
                        Fen: fen,
                        Moves: infoUci == null ? "" : string.Join(' ', infoUci),
                        StartPly: -1,
                        Title: iw.Length == 0 ? null : PgnParser.Truncate(iw, 300),
                        Chapter: ib.Length == 0 ? null : PgnParser.Truncate(ib, 200),
                        Comment: infoText,
                        MoveComments: moveComments,
                        IsInfoOnly: true,
                        MoveShapes: moveShapes,
                        ChessableOid: chessableOid));
                    continue;
                }
                invalid++; continue;
            }

            // Trainingsstart bestimmen. Zwei Buch-Typen kommen vor:
            //  (a) Mid-line-[%tqu]: ganze Partie ab Grundstellung, der Marker hängt an Zug k
            //      → StartPly = k, fen+moves bleiben die KOMPLETTE Partie, gelöst ab moves[k+1].
            //  (b) FEN ist bereits die Puzzle-Stellung (kein/Wurzel-Marker) → gelöst ab moves[0]
            //      → StartPly = -1.
            // Ausnahme: FEN = Grundstellung OHNE Mid-line-Marker = ganze Partie ohne definierten
            // Trainingsstart → kein Puzzle, überspringen (wie der Bot non-[%tqu]-Partien filtert).
            var tquIndex = PgnParser.FindTquMoveIndex(moveText);
            int startPly;
            if (tquIndex is int k && k >= 0 && k <= uci.Count - 2)
            {
                startPly = k;
            }
            else
            {
                // Grundstellung ohne Trainingsmarker = kein definierter Trainingsstart → verwerfen.
                // AUSNAHMEN: Info-Linien behalten wir (werden nicht abgefragt, nur durchgeklickt),
                // und beim EIGENEN Kurs ist genau das das Repertoire des Nutzers (siehe
                // playFromStartPosition) — dort wird die Linie unten mit dem gemeinsamen
                // Trainingsstart nachgetragen.
                if (PgnParser.IsStartPosition(fen) && !isInfoOnly)
                {
                    if (!playFromStartPosition) { invalid++; continue; }
                    var w0 = headers.GetValueOrDefault("White", "").Trim();
                    var b0 = headers.GetValueOrDefault("Black", "").Trim();
                    fromStart.Add(new ParsedPuzzle(
                        LineId: PgnParser.Truncate($"{fileName}:{round}", 300),
                        Round: PgnParser.Truncate(round, 20),
                        Fen: fen,
                        Moves: string.Join(' ', uci),
                        StartPly: -1,   // vorlaeufig; unten gemeinsam gesetzt
                        Title: string.IsNullOrEmpty(w0) ? null : PgnParser.Truncate(w0, 300),
                        Chapter: string.IsNullOrEmpty(b0) ? null : PgnParser.Truncate(b0, 200),
                        Comment: comment,
                        MoveComments: moveComments,
                        IsInfoOnly: false,
                        MoveShapes: moveShapes,
                        AltMoves: altMoves,
                        ChessableOid: chessableOid));
                    continue;
                }
                startPly = StartPlyFromSolverColor(fen, solverColor, uci.Count);
            }

            var white = headers.GetValueOrDefault("White", "").Trim();
            var black = headers.GetValueOrDefault("Black", "").Trim();

            result.Add(new ParsedPuzzle(
                LineId: PgnParser.Truncate($"{fileName}:{round}", 300),
                Round: PgnParser.Truncate(round, 20),
                Fen: fen,
                Moves: string.Join(' ', uci),
                StartPly: startPly,
                Title: string.IsNullOrEmpty(white) ? null : PgnParser.Truncate(white, 300),
                Chapter: string.IsNullOrEmpty(black) ? null : PgnParser.Truncate(black, 200),
                Comment: comment,
                MoveComments: moveComments,
                IsInfoOnly: isInfoOnly,
                MoveShapes: moveShapes,
                AltMoves: altMoves,
                ChessableOid: chessableOid));
        }

        // Die gesammelten Repertoire-Linien bekommen JETZT ihren gemeinsamen Trainingsstart.
        if (fromStart.Count > 0)
        {
            var startPlyForAll = StartPlyForRepertoire(fromStart);
            foreach (var p in fromStart)
                result.Add(p with { StartPly = startPlyForAll });
        }

        return new ParseResult(result, invalid);
    }

    // ---- Persistenz -------------------------------------------------------
    /// <summary>
    /// Erkennt eine Kapitel-Überschrift mit motivverratendem Titel („Chapter 2: Back-Rank Mates",
    /// „Kapitel 3: Abzugsschach") und behält nur das Label („Chapter 2"). Greift bewusst nur bei
    /// diesem Muster — freie Kapitelnamen ohne „Chapter/Kapitel N:"-Präfix bleiben unangetastet.
    /// </summary>
    private static readonly Regex ChapterSpoilerRx =
        new(@"^(\s*(?:chapter|kapitel|poglavlje)\s+\d+)\s*:\s*\S.*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Strippt den Spoiler-Teil eines Kapitelnamens (siehe <see cref="ChapterSpoilerRx"/>).</summary>
    public static string? StripChapterSpoiler(string? chapter)
    {
        if (string.IsNullOrEmpty(chapter)) return chapter;
        var m = ChapterSpoilerRx.Match(chapter);
        return m.Success ? m.Groups[1].Value.Trim() : chapter;
    }

    /// <summary>Kapitelname je Buchart: Puzzle-Bücher werden entschärft (Spoiler raus),
    /// Study-Bücher behalten ihre Kapitelnamen.</summary>
    private static string? ChapterForBook(BookKind kind, string? chapter)
        => kind == BookKind.Puzzle ? StripChapterSpoiler(chapter) : chapter;

    /// <summary>
    /// Parst eine Datei und legt Book + BookPuzzles an. Neue Linien (per LineId) werden hinzugefügt,
    /// bereits vorhandene normalerweise übersprungen (idempotenter (Re-)Import / Resume).
    /// <para>AUSNAHME — Neu-Aufbereitung: Ist das Buch <b>veraltet</b> (<c>Book.ImportVersion &lt;
    /// <see cref="ImportPipeline.CurrentVersion"/></c>), werden bestehende Linien <b>in-place
    /// aktualisiert</b> (Moves/StartPly/Comment/MoveComments/Title/Chapter), statt sie zu überspringen
    /// — die BookPuzzle-Id bleibt erhalten, also auch aller Fortschritt/alle Statistiken, die darauf
    /// verweisen. So holt ein Re-Import eines Altbuchs die neuen abgeleiteten Felder nach.</para>
    /// Normalerweise wird das Roh-PGN als <c>Book.SourcePgn</c> gespeichert und die Pipeline-Version
    /// hochgesetzt, damit das Buch künftig offline neu aufbereitbar ist. <paramref name="preserveExistingSourcePgn"/>
    /// = true (getReview-Lücken-Merge) überschreibt ein bereits vorhandenes <c>SourcePgn</c> NICHT — der
    /// Merge liefert nur die fehlenden Linien, nicht das ganze Buch; ein vollständiges getGame-SourcePgn
    /// bliebe sonst durch das Teil-PGN ersetzt (nur bei leerem SourcePgn wird es erstmalig gesetzt).
    /// </summary>
    /// <summary>Zahl am ENDE einer Rundennummer („002.<b>001</b>", „<b>7</b>") — daran rückt eine Linie vor.</summary>
    private static readonly Regex TrailingNumber = new(@"^(.*?)(\d+)$", RegexOptions.Compiled);

    /// <summary>Reißleine gegen eine Endlosschleife, falls ein ganzes Kapitel dicht belegt ist.</summary>
    private const int MaxLineIdProbes = 1000;

    /// <summary>
    /// Freie <see cref="BookPuzzle.LineId"/> für eine NEU anzulegende Linie. Die Id ist GLOBAL
    /// eindeutig (Index in <c>AppDbContext</c>); ein Duplikat lässt <c>SaveChanges</c> auf MariaDB
    /// werfen und riss damit den ganzen Import ab.
    ///
    /// <para><b>Heute kann das nicht passieren</b> — ein Buch kommt vollständig und in Reihenfolge,
    /// die Rundennummern sind also von sich aus eindeutig. Es ist die Vorarbeit für „eine
    /// Import-Schiene": sobald ein Import nur die NEUEN Linien schickt (Buch wie Repertoire),
    /// nummeriert piratechess nur noch die gesendeten — und dann kann die Nummer einer neuen Linie
    /// auf eine bestehende treffen. <see cref="CourseAuthoringService"/> hat dieselbe Schranke längst.</para>
    ///
    /// <para>Ausgewichen wird INNERHALB des Kapitels (002.005 → 002.006), damit die Linie dort
    /// bleibt, wo sie hingehört. Trägt die Runde keine Zahl am Ende (Altbestand „1", ein
    /// getReview-Füller mit der oid), wird angehängt (…-2, …-3).</para>
    /// </summary>
    /// <param name="vergeben">Bereits benutzte LineIds; die gewählte wird aufgenommen.</param>
    internal static (string LineId, string Round) FreeLineId(string fileName, string round, HashSet<string> vergeben)
    {
        string Bauen(string r) => PgnParser.Truncate($"{fileName}:{r}", 300);

        var id = Bauen(round);
        if (vergeben.Add(id)) return (id, round);

        var m = TrailingNumber.Match(round ?? string.Empty);
        for (var i = 1; i <= MaxLineIdProbes; i++)
        {
            string kandidat;
            if (m.Success)
            {
                var zahl = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + i;
                // Stellenzahl beibehalten, sonst wechselte die Schreibweise mitten im Kapitel
                // (die Lesereihenfolge sortiert nach Round.Length, dann Round).
                kandidat = m.Groups[1].Value
                    + zahl.ToString(new string('0', m.Groups[2].Value.Length), CultureInfo.InvariantCulture);
            }
            else kandidat = $"{round}-{i + 1}";

            // Round ist auf 20 Zeichen begrenzt — lieber laut scheitern als still abschneiden.
            if (kandidat.Length > 20) break;

            id = Bauen(kandidat);
            if (vergeben.Add(id)) return (id, kandidat);
        }
        throw new InvalidOperationException(
            $"Keine freie LineId für {fileName}:{round} gefunden ({MaxLineIdProbes} Versuche).");
    }

    /// <param name="partial">Der Stapel enthält NUR die neuen Linien, nicht den ganzen Kurs (Teil-Import
    /// aus dem Browser). Ändert genau eine Entscheidung: trifft eine neue Linie mit eigener, im Buch
    /// unbekannter oid auf eine schon vergebene Positionsnummer, ist sie eine ANDERE Linie und bekommt
    /// einen freien Platz — bei einem vollständigen Re-Import wäre dieselbe Nummer dagegen dieselbe
    /// Linie mit geändertem Inhalt, und dann darf nichts angelegt werden.</param>
    public async Task<BookImportItemDto> ImportFileAsync(string fileName, string pgnText, CancellationToken ct,
        bool preserveExistingSourcePgn = false, bool playFromStartPosition = false, bool partial = false)
    {
        // Buch-/Kurs-Import: zug-lose Erklär-/Intro-Seiten als Info-Linien behalten (sequenziell durchklickbar).
        var (parsed, invalid) = ParsePgn(fileName, pgnText, keepCommentOnlyAsInfo: true,
            playFromStartPosition: playFromStartPosition);
        var now = DateTime.UtcNow;

        var book = await _db.Books.FirstOrDefaultAsync(b => b.FileName == fileName, ct);
        if (book == null)
        {
            book = new Book
            {
                FileName = PgnParser.Truncate(fileName, 200),
                DisplayName = PgnParser.Truncate(CleanDisplayName(fileName), 200),
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Books.Add(book);
            await _db.SaveChangesAsync(ct); // Id materialisieren
        }

        // Veraltetes Buch ⇒ bestehende Linien aktualisieren statt überspringen (Neu-Aufbereitung).
        var upgrade = book.ImportVersion < ImportPipeline.CurrentVersion;

        // Bestehende Linien laden: beim Upgrade ALLE als TRACKED Entities (zum In-place-Aktualisieren).
        var existing = upgrade
            ? await _db.BookPuzzles.Where(bp => bp.BookId == book.Id || bp.BookFileName == fileName)
                .ToDictionaryAsync(bp => bp.LineId, ct)
            : new Dictionary<string, BookPuzzle>();
        var existingLineIds = upgrade
            ? existing.Keys.ToHashSet()
            : await _db.BookPuzzles.Where(bp => bp.BookId == book.Id || bp.BookFileName == fileName)
                .Select(bp => bp.LineId).ToHashSetAsync(ct);

        // Alt-Linien ohne Chessable-oid (Import vor piratechess v1.29.0): kommt dieselbe Linie — gleiche LineId UND
        // gleiche Züge — mit oid herein, bekommt die vorhandene sie nachgetragen. Sonst erkennt das Fortschritts-
        // Overlay sie nie als importiert, und jedes „Kurs holen" holt sie erneut. Beim Upgrade übernimmt der
        // In-place-Pfad die oid ohnehin. Eine oid, die im Buch schon vorkommt (z. B. an einem Review-Füller),
        // wird NICHT ein zweites Mal vergeben.
        var oidlessByLineId = new Dictionary<string, BookPuzzle>();
        var bookOids = new HashSet<string>(StringComparer.Ordinal);
        if (!upgrade)
        {
            foreach (var bp in await _db.BookPuzzles
                    .Where(bp => (bp.BookId == book.Id || bp.BookFileName == fileName) && bp.ChessableOid == null)
                    .ToListAsync(ct))
                oidlessByLineId.TryAdd(bp.LineId, bp);
            bookOids.UnionWith(await _db.BookPuzzles
                .Where(bp => (bp.BookId == book.Id || bp.BookFileName == fileName) && bp.ChessableOid != null)
                .Select(bp => bp.ChessableOid!)
                .ToListAsync(ct));
        }

        // ===== Identität einer Chessable-Linie: die oid. =====
        // Nicht ihre Position im Kurs. Dieselbe Linie kann unter einer anderen Nummer ankommen — ein Kapitel
        // wird umsortiert, eine Lücke davor gefüllt, ein Kapitel kommt in Teilen. Die Nummer (Round, und die
        // daraus gebaute LineId) ist deshalb nur ein ETIKETT; wer eine Linie IST, sagt allein die oid.
        //
        // Vorher liefen hier drei Abgleiche nebeneinander (LineId, Review-Füller je oid, oid-Nachtrag), die
        // sich gegenseitig absichern mussten: getReview belegt eine Lücke mit LineId={file}:{oid}, das echte
        // getGame liefert Round="Kapitel.Index" — die LineIds passen also NICHT, und ein reiner LineId-
        // Abgleich legte ein Duplikat an. Mit der oid als Schlüssel ist das EIN Fall statt drei.
        //
        // Geladen wird nur, was dieser Stapel überhaupt mitbringt (oids in Blöcken), damit ein Re-Import
        // eines großen Kurses nicht das ganze Buch als getrackte Entities in den Speicher zieht.
        var batchOids = parsed
            .Where(p => !string.IsNullOrEmpty(p.ChessableOid))
            .Select(p => p.ChessableOid!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var byOid = new Dictionary<string, BookPuzzle>(StringComparer.Ordinal);
        foreach (var block in batchOids.Chunk(500))
        {
            foreach (var bp in await _db.BookPuzzles
                    .Where(bp => (bp.BookId == book.Id || bp.BookFileName == fileName)
                        && bp.ChessableOid != null && block.Contains(bp.ChessableOid))
                    .ToListAsync(ct))
                byOid[bp.ChessableOid!] = bp;   // duplikat-tolerant (letzter gewinnt), kein ToDictionary-Wurf
        }

        var toAdd = new List<BookPuzzle>();
        // In DIESEM Durchlauf vergebene LineIds — daran wird eine zweite Linie mit derselben
        // Positionsnummer erkannt (s. unten). Getrennt von existingLineIds, weil dort auch der
        // Bestand drinsteht und die Unterscheidung genau darauf beruht.
        var inDiesemImport = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        var updated = 0;
        var seen = new HashSet<string>();

        foreach (var p in parsed)
        {
            // Duplikat im selben Stapel — über die oid, wo es eine gibt (dieselbe Linie kann im Stapel
            // unter zwei Nummern auftauchen, etwa Lücken-Füller + echtes getGame).
            if (!seen.Add(string.IsNullOrEmpty(p.ChessableOid) ? p.LineId : "oid:" + p.ChessableOid))
            { skipped++; continue; }

            // ===== Abgleich über die oid: dieselbe Linie, egal unter welcher Nummer sie früher lag. =====
            // Die vorhandene Zeile wird WIEDERVERWENDET, nie gelöscht+neu angelegt: auf ihr kann schon
            // Fortschritt liegen (CoursePuzzleResult/CourseAttempt/CalculationTree = Restrict-FKs → ein
            // Delete würde in MariaDB werfen und den Fortschritt verlieren).
            if (!string.IsNullOrEmpty(p.ChessableOid) && byOid.TryGetValue(p.ChessableOid, out var sameLine))
            {
                // Ein getReview-Lücken-Füller wird vom echten getGame abgelöst („getGame gewinnt"):
                // inhaltlich sind beide für die Linie deckungsgleich, der Fortschritt gilt also weiter.
                var warFüller = sameLine.Source is not null;
                if (upgrade || warFüller)
                {
                    sameLine.Fen = p.Fen;
                    sameLine.Moves = p.Moves;
                    sameLine.StartPly = p.StartPly;
                    sameLine.Comment = p.Comment;
                    sameLine.MoveComments = p.MoveComments == null ? null : JsonSerializer.Serialize(p.MoveComments);
                    sameLine.MoveShapes = p.MoveShapes == null ? null : JsonSerializer.Serialize(p.MoveShapes);
                    sameLine.AltMoves = p.AltMoves == null ? null : JsonSerializer.Serialize(p.AltMoves);
                    sameLine.IsInfoOnly = p.IsInfoOnly;
                    sameLine.Source = null;   // ab jetzt vollwertig (getGame)
                    updated++;
                }
                else if (sameLine.Moves != p.Moves)
                {
                    // Die oid sagt „dieselbe Linie", die Züge sagen etwas anderes. Das ist ein Widerspruch
                    // (ein verrutschter oid-Header hat das 2026-09 schon einmal getan) — dann lieber nichts
                    // anfassen, als eine fremde Linie umzuetikettieren. Inhalt ändert ohnehin nur ein Upgrade.
                    skipped++;
                    continue;
                }
                else if (sameLine.Round != p.Round)
                {
                    updated++;                // nur die Position hat sich verschoben
                }
                else { skipped++; }

                // Etiketten immer nachziehen — sie beschreiben die Position, nicht die Identität.
                sameLine.Round = p.Round;
                sameLine.Title = p.Title;
                sameLine.Chapter = ChapterForBook(book.Kind, p.Chapter);
                // LineId ist global eindeutig: nur übernehmen, wenn sie frei ist. Sonst behält die Linie
                // ihr altes Etikett — bei einer Umsortierung tauschen sonst zwei Linien ihre LineId und
                // der eindeutige Index schlägt beim Speichern zu.
                if (sameLine.LineId != p.LineId && !existingLineIds.Contains(p.LineId))
                {
                    existingLineIds.Remove(sameLine.LineId);
                    sameLine.LineId = p.LineId;
                    existingLineIds.Add(p.LineId);
                }
                continue;
            }

            var alsNeueLinie = false;
            if (existingLineIds.Contains(p.LineId))
            {
                // In-place aktualisieren, wenn das Buch veraltet ist (Neu-Aufbereitung); sonst
                // überspringen (idempotenter Resume).
                existing.TryGetValue(p.LineId, out var bp);
                if (bp is not null && upgrade)
                {
                    bp.Round = p.Round;
                    bp.Fen = p.Fen;
                    bp.Moves = p.Moves;
                    bp.StartPly = p.StartPly;
                    bp.Title = p.Title;
                    bp.Chapter = ChapterForBook(book.Kind, p.Chapter);
                    bp.Comment = p.Comment;
                    bp.MoveComments = p.MoveComments == null ? null : JsonSerializer.Serialize(p.MoveComments);
                    bp.MoveShapes = p.MoveShapes == null ? null : JsonSerializer.Serialize(p.MoveShapes);
                    bp.AltMoves = p.AltMoves == null ? null : JsonSerializer.Serialize(p.AltMoves);
                    bp.IsInfoOnly = p.IsInfoOnly;
                    if (!string.IsNullOrEmpty(p.ChessableOid)) bp.ChessableOid = p.ChessableOid;
                    updated++;
                }
                else if (!upgrade && !string.IsNullOrEmpty(p.ChessableOid) && !bookOids.Contains(p.ChessableOid)
                         && oidlessByLineId.Remove(p.LineId, out var ohneOid)
                         && ohneOid.Moves == p.Moves && ohneOid.StartPly == p.StartPly)
                {
                    ohneOid.ChessableOid = p.ChessableOid;   // nur die oid — Inhalt und Fortschritt bleiben
                    bookOids.Add(p.ChessableOid);
                    updated++;
                }
                else if (partial && !string.IsNullOrEmpty(p.ChessableOid) && !bookOids.Contains(p.ChessableOid))
                {
                    // Teil-Import: hier nummeriert piratechess nur die GESENDETEN Linien, eine Kollision
                    // mit dem Bestand sagt also nichts über die Identität. Die oid tut das — und die ist
                    // im Buch unbekannt, also eine neue Linie.
                    alsNeueLinie = true;
                }
                else if (inDiesemImport.Contains(p.LineId))
                {
                    // Die Nummer wurde in DIESEM Import gerade erst vergeben, und zwar an eine Linie
                    // mit anderer oid — zwei verschiedene oids können nicht dieselbe Linie sein. Also
                    // eine echte zweite Linie, die nur zufällig auf derselben Nummer sitzt; sie
                    // bekommt unten einen freien Platz. Ohne das liefen BEIDE in denselben Index und
                    // der ganze Import brach am SaveChanges ab.
                    //
                    // Bewusst NICHT auf eine Kollision mit dem BESTAND ausgeweitet: dort ist „gleiche
                    // Nummer, andere Züge" bei einem vollständigen Re-Import dieselbe Linie mit
                    // geändertem Inhalt und bei einem Teil-Import eine andere — unterscheiden kann der
                    // Server das erst, wenn der Client den Teil-Import als solchen meldet.
                    alsNeueLinie = true;
                }
                else { skipped++; }
                if (!alsNeueLinie) continue;
            }
            // Die Rundennummer eines Teil-Imports kann auf eine bestehende Linie treffen → freien
            // Platz im Kapitel suchen, statt den Import am eindeutigen Index scheitern zu lassen.
            var (neueLineId, neueRound) = FreeLineId(fileName, p.Round, existingLineIds);
            inDiesemImport.Add(neueLineId);
            toAdd.Add(new BookPuzzle
            {
                LineId = neueLineId,
                BookFileName = PgnParser.Truncate(fileName, 200),
                BookId = book.Id,
                Round = neueRound,
                Fen = p.Fen,
                Moves = p.Moves,
                StartPly = p.StartPly,
                Title = p.Title,
                Chapter = ChapterForBook(book.Kind, p.Chapter),
                Comment = p.Comment,
                MoveComments = p.MoveComments == null ? null : JsonSerializer.Serialize(p.MoveComments),
                MoveShapes = p.MoveShapes == null ? null : JsonSerializer.Serialize(p.MoveShapes),
                AltMoves = p.AltMoves == null ? null : JsonSerializer.Serialize(p.AltMoves),
                IsInfoOnly = p.IsInfoOnly,
                ChessableOid = p.ChessableOid,
            });
        }

        if (toAdd.Count > 0) _db.BookPuzzles.AddRange(toAdd);

        // Roh-PGN als Reprocessing-Quelle merken + Pipeline-Version hochsetzen. Beim getReview-Merge
        // (preserveExistingSourcePgn) NICHT das vorhandene (ggf. vollständige getGame-)SourcePgn mit dem
        // Teil-PGN der Lücken überschreiben — nur ein noch leeres SourcePgn erstmalig setzen.
        // Ein TEIL-Import trägt per Definition nicht den ganzen Kurs — er darf ein vollständiges
        // SourcePgn also nie ersetzen. Das hängt an `partial` selbst und nicht am Aufrufer: sonst
        // müsste jede Aufrufstelle daran denken, und genau das läuft irgendwann auseinander.
        if ((!preserveExistingSourcePgn && !partial) || string.IsNullOrEmpty(book.SourcePgn))
            book.SourcePgn = pgnText;
        book.ImportVersion = ImportPipeline.CurrentVersion;
        book.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // Tipp-Generierung (LLM + Stockfish) asynchron anstoßen — blockiert den Import nicht.
        // HintGenerationService ist idempotent (überspringt aktuelle Tipps) und no-op ohne API-Key.
        if (_bgQueue != null && (toAdd.Count > 0 || updated > 0))
        {
            var puzzleIds = await _db.BookPuzzles.Where(bp => bp.BookId == book.Id)
                .Select(bp => bp.Id).ToListAsync(ct);
            await _bgQueue.EnqueueAsync(async (sp, token) =>
                await sp.GetRequiredService<HintGenerationService>().GenerateForPuzzlesAsync(puzzleIds, false, token));
        }

        return new BookImportItemDto
        {
            BookId = book.Id,
            FileName = fileName,
            Imported = toAdd.Count,
            Skipped = skipped,
            Updated = updated,
            Invalid = invalid,
        };
    }

    /// <summary>
    /// Wem gehoert dieses Repertoire — und ab welchem Halbzug wird deshalb geuebt?
    ///
    /// <para>Ein Eroeffnungsrepertoire beginnt in der Grundstellung und traegt keinen
    /// Trainingsmarker. Wer zuerst zieht, entscheidet aber darueber, ob der Nutzer den ERSTEN Zug
    /// spielt (sein 1.e4) oder den ZWEITEN (seine Antwort auf 1.d4). Beides falsch zu machen heisst:
    /// der Kurs fragt die Zuege des Gegners ab.</para>
    ///
    /// <para><b>Das Signal ist die Laenge der Linien.</b> Eine Repertoire-Linie endet mit dem
    /// eigenen Zug — man lernt „und dann spiele ich X". Ungerade Zugzahl heisst also Weiss,
    /// gerade heisst Schwarz. Am echten Fall gemessen (Chessable „Lifetime Repertoires: Plichta's
    /// 1.e4", 902 Linien): 897 enden mit einem weissen Zug, 3 nicht. Der erste ZUG taugt dagegen
    /// NICHT als Signal — ein Schwarz-Repertoire gegen 1.d4 beginnt in jeder Linie mit 1.d4.</para>
    ///
    /// <para>Entschieden wird fuer die ganze Datei gemeinsam: eine einzelne Linie sagt nichts
    /// darueber, wem das Repertoire gehoert, und ein Repertoire hat genau einen Besitzer.</para>
    /// </summary>
    /// <returns><c>-1</c> = ab dem ersten Zug loesen (Weiss), <c>0</c> = der erste Zug wird
    /// vorgespielt, geloest wird ab dem zweiten (Schwarz).</returns>
    internal static int StartPlyForRepertoire(IReadOnlyCollection<ParsedPuzzle> fromStart)
    {
        var endsWithWhite = 0;
        foreach (var p in fromStart)
        {
            var plies = p.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (plies % 2 == 1) endsWithWhite++;
        }
        return endsWithWhite * 2 >= fromStart.Count ? -1 : 0;
    }

    /// <summary>
    /// Trainingsstart einer Chessable-Linie OHNE <c>[%tqu]</c>, abgeleitet aus der Solverfarbe
    /// (Header <c>[ChessableColor]</c>, von piratechess auch im Repertoire-Modus mitgegeben).
    /// <para>Chessables Partie-Kurse stellen die Aufgabe oft als „der Gegner hat gerade X gespielt,
    /// widerlege das": der erste Zug der Linie gehoert dann dem GEGNER und wird vorgespielt. Ohne diese
    /// Angabe galt jede solche Linie als „ab der FEN loesen" (StartPly -1), und im Kurs stand die
    /// falsche Seite am Zug — gemeldet am 2026-09-18 an einer Olympiade-Partie, wo RookHub 10...Nd4 vom
    /// Nutzer verlangte, waehrend Chessable den Zug vorspielt und nach 11.Bg5 fragt.</para>
    /// </summary>
    /// <param name="fen">Ausgangsstellung der Linie.</param>
    /// <param name="solverColor">„white"/„black" aus dem Header; leer/unbekannt ⇒ bisheriges Verhalten.</param>
    /// <param name="moveCount">Halbzuege der Hauptlinie. Bei nur EINEM Zug bliebe nach dem Vorspielen
    /// nichts zu loesen — dann bleibt es bei <c>-1</c>.</param>
    /// <returns><c>-1</c> = ab moves[0] loesen, <c>0</c> = moves[0] vorspielen, loesen ab moves[1].</returns>
    internal static int StartPlyFromSolverColor(string fen, string? solverColor, int moveCount)
    {
        if (string.IsNullOrWhiteSpace(solverColor) || moveCount < 2) return -1;
        bool solverIsWhite;
        if (solverColor.Equals("white", StringComparison.OrdinalIgnoreCase)) solverIsWhite = true;
        else if (solverColor.Equals("black", StringComparison.OrdinalIgnoreCase)) solverIsWhite = false;
        else return -1;   // unbekannter Wert: nichts raten

        // Zugfarbe der FEN steht im zweiten Feld ("w"/"b"); fehlt es, gilt Weiss am Zug.
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var whiteToMove = parts.Length < 2 || !parts[1].Equals("b", StringComparison.OrdinalIgnoreCase);
        return whiteToMove == solverIsWhite ? -1 : 0;
    }

}
