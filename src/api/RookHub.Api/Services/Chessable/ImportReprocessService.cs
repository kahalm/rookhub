using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Schmale Abstraktion fürs erneute Einreihen eines Chessable-Kurs-Imports — entkoppelt den
/// <see cref="ImportReprocessService"/> von der vollen <see cref="ChessableImportService"/>
/// (die diese Schnittstelle implementiert) und macht den Re-Fetch-Pfad testbar.
/// </summary>
public interface ICourseReimporter
{
    /// <param name="knownCached">Vorab bekannter Cache-Status des Kurses (aus einem Batch-Abruf), um den
    /// teuren Einzel-Cache-Check je Kurs zu sparen. null ⇒ der Reimporter ermittelt ihn selbst.</param>
    /// <param name="trustOwnership">true ⇒ die Eigentumsprüfung gegen die Chessable-Bibliothek überspringen.
    /// Für den Admin-Massen-Reprocess bereits im Bestand befindlicher Kurse: Chessables getHomeData listet
    /// nur einen Teil der Bibliothek (Home-Ansicht) → sonst würden eigene, längst importierte Kurse fälschlich
    /// als „nicht besessen" übersprungen. Admins dürfen ohnehin jeden Kurs holen, daher unbedenklich.</param>
    Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, int? targetRepertoireId = null, bool? knownCached = null, bool trustOwnership = false, CancellationToken ct = default);

    /// <summary>Alle im piratechess-DB-Cache vorliegenden Kurs-Bids auf einen Schlag (1 Aufruf statt
    /// N Einzel-Cache-Checks) — für den Massen-Reprocess, um die Lane-Klassifikation vorab zu füllen.</summary>
    Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default);
}

/// <summary>
/// Schmale Abstraktion über den geteilten piratechess-Linien-Cache (implementiert von
/// <see cref="ChessableProxyService"/>) — macht den Cache-Weg des <see cref="ImportReprocessService"/> testbar
/// wie <see cref="ICourseReimporter"/> den Re-Fetch-Weg.
/// </summary>
public interface ICachedLineSource
{
    /// <summary>Welche der oids liegen im Cache (nur Existenz, billig) — so, dass piratechess einen Import des Kurses
    /// <paramref name="bid"/> damit füllt. Weich: Fehler → leere Menge.</summary>
    Task<HashSet<string>> GetCachedLineOidsAsync(string bid, IReadOnlyCollection<string> oids, CancellationToken ct = default);

    /// <summary>PGN je gecachter oid des Kurses <paramref name="bid"/>, mit der AKTUELLEN piratechess-Logik erzeugt.
    /// Nicht gecachte oids fehlen; ein Verbindungsfehler WIRFT.</summary>
    Task<Dictionary<string, string>> GetCachedLinePgnsAsync(string bid, IEnumerable<string> oids, string mode = "None", CancellationToken ct = default);
}

/// <summary>
/// Neu-Aufbereitung („Reprocessing") veralteter Datensätze, wenn die Import-Pipeline weiterentwickelt
/// wurde (<see cref="ImportPipeline"/>). Datensätze mit <c>ImportVersion &lt; CurrentVersion</c> gelten
/// als veraltet; der Reprocess-Knopf je Sektion ruft diesen Service.
///
/// <para><b>Kurse/Bücher:</b> bevorzugt lokal aus dem gespeicherten Roh-PGN (<c>BookSource.SourcePgn</c>)
/// — verlustfrei und in-place (Match per LineId, Fortschritt/Statistik bleiben erhalten). Ein Chessable-Kurs,
/// dessen Quelle schon <c>[ChessableOid]</c> trägt, bekommt vorher die Zugtexte seiner Linien frisch aus dem
/// geteilten piratechess-Linien-Cache (<see cref="StaleAction.Cache"/>, <see cref="CachedSourceRebuild"/>) —
/// so kommen Änderungen an der PGN-Erzeugung in piratechess in bestehende Kurse, ohne Chessable-Kontakt.
/// Fehlt die Quelle (Altbestand), wird für Chessable-Kurse ein Re-Fetch-Hintergrund-Job eingereiht
/// (<see cref="ChessableImportService.EnqueueReimportAsync"/>). Sonst: nur per manuellem Re-Import.
/// Welcher Fall gilt, entscheidet <see cref="StaleContentRule.ActionForBook"/> — für Status UND Lauf.</para>
///
/// <para><b>Repertoires:</b> speichern ihr Roh-PGN selbst und werten live aus — es gibt keine abgeleiteten Daten
/// zu erneuern. Ein Chessable-Repertoire, dessen Dateien <c>[ChessableOid]</c> tragen, bekommt wie ein Kurs die
/// Zugtexte seiner Linien aus dem Linien-Cache (<see cref="StaleAction.Cache"/>, je Datei, ausgeblendete Partien
/// bleiben); alle übrigen werden auf die aktuelle Version gesetzt, Chessable ohne oids wird re-gefetcht. Welcher
/// Fall gilt, entscheidet <see cref="StaleContentRule.ActionForRepertoire"/> — für Status UND Lauf.</para>
/// </summary>
public partial class ImportReprocessService
{
    private readonly AppDbContext _db;
    private readonly PgnImportService _pgnImport;
    private readonly ICourseReimporter _chessableImport;
    /// <summary>Der geteilte Linien-Cache (Cache-Weg). null nur in Tests, die ihn nicht brauchen — dann
    /// bleibt ein Cache-Kurs veraltet wie bei einem nicht erreichbaren piratechess.</summary>
    private readonly ICachedLineSource? _cachedLines;
    private readonly ILogger<ImportReprocessService> _logger;
    /// <summary>Läuft der RookHub-EIGENE Chessable-Weg (<c>Chessable:Enabled</c>)? Ist er aus (PROD seit
    /// 2026-09-09), kann NICHTS neu von Chessable geholt werden — ein Re-Fetch-Auftrag bliebe für immer
    /// liegen, weil die Lanes gar nicht laufen. Dann gilt für ein veraltetes Buch: lokal aus der
    /// gespeicherten Quelle aufbereiten, wenn es eine hat (bringt genau das, was das Banner verspricht —
    /// die Zug-Kommentare; nur die <c>[ChessableOid]</c> fehlen weiter), sonst als „braucht Re-Import"
    /// ausweisen statt als aktualisierbar. Vorher stand dort dauerhaft „1 Kurs kann aktualisiert werden",
    /// und jeder Klick meldete nur „übersprungen" (gemeldet 2026-09-20).</summary>
    private readonly bool _chessableEnabled;

    private readonly ChessableRefetchQueue _refetchQueue;
    private readonly RepertoireReprocess _repertoires;

    /// <summary>oids je Abfrage an den Linien-Cache. piratechess lädt dafür je oid das rohe getGame-JSON —
    /// gemessen ~455 KB je Linie im gemeldeten Kurs (1.881 Linien = 835 MB in EINER Abfrage). 100 Linien
    /// sind ~45 MB: groß genug, dass ein Kurs in ein paar Dutzend Aufrufen durch ist, klein genug für den
    /// Speicher von piratechess und den Proxy-Timeout.</summary>
    public const int CacheRebuildBatchSize = 100;

    public ImportReprocessService(
        AppDbContext db,
        PgnImportService pgnImport,
        ICourseReimporter chessableImport,
        ILogger<ImportReprocessService> logger,
        IConfiguration? configuration = null,
        ICachedLineSource? cachedLines = null)
    {
        _chessableEnabled = ChessableSwitch.IsEnabled(configuration);
        _db = db;
        _pgnImport = pgnImport;
        _chessableImport = chessableImport;
        _cachedLines = cachedLines;
        _logger = logger;
        _refetchQueue = new ChessableRefetchQueue(db, chessableImport, _chessableEnabled);
        _repertoires = new RepertoireReprocess(db, chessableImport, _refetchQueue,
            new RepertoireCacheRebuild(db, cachedLines, logger), logger, _chessableEnabled);
    }

    // ===== Kurse / Bücher =====

    /// <summary>Verwaltbare Bücher: Admin = alle, sonst die eigenen (per OwnerUserId importierten) Kurse.</summary>
    private IQueryable<Book> ManageableBooks(int userId, bool isAdmin) =>
        isAdmin ? _db.Books : _db.Books.Where(b => b.OwnerUserId == userId);

    public async Task<ReprocessStatusDto> GetCourseStatusAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        var books = ManageableBooks(userId, isAdmin);
        var total = await books.CountAsync(ct);
        // Nur die nötigen Felder der veralteten Bücher laden.
        // ZWEI Schritte, nicht einer (gemessen 2026-10-06 auf Prod): filtert MariaDB über `ImportVersion` und liest
        // in DERSELBEN Abfrage `SourcePgn`, holt es die ausgelagerten Quelltexte JEDES durchsuchten Buchs (390 Bücher,
        // 190 MB) — 1,1 s, auch für ein bloßes `IS NOT NULL`. Erst die Ids, dann gezielt per Primärschlüssel: 0,1 s.
        var staleIds = await books.Where(b => b.ImportVersion < ImportPipeline.CurrentVersion).Select(b => b.Id).ToListAsync(ct);
        var stale = await _db.Books
            .Where(b => staleIds.Contains(b.Id))
            .Select(b => new
            {
                HasSource = b.Source.SourcePgn != null && b.Source.SourcePgn != "",
                // „Modern": Quelle enthält bereits ALLES, was die aktuelle Pipeline aus dem Quell-PGN zieht —
                // maßgeblich der jüngste Marker [ChessableOid] (piratechess ≥ v1.0.39). Nur dann reicht ein
                // lokaler Re-Parse (SQL-LIKE, lädt das große PGN NICHT). Eine ältere Quelle mit zwar [%alt]/
                // [%info], aber OHNE [ChessableOid] ist NICHT modern → braucht einen Chessable-Re-Fetch, damit
                // die oids (Grundlage der Fortschritts-Overlays) reinkommen.
                SourceModern = b.Source.SourcePgn != null && b.Source.SourcePgn.Contains("[ChessableOid"),
                b.Tags, b.FileName, b.OwnerUserId,
                CacheMissed = b.CacheMissAt != null && b.CacheMissAt >= b.UpdatedAt,   // Book.CacheMissAt gilt, solange nicht älter als UpdatedAt
            })
            .ToListAsync(ct);

        // Re-Fetch nur noch für Chessable-Kurse, deren gespeicherte Quelle die Marker NOCH NICHT enthält
        // (alte Abrufe). Chessable-Kurse mit „moderner" Quelle kommen aus dem Linien-Cache, alle
        // Nicht-Chessable mit Quelle LOKAL aus dem gespeicherten PGN (beides ohne Chessable-Kontakt).
        // EINE Regel für Anzeige und Ausführung (ActionFor) — laufen sie auseinander, verspricht das
        // Banner eine Aktion, die der Lauf dann überspringt, und es bleibt für immer stehen.
        // Ausnahme: ein Buch, dessen Linien der letzte Lauf NICHT im Cache fand, zählt als „braucht Re-Import"
        // (CacheMissed) — der Lauf versucht es weiter, verspricht im Banner aber nichts mehr.
        var actions = stale.Select(b => StaleContentRule.ActionForBook(b.HasSource, b.SourceModern, b.Tags, b.FileName,
            _chessableEnabled, b.CacheMissed)).ToList();
        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale.Count,
            Refetchable = actions.Count(a => a == StaleAction.Refetch),
            // Cache zählt mit: für den Nutzer ist es dasselbe — ein Klick, kein Download. Das Banner rechnet
            // reprocessableLocally + refetchable und bleibt damit unverändert.
            ReprocessableLocally = actions.Count(a => a is StaleAction.Local or StaleAction.Cache),
            FromCache = actions.Count(a => a == StaleAction.Cache),
            NeedsReimport = actions.Count(a => a == StaleAction.Manual),
            // UI-Sweep 2026-10-11 (r2-rg-rep-reprocess): Umfang fürs Banner (Admin = alle Nutzer, davon eigene).
            AllUsers = isAdmin,
            OwnActionable = stale.Zip(actions).Count(x => x.First.OwnerUserId == userId && IsActionable(x.Second)),
        };
    }

    /// <summary>Aktualisierbar im Sinne des Banners (lokal, aus dem Linien-Cache oder per Re-Fetch).</summary>
    internal static bool IsActionable(StaleAction a) => a is StaleAction.Local or StaleAction.Cache or StaleAction.Refetch;

    /// <param name="localOnly">true = nur aus dem serverseitig gespeicherten Quell-PGN aufbereiten
    /// („Aus Cache"), KEIN Chessable-Re-Fetch übers Netz. Der Linien-Cache-Weg (<see cref="StaleAction.Cache"/>)
    /// gehört dazu — „Aus Cache" meint „ohne Chessable-Abruf", und genau das ist er. false = zusätzlich
    /// Chessable-Altbestand ohne Quelle als Re-Fetch-Job einreihen („Alle").</param>
    public async Task<ReprocessResultDto> ReprocessCoursesAsync(int userId, bool isAdmin, bool localOnly = false, CancellationToken ct = default)
    {
        // Nur Metadaten + die SQL-seitig ermittelten Quell-Flags (wie GetCourseStatusAsync) — NICHT das
        // Roh-PGN, und nichts davon getrackt. Den Text lädt der lokale Zweig unten je Buch genau EINMAL
        // (PgnImportService.ReprocessFromStoredSourceAsync), der Cache-Zweig zweimal (ungetrackt zum Umschreiben,
        // dann im Import), und beide geben ihn nach dem Buch wieder frei (ChangeTracker.Clear). Vorher hingen
        // die Texte ALLER veralteten Bücher (je bis zu mehrere MB) gleichzeitig im Speicher und blieben bis
        // zum Ende des Laufs getrackt.
        // ZWEI Schritte, nicht einer (gemessen 2026-10-06 auf Prod): filtert MariaDB über `ImportVersion` und liest
        // in DERSELBEN Abfrage `SourcePgn`, holt es die ausgelagerten Quelltexte JEDES durchsuchten Buchs (390 Bücher,
        // 190 MB) — 1,1 s, auch für ein bloßes `IS NOT NULL`. Erst die Ids, dann gezielt per Primärschlüssel: 0,1 s.
        var staleIds = await ManageableBooks(userId, isAdmin)
            .Where(b => b.ImportVersion < ImportPipeline.CurrentVersion).Select(b => b.Id).ToListAsync(ct);
        var stale = await _db.Books
            .Where(b => staleIds.Contains(b.Id))
            .OrderBy(b => b.Id)
            .Select(b => new
            {
                b.Id, b.FileName, b.DisplayName, b.Tags, b.OwnerUserId,
                HasSource = b.Source.SourcePgn != null && b.Source.SourcePgn != "",
                SourceModern = b.Source.SourcePgn != null && b.Source.SourcePgn.Contains(StaleContentRule.ModernMarker),
            })
            .ToListAsync(ct);

        var result = new ReprocessResultDto();
        var refetch = new List<ChessableRefetchQueue.RefetchCandidate>();
        foreach (var book in stale)
        {
            var action = ActionFor(book.HasSource, book.SourceModern, book.Tags, book.FileName);
            if (action == StaleAction.Refetch)
            {
                // Chessable OHNE moderne Quelle: vollständiger Re-Fetch (die [ChessableOid] steht nicht im
                // alten gecachten PGN → ein lokales Reprocess könnte sie nie setzen).
                if (localOnly) continue; // Netz-Re-Fetch bewusst auslassen
                TryParseBid(book.FileName, out var bid);   // ActionFor hat die bid bereits geprüft
                refetch.Add(new ChessableRefetchQueue.RefetchCandidate(book.OwnerUserId ?? userId, bid, "book", book.DisplayName, null));
            }
            else if (action == StaleAction.Local)
            {
                await ReprocessLocalAsync(book.Id, book.FileName, ownCourse: book.OwnerUserId != null, result);
            }
            else if (action == StaleAction.Cache)
            {
                await ReprocessFromCacheAsync(book.Id, book.FileName, ownCourse: book.OwnerUserId != null, result);
            }
            else
            {
                result.Skipped++; // keine Quelle, kein Re-Fetch → nur manueller Re-Import
            }
        }

        // Zentrales Einreihen (Batch-Cache, Backoff, Dedup, Admin-Bypass, kein Abbruch) — geteilt mit Repertoiren.
        await _refetchQueue.EnqueueAsync(refetch, isAdmin, result);

        _logger.LogInformation(
            "Course-Reprocess für User {UserId} (admin={IsAdmin}, localOnly={LocalOnly}): {Reprocessed} aufbereitet ({UpdatedLines} Linien), davon {RebuiltFromCache} aus dem Linien-Cache ({CacheLinesReplaced} Linien ersetzt), {Enqueued} eingereiht, {Skipped} übersprungen, {Failed} fehlgeschlagen",
            userId, isAdmin, localOnly, result.Reprocessed, result.UpdatedLines, result.RebuiltFromCache, result.CacheLinesReplaced,
            result.Enqueued, result.Skipped, result.Failed);
        return result;
    }

    /// <summary>Lokaler Zweig für EIN Buch (<see cref="StaleAction.Local"/>), isoliert: zählt Reprocessed bzw. Failed.</summary>
    /// <param name="ownCourse">Ein EIGENER Kurs (<c>OwnerUserId</c> gesetzt) — seine Repertoire-Linien aus der Grundstellung bleiben spielbar.</param>
    private async Task ReprocessLocalAsync(int bookId, string fileName, bool ownCourse, ReprocessResultDto result)
    {
        // Lokal verlustfrei + in-place aus dem gespeicherten PGN (der Import-Kern erkennt das
        // veraltete Buch). Gilt für Nicht-Chessable-Kurse und — ohne eigenen Chessable-Weg — für
        // Chessable-Altbestand ohne oids → kein Chessable-Kontakt, kein Crash/Dedup/Bearer.
        // (Chessable MIT oids geht über den Cache-Zweig unten.)
        // FALLE: EIN kaputtes Buch (korruptes SourcePgn, Parser-Sonderfall, DbUpdateException)
        // riss ohne dieses try/catch den GANZEN Batch mit — alle nachfolgenden Bücher blieben
        // veraltet und die gesammelten Re-Fetch-Kandidaten (unten) wurden nie eingereiht, weil
        // die Exception bis in den fire-and-forget-Launcher durchschlug. Also je Buch isolieren.
        try
        {
            // Dieselbe Regel wie beim Anlegen: bei einem EIGENEN Kurs bleiben die
            // Repertoire-Linien aus der Grundstellung spielbar. Ohne das raeumte ein
            // Reprocess genau die Linien wieder ab, die die Umwandlung erzeugt hat.
            var res = await _pgnImport.ReprocessFromStoredSourceAsync(bookId,
                playFromStartPosition: ownCourse, CancellationToken.None);
            result.Reprocessed++;
            result.UpdatedLines += res.Updated;
        }
        catch (Exception ex)
        {
            // NICHT als "übersprungen" verbuchen: das Buch behält seine ImportVersion, bleibt
            // also im „Aktualisieren (N)"-Banner stehen. Nur ein eigener Fehler-Zähler macht
            // den Unterschied zwischen „nichts zu tun" und „kaputt" nach außen sichtbar.
            result.Failed++;
            _logger.LogWarning(ex,
                "Course-Reprocess: Buch {FileName} (Id {BookId}) konnte nicht neu aufbereitet werden — bleibt veraltet",
                fileName, bookId);
        }
        finally
        {
            // Der Lauf hat EINEN DbContext (ReprocessLauncher-Scope). Ohne das blieben Buch, Roh-PGN
            // (bis 6 MB) und alle Linien jedes Buchs bis zum Ende getrackt — und nach einem
            // gescheiterten SaveChanges risse der kaputte Tracker-Zustand das NÄCHSTE Buch mit.
            // Unbedenklich: `stale` ist eine Projektion, nach dem Buch braucht niemand mehr etwas
            // aus dem Tracker.
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>Cache-Zweig für EIN Buch (<see cref="StaleAction.Cache"/>), isoliert: zählt Reprocessed/RebuiltFromCache bzw. Skipped/Failed.</summary>
    private async Task ReprocessFromCacheAsync(int bookId, string fileName, bool ownCourse, ReprocessResultDto result)
    {
        // Chessable mit oids: Zugtexte aus dem geteilten Linien-Cache (aktuelle piratechess-Logik),
        // dann dieselbe In-place-Aufbereitung wie lokal. Läuft auch bei localOnly (kein Chessable-
        // Kontakt) und auch mit Chessable:Enabled=false. Je Buch isoliert wie der lokale Zweig.
        try
        {
            var rebuilt = await RebuildFromCacheAsync(bookId, playFromStartPosition: ownCourse);
            if (rebuilt is { } r)
            {
                result.Reprocessed++;
                result.RebuiltFromCache++;
                result.CacheLinesReplaced += r.Replaced;
                result.UpdatedLines += r.Updated;
            }
            else
            {
                // Nichts aus dem Cache übernommen (leer, anderer Server, piratechess weg): das Buch bleibt
                // veraltet und wird beim nächsten „Aktualisieren" erneut versucht.
                result.Skipped++;
            }
        }
        catch (Exception ex)
        {
            // Portion geworfen (piratechess nicht erreichbar) oder Import gescheitert: NICHTS wurde
            // geschrieben, das Buch behält seine Version — wie ein Fehler im lokalen Zweig.
            result.Failed++;
            _logger.LogWarning(ex,
                "Course-Reprocess: Buch {FileName} (Id {BookId}) konnte nicht aus dem Linien-Cache erneuert werden — bleibt veraltet",
                fileName, bookId);
        }
        finally
        {
            _db.ChangeTracker.Clear();   // wie im lokalen Zweig: nach dem Buch nichts mehr im Tracker
        }
    }

    /// <summary>
    /// Cache-Weg für EIN Buch (<see cref="StaleAction.Cache"/>): Zugtexte aller Linien aus dem geteilten
    /// piratechess-Linien-Cache, Header aus dem gespeicherten PGN, dann In-place-Aufbereitung über
    /// <see cref="PgnImportService.ImportFileAsync"/> (lädt das Buch selbst, schreibt den Text nur, wenn er sich
    /// unterscheidet, setzt <c>ImportVersion</c>).
    /// <para>Erst prüfen, dann schreiben: gibt es keine einzige gecachte Linie, wird nichts geholt; wirft eine
    /// Portion, wird nichts geschrieben — kein halb erneuerter Kurs mit hochgesetzter Version.</para>
    /// </summary>
    /// <returns>null = nichts aus dem Cache übernommen, das Buch bleibt veraltet.</returns>
    private async Task<(int Replaced, int Updated)?> RebuildFromCacheAsync(int bookId, bool playFromStartPosition)
    {
        if (_cachedLines is null) return null;

        // Den Text genau dieses Buchs — er wird umgeschrieben (erlaubte Include-Stelle, BookSourceIncludeGuardTests).
        // AsNoTracking: ImportFileAsync lädt das Buch getrackt ein zweites Mal; so hängt nur EINE Instanz im Tracker.
        // Alle Aufrufe mit CancellationToken.None: der Lauf ist fire-and-forget (ReprocessLauncher), Wegnavigieren
        // darf ihn nicht mitten im Buch abbrechen.
        var book = await _db.Books.AsNoTracking().Include(b => b.Source)
            .FirstOrDefaultAsync(b => b.Id == bookId, CancellationToken.None)
            ?? throw new KeyNotFoundException($"Book {bookId} not found.");
        var source = book.Source?.SourcePgn;
        if (string.IsNullOrEmpty(source)) return null;

        var oids = CachedSourceRebuild.OidsOf(source);
        if (oids.Count == 0) return null;

        // Der Linien-Cache füllt nur Linien DIESES Kurses (piratechess ordnet jede Linie ihrem Kurs zu) — ohne bid aus dem Dateinamen
        // (chessable-u{uid}-{bid}.pgn) gibt es nichts zu fragen.
        if (!TryParseBid(book.FileName, out var bid))
        {
            _logger.LogInformation(
                "Course-Reprocess: Buch {FileName} (Id {BookId}) — Kurs-Id nicht aus dem Dateinamen lösbar, bleibt veraltet",
                book.FileName, bookId);
            return null;
        }

        // Ein billiger Existenz-Aufruf vorab: ein Server ohne (diesen) Cache soll nicht Dutzende teure
        // PGN-Abfragen absetzen, um am Ende nichts zu haben. Weich — piratechess weg liefert hier „nichts".
        var cached = await _cachedLines.GetCachedLineOidsAsync(bid, oids, CancellationToken.None);
        var wanted = oids.Where(cached.Contains).ToList();
        if (wanted.Count == 0)
        {
            _logger.LogInformation(
                "Course-Reprocess: Buch {FileName} (Id {BookId}) — keine der {Total} Linien im Linien-Cache, bleibt veraltet",
                book.FileName, bookId, oids.Count);
            // Merken, damit Banner und Liste das Buch als „braucht Re-Import" führen statt es bei jedem Aufruf als
            // aktualisierbar anzubieten (Book.CacheMissAt). UpdatedAt bleibt unberührt — ein neuer Import hebt die
            // Markierung über genau dieses Feld wieder auf.
            var tracked = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, CancellationToken.None);
            if (tracked != null)
            {
                tracked.CacheMissAt = DateTime.UtcNow > tracked.UpdatedAt ? DateTime.UtcNow : tracked.UpdatedAt;
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            return null;
        }

        var mode = CachedSourceRebuild.ModeFor(source);
        var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var portion in wanted.Chunk(CacheRebuildBatchSize))
            foreach (var (oid, pgn) in await _cachedLines.GetCachedLinePgnsAsync(bid, portion, mode, CancellationToken.None))
                fresh[oid] = pgn;

        var rebuilt = CachedSourceRebuild.Rebuild(source, fresh);
        _logger.LogInformation(
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
        var updatedAt = await _db.Books.Where(b => b.Id == bookId).Select(b => (DateTime?)b.UpdatedAt)
            .FirstOrDefaultAsync(CancellationToken.None);
        if (updatedAt != book.UpdatedAt)
        {
            _logger.LogInformation(
                "Course-Reprocess: Buch {FileName} (Id {BookId}) wurde während der Cache-Abfragen geändert — nichts geschrieben, bleibt veraltet",
                book.FileName, bookId);
            return null;
        }

        var res = await _pgnImport.ImportFileAsync(book.FileName, rebuilt.Pgn, CancellationToken.None,
            playFromStartPosition: playFromStartPosition);
        return (rebuilt.Replaced, res.Updated);
    }

    // ===== Repertoires (RepertoireReprocess) =====

    /// <summary>Status der veralteten Repertoires — dieselbe Vierteilung wie bei den Kursen (<see cref="RepertoireReprocess.GetStatusAsync"/>).</summary>
    public Task<ReprocessStatusDto> GetRepertoireStatusAsync(int userId, bool isAdmin = false, CancellationToken ct = default) =>
        _repertoires.GetStatusAsync(userId, isAdmin, ct);

    /// <summary>Veraltete Repertoires neu aufbereiten (<see cref="RepertoireReprocess.ReprocessAsync"/>).</summary>
    /// <param name="localOnly">true = ohne Chessable-Abruf („Aus Cache"): Versions-Mark und Linien-Cache-Weg; false = zusätzlich
    /// Chessable-Repertoires ohne oids frisch holen („Alle").</param>
    public Task<ReprocessResultDto> ReprocessRepertoiresAsync(int userId, bool isAdmin = false, bool localOnly = false, CancellationToken ct = default) =>
        _repertoires.ReprocessAsync(userId, isAdmin, localOnly, ct);

    // ===== Helpers =====

    /// <summary>Ein Buch kann (vollständig) per Chessable-Re-Fetch aufbereitet werden, wenn es als
    /// Chessable-Import erkennbar ist UND sich die bid aus dem Dateinamen lösen lässt — sonst bleibt
    /// nur lokales Reprocess (mit Quelle) bzw. manueller Re-Import. Muss zur Verzweigung in
    /// <see cref="ReprocessCoursesAsync"/> passen, damit Status-Zählung und Ausführung übereinstimmen.</summary>
    private StaleAction ActionFor(bool hasSource, bool sourceModern, string? tags, string fileName)
        => StaleContentRule.ActionForBook(hasSource, sourceModern, tags, fileName, _chessableEnabled);

    private static bool CanRefetch(string? tags, string fileName) =>
        StaleContentRule.CanRefetch(tags, fileName);

    private static bool IsChessable(string? tags, string fileName) =>
        StaleContentRule.IsChessable(tags, fileName);

    /// <summary>Holt die Chessable-bid aus dem konventionellen Buch-Dateinamen <c>chessable-u{uid}-{bid}.pgn</c>.</summary>
    private static bool TryParseBid(string fileName, out string bid) =>
        StaleContentRule.TryParseBid(fileName, out bid);

    [GeneratedRegex(@"^chessable-(\d+)\.pgn$", RegexOptions.IgnoreCase)]
    private static partial Regex RepertoireBidRegex();

    /// <summary>Chessable-bid eines Repertoires: bevorzugt <see cref="Repertoire.ChessableCourseId"/>,
    /// sonst aus einem Repertoire-Dateinamen <c>chessable-{bid}.pgn</c> (Altbestand ohne gesetzte CourseId,
    /// z. B. vor dem [Site]-Auto-Extract importiert) — aus den NAMEN, nie aus dem Inhalt. null ⇒ nicht auflösbar.</summary>
    internal static string? ResolveRepertoireBid(string? chessableCourseId, IEnumerable<string> fileNames)
    {
        if (!string.IsNullOrWhiteSpace(chessableCourseId)) return chessableCourseId;
        foreach (var name in fileNames)
        {
            var m = RepertoireBidRegex().Match(name);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }
}
