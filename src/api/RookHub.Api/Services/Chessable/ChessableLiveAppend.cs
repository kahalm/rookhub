using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Live-Anhängen aus der Erweiterung (V1 „beim Durchklicken") und der Import-Stand je oid für deren Overlays.
/// Teil von <see cref="ChessableImportService"/> (Zerlegung Z3), von der Fassade gebaut; das prozessweite
/// Schloss je (User, bid) existiert genau einmal (statisch), wie vorher in der Fassade.
/// </summary>
internal sealed class ChessableLiveAppend
{
    private readonly AppDbContext _db;
    private readonly PgnImportService _pgnImport;
    private readonly RepertoireService _repertoires;
    private readonly ChessableProxyService _proxy;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableLiveAppend(AppDbContext db, PgnImportService pgnImport, RepertoireService repertoires,
        ChessableProxyService proxy, ILogger<ChessableImportService> logger)
    {
        _db = db;
        _pgnImport = pgnImport;
        _repertoires = repertoires;
        _proxy = proxy;
        _logger = logger;
    }

    /// <summary>Siehe <see cref="ChessableImportService.AppendLiveAsync"/>.</summary>
    public Task<ChessableImportService.LiveAppendResult> AppendAsync(
        int userId, string bid, string pgn, string courseName, string target, CancellationToken ct, bool partial)
    {
        // FALLE: der Live-Append ist ein Read-Modify-Write auf demselben PgnContent (lesen → anhängen →
        // zurückschreiben) OHNE Concurrency-Token. Klickt der Nutzer schnell durch oder feuert ein
        // Netz-Retry parallel, lesen beide Requests denselben Stand und der zweite SaveChanges
        // überschreibt die Linie des ersten — sie verschwindet still. Ebenso fänden zwei parallele
        // ERSTE Linien beide kein Repertoire und legten zwei chessable-{bid}-Repertoires an. Darum je
        // (User, bid) serialisieren.
        return WithAppendLockAsync($"{userId}:{bid}", () => AppendCoreAsync(userId, bid, pgn, courseName, target, partial, ct), ct);
    }

    /// <summary>Prozessweite Schlösser je (User, bid) für <see cref="AppendAsync"/>. Bewusst nicht
    /// aufgeräumt: pro durchgeklicktem Kurs genau ein Eintrag (vernachlässigbar), und ein Entfernen
    /// bräuchte selbst wieder Synchronisation. Prozesslokal — bei mehreren API-Instanzen bräuchte es
    /// zusätzlich ein DB-seitiges Schloss/Concurrency-Token.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _appendGates = new();

    internal static async Task<T> WithAppendLockAsync<T>(string key, Func<Task<T>> body, CancellationToken ct = default)
    {
        var gate = _appendGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        // Mit Token warten: hängt der Body (DB-Hänger im Read-Modify-Write), soll ein abgebrochener
        // Request nicht unkündbar in der Schlange stehen bleiben.
        await gate.WaitAsync(ct);
        try { return await body(); }
        finally { gate.Release(); }
    }

    private async Task<ChessableImportService.LiveAppendResult> AppendCoreAsync(
        int userId, string bid, string pgn, string courseName, string target, bool partial, CancellationToken ct)
    {
        target = ChessableImportTargets.Normalize(target);
        var name = ChessableImportTargets.DisplayName(courseName, bid);

        if (target == "book")
            return await AppendToBookAsync(userId, bid, pgn, name, partial, ct);

        var repFile = ChessableImportTargets.RepertoireFileName(bid);
        var rep = await _db.Repertoires.Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.UserId == userId && (r.ChessableCourseId == bid || r.Files.Any(f => f.FileName == repFile)), ct);

        if (rep is null)
        {
            // Erste Linie → Repertoire anlegen, damit es sofort sichtbar ist und live mitwächst.
            var created = await _repertoires.CreateAsync(userId, new CreateRepertoireDto
            {
                Name = name,
                Description = $"Aus Chessable (bid {bid}) — live beim Durchklicken erfasst",
                Kind = RepertoireKind.Opening,
                IsPublic = false,
                UseForExtension = false,
            });
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn));
            await _repertoires.UploadFileAsync(created.Id, userId, repFile, ms);
            var r2 = await _db.Repertoires.FirstAsync(r => r.Id == created.Id, ct);
            if (string.IsNullOrEmpty(r2.ChessableCourseId)) { r2.ChessableCourseId = bid; await _db.SaveChangesAsync(ct); }
            return new(ChessablePgnBlocks.Count(pgn), created.Id, target, 0);
        }

        var file = rep.Files.FirstOrDefault(f => f.FileName == repFile) ?? rep.Files.FirstOrDefault();
        if (file is null)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn));
            await _repertoires.UploadFileAsync(rep.Id, userId, repFile, ms);
            return new(ChessablePgnBlocks.Count(pgn), rep.Id, target, 0);
        }

        var (added, backfillCount) = await MergeIntoFileAsync(rep, file, bid, pgn, ct);
        return new(added, rep.Id, target, backfillCount);
    }

    private async Task<ChessableImportService.LiveAppendResult> AppendToBookAsync(
        int userId, string bid, string pgn, string name, bool partial, CancellationToken ct)
    {
        var fileName = ChessableImportTargets.BookFileName(userId, bid);
        // Der Browser liefert nie den ganzen Kurs auf einmal (Kapitel-Chunk, Live-Append) — der Text wird je oid
        // ins SourcePgn zusammengeführt statt es zu ersetzen (s. ImportFileAsync, mergeSourcePgn).
        var res = await _pgnImport.ImportFileAsync(fileName, pgn, ct, partial: partial, mergeSourcePgn: true,
            ownerUserId: userId);
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct);
        if (book is not null)
        {
            book.OwnerUserId = userId;
            if (string.IsNullOrWhiteSpace(book.DisplayName)) book.DisplayName = name;
            book.Tags = "chessable";
            book.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return new(res.Imported, res.BookId, "book", 0);
    }

    /// <summary>Hängt die noch nicht vorhandenen Linien an die Repertoire-Datei an, trägt fehlende oids nach und
    /// bereinigt die Datei. Liefert (neu angehängt, oids nachgetragen).</summary>
    private async Task<(int Added, int Backfilled)> MergeIntoFileAsync(
        Repertoire rep, RepertoireFile file, string bid, string pgn, CancellationToken ct)
    {
        // Nur die noch nicht vorhandenen Linien anhängen → erneutes Durchklicken derselben Linie erzeugt keine
        // Dublette. Je eingehender Linie, in dieser Reihenfolge:
        //  1. Ihre oid steht schon in der Datei → dieselbe Chessable-Linie, nichts zu tun.
        //  2. Gleicher Zugtext wie eine Bestands-Linie OHNE oid (Import vor piratechess v1.29.0) → deren oid
        //     NACHTRAGEN statt anzuhängen. Ohne oid erkennt das Fortschritts-Overlay die Linie nie als importiert,
        //     und jedes „Kurs holen" hält sie für neu.
        //  3. Ohne eigene oid: gleicher Zugtext → Dublette.
        //  4. Sonst anhängen — auch bei gleichem Zugtext unter ANDERER oid: Chessable wiederholt Linien (z. B. im
        //     Quick-Starter-Kapitel), das sind eigene Linien mit eigenem Fortschritt.
        // Verglichen werden die EXAKTEN Zugtexte, keine Teilzeichenketten: `existing.Contains(moves)` ließ früher
        // eine kürzere Linie, die Präfix einer längeren ist, still verschwinden — und war quadratisch in der
        // PGN-Größe, im Request-Pfad des Live-Appends.
        var existing = file.PgnContent ?? string.Empty;
        var existingBlocks = ChessablePgnBlocks.Split(existing).ToList();
        var knownOids = new HashSet<string>(StringComparer.Ordinal);
        var seenMoves = new HashSet<string>(StringComparer.Ordinal);
        var oidlessByMoves = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < existingBlocks.Count; i++)
        {
            var blockOid = ChessablePgnBlocks.OidOf(existingBlocks[i]);
            if (blockOid != null) knownOids.Add(blockOid);
            var blockMoves = ChessablePgnBlocks.MovetextOf(existingBlocks[i]);
            if (blockMoves.Length == 0) continue;
            seenMoves.Add(blockMoves);
            // Ausgeblendete Kopien bekommen keine oid nachgetragen — ihr Zugtext bleibt aber „gesehen", damit
            // derselbe Inhalt nicht erneut angehängt wird.
            if (blockOid == null && !RepertoirePgnCleanup.IsHiddenGame(existingBlocks[i])) oidlessByMoves.TryAdd(blockMoves, i);
        }

        var backfill = new Dictionary<int, string>();   // Bestands-Block (Index) → nachzutragende oid
        var appended = new StringBuilder();
        var added = 0;
        foreach (var game in ChessablePgnBlocks.Split(pgn))
        {
            var moves = ChessablePgnBlocks.MovetextOf(game);
            if (moves.Length == 0) continue;
            var oid = ChessablePgnBlocks.OidOf(game);
            if (oid != null)
            {
                if (knownOids.Contains(oid)) continue;
                if (oidlessByMoves.Remove(moves, out var blockIndex))
                {
                    backfill[blockIndex] = oid;
                    knownOids.Add(oid);
                    continue;
                }
                knownOids.Add(oid);
            }
            else if (seenMoves.Contains(moves)) continue;
            seenMoves.Add(moves);
            appended.Append("\n\n\n").Append(game.Trim());
            added++;
        }
        if (added > 0 || backfill.Count > 0)
        {
            var content = backfill.Count > 0 ? ChessablePgnBlocks.InsertChessableOids(existing, backfill) : existing;
            file.PgnContent = content.TrimEnd() + appended + "\n";
            // Bytes, nicht Zeichen: das PGN trägt Kommentare in de/hr, ein Zeichenzähler wies die
            // Datei damit kleiner aus, als sie auf der Platte ist.
            file.FileSize = Encoding.UTF8.GetByteCount(file.PgnContent);
            rep.UpdatedAt = DateTime.UtcNow;
            if (added > 0) rep.ImportVersion = ImportPipeline.CurrentVersion;
            await _db.SaveChangesAsync(ct);
        }
        // Altlasten gleich mit bereinigen (nie löschen, nur ausblenden) — neu hinzugekommene Linien können eine alte
        // Kopie erst erkennbar machen, und Dateien mit veraltetem Regelstand kommen so ohne Neustart dran.
        if (added > 0 || backfill.Count > 0 || file.CleanupVersion < RepertoirePgnCleanup.CurrentVersion)
        {
            var cleanup = await RepertoireCleanupService.CleanupFileAsync(file, bid, _proxy, _logger, apply: true, ct);
            if (cleanup != null)
            {
                if (cleanup.Count > 0)
                {
                    rep.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInformation("Repertoire-Bereinigung beim Live-Append: {Count} Änderungen in Datei {FileId} (bid {Bid})",
                        cleanup.Count, file.Id, bid);
                }
                await _db.SaveChangesAsync(ct);
            }
        }
        return (added, backfill.Count);
    }

    /// <summary>Siehe <see cref="ChessableImportService.GetImportedOidsAsync"/>.</summary>
    public async Task<(HashSet<string> Oids, bool Book, bool Repertoire)> GetImportedOidsAsync(
        int userId, string bid, CancellationToken ct)
    {
        var oids = new HashSet<string>();

        var bookFile = ChessableImportTargets.BookFileName(userId, bid);
        // AUCH aus getReview vorbelegte Lücken-Füller (Source=="review") zählen als importiert/gecacht:
        // getReview und getGame sind für die Linie inhaltlich gleichwertig (nur getGame trägt zusätzlich
        // die „schwierigen Züge"), ein getGame-Re-Fetch bringt für die Linie also KEINEN Mehrwert. Darum
        // im Overlay als „gecacht" (✓) UND vom (getGame-)Crawl NICHT erneut geholt.
        var bookOids = await _db.BookPuzzles
            .Where(bp => bp.BookFileName == bookFile && bp.ChessableOid != null)
            .Select(bp => bp.ChessableOid!)
            .ToListAsync(ct);
        foreach (var o in bookOids) oids.Add(o);
        var hasBook = bookOids.Count > 0
            || await _db.Books.AnyAsync(b => b.FileName == bookFile && b.OwnerUserId == userId, ct);

        var repFile = ChessableImportTargets.RepertoireFileName(bid);
        var repIds = await _db.Repertoires
            .Where(r => r.UserId == userId && (r.ChessableCourseId == bid || r.Files.Any(f => f.FileName == repFile)))
            .Select(r => r.Id).ToListAsync(ct);
        var hasRep = repIds.Count > 0;
        if (hasRep)
            await CollectRepertoireOidsAsync(repIds, oids, ct);

        return (oids, hasBook, hasRep);
    }

    private async Task CollectRepertoireOidsAsync(List<int> repIds, HashSet<string> oids, CancellationToken ct)
    {
        // Das PGN wird NUR fuer Dateien geholt, deren Kennungs-Zwischenspeicher fehlt oder
        // veraltet ist. Vorher wanderte hier bei jedem Extension-Poll der komplette PGN-Text
        // jedes passenden Repertoires ueber die Leitung, nur um ihn per Regex zu durchsuchen.
        var files = await _db.RepertoireFiles
            .Where(f => repIds.Contains(f.RepertoireId))
            .Select(f => new
            {
                f.Id,
                f.ChessableOidsCache,
                Stale = f.ChessableOidsCache == null || f.ChessableOidsPgnLength != f.PgnContent.Length,
                PgnIfStale = (f.ChessableOidsCache == null || f.ChessableOidsPgnLength != f.PgnContent.Length)
                    ? f.PgnContent : null,
            })
            .ToListAsync(ct);

        var rebuilt = new Dictionary<int, (string Cache, int Length)>();
        foreach (var f in files)
        {
            if (!f.Stale)
            {
                foreach (var o in f.ChessableOidsCache!.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    oids.Add(o);
                continue;
            }
            var content = f.PgnIfStale ?? string.Empty;
            var found = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in ChessablePgnBlocks.ChessableOidRegex.Matches(content))
            {
                oids.Add(m.Groups[1].Value);
                found.Add(m.Groups[1].Value);
            }
            rebuilt[f.Id] = (string.Join('\n', found.Distinct()), content.Length);
        }

        // Einmalig nachtragen; best-effort, denn die Antwort oben stimmt auch ohne den Nachtrag.
        if (rebuilt.Count > 0)
        {
            try
            {
                var ids = rebuilt.Keys.ToList();
                var tracked = await _db.RepertoireFiles.Where(f => ids.Contains(f.Id)).ToListAsync(ct);
                foreach (var f in tracked)
                {
                    (f.ChessableOidsCache, var len) = rebuilt[f.Id];
                    f.ChessableOidsPgnLength = len;
                }
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) { /* Antwort stimmt trotzdem */ }
        }
    }
}
