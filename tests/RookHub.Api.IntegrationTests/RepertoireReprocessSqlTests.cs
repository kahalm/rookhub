using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Status und Lauf der Repertoire-Aufbereitung beantworten „Chessable-Repertoire? trägt eine Datei
/// <c>[ChessableOid]</c>?" seit 0.510.0 per Projektion + SQL-<c>LIKE</c>. Vorher luden beide jedes Repertoire mit
/// <c>.Include(r =&gt; r.Files)</c> — also jeden PGN-Text (Prod: ~250 MB Chessable-PGN) — nur für diese zwei Fragen.
///
/// <para>Warum gegen MariaDB: InMemory rechnet die Projektion im Speicher und sagt nichts darüber, ob sie als SQL
/// übersetzt wird und welche Spalten dabei über die Leitung gehen. Gezählt wird deshalb am abgesetzten SQL: ein
/// <c>`PgnContent`</c>, dem kein <c>LIKE</c> folgt, ist eine Stelle, an der der Text geladen wird.</para>
/// </summary>
public class RepertoireReprocessSqlTests(RepertoireReprocessFixture fixture)
    : IAsyncLifetime, IClassFixture<RepertoireReprocessFixture>
{
    private readonly List<AppDbContext> _contexts = new();

    public async Task InitializeAsync() => await fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var db in _contexts) await db.DisposeAsync();
    }

    private const string PlainPgn = "[Event \"X\"]\n[Round \"1\"]\n"
        + "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n"
        + "2. Nf3 {Develops.} Nc6 3. Bb5 {The pin.} a6 *\n";

    // Dieselbe Linie wie SameLineCache.Line (oid 10, gleicher Zugtext): der Cache-Weg läuft durch, der Text bleibt.
    private const string ModernPgn = "[Event \"X\"]\n[Round \"1\"]\n"
        + "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n[ChessableOid \"10\"]\n\n"
        + "2. Nf3 {[%alt g1e2] Develops.} Nc6 3. Bb5 {The pin.} a6 *\n";

    /// <summary>Eine Erwähnung der Textspalte, die KEIN <c>LIKE</c> ist — also ein Ladevorgang (SELECT-Liste) oder
    /// ein Schreiben (<c>SET `PgnContent` = @p</c>).</summary>
    private static readonly Regex TextColumnNotLike = new(@"`PgnContent`(?!\s+LIKE)", RegexOptions.Compiled);

    private static List<string> TextLoads(CommandTextRecorder recorder) =>
        recorder.Commands.Where(c => TextColumnNotLike.IsMatch(c)).ToList();

    private AppDbContext Fresh(CommandTextRecorder? recorder = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, new MySqlServerVersion(new Version(11, 0, 0)));
        if (recorder != null) builder.AddInterceptors(recorder);
        var db = new AppDbContext(builder.Options);
        _contexts.Add(db);
        return db;
    }

    private ImportReprocessService Reprocess(AppDbContext db) =>
        new(db, new PgnImportService(db), new NoRefetch(), NullLogger<ImportReprocessService>.Instance,
            cachedLines: new SameLineCache());

    private async Task<int> UserAsync(string name)
    {
        var db = Fresh();
        var u = new AppUser { Username = name, Email = $"{name}@t.local", PasswordHash = "x" };
        db.AppUsers.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> RepertoireAsync(int uid, string name, string? courseId, int importVersion,
        params (string FileName, string Pgn)[] files)
    {
        var db = Fresh();
        var rep = new Repertoire { UserId = uid, Name = name, ChessableCourseId = courseId, ImportVersion = importVersion };
        foreach (var (fileName, pgn) in files)
            rep.Files.Add(new RepertoireFile { FileName = fileName, PgnContent = pgn, FileSize = Encoding.UTF8.GetByteCount(pgn) });
        db.Repertoires.Add(rep);
        await db.SaveChangesAsync();
        return rep.Id;
    }

    [MySqlFact]
    public async Task GetRepertoireStatus_ProjiziertHerkunftUndMarkerInSql_OhneDenText()
    {
        var uid = await UserAsync("rstatus");
        await RepertoireAsync(uid, "eigen", null, 0, ("eigen.pgn", PlainPgn));                 // Versions-Mark
        await RepertoireAsync(uid, "upload", null, 0, ("upload.pgn", ModernPgn));              // oids, kein Chessable → Versions-Mark
        await RepertoireAsync(uid, "alt", null, 0, ("chessable-111.pgn", PlainPgn));           // Re-Fetch
        await RepertoireAsync(uid, "modern", "222", 0, ("chessable-222.pgn", ModernPgn), ("notizen.pgn", PlainPgn)); // Cache
        await RepertoireAsync(uid, "aktuell", null, ImportPipeline.CurrentVersion, ("aktuell.pgn", PlainPgn));

        var recorder = new CommandTextRecorder();
        var status = await Reprocess(Fresh(recorder)).GetRepertoireStatusAsync(uid);

        Assert.Equal(5, status.Total);
        Assert.Equal(4, status.Stale);
        Assert.Equal(3, status.ReprocessableLocally);   // eigen + upload + modern (Cache zählt mit)
        Assert.Equal(1, status.FromCache);
        Assert.Equal(1, status.Refetchable);            // eigener Chessable-Weg: Vorgabe an
        Assert.Equal(0, status.NeedsReimport);
        Assert.Contains(recorder.Commands, c => Regex.IsMatch(c, @"`PgnContent`\s+LIKE"));
        Assert.Empty(TextLoads(recorder));
    }

    [MySqlFact]
    public async Task ReprocessRepertoires_OhneCacheWeg_LaedtUndSchreibtKeinenText()
    {
        var uid = await UserAsync("rmark");
        var ids = new[]
        {
            await RepertoireAsync(uid, "eigen", null, 0, ("eigen.pgn", PlainPgn)),
            await RepertoireAsync(uid, "upload", null, 0, ("upload.pgn", ModernPgn)),
            // Chessable ohne oids, ohne Bearer, kein Admin → nie automatisch holbar → Versions-Mark.
            await RepertoireAsync(uid, "alt", null, 0, ("chessable-111.pgn", PlainPgn)),
        };

        var recorder = new CommandTextRecorder();
        var db = Fresh(recorder);
        var result = await Reprocess(db).ReprocessRepertoiresAsync(uid);

        Assert.Equal(3, result.Reprocessed);
        Assert.Equal(0, result.RebuiltFromCache);
        Assert.Equal(0, result.Failed);
        Assert.Empty(TextLoads(recorder));              // weder geladen noch geschrieben
        var versions = await Fresh().Repertoires.ToDictionaryAsync(r => r.Id, r => r.ImportVersion);
        Assert.All(ids, id => Assert.Equal(ImportPipeline.CurrentVersion, versions[id]));
    }

    [MySqlFact]
    public async Task ReprocessRepertoires_CacheWeg_LaedtNurDieDateiMitOids_Einmal()
    {
        var uid = await UserAsync("rcache");
        var id = await RepertoireAsync(uid, "modern", "222", 0, ("chessable-222.pgn", ModernPgn), ("notizen.pgn", PlainPgn));

        var recorder = new CommandTextRecorder();
        var db = Fresh(recorder);
        var result = await Reprocess(db).ReprocessRepertoiresAsync(uid);

        Assert.Equal(1, result.Reprocessed);
        Assert.Equal(1, result.RebuiltFromCache);
        Assert.Equal(1, result.CacheLinesReplaced);
        Assert.Equal(0, result.Failed);
        // Genau EIN Ladevorgang: die Datei mit oids. Die Notizen-Datei filtert das LIKE vorher heraus, und die
        // Status-/Lauf-Projektion lädt keinen Text.
        var loads = TextLoads(recorder);
        Assert.Single(loads);
        Assert.StartsWith("SELECT", loads[0].TrimStart());
        // Der Cache liefert denselben Zugtext — also auch kein UPDATE der LONGTEXT-Spalte, nur Version + Zeitstempel.
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("`PgnContent` = @"));
        Assert.Empty(db.ChangeTracker.Entries());

        var check = Fresh();
        Assert.Equal(ImportPipeline.CurrentVersion, (await check.Repertoires.SingleAsync(r => r.Id == id)).ImportVersion);
        var texts = await check.RepertoireFiles.Where(f => f.RepertoireId == id).ToDictionaryAsync(f => f.FileName, f => f.PgnContent);
        Assert.Equal(ModernPgn, texts["chessable-222.pgn"]);
        Assert.Equal(PlainPgn, texts["notizen.pgn"]);
    }
}
