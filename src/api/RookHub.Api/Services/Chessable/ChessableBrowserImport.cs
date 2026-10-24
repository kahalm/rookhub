using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using Serilog.Context;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Browser-Import aus der RepCheck-Erweiterung: ganzer Kurs auf einmal (<see cref="ImportPgnDirectAsync"/>) oder
/// laufend Kapitel für Kapitel (Datensatz anlegen, Chunk anhängen, abschließen/abbrechen) — immer ohne
/// Chessable-Abruf; dazu die Schranke für Linien aus dem geteilten Cache. Teil von <see cref="ChessableImportService"/>
/// (Zerlegung Z3), von der Fassade gebaut, loggt unter deren Quelle.
/// </summary>
internal sealed class ChessableBrowserImport
{
    private readonly AppDbContext _db;
    private readonly ChessableTargetWriter _writer;
    private readonly ChessableLiveAppend _liveAppend;
    private readonly ChessableImportOutcome _outcome;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableBrowserImport(AppDbContext db, ChessableTargetWriter writer, ChessableLiveAppend liveAppend,
        ChessableImportOutcome outcome, ILogger<ChessableImportService> logger)
    {
        _db = db;
        _writer = writer;
        _liveAppend = liveAppend;
        _outcome = outcome;
        _logger = logger;
    }

    /// <summary>
    /// Steht <paramref name="bid"/> in der von Chessable bestätigten Kursliste des Nutzers
    /// (<see cref="ChessableCredential.CachedCoursesJson"/> — geschrieben von „Testen“, der Kursliste und dem
    /// Nacht-Refresh, immer mit seinem eigenen Bearer)? Schranke für alles, was Inhalte aus dem GETEILTEN
    /// Linien-Cache herausgibt: den Extension-Ingest mit Linien ohne Inhalt und die cached-lines-Auskunft. Ohne sie
    /// holte sich jedes Konto die Linien fremder, bezahlter Kurse über ihre oid. Bewusst ohne Live-Abruf: die
    /// Extension-Wege laufen auch mit <c>Chessable:Enabled=false</c>, und ein Chessable-Abruf je Chunk wäre genau das,
    /// was der Browser-Import vermeiden soll.
    /// </summary>
    public async Task<bool> HasVerifiedCourseAsync(int userId, string bid, CancellationToken ct = default)
    {
        var json = await _db.ChessableCredentials.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.CachedCoursesJson)
            .FirstOrDefaultAsync(ct);
        return ChessableReviewLineService.OwnedBids(json).Contains(bid);
    }

    /// <summary>
    /// Importiert ein bereits FERTIGES Kurs-PGN (vom Browser über die RepCheck-Extension geholt und von
    /// piratechess fetch-frei geparst) direkt als Repertoire/Buch — OHNE piratechess-Fetch, Queue oder VPN.
    /// Nutzt exakt dieselben Import-Primitive (<see cref="ChessableTargetWriter"/>)
    /// wie der Server-Fetch-Pfad und legt einen abgeschlossenen <see cref="ChessableImport"/>-Satz an
    /// (Verlauf/Anzeige, Benachrichtigung). Idempotent bei erneutem Senden desselben Kurses: Buch dedupliziert
    /// per LineId, Repertoire wird IN-PLACE ersetzt (Trainings-Fortschritt bleibt), wenn schon eins aus diesem
    /// Kurs (bid) existiert.
    /// </summary>
    public async Task<ChessableImport> ImportPgnDirectAsync(
        int userId, string bid, string pgn, string courseName, string target, int lineCount, CancellationToken ct = default)
    {
        target = ChessableImportTargets.Normalize(target);
        var name = ChessableImportTargets.DisplayName(courseName, bid);
        var import = new ChessableImport
        {
            UserId = userId,
            Bid = bid,
            CourseName = name,
            Target = target,
            Status = ChessableImportStatus.Running,
            Phase = ChessableImportPhase.Importing,
            LineCount = lineCount,
            FullyCached = true,   // Browser lieferte die Daten → kein Download nötig
            FromBrowser = true,   // nie von den Server-Lanes abrufen
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
        };

        // Idempotenz Repertoire: erneutes „Über meinen Browser holen" desselben Kurses landet im VORHANDENEN
        // Repertoire (Id/Fortschritt bleiben) statt ein Duplikat anzulegen — und hängt dort nur an. Match wie
        // bei BumpSiblingRepertoiresAsync über ChessableCourseId ODER den stabilen Dateinamen (das PGN des
        // Parsers trägt kein [Site]-Tag → ChessableCourseId wird beim Neuanlegen unten explizit gesetzt).
        if (target == "repertoire")
        {
            var fileName = ChessableImportTargets.RepertoireFileName(bid);
            var existing = await _db.Repertoires
                .Where(r => r.UserId == userId && (r.ChessableCourseId == bid || r.Files.Any(f => f.FileName == fileName)))
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(ct);
            if (existing is int repId) import.TargetRepertoireId = repId;
        }

        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync(ct);

        // Dieser Satz ist ab sofort „inflight" (running/importing), wird aber INLINE im Request
        // getrieben — nicht vom Drain. Ohne Registrierung hielte ihn der Watchdog für eine
        // verwaiste Zombie-Zeile, setzte ihn nach der Karenz auf „queued" zurück, und die
        // Fast-Lane liefe parallel zum noch laufenden Direktimport (doppelter Import, im
        // schlimmsten Fall sogar ein echter Chessable-Abruf für einen reinen Browser-Import).
        using var tracked = ChessableImportProgress.TrackInflight(import.Id);

        try
        {
            if (target == "book")
                await _writer.ImportAsBookAsync(import, pgn, name, ct);
            else if (import.TargetRepertoireId is not null)
            {
                // Vorhandenes Repertoire: ANHÄNGEN, nie ersetzen. Der Browser liefert oft nur einen Teil des Kurses
                // („Mitschnitt importieren" = die gerade durchgeklickten Kapitel), und das Ersetzen löschte alle übrigen
                // Partien — auf Prod stand Repertoire 232 am 2026-08-12 dreimal hintereinander auf EINER Linie. Derselbe
                // Weg wie der Live-Append: bekannte oids übersprungen, oid-lose Bestandslinien nachgetragen, Bereinigung
                // inline. Ersetzt wird nur noch beim serverseitigen Neuabruf (RunAsync), der immer den ganzen Kurs liefert.
                var (added, repId, _) = await _liveAppend.AppendAsync(userId, bid, pgn, name, target, ct, partial: false);
                import.ResultId = repId;
                import.Imported = added;
                import.Skipped = Math.Max(0, lineCount - added);
                import.Invalid = 0;
            }
            else
                await _writer.ImportAsRepertoireAsync(import, pgn, name, ct);

            // Kurs-Zuordnung (bid) explizit setzen, falls neu angelegt und noch leer (das Parser-PGN trägt
            // kein [Site]-Tag) → macht künftige Browser-Importe desselben Kurses idempotent (in-place).
            if (target == "repertoire" && import.ResultId is int newRepId)
            {
                var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == newRepId, ct);
                if (rep is not null && string.IsNullOrEmpty(rep.ChessableCourseId))
                {
                    rep.ChessableCourseId = bid;
                    await _db.SaveChangesAsync(ct);
                }
            }

            import.Status = ChessableImportStatus.Completed;
            import.Phase = ChessableImportPhase.Done;
            import.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
                _logger.LogInformation(
                    "Browser-Import {Id} fertig: {Target} '{Name}' (bid {Bid}), imported={Imported}",
                    import.Id, import.Target, name, import.Bid, import.Imported);

            await _outcome.NotifyCompletedAsync(import, name, "0s", "0s");
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
                _logger.LogWarning(ex, "Browser-Import {Id} fehlgeschlagen (bid {Bid})", import.Id, import.Bid);
            await _outcome.FailAsync(import, ex.Message);
            throw;
        }
        return import;
    }

    // ===== Laufender Browser-Import (Kapitel für Kapitel) =====
    // Der Browser streamt einen Kurs in Chunks; jeder wird SOFORT geparst und angehängt, statt bis zum
    // Schluss im Speicher zu liegen (siehe ChessableIngestSessionStore). Drei Schritte, damit die
    // Sitzung trotzdem EINEN Import-Datensatz und EINE Benachrichtigung ergibt.

    /// <summary>
    /// Schritt 1: Import-Datensatz der Sitzung anlegen. Der zurückgegebene Token hält ihn als „lokal
    /// getrieben" markiert — er gehört in die Sitzung, nicht in den Request, sonst hält der Watchdog den
    /// Import zwischen zwei Chunks für verwaist und reiht ihn neu ein.
    /// </summary>
    public async Task<(ChessableImport Import, IDisposable Inflight)> StartBrowserImportAsync(
        int userId, string bid, string? courseName, string target, CancellationToken ct = default)
    {
        target = ChessableImportTargets.Normalize(target);
        var import = new ChessableImport
        {
            UserId = userId,
            Bid = bid,
            CourseName = ChessableImportTargets.DisplayName(courseName, bid),
            Target = target,
            Status = ChessableImportStatus.Running,
            Phase = ChessableImportPhase.Importing,
            FullyCached = true,   // Browser liefert die Daten → kein Chessable-Abruf
            FromBrowser = true,   // nie von den Server-Lanes abrufen
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
        };
        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync(ct);
        return (import, ChessableImportProgress.TrackInflight(import.Id));
    }

    /// <summary>
    /// Kapitel-Versatz für den ERSTEN Chunk einer Sitzung, die der Server nicht kennt (Codereview 2026-09-29, A3-006).
    /// Der Versatz lebt nur im Arbeitsspeicher (<see cref="ChessableIngestSessionStore"/>). Startet die API mitten im
    /// kapitelweisen Import neu (Watchtower nachts, Deploy), legt der nächste Chunk derselben Sitzung eine NEUE mit
    /// Versatz 0 an: er trägt wieder „002.xxx" und trifft auf die LineIds der schon importierten Kapitel — unbekannte oid,
    /// kein Teil-Import, also fiel jede dieser Linien auf „übersprungen", und die restlichen Kapitel fehlten still.
    /// <para>Liefert die höchste Kapitelnummer des Buchs, wenn der Chunk mit Versatz 0 auf belegte LineIds träfe UND
    /// keine seiner oids im Buch vorkommt: dann ist er keine Wiederholung schon importierter Linien, sondern neuer
    /// Inhalt, und mit Versatz 0 ginge er verloren. Sonst 0 (bisheriges Verhalten) — ein erneutes Holen desselben Kurses
    /// trägt bekannte oids und behält seine Nummern. Ohne oids lässt sich das nicht unterscheiden: dann auch 0. Belegt
    /// zählt nur eine LineId mit oid — ein Alt-Buch ohne oids behält Versatz 0, damit der oid-Nachtrag greift.</para>
    /// </summary>
    public async Task<int> ResumeChapterOffsetAsync(int userId, string bid, string chunkPgn, CancellationToken ct = default)
    {
        var fileName = ChessableImportTargets.BookFileName(userId, bid);
        var parsed = PgnImportService.ParsePgn(fileName, chunkPgn, keepCommentOnlyAsInfo: true).Puzzles;
        var oids = parsed.Where(p => !string.IsNullOrEmpty(p.ChessableOid)).Select(p => p.ChessableOid!)
            .Distinct(StringComparer.Ordinal).ToList();
        if (oids.Count == 0) return 0;
        if (await _db.BookPuzzles.AnyAsync(bp => bp.BookFileName == fileName && bp.ChessableOid != null
                && oids.Contains(bp.ChessableOid), ct))
            return 0;
        // Nur eine Kollision mit Linien, die eine oid TRAGEN, zeigt einen Neustart mitten im Import an (die Kapitel dieser
        // Sitzung sind mit oid gespeichert). Trifft der Chunk nur auf oid-lose Altlinien (Import vor piratechess v1.29.0),
        // ist das erneute Holen eines Alt-Buchs: der oid-Nachtrag (gleiche LineId + Züge + StartPly, v0.476.1) braucht
        // Versatz 0 — hinter dem Buch stünde der ganze Kurs doppelt da.
        var lineIds = parsed.Select(p => p.LineId).Distinct(StringComparer.Ordinal).ToList();
        if (!await _db.BookPuzzles.AnyAsync(bp => bp.BookFileName == fileName && bp.ChessableOid != null
                && lineIds.Contains(bp.LineId), ct))
            return 0;

        var rounds = await _db.BookPuzzles.Where(bp => bp.BookFileName == fileName).Select(bp => bp.Round).ToListAsync(ct);
        var maxChapter = rounds.Count == 0 ? 0 : rounds.Max(ChessableRoundOffset.ChapterOf);
        if (maxChapter > 0)
            using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
                _logger.LogInformation(
                    "Browser-Import: unbekannte Sitzung trifft auf belegte Kapitelnummern — setzt hinter Kapitel {MaxChapter} fort (User {UserId}, bid {Bid})",
                    maxChapter, userId, bid);
        return maxChapter;
    }

    /// <summary>
    /// Schritt 2: einen Kapitel-Chunk anhängen. Geht denselben Weg wie der Live-Append (dedupliziert,
    /// je (User, bid) serialisiert) und schreibt die Zähler des Import-Datensatzes fort, damit die
    /// Warteschlangen-Anzeige mitwächst.
    /// </summary>
    public async Task<ChessableImportService.LiveAppendResult> AppendBrowserChunkAsync(
        int importId, string pgn, int linesInChunk, bool partial = false, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct)
            ?? throw new InvalidOperationException($"Chessable import {importId} not found.");

        var res = await _liveAppend.AppendAsync(import.UserId, import.Bid, pgn, import.CourseName, import.Target, ct, partial);

        // Der Live-Append setzt den Anzeigenamen eines Buchs nur, wenn er LEER ist — er ist fuer vorhandene
        // Buecher gedacht, deren Namen der Nutzer gepflegt haben kann. PgnImportService legt ein neues Buch aber
        // schon mit dem Dateinamen als Anzeigename an (chessable-u5-55720), und genau so hiess das Buch nach dem
        // ersten laufenden Import (2026-09-19). Ein Buch, das noch seinen Dateinamen traegt, bekommt hier den
        // Kursnamen; ein vom Nutzer umbenanntes bleibt unangetastet.
        if (import.Target == "book" && res.ResultId is int bookId)
        {
            var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct);
            if (book is not null && ChessableImportTargets.IsFileNameDerived(book.DisplayName, book.FileName))
                book.DisplayName = import.CourseName;
        }

        import.Imported += res.Imported;
        import.Skipped += Math.Max(0, linesInChunk - res.Imported);
        import.LineCount += linesInChunk;
        import.LinesDone = import.LineCount;
        import.ChaptersDone += 1;
        import.ChaptersTotal = Math.Max(import.ChaptersTotal, import.ChaptersDone);
        if (res.ResultId is not null) import.ResultId = res.ResultId;
        await _db.SaveChangesAsync(ct);
        return res;
    }

    /// <summary>
    /// Schritt 3: Sitzung abschließen — Status, Kurs-Zuordnung, Log, Benachrichtigung. <c>null</c>, wenn
    /// der Datensatz nicht (mehr) existiert.
    /// </summary>
    public async Task<ChessableImport?> FinishBrowserImportAsync(int importId, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null) return null;

        // Kurs-Zuordnung (bid) nachtragen: das Parser-PGN trägt kein [Site]-Tag, und ohne sie fände ein
        // späterer Browser-Import das Repertoire nicht wieder und legte ein zweites an.
        if (import.Target == "repertoire" && import.ResultId is int repId)
        {
            var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == repId, ct);
            if (rep is not null && string.IsNullOrEmpty(rep.ChessableCourseId)) rep.ChessableCourseId = import.Bid;
        }

        import.Status = ChessableImportStatus.Completed;
        import.Phase = ChessableImportPhase.Done;
        import.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        using (LogContext.PushProperty("LogTags", "import,chessable,browser"))
            _logger.LogInformation(
                "Browser-Import {Id} fertig (laufend): {Target} '{Name}' (bid {Bid}), Kapitel={Chapters}, imported={Imported}",
                import.Id, import.Target, import.CourseName, import.Bid, import.ChaptersDone, import.Imported);

        await _outcome.NotifyCompletedAsync(import, import.CourseName, "0s", "0s");
        return import;
    }

    /// <summary>Bricht den Import-Datensatz einer Sitzung ab. Das bereits Importierte BLEIBT — es liegt
    /// in der Datenbank und ist genau der Gewinn des laufenden Imports.</summary>
    public async Task FailBrowserImportAsync(int importId, string message, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is not null) await _outcome.FailAsync(import, message);
    }
}
