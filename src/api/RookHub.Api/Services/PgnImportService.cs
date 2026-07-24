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
    public static ParseResult ParsePgn(string fileName, string pgnText, bool keepCommentOnlyAsInfo = false)
    {
        var result = new List<ParsedPuzzle>();
        var invalid = 0;
        foreach (var (headers, moveText) in PgnParser.SplitGames(pgnText))
        {
            var fen = headers.GetValueOrDefault("FEN", "").Trim();
            var round = headers.GetValueOrDefault("Round", "").Trim();
            // Chessable-oid (von piratechess als [ChessableOid] mitgegeben) → eindeutige Zuordnung
            // importierte Linie ↔ Chessable-Linie für die Fortschritts-Overlays. null wenn nicht vorhanden.
            var oidHdr = headers.GetValueOrDefault("ChessableOid", "").Trim();
            var chessableOid = string.IsNullOrEmpty(oidHdr) ? null : PgnParser.Truncate(oidHdr, 32);
            // Skip-Regeln wie import_books.py
            if (string.IsNullOrEmpty(fen) || fen == "?") { invalid++; continue; }
            if (string.IsNullOrEmpty(round) || round == "?") { invalid++; continue; }

            var comment = PgnParser.ExtractFirstComment(moveText);
            var moveComments = PgnParser.ExtractMoveComments(moveText);
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
                // AUSNAHME: Info-Linien behalten wir (werden nicht abgefragt, nur durchgeklickt).
                if (PgnParser.IsStartPosition(fen) && !isInfoOnly) { invalid++; continue; }
                startPly = -1;
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
    /// Immer wird das Roh-PGN als <c>Book.SourcePgn</c> gespeichert und die Pipeline-Version
    /// hochgesetzt, damit das Buch künftig offline neu aufbereitbar ist.
    /// </summary>
    public async Task<BookImportItemDto> ImportFileAsync(string fileName, string pgnText, CancellationToken ct)
    {
        // Buch-/Kurs-Import: zug-lose Erklär-/Intro-Seiten als Info-Linien behalten (sequenziell durchklickbar).
        var (parsed, invalid) = ParsePgn(fileName, pgnText, keepCommentOnlyAsInfo: true);
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

        // Bestehende Linien laden: beim Upgrade als TRACKED Entities (zum Aktualisieren),
        // sonst nur die LineIds (leichtgewichtig, reines Dedup).
        var existing = upgrade
            ? await _db.BookPuzzles.Where(bp => bp.BookId == book.Id || bp.BookFileName == fileName)
                .ToDictionaryAsync(bp => bp.LineId, ct)
            : new Dictionary<string, BookPuzzle>();
        var existingLineIds = upgrade
            ? existing.Keys.ToHashSet()
            : await _db.BookPuzzles.Where(bp => bp.BookId == book.Id || bp.BookFileName == fileName)
                .Select(bp => bp.LineId).ToHashSetAsync(ct);

        var toAdd = new List<BookPuzzle>();
        var skipped = 0;
        var updated = 0;
        var seen = new HashSet<string>();

        foreach (var p in parsed)
        {
            if (!seen.Add(p.LineId)) { skipped++; continue; }      // Duplikat im selben Batch
            if (existingLineIds.Contains(p.LineId))
            {
                if (upgrade && existing.TryGetValue(p.LineId, out var bp))
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
                else { skipped++; }
                continue;
            }
            toAdd.Add(new BookPuzzle
            {
                LineId = p.LineId,
                BookFileName = PgnParser.Truncate(fileName, 200),
                BookId = book.Id,
                Round = p.Round,
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

        // Roh-PGN als Reprocessing-Quelle merken + Pipeline-Version hochsetzen.
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
}
