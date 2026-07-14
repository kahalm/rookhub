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
/// Neu-Aufbereitung („Reprocessing") veralteter Datensätze, wenn die Import-Pipeline weiterentwickelt
/// wurde (<see cref="ImportPipeline"/>). Datensätze mit <c>ImportVersion &lt; CurrentVersion</c> gelten
/// als veraltet; der Reprocess-Knopf je Sektion ruft diesen Service.
///
/// <para><b>Kurse/Bücher:</b> bevorzugt lokal aus dem gespeicherten Roh-PGN (<c>Book.SourcePgn</c>)
/// — verlustfrei und in-place (Match per LineId, Fortschritt/Statistik bleiben erhalten). Fehlt die
/// Quelle (Altbestand), wird für Chessable-Kurse ein Re-Fetch-Hintergrund-Job eingereiht
/// (<see cref="ChessableImportService.EnqueueReimportAsync"/>). Sonst: nur per manuellem Re-Import.</para>
///
/// <para><b>Repertoires:</b> speichern ihr Roh-PGN selbst und werten live aus — heute gibt es keine
/// abgeleiteten Daten zu erneuern; der Lauf markiert sie nur auf die aktuelle Version (zukunftssicher).</para>
/// </summary>
public partial class ImportReprocessService
{
    private readonly AppDbContext _db;
    private readonly PgnImportService _pgnImport;
    private readonly ICourseReimporter _chessableImport;
    private readonly ILogger<ImportReprocessService> _logger;

    /// <summary>Wie lange ein nicht-cachebarer (truncated) Kurs nach einem erfolglosen Re-Fetch im
    /// automatischen Massen-Reprocess übersprungen wird, bevor er erneut versucht wird. Ein solcher
    /// Kurs wird von piratechess nicht gecacht und würde sonst bei JEDEM „Update all" komplett neu von
    /// Chessable geholt (Hunderte Line-Fetches → Block-Risiko). Ein gezielter Admin-Re-Import umgeht das.</summary>
    private static readonly TimeSpan IncompleteRefetchBackoff = TimeSpan.FromHours(24);

    public ImportReprocessService(
        AppDbContext db,
        PgnImportService pgnImport,
        ICourseReimporter chessableImport,
        ILogger<ImportReprocessService> logger)
    {
        _db = db;
        _pgnImport = pgnImport;
        _chessableImport = chessableImport;
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
                HasSource = b.SourcePgn != null,
                // „Modern": Quelle enthält bereits ALLES, was die aktuelle Pipeline aus dem Quell-PGN zieht —
                // maßgeblich der jüngste Marker [ChessableOid] (piratechess ≥ v1.0.39). Nur dann reicht ein
                // lokaler Re-Parse (SQL-LIKE, lädt das große PGN NICHT). Eine ältere Quelle mit zwar [%alt]/
                // [%info], aber OHNE [ChessableOid] ist NICHT modern → braucht einen Chessable-Re-Fetch, damit
                // die oids (Grundlage der Fortschritts-Overlays) reinkommen.
                SourceModern = b.SourcePgn != null && b.SourcePgn.Contains("[ChessableOid"),
                b.Tags, b.FileName,
            })
            .ToListAsync(ct);

        // Re-Fetch nur noch für Chessable-Kurse, deren gespeicherte Quelle die Marker NOCH NICHT enthält
        // (alte Abrufe). Chessable-Kurse mit „moderner" Quelle + alle Nicht-Chessable mit Quelle werden
        // LOKAL aus dem gespeicherten PGN aufbereitet (kein Netz, umgeht Crash/Dedup/Bearer).
        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale.Count,
            Refetchable = stale.Count(b => CanRefetch(b.Tags, b.FileName) && !b.SourceModern),
            ReprocessableLocally = stale.Count(b => b.HasSource && (!CanRefetch(b.Tags, b.FileName) || b.SourceModern)),
            NeedsReimport = stale.Count(b => !b.HasSource && !CanRefetch(b.Tags, b.FileName)),
        };
    }

    /// <param name="localOnly">true = nur aus dem serverseitig gespeicherten Quell-PGN aufbereiten
    /// („Aus Cache"), KEIN Chessable-Re-Fetch übers Netz. false = zusätzlich Chessable-Altbestand
    /// ohne Quelle als Re-Fetch-Job einreihen („Alle").</param>
    public async Task<ReprocessResultDto> ReprocessCoursesAsync(int userId, bool isAdmin, bool localOnly = false, CancellationToken ct = default)
    {
        var stale = await ManageableBooks(userId, isAdmin)
            .Where(b => b.ImportVersion < ImportPipeline.CurrentVersion)
            .ToListAsync(ct);

        var result = new ReprocessResultDto();
        var refetch = new List<RefetchCandidate>();
        foreach (var book in stale)
        {
            if (IsChessable(book.Tags, book.FileName) && TryParseBid(book.FileName, out var bid)
                && !SourceHasModernMarkers(book.SourcePgn))
            {
                // Chessable OHNE moderne Quelle: vollständiger Re-Fetch (die [ChessableOid] steht nicht im
                // alten gecachten PGN → ein lokales Reprocess könnte sie nie setzen).
                if (localOnly) continue; // Netz-Re-Fetch bewusst auslassen
                refetch.Add(new RefetchCandidate(book.OwnerUserId ?? userId, bid, "book", book.DisplayName, null));
            }
            else if (!string.IsNullOrEmpty(book.SourcePgn))
            {
                // Lokal verlustfrei + in-place aus dem gespeicherten PGN (ImportFileAsync erkennt das
                // veraltete Buch). Gilt für Nicht-Chessable UND Chessable mit „moderner" Quelle
                // ([ChessableOid] bereits vorhanden) → kein Chessable-Kontakt, kein Crash/Dedup/Bearer.
                var res = await _pgnImport.ImportFileAsync(book.FileName, book.SourcePgn, CancellationToken.None);
                result.Reprocessed++;
                result.UpdatedLines += res.Updated;
            }
            else
            {
                result.Skipped++; // keine Quelle, kein Re-Fetch → nur manueller Re-Import
            }
        }

        // Zentrales Einreihen (Batch-Cache, Backoff, Dedup, Admin-Bypass, kein Abbruch) — geteilt mit Repertoiren.
        await EnqueueRefetchesAsync(refetch, isAdmin, result);

        _logger.LogInformation(
            "Course-Reprocess für User {UserId} (admin={IsAdmin}, localOnly={LocalOnly}): {Reprocessed} lokal ({UpdatedLines} Linien), {Enqueued} eingereiht, {Skipped} übersprungen",
            userId, isAdmin, localOnly, result.Reprocessed, result.UpdatedLines, result.Enqueued, result.Skipped);
        return result;
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
                    .Where(i => i.Bid == c.Bid && i.Status == "completed" && i.CompletedAt != null)
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

    public async Task<ReprocessStatusDto> GetRepertoireStatusAsync(int userId, bool isAdmin = false, CancellationToken ct = default)
    {
        // Admin sieht/aktualisiert die Repertoires ALLER User (wie bei Kursen); sonst nur die eigenen.
        var reps = await _db.Repertoires
            .Where(r => isAdmin || r.UserId == userId)
            .Include(r => r.Files)
            .ToListAsync(ct);
        var total = reps.Count;
        var stale = reps.Where(r => r.ImportVersion < ImportPipeline.CurrentVersion).ToList();
        // Re-Fetch nur für Chessable-Repertoires, deren gespeichertes PGN die [%alt]/[%info]-Marker NOCH
        // NICHT enthält. Ist es „modern" (bereits enthalten) bzw. Nicht-Chessable → reiner Versions-Mark.
        // Hinweis: „Aktualisieren" löst inzwischen JEDEN stale-Fall auf (Bearer-Re-Fetch, Cache-Re-Fetch
        // ohne Bearer, oder Versions-Mark), sodass das Banner danach immer leer wird.
        var refetchable = stale.Count(r => ResolveRepertoireBid(r) != null && !RepertoireSourceModern(r));
        return new ReprocessStatusDto
        {
            CurrentVersion = ImportPipeline.CurrentVersion,
            Total = total,
            Stale = stale.Count,
            ReprocessableLocally = stale.Count - refetchable,
            Refetchable = refetchable,
        };
    }

    /// <param name="localOnly">true = nur lokal aufbereitbare Repertoires („Aus Cache": Nicht-Chessable
    /// → reiner Versions-Mark), KEIN Chessable-Re-Fetch übers Netz. false = zusätzlich Chessable-Repertoires
    /// frisch holen („Alle").</param>
    public async Task<ReprocessResultDto> ReprocessRepertoiresAsync(int userId, bool isAdmin = false, bool localOnly = false, CancellationToken ct = default)
    {
        // Admin: alle User; sonst nur eigene. Re-Fetch je Repertoire läuft mit dem Bearer des jeweiligen
        // Owners (gecachte Kurse laufen ohnehin ohne Bearer durch).
        var stale = await _db.Repertoires
            .Where(r => (isAdmin || r.UserId == userId) && r.ImportVersion < ImportPipeline.CurrentVersion)
            .Include(r => r.Files)
            .ToListAsync(ct);

        var result = new ReprocessResultDto();
        var now = DateTime.UtcNow;
        var refetch = new List<RefetchCandidate>();
        var bearerUsers = await _db.ChessableCredentials.Select(c => c.UserId).ToHashSetAsync(ct);
        // Ein gecachter Kurs ist AUCH ohne Bearer holbar (piratechess liefert ihn aus dem Rohdaten-Cache) —
        // aber nur im Admin-Reprocess (trustOwnership; sonst wäre es ein Eigentums-Bypass, [[0.203.9]]).
        // Cache-Status daher NUR abrufen, wenn es überhaupt einen bearer-losen Chessable-Kandidaten gibt.
        var needCache = !localOnly && isAdmin && stale.Any(r =>
            ResolveRepertoireBid(r) is { } b && !RepertoireSourceModern(r) && !bearerUsers.Contains(r.UserId));
        var cachedBids = needCache
            ? await _chessableImport.GetCachedBidsAsync(CancellationToken.None)
            : new HashSet<string>();
        foreach (var r in stale)
        {
            var bid = ResolveRepertoireBid(r);
            if (bid != null && !RepertoireSourceModern(r))
            {
                // Chessable-Repertoire OHNE moderne Quelle = Re-Fetch-Kandidat.
                if (localOnly) continue; // „Aus Cache"-Modus: Netz-Re-Fetch bewusst auslassen (bleibt stale)
                if (bearerUsers.Contains(r.UserId) || (isAdmin && cachedBids.Contains(bid)))
                {
                    // Frisch holen (inkl. [%alt]) und IN-PLACE ins bestehende Repertoire schreiben (Id/
                    // Trainings-Fortschritt bleiben; Version steigt beim Job-Abschluss). Owner = r.UserId →
                    // richtiger Bearer bzw. — falls keiner — Cache-Re-Fetch (Admin, gecacht).
                    refetch.Add(new RefetchCandidate(r.UserId, bid, "repertoire", r.Name, r.Id));
                    continue;
                }
                // Chessable, aber weder Bearer noch (admin-)gecacht → nie automatisch holbar. Statt es ewig
                // im Banner hängen zu lassen, als aktuell markieren (ein echter Re-Import erfordert erst,
                // dass der Besitzer einen Bearer hinterlegt). Fällt in den Versions-Mark unten.
            }
            // Nicht-Chessable / „moderne" Quelle / Chessable-ohne-Bearer-und-nicht-gecacht → Versions-Mark.
            r.ImportVersion = ImportPipeline.CurrentVersion;
            r.UpdatedAt = now;
            result.Reprocessed++;
        }

        // Zentrales Einreihen (Batch-Cache, Backoff, Dedup, Admin-Bypass, kein Abbruch) — geteilt mit Kursen.
        // Die bereits ermittelte Cache-Menge weiterreichen (spart einen zweiten Abruf); sonst holt sie der
        // zentrale Pfad selbst (z. B. reine Bearer-Kandidaten, für die needCache nicht griff).
        await EnqueueRefetchesAsync(refetch, isAdmin, result, needCache ? cachedBids : null);
        await _db.SaveChangesAsync(CancellationToken.None);
        return result;
    }

    // ===== Helpers =====

    /// <summary>Ein Buch kann (vollständig) per Chessable-Re-Fetch aufbereitet werden, wenn es als
    /// Chessable-Import erkennbar ist UND sich die bid aus dem Dateinamen lösen lässt — sonst bleibt
    /// nur lokales Reprocess (mit Quelle) bzw. manueller Re-Import. Muss zur Verzweigung in
    /// <see cref="ReprocessCoursesAsync"/> passen, damit Status-Zählung und Ausführung übereinstimmen.</summary>
    private static bool CanRefetch(string? tags, string fileName) =>
        IsChessable(tags, fileName) && TryParseBid(fileName, out _);

    /// <summary>Enthält die gespeicherte Quelle bereits ALLES, was die aktuelle Pipeline aus dem Quell-PGN
    /// zieht? Maßgeblich ist der jüngste quell-abhängige Marker <c>[ChessableOid]</c> (piratechess ≥ v1.0.39,
    /// Basis der Fortschritts-Overlays). Nur dann ist ein LOKALES Reprocess vollständig — kein Chessable-
    /// Re-Fetch nötig. Eine ältere Quelle mit zwar <c>[%alt]</c>/<c>[%info]</c>, aber OHNE <c>[ChessableOid]</c>
    /// ist NICHT modern und MUSS re-gefetcht werden (lokaler Re-Parse könnte die oids nie ergänzen). Ein PGN
    /// mit oids trägt implizit auch [%alt]/[%info], da dieselbe piratechess-Version alle Marker schreibt.</summary>
    private static bool SourceHasModernMarkers(string? pgn) =>
        pgn != null && pgn.Contains("[ChessableOid", StringComparison.Ordinal);

    /// <summary>Wie <see cref="SourceHasModernMarkers"/>, aber für ein Repertoire (Quelle = seine
    /// gespeicherten PGN-Dateien). Trifft es zu, reicht ein reiner Versions-Mark statt Re-Fetch —
    /// der Trainer liest das PGN [%alt] ohnehin live.</summary>
    private static bool RepertoireSourceModern(Repertoire r) =>
        r.Files.Any(f => SourceHasModernMarkers(f.PgnContent));

    private static bool IsChessable(string? tags, string fileName) =>
        (tags ?? string.Empty).Contains("chessable", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("chessable-", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^chessable-u\d+-(.+)\.pgn$", RegexOptions.IgnoreCase)]
    private static partial Regex ChessableBidRegex();

    /// <summary>Holt die Chessable-bid aus dem konventionellen Buch-Dateinamen <c>chessable-u{uid}-{bid}.pgn</c>.</summary>
    private static bool TryParseBid(string fileName, out string bid)
    {
        var m = ChessableBidRegex().Match(fileName);
        bid = m.Success ? m.Groups[1].Value : string.Empty;
        return m.Success;
    }

    [GeneratedRegex(@"^chessable-(\d+)\.pgn$", RegexOptions.IgnoreCase)]
    private static partial Regex RepertoireBidRegex();

    /// <summary>Chessable-bid eines Repertoires: bevorzugt <see cref="Repertoire.ChessableCourseId"/>,
    /// sonst aus dem Repertoire-Dateinamen <c>chessable-{bid}.pgn</c> (Altbestand ohne gesetzte CourseId,
    /// z. B. vor dem [Site]-Auto-Extract importiert). null ⇒ kein Chessable-Repertoire / nicht auflösbar.</summary>
    private static string? ResolveRepertoireBid(Repertoire rep)
    {
        if (!string.IsNullOrWhiteSpace(rep.ChessableCourseId)) return rep.ChessableCourseId;
        foreach (var f in rep.Files)
        {
            var m = RepertoireBidRegex().Match(f.FileName);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }
}
