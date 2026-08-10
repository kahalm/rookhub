using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Kalkulations-Modus: der Nutzer bekommt reine Stellungen eines Buchs (FEN + optionaler
/// Kommentar) und legt zu jeder seinen EIGENEN Analysebaum an (<see cref="CalculationTree"/>).
/// Eine Lösung gibt es hier nicht — deshalb verlässt <see cref="BookPuzzle.Moves"/> in diesem
/// Pfad den Server nie vollständig: ausgeliefert wird höchstens der Vorlauf bis zum
/// Trainingsstart (<c>StartPly</c>), damit das Frontend die Aufgabenstellung nachstellen kann.
///
/// <para>Zugriff wird je Buch über <see cref="CourseAccess"/> erzwungen (kein Zugriff → 404 via
/// <see cref="KeyNotFoundException"/>). Der Modus ist bewusst nicht auf Bücher mit
/// <see cref="Book.IsCalculation"/> beschränkt — das Flag steuert nur, ob die Kursübersicht ihn
/// anbietet; per Direkt-Link darf jede zugängliche Buchstellung durchgerechnet werden.</para>
/// </summary>
public class CalculationService
{
    /// <summary>Obergrenze für einen gespeicherten Baum (Zeichen). Großzügig — ein sehr ausführlich
    /// kommentierter Baum mit hunderten Zügen bleibt deutlich darunter; schützt vor Müll-Uploads.</summary>
    public const int MaxTreeJsonLength = 262_144;

    /// <summary>Deckel für EINE Zeit-Übertragung (Sekunden). Der Client schickt Deltas seit dem
    /// letzten Speichern; ein hängender/schlafender Client soll die Summe nicht sprengen. Analog
    /// zum Pro-Stellung-Deckel des Solvers (<c>TrainingGoalService.PerChessableFlushCapSeconds</c>).</summary>
    public const int MaxSecondsPerFlush = 3_600;

    /// <summary>Absolute Obergrenze der aufsummierten Rechenzeit EINER Stellung (~277 h). Verhindert,
    /// dass sich viele einzeln erlaubte Deltas zu einer absurden Kapitelsumme addieren.</summary>
    public const int MaxSecondsSpent = 1_000_000;


    /// <summary>Feldlängen der Festlegung (siehe <c>CalculationTree.ChosenSan/ChosenUci</c>) — länger
    /// ist kein Zug, sondern Müll und wird als 400 abgewiesen statt still abgeschnitten.</summary>
    public const int MaxChosenSanLength = 20;
    public const int MaxChosenUciLength = 10;

    /// <summary>Länge der Idempotenz-Marke eines Zeit-Deltas (<c>CalculationTree.SecondsToken</c>).
    /// Der Inhalt ist für den Server opak — nur die Länge wird geprüft.</summary>
    public const int MaxSecondsTokenLength = 64;

    private readonly AppDbContext _db;

    public CalculationService(AppDbContext db) => _db = db;

    private async Task EnsureBookAccessAsync(int userId, int bookId, bool isAdmin, CancellationToken ct = default)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct))
            throw new KeyNotFoundException("Book not found.");
    }

    /// <summary>Kopf + leichte Stellungsliste eines Buchs (ohne FEN/Kommentar/Züge) inkl. Markierung,
    /// zu welchen Stellungen der Nutzer schon einen Baum gespeichert hat, der drei Trainings-Werte
    /// je Stellung (Festlegung/Zeit/Bewertungsstufe) und der SERVERSEITIG gerechneten Kapitelsummen
    /// (Punkte IMMER mit ihrem Maximum).
    /// Reihenfolge wie im Kurs (Round → Id).</summary>
    public async Task<CalcBookDto> GetBookAsync(int userId, int bookId, bool isAdmin, CancellationToken ct = default)
    {
        await EnsureBookAccessAsync(userId, bookId, isAdmin, ct);

        var book = await _db.Books.Where(b => b.Id == bookId)
            .Select(b => new { b.Id, b.DisplayName, b.IsCalculation })
            .FirstAsync(ct);

        var positions = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round.Length).ThenBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => new CalcPositionListItemDto
            {
                Id = bp.Id,
                Round = bp.Round,
                Title = bp.Title,
                Chapter = bp.Chapter,
            })
            .ToListAsync(ct);

        // Nur die Kennzahlen laden, NICHT das (bis zu 256 KB große) TreeJson — für die Liste zählt
        // bloß, OB ein Baum da ist.
        var rows = await _db.CalculationTrees
            .Where(t => t.UserId == userId && t.BookId == bookId)
            .Select(t => new
            {
                t.BookPuzzleId,
                HasTree = t.TreeJson != "",
                t.ChosenSan,
                t.ChosenUci,
                t.SecondsSpent,
                t.Grade,
            })
            .ToListAsync(ct);
        var byPuzzle = rows.ToDictionary(r => r.BookPuzzleId);

        foreach (var p in positions)
        {
            if (!byPuzzle.TryGetValue(p.Id, out var r)) continue;
            p.HasTree = r.HasTree;
            p.ChosenSan = r.ChosenSan;
            p.ChosenUci = r.ChosenUci;
            p.SecondsSpent = r.SecondsSpent;
            p.Grade = r.Grade;
            p.Points = r.Grade is int g ? CalculationGrades.PointsFor(g) : null;
        }

        var chapters = SummarizeChapters(positions);
        return new CalcBookDto
        {
            BookId = book.Id,
            DisplayName = book.DisplayName,
            IsCalculation = book.IsCalculation,
            Positions = positions,
            Chapters = chapters,
            Points = chapters.Sum(c => c.Points),
            MaxPoints = chapters.Sum(c => c.MaxPoints),
            SecondsSum = chapters.Sum(c => c.SecondsSum),
        };
    }

    /// <summary>
    /// Kapitelsummen über die VOLLSTÄNDIGE Stellungsliste — bewusst auf dem Server, damit eine im
    /// Frontend gefilterte/gekürzte Anzeige die Summen nicht verfälscht. Reihenfolge = erstes
    /// Auftreten des Kapitels in der Liste; leerer/whitespace-Kapitelname zählt als „ohne Kapitel"
    /// (<c>null</c>), wie überall sonst in der Kursverwaltung.
    /// </summary>
    private static List<CalcChapterSummaryDto> SummarizeChapters(List<CalcPositionListItemDto> positions)
    {
        var order = new List<string?>();
        var byChapter = new Dictionary<string, CalcChapterSummaryDto>(StringComparer.Ordinal);
        const string NoChapterKey = "\0";       // eigener Schlüssel für „ohne Kapitel"

        foreach (var p in positions)
        {
            var chapter = string.IsNullOrWhiteSpace(p.Chapter) ? null : p.Chapter;
            var key = chapter ?? NoChapterKey;
            if (!byChapter.TryGetValue(key, out var sum))
            {
                sum = new CalcChapterSummaryDto { Chapter = chapter };
                byChapter[key] = sum;
                order.Add(chapter);
            }
            sum.PositionCount++;
            if (p.HasTree) sum.TreeCount++;
            if (!string.IsNullOrEmpty(p.ChosenSan)) sum.ChosenCount++;
            if (p.Grade is int grade) { sum.RatedCount++; sum.Points += CalculationGrades.PointsFor(grade); }
            sum.SecondsSum += p.SecondsSpent;
        }

        // Das Maximum hängt an ALLEN Stellungen des Kapitels, nicht an den bewerteten: „14 / 24"
        // soll sagen, wie viel im Kapitel überhaupt zu holen ist.
        foreach (var sum in byChapter.Values) sum.MaxPoints = CalculationGrades.MaxPointsFor(sum.PositionCount);

        return order.Select(c => byChapter[c ?? NoChapterKey]).ToList();
    }

    /// <summary>
    /// ANONYMER Lesezugriff: Kopf + vollständige Stellungen eines öffentlich freigegebenen Buchs.
    /// Die EINZIGE Öffnung des Kalkulations-Modus — rein lesend, ohne jeden Nutzer-Kontext.
    ///
    /// <para><b>Gate</b>: <see cref="Book.IsPublic"/> — „ausdrücklich öffentlich freigegeben", und
    /// zwar exakt dieselbe Bedingung wie die Slug-Auflösung (<see cref="CourseService.ResolvePublicSlugAsync"/>),
    /// damit Einstieg (<c>/{slug}</c>) und Inhalt nicht auseinanderlaufen. Ein NICHT freigegebenes
    /// Buch ist hier nicht existent (<see cref="KeyNotFoundException"/> → 404, kein Existenz-Orakel).</para>
    ///
    /// <para>Bewusst NICHT <see cref="BookAccess.PubliclyExposed"/>: die Pool-Flags
    /// (<see cref="Book.ForDaily"/>/<see cref="Book.ForRandom"/>/<see cref="Book.ForBlind"/>) öffnen
    /// EINZELNE Puzzles und Zufallsziehungen, nicht den strukturierten Kurs (siehe
    /// <see cref="BookAccess"/>). Ein persönlicher Import, den ein Admin in den Tagespuzzle-Pool
    /// gelegt hat, würde damit sonst anonym als vollständiger, geordneter Kurs herausgehen.</para>
    ///
    /// <para><b>Keine Trainings-Werte</b>: Baum, Zeit, Stufe und Festlegung gibt es für anonyme
    /// Aufrufer nicht — die liegen bei ihnen im Browser. Es wird deshalb gar keine
    /// <see cref="CalculationTree"/>-Zeile gelesen, und das Antwort-DTO
    /// (<see cref="CalcPublicBookDto"/>) hat für sie keine Felder.</para>
    ///
    /// <para><b>Keine Lösung</b>: wie im eingeloggten Pfad verlässt <see cref="BookPuzzle.Moves"/> den
    /// Server nicht — höchstens der Vorlauf bis zum Trainingsstart (<see cref="SetupMoves"/>).</para>
    /// </summary>
    public async Task<CalcPublicBookDto> GetPublicBookAsync(int bookId, CancellationToken ct = default)
    {
        var book = await _db.Books
            .Where(b => b.Id == bookId && b.IsPublic)
            .Select(b => new { b.Id, b.DisplayName, b.IsCalculation })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Book not found.");

        // Moves/StartPly werden NUR geladen, um daraus den Vorlauf zu schneiden — sie landen nie im DTO.
        var rows = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round.Length).ThenBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => new { bp.Id, bp.Round, bp.Title, bp.Chapter, bp.Fen, bp.Comment, bp.Moves, bp.StartPly })
            .ToListAsync(ct);

        return new CalcPublicBookDto
        {
            BookId = book.Id,
            DisplayName = book.DisplayName,
            IsCalculation = book.IsCalculation,
            Positions = rows.Select(r => new CalcPublicPositionDto
            {
                Id = r.Id,
                Round = r.Round,
                Title = r.Title,
                Chapter = r.Chapter,
                Fen = r.Fen,
                SetupMoves = SetupMoves(r.Moves, r.StartPly),
                Comment = r.Comment,
            }).ToList(),
        };
    }

    /// <summary>Eine Stellung inkl. eigenem Baum. Liefert NIE die Lösungszüge — nur den Vorlauf bis
    /// zum Trainingsstart (<c>StartPly</c>), sonst gar keine Züge.</summary>
    public async Task<CalcPositionDto> GetPositionAsync(int userId, int bookPuzzleId, bool isAdmin,
        CancellationToken ct = default)
    {
        var puzzle = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == bookPuzzleId, ct)
            ?? throw new KeyNotFoundException("Position not found.");
        var bookId = puzzle.BookId ?? throw new KeyNotFoundException("Position not found.");
        await EnsureBookAccessAsync(userId, bookId, isAdmin, ct);

        var tree = await _db.CalculationTrees
            .FirstOrDefaultAsync(t => t.UserId == userId && t.BookPuzzleId == bookPuzzleId, ct);

        return new CalcPositionDto
        {
            Id = puzzle.Id,
            BookId = bookId,
            Round = puzzle.Round,
            Title = puzzle.Title,
            Chapter = puzzle.Chapter,
            Fen = puzzle.Fen,
            SetupMoves = SetupMoves(puzzle),
            Comment = puzzle.Comment,
            // Leeres TreeJson = Zeile existiert nur wegen Zeit/Festlegung/Bewertung ⇒ „kein Baum".
            TreeJson = string.IsNullOrEmpty(tree?.TreeJson) ? null : tree!.TreeJson,
            TreeUpdatedAt = string.IsNullOrEmpty(tree?.TreeJson) ? null : tree!.UpdatedAt,
            ChosenSan = tree?.ChosenSan,
            ChosenUci = tree?.ChosenUci,
            SecondsSpent = tree?.SecondsSpent ?? 0,
            Grade = tree?.Grade,
            Points = tree?.Grade is int grade ? CalculationGrades.PointsFor(grade) : null,
        };
    }

    /// <summary>
    /// Züge von der Header-FEN bis zur Aufgabenstellung. Bei <c>StartPly &gt;= 0</c> (Trainingsstart
    /// mitten in der Partie) sind das die Halbzüge <c>0..StartPly</c> — also ausdrücklich NUR der
    /// Vorlauf, nie der ab <c>StartPly+1</c> beginnende Lösungsweg. Sonst leer.
    /// </summary>
    internal static string SetupMoves(BookPuzzle puzzle) => SetupMoves(puzzle.Moves, puzzle.StartPly);

    /// <inheritdoc cref="SetupMoves(BookPuzzle)"/>
    /// <remarks>Überladung für Projektionen, die nur die beiden Felder laden (statt die ganze Zeile).</remarks>
    internal static string SetupMoves(string? moves, int startPly)
    {
        if (startPly < 0 || string.IsNullOrWhiteSpace(moves)) return string.Empty;
        var parts = moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Take(Math.Min(startPly + 1, parts.Length)));
    }

    /// <summary>Speichert (Upsert) den Analysebaum des Users zu einer Stellung; die drei
    /// Trainings-Werte dürfen im selben Aufruf mitkommen (siehe <see cref="ICalcMetaInput"/>).</summary>
    /// <exception cref="ArgumentException">Baum ist kein gültiges JSON oder zu groß, oder die
    /// Festlegung ist keine plausible Zugangabe (→ 400).</exception>
    public async Task<CalcPositionStateDto> SaveTreeAsync(int userId, int bookPuzzleId, SaveCalcTreeDto dto,
        bool isAdmin, CancellationToken ct = default)
    {
        var json = dto.TreeJson ?? string.Empty;
        if (json.Length > MaxTreeJsonLength)
            throw new ArgumentException($"Tree too large (max {MaxTreeJsonLength} characters).");
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Tree must not be empty.");
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ArgumentException("Tree is not valid JSON."); }
        ValidateMeta(dto);

        var (tree, _) = await UpsertAsync(userId, bookPuzzleId, isAdmin, createIfMissing: true, ct);
        var now = DateTime.UtcNow;
        tree!.TreeJson = json;
        tree.UpdatedAt = now;
        ApplyMeta(tree, dto);
        await _db.SaveChangesAsync(ct);
        return State(tree);
    }

    /// <summary>
    /// Ändert NUR die drei Trainings-Werte einer Stellung (Festlegung, Rechenzeit, Bewertungsstufe), ohne
    /// den Baum erneut zu übertragen — man kann sich festlegen oder sich bewerten, ohne am Baum
    /// etwas zu ändern, und Zeit fällt auch an, bevor der erste Zug im Baum steht. Legt die Zeile
    /// bei Bedarf mit LEEREM Baum an (zählt dann nirgends als „bearbeitet").
    /// </summary>
    /// <exception cref="ArgumentException">Unplausible Zugangabe (→ 400).</exception>
    public async Task<CalcPositionStateDto> PatchMetaAsync(int userId, int bookPuzzleId, PatchCalcMetaDto dto,
        bool isAdmin, CancellationToken ct = default)
    {
        ValidateMeta(dto);
        // Ohne bestehende Zeile wird nur dann eine angelegt, wenn der Aufruf wirklich etwas ändert
        // (ein leerer PATCH soll keine Karteileichen erzeugen).
        var (tree, existed) = await UpsertAsync(userId, bookPuzzleId, isAdmin, createIfMissing: true, ct);
        var changed = ApplyMeta(tree!, dto);
        if (!existed && !changed)
        {
            _db.CalculationTrees.Remove(tree!);       // frisch angelegt, aber nichts drin: verwerfen
            return State(tree!);
        }
        if (changed) tree!.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return State(tree!);
    }

    /// <summary>Holt die Zeile des Users zu einer Stellung (mit Zugriffsprüfung) oder legt sie an.
    /// Rückgabe: die Zeile + ob sie schon existierte.</summary>
    private async Task<(CalculationTree? Tree, bool Existed)> UpsertAsync(int userId, int bookPuzzleId,
        bool isAdmin, bool createIfMissing, CancellationToken ct)
    {
        var puzzle = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == bookPuzzleId, ct)
            ?? throw new KeyNotFoundException("Position not found.");
        var bookId = puzzle.BookId ?? throw new KeyNotFoundException("Position not found.");
        await EnsureBookAccessAsync(userId, bookId, isAdmin, ct);

        var tree = await _db.CalculationTrees
            .FirstOrDefaultAsync(t => t.UserId == userId && t.BookPuzzleId == bookPuzzleId, ct);
        if (tree != null)
        {
            // Altzeilen aus einer Zeit vor gesetztem BookId heilen (defensiv, kostet nichts).
            tree.BookId = bookId;
            return (tree, true);
        }
        if (!createIfMissing) return (null, false);

        var now = DateTime.UtcNow;
        tree = new CalculationTree
        {
            UserId = userId,
            BookId = bookId,
            BookPuzzleId = bookPuzzleId,
            TreeJson = string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.CalculationTrees.Add(tree);
        return (tree, false);
    }

    /// <summary>Prüft Bewertungsstufe + Festlegung. Beides wird bei Unsinn als 400 abgewiesen statt
    /// still zurechtgebogen: eine unbekannte Stufe ist ein Client-Fehler und ausdrücklich nicht
    /// „nicht gelöst" (Stufe 0), abgeschnittenes SAN wäre ein falscher Zug in den Daten.</summary>
    private static void ValidateMeta(ICalcMetaInput dto)
    {
        if (!dto.ClearGrade && dto.Grade is int grade && !CalculationGrades.IsValid(grade))
            throw new ArgumentException(
                $"Unknown grade {grade} (expected {CalculationGrades.Min}..{CalculationGrades.Max}).");
        if (dto.ChosenSan is { Length: > MaxChosenSanLength })
            throw new ArgumentException($"Chosen move (SAN) too long (max {MaxChosenSanLength} characters).");
        if (dto.ChosenUci is { Length: > MaxChosenUciLength })
            throw new ArgumentException($"Chosen move (UCI) too long (max {MaxChosenUciLength} characters).");
        if (dto.SecondsToken is { Length: > MaxSecondsTokenLength })
            throw new ArgumentException($"Seconds token too long (max {MaxSecondsTokenLength} characters).");
    }

    /// <summary>
    /// Überträgt die drei Trainings-Werte auf die Zeile. Weggelassene Felder bleiben unverändert.
    /// <list type="bullet">
    /// <item>Zeit wird AUFADDIERT (Delta), je Übertragung auf <see cref="MaxSecondsPerFlush"/> und
    /// in Summe auf <see cref="MaxSecondsSpent"/> gedeckelt; Negatives zählt als 0. Trägt der Patch
    /// eine Idempotenz-Marke (<c>SecondsToken</c>), zählt nur, was unter dieser Marke noch NICHT
    /// verbucht war — ein Retry addiert die Zeit also kein zweites Mal (siehe
    /// <see cref="ApplySeconds"/>).</item>
    /// <item>Die Bewertungsstufe wird gesetzt, wie sie kommt (vorher validiert, siehe
    /// <see cref="ValidateMeta"/>); <c>ClearGrade</c> setzt zurück auf <c>null</c> („noch nicht
    /// bewertet" ≠ Stufe 0 „nicht gelöst").</item>
    /// <item>Die Festlegung ist ein TOGGLE über das SAN: anderer Zug ⇒ verschieben, derselbe Zug ⇒
    /// zurücknehmen (das SAN identifiziert den Zug in einer Stellung eindeutig).</item>
    /// </list>
    /// </summary>
    /// <returns><c>true</c>, wenn sich tatsächlich etwas geändert hat.</returns>
    private static bool ApplyMeta(CalculationTree tree, ICalcMetaInput dto)
    {
        var changed = ApplySeconds(tree, dto);

        if (dto.ClearGrade)
        {
            if (tree.Grade != null) { tree.Grade = null; changed = true; }
        }
        else if (dto.Grade is int grade)
        {
            if (tree.Grade != grade) { tree.Grade = grade; changed = true; }
        }

        if (dto.ClearChoice)
        {
            if (tree.ChosenSan != null || tree.ChosenUci != null)
            {
                tree.ChosenSan = null;
                tree.ChosenUci = null;
                changed = true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(dto.ChosenSan))
        {
            // SETZEN, nicht toggeln: genau EINE Festlegung je Stellung ⇒ ein anderes SAN verschiebt
            // sie, dasselbe SAN ist ein No-op. Das Zurücknehmen macht der Client selbst (er kennt
            // den Zustand) und schickt dafür ClearChoice.
            //
            // FRÜHER togglete der Server bei gleichem SAN auf null — das war NICHT idempotent: kam
            // die Anfrage an und ging nur die ANTWORT verloren, löschte der (identische) Retry die
            // gerade gesetzte Festlegung wieder. Anders als die Zeit (SecondsToken) hat die Wahl
            // keine Marke; ihre Idempotenz kommt allein daraus, dass SET wiederholbar ist.
            // (Review-Fund 2026-08-09.)
            var san = dto.ChosenSan.Trim();
            var uci = string.IsNullOrWhiteSpace(dto.ChosenUci) ? null : dto.ChosenUci.Trim();
            if (!string.Equals(tree.ChosenSan, san, StringComparison.Ordinal)
                || !string.Equals(tree.ChosenUci, uci, StringComparison.Ordinal))
            {
                tree.ChosenSan = san;
                tree.ChosenUci = uci;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Trägt den ZEIT-Anteil eines Patches nach — der einzige Teil, der ADDIERT statt SETZT und
    /// deshalb als einziger wiederholungsfest gemacht werden muss.
    /// <para><b>Warum überhaupt:</b> „at least once". Kam die Anfrage beim Server an und ging nur die
    /// ANTWORT verloren (Timeout, Verbindungsabbruch, 502 vom Proxy), wiederholt der Client sie —
    /// ohne Marke würde die Zeit ein zweites Mal addiert und wäre still und dauerhaft zu groß.</para>
    /// <para><b>Wie:</b> Der Client vergibt je gemessenem Delta eine eindeutige Marke und schickt sie
    /// mit; die Zeile merkt sich die zuletzt verbuchte Marke samt der darunter angerechneten
    /// Sekunden. Kommt dieselbe Marke wieder, zählt nur, was darunter noch nicht angerechnet war
    /// (identischer Retry ⇒ 0; ein beim Wiedereinreihen um neue Messungen GEWACHSENER Patch behält
    /// seine Marke ⇒ es zählt die Differenz). Marke fehlt = Alt-Verhalten, wird addiert.</para>
    /// </summary>
    /// <returns><c>true</c>, wenn Sekunden tatsächlich dazugekommen sind.</returns>
    private static bool ApplySeconds(CalculationTree tree, ICalcMetaInput dto)
    {
        if (dto.AddSeconds is not int add) return false;

        var token = string.IsNullOrWhiteSpace(dto.SecondsToken) ? null : dto.SecondsToken.Trim();
        var requested = Math.Max(0, add);
        var already = token != null && string.Equals(tree.SecondsToken, token, StringComparison.Ordinal)
            ? tree.SecondsTokenApplied
            : 0;
        var delta = Math.Clamp(requested - already, 0, MaxSecondsPerFlush);

        var changed = false;
        if (delta > 0)
        {
            var total = (int)Math.Min((long)tree.SecondsSpent + delta, MaxSecondsSpent);
            if (total != tree.SecondsSpent) { tree.SecondsSpent = total; changed = true; }
        }
        // Die Marke NUR merken, wenn wirklich Zeit im Spiel war: sonst würde ein wirkungsloser
        // Patch (0 s) eine Karteileiche anlegen.
        //
        // Gemerkt wird, was TATSÄCHLICH angerechnet wurde (already + delta), NICHT das angeforderte
        // `requested`. Nur so bleibt ein über MaxSecondsPerFlush hinausgewachsener Patch über
        // mehrere Retries hinweg NACHHOLBAR: sonst setzte ein einziger Über-Cap-Send
        // SecondsTokenApplied = requested, und der Rest (requested − cap) wäre für immer
        // unterschlagen, weil der nächste Retry `already == requested` sähe (Review-Fund
        // 2026-08-09). Im Normalfall (requested ≤ cap) ist already + delta == requested — unverändert.
        var applied = already + delta;
        if (token != null && requested > 0 &&
            (!string.Equals(tree.SecondsToken, token, StringComparison.Ordinal) ||
             tree.SecondsTokenApplied != applied))
        {
            tree.SecondsToken = token;
            tree.SecondsTokenApplied = applied;
            changed = true;
        }
        return changed;
    }

    private static CalcPositionStateDto State(CalculationTree tree) => new()
    {
        BookPuzzleId = tree.BookPuzzleId,
        UpdatedAt = tree.UpdatedAt,
        HasTree = !string.IsNullOrEmpty(tree.TreeJson),
        ChosenSan = tree.ChosenSan,
        ChosenUci = tree.ChosenUci,
        SecondsSpent = tree.SecondsSpent,
        Grade = tree.Grade,
        Points = tree.Grade is int grade ? CalculationGrades.PointsFor(grade) : null,
    };

    /// <summary>
    /// Löscht den eigenen Baum zu einer Stellung. Idempotent (kein Baum → einfach nichts).
    /// <para>Trägt die Zeile noch Rechenzeit, Festlegung oder Bewertung, bleibt sie mit LEEREM Baum
    /// stehen: „Baum verwerfen" heißt Analyse neu anfangen, nicht die schon investierte Zeit und
    /// die eigene Bewertung stillschweigend wegwerfen.</para>
    /// </summary>
    public async Task DeleteTreeAsync(int userId, int bookPuzzleId, bool isAdmin, CancellationToken ct = default)
    {
        var tree = await _db.CalculationTrees
            .FirstOrDefaultAsync(t => t.UserId == userId && t.BookPuzzleId == bookPuzzleId, ct);
        if (tree == null) return;
        await EnsureBookAccessAsync(userId, tree.BookId, isAdmin, ct);

        var keepMeta = tree.SecondsSpent > 0 || tree.Grade != null || tree.ChosenSan != null;
        if (keepMeta)
        {
            if (tree.TreeJson.Length == 0) return;     // nichts zu verwerfen
            tree.TreeJson = string.Empty;
            tree.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.CalculationTrees.Remove(tree);
        }
        await _db.SaveChangesAsync(ct);
    }
}
