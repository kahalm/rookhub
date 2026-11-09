using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Neu-Aufbereitung veralteter Repertoires (Status und Lauf) des <see cref="ImportReprocessService"/> (aus ihm
/// herausgezogen, 2026-11-09). Repertoires speichern ihr Roh-PGN selbst und werten live aus: Chessable mit oids
/// kommt aus dem Linien-Cache (<see cref="RepertoireCacheRebuild"/>), Chessable ohne oids wird re-gefetcht
/// (<see cref="ChessableRefetchQueue"/>), alle übrigen bekommen nur die aktuelle Version. Welcher Fall gilt, entscheidet
/// <see cref="StaleContentRule.ActionForRepertoire"/> — für Status UND Lauf. Protokolliert unter der Kategorie des Dienstes.
/// </summary>
internal sealed class RepertoireReprocess(AppDbContext db, ICourseReimporter chessableImport, ChessableRefetchQueue refetchQueue,
    RepertoireCacheRebuild cacheRebuild, ILogger<ImportReprocessService> logger, bool chessableEnabled)
{
    /// <summary>
    /// Was Status und Lauf über ein veraltetes Repertoire wissen müssen — OHNE seinen PGN-Text. Von den Dateinamen
    /// stehen nur die mit <c>chessable-</c> dabei (Herkunft + bid); ob eine Datei oids trägt, kommt fertig aus SQL.
    /// </summary>
    private sealed record StaleRepertoire(int Id, int UserId, string Name, string? ChessableCourseId,
        IReadOnlyList<string> ChessableFileNames, bool SourceModern, bool CacheMissed)
    {
        /// <summary>Chessable-Herkunft: die hinterlegte Kurs-Id ODER ein Dateiname aus dem Chessable-Import —
        /// dieselbe Regel wie die (!)-Markierung der Liste (<c>RepertoireService.MarkNeedsReimportAsync</c>).</summary>
        public bool IsChessable => !string.IsNullOrEmpty(ChessableCourseId) || ChessableFileNames.Count > 0;

        /// <summary>bid für einen Re-Fetch; null ⇒ nicht auflösbar (siehe <see cref="ImportReprocessService.ResolveRepertoireBid"/>).</summary>
        public string? Bid => ImportReprocessService.ResolveRepertoireBid(ChessableCourseId, ChessableFileNames);
    }

    /// <summary>Verwaltbare Repertoires: Admin = die ALLER User (wie bei Kursen), sonst die eigenen.</summary>
    private IQueryable<Repertoire> ManageableRepertoires(int userId, bool isAdmin) =>
        isAdmin ? db.Repertoires : db.Repertoires.Where(r => r.UserId == userId);

    /// <summary>
    /// Die veralteten Repertoires als PROJEKTION — ob eine Datei <c>[ChessableOid]</c> trägt, fragt ein SQL-<c>LIKE</c>,
    /// der Text selbst wird nicht übertragen. Vorher luden Status und Lauf jedes Repertoire mit
    /// <c>.Include(r =&gt; r.Files)</c>, also JEDEN PGN-Text (Prod trägt ~250 MB Chessable-PGN), nur um Herkunft und
    /// diesen Marker zu prüfen — dieselbe Klasse Fehler wie bei den Büchern (0.508.3: 23 GB RAM auf Prod). Den Text lädt
    /// nur noch der Cache-Weg, und zwar Datei für Datei (<see cref="RepertoireCacheRebuild.RebuildAsync"/>).
    /// </summary>
    private async Task<List<StaleRepertoire>> LoadStaleRepertoiresAsync(IQueryable<Repertoire> repertoires, CancellationToken ct)
    {
        var rows = await repertoires
            .Where(r => r.ImportVersion < ImportPipeline.CurrentVersion)
            .Select(r => new
            {
                r.Id, r.UserId, r.Name, r.ChessableCourseId,
                ChessableFileNames = r.Files.Where(f => f.FileName.StartsWith("chessable-")).Select(f => f.FileName).ToList(),
                SourceModern = r.Files.Any(f => f.PgnContent.Contains(StaleContentRule.ModernMarker)),
                CacheMissed = r.CacheMissAt != null && r.CacheMissAt >= r.UpdatedAt,   // Repertoire.CacheMissAt gilt, solange nicht älter als UpdatedAt
            })
            .ToListAsync(ct);
        return rows.Select(r => new StaleRepertoire(r.Id, r.UserId, r.Name, r.ChessableCourseId, r.ChessableFileNames, r.SourceModern,
                r.CacheMissed))
            .ToList();
    }

    /// <summary>Dieselbe Regel für Status, Lauf und die (!)-Markierung der Liste (<see cref="StaleContentRule.ActionForRepertoire"/>).
    /// <paramref name="forDisplay"/> = Status: ein Repertoire, aus dem der letzte Lauf nichts aus dem Cache übernehmen konnte
    /// (<see cref="Repertoire.CacheMissAt"/>), zählt als „braucht Re-Import". Der LAUF fragt ohne und versucht es weiter.</summary>
    private StaleAction ActionFor(StaleRepertoire r, bool forDisplay = false) =>
        StaleContentRule.ActionForRepertoire(r.IsChessable, r.SourceModern, chessableEnabled, forDisplay && r.CacheMissed);

    public async Task<ReprocessStatusDto> GetStatusAsync(int userId, bool isAdmin, CancellationToken ct)
    {
        var repertoires = ManageableRepertoires(userId, isAdmin);
        var total = await repertoires.CountAsync(ct);
        var stale = await LoadStaleRepertoiresAsync(repertoires, ct);
        // Dieselbe Vierteilung wie bei den Kursen — „Manual" ist der Showstopper, der in der LISTE als (!) am
        // Repertoire steht statt als anonyme Zahl im Banner. Anzeige = Ausführung: der Lauf nimmt dieselbe Regel.
        // Ausnahme wie bei Kursen: woraus der letzte Lauf nichts aus dem Cache übernehmen konnte, zählt als „braucht
        // Re-Import" (CacheMissed) — der Lauf versucht es weiter, verspricht im Banner aber nichts mehr.
        var actions = stale.Select(r => ActionFor(r, forDisplay: true)).ToList();
        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale.Count,
            // Cache zählt mit wie bei Kursen: für den Nutzer ein Klick, kein Download — das Banner rechnet
            // reprocessableLocally + refetchable und bleibt damit unverändert.
            ReprocessableLocally = actions.Count(a => a is StaleAction.Local or StaleAction.Cache),
            FromCache = actions.Count(a => a == StaleAction.Cache),
            Refetchable = actions.Count(a => a == StaleAction.Refetch),
            NeedsReimport = actions.Count(a => a == StaleAction.Manual),
            // UI-Sweep 2026-10-11 (r2-rg-rep-reprocess): Umfang fürs Banner (Admin = alle Nutzer, davon eigene).
            AllUsers = isAdmin,
            OwnActionable = stale.Zip(actions).Count(x => x.First.UserId == userId && ImportReprocessService.IsActionable(x.Second)),
        };
    }

    /// <param name="localOnly">true = ohne Chessable-Abruf („Aus Cache"): Versions-Mark UND der Linien-Cache-Weg
    /// (<see cref="StaleAction.Cache"/>) — wie bei Kursen gehört er dazu, denn „ohne Chessable-Abruf" ist genau das.
    /// false = zusätzlich Chessable-Repertoires ohne oids frisch holen („Alle").</param>
    public async Task<ReprocessResultDto> ReprocessAsync(int userId, bool isAdmin, bool localOnly, CancellationToken ct)
    {
        // Nur die Projektion (kein PGN-Text, nichts getrackt) — wie bei den Kursen. Re-Fetch je Repertoire läuft mit
        // dem Bearer des jeweiligen Owners (gecachte Kurse laufen ohnehin ohne Bearer durch).
        var stale = await LoadStaleRepertoiresAsync(ManageableRepertoires(userId, isAdmin), ct);

        var result = new ReprocessResultDto();
        var refetch = new List<ChessableRefetchQueue.RefetchCandidate>();
        var versionMark = new List<int>();
        var bearerUsers = await db.ChessableCredentials.Select(c => c.UserId).ToHashSetAsync(ct);
        // Ein gecachter Kurs ist AUCH ohne Bearer holbar (piratechess liefert ihn aus dem Rohdaten-Cache) —
        // aber nur im Admin-Reprocess (trustOwnership; sonst wäre es ein Eigentums-Bypass, [[0.203.9]]).
        // Cache-Status daher NUR abrufen, wenn es überhaupt einen bearer-losen Re-Fetch-Kandidaten gibt
        // (Refetch setzt den eigenen Chessable-Weg voraus).
        var needCache = !localOnly && isAdmin && stale.Any(r =>
            ActionFor(r) == StaleAction.Refetch && r.Bid != null && !bearerUsers.Contains(r.UserId));
        var cachedBids = needCache
            ? await chessableImport.GetCachedBidsAsync(CancellationToken.None)
            : new HashSet<string>();
        foreach (var r in stale)
        {
            var action = ActionFor(r);
            if (action == StaleAction.Manual)
            {
                // Ohne den eigenen Chessable-Weg ist dieses Repertoire NICHT holbar. Es trotzdem auf die
                // aktuelle Version zu setzen wäre eine Lüge: die [%alt]-Varianten fehlen weiter. Es bleibt
                // veraltet und trägt in der Liste ein (!) mit dem Hinweis auf die Erweiterung.
                result.Skipped++;
                continue;
            }
            if (action == StaleAction.Cache)
            {
                await RebuildFromCacheAsync(r, result);
                continue;
            }
            if (action == StaleAction.Refetch)
            {
                // Chessable-Repertoire OHNE oids = Re-Fetch-Kandidat.
                if (localOnly) continue; // „Aus Cache"-Modus: Netz-Re-Fetch bewusst auslassen (bleibt stale)
                if (r.Bid is { } bid && (bearerUsers.Contains(r.UserId) || (isAdmin && cachedBids.Contains(bid))))
                {
                    // Frisch holen (inkl. [%alt]) und IN-PLACE ins bestehende Repertoire schreiben (Id/
                    // Trainings-Fortschritt bleiben; Version steigt beim Job-Abschluss). Owner = r.UserId →
                    // richtiger Bearer bzw. — falls keiner — Cache-Re-Fetch (Admin, gecacht).
                    refetch.Add(new ChessableRefetchQueue.RefetchCandidate(r.UserId, bid, "repertoire", r.Name, r.Id));
                    continue;
                }
                // Chessable, aber weder Bearer noch (admin-)gecacht (oder keine bid lösbar) → nie automatisch holbar.
                // Statt es ewig im Banner hängen zu lassen, als aktuell markieren (ein echter Re-Import erfordert
                // erst, dass der Besitzer einen Bearer hinterlegt). Fällt in den Versions-Mark unten.
            }
            // Nicht-Chessable / Chessable-ohne-Bearer-und-nicht-gecacht → Versions-Mark.
            versionMark.Add(r.Id);
        }

        await MarkVersionsAsync(versionMark, result);

        // Zentrales Einreihen (Batch-Cache, Backoff, Dedup, Admin-Bypass, kein Abbruch) — geteilt mit Kursen.
        // Die bereits ermittelte Cache-Menge weiterreichen (spart einen zweiten Abruf); sonst holt sie der
        // zentrale Pfad selbst (z. B. reine Bearer-Kandidaten, für die needCache nicht griff).
        await refetchQueue.EnqueueAsync(refetch, isAdmin, result, needCache ? cachedBids : null);
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "Repertoire-Reprocess für User {UserId} (admin={IsAdmin}, localOnly={LocalOnly}): {Reprocessed} aufbereitet, davon {RebuiltFromCache} aus dem Linien-Cache ({CacheLinesReplaced} Linien ersetzt), {Enqueued} eingereiht, {Skipped} übersprungen, {Failed} fehlgeschlagen",
            userId, isAdmin, localOnly, result.Reprocessed, result.RebuiltFromCache, result.CacheLinesReplaced,
            result.Enqueued, result.Skipped, result.Failed);
        return result;
    }

    /// <summary>Cache-Zweig für EIN Repertoire, isoliert: zählt Reprocessed/RebuiltFromCache bzw. Skipped/Failed.</summary>
    private async Task RebuildFromCacheAsync(StaleRepertoire r, ReprocessResultDto result)
    {
        // Chessable mit oids: Zugtexte aus dem geteilten Linien-Cache (aktuelle piratechess-Logik), dann der
        // Versions-Mark. Läuft auch bei localOnly (kein Chessable-Kontakt) und mit Chessable:Enabled=false.
        // Je Repertoire isoliert wie bei den Kursen: EIN kaputtes darf den Lauf nicht mitreißen.
        try
        {
            if (await cacheRebuild.RebuildAsync(r.Id, r.Name, r.Bid) is { } replaced)
            {
                result.Reprocessed++;
                result.RebuiltFromCache++;
                result.CacheLinesReplaced += replaced;
            }
            else
            {
                // Nichts übernommen (nichts gecacht, piratechess weg, währenddessen geändert): bleibt
                // veraltet und wird beim nächsten „Aktualisieren" erneut versucht.
                result.Skipped++;
            }
        }
        catch (Exception ex)
        {
            // Portion geworfen (piratechess nicht erreichbar) oder Schreiben gescheitert: NICHTS wurde
            // geschrieben, das Repertoire behält seine Version.
            result.Failed++;
            logger.LogWarning(ex,
                "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}) konnte nicht aus dem Linien-Cache erneuert werden — bleibt veraltet",
                r.Name, r.Id);
        }
        finally
        {
            // Die Dateien EINES Repertoires hingen bis hierher im Tracker — vor dem nächsten wieder raus
            // (Prod: ~250 MB Chessable-PGN), und ein gescheitertes SaveChanges risse sonst das nächste mit.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Versions-Mark für die gesammelten Repertoires (ohne Dateien zu laden); zählt Reprocessed.</summary>
    private async Task MarkVersionsAsync(List<int> versionMark, ReprocessResultDto result)
    {
        // Versions-Mark gezielt: nur die Repertoire-Zeilen, OHNE Dateien — vorher hingen hier die PGN-Texte aller
        // veralteten Repertoires bis zum Ende des Laufs im Tracker. Erst nach der Schleife geladen, weil der
        // Cache-Zweig den Tracker je Repertoire leert.
        if (versionMark.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var rep in await db.Repertoires.Where(x => versionMark.Contains(x.Id)).ToListAsync(CancellationToken.None))
            {
                rep.ImportVersion = ImportPipeline.CurrentVersion;
                rep.UpdatedAt = now;
                result.Reprocessed++;
            }
        }
    }
}

