using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Roh-Senke für die getReview-Antworten der RepCheck-Extension (siehe <see cref="ChessableReviewLine"/>).
/// Eine Zeile je (User, Kurs-bid, Varianten-oid), per Upsert aktuell gehalten (letzter Stand gewinnt).
/// Das JSON wird hier NICHT geparst — nur roh abgelegt; der Aufbau zum Kurs (Fallback zu getGame)
/// passiert erst später über <see cref="ChessableReviewParser"/>.
/// </summary>
public class ChessableReviewLineService
{
    /// <summary>Deckel je Batch — ein Kurs-Training kommt in mehreren Batches (analog ProblemMoves).</summary>
    public const int MaxEntriesPerBatch = 500;

    /// <summary>Größen-Deckel je Eintrag (Missbrauchs-/Sanity-Schranke; getReview ist normal einige KB).</summary>
    public const int MaxJsonLength = 256 * 1024;

    /// <summary>Zeilen-Deckel je Chessable-uid in der ANON-Senke (DoS-/Poisoning-Schranke des offenen
    /// Endpoints): jenseits davon werden für diese uid keine NEUEN oids mehr angenommen (Updates
    /// bestehender bleiben). Großzügig über einem realen Trainingsbestand (großer Kurs ~1–2k Linien).</summary>
    public const int MaxAnonRowsPerUid = 5000;

    /// <summary>GESAMT-Deckel der Anon-Senke. Der Deckel je uid bindet nichts, solange die uid ein frei
    /// wählbares Feld des offenen Endpoints ist — ein Skript nimmt einfach fortlaufende uids und legt
    /// beliebig viele Partitionen an. Die Zeilen sind LONGTEXT-JSON; ohne diese Schranke läuft die
    /// Datenbank des Stacks voll und ALLE Schreibwege der App stehen, lange bevor ein Alarm greift.</summary>
    public const int MaxAnonRowsTotal = 200_000;

    /// <summary>Batch-Deckel des OFFENEN Endpoints — deutlich kleiner als <see cref="MaxEntriesPerBatch"/>:
    /// die Extension schickt je Trainingsschritt eine Handvoll Linien, 500 × 256 KB pro Request braucht
    /// dort niemand (der Request-Deckel von 16 MB wäre sonst tatsächlich ausschöpfbar).</summary>
    public const int MaxAnonEntriesPerBatch = 50;

    private readonly AppDbContext _db;
    private readonly PgnImportService _pgnImport;
    private readonly ILogger<ChessableReviewLineService>? _log;

    public ChessableReviewLineService(AppDbContext db, PgnImportService pgnImport,
        ILogger<ChessableReviewLineService>? log = null)
    {
        _db = db;
        _pgnImport = pgnImport;
        _log = log;
    }

    /// <summary>Die bids der gecachten Chessable-Kursliste des Nutzers (leer, wenn nie eine geholt wurde).
    /// Grundlage der Besitz-Schranke in <see cref="ClaimAnonForUidAsync"/>.</summary>
    private static HashSet<string> OwnedBids(string? cachedCoursesJson)
    {
        if (string.IsNullOrEmpty(cachedCoursesJson)) return new HashSet<string>();
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<ChessableCourseDto>>(cachedCoursesJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return list?.Where(c => !string.IsNullOrWhiteSpace(c.Bid)).Select(c => c.Bid!).ToHashSet()
                   ?? new HashSet<string>();
        }
        catch (System.Text.Json.JsonException) { return new HashSet<string>(); }
    }

    /// <summary>
    /// Upsert je oid (letzter Stand im Batch gewinnt). Verworfen werden Einträge ohne gültige numerische
    /// oid (≤32), mit leerem/übergroßem JSON (&gt; <see cref="MaxJsonLength"/>). Liefert die Zahl der
    /// tatsächlich geschriebenen/aktualisierten Zeilen.
    /// </summary>
    public async Task<int> UpsertBatchAsync(int userId, string bid,
        List<ChessableReviewLineEntryDto> entries, CancellationToken ct = default)
    {
        var clean = (entries ?? new())
            .Where(e => e is not null
                && !string.IsNullOrWhiteSpace(e.Oid)
                && e.Oid.Trim().Length <= 32 && e.Oid.Trim().All(char.IsAsciiDigit)
                && !string.IsNullOrWhiteSpace(e.Json)
                && e.Json.Length <= MaxJsonLength)
            .GroupBy(e => e.Oid.Trim())
            .Select(g => g.Last())   // letzter Stand je oid gewinnt innerhalb des Batches
            .Take(MaxEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        var oids = clean.Select(e => e.Oid.Trim()).ToList();
        var existing = await _db.ChessableReviewLines
            .Where(r => r.UserId == userId && r.Bid == bid && oids.Contains(r.Oid))
            .ToDictionaryAsync(r => r.Oid, ct);

        var now = DateTime.UtcNow;
        var written = 0;
        foreach (var e in clean)
        {
            var oid = e.Oid.Trim();
            if (!existing.TryGetValue(oid, out var row))
            {
                row = new ChessableReviewLine { UserId = userId, Bid = bid, Oid = oid };
                _db.ChessableReviewLines.Add(row);
                existing[oid] = row;
            }
            row.Json = e.Json;
            row.ChapterTitle = ExtractChapterTitle(e.Json);
            row.UpdatedAt = now;
            written++;
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Race auf dem Unique-Index (paralleler Flush desselben Users): Batch ist idempotent —
            // verwerfen, der nächste Flush bringt denselben Stand erneut.
            _db.ChangeTracker.Clear();
        }
        return written;
    }

    /// <summary>
    /// Token-loser Zwilling von <see cref="UpsertBatchAsync"/>: legt getReview-Linien eines Users OHNE
    /// RookHub-Account in der Anon-Senke ab, identifiziert über die Chessable-<c>uid</c>. Gleiche
    /// Validierung/Deckel. Liefert die Zahl geschriebener/aktualisierter Zeilen.
    /// </summary>
    public async Task<int> UpsertAnonBatchAsync(string uid, string bid,
        List<ChessableReviewLineEntryDto> entries, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uid) || uid.Length > 32 || !uid.All(char.IsAsciiDigit)) return 0;

        var clean = (entries ?? new())
            .Where(e => e is not null
                && !string.IsNullOrWhiteSpace(e.Oid)
                && e.Oid.Trim().Length <= 32 && e.Oid.Trim().All(char.IsAsciiDigit)
                && !string.IsNullOrWhiteSpace(e.Json)
                && e.Json.Length <= MaxJsonLength)
            .GroupBy(e => e.Oid.Trim())
            .Select(g => g.Last())
            .Take(MaxAnonEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        var oids = clean.Select(e => e.Oid.Trim()).ToList();
        var existing = await _db.AnonymousChessableReviewLines
            .Where(r => r.ChessableUid == uid && r.Bid == bid && oids.Contains(r.Oid))
            .ToDictionaryAsync(r => r.Oid, ct);
        // Gesamtbestand dieser uid (über alle bids) für den Deckel — NEUE oids jenseits davon abweisen.
        var uidRowCount = await _db.AnonymousChessableReviewLines.CountAsync(r => r.ChessableUid == uid, ct);
        // …und der Gesamtbestand der Senke: nur NEUE Zeilen werden abgewiesen, Aktualisierungen
        // bestehender laufen weiter (ein legitimer Nutzer verliert dadurch nichts).
        var totalRowCount = await _db.AnonymousChessableReviewLines.CountAsync(ct);

        var now = DateTime.UtcNow;
        var written = 0;
        foreach (var e in clean)
        {
            var oid = e.Oid.Trim();
            if (!existing.TryGetValue(oid, out var row))
            {
                if (uidRowCount >= MaxAnonRowsPerUid) continue;   // Deckel erreicht → keine neue Zeile
                if (totalRowCount >= MaxAnonRowsTotal) continue;   // Senke insgesamt voll
                row = new AnonymousChessableReviewLine { ChessableUid = uid, Bid = bid, Oid = oid, CreatedAt = now };
                _db.AnonymousChessableReviewLines.Add(row);
                existing[oid] = row;
                uidRowCount++; totalRowCount++;
            }
            row.Json = e.Json;
            row.ChapterTitle = ExtractChapterTitle(e.Json);
            row.UpdatedAt = now;
            written++;
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); }   // Race auf dem Unique-Index → idempotent verwerfen
        return written;
    }

    /// <summary>
    /// Übernimmt („claim") alle anonym (token-los) für eine Chessable-<c>uid</c> gesammelten getReview-
    /// Linien in den RookHub-Account <paramref name="userId"/> — aufgerufen, wenn der User seinen
    /// Chessable-Bearer mit RookHub verknüpft (der Server decodiert dieselbe uid daraus). Die Anon-Zeilen
    /// werden in <see cref="ChessableReviewLine"/> übernommen (Upsert je (User,bid,oid), letzter Stand
    /// gewinnt), aus der Anon-Senke entfernt und die betroffenen Kurse einmal aufgebaut
    /// (<see cref="MergeIntoCourseAsync"/>). Idempotent. Liefert die Zahl übernommener Zeilen.
    /// </summary>
    public async Task<int> ClaimAnonForUidAsync(int userId, string uid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uid)) return 0;

        var anon = await _db.AnonymousChessableReviewLines
            .Where(r => r.ChessableUid == uid)
            .ToListAsync(ct);
        if (anon.Count == 0) return 0;

        // BESITZ-SCHRANKE. Die Ablage-Seite der Anon-Senke ist unauthentifiziert und kann die uid nicht
        // prüfen (nur der Claim beweist sie gegen Chessable) — jeder kann also unter einer fremden,
        // durchprobierbaren uid Linien einwerfen. Ohne diese Schranke übernahm der Claim ALLES und der
        // anschließende Kurs-Aufbau legte daraus echte Bücher an, samt frei gewähltem Namen und
        // erfundenen Zügen in einem Kurs, den das Opfer nie importiert hat. Übernommen wird deshalb nur,
        // was zu einem Kurs seiner EIGENEN, von Chessable gemeldeten Kursliste gehört.
        var owned = OwnedBids(await _db.ChessableCredentials
            .Where(c => c.UserId == userId).Select(c => c.CachedCoursesJson).FirstOrDefaultAsync(ct));
        if (owned.Count == 0) return 0;   // Kursliste (noch) unbekannt → nichts übernehmen, Zeilen bleiben liegen
        var foreignBids = anon.Where(r => !owned.Contains(r.Bid)).Select(r => r.Bid).Distinct().ToList();
        anon = anon.Where(r => owned.Contains(r.Bid)).ToList();
        if (anon.Count == 0) return 0;    // nur fremde bids: liegen lassen, die Retention entsorgt sie

        var bids = anon.Select(r => r.Bid).Distinct().ToList();
        var existing = (await _db.ChessableReviewLines
                .Where(r => r.UserId == userId && bids.Contains(r.Bid))
                .ToListAsync(ct))
            .ToDictionary(r => (r.Bid, r.Oid));

        var now = DateTime.UtcNow;
        var claimed = 0;
        foreach (var a in anon)
        {
            if (!existing.TryGetValue((a.Bid, a.Oid), out var row))
            {
                row = new ChessableReviewLine { UserId = userId, Bid = a.Bid, Oid = a.Oid };
                _db.ChessableReviewLines.Add(row);
                existing[(a.Bid, a.Oid)] = row;
            }
            row.Json = a.Json;
            row.ChapterTitle = a.ChapterTitle;
            row.UpdatedAt = now;
            claimed++;
        }
        _db.AnonymousChessableReviewLines.RemoveRange(anon);

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return 0; }

        if (foreignBids.Count > 0)
            _log?.LogInformation("Claim uid {Uid}: {Count} bid(s) nicht in der Kursliste des Nutzers — übersprungen ({Bids})",
                uid, foreignBids.Count, string.Join(",", foreignBids.Take(10)));

        // Betroffene Kurse aufbauen (getGame gewinnt, Review füllt Lücken) — best-effort je bid.
        foreach (var bid in bids)
        {
            try { await MergeIntoCourseAsync(userId, bid, ct); }
            catch { /* ein Kurs-Merge-Fehler darf den Claim nicht kippen */ }
        }
        return claimed;
    }

    /// <summary>Retention: ungeclaimte Anon-Zeilen älter als <paramref name="maxAge"/> löschen — der
    /// Absender hat seine Chessable-uid nie mit einem RookHub-Account verknüpft (Default-URL-Nutzer, der
    /// nie einen Bearer hinterlegt). Verhindert unbegrenztes Wachstum der Anon-Senke. Nächtlich getrieben.
    /// Liefert die Zahl gelöschter Zeilen.</summary>
    public async Task<int> PruneAnonOlderThanAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var old = await _db.AnonymousChessableReviewLines.Where(r => r.UpdatedAt < cutoff).ToListAsync(ct);
        if (old.Count == 0) return 0;
        _db.AnonymousChessableReviewLines.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
        return old.Count;
    }

    /// <summary>Kürzere Retention für uids, zu denen es GAR KEIN verknüpftes Konto gibt. Sie sind bis auf
    /// Weiteres nicht claimbar (niemand hat diese Chessable-Identität bewiesen) und damit genau der Topf,
    /// den der offene Endpoint auf Vorrat füllen kann. Der legitime Weg — anonym trainieren, dann Konto
    /// anlegen und Bearer verknüpfen — liegt in Tagen, nicht in Monaten; wer verknüpft hat, behält die
    /// volle Frist über <see cref="PruneAnonOlderThanAsync"/>.</summary>
    public async Task<int> PruneUnlinkedAnonOlderThanAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var linked = await _db.ChessableCredentials
            .Where(c => c.ChessableUid != null)
            .Select(c => c.ChessableUid!)
            .ToListAsync(ct);
        var old = await _db.AnonymousChessableReviewLines
            .Where(r => r.UpdatedAt < cutoff && !linked.Contains(r.ChessableUid))
            .ToListAsync(ct);
        if (old.Count == 0) return 0;
        _db.AnonymousChessableReviewLines.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
        return old.Count;
    }

    /// <summary>
    /// Lässt die gespeicherten <c>getReview</c>-Linien dieses Kurses in den Chessable-Kurs (Buch) des
    /// Users einfließen — als reiner LÜCKEN-Füller: getGame GEWINNT, Review überschreibt nie etwas.
    ///
    /// <para>Zielbuch ist <c>chessable-u{userId}-{bid}.pgn</c> (dieselbe Namenskonvention wie der
    /// getGame-Buch-Import in <see cref="ChessableImportService.ImportAsBookAsync"/> /
    /// <see cref="ChessableImportService.AppendLiveAsync"/>). Nur Review-Linien, deren <c>oid</c> im Buch
    /// NOCH KEIN <see cref="Models.BookPuzzle"/> ist, werden über <see cref="ChessableReviewParser"/> zu
    /// PGN konvertiert, zu EINEM Text gefügt und über <see cref="PgnImportService.ImportFileAsync"/>
    /// angehängt. Die dadurch NEU angelegten Linien werden mit <c>Source="review"</c> markiert — das ist
    /// die einzige Stelle, die <see cref="Models.BookPuzzle.Source"/> setzt; getGame-Linien bleiben
    /// <c>null</c>. Existiert das Buch noch nicht, wird es dabei angelegt (reiner Review-Kurs, den
    /// getGame später anreichert).</para>
    ///
    /// <para>Idempotent: ein zweiter Lauf findet keine Lücken mehr (die oids sind jetzt BookPuzzles) und
    /// legt nichts doppelt an. Liefert die Zahl der NEU angelegten Review-Linien.</para>
    /// </summary>
    public async Task<int> MergeIntoCourseAsync(int userId, string bid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bid)) return 0;

        var reviewRows = await _db.ChessableReviewLines
            .Where(r => r.UserId == userId && r.Bid == bid)
            .ToListAsync(ct);
        if (reviewRows.Count == 0) return 0;

        var fileName = $"chessable-u{userId}-{bid}.pgn";

        // Bereits als Buch vorhandene oids (JEDE Quelle) — die füllt Review NICHT, es überschreibt nichts.
        var existingOids = (await _db.BookPuzzles
                .Where(bp => bp.BookFileName == fileName && bp.ChessableOid != null)
                .Select(bp => bp.ChessableOid!)
                .ToListAsync(ct))
            .ToHashSet();

        // Nur die Lücken (oid noch kein BookPuzzle) zu PGN konvertieren; unbrauchbare Antworten überspringen.
        var pgns = new List<string>();
        var seen = new HashSet<string>();
        string? bookName = null;
        foreach (var row in reviewRows)
        {
            if (existingOids.Contains(row.Oid)) continue;
            if (!seen.Add(row.Oid)) continue;
            var converted = ChessableReviewParser.TryConvert(row.Json);
            if (converted is null) continue;
            pgns.Add(converted.Pgn);
            bookName ??= ExtractBookName(row.Json);
        }
        if (pgns.Count == 0) return 0;

        var combined = string.Join("\n\n\n", pgns);

        // Vor dem Import: welche Buch-Linien-Ids gibt es schon? → nur die dadurch NEU angelegten werden
        // als Source="review" markiert (nichts Bestehendes wird angefasst).
        var beforeIds = (await _db.BookPuzzles
                .Where(bp => bp.BookFileName == fileName)
                .Select(bp => bp.Id)
                .ToListAsync(ct))
            .ToHashSet();

        // preserveExistingSourcePgn: der Merge liefert NUR die Lücken-Linien; ein bereits von getGame
        // gesetztes (vollständiges) Book.SourcePgn darf davon NICHT überschrieben werden (sonst wäre die
        // Reprocessing-Quelle nur noch das Teil-PGN). Nur ein leeres SourcePgn wird erstmalig gesetzt.
        var res = await _pgnImport.ImportFileAsync(fileName, combined, ct, preserveExistingSourcePgn: true);

        // Buch als persönliches Chessable-Buch kennzeichnen (analog getGame-Buch-Import). Bei einem
        // frisch angelegten reinen Review-Kurs auch einen brauchbaren Anzeigenamen setzen (statt des
        // rohen Dateinamens); ein bereits von getGame gesetzter Name/Owner bleibt unangetastet.
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct);
        if (book is not null)
        {
            var freshBook = book.OwnerUserId is null;
            book.OwnerUserId ??= userId;
            if (string.IsNullOrWhiteSpace(book.Tags)) book.Tags = "chessable";
            if (freshBook && !string.IsNullOrWhiteSpace(bookName))
                book.DisplayName = bookName!.Length > 200 ? bookName[..200] : bookName;
            book.UpdatedAt = DateTime.UtcNow;
        }

        // Die durch DIESEN Merge NEU angelegten Linien mit Source="review" markieren (einzige Stelle,
        // die Source setzt). getGame-Linien bleiben Source=null.
        var newLines = await _db.BookPuzzles
            .Where(bp => bp.BookFileName == fileName && !beforeIds.Contains(bp.Id))
            .ToListAsync(ct);
        foreach (var bp in newLines) bp.Source = "review";

        await _db.SaveChangesAsync(ct);
        return newLines.Count;
    }

    /// <summary>Best-effort Kursname (<c>book_name</c>) aus der getReview-Antwort — nur für den
    /// Anzeigenamen eines frisch angelegten reinen Review-Kurses; kein Wurf.</summary>
    private static string? ExtractBookName(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("lesson", out var lesson)
                || lesson.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!lesson.TryGetProperty("moves", out var moves)
                || moves.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            foreach (var m in moves.EnumerateArray())
            {
                if (m.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (m.TryGetProperty("book_name", out var bn)
                    && bn.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = bn.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Best-effort Kapiteltitel aus der Antwort (nur für die Übersicht; kein Wurf).</summary>
    private static string? ExtractChapterTitle(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("lesson", out var lesson)
                || lesson.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (lesson.TryGetProperty("chapter", out var chapter)
                && chapter.ValueKind == System.Text.Json.JsonValueKind.Object
                && chapter.TryGetProperty("title", out var t)
                && t.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = t.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : (s!.Length > 300 ? s[..300] : s);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
