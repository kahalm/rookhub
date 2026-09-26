using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Kurs-Uebersetzung gegen ECHTES MariaDB: der eindeutige Index (BookPuzzleId, Language), die Abfragen des
/// Laufs (offene Arbeit ueber CommentSet → BookPuzzle, Wiederverwendung als GROUP BY SourceHash + MAX(Id),
/// Stichprobe fuer die Quellsprache), die des Localizers (Quellsprache ueber das Buch, IN-Listen, Kapitel-Labels
/// mit DISTINCT ueber LONGTEXT) und die Loeschpfade, in denen die TEXTE dem Fremdschluessel ueberlassen
/// bleiben. InMemory saehe keinen dieser Punkte. Das Modell ist ausgetauscht — nie ein echtes.
/// </summary>
public class CourseTranslationSqlTests(CourseTranslationFixture fixture)
    : IAsyncLifetime, IClassFixture<CourseTranslationFixture>
{
    private ServiceProvider _provider = null!;
    private readonly List<IServiceScope> _scopes = new();
    private readonly EchoTranslator _llm = new();

    /// <summary>Reicht jeden Eintrag mit „DE:" durch und zaehlt die Aufrufe.</summary>
    private sealed class EchoTranslator : IClaudeJsonClient
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public bool IsConfigured => true;
        public string TranslationModel => "echo";
        public Task<string?> GenerateHintsJsonAsync(string system, string prompt, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string prompt, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            using var doc = JsonDocument.Parse(prompt);
            var items = doc.RootElement.GetProperty("items").EnumerateArray()
                .Select(i => new { ply = i.GetProperty("ply").GetInt32(), text = "DE:" + i.GetProperty("text").GetString() });
            return Task.FromResult<string?>(JsonSerializer.Serialize(new { items }));
        }
    }

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        if (fixture.Schema is null) return;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["CourseTranslation:Parallel"] = "3" }).Build());
        services.AddSingleton<IClaudeJsonClient>(_llm);
        services.AddDbContext<AppDbContext>(o =>
            o.UseMySql(fixture.Schema.ConnectionString, new MySqlServerVersion(new Version(11, 0, 0))));
        services.AddScoped<CourseTranslationService>();
        services.AddScoped<CourseCommentLocalizer>();
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        foreach (var s in _scopes) s.Dispose();
        if (_provider is not null) await _provider.DisposeAsync();
    }

    private T Get<T>() where T : notnull
    {
        var scope = _provider.CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    private async Task<Book> BookAsync(string? lang, string name = "Kurs")
    {
        var db = Get<AppDbContext>();
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = name, CommentLanguage = lang,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> LineAsync(Book book, string round, string chapter, string comment)
    {
        var db = Get<AppDbContext>();
        var line = new BookPuzzle
        {
            LineId = $"{book.FileName}:{round}", BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", Moves = "e2e4 e7e5",
            Title = "Titel " + round, Chapter = chapter, Comment = comment,
            MoveComments = JsonSerializer.Serialize(new Dictionary<int, string> { [-1] = comment, [0] = "Idee mit Nf3" }),
        };
        db.BookPuzzles.Add(line);
        await db.SaveChangesAsync();
        return line;
    }

    [MySqlFact]
    public async Task EindeutigerIndex_EinSatzJeLinieUndSprache()
    {
        var book = await BookAsync("en");
        var line = await LineAsync(book, "001", "Kapitel", "Kommentar");
        var db = Get<AppDbContext>();
        db.CommentSets.Add(new CommentSet { BookPuzzleId = line.Id, Language = "de", Origin = CommentOrigin.Machine });
        await db.SaveChangesAsync();

        var second = Get<AppDbContext>();
        second.CommentSets.Add(new CommentSet { BookPuzzleId = line.Id, Language = "de", Origin = CommentOrigin.Machine });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // Eine andere Sprache derselben Linie ist erlaubt.
        var third = Get<AppDbContext>();
        third.CommentSets.Add(new CommentSet { BookPuzzleId = line.Id, Language = "fr", Origin = CommentOrigin.Machine });
        await third.SaveChangesAsync();
    }

    [MySqlFact]
    public async Task KursLauf_UndLocalizer_AufMariaDb()
    {
        // Neutrale Texte: die Stichprobe laeuft (SQL mit Round-Sortierung), bestimmt aber keine Sprache → „und".
        // Englische Vorlagen gingen nicht: der Echo-Uebersetzer gaebe sie englisch zurueck, und die Sprachpruefung
        // des Kerns verwuerfe sie zu Recht.
        var book = await BookAsync(lang: null);
        var english = "Erste Idee: Aufbau am Damenfluegel";
        var l1 = await LineAsync(book, "001", "Kapitel A", english);
        var l2 = await LineAsync(book, "002", "Kapitel A", "Zweiter Kommentar");
        var l3 = await LineAsync(book, "010", "Kapitel B", "Dritter Kommentar");

        // (1) Erster Lauf: Quellsprache aus der Stichprobe, eine Kapitel-Fuhre + drei Linien.
        var run = await Get<CourseTranslationService>().TranslateCourseAsync(book.Id, "de");
        Assert.Equal(CourseTranslationRunStatus.Done, run.Status);
        Assert.Equal("und", run.SourceLanguage);
        Assert.Equal(3, run.LinesDone);
        Assert.Equal(0, run.LinesFailed);
        Assert.Equal(4, _llm.Calls);
        Assert.Equal("und", (await Get<AppDbContext>().Books.SingleAsync(b => b.Id == book.Id)).CommentLanguage);

        // (2) Zweiter Lauf: nichts offen — kein Aufruf.
        Assert.Equal(CourseTranslationRunStatus.NothingToDo,
            (await Get<CourseTranslationService>().TranslateCourseAsync(book.Id, "de")).Status);
        Assert.Equal(4, _llm.Calls);

        // (3) Eine Kopie des Kurses: alles per Fingerabdruck wiederverwendet (GROUP BY + MAX auf MariaDB).
        var copy = await BookAsync("en", "Kopie");
        var c1 = await LineAsync(copy, "001", "Kapitel A", english);
        var copyRun = await Get<CourseTranslationService>().TranslateCourseAsync(copy.Id, "de");
        Assert.Equal(1, copyRun.LinesDone);
        Assert.Equal(4, _llm.Calls);

        // (4) Ausliefern: Kommentare ersetzt, Titel/Kapitel bleiben, Labels gesetzt.
        var db = Get<AppDbContext>();
        var dtos = (await CourseService.PuzzlesWithBookInReadingOrder(db, book.Id).ToListAsync())
            .Select(BookPuzzleService.MapToDto).ToList();
        await Get<CourseCommentLocalizer>().ApplyAsync(dtos, "de");
        var first = dtos.Single(d => d.Id == l1.Id);
        Assert.Equal("DE:" + english, first.Comment);
        Assert.Equal("DE:" + english, first.MoveComments![-1]);
        Assert.Equal("DE:Idee mit Nf3", first.MoveComments[0]);   // Quelle „und": Buchstaben bleiben
        Assert.Equal("Kapitel A", first.Chapter);
        Assert.Equal("DE:Kapitel A", first.ChapterLabel);
        Assert.Equal("DE:Titel 001", first.TitleLabel);
        Assert.Equal(new[] { "und", "de" }, first.CommentLanguages);
        Assert.Equal("DE:Dritter Kommentar", dtos.Single(d => d.Id == l3.Id).Comment);

        var chapters = new List<CourseChapterDto> { new() { Name = "Kapitel A" }, new() { Name = "Kapitel B" } };
        await Get<CourseCommentLocalizer>().ApplyAsync(book.Id, chapters, "de");
        Assert.Equal(new[] { "DE:Kapitel A", "DE:Kapitel B" }, chapters.Select(c => c.Label));

        var position = new CalcPositionDto { Id = l2.Id, Title = "Titel 002", Chapter = "Kapitel A", Comment = "Zweiter Kommentar" };
        await Get<CourseCommentLocalizer>().ApplyAsync(position, "de");
        Assert.Equal("DE:Zweiter Kommentar", position.Comment);

        var copyDto = BookPuzzleService.MapToDto(await Get<AppDbContext>().BookPuzzles.Include(bp => bp.Book)
            .SingleAsync(bp => bp.Id == c1.Id));
        await Get<CourseCommentLocalizer>().ApplyAsync(copyDto, "de");
        Assert.Equal("DE:" + english, copyDto.Comment);
    }

    /// <summary>Relational bleiben die Texte dem Fremdschluessel ueberlassen (Cascade von CommentSets) — dieser
    /// Test belegt, dass dabei nichts stehen bleibt und nichts blockiert.</summary>
    [MySqlFact]
    public async Task Loeschpfade_RaeumenSaetzeTexteUndAuftraegeAb()
    {
        var book = await BookAsync("en");
        var keep = await LineAsync(book, "001", "Eins", "Bleibt");
        var doomed = await LineAsync(book, "002", "Eins", "Geht");
        await Get<CourseTranslationService>().TranslateCourseAsync(book.Id, "de");
        var jobs = Get<AppDbContext>();
        jobs.CourseTranslationJobs.Add(new CourseTranslationJob { BookId = book.Id, Language = "de" });
        await jobs.SaveChangesAsync();

        await new CourseAuthoringService(Get<AppDbContext>()).DeleteLineAsync(0, book.Id, doomed.Id, isAdmin: true);

        var db = Get<AppDbContext>();
        Assert.Equal(new[] { keep.Id }, await db.CommentSets.Select(s => s.BookPuzzleId!.Value).ToListAsync());
        Assert.False(await db.CommentTexts.AnyAsync(t => !db.CommentSets.Any(s => s.Id == t.CommentSetId)));
        Assert.True(await db.CommentTexts.AnyAsync());

        await new BookAdminService(Get<AppDbContext>()).DeleteBookAsync(book.Id);

        var after = Get<AppDbContext>();
        Assert.Empty(await after.CommentSets.ToListAsync());
        Assert.Empty(await after.CommentTexts.ToListAsync());
        Assert.Empty(await after.CourseTranslationJobs.ToListAsync());
    }
}
