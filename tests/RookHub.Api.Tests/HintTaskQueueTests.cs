using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Codereview W2 A4-002: jeder Import reihte die Tipp-Generierung (Stockfish + drei LLM-Aufrufe je Linie) für ALLE
/// Puzzles des Buchs auf der EINZIGEN allgemeinen Hintergrund-Queue ein — ein persönlicher Kurs mit 20 000 Linien hielt
/// Chessable-Import-Tickets, Turnierkarten und Abo-Prüfungen stundenlang auf. Jetzt: eigene Queue, ein wartender Lauf je
/// Buch, persönliche Kurse je Lauf und je Tag gedeckelt.
/// </summary>
public class HintTaskQueueTests : IDisposable
{
    private const string Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly AppDbContext _db;

    public HintTaskQueueTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeClaude : IClaudeJsonClient
    {
        public bool IsConfigured { get; set; } = true;
        public int Calls { get; private set; }
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>(
                """{"hint1":"Achte auf die Schwäche am Rand","hint2":"Deine Dame entscheidet","hint3":"Beginne mit Dame nach h8"}""");
        }
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public string TranslationModel => "test";
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private HintGenerationService Hints(IClaudeJsonClient claude)
    {
        // Kein Engine-Binary: die Analyse scheitert sofort, die Tipps entstehen ohne Engine-Signal.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stockfish:Path"] = "/nonexistent/stockfish" })
            .Build();
        return new HintGenerationService(_db, claude, new StockfishAnalyzer(config, NullLogger<StockfishAnalyzer>.Instance),
            NullLogger<HintGenerationService>.Instance);
    }

    private async Task<int> SeedBookAsync(int? ownerUserId, int lines, int infoLines = 0, int hinted = 0)
    {
        var book = new Book
        {
            FileName = $"hints-{Guid.NewGuid():N}.pgn", DisplayName = "Kurs", OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        for (var i = 0; i < lines + infoLines + hinted; i++)
            _db.BookPuzzles.Add(new BookPuzzle
            {
                LineId = $"{book.FileName}:{i}", BookFileName = book.FileName, BookId = book.Id, Round = $"{i}",
                Fen = Fen, Moves = "d1h5 e8e7 h5f7", StartPly = -1,
                IsInfoOnly = i >= lines && i < lines + infoLines,
                HintsJson = i >= lines + infoLines ? """{"de":["a","b","c"]}""" : null,
                HintsVersion = i >= lines + infoLines ? HintGenerationService.CurrentHintsVersion : 0,
            });
        await _db.SaveChangesAsync();
        return book.Id;
    }

    // ---- Queue ----

    [Fact]
    public void TryEnqueueBook_SameBookTwice_OnlyOneWaitingRun()
    {
        var queue = new HintTaskQueue();

        Assert.True(queue.TryEnqueueBook(1, 5));
        Assert.True(queue.TryEnqueueBook(1, 5));   // ein kapitelweiser Import stößt je Chunk an
        Assert.True(queue.TryEnqueueBook(2, null));

        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public async Task TryEnqueueBook_OnceTheRunStarted_TheBookCanBeQueuedAgain()
    {
        var queue = new HintTaskQueue();
        var services = new ServiceCollection();
        services.AddSingleton(_ => Hints(new FakeClaude { IsConfigured = false }));
        using var sp = services.BuildServiceProvider();

        queue.TryEnqueueBook(1, 5);
        Assert.True(queue.TryDequeue(out var run));
        await run!(sp, CancellationToken.None);

        // Was ein Import NACH dem Start anlegt, sieht der laufende Lauf evtl. nicht mehr — also wieder einreihen.
        Assert.True(queue.TryEnqueueBook(1, 5));
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void TryEnqueueBook_FullQueue_DropsInsteadOfWaiting()
    {
        var queue = new HintTaskQueue();
        for (var book = 1; book <= 256; book++) Assert.True(queue.TryEnqueueBook(book, 5));

        // Voll: verwerfen (der nächste Import stößt erneut an) statt den Import-Request warten zu lassen.
        Assert.False(queue.TryEnqueueBook(257, 5));
        Assert.Equal(256, queue.PendingCount);
        // … und der verworfene Lauf sperrt sein Buch nicht als „wartet schon".
        Assert.True(queue.TryDequeue(out _));
        Assert.True(queue.TryEnqueueBook(257, 5));
    }

    [Fact]
    public void TakeDailyBudget_CapsPerUserAndDay_AndStartsFreshTheNextUtcDay()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero));
        var queue = new HintTaskQueue(time: time);

        Assert.Equal(200, queue.TakeDailyBudget(5, 200));
        Assert.Equal(100, queue.TakeDailyBudget(5, 200));   // nur noch der Rest bis 300
        Assert.Equal(0, queue.TakeDailyBudget(5, 1));
        Assert.Equal(HintTaskQueue.MaxPersonalPuzzlesPerDay, queue.TakeDailyBudget(6, 1000));   // anderes Konto

        time.Now = time.Now.AddHours(2);   // nächster UTC-Tag
        Assert.Equal(50, queue.TakeDailyBudget(5, 50));
    }

    [Fact]
    public void Limits_StayWhereTheCommitArguedThem()
    {
        // Literale Werte: 100 je Lauf ≈ eine Stunde Tipp-Consumer, 300 je Tag ≈ 900 LLM-Aufrufe je Konto.
        Assert.Equal(100, HintTaskQueue.MaxPersonalPuzzlesPerRun);
        Assert.Equal(300, HintTaskQueue.MaxPersonalPuzzlesPerDay);
    }

    // ---- Lauf je Buch ----

    [Fact]
    public async Task GenerateForBookAsync_PersonalCourse_AtMostOneRunCap()
    {
        var bookId = await SeedBookAsync(ownerUserId: 5, lines: 150);
        var claude = new FakeClaude();

        var done = await Hints(claude).GenerateForBookAsync(bookId, null, new HintTaskQueue());

        Assert.Equal(HintTaskQueue.MaxPersonalPuzzlesPerRun, done);
        Assert.Equal(HintTaskQueue.MaxPersonalPuzzlesPerRun * 3, claude.Calls);   // de/en/hr je Puzzle
    }

    [Fact]
    public async Task GenerateForBookAsync_PersonalCourse_DailyBudgetSpansAllBooksOfTheOwner()
    {
        var bookId = await SeedBookAsync(ownerUserId: 5, lines: 50);
        var queue = new HintTaskQueue();
        queue.TakeDailyBudget(5, 280);   // andere Kurse desselben Kontos heute schon

        var done = await Hints(new FakeClaude()).GenerateForBookAsync(bookId, null, queue);

        Assert.Equal(20, done);
    }

    [Fact]
    public async Task GenerateForBookAsync_OwnerFromTheCaller_CountsEvenBeforeTheBookCarriesIt()
    {
        // Ein neuer persönlicher Kurs bekommt OwnerUserId erst NACH dem Import — der Lauf kann früher starten.
        var bookId = await SeedBookAsync(ownerUserId: null, lines: 10);
        var queue = new HintTaskQueue();
        queue.TakeDailyBudget(5, HintTaskQueue.MaxPersonalPuzzlesPerDay);
        var claude = new FakeClaude();

        var done = await Hints(claude).GenerateForBookAsync(bookId, 5, queue);

        Assert.Equal(0, done);
        Assert.Equal(0, claude.Calls);
    }

    [Fact]
    public async Task GenerateForBookAsync_AdminBook_IsNotCapped()
    {
        var bookId = await SeedBookAsync(ownerUserId: null, lines: 150);

        var done = await Hints(new FakeClaude()).GenerateForBookAsync(bookId, null, new HintTaskQueue());

        Assert.Equal(150, done);
    }

    [Fact]
    public async Task GenerateForBookAsync_SkipsInfoLinesAndCurrentHints_AndADeletedBookCostsNothing()
    {
        var bookId = await SeedBookAsync(ownerUserId: 5, lines: 3, infoLines: 4, hinted: 5);
        var queue = new HintTaskQueue();
        var claude = new FakeClaude();

        Assert.Equal(3, await Hints(claude).GenerateForBookAsync(bookId, null, queue));
        Assert.Equal(0, await Hints(claude).GenerateForBookAsync(bookId + 999, 5, queue));
        Assert.Equal(HintTaskQueue.MaxPersonalPuzzlesPerDay - 3, queue.TakeDailyBudget(5, 1000));
    }

    // ---- Einreihen beim Import ----

    private sealed class RecordingHintQueue : IHintTaskQueue
    {
        public List<(int BookId, int? Owner)> Books { get; } = new();
        public bool TryEnqueueBook(int bookId, int? ownerUserId) { Books.Add((bookId, ownerUserId)); return true; }
        public int TakeDailyBudget(int userId, int wanted) => wanted;
        public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
            => throw new InvalidOperationException("Tipps laufen über TryEnqueueBook");
        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private static string Line(string round, string moves) =>
        $"\n[Event \"X\"]\n[Round \"{round}\"]\n[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n{moves} *\n";

    [Fact]
    public async Task ImportFileAsync_QueuesOneHintRunForTheBook_WithTheOwnerOfTheCaller()
    {
        var queue = new RecordingHintQueue();
        var import = new PgnImportService(_db, queue);

        var res = await import.ImportFileAsync("user-u7-x.pgn",
            Line("1", "2. Nf3 Nc6 3. Bb5 a6") + Line("2", "2. d4 exd4 3. Qxd4 Nc6"), CancellationToken.None,
            playFromStartPosition: true, ownerUserId: 7);

        // EIN Auftrag für das Buch (nicht einer mit allen Puzzle-Ids), mit dem Besitzer, den das Buch noch nicht trägt.
        Assert.Equal(new[] { (res.BookId, (int?)7) }, queue.Books);
        // Ohne neue/geänderte Linien nichts.
        await import.ImportFileAsync("user-u7-x.pgn", Line("1", "2. Nf3 Nc6 3. Bb5 a6"), CancellationToken.None,
            playFromStartPosition: true, ownerUserId: 7);
        Assert.Single(queue.Books);
    }

    [Fact]
    public async Task ImportFileAsync_BookWithOwner_UsesTheStoredOwner()
    {
        _db.Books.Add(new Book
        {
            FileName = "chessable-u9-1.pgn", DisplayName = "K", OwnerUserId = 9, ImportVersion = ImportPipeline.CurrentVersion,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        });
        await _db.SaveChangesAsync();
        var queue = new RecordingHintQueue();

        await new PgnImportService(_db, queue).ImportFileAsync("chessable-u9-1.pgn", Line("1", "2. Nf3 Nc6 3. Bb5 a6"),
            CancellationToken.None);

        Assert.Equal(9, Assert.Single(queue.Books).Owner);
    }

    // ---- Verdrahtung ----

    [Fact]
    public void ProgramCs_RunsHintsOnTheirOwnQueueAndConsumer()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("AddSingleton<IHintTaskQueue, HintTaskQueue>()", src);
        Assert.Contains("AddHostedService<HintTaskWorker>()", src);
    }

    [Fact]
    public void PgnImportService_TakesTheHintQueue_NotTheGeneralQueue()
    {
        // Typ-Schranke: der Import kann die Tipp-Generierung gar nicht mehr auf die allgemeine Queue legen.
        var ctor = Assert.Single(typeof(PgnImportService).GetConstructors());
        Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType == typeof(IBackgroundTaskQueue));
        Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(IHintTaskQueue));
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}
