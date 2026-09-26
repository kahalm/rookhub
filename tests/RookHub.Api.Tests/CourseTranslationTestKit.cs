using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Ein Uebersetzer fuer die Kurs-Tests — NIE ein echtes Modell. Reicht jeden Eintrag mit einem Praefix
/// durch („DE:"), damit sich pruefen laesst, WAS angefragt wurde und wohin das Ergebnis wandert. Die
/// Vorlagen der Tests sind bewusst neutral (kaum englische Funktionswoerter): sonst schluege die
/// Sprachpruefung des Kerns an („liest sich als en").
/// </summary>
public sealed class FakeCourseTranslator : IClaudeJsonClient
{
    private readonly Lock _gate = new();
    private readonly List<(string System, List<(int Ply, string Text)> Items)> _calls = [];

    public bool IsConfigured { get; set; } = true;
    public string TranslationModel => "test-modell";

    /// <summary>Feste Antwort statt des Durchreichens (<c>null</c> = durchreichen).</summary>
    public string? Answer { get; set; }

    /// <summary>Scheitert (liefert <c>null</c>) — wie ein abgebrochener Modellaufruf.</summary>
    public bool Fail { get; set; }

    /// <summary>Scheitert nur bei Auftraegen, deren System-Text dies enthaelt (z. B. nur die Kapitel-Fuhre).</summary>
    public string? FailIfSystemContains { get; set; }

    /// <summary>Scheitert nur bei Anfragen, deren Eintraege diesen Text enthalten (z. B. eine bestimmte Linie).</summary>
    public string? FailIfPromptContains { get; set; }

    /// <summary>So lange „rechnet" das Modell — damit parallele Linien sich wirklich ueberschneiden.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Beim n-ten Aufruf (1-basiert) wird dieser Token-Geber abgebrochen — die Antwort kommt trotzdem.</summary>
    public (int Call, CancellationTokenSource Cts)? CancelAt { get; set; }

    public string Prefix { get; set; } = "DE:";

    public IReadOnlyList<(string System, List<(int Ply, string Text)> Items)> Calls
    {
        get { lock (_gate) return _calls.ToList(); }
    }

    public Task<string?> GenerateHintsJsonAsync(string system, string prompt, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public async Task<string?> TranslateCommentsJsonAsync(string system, string prompt, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(prompt);
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => (Ply: i.GetProperty("ply").GetInt32(), Text: i.GetProperty("text").GetString() ?? ""))
            .ToList();
        int n;
        lock (_gate)
        {
            _calls.Add((system, items));
            n = _calls.Count;
        }
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, CancellationToken.None);
        if (CancelAt is { } c && c.Call == n) c.Cts.Cancel();
        if (Fail || (FailIfSystemContains is { } marker && system.Contains(marker))
                 || (FailIfPromptContains is { } text && items.Any(i => i.Text.Contains(text))))
            return null;
        if (Answer is not null) return Answer;
        var answer = items.Select(i => new { ply = i.Ply, text = Prefix + i.Text });
        return JsonSerializer.Serialize(new { items = answer });
    }
}

/// <summary>
/// Dienst-Container fuer die Kurs-Uebersetzung unter InMemory: der Lauf oeffnet je Linie einen EIGENEN
/// Scope (eigener DbContext), also braucht der Test einen echten <see cref="IServiceScopeFactory"/> — alle
/// Kontexte teilen sich ueber die <see cref="InMemoryDatabaseRoot"/> dieselbe Datenbank.
/// </summary>
public sealed class CourseTranslationTestKit : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly List<IServiceScope> _scopes = [];

    public FakeCourseTranslator Llm { get; } = new();

    public CourseTranslationTestKit(int parallel = 1)
    {
        var root = new InMemoryDatabaseRoot();
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CourseTranslation:Parallel"] = parallel.ToString(),
            }).Build());
        services.AddSingleton<IClaudeJsonClient>(Llm);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name, root));
        services.AddScoped<CourseTranslationService>();
        services.AddScoped<CourseCommentLocalizer>();
        _provider = services.BuildServiceProvider();
    }

    private IServiceProvider Scope()
    {
        var scope = _provider.CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider;
    }

    /// <summary>Ein frischer Kontext (eigener Scope) — sieht, was die Laeufe geschrieben haben.</summary>
    public AppDbContext Db() => Scope().GetRequiredService<AppDbContext>();

    public CourseTranslationService Service() => Scope().GetRequiredService<CourseTranslationService>();

    public CourseCommentLocalizer Localizer() => Scope().GetRequiredService<CourseCommentLocalizer>();

    public async Task<Book> SeedBookAsync(string? commentLanguage = "en", string name = "Kurs")
    {
        var db = Db();
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = name, CommentLanguage = commentLanguage,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    public async Task<BookPuzzle> SeedLineAsync(Book book, string round, string? title = null, string? chapter = null,
        string? comment = null, Dictionary<int, string>? moveComments = null)
    {
        var db = Db();
        var line = new BookPuzzle
        {
            LineId = $"{book.FileName}:{round}", BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", Moves = "e2e4 e7e5 g1f3",
            Title = title, Chapter = chapter, Comment = comment,
            MoveComments = moveComments is null ? null : JsonSerializer.Serialize(moveComments),
        };
        db.BookPuzzles.Add(line);
        await db.SaveChangesAsync();
        return line;
    }

    /// <summary>Die Texte des Satzes (Linie, Sprache) je Stelle — leer, wenn es keinen gibt.</summary>
    public async Task<Dictionary<int, CommentText>> TextsAsync(int lineId, string lang)
    {
        var set = await Db().CommentSets.AsNoTracking().Include(s => s.Texts)
            .FirstOrDefaultAsync(s => s.BookPuzzleId == lineId && s.Language == lang);
        return set?.Texts.ToDictionary(t => t.Ply) ?? new Dictionary<int, CommentText>();
    }

    public void Dispose()
    {
        foreach (var s in _scopes) s.Dispose();
        _provider.Dispose();
    }
}
