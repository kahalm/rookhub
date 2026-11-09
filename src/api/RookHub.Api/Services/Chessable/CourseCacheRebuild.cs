using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Cache-Weg für EIN Buch (<see cref="StaleAction.Cache"/>) des <see cref="ImportReprocessService"/> (aus ihm herausgezogen,
/// 2026-11-09) samt der Markierung „keine Linie im Cache" (<c>Book.CacheMissAt</c>). Protokolliert unter der Kategorie des Dienstes.
/// </summary>
/// <param name="cachedLines">Der geteilte Linien-Cache; null nur in Tests — dann bleibt das Buch veraltet.</param>
internal sealed class CourseCacheRebuild(AppDbContext db, PgnImportService pgnImport, ICachedLineSource? cachedLines,
    ILogger<ImportReprocessService> logger)
{
    /// <summary>
    /// Cache-Weg für EIN Buch (<see cref="StaleAction.Cache"/>): Zugtexte aller Linien aus dem geteilten
    /// piratechess-Linien-Cache, Header aus dem gespeicherten PGN, dann In-place-Aufbereitung über
    /// <see cref="PgnImportService.ImportFileAsync"/> (lädt das Buch selbst, schreibt den Text nur, wenn er sich
    /// unterscheidet, setzt <c>ImportVersion</c>).
    /// <para>Erst prüfen, dann schreiben: gibt es keine einzige gecachte Linie, wird nichts geholt; wirft eine
    /// Portion, wird nichts geschrieben — kein halb erneuerter Kurs mit hochgesetzter Version.</para>
    /// </summary>
    /// <returns>null = nichts aus dem Cache übernommen, das Buch bleibt veraltet.</returns>
    public async Task<(int Replaced, int Updated)?> RebuildAsync(int bookId, bool playFromStartPosition)
    {
        if (cachedLines is null) return null;

        // Den Text genau dieses Buchs — er wird umgeschrieben (erlaubte Include-Stelle, BookSourceIncludeGuardTests).
        // AsNoTracking: ImportFileAsync lädt das Buch getrackt ein zweites Mal; so hängt nur EINE Instanz im Tracker.
        // Alle Aufrufe mit CancellationToken.None: der Lauf ist fire-and-forget (ReprocessLauncher), Wegnavigieren
        // darf ihn nicht mitten im Buch abbrechen.
        var book = await db.Books.AsNoTracking().Include(b => b.Source)
            .FirstOrDefaultAsync(b => b.Id == bookId, CancellationToken.None)
            ?? throw new KeyNotFoundException($"Book {bookId} not found.");
        var source = book.Source?.SourcePgn;
        if (string.IsNullOrEmpty(source)) return null;

        var oids = CachedSourceRebuild.OidsOf(source);
        if (oids.Count == 0) return null;

        // Der Linien-Cache füllt nur Linien DIESES Kurses (piratechess ordnet jede Linie ihrem Kurs zu) — ohne bid aus dem Dateinamen
        // (chessable-u{uid}-{bid}.pgn) gibt es nichts zu fragen.
        if (!StaleContentRule.TryParseBid(book.FileName, out var bid))
        {
            logger.LogInformation(
                "Course-Reprocess: Buch {FileName} (Id {BookId}) — Kurs-Id nicht aus dem Dateinamen lösbar, bleibt veraltet",
                book.FileName, bookId);
            return null;
        }

        // Ein billiger Existenz-Aufruf vorab: ein Server ohne (diesen) Cache soll nicht Dutzende teure
        // PGN-Abfragen absetzen, um am Ende nichts zu haben. Weich — piratechess weg liefert hier „nichts".
        var cached = await cachedLines.GetCachedLineOidsAsync(bid, oids, CancellationToken.None);
        var wanted = oids.Where(cached.Contains).ToList();
        if (wanted.Count == 0)
        {
            logger.LogInformation(
                "Course-Reprocess: Buch {FileName} (Id {BookId}) — keine der {Total} Linien im Linien-Cache, bleibt veraltet",
                book.FileName, bookId, oids.Count);
            // Merken, damit Banner und Liste das Buch als „braucht Re-Import" führen statt es bei jedem Aufruf als
            // aktualisierbar anzubieten (Book.CacheMissAt). UpdatedAt bleibt unberührt — ein neuer Import hebt die
            // Markierung über genau dieses Feld wieder auf.
            var tracked = await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, CancellationToken.None);
            if (tracked != null)
            {
                tracked.CacheMissAt = DateTime.UtcNow > tracked.UpdatedAt ? DateTime.UtcNow : tracked.UpdatedAt;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            return null;
        }

        var mode = CachedSourceRebuild.ModeFor(source);
        var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var portion in wanted.Chunk(ImportReprocessService.CacheRebuildBatchSize))
            foreach (var (oid, pgn) in await cachedLines.GetCachedLinePgnsAsync(bid, portion, mode, CancellationToken.None))
                fresh[oid] = pgn;

        var rebuilt = CachedSourceRebuild.Rebuild(source, fresh);
        logger.LogInformation(
            "Course-Reprocess: Buch {FileName} (Id {BookId}, Modus {Mode}) — {Replaced} von {Total} Linien aus dem Cache, {Missing} nicht gecacht, {ModeMismatch} Modus-Konflikt, {Conflicts} Konflikt, {Hidden} ausgeblendet",
            book.FileName, bookId, mode, rebuilt.Replaced, rebuilt.Total, rebuilt.Missing, rebuilt.ModeMismatch, rebuilt.Conflicts, rebuilt.Hidden);
        // Keine Linie übernommen (etwa alle im falschen Modus): nicht als erneuert ausgeben — sonst stünde das
        // Buch auf der aktuellen Version, ohne dass sich etwas geändert hat, und käme nie wieder dran.
        if (rebuilt.Replaced == 0) return null;

        // Ein großer Kurs braucht Dutzende Cache-Abfragen, also Minuten. Hat in der Zeit ein anderer Weg das
        // Buch geschrieben (ein Browser-Import hängt Linien an), fehlten dessen Linien im umgeschriebenen Text,
        // und ImportFileAsync überschriebe ihn damit. Der einzige Schreiber des Texts (ImportIntoBookAsync) setzt
        // dabei UpdatedAt — steht es anders als beim Laden, lieber nichts schreiben: das Buch bleibt veraltet
        // und kommt beim nächsten „Aktualisieren" mit dem neuen Stand dran.
        var updatedAt = await db.Books.Where(b => b.Id == bookId).Select(b => (DateTime?)b.UpdatedAt)
            .FirstOrDefaultAsync(CancellationToken.None);
        if (updatedAt != book.UpdatedAt)
        {
            logger.LogInformation(
                "Course-Reprocess: Buch {FileName} (Id {BookId}) wurde während der Cache-Abfragen geändert — nichts geschrieben, bleibt veraltet",
                book.FileName, bookId);
            return null;
        }

        var res = await pgnImport.ImportFileAsync(book.FileName, rebuilt.Pgn, CancellationToken.None,
            playFromStartPosition: playFromStartPosition);
        return (rebuilt.Replaced, res.Updated);
    }
}
