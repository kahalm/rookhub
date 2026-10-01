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

    /// <summary>Größen-Deckel je Eintrag der ANON-Senke, in UTF-8-Bytes (so viel legt MariaDB ab — der
    /// Zeichen-Deckel <see cref="MaxJsonLength"/> ließ mit Drei-Byte-Zeichen das Dreifache durch). Gemessen am
    /// 29.09.2026 auf Prod: größte getReview-Antwort 77 073 Byte (angemeldet), anonym 34 115 Byte — 128 KB lassen
    /// dem größten je gesehenen Eintrag gut 1,6-fach Luft.</summary>
    public const int MaxAnonJsonBytes = 128 * 1024;

    /// <summary>GESAMT-Byte-Deckel der Anon-Senke. Der Zeilendeckel <see cref="MaxAnonRowsTotal"/> allein ließ
    /// 200 000 × <see cref="MaxJsonLength"/> ≈ 51 GB zu — ohne Konto, aus einer einzigen IP in gut zwei Stunden.
    /// 1 GiB fasst die 200 000 Zeilen bei realer Größe (im Schnitt rund 5 KB). Jenseits davon: keine neue Zeile,
    /// und Aktualisierungen nur, wenn sie die Zeile nicht vergrößern (was schon liegt, verliert niemand).</summary>
    public const long MaxAnonBytesTotal = 1L << 30;

    /// <summary>Wirksamer Byte-Deckel der Anon-Senke; nur Tests setzen ihn klein.</summary>
    internal long AnonBytesCap { get; init; } = MaxAnonBytesTotal;

    /// <summary>Wirksames Byte-Kontingent je Konto (<see cref="ChessableSinkBytes.MaxUserBytes"/>); nur Tests setzen es klein.</summary>
    internal long UserBytesCap { get; init; } = ChessableSinkBytes.MaxUserBytes;

    /// <summary>Portionsgröße der Retention: gelöscht wird über die Ids, das JSON wird dabei nie geladen. Nur Tests
    /// setzen sie klein.</summary>
    internal int DeleteChunkSize { get; init; } = 1000;

    /// <summary>Portionsgröße, in der Übernahme und Kurs-Merge das JSON laden (je Zeile bis 256 K Zeichen) — nie den
    /// ganzen Bestand auf einmal. Nur Tests setzen sie klein.</summary>
    internal int JsonChunkSize { get; init; } = 100;

    private readonly AppDbContext _db;
    private readonly PgnImportService _pgnImport;
    private readonly ILogger<ChessableReviewLineService>? _log;
    private readonly ChessableSinkBytes _sinkBytes;

    public ChessableReviewLineService(AppDbContext db, PgnImportService pgnImport,
        ILogger<ChessableReviewLineService>? log = null, ChessableSinkBytes? sinkBytes = null)
    {
        _db = db;
        _pgnImport = pgnImport;
        _log = log;
        _sinkBytes = sinkBytes ?? new ChessableSinkBytes();
    }

    /// <summary>Die bids der gecachten Chessable-Kursliste des Nutzers (leer, wenn nie eine geholt wurde).
    /// Grundlage der Besitz-Schranke in <see cref="ClaimAnonForUidAsync"/> und
    /// <see cref="ChessableImportService.HasVerifiedCourseAsync"/>.</summary>
    internal static HashSet<string> OwnedBids(string? cachedCoursesJson)
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
    /// oid (≤32), mit leerem/übergroßem JSON (&gt; <see cref="MaxJsonLength"/>) und — ist das Byte-Kontingent des
    /// Kontos (<see cref="ChessableSinkBytes.MaxUserBytes"/>) erschöpft — alles, was es wachsen ließe. Liefert die
    /// Zahl der tatsächlich geschriebenen/aktualisierten Zeilen.
    /// </summary>
    public async Task<int> UpsertBatchAsync(int userId, string bid,
        List<ChessableReviewLineEntryDto> entries, CancellationToken ct = default)
    {
        // oid in kanonischer Form (ChessableIds, A3-013): „00123" und „123" sind dieselbe Linie.
        var clean = (entries ?? new())
            .Where(e => e is not null
                && !string.IsNullOrWhiteSpace(e.Json)
                && e.Json.Length <= MaxJsonLength)
            .Select(e => (Oid: ChessableIds.CanonicalOid(e.Oid?.Trim())!, E: e))
            .Where(x => x.Oid is not null)
            .GroupBy(x => x.Oid)
            .Select(g => g.Last())   // letzter Stand je oid gewinnt innerhalb des Batches
            .Take(MaxEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        var oids = clean.Select(x => x.Oid).ToList();
        var existing = await _db.ChessableReviewLines
            .Where(r => r.UserId == userId && r.Bid == bid && oids.Contains(r.Oid))
            .ToDictionaryAsync(r => r.Oid, ct);
        var budget = _sinkBytes.ForUser(_db, userId, UserBytesCap, ct);

        var now = DateTime.UtcNow;
        var written = 0;
        foreach (var (oid, e) in clean)
        {
            existing.TryGetValue(oid, out var row);
            var delta = row is null
                ? ChessableSinkBytes.Utf8(e.Json) + ChessableSinkBytes.RowOverheadBytes
                : ChessableSinkBytes.Utf8(e.Json) - ChessableSinkBytes.Utf8(row.Json);
            if (!await budget.TryTakeAsync(delta)) continue;   // Kontingent des Kontos erschöpft
            if (row is null)
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

        try
        {
            await _db.SaveChangesAsync(ct);
            budget.Commit();
        }
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
    /// Validierung, aber enger gedeckelt: je Eintrag <see cref="MaxAnonJsonBytes"/>, je Batch
    /// <see cref="MaxAnonEntriesPerBatch"/>, dazu Zeilen je uid, Zeilen gesamt und Bytes gesamt
    /// (<see cref="MaxAnonBytesTotal"/>). Liefert die Zahl geschriebener/aktualisierter Zeilen.
    /// </summary>
    public async Task<int> UpsertAnonBatchAsync(string uid, string bid,
        List<ChessableReviewLineEntryDto> entries, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uid) || uid.Length > 32 || !uid.All(char.IsAsciiDigit)) return 0;

        var clean = (entries ?? new())
            .Where(e => e is not null
                && !string.IsNullOrWhiteSpace(e.Json)
                && e.Json.Length <= MaxAnonJsonBytes && ChessableSinkBytes.Utf8(e.Json) <= MaxAnonJsonBytes)
            .Select(e => (Oid: ChessableIds.CanonicalOid(e.Oid?.Trim())!, E: e))
            .Where(x => x.Oid is not null)
            .GroupBy(x => x.Oid)
            .Select(g => g.Last())
            .Take(MaxAnonEntriesPerBatch)
            .ToList();
        if (clean.Count == 0) return 0;

        var oids = clean.Select(x => x.Oid).ToList();
        var existing = await _db.AnonymousChessableReviewLines
            .Where(r => r.ChessableUid == uid && r.Bid == bid && oids.Contains(r.Oid))
            .ToDictionaryAsync(r => r.Oid, ct);
        // Gesamtbestand dieser uid (über alle bids) für den Deckel — NEUE oids jenseits davon abweisen.
        var uidRowCount = await _db.AnonymousChessableReviewLines.CountAsync(r => r.ChessableUid == uid, ct);
        // …und der Gesamtbestand der Senke: nur NEUE Zeilen werden abgewiesen, Aktualisierungen
        // bestehender laufen weiter (ein legitimer Nutzer verliert dadurch nichts).
        var totalRowCount = await _db.AnonymousChessableReviewLines.CountAsync(ct);
        // …und ihr Byte-Stand (gezählt erst, wenn ein Eintrag die Senke wachsen ließe).
        var budget = _sinkBytes.ForAnon(_db, AnonBytesCap, ct);

        var now = DateTime.UtcNow;
        var written = 0;
        foreach (var (oid, e) in clean)
        {
            existing.TryGetValue(oid, out var row);
            if (row is null)
            {
                if (uidRowCount >= MaxAnonRowsPerUid) continue;   // Deckel erreicht → keine neue Zeile
                if (totalRowCount >= MaxAnonRowsTotal) continue;   // Senke insgesamt voll
            }
            var delta = ChessableSinkBytes.Utf8(e.Json) - (row is null ? 0 : ChessableSinkBytes.Utf8(row.Json));
            if (!await budget.TryTakeAsync(delta)) continue;   // Senke in Bytes voll → nichts, was sie wachsen lässt
            if (row is null)
            {
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

        try
        {
            await _db.SaveChangesAsync(ct);
            budget.Commit();
        }
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

        // Erst nur die Schlüssel: bis zu MaxAnonRowsPerUid Zeilen LONGTEXT auf einmal hieße bis zu 5 000 × 128 KB in
        // der API. Das JSON kommt unten portionsweise.
        var anon = await _db.AnonymousChessableReviewLines
            .Where(r => r.ChessableUid == uid)
            .Select(r => new { r.Id, r.Bid })
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
        var claimIds = anon.Where(r => owned.Contains(r.Bid)).Select(r => r.Id).ToList();
        if (claimIds.Count == 0) return 0;    // nur fremde bids: liegen lassen, die Retention entsorgt sie

        // Portionsweise übernehmen, jede Portion für sich gespeichert (idempotent: eine abgebrochene Übernahme holt der
        // nächste Aufruf mit dem Rest nach) und danach aus dem Tracker genommen — sonst hielte er am Ende doch alles.
        var now = DateTime.UtcNow;
        var claimed = 0;
        var bids = new List<string>();
        foreach (var chunk in claimIds.Chunk(JsonChunkSize))
        {
            var rows = await _db.AnonymousChessableReviewLines.Where(r => chunk.Contains(r.Id)).ToListAsync(ct);
            if (rows.Count == 0) continue;
            var chunkBids = rows.Select(r => r.Bid).Distinct().ToList();
            var chunkOids = rows.Select(r => r.Oid).Distinct().ToList();
            var existing = (await _db.ChessableReviewLines
                    .Where(r => r.UserId == userId && chunkBids.Contains(r.Bid) && chunkOids.Contains(r.Oid))
                    .ToListAsync(ct))
                .ToDictionary(r => (r.Bid, r.Oid));

            foreach (var a in rows)
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
            }
            _db.AnonymousChessableReviewLines.RemoveRange(rows);

            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { _db.ChangeTracker.Clear(); break; }

            foreach (var row in existing.Values) _db.Entry(row).State = EntityState.Detached;
            claimed += rows.Count;
            foreach (var b in chunkBids)
                if (!bids.Contains(b)) bids.Add(b);
        }
        if (claimed == 0) return 0;
        _sinkBytes.ForgetAnon();
        _sinkBytes.ForgetUser(userId);

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
    /// nie einen Bearer hinterlegt). Verhindert unbegrenztes Wachstum der Anon-Senke. Täglich getrieben vom
    /// <see cref="AnonymousDataRetentionService"/> (läuft unabhängig von <c>Chessable:Enabled</c>).
    /// Liefert die Zahl gelöschter Zeilen.</summary>
    public async Task<int> PruneAnonOlderThanAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        return await DeleteAnonAsync(_db.AnonymousChessableReviewLines.Where(r => r.UpdatedAt < cutoff), ct);
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
        return await DeleteAnonAsync(_db.AnonymousChessableReviewLines
            .Where(r => r.UpdatedAt < cutoff && !linked.Contains(r.ChessableUid)), ct);
    }

    /// <summary>Löscht die Zeilen der Anon-Senke, die <paramref name="query"/> trifft — portionsweise über die Ids, ohne das
    /// JSON zu laden (bis zu 200 000 Zeilen LONGTEXT: auf einmal geladen legte die Retention die API selbst um).
    /// Relational ein DELETE je Portion; InMemory (Tests) kennt kein ExecuteDelete.</summary>
    private async Task<int> DeleteAnonAsync(IQueryable<AnonymousChessableReviewLine> query, CancellationToken ct)
    {
        var deleted = 0;
        while (true)
        {
            var ids = await query.OrderBy(r => r.Id).Select(r => r.Id).Take(DeleteChunkSize).ToListAsync(ct);
            if (ids.Count == 0) break;
            if (_db.Database.IsRelational())
                await _db.AnonymousChessableReviewLines.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(ct);
            else
            {
                _db.AnonymousChessableReviewLines.RemoveRange(
                    await _db.AnonymousChessableReviewLines.Where(r => ids.Contains(r.Id)).ToListAsync(ct));
                await _db.SaveChangesAsync(ct);
            }
            deleted += ids.Count;
            if (ids.Count < DeleteChunkSize) break;
        }
        if (deleted > 0) _sinkBytes.ForgetAnon();
        return deleted;
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

        // Erst nur die oids: der Merge läuft bei JEDEM review-lines-Aufruf, das JSON (bis 256 K Zeichen je Linie) braucht
        // er aber nur für die Lücken — und die sind nach dem ersten Merge wenige.
        var reviewOids = await _db.ChessableReviewLines
            .Where(r => r.UserId == userId && r.Bid == bid)
            .Select(r => r.Oid)
            .ToListAsync(ct);
        if (reviewOids.Count == 0) return 0;

        var fileName = $"chessable-u{userId}-{bid}.pgn";

        // Bereits als Buch vorhandene oids (JEDE Quelle) — die füllt Review NICHT, es überschreibt nichts.
        var existingOids = (await _db.BookPuzzles
                .Where(bp => bp.BookFileName == fileName && bp.ChessableOid != null)
                .Select(bp => bp.ChessableOid!)
                .ToListAsync(ct))
            .ToHashSet();

        // Nur die Lücken (oid noch kein BookPuzzle) zu PGN konvertieren; unbrauchbare Antworten überspringen. Ihr JSON
        // kommt portionsweise.
        var gapOids = reviewOids.Where(o => !existingOids.Contains(o)).Distinct().ToList();
        var pgns = new List<string>();
        string? bookName = null;
        foreach (var chunk in gapOids.Chunk(JsonChunkSize))
        {
            var jsonByOid = (await _db.ChessableReviewLines
                    .Where(r => r.UserId == userId && r.Bid == bid && chunk.Contains(r.Oid))
                    .Select(r => new { r.Oid, r.Json })
                    .ToListAsync(ct))
                .ToDictionary(r => r.Oid, r => r.Json);
            foreach (var oid in chunk)
            {
                if (!jsonByOid.TryGetValue(oid, out var json)) continue;
                var converted = ChessableReviewParser.TryConvert(json);
                if (converted is null) continue;
                pgns.Add(converted.Pgn);
                bookName ??= ExtractBookName(json);
            }
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
        // gesetztes (vollständiges) Book.Source.SourcePgn darf davon NICHT überschrieben werden (sonst wäre die
        // Reprocessing-Quelle nur noch das Teil-PGN). Nur ein leeres SourcePgn wird erstmalig gesetzt.
        var res = await _pgnImport.ImportFileAsync(fileName, combined, ct, preserveExistingSourcePgn: true,
            ownerUserId: userId);

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
