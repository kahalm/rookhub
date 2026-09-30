using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ChessableReviewLineServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChessableReviewLineService _service;

    public ChessableReviewLineServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new ChessableReviewLineService(_db, new PgnImportService(_db));
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync(string username = "reviewuser")
    {
        var user = new AppUser { Username = username, Email = $"{username}@test.com", PasswordHash = "hash" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static ChessableReviewLineEntryDto Entry(string oid, string json) => new() { Oid = oid, Json = json };

    private static string LoadFixture()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "getReview-sample.json"));

    // Fixture-Kurs (bid) und Linie (oid), wie in ChessableReviewParserTests belegt.
    /// <summary>Legt die gecachte Chessable-Kursliste des Nutzers an — der Claim übernimmt nur Linien zu
    /// Kursen, die dem Konto laut Chessable wirklich gehören (sonst könnte jeder unter einer fremden,
    /// durchprobierbaren uid Linien einwerfen und sie dem Opfer als Kurs unterschieben).</summary>
    private async Task GiveUserCoursesAsync(AppUser user, params string[] bids)
    {
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = user.Id,
            EncryptedBearer = "enc",
            CachedCoursesJson = System.Text.Json.JsonSerializer.Serialize(
                bids.Select(b => new ChessableCourseDto(b, $"Kurs {b}")).ToList()),
            CoursesCachedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private const string FixtureBid = "228856";
    private const string FixtureOid = "36730415";

    /// <summary>Ein „getGame"-Buch-Puzzle-PGN für dieselbe Linie (oid) — im ECHTEN getGame-Format:
    /// <c>[Round "Kapitel.Index"]</c> plus die oid separat im <c>[ChessableOid]</c>-Header. Die LineId
    /// (<c>{file}:002.001</c>) unterscheidet sich damit bewusst von der Review-LineId (<c>{file}:{oid}</c>),
    /// genau wie in Produktion — die Zusammenführung MUSS über die oid laufen, nicht die LineId. Der Inhalt
    /// weicht ab, damit ein Ersetzen am Inhalt nachweisbar ist.</summary>
    private static string GetGamePgnForOid(string oid) =>
        "[Event \"Chessable\"]\n" +
        "[Round \"002.001\"]\n" +
        "[White \"getGame Full Version\"]\n" +
        "[Result \"*\"]\n" +
        "[SetUp \"1\"]\n" +
        "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n" +
        $"[ChessableOid \"{oid}\"]\n\n" +
        "{ [%tqu \"En\",\"find the move\"] getGame-comment } 2.Nf3 Nc6 3. Bb5 *\n";

    [Fact]
    public async Task MergeIntoCourse_CreatesMissingReviewLines_MarkedSourceReview()
    {
        var user = await CreateUserAsync();
        await _service.UpsertBatchAsync(user.Id, FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });

        var created = await _service.MergeIntoCourseAsync(user.Id, FixtureBid);

        Assert.Equal(1, created);
        var fileName = $"chessable-u{user.Id}-{FixtureBid}.pgn";
        var bp = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);
        Assert.Equal(FixtureOid, bp.ChessableOid);
        Assert.Equal("review", bp.Source);          // die einzige Stelle, die Source setzt
        Assert.False(bp.IsInfoOnly);                 // echte Puzzle-Linie aus getReview

        // Das Buch wird angelegt (reiner Review-Kurs) — Owner + Chessable-Tag + brauchbarer Name.
        var book = await _db.Books.SingleAsync(b => b.FileName == fileName);
        Assert.Equal(user.Id, book.OwnerUserId);
        Assert.Equal("chessable", book.Tags);
        Assert.Contains("Gustafsson", book.DisplayName);   // aus book_name der getReview-Antwort
    }

    [Fact]
    public async Task GetGameImport_AfterMerge_ReplacesReviewLine_AndClearsSource()
    {
        var user = await CreateUserAsync();
        await _service.UpsertBatchAsync(user.Id, FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });
        await _service.MergeIntoCourseAsync(user.Id, FixtureBid);

        var fileName = $"chessable-u{user.Id}-{FixtureBid}.pgn";
        var before = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);
        Assert.Equal("review", before.Source);
        var reviewLineId = before.LineId;           // Review-LineId = {file}:{oid}

        // getGame läuft danach über DIESELBE oid, aber im ECHTEN Format (Round="002.001", oid nur im
        // Header) → ABWEICHENDE LineId. Die Zusammenführung über die oid ERSETZT den Füller in-place,
        // statt eine zweite Linie anzulegen (früherer Bug: LineId-Abgleich → Duplikat).
        var res = await new PgnImportService(_db).ImportFileAsync(fileName, GetGamePgnForOid(FixtureOid), CancellationToken.None);

        Assert.Equal(0, res.Imported);              // KEIN Duplikat neu angelegt …
        Assert.Equal(1, res.Updated);               // … sondern die Review-Linie in-place ersetzt
        var after = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);   // weiterhin genau EINE Linie
        Assert.Equal(before.Id, after.Id);          // dieselbe Zeile wiederverwendet (Fortschritt/FKs bleiben)
        Assert.NotEqual(reviewLineId, after.LineId);// hat jetzt die getGame-LineId (Round=Kapitel.Index)
        Assert.Null(after.Source);                  // getGame gewinnt → Source auf null
        Assert.Equal("g1f3 b8c6 f1b5", after.Moves);        // getGame-Zugfolge, nicht die Review-Version
        Assert.Contains("getGame-comment", after.Comment);
    }

    [Fact]
    public async Task GetGameImport_AfterMerge_PreservesProgressOnReviewLine()
    {
        var user = await CreateUserAsync();
        await _service.UpsertBatchAsync(user.Id, FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });
        await _service.MergeIntoCourseAsync(user.Id, FixtureBid);

        var fileName = $"chessable-u{user.Id}-{FixtureBid}.pgn";
        var reviewLine = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);

        // Der Nutzer LÖST die (aus getReview aufgebaute) Linie schon, BEVOR getGame kommt → Fortschritt.
        _db.CoursePuzzleResults.Add(new CoursePuzzleResult
        {
            UserId = user.Id, BookId = reviewLine.BookId ?? 0, BookPuzzleId = reviewLine.Id,
            SolvedAt = DateTime.UtcNow, TimeSeconds = 12,
        });
        await _db.SaveChangesAsync();

        // getGame ersetzt die Linie in-place (KEIN Delete → der Restrict-FK von CoursePuzzleResult bliebe
        // sonst hängen bzw. würfe in MariaDB, der Fortschritt ginge verloren).
        await new PgnImportService(_db).ImportFileAsync(fileName, GetGamePgnForOid(FixtureOid), CancellationToken.None);

        var after = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);
        Assert.Equal(reviewLine.Id, after.Id);      // dieselbe Zeile → FK zeigt weiter gültig darauf
        var result = await _db.CoursePuzzleResults.SingleAsync(r => r.UserId == user.Id);
        Assert.Equal(after.Id, result.BookPuzzleId); // Fortschritt bleibt an der (jetzt getGame-)Linie
    }

    [Fact]
    public async Task MergeIntoCourse_IsIdempotent_SecondRunAddsNothing()
    {
        var user = await CreateUserAsync();
        await _service.UpsertBatchAsync(user.Id, FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });

        var first = await _service.MergeIntoCourseAsync(user.Id, FixtureBid);
        var second = await _service.MergeIntoCourseAsync(user.Id, FixtureBid);

        Assert.Equal(1, first);
        Assert.Equal(0, second);                    // zweiter Lauf findet keine Lücke mehr
        var fileName = $"chessable-u{user.Id}-{FixtureBid}.pgn";
        Assert.Equal(1, await _db.BookPuzzles.CountAsync(b => b.BookFileName == fileName));   // kein Duplikat
    }

    [Fact]
    public async Task MergeIntoCourse_DoesNotOverwriteExistingGetGameLine()
    {
        var user = await CreateUserAsync();
        var fileName = $"chessable-u{user.Id}-{FixtureBid}.pgn";

        // getGame hat die Linie schon (Source=null) → Merge darf sie NICHT anfassen.
        await new PgnImportService(_db).ImportFileAsync(fileName, GetGamePgnForOid(FixtureOid), CancellationToken.None);
        await _service.UpsertBatchAsync(user.Id, FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });

        var created = await _service.MergeIntoCourseAsync(user.Id, FixtureBid);

        Assert.Equal(0, created);                    // oid bereits als Buch-Linie vorhanden → keine Lücke
        var bp = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);
        Assert.Null(bp.Source);                      // unverändert die getGame-Linie
        Assert.Equal("g1f3 b8c6 f1b5", bp.Moves);
    }

    [Fact]
    public async Task UpsertBatch_StoresRawJsonAndChapterTitle()
    {
        var user = await CreateUserAsync();
        var json = "{\"lesson\":{\"chapter\":{\"title\":\"My Chapter\"},\"moves\":[]}}";

        var stored = await _service.UpsertBatchAsync(user.Id, "228856", new() { Entry("1", json), Entry("2", json) });

        Assert.Equal(2, stored);
        var rows = await _db.ChessableReviewLines.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(json, r.Json));
        Assert.All(rows, r => Assert.Equal("My Chapter", r.ChapterTitle));
    }

    [Fact]
    public async Task UpsertBatch_SameOid_IsIdempotent_LastWins()
    {
        var user = await CreateUserAsync();

        await _service.UpsertBatchAsync(user.Id, "228856", new() { Entry("42", "{\"v\":1}") });
        await _service.UpsertBatchAsync(user.Id, "228856", new() { Entry("42", "{\"v\":2}") });

        var row = await _db.ChessableReviewLines.SingleAsync();   // genau EINE Zeile je (User,bid,oid)
        Assert.Equal("42", row.Oid);
        Assert.Equal("{\"v\":2}", row.Json);                      // letzter Stand gewinnt
    }

    [Fact]
    public async Task UpsertBatch_LastWinsWithinBatch_ForDuplicateOid()
    {
        var user = await CreateUserAsync();

        var stored = await _service.UpsertBatchAsync(user.Id, "228856",
            new() { Entry("7", "{\"v\":\"a\"}"), Entry("7", "{\"v\":\"b\"}") });

        Assert.Equal(1, stored);
        var row = await _db.ChessableReviewLines.SingleAsync();
        Assert.Equal("{\"v\":\"b\"}", row.Json);
    }

    [Fact]
    public async Task UpsertBatch_RejectsInvalidOidEmptyJsonAndOversize()
    {
        var user = await CreateUserAsync();
        var big = "{\"x\":\"" + new string('a', ChessableReviewLineService.MaxJsonLength) + "\"}";

        var stored = await _service.UpsertBatchAsync(user.Id, "228856", new()
        {
            Entry("abc", "{}"),   // nicht-numerische oid
            Entry("1", ""),       // leeres JSON
            Entry("2", big),      // zu groß
            Entry("3", "{}"),     // gültig
        });

        Assert.Equal(1, stored);
        var row = await _db.ChessableReviewLines.SingleAsync();
        Assert.Equal("3", row.Oid);
    }

    [Fact]
    public async Task UpsertBatch_SeparatesUsersAndBooks()
    {
        var a = await CreateUserAsync("a");
        var b = await CreateUserAsync("b");

        await _service.UpsertBatchAsync(a.Id, "100", new() { Entry("1", "{\"u\":\"a\"}") });
        await _service.UpsertBatchAsync(b.Id, "100", new() { Entry("1", "{\"u\":\"b\"}") });
        await _service.UpsertBatchAsync(a.Id, "200", new() { Entry("1", "{\"u\":\"a2\"}") });

        Assert.Equal(3, await _db.ChessableReviewLines.CountAsync());   // (a,100,1),(b,100,1),(a,200,1)
    }

    // ===== Token-loser (anonymer) Pfad: uid-basiert sammeln, beim Verbinden claimen =====

    [Fact]
    public async Task AnonUpsert_ThenClaim_MovesLinesAndBuildsCourse()
    {
        var user = await CreateUserAsync();
        await GiveUserCoursesAsync(user, FixtureBid);
        const string uid = "790927";

        // token-los: Linie landet in der Anon-Senke (keine UserId), NICHT in ChessableReviewLines
        var storedAnon = await _service.UpsertAnonBatchAsync(uid, FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });
        Assert.Equal(1, storedAnon);
        Assert.Equal(1, await _db.AnonymousChessableReviewLines.CountAsync());
        Assert.Equal(0, await _db.ChessableReviewLines.CountAsync());

        // User verknüpft später seinen Bearer (uid daraus decodiert) → Claim übernimmt + baut den Kurs
        var claimed = await _service.ClaimAnonForUidAsync(user.Id, uid);

        Assert.Equal(1, claimed);
        Assert.Equal(0, await _db.AnonymousChessableReviewLines.CountAsync());   // Anon-Senke geleert
        var owned = await _db.ChessableReviewLines.SingleAsync();
        Assert.Equal(user.Id, owned.UserId);
        Assert.Equal(FixtureOid, owned.Oid);
        // Kurs wurde aufgebaut: BookPuzzle mit Source="review"
        var fileName = $"chessable-u{user.Id}-{FixtureBid}.pgn";
        var bp = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);
        Assert.Equal("review", bp.Source);
        Assert.Equal(FixtureOid, bp.ChessableOid);
    }

    [Fact]
    public async Task ClaimAnon_IsIdempotent_AndNoOpForUnknownUid()
    {
        var user = await CreateUserAsync();
        await GiveUserCoursesAsync(user, FixtureBid);
        await _service.UpsertAnonBatchAsync("790927", FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });

        Assert.Equal(1, await _service.ClaimAnonForUidAsync(user.Id, "790927"));
        Assert.Equal(0, await _service.ClaimAnonForUidAsync(user.Id, "790927"));   // nichts mehr da
        Assert.Equal(0, await _service.ClaimAnonForUidAsync(user.Id, "999999"));   // fremde uid
    }

    [Fact]
    public async Task GetGameUpgradeImport_AfterMerge_ReplacesFillerInPlace_NoDuplicate()
    {
        var user = await CreateUserAsync();
        await _service.UpsertBatchAsync(user.Id, FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });
        await _service.MergeIntoCourseAsync(user.Id, FixtureBid);

        var fileName = $"chessable-u{user.Id}-{FixtureBid}.pgn";
        // Pipeline-Version bumpt später → Buch gilt als VERALTET (Upgrade-Pfad beim nächsten Import).
        var book = await _db.Books.SingleAsync(b => b.FileName == fileName);
        book.ImportVersion = 0;
        await _db.SaveChangesAsync();

        // getGame-Re-Import des veralteten Buchs über dieselbe oid → ersetzt den Review-Füller in-place;
        // KEIN Duplikat (früherer Bug: reviewByOid war beim Upgrade leer → zweite Linie).
        await new PgnImportService(_db).ImportFileAsync(fileName, GetGamePgnForOid(FixtureOid), CancellationToken.None);

        Assert.Equal(1, await _db.BookPuzzles.CountAsync(b => b.BookFileName == fileName));   // genau EINE Linie
        var bp = await _db.BookPuzzles.SingleAsync(b => b.BookFileName == fileName);
        Assert.Null(bp.Source);   // getGame gewinnt
    }

    [Fact]
    public async Task AnonUpsert_PerUidCap_RejectsNewOids_ButUpdatesExisting()
    {
        var cap = ChessableReviewLineService.MaxAnonRowsPerUid;
        // uid bis zum Deckel füllen (eine bestehende oid = "0" merken wir uns für den Update-Fall).
        var seed = Enumerable.Range(0, cap).Select(i => new AnonymousChessableReviewLine
        { ChessableUid = "42", Bid = "1", Oid = i.ToString(), Json = "{\"v\":1}" }).ToList();
        _db.AnonymousChessableReviewLines.AddRange(seed);
        await _db.SaveChangesAsync();

        // NEUE oid jenseits des Deckels → abgewiesen (kein Wachstum).
        await _service.UpsertAnonBatchAsync("42", "1", new() { Entry("999999", "{\"v\":9}") });
        Assert.Equal(cap, await _db.AnonymousChessableReviewLines.CountAsync(r => r.ChessableUid == "42"));
        Assert.Equal(0, await _db.AnonymousChessableReviewLines.CountAsync(r => r.Oid == "999999"));

        // BESTEHENDE oid → Update bleibt erlaubt (kein neuer Datensatz).
        await _service.UpsertAnonBatchAsync("42", "1", new() { Entry("0", "{\"v\":2}") });
        Assert.Equal(cap, await _db.AnonymousChessableReviewLines.CountAsync(r => r.ChessableUid == "42"));
        Assert.Equal("{\"v\":2}", (await _db.AnonymousChessableReviewLines.SingleAsync(r => r.ChessableUid == "42" && r.Oid == "0")).Json);
    }

    [Fact]
    public async Task AnonUpsert_RejectsNonNumericUid()
    {
        var stored = await _service.UpsertAnonBatchAsync("abc", FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });
        Assert.Equal(0, stored);
        Assert.Equal(0, await _db.AnonymousChessableReviewLines.CountAsync());
    }

    /// <summary>Ein JSON-Eintrag von genau <paramref name="bytes"/> UTF-8-Bytes (nur ASCII).</summary>
    private static string JsonOfBytes(int bytes) => "{\"x\":\"" + new string('a', bytes - 8) + "\"}";

    [Fact]
    public async Task AnonUpsert_PerEntryCap_CountsUtf8Bytes()
    {
        // Der Deckel je Eintrag zählte ZEICHEN (256 K): „€" sind in MariaDB drei Bytes, er ließ also das Dreifache
        // durch — und lag ohnehin beim Mehrfachen jeder realen getReview-Antwort (Prod-Maximum anonym 34 KB).
        var multiByte = "{\"x\":\"" + new string('€', 50_000) + "\"}";   // nur ~50 K Zeichen, aber ~150 KB
        var tooBig = JsonOfBytes(ChessableReviewLineService.MaxAnonJsonBytes + 1);
        var fits = JsonOfBytes(ChessableReviewLineService.MaxAnonJsonBytes);

        var stored = await _service.UpsertAnonBatchAsync("42", "1", new()
        {
            Entry("1", multiByte), Entry("2", tooBig), Entry("3", fits),
        });

        Assert.Equal(1, stored);
        Assert.Equal("3", (await _db.AnonymousChessableReviewLines.SingleAsync()).Oid);
    }

    [Fact]
    public async Task AnonUpsert_TotalByteCap_BlocksGrowth_ButKeepsNonGrowingUpdates()
    {
        // Der Gesamtdeckel zählte nur ZEILEN: 200 000 × 256 K Zeichen ≈ 51 GB ohne Konto. Jetzt zählen Bytes —
        // über dem Deckel keine neue Zeile (auch nicht unter einer frischen uid) und kein Wachstum bestehender.
        var svc = new ChessableReviewLineService(_db, new PgnImportService(_db)) { AnonBytesCap = 100 };
        Assert.Equal(1, await svc.UpsertAnonBatchAsync("42", "1", new() { Entry("1", JsonOfBytes(60)) }));

        Assert.Equal(0, await svc.UpsertAnonBatchAsync("43", "1", new() { Entry("2", JsonOfBytes(50)) }));   // 60 + 50 > 100
        Assert.Equal(0, await svc.UpsertAnonBatchAsync("42", "1", new() { Entry("1", JsonOfBytes(120)) }));  // 60 → 120
        Assert.Equal(60, (await _db.AnonymousChessableReviewLines.SingleAsync()).Json.Length);

        // Verkleinern bleibt erlaubt — und macht Platz für die neue Zeile.
        Assert.Equal(1, await svc.UpsertAnonBatchAsync("42", "1", new() { Entry("1", JsonOfBytes(30)) }));
        Assert.Equal(1, await svc.UpsertAnonBatchAsync("43", "1", new() { Entry("2", JsonOfBytes(50)) }));   // 30 + 50
        Assert.Equal(2, await _db.AnonymousChessableReviewLines.CountAsync());
    }

    [Fact]
    public async Task AnonUpsert_TotalByteCap_WithCache_CountsOwnWritesBetweenRecounts()
    {
        // Mit Cache wird die Summe nicht je Anfrage neu gezählt — die eigenen Schreibvorgänge müssen also auf die
        // gemerkte Summe aufgeschlagen werden, sonst liefe die Senke bis zum nächsten Nachzählen ungebremst voll.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new ChessableReviewLineService(_db, new PgnImportService(_db), sinkBytes: new ChessableSinkBytes(cache))
        { AnonBytesCap = 100 };

        Assert.Equal(1, await svc.UpsertAnonBatchAsync("42", "1", new() { Entry("1", JsonOfBytes(60)) }));
        Assert.Equal(0, await svc.UpsertAnonBatchAsync("43", "1", new() { Entry("2", JsonOfBytes(50)) }));
        Assert.Equal(1, await svc.UpsertAnonBatchAsync("43", "1", new() { Entry("2", JsonOfBytes(40)) }));
    }

    [Fact]
    public async Task UpsertBatch_OverAccountByteQuota_BlocksNewAndGrowingLines()
    {
        // Vorher kein Deckel je Konto: beliebige bid/oid-Paare, je Batch 500 × 256 K Zeichen. Jetzt ein Byte-Kontingent
        // (JSON-Bytes + Aufschlag je Zeile): darüber keine neue Linie und kein Wachstum; Verkleinern geht immer.
        var user = await CreateUserAsync();
        var svc = new ChessableReviewLineService(_db, new PgnImportService(_db)) { UserBytesCap = 400 };

        Assert.Equal(1, await svc.UpsertBatchAsync(user.Id, "1", new() { Entry("1", JsonOfBytes(100)) }));   // 100 + 128
        Assert.Equal(0, await svc.UpsertBatchAsync(user.Id, "1", new() { Entry("2", JsonOfBytes(100)) }));   // 228 + 228
        Assert.Equal(0, await svc.UpsertBatchAsync(user.Id, "1", new() { Entry("1", JsonOfBytes(300)) }));   // 228 + 200
        Assert.Equal(100, (await _db.ChessableReviewLines.SingleAsync()).Json.Length);

        Assert.Equal(1, await svc.UpsertBatchAsync(user.Id, "1", new() { Entry("1", JsonOfBytes(40)) }));    // 168
        Assert.Equal(1, await svc.UpsertBatchAsync(user.Id, "1", new() { Entry("2", JsonOfBytes(100)) }));   // 168 + 228
        Assert.Equal(2, await _db.ChessableReviewLines.CountAsync());
    }

    [Fact]
    public async Task PruneAnon_RemovesOnlyOldUnclaimedRows()
    {
        _db.AnonymousChessableReviewLines.Add(new AnonymousChessableReviewLine
        { ChessableUid = "1", Bid = "228856", Oid = "1", Json = "{}", UpdatedAt = DateTime.UtcNow.AddDays(-120) });
        _db.AnonymousChessableReviewLines.Add(new AnonymousChessableReviewLine
        { ChessableUid = "1", Bid = "228856", Oid = "2", Json = "{}", UpdatedAt = DateTime.UtcNow.AddDays(-10) });
        await _db.SaveChangesAsync();

        var pruned = await _service.PruneAnonOlderThanAsync(TimeSpan.FromDays(90));

        Assert.Equal(1, pruned);
        Assert.Equal("2", (await _db.AnonymousChessableReviewLines.SingleAsync()).Oid);   // die junge bleibt
    }

    [Fact]
    public async Task ClaimAnon_SkipsCoursesTheUserDoesNotOwn_AndBuildsNoBook()
    {
        // Angriffsfall: die ABLAGE-Seite der Anon-Senke ist unauthentifiziert und kann die uid nicht
        // prüfen (nur der Claim beweist sie gegen Chessable). Jeder kann also unter einer fremden,
        // durchprobierbaren uid Linien einwerfen. Vorher übernahm der Claim ALLES und legte daraus
        // echte Bücher an — mit frei gewähltem Namen und erfundenen Zügen in einem Kurs, den das
        // Opfer nie besaß. Jetzt zählt nur, was in seiner eigenen Chessable-Kursliste steht.
        var user = await CreateUserAsync();
        await GiveUserCoursesAsync(user, FixtureBid);                 // NUR dieser Kurs gehört ihm
        await _service.UpsertAnonBatchAsync("790927", FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });
        await _service.UpsertAnonBatchAsync("790927", "99999", new() { Entry("1", LoadFixture()) });

        var claimed = await _service.ClaimAnonForUidAsync(user.Id, "790927");

        Assert.Equal(1, claimed);                                     // nur die eigene Linie
        Assert.Equal(FixtureBid, (await _db.ChessableReviewLines.SingleAsync()).Bid);
        Assert.False(await _db.BookPuzzles.AnyAsync(b => b.BookFileName == $"chessable-u{user.Id}-99999.pgn"));
        // Die untergeschobene Zeile bleibt in der Anon-Senke liegen und verfällt über die Retention.
        Assert.Equal(1, await _db.AnonymousChessableReviewLines.CountAsync());
    }

    [Fact]
    public async Task ClaimAnon_WithoutKnownCourseList_TakesNothingYet()
    {
        // Ohne gecachte Kursliste ist Besitz nicht feststellbar. Dann wird NICHTS übernommen (die
        // Zeilen bleiben liegen); der nächtliche Kurslisten-Refresh holt den Claim danach nach.
        var user = await CreateUserAsync();
        await _service.UpsertAnonBatchAsync("790927", FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });

        Assert.Equal(0, await _service.ClaimAnonForUidAsync(user.Id, "790927"));
        Assert.Equal(1, await _db.AnonymousChessableReviewLines.CountAsync());
    }

    [Fact]
    public async Task PruneUnlinkedAnon_KeepsRowsOfLinkedUids()
    {
        // Der Deckel je uid bindet nichts, solange die uid ein frei wählbares Feld des offenen
        // Endpoints ist. Deshalb verfallen Zeilen ohne verknüpftes Konto deutlich früher — die eines
        // verknüpften Kontos (claimbar) behalten die volle Frist.
        var user = await CreateUserAsync();
        await GiveUserCoursesAsync(user, FixtureBid);
        var cred = await _db.ChessableCredentials.FirstAsync();
        cred.ChessableUid = "790927";
        await _service.UpsertAnonBatchAsync("790927", FixtureBid, new() { Entry(FixtureOid, LoadFixture()) });
        await _service.UpsertAnonBatchAsync("111111", FixtureBid, new() { Entry("2", LoadFixture()) });
        foreach (var row in await _db.AnonymousChessableReviewLines.ToListAsync())
            row.UpdatedAt = DateTime.UtcNow.AddDays(-30);
        await _db.SaveChangesAsync();

        var pruned = await _service.PruneUnlinkedAnonOlderThanAsync(TimeSpan.FromDays(14));

        Assert.Equal(1, pruned);
        Assert.Equal("790927", (await _db.AnonymousChessableReviewLines.SingleAsync()).ChessableUid);
    }
}
