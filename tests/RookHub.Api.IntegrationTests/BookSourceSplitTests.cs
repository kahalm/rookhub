using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>Merkt sich den Text aller abgesetzten Befehle (Lesen UND Schreiben — SaveChanges schickt
/// seine Stapel über einen Reader, weil es danach ROW_COUNT() liest).</summary>
public sealed class CommandTextRecorder : DbCommandInterceptor
{
    public List<string> Commands { get; } = new();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken ct = default)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        Commands.Add(command.CommandText);
        return base.NonQueryExecutingAsync(command, eventData, result, ct);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken ct = default)
    {
        Commands.Add(command.CommandText);
        return base.ScalarExecutingAsync(command, eventData, result, ct);
    }
}

/// <summary>Chessable-Re-Fetch-Stub: der Test läuft nur den lokalen Zweig, eingereiht wird nichts.</summary>
internal sealed class NoRefetch : ICourseReimporter
{
    public int Calls { get; private set; }

    public Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName,
        int? targetRepertoireId = null, bool? knownCached = null, bool trustOwnership = false, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult<int?>(null);
    }

    public Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default) => Task.FromResult(new HashSet<string>());
}

/// <summary>Linien-Cache-Stub: kennt nur die Linie mit oid 10 aus <c>ModernPgn</c> und liefert sie mit
/// DEMSELBEN Zugtext zurück (nur mit den Headern des Fake-Kapitels) — der Cache-Weg läuft also durch, der
/// Text bleibt aber gleich.</summary>
internal sealed class SameLineCache : ICachedLineSource
{
    public const string Line = "[Event \"x\"]\n[Round \"001.001\"]\n"
        + "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n[ChessableOid \"10\"]\n\n"
        + "2. Nf3 {[%alt g1e2] Develops.} Nc6 3. Bb5 {The pin.} a6 *";

    public Task<HashSet<string>> GetCachedLineOidsAsync(IReadOnlyCollection<string> oids, CancellationToken ct = default)
        => Task.FromResult(oids.Where(o => o == "10").ToHashSet());

    public Task<Dictionary<string, string>> GetCachedLinePgnsAsync(IEnumerable<string> oids, string mode = "None", CancellationToken ct = default)
        => Task.FromResult(oids.Where(o => o == "10").ToDictionary(o => o, _ => Line));
}

/// <summary>
/// Das Roh-PGN liegt seit 0.508.3 per TABELLENSPLITTING in <see cref="BookSource"/> — dieselbe Zeile
/// in <c>Books</c>, aber eine eigene Entität, damit <c>.Include(bp =&gt; bp.Book)</c> es nicht mehr
/// mitlädt (vorher 11 GB aus der DB für EINEN Kurs-Puzzle-Request).
///
/// <para>Warum gegen MariaDB: InMemory kennt kein Tabellensplitting (dort sind Book und BookSource
/// zwei getrennte „Tabellen"). Ob Anlegen, Ändern und Löschen auf der GETEILTEN Zeile richtig
/// ankommen — insbesondere, dass ein Buch-Update ohne geladene Source den Text NICHT überschreibt
/// und ein Löschen ohne geladene Source die Zeile trotzdem entfernt —, zeigt sich nur hier. Ebenso, ob die
/// Quell-Flags (<c>b.Source.SourcePgn != null …</c>, <c>LIKE '%[ChessableOid%'</c>) als SQL laufen und für
/// NULL / leer / modern / nicht-modern das Richtige liefern.</para>
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class BookSourceSplitTests(BookSourceSplitFixture fixture)
    : IAsyncLifetime, IClassFixture<BookSourceSplitFixture>
{
    private readonly List<AppDbContext> _contexts = new();

    public async Task InitializeAsync() => await fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var db in _contexts) await db.DisposeAsync();
    }

    private AppDbContext Fresh(CommandTextRecorder? recorder = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, new MySqlServerVersion(new Version(11, 0, 0)));
        if (recorder != null) builder.AddInterceptors(recorder);
        var db = new AppDbContext(builder.Options);
        _contexts.Add(db);
        return db;
    }

    /// <summary>Spalte direkt per SQL lesen — am Mapping vorbei.</summary>
    private async Task<string?> RawSourcePgnAsync(int bookId) =>
        await Fresh().Database
            .SqlQuery<string?>($"SELECT SourcePgn AS Value FROM Books WHERE Id = {bookId}")
            .SingleAsync();

    private async Task<int> SeedAsync(string fileName, string? sourcePgn, int? owner = null,
        int importVersion = 0, string? tags = null)
    {
        var db = Fresh();
        var book = new Book
        {
            FileName = fileName, DisplayName = fileName, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            OwnerUserId = owner, ImportVersion = importVersion, Tags = tags,
            Source = new BookSource { SourcePgn = sourcePgn },
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [MySqlFact]
    public async Task Anlegen_SchreibtDenTextInDieSpalteDerBuchzeile()
    {
        var mit = await SeedAsync("mit.pgn", "1. e4 e5 *");
        var ohne = await SeedAsync("ohne.pgn", null);

        Assert.Equal("1. e4 e5 *", await RawSourcePgnAsync(mit));
        Assert.Null(await RawSourcePgnAsync(ohne));
        Assert.Equal(2, await Fresh().Books.CountAsync());   // eine Zeile je Buch, keine Extratabelle
    }

    [MySqlFact]
    public async Task Laden_OhneInclude_Null_MitInclude_InstanzAuchOhneText()
    {
        var mit = await SeedAsync("mit.pgn", "1. d4 *");
        var ohne = await SeedAsync("ohne.pgn", null);

        Assert.Null((await Fresh().Books.SingleAsync(b => b.Id == mit)).Source);

        var db = Fresh();
        var a = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == mit);
        var b = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == ohne);
        Assert.Equal("1. d4 *", a.Source.SourcePgn);
        Assert.NotNull(b.Source);
        Assert.Null(b.Source.SourcePgn);
    }

    [MySqlFact]
    public async Task BuchAendern_OhneGeladeneSource_LaesstDenTextStehen()
    {
        var id = await SeedAsync("k.pgn", "1. c4 *");

        var db = Fresh();
        var book = await db.Books.SingleAsync(b => b.Id == id);
        book.DisplayName = "Umbenannt";
        await db.SaveChangesAsync();

        Assert.Equal("1. c4 *", await RawSourcePgnAsync(id));
    }

    [MySqlFact]
    public async Task Schreiben_UeberInclude_AendertNurDenText()
    {
        var id = await SeedAsync("k.pgn", "alt");

        var db = Fresh();
        var book = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == id);
        book.Source.SourcePgn = "neu";
        await db.SaveChangesAsync();

        Assert.Equal("neu", await RawSourcePgnAsync(id));
        Assert.Equal("k.pgn", (await Fresh().Books.SingleAsync(b => b.Id == id)).DisplayName);
    }

    [MySqlFact]
    public async Task DeleteBook_OhneGeladeneSource_EntferntDieZeile()
    {
        var id = await SeedAsync("weg.pgn", "1. f4 *");
        var bleibt = await SeedAsync("bleibt.pgn", "1. b3 *");

        await new BookAdminService(Fresh()).DeleteBookAsync(id);

        var db = Fresh();
        Assert.False(await db.Books.AnyAsync(b => b.Id == id));
        Assert.Equal("1. b3 *", await RawSourcePgnAsync(bleibt));
    }

    [MySqlFact]
    public async Task KursPuzzleAbfrage_UebertraegtKeinSourcePgn()
    {
        var id = await SeedAsync("kurs.pgn", new string('x', 100_000));
        var seed = Fresh();
        seed.BookPuzzles.Add(new BookPuzzle { LineId = "kurs.pgn:1", BookId = id, BookFileName = "kurs.pgn", Round = "1", Fen = "f", Moves = "e2e4" });
        seed.BookPuzzles.Add(new BookPuzzle { LineId = "kurs.pgn:2", BookId = id, BookFileName = "kurs.pgn", Round = "2", Fen = "f", Moves = "d2d4" });
        await seed.SaveChangesAsync();

        var recorder = new CommandTextRecorder();
        var puzzles = await CourseService.PuzzlesWithBookInReadingOrder(Fresh(recorder), id).ToListAsync();

        Assert.Equal(2, puzzles.Count);
        Assert.All(puzzles, p => Assert.Equal("kurs.pgn", p.Book!.DisplayName));
        Assert.All(puzzles, p => Assert.Null(p.Book!.Source));
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("SourcePgn"));
    }

    [MySqlFact]
    public async Task NeuesBuchOhneSource_WirdNichtGeschrieben()
    {
        // Relational fiele es sonst nicht auf: INSERT ohne die Spalte, SourcePgn = NULL.
        var db = Fresh();
        var book = new Book { FileName = "ohne.pgn", DisplayName = "Ohne" };

        Assert.Throws<InvalidOperationException>(() => db.Books.Add(book));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Equal(0, await Fresh().Books.CountAsync());
    }

    // ===== Quell-Flags: NULL / leer / nicht-modern / modern =====

    private const string PlainPgn = "[Event \"X\"]\n[Round \"1\"]\n"
        + "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n"
        + "2. Nf3 {Develops.} Nc6 3. Bb5 {The pin.} a6 *\n";

    private const string ModernPgn = "[Event \"X\"]\n[Round \"1\"]\n"
        + "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n[ChessableOid \"10\"]\n\n"
        + "2. Nf3 {[%alt g1e2] Develops.} Nc6 3. Bb5 {The pin.} a6 *\n";

    private async Task<int> UserAsync(string name)
    {
        var db = Fresh();
        var u = new AppUser { Username = name, Email = $"{name}@t.local", PasswordHash = "x" };
        db.AppUsers.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    /// <summary>Fünf veraltete eigene Bücher — je ein Quell-Zustand — plus ein aktuelles ohne Quelle.</summary>
    private async Task<Dictionary<string, int>> SeedQuellZustaendeAsync(int uid) => new()
    {
        ["null"] = await SeedAsync("null.pgn", null, owner: uid),
        ["leer"] = await SeedAsync("leer.pgn", "", owner: uid),
        ["plain"] = await SeedAsync("plain.pgn", PlainPgn, owner: uid),
        ["cb-alt"] = await SeedAsync($"chessable-u{uid}-111.pgn", PlainPgn, owner: uid, tags: "chessable"),
        ["cb-modern"] = await SeedAsync($"chessable-u{uid}-222.pgn", ModernPgn, owner: uid, tags: "chessable"),
        ["aktuell"] = await SeedAsync("aktuell.pgn", null, owner: uid, importVersion: ImportPipeline.CurrentVersion),
    };

    private ImportReprocessService Reprocess(AppDbContext db) =>
        new(db, new PgnImportService(db), new NoRefetch(), NullLogger<ImportReprocessService>.Instance,
            cachedLines: new SameLineCache());

    [MySqlFact]
    public async Task GetCourseStatus_ProjiziertDieQuellFlagsInSql()
    {
        var uid = await UserAsync("status");
        await SeedQuellZustaendeAsync(uid);

        var recorder = new CommandTextRecorder();
        var status = await Reprocess(Fresh(recorder)).GetCourseStatusAsync(uid, isAdmin: false);

        Assert.Equal(6, status.Total);
        Assert.Equal(5, status.Stale);
        Assert.Equal(2, status.ReprocessableLocally);   // plain + Chessable modern (Cache-Weg zählt mit)
        Assert.Equal(1, status.FromCache);              // Chessable modern
        Assert.Equal(1, status.Refetchable);            // Chessable ohne [ChessableOid]
        Assert.Equal(2, status.NeedsReimport);          // NULL + leer
        // Die Flags laufen als SQL (LIKE), der Text selbst wird nicht übertragen.
        Assert.Contains(recorder.Commands, c => c.Contains("LIKE") && c.Contains("SourcePgn"));
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("SourcePgn") && !c.Contains("LIKE"));
    }

    [MySqlFact]
    public async Task ReprocessCourses_LokalerPfad_LaedtJedenTextEinmal_UndSchreibtIhnNichtZurueck()
    {
        var uid = await UserAsync("reproc");
        var ids = await SeedQuellZustaendeAsync(uid);

        var recorder = new CommandTextRecorder();
        var db = Fresh(recorder);
        var result = await Reprocess(db).ReprocessCoursesAsync(uid, isAdmin: false, localOnly: true);

        Assert.Equal(2, result.Reprocessed);   // plain (lokal) + Chessable modern (Linien-Cache)
        Assert.Equal(1, result.RebuiltFromCache);
        Assert.Equal(2, result.Skipped);       // NULL + leer
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Enqueued);      // localOnly: Chessable ohne oid bleibt liegen

        // Lokal aufbereitet: GENAU ein Ladevorgang des Texts (das Include im Import-Kern) — vorher zwei
        // (Projektion + Include). Cache-Weg: zwei (ungetrackt zum Umschreiben, dann im Import-Kern). Die
        // Flag-Abfrage mit LIKE lädt ihn nicht.
        var loads = recorder.Commands.Where(c => c.Contains("SourcePgn") && !c.Contains("LIKE")).ToList();
        Assert.Equal(3, loads.Count);
        Assert.All(loads, c => Assert.StartsWith("SELECT", c.TrimStart()));
        // Der Text ist unverändert — also auch kein UPDATE der LONGTEXT-Spalte.
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("`SourcePgn` = @"));
        Assert.Empty(db.ChangeTracker.Entries());

        Assert.Equal(PlainPgn, await RawSourcePgnAsync(ids["plain"]));
        Assert.Equal(ModernPgn, await RawSourcePgnAsync(ids["cb-modern"]));
        var check = Fresh();
        var versions = await check.Books.ToDictionaryAsync(b => b.Id, b => b.ImportVersion);
        Assert.Equal(ImportPipeline.CurrentVersion, versions[ids["plain"]]);
        Assert.Equal(ImportPipeline.CurrentVersion, versions[ids["cb-modern"]]);
        Assert.Equal(0, versions[ids["null"]]);
        Assert.Equal(0, versions[ids["leer"]]);
        Assert.Equal(0, versions[ids["cb-alt"]]);
        Assert.Equal(1, await check.BookPuzzles.CountAsync(bp => bp.BookId == ids["plain"]));
        Assert.Equal("10", (await check.BookPuzzles.SingleAsync(bp => bp.BookId == ids["cb-modern"])).ChessableOid);
    }

    [MySqlFact]
    public async Task Kursliste_NeedsReimport_AusDenQuellFlags()
    {
        var uid = await UserAsync("liste");
        var ids = await SeedQuellZustaendeAsync(uid);

        using var scope = fixture.Factory.Services.CreateScope();
        var kurse = await scope.ServiceProvider.GetRequiredService<CourseService>().GetCoursesAsync(uid, isAdmin: false);

        var needs = kurse.ToDictionary(k => k.BookId, k => k.NeedsReimport);
        Assert.Equal(6, needs.Count);
        Assert.True(needs[ids["null"]]);
        Assert.True(needs[ids["leer"]]);
        Assert.False(needs[ids["plain"]]);       // lokal aufbereitbar
        Assert.False(needs[ids["cb-alt"]]);      // Re-Fetch (bzw. ohne Chessable-Weg: lokal)
        Assert.False(needs[ids["cb-modern"]]);   // aus dem Linien-Cache
        Assert.False(needs[ids["aktuell"]]);     // nicht veraltet — auch ohne Quelle kein (!)
    }
}
