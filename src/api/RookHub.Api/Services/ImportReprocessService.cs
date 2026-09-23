using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

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
    /// <summary>Welche der oids liegen im Cache (nur Existenz, billig). Weich: Fehler → leere Menge.</summary>
    Task<HashSet<string>> GetCachedLineOidsAsync(IReadOnlyCollection<string> oids, CancellationToken ct = default);

    /// <summary>PGN je gecachter oid, mit der AKTUELLEN piratechess-Logik erzeugt. Nicht gecachte oids fehlen;
    /// ein Verbindungsfehler WIRFT.</summary>
    Task<Dictionary<string, string>> GetCachedLinePgnsAsync(IEnumerable<string> oids, string mode = "None", CancellationToken ct = default);
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

    /// <summary>Wie lange ein nicht-cachebarer (truncated) Kurs nach einem erfolglosen Re-Fetch im
    /// automatischen Massen-Reprocess übersprungen wird, bevor er erneut versucht wird. Ein solcher
    /// Kurs wird von piratechess nicht gecacht und würde sonst bei JEDEM „Update all" komplett neu von
    /// Chessable geholt (Hunderte Line-Fetches → Block-Risiko). Ein gezielter Admin-Re-Import umgeht das.</summary>
    private static readonly TimeSpan IncompleteRefetchBackoff = TimeSpan.FromHours(24);

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
        _chessableEnabled = configuration?.GetValue("Chessable:Enabled", true) ?? true;
        _db = db;
        _pgnImport = pgnImport;
        _chessableImport = chessableImport;
        _cachedLines = cachedLines;
        _logger = logger;
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
        var stale = await books
            .Where(b => b.ImportVersion < ImportPipeline.CurrentVersion)
            .Select(b => new
            {
                HasSource = b.Source.SourcePgn != null && b.Source.SourcePgn != "",
                // „Modern": Quelle enthält bereits ALLES, was die aktuelle Pipeline aus dem Quell-PGN zieht —
                // maßgeblich der jüngste Marker [ChessableOid] (piratechess ≥ v1.0.39). Nur dann reicht ein
                // lokaler Re-Parse (SQL-LIKE, lädt das große PGN NICHT). Eine ältere Quelle mit zwar [%alt]/
                // [%info], aber OHNE [ChessableOid] ist NICHT modern → braucht einen Chessable-Re-Fetch, damit
                // die oids (Grundlage der Fortschritts-Overlays) reinkommen.
                SourceModern = b.Source.SourcePgn != null && b.Source.SourcePgn.Contains("[ChessableOid"),
                b.Tags, b.FileName,
            })
            .ToListAsync(ct);

        // Re-Fetch nur noch für Chessable-Kurse, deren gespeicherte Quelle die Marker NOCH NICHT enthält
        // (alte Abrufe). Chessable-Kurse mit „moderner" Quelle kommen aus dem Linien-Cache, alle
        // Nicht-Chessable mit Quelle LOKAL aus dem gespeicherten PGN (beides ohne Chessable-Kontakt).
        // EINE Regel für Anzeige und Ausführung (ActionFor) — laufen sie auseinander, verspricht das
        // Banner eine Aktion, die der Lauf dann überspringt, und es bleibt für immer stehen.
        var actions = stale.Select(b => ActionFor(b.HasSource, b.SourceModern, b.Tags, b.FileName)).ToList();
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
        };
    }

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
        var stale = await ManageableBooks(userId, isAdmin)
            .Where(b => b.ImportVersion < ImportPipeline.CurrentVersion)
            .Select(b => new
            {
                b.Id, b.FileName, b.DisplayName, b.Tags, b.OwnerUserId,
                HasSource = b.Source.SourcePgn != null && b.Source.SourcePgn != "",
                SourceModern = b.Source.SourcePgn != null && b.Source.SourcePgn.Contains(StaleContentRule.ModernMarker),
            })
            .ToListAsync(ct);

        var result = new ReprocessResultDto();
        var refetch = new List<RefetchCandidate>();
        foreach (var book in stale)
        {
            var action = ActionFor(book.HasSource, book.SourceModern, book.Tags, book.FileName);
            if (action == StaleAction.Refetch)
            {
                // Chessable OHNE moderne Quelle: vollständiger Re-Fetch (die [ChessableOid] steht nicht im
                // alten gecachten PGN → ein lokales Reprocess könnte sie nie setzen).
                if (localOnly) continue; // Netz-Re-Fetch bewusst auslassen
                TryParseBid(book.FileName, out var bid);   // ActionFor hat die bid bereits geprüft
                refetch.Add(new RefetchCandidate(book.OwnerUserId ?? userId, bid, "book", book.DisplayName, null));
            }
            else if (action == StaleAction.Local)
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
                    var res = await _pgnImport.ReprocessFromStoredSourceAsync(book.Id,
                        playFromStartPosition: book.OwnerUserId != null, CancellationToken.None);
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
                        book.FileName, book.Id);
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
            else if (action == StaleAction.Cache)
            {
                // Chessable mit oids: Zugtexte aus dem geteilten Linien-Cache (aktuelle piratechess-Logik),
                // dann dieselbe In-place-Aufbereitung wie lokal. Läuft auch bei localOnly (kein Chessable-
                // Kontakt) und auch mit Chessable:Enabled=false. Je Buch isoliert wie der lokale Zweig.
                try
                {
                    var rebuilt = await RebuildFromCacheAsync(book.Id, playFromStartPosition: book.OwnerUserId != null);
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
                        book.FileName, book.Id);
                }
                finally
                {
                    _db.ChangeTracker.Clear();   // wie im lokalen Zweig: nach dem Buch nichts mehr im Tracker
                }
            }
            else
            {
                result.Skipped++; // keine Quelle, kein Re-Fetch → nur manueller Re-Import
            }
        }

        // Zentrales Einreihen (Batch-Cache, Backoff, Dedup, Admin-Bypass, kein Abbruch) — geteilt mit Repertoiren.
        await EnqueueRefetchesAsync(refetch, isAdmin, result);

        _logger.LogInformation(
            "Course-Reprocess für User {UserId} (admin={IsAdmin}, localOnly={LocalOnly}): {Reprocessed} aufbereitet ({UpdatedLines} Linien), davon {RebuiltFromCache} aus dem Linien-Cache ({CacheLinesReplaced} Linien ersetzt), {Enqueued} eingereiht, {Skipped} übersprungen, {Failed} fehlgeschlagen",
            userId, isAdmin, localOnly, result.Reprocessed, result.UpdatedLines, result.RebuiltFromCache, result.CacheLinesReplaced,
            result.Enqueued, result.Skipped, result.Failed);
        return result;
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

        // Ein billiger Existenz-Aufruf vorab: ein Server ohne (diesen) Cache soll nicht Dutzende teure
        // PGN-Abfragen absetzen, um am Ende nichts zu haben. Weich — piratechess weg liefert hier „nichts".
        var cached = await _cachedLines.GetCachedLineOidsAsync(oids, CancellationToken.None);
        var wanted = oids.Where(cached.Contains).ToList();
        if (wanted.Count == 0)
        {
            _logger.LogInformation(
                "Course-Reprocess: Buch {FileName} (Id {BookId}) — keine der {Total} Linien im Linien-Cache, bleibt veraltet",
                book.FileName, bookId, oids.Count);
            return null;
        }

        var mode = CachedSourceRebuild.ModeFor(source);
        var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var portion in wanted.Chunk(CacheRebuildBatchSize))
            foreach (var (oid, pgn) in await _cachedLines.GetCachedLinePgnsAsync(portion, mode, CancellationToken.None))
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

    /// <summary>Kandidat für einen Chessable-Re-Fetch — Kurs ODER Repertoire.</summary>
    private readonly record struct RefetchCandidate(int OwnerId, string Bid, string Target, string Name, int? TargetRepertoireId);

    /// <summary>
    /// ZENTRALES Einreihen von Chessable-Re-Fetch-Jobs für Kurse UND Repertoires. Hier liegen — an EINER
    /// Stelle statt je Pfad dupliziert — alle Vorsichtsmaßnahmen: EINMALIGER Batch-Cache-Abruf statt teurem
    /// Einzel-Check je Kurs; kein Abbruch am Request-Token (Wegnavigieren darf das Einreihen nicht killen →
    /// <see cref="CancellationToken.None"/>); Backoff für nicht-cachebare (truncated) Kurse, damit sie
    /// Chessable nicht bei jedem Lauf neu fluten; Admin-Eigentums-Bypass (getHomeData listet nur einen Teil
    /// der Bibliothek). Dedup gegen bereits laufende Importe steckt in <c>EnqueueReimportAsync</c>.
    /// Zählt Enqueued/Skipped in <paramref name="result"/>.
    /// </summary>
    private async Task EnqueueRefetchesAsync(IReadOnlyList<RefetchCandidate> candidates, bool isAdmin, ReprocessResultDto result, IReadOnlySet<string>? knownCachedBids = null)
    {
        if (candidates.Count == 0) return;
        if (!_chessableEnabled)
        {
            // Sicherheitsnetz: ohne die eigenen Lanes würde ein angelegter Auftrag nie abgearbeitet — er
            // stünde als „läuft" in der Liste und blockierte über die Dedup-Regel jeden späteren Versuch.
            result.Skipped += candidates.Count;
            return;
        }

        // Cache-Status ALLER Kandidaten EINMAL en bloc (1 piratechess-Aufruf) statt je Kurs ein teurer
        // Einzel-Check, der den ganzen Cache-Blob lädt+entpackt. Der Aufrufer kann die Menge bereits
        // ermittelt haben (Repertoire-Reprocess braucht sie schon für die Bearer-lose Cache-Entscheidung).
        var cachedBids = knownCachedBids ?? await _chessableImport.GetCachedBidsAsync(CancellationToken.None);

        foreach (var c in candidates)
        {
            var cached = cachedBids.Contains(c.Bid);
            // Backoff: ein nicht-cachebarer (truncated) Kurs würde bei jedem Lauf komplett neu von Chessable
            // geholt. Wurde er kürzlich schon (erfolglos = weiterhin nicht gecacht) geholt, jetzt überspringen.
            if (!cached)
            {
                var lastDone = await _db.ChessableImports
                    .Where(i => i.Bid == c.Bid && i.Status == ChessableImportStatus.Completed && i.CompletedAt != null)
                    .OrderByDescending(i => i.CompletedAt)
                    .Select(i => i.CompletedAt)
                    .FirstOrDefaultAsync(CancellationToken.None);
                if (lastDone.HasValue && lastDone.Value > DateTime.UtcNow - IncompleteRefetchBackoff)
                {
                    result.Skipped++;
                    continue;
                }
            }

            // trustOwnership=isAdmin: getHomeData listet nur einen Teil der Bibliothek → sonst würden eigene,
            // längst importierte Kurse fälschlich als „nicht besessen" abgewiesen; Admins dürfen ohnehin jeden
            // Kurs holen (Nicht-Admin bleibt geprüft = v0.203.9-Schutz gegen Cached-Content-Diebstahl).
            var importId = await _chessableImport.EnqueueReimportAsync(
                c.OwnerId, c.Bid, c.Target, c.Name, targetRepertoireId: c.TargetRepertoireId,
                knownCached: cached, trustOwnership: isAdmin, ct: CancellationToken.None);
            if (importId != null) result.Enqueued++;
            else result.Skipped++;
        }
    }

    // ===== Repertoires =====

    /// <summary>
    /// Was Status und Lauf über ein veraltetes Repertoire wissen müssen — OHNE seinen PGN-Text. Von den Dateinamen
    /// stehen nur die mit <c>chessable-</c> dabei (Herkunft + bid); ob eine Datei oids trägt, kommt fertig aus SQL.
    /// </summary>
    private sealed record StaleRepertoire(int Id, int UserId, string Name, string? ChessableCourseId,
        IReadOnlyList<string> ChessableFileNames, bool SourceModern)
    {
        /// <summary>Chessable-Herkunft: die hinterlegte Kurs-Id ODER ein Dateiname aus dem Chessable-Import —
        /// dieselbe Regel wie die (!)-Markierung der Liste (<c>RepertoireService.MarkNeedsReimportAsync</c>).</summary>
        public bool IsChessable => !string.IsNullOrEmpty(ChessableCourseId) || ChessableFileNames.Count > 0;

        /// <summary>bid für einen Re-Fetch; null ⇒ nicht auflösbar (siehe <see cref="ResolveRepertoireBid"/>).</summary>
        public string? Bid => ResolveRepertoireBid(ChessableCourseId, ChessableFileNames);
    }

    /// <summary>Verwaltbare Repertoires: Admin = die ALLER User (wie bei Kursen), sonst die eigenen.</summary>
    private IQueryable<Repertoire> ManageableRepertoires(int userId, bool isAdmin) =>
        isAdmin ? _db.Repertoires : _db.Repertoires.Where(r => r.UserId == userId);

    /// <summary>
    /// Die veralteten Repertoires als PROJEKTION — ob eine Datei <c>[ChessableOid]</c> trägt, fragt ein SQL-<c>LIKE</c>,
    /// der Text selbst wird nicht übertragen. Vorher luden Status und Lauf jedes Repertoire mit
    /// <c>.Include(r =&gt; r.Files)</c>, also JEDEN PGN-Text (Prod trägt ~250 MB Chessable-PGN), nur um Herkunft und
    /// diesen Marker zu prüfen — dieselbe Klasse Fehler wie bei den Büchern (0.508.3: 23 GB RAM auf Prod). Den Text lädt
    /// nur noch der Cache-Weg, und zwar Datei für Datei (<see cref="RebuildRepertoireFromCacheAsync"/>).
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
            })
            .ToListAsync(ct);
        return rows.Select(r => new StaleRepertoire(r.Id, r.UserId, r.Name, r.ChessableCourseId, r.ChessableFileNames, r.SourceModern))
            .ToList();
    }

    /// <summary>Dieselbe Regel für Status, Lauf und die (!)-Markierung der Liste (<see cref="StaleContentRule.ActionForRepertoire"/>).</summary>
    private StaleAction ActionFor(StaleRepertoire r) =>
        StaleContentRule.ActionForRepertoire(r.IsChessable, r.SourceModern, _chessableEnabled);

    public async Task<ReprocessStatusDto> GetRepertoireStatusAsync(int userId, bool isAdmin = false, CancellationToken ct = default)
    {
        var repertoires = ManageableRepertoires(userId, isAdmin);
        var total = await repertoires.CountAsync(ct);
        var stale = await LoadStaleRepertoiresAsync(repertoires, ct);
        // Dieselbe Vierteilung wie bei den Kursen — „Manual" ist der Showstopper, der in der LISTE als (!) am
        // Repertoire steht statt als anonyme Zahl im Banner. Anzeige = Ausführung: der Lauf nimmt dieselbe Regel.
        var actions = stale.Select(ActionFor).ToList();
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
        };
    }

    /// <param name="localOnly">true = ohne Chessable-Abruf („Aus Cache"): Versions-Mark UND der Linien-Cache-Weg
    /// (<see cref="StaleAction.Cache"/>) — wie bei Kursen gehört er dazu, denn „ohne Chessable-Abruf" ist genau das.
    /// false = zusätzlich Chessable-Repertoires ohne oids frisch holen („Alle").</param>
    public async Task<ReprocessResultDto> ReprocessRepertoiresAsync(int userId, bool isAdmin = false, bool localOnly = false, CancellationToken ct = default)
    {
        // Nur die Projektion (kein PGN-Text, nichts getrackt) — wie bei den Kursen. Re-Fetch je Repertoire läuft mit
        // dem Bearer des jeweiligen Owners (gecachte Kurse laufen ohnehin ohne Bearer durch).
        var stale = await LoadStaleRepertoiresAsync(ManageableRepertoires(userId, isAdmin), ct);

        var result = new ReprocessResultDto();
        var refetch = new List<RefetchCandidate>();
        var versionMark = new List<int>();
        var bearerUsers = await _db.ChessableCredentials.Select(c => c.UserId).ToHashSetAsync(ct);
        // Ein gecachter Kurs ist AUCH ohne Bearer holbar (piratechess liefert ihn aus dem Rohdaten-Cache) —
        // aber nur im Admin-Reprocess (trustOwnership; sonst wäre es ein Eigentums-Bypass, [[0.203.9]]).
        // Cache-Status daher NUR abrufen, wenn es überhaupt einen bearer-losen Re-Fetch-Kandidaten gibt
        // (Refetch setzt den eigenen Chessable-Weg voraus).
        var needCache = !localOnly && isAdmin && stale.Any(r =>
            ActionFor(r) == StaleAction.Refetch && r.Bid != null && !bearerUsers.Contains(r.UserId));
        var cachedBids = needCache
            ? await _chessableImport.GetCachedBidsAsync(CancellationToken.None)
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
                // Chessable mit oids: Zugtexte aus dem geteilten Linien-Cache (aktuelle piratechess-Logik), dann der
                // Versions-Mark. Läuft auch bei localOnly (kein Chessable-Kontakt) und mit Chessable:Enabled=false.
                // Je Repertoire isoliert wie bei den Kursen: EIN kaputtes darf den Lauf nicht mitreißen.
                try
                {
                    if (await RebuildRepertoireFromCacheAsync(r.Id, r.Name) is { } replaced)
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
                    _logger.LogWarning(ex,
                        "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}) konnte nicht aus dem Linien-Cache erneuert werden — bleibt veraltet",
                        r.Name, r.Id);
                }
                finally
                {
                    // Die Dateien EINES Repertoires hingen bis hierher im Tracker — vor dem nächsten wieder raus
                    // (Prod: ~250 MB Chessable-PGN), und ein gescheitertes SaveChanges risse sonst das nächste mit.
                    _db.ChangeTracker.Clear();
                }
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
                    refetch.Add(new RefetchCandidate(r.UserId, bid, "repertoire", r.Name, r.Id));
                    continue;
                }
                // Chessable, aber weder Bearer noch (admin-)gecacht (oder keine bid lösbar) → nie automatisch holbar.
                // Statt es ewig im Banner hängen zu lassen, als aktuell markieren (ein echter Re-Import erfordert
                // erst, dass der Besitzer einen Bearer hinterlegt). Fällt in den Versions-Mark unten.
            }
            // Nicht-Chessable / Chessable-ohne-Bearer-und-nicht-gecacht → Versions-Mark.
            versionMark.Add(r.Id);
        }

        // Versions-Mark gezielt: nur die Repertoire-Zeilen, OHNE Dateien — vorher hingen hier die PGN-Texte aller
        // veralteten Repertoires bis zum Ende des Laufs im Tracker. Erst nach der Schleife geladen, weil der
        // Cache-Zweig den Tracker je Repertoire leert.
        if (versionMark.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var rep in await _db.Repertoires.Where(x => versionMark.Contains(x.Id)).ToListAsync(CancellationToken.None))
            {
                rep.ImportVersion = ImportPipeline.CurrentVersion;
                rep.UpdatedAt = now;
                result.Reprocessed++;
            }
        }

        // Zentrales Einreihen (Batch-Cache, Backoff, Dedup, Admin-Bypass, kein Abbruch) — geteilt mit Kursen.
        // Die bereits ermittelte Cache-Menge weiterreichen (spart einen zweiten Abruf); sonst holt sie der
        // zentrale Pfad selbst (z. B. reine Bearer-Kandidaten, für die needCache nicht griff).
        await EnqueueRefetchesAsync(refetch, isAdmin, result, needCache ? cachedBids : null);
        await _db.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation(
            "Repertoire-Reprocess für User {UserId} (admin={IsAdmin}, localOnly={LocalOnly}): {Reprocessed} aufbereitet, davon {RebuiltFromCache} aus dem Linien-Cache ({CacheLinesReplaced} Linien ersetzt), {Enqueued} eingereiht, {Skipped} übersprungen, {Failed} fehlgeschlagen",
            userId, isAdmin, localOnly, result.Reprocessed, result.RebuiltFromCache, result.CacheLinesReplaced,
            result.Enqueued, result.Skipped, result.Failed);
        return result;
    }

    /// <summary>
    /// Cache-Weg für EIN Repertoire (<see cref="StaleAction.Cache"/>) — dieselben Regeln wie
    /// <see cref="RebuildFromCacheAsync"/> für Kurse, nur ohne Import danach (der Trainer wertet das PGN live aus):
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
    private async Task<int?> RebuildRepertoireFromCacheAsync(int repertoireId, string name)
    {
        if (_cachedLines is null) return null;

        // Den Stand VOR dem Lesen der Texte merken: wer danach schreibt (Live-Append der Extension, Upload, Löschen
        // einer Datei, die Bereinigung), setzt Repertoire.UpdatedAt — und das fällt unten auf. Alle Aufrufe mit
        // CancellationToken.None: der Lauf ist fire-and-forget (ReprocessLauncher), Wegnavigieren darf ihn nicht
        // mitten im Repertoire abbrechen.
        var loadedAt = await RepertoireUpdatedAtAsync(repertoireId);
        if (loadedAt is null) return null;   // inzwischen gelöscht

        // Die Texte Datei für Datei, und nur Dateien, die überhaupt oids tragen (LIKE, ohne den Text zu übertragen).
        // Sie bleiben bis zum Schreiben getrackt — die EINES Repertoires; der Aufrufer leert den Tracker danach.
        var fileIds = await _db.RepertoireFiles
            .Where(f => f.RepertoireId == repertoireId && f.PgnContent.Contains(StaleContentRule.ModernMarker))
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .ToListAsync(CancellationToken.None);
        var files = new List<(RepertoireFile File, string Mode, IReadOnlyList<string> Oids)>();
        foreach (var id in fileIds)
        {
            var file = await _db.RepertoireFiles.FirstOrDefaultAsync(f => f.Id == id, CancellationToken.None);
            if (file is null) continue;
            // Modus je DATEI: ein aus einem Kurs umgewandeltes Repertoire trägt die Trainingsmarker, ein von
            // Chessable geholtes nicht — beide können im selben Repertoire liegen.
            files.Add((file, CachedSourceRebuild.ModeFor(file.PgnContent), CachedSourceRebuild.OidsOf(file.PgnContent)));
        }
        var oids = files.SelectMany(f => f.Oids).Distinct(StringComparer.Ordinal).ToList();
        if (oids.Count == 0) return null;

        // EIN billiger Existenz-Aufruf fürs ganze Repertoire: ein Server ohne (diesen) Cache soll nicht Dutzende teure
        // PGN-Abfragen absetzen, um am Ende nichts zu haben. Weich — piratechess weg liefert hier „nichts".
        var cached = await _cachedLines.GetCachedLineOidsAsync(oids, CancellationToken.None);
        if (!oids.Any(cached.Contains))
        {
            _logger.LogInformation(
                "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}) — keine der {Total} Linien im Linien-Cache, bleibt veraltet",
                name, repertoireId, oids.Count);
            return null;
        }

        // Je Modus eigene Abfragen (derselbe Linien-Inhalt kommt je Modus anders heraus), in Portionen wie bei Kursen.
        var freshByMode = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var group in files.GroupBy(f => f.Mode, StringComparer.Ordinal))
        {
            var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
            var wanted = group.SelectMany(f => f.Oids).Where(cached.Contains).Distinct(StringComparer.Ordinal).ToList();
            foreach (var portion in wanted.Chunk(CacheRebuildBatchSize))
                foreach (var (oid, pgn) in await _cachedLines.GetCachedLinePgnsAsync(portion, group.Key, CancellationToken.None))
                    fresh[oid] = pgn;
            freshByMode[group.Key] = fresh;
        }

        var rebuilt = files.Select(f => (f.File, Result: CachedSourceRebuild.Rebuild(f.File.PgnContent, freshByMode[f.Mode]))).ToList();
        var replaced = rebuilt.Sum(f => f.Result.Replaced);
        _logger.LogInformation(
            "Repertoire-Reprocess: Repertoire „{Name}“ (Id {RepertoireId}, {Files} Dateien mit oids, Modus {Modes}) — {Replaced} von {Total} Linien aus dem Cache, {Missing} nicht gecacht, {ModeMismatch} Modus-Konflikt, {Conflicts} Konflikt, {Hidden} ausgeblendet",
            name, repertoireId, files.Count, string.Join('/', freshByMode.Keys), replaced, rebuilt.Sum(f => f.Result.Total),
            rebuilt.Sum(f => f.Result.Missing), rebuilt.Sum(f => f.Result.ModeMismatch), rebuilt.Sum(f => f.Result.Conflicts),
            rebuilt.Sum(f => f.Result.Hidden));
        // Keine Linie übernommen (etwa alle im falschen Modus): nicht als erneuert ausgeben — sonst stünde das
        // Repertoire auf der aktuellen Version, ohne dass sich etwas geändert hat, und käme nie wieder dran.
        if (replaced == 0) return null;

        // Ein großes Repertoire braucht Dutzende Cache-Abfragen, also Minuten. Hat in der Zeit ein anderer Weg
        // geschrieben (die Extension hängt live Linien an), fehlten dessen Linien im umgeschriebenen Text, und dieser
        // Lauf überschriebe sie. Alle Schreiber des Repertoire-PGN setzen UpdatedAt — steht es anders als beim Laden,
        // lieber nichts schreiben: das Repertoire bleibt veraltet und kommt beim nächsten „Aktualisieren" dran.
        if (await RepertoireUpdatedAtAsync(repertoireId) != loadedAt)
        {
            _logger.LogInformation(
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
        var repertoire = await _db.Repertoires.FirstAsync(r => r.Id == repertoireId, CancellationToken.None);
        repertoire.ImportVersion = ImportPipeline.CurrentVersion;
        repertoire.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        return replaced;
    }

    private Task<DateTime?> RepertoireUpdatedAtAsync(int repertoireId) =>
        _db.Repertoires.Where(r => r.Id == repertoireId).Select(r => (DateTime?)r.UpdatedAt)
            .FirstOrDefaultAsync(CancellationToken.None);

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
    private static string? ResolveRepertoireBid(string? chessableCourseId, IEnumerable<string> fileNames)
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
