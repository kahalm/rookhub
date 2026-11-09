using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Cache-Weg für EIN Repertoire (<see cref="StaleAction.Cache"/>) des <see cref="ImportReprocessService"/> (aus ihm
/// herausgezogen, 2026-11-09) samt der Markierung „nichts übernommen" (<see cref="Repertoire.CacheMissAt"/>).
/// Protokolliert unter der Kategorie des Dienstes.
/// </summary>
/// <param name="cachedLines">Der geteilte Linien-Cache; null nur in Tests — dann bleibt das Repertoire veraltet.</param>
internal sealed class RepertoireCacheRebuild(AppDbContext db, ICachedLineSource? cachedLines, ILogger<ImportReprocessService> logger)
{
    /// <summary>
    /// Cache-Weg für EIN Repertoire (<see cref="StaleAction.Cache"/>) — dieselben Regeln wie
    /// <see cref="CourseCacheRebuild.RebuildAsync"/> für Kurse, nur ohne Import danach (der Trainer wertet das PGN live aus):
    /// je Datei mit <c>[ChessableOid]</c> die Zugtexte aus dem geteilten Linien-Cache über
    /// <see cref="CachedSourceRebuild"/> (Header bleiben, ausgeblendete Partien bleiben, Modus je DATEI), dann Text
    /// schreiben und die Version setzen.
    /// <para>Erst prüfen, dann schreiben: gibt es keine einzige gecachte Linie, wird nichts geholt; wirft eine
    /// Portion, wird nichts geschrieben; wird keine Linie übernommen oder hat jemand das Repertoire währenddessen
    /// geschrieben, auch nicht. In allen Fällen bleibt es veraltet und kommt beim nächsten „Aktualisieren" dran.</para>
    /// <para><c>RepertoireFile.ChessableOidsCache</c>/<c>ChessableOidsPgnLength</c> und <c>CleanupVersion</c> bleiben
    /// unberührt: die oid-Menge ändert der Rebuild nicht (nur der Zugtext wird ersetzt, oid-Header kommen nie dazu),
    /// der Kennungs-Zwischenspeicher prüft sich über die PGN-Länge ohnehin selbst, und ausgeblendete Partien und
    /// entfernte oids stehen danach genauso da wie vorher.</para>
    /// </summary>
    /// <returns>Zahl der übernommenen Linien; null = nichts übernommen, das Repertoire bleibt veraltet.</returns>
    /// <param name="bid">Kurs-Id des Repertoires (<see cref="ImportReprocessService.ResolveRepertoireBid"/>); der Linien-Cache füllt nur Linien
    /// dieses Kurses. null → nichts zu fragen, bleibt veraltet.</param>
    public async Task<int?> RebuildAsync(int repertoireId, string name, string? bid)
    {
        if (cachedLines is null) return null;
        if (bid is null)
        {
            logger.LogInformation(
                "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}) — keine Kurs-Id, bleibt veraltet",
                name, repertoireId);
            await MarkRepertoireCacheMissAsync(repertoireId);
            return null;
        }

        // Den Stand VOR dem Lesen der Texte merken: wer danach schreibt (Live-Append der Extension, Upload, Löschen
        // einer Datei, die Bereinigung), setzt Repertoire.UpdatedAt — und das fällt unten auf. Alle Aufrufe mit
        // CancellationToken.None: der Lauf ist fire-and-forget (ReprocessLauncher), Wegnavigieren darf ihn nicht
        // mitten im Repertoire abbrechen.
        var loadedAt = await RepertoireUpdatedAtAsync(repertoireId);
        if (loadedAt is null) return null;   // inzwischen gelöscht

        // Die Texte Datei für Datei, und nur Dateien, die überhaupt oids tragen (LIKE, ohne den Text zu übertragen).
        // Sie bleiben bis zum Schreiben getrackt — die EINES Repertoires; der Aufrufer leert den Tracker danach.
        var fileIds = await db.RepertoireFiles
            .Where(f => f.RepertoireId == repertoireId && f.PgnContent.Contains(StaleContentRule.ModernMarker))
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .ToListAsync(CancellationToken.None);
        var files = new List<(RepertoireFile File, string Mode, IReadOnlyList<string> Oids)>();
        foreach (var id in fileIds)
        {
            var file = await db.RepertoireFiles.FirstOrDefaultAsync(f => f.Id == id, CancellationToken.None);
            if (file is null) continue;
            // Modus je DATEI: ein aus einem Kurs umgewandeltes Repertoire trägt die Trainingsmarker, ein von
            // Chessable geholtes nicht — beide können im selben Repertoire liegen.
            files.Add((file, CachedSourceRebuild.ModeFor(file.PgnContent), CachedSourceRebuild.OidsOf(file.PgnContent)));
        }
        var oids = files.SelectMany(f => f.Oids).Distinct(StringComparer.Ordinal).ToList();
        if (oids.Count == 0)
        {
            await MarkRepertoireCacheMissAsync(repertoireId);
            return null;
        }

        // EIN billiger Existenz-Aufruf fürs ganze Repertoire: ein Server ohne (diesen) Cache soll nicht Dutzende teure
        // PGN-Abfragen absetzen, um am Ende nichts zu haben. Weich — piratechess weg liefert hier „nichts".
        var cached = await cachedLines.GetCachedLineOidsAsync(bid, oids, CancellationToken.None);
        if (!oids.Any(cached.Contains))
        {
            logger.LogInformation(
                "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}) — keine der {Total} Linien im Linien-Cache, bleibt veraltet",
                name, repertoireId, oids.Count);
            await MarkRepertoireCacheMissAsync(repertoireId);
            return null;
        }

        // Je Modus eigene Abfragen (derselbe Linien-Inhalt kommt je Modus anders heraus), in Portionen wie bei Kursen.
        var freshByMode = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var group in files.GroupBy(f => f.Mode, StringComparer.Ordinal))
        {
            var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
            var wanted = group.SelectMany(f => f.Oids).Where(cached.Contains).Distinct(StringComparer.Ordinal).ToList();
            foreach (var portion in wanted.Chunk(ImportReprocessService.CacheRebuildBatchSize))
                foreach (var (oid, pgn) in await cachedLines.GetCachedLinePgnsAsync(bid, portion, group.Key, CancellationToken.None))
                    fresh[oid] = pgn;
            freshByMode[group.Key] = fresh;
        }

        var rebuilt = files.Select(f => (f.File, Result: CachedSourceRebuild.Rebuild(f.File.PgnContent, freshByMode[f.Mode]))).ToList();
        var replaced = rebuilt.Sum(f => f.Result.Replaced);
        logger.LogInformation(
            "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}, {Files} Dateien mit oids, Modus {Modes}) — {Replaced} von {Total} Linien aus dem Cache, {Missing} nicht gecacht, {ModeMismatch} Modus-Konflikt, {Conflicts} Konflikt, {Hidden} ausgeblendet",
            name, repertoireId, files.Count, string.Join('/', freshByMode.Keys), replaced, rebuilt.Sum(f => f.Result.Total),
            rebuilt.Sum(f => f.Result.Missing), rebuilt.Sum(f => f.Result.ModeMismatch), rebuilt.Sum(f => f.Result.Conflicts),
            rebuilt.Sum(f => f.Result.Hidden));
        // Keine Linie übernommen (etwa alle im falschen Modus): nicht als erneuert ausgeben — sonst stünde das
        // Repertoire auf der aktuellen Version, ohne dass sich etwas geändert hat, und käme nie wieder dran.
        if (replaced == 0)
        {
            await MarkRepertoireCacheMissAsync(repertoireId);
            return null;
        }

        // Ein großes Repertoire braucht Dutzende Cache-Abfragen, also Minuten. Hat in der Zeit ein anderer Weg
        // geschrieben (die Extension hängt live Linien an), fehlten dessen Linien im umgeschriebenen Text, und dieser
        // Lauf überschriebe sie. Alle Schreiber des Repertoire-PGN setzen UpdatedAt — steht es anders als beim Laden,
        // lieber nichts schreiben: das Repertoire bleibt veraltet und kommt beim nächsten „Aktualisieren" dran.
        if (await RepertoireUpdatedAtAsync(repertoireId) != loadedAt)
        {
            logger.LogInformation(
                "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}) wurde während der Cache-Abfragen geändert — nichts geschrieben, bleibt veraltet",
                name, repertoireId);
            return null;
        }

        foreach (var (file, result) in rebuilt)
        {
            if (result.Replaced == 0) continue;
            file.PgnContent = result.Pgn;
            // Bytes, nicht Zeichen — wie beim Hochladen und in der Bereinigung.
            file.FileSize = Encoding.UTF8.GetByteCount(result.Pgn);
        }
        var repertoire = await db.Repertoires.FirstAsync(r => r.Id == repertoireId, CancellationToken.None);
        repertoire.ImportVersion = ImportPipeline.CurrentVersion;
        repertoire.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return replaced;
    }

    /// <summary>
    /// Merkt sich, dass der Cache-Weg für dieses Repertoire nichts übernehmen konnte (<see cref="Repertoire.CacheMissAt"/>),
    /// damit Banner und Liste es als „braucht Re-Import" führen statt es bei jedem Aufruf als aktualisierbar anzubieten.
    /// UpdatedAt bleibt unberührt — ein neuer Import hebt die Markierung über genau dieses Feld wieder auf. Nicht bei
    /// vorübergehenden Gründen (währenddessen geändert, Portion geworfen); ein piratechess-Ausfall liefert allerdings
    /// ebenfalls „nichts gecacht" und markiert damit zu Unrecht — der nächste Lauf heilt das.
    /// </summary>
    internal async Task MarkRepertoireCacheMissAsync(int repertoireId)
    {
        var tracked = await db.Repertoires.FirstOrDefaultAsync(r => r.Id == repertoireId, CancellationToken.None);
        if (tracked is null) return;
        var now = DateTime.UtcNow;
        tracked.CacheMissAt = now > tracked.UpdatedAt ? now : tracked.UpdatedAt;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private Task<DateTime?> RepertoireUpdatedAtAsync(int repertoireId) =>
        db.Repertoires.Where(r => r.Id == repertoireId).Select(r => (DateTime?)r.UpdatedAt)
            .FirstOrDefaultAsync(CancellationToken.None);
}
