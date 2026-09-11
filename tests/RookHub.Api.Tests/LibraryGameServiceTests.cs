using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Rohbestand als Nachschlagewerk: suchen und einzelne Partien zum Rechnen anfordern. Bis
/// dahin sah nur das Wartungswerkzeug hinein — die Auswahl aus 130 000 Partien war damit die
/// Aufgabe genau einer Person mit Datenbankzugang.
/// </summary>
public class LibraryGameServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly LibraryGameService _svc;

    public LibraryGameServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        var jobs = new AnalysisJobService(_db, new EncryptionService(config), null);
        var analyses = new GameAnalysisService(_db, jobs, NullLogger<GameAnalysisService>.Instance);
        _svc = new LibraryGameService(_db, analyses);
    }

    public void Dispose() => _db.Dispose();

    private const string Game = """
[Event "Testpartie"]
[White "Anderssen"]
[Black "Kieseritzky"]
[Result "1-0"]

1. e4 e5 2. f4 exf4 3. Bc4 Qh4+ 4. Kf1 b5 5. Bxb5 Nf6 6. Nf3 Qh6 7. d3 Nh5 1-0
""";

    private async Task<AppUser> CreateUserAsync(string name = "u", bool withEngine = true)
    {
        var user = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        if (withEngine)
        {
            _db.LichessEngineCredentials.Add(new LichessEngineCredential
            {
                UserId = user.Id, User = user, EncryptedToken = "enc", BackgroundEngineIds = $"eei_{name}",
            });
            await _db.SaveChangesAsync();
        }
        return user;
    }

    private async Task<LibraryGame> AddGameAsync(string white, string black, int score = 50,
        LibraryGameStatus status = LibraryGameStatus.New, string? annotator = null,
        string? languages = "en", int commentedPlies = 10, string? evt = "Turnier")
    {
        var game = new LibraryGame
        {
            White = white, Black = black, Event = evt, Annotator = annotator, Languages = languages,
            CommentedPlies = commentedPlies, Score = score, Status = status, PlyCount = 14,
            PlayedOn = new DateOnly(1951, 6, 1), Pgn = Game,
            SearchText = LibraryGameReader.SearchTextOf(white, black, evt, annotator),
        };
        _db.LibraryGames.Add(game);
        await _db.SaveChangesAsync();
        return game;
    }

    // ===== Suchen =============================================================

    /// <summary>Ein Feld fuer alle vier Spalten: Spieler, Turnier UND Kommentator.</summary>
    [Fact]
    public async Task Search_findetUeberSpielerTurnierUndKommentator()
    {
        var user = await CreateUserAsync();
        await AddGameAsync("Capablanca", "Lasker", annotator: "Aagaard,Jacob", evt: "St. Petersburg");
        await AddGameAsync("Tal", "Botwinnik", annotator: "Stohl,Igor", evt: "Moskau");

        Assert.Single((await _svc.SearchAsync(user.Id, "Capablanca", null, null, 1, 25)).Items);
        Assert.Single((await _svc.SearchAsync(user.Id, "Botwinnik", null, null, 1, 25)).Items);
        Assert.Single((await _svc.SearchAsync(user.Id, "Petersburg", null, null, 1, 25)).Items);
        Assert.Single((await _svc.SearchAsync(user.Id, "Stohl", null, null, 1, 25)).Items);
        Assert.Empty((await _svc.SearchAsync(user.Id, "Kasparow", null, null, 1, 25)).Items);
        // Gross-/Kleinschreibung ist egal — gesucht wird ueber eine kleingeschriebene Spalte.
        Assert.Single((await _svc.SearchAsync(user.Id, "capablanca", null, null, 1, 25)).Items);
    }

    /// <summary>Die erste Seite ist die einzige, die die meisten je ansehen — dort gehoeren die
    /// durchgaengig erklaerten Partien hin, nicht die zufaellig juengsten.</summary>
    [Fact]
    public async Task Search_sortiertNachEignungsnote()
    {
        var user = await CreateUserAsync();
        await AddGameAsync("Schwach", "Gegner", score: 20);
        await AddGameAsync("Stark", "Gegner", score: 95);
        await AddGameAsync("Mittel", "Gegner", score: 60);

        var page = await _svc.SearchAsync(user.Id, null, null, null, 1, 25);

        Assert.Equal(["Stark", "Mittel", "Schwach"], page.Items.Select(i => i.White));
    }

    /// <summary>Dubletten und Aussortiertes gehoeren nicht in die Auswahl.</summary>
    [Fact]
    public async Task Search_laesstDublettenUndAussortierteWeg()
    {
        var user = await CreateUserAsync();
        await AddGameAsync("Sichtbar", "Gegner");
        await AddGameAsync("Dublette", "Gegner", status: LibraryGameStatus.Duplicate);
        await AddGameAsync("Aussortiert", "Gegner", status: LibraryGameStatus.Rejected);

        var page = await _svc.SearchAsync(user.Id, null, null, null, 1, 25);

        Assert.Single(page.Items);
        Assert.Equal("Sichtbar", page.Items[0].White);
    }

    [Fact]
    public async Task Search_filtertSpracheUndKommentardichte()
    {
        var user = await CreateUserAsync();
        await AddGameAsync("Deutsch", "Gegner", languages: "de", commentedPlies: 30);
        await AddGameAsync("Englisch", "Gegner", languages: "en", commentedPlies: 5);
        await AddGameAsync("Gemischt", "Gegner", languages: "en,de", commentedPlies: 30);

        var german = await _svc.SearchAsync(user.Id, null, "de", null, 1, 25);
        Assert.Equal(2, german.Items.Count);       // „de" und „en,de"

        var dense = await _svc.SearchAsync(user.Id, null, null, 20, 1, 25);
        Assert.Equal(2, dense.Items.Count);
    }

    [Fact]
    public async Task Search_blaettert()
    {
        var user = await CreateUserAsync();
        for (var i = 0; i < 7; i++) await AddGameAsync($"Spieler{i}", "Gegner", score: 90 - i);

        var first = await _svc.SearchAsync(user.Id, null, null, null, 1, 3);
        var second = await _svc.SearchAsync(user.Id, null, null, null, 2, 3);

        Assert.Equal(7, first.Total);
        Assert.Equal(3, first.Items.Count);
        Assert.Equal("Spieler3", second.Items[0].White);
    }

    /// <summary>Eine Zeile muss sagen, ob die Partie schon spielbar ist — sonst fordert man an, was
    /// daneben schon fertig liegt.</summary>
    [Fact]
    public async Task Search_markiertWasSchonSpielbarIst()
    {
        var user = await CreateUserAsync();
        var other = await CreateUserAsync("anderer", withEngine: false);
        var inPool = await AddGameAsync("ImBestand", "Gegner");
        var mine = await AddGameAsync("Angefordert", "Gegner");
        await AddGameAsync("Neu", "Gegner");

        _db.GameAnalyses.Add(new GameAnalysis
        {
            UserId = other.Id, Pgn = Game, StartFen = "x", LibraryGameId = inPool.Id, IsPublic = true,
        });
        _db.GameAnalyses.Add(new GameAnalysis
        {
            UserId = user.Id, Pgn = Game, StartFen = "x", LibraryGameId = mine.Id,
        });
        await _db.SaveChangesAsync();

        var page = await _svc.SearchAsync(user.Id, null, null, null, 1, 25);
        var byName = page.Items.ToDictionary(i => i.White!);

        Assert.True(byName["ImBestand"].InPool);
        Assert.False(byName["ImBestand"].Requested);
        Assert.True(byName["Angefordert"].Requested);
        Assert.NotNull(byName["Angefordert"].GameAnalysisId);
        Assert.False(byName["Neu"].InPool);
        Assert.Null(byName["Neu"].GameAnalysisId);
    }

    /// <summary>Die Zuege bleiben draussen — 130 000 Zeilen mit 338 MB Partietext gehoeren nicht in
    /// eine Trefferliste.</summary>
    [Fact]
    public async Task Search_liefertKeinePgn()
    {
        var user = await CreateUserAsync();
        await AddGameAsync("Capablanca", "Lasker");

        var page = await _svc.SearchAsync(user.Id, null, null, null, 1, 25);

        Assert.DoesNotContain(nameof(LibraryGame.Pgn), typeof(LibraryGameDto).GetProperties().Select(p => p.Name));
        Assert.Single(page.Items);
    }

    // ===== Anfordern ==========================================================

    [Fact]
    public async Task Request_legtEineAnalyseMitFesterTiefeAn()
    {
        var user = await CreateUserAsync();
        var game = await AddGameAsync("Anderssen", "Kieseritzky");

        var result = await _svc.RequestAsync(user.Id, game.Id);

        Assert.Null(result.Reason);
        Assert.False(result.AlreadyPlayable);
        Assert.NotNull(result.Analysis);
        Assert.Equal(GameAnalysisDefaults.GuessTargetDepth, result.Analysis!.TargetDepth);

        var analysis = await _db.GameAnalyses.FirstAsync(a => a.Id == result.Analysis.Id);
        Assert.Equal(game.Id, analysis.LibraryGameId);
        Assert.Equal(GameAnalysisOrigin.Guess, analysis.Origin);
        Assert.Equal("Anderssen – Kieseritzky, Turnier 1951", analysis.Title);
    }

    /// <summary>Eine halbe Stunde Engine-Zeit fuer etwas, das daneben schon fertig liegt, waere die
    /// teuerste Art, nichts zu gewinnen.</summary>
    [Fact]
    public async Task Request_rechnetNichtsDoppelt()
    {
        var user = await CreateUserAsync();
        var game = await AddGameAsync("Anderssen", "Kieseritzky");

        var first = await _svc.RequestAsync(user.Id, game.Id);
        var second = await _svc.RequestAsync(user.Id, game.Id);

        Assert.True(second.AlreadyPlayable);
        Assert.Equal(first.Analysis!.Id, second.Analysis!.Id);
        Assert.Single(await _db.GameAnalyses.Where(a => a.LibraryGameId == game.Id).ToListAsync());
    }

    /// <summary>Liegt die Partie im kuratierten Bestand, wird sie nicht ein zweites Mal gerechnet —
    /// dort kann sie ohnehin jeder spielen.</summary>
    [Fact]
    public async Task Request_imBestand_gibtDieVorhandenePartie()
    {
        var user = await CreateUserAsync();
        var owner = await CreateUserAsync("besitzer", withEngine: false);
        var game = await AddGameAsync("Anderssen", "Kieseritzky");
        _db.GameAnalyses.Add(new GameAnalysis
        {
            UserId = owner.Id, Pgn = Game, StartFen = "x", LibraryGameId = game.Id, IsPublic = true,
            Title = "Schon da",
        });
        await _db.SaveChangesAsync();

        var result = await _svc.RequestAsync(user.Id, game.Id);

        Assert.True(result.AlreadyPlayable);
        // Und die Partie kommt MIT: sie gehoert einem anderen Konto, der Aufrufer muss trotzdem
        // erfahren, wohin er gehen soll.
        Assert.NotNull(result.Analysis);
        Assert.Equal("Schon da", result.Analysis!.Title);
        Assert.Single(await _db.GameAnalyses.Where(a => a.LibraryGameId == game.Id).ToListAsync());
    }

    [Fact]
    public async Task Request_unbekanntePartie_gibtDenGrund()
    {
        var user = await CreateUserAsync();

        var result = await _svc.RequestAsync(user.Id, 12345);

        Assert.Equal(LibraryRequestReason.NotFound, result.Reason);
        Assert.Null(result.Analysis);
    }

    /// <summary>Derselbe Deckel wie beim Einwurf — angefordert wird auf Rechenzeit, die jemandem
    /// gehoert.</summary>
    [Fact]
    public async Task Request_ueberDemDeckel_nimmtNichtsMehrAn()
    {
        var user = await CreateUserAsync();
        for (var i = 0; i < GameAnalysisDefaults.MaxOpenGuessGamesPerUser; i++)
        {
            var open = await AddGameAsync($"Partie{i}", "Gegner");
            Assert.Null((await _svc.RequestAsync(user.Id, open.Id)).Reason);
        }

        var refused = await AddGameAsync("ZuViel", "Gegner");
        var result = await _svc.RequestAsync(user.Id, refused.Id);

        Assert.Equal(LibraryRequestReason.TooManyOpen, result.Reason);
    }

    [Fact]
    public async Task Request_ohneEngine_gibtDenGrund()
    {
        var user = await CreateUserAsync("ohneEngine", withEngine: false);
        var game = await AddGameAsync("Anderssen", "Kieseritzky");

        var result = await _svc.RequestAsync(user.Id, game.Id);

        Assert.Equal(LibraryRequestReason.NoEngine, result.Reason);
    }

    /// <summary>
    /// Der Volltext-Ausdruck: jedes Wort Pflicht, das letzte mit Stern (wer tippt, ist mitten im
    /// Wort). Die Operatoren der Boolean-Syntax werden WEGGEWORFEN — ein Bindestrich in einem
    /// Doppelnamen waere sonst eine Suche, die das Gegenteil meint.
    /// </summary>
    [Fact]
    public void BooleanTerm_bautDenAusdruck()
    {
        Assert.Equal("+capablanca*", LibraryGameService.BooleanTerm("capablanca"));
        Assert.Equal("+lasker +capablanca*", LibraryGameService.BooleanTerm("lasker capablanca"));
        Assert.Equal("+nimzowitsch*", LibraryGameService.BooleanTerm("-nimzowitsch"));
        Assert.Equal("+saint +john*", LibraryGameService.BooleanTerm("Saint-John"));
    }

    /// <summary>Zu kurze Woerter nimmt der Volltext-Index gar nicht auf — dann wird nicht
    /// gefiltert, statt verlaesslich nichts zu finden.</summary>
    [Fact]
    public void BooleanTerm_zuKurzGibtNull()
    {
        Assert.Null(LibraryGameService.BooleanTerm(null));
        Assert.Null(LibraryGameService.BooleanTerm("  "));
        Assert.Null(LibraryGameService.BooleanTerm("ab"));
        Assert.Null(LibraryGameService.BooleanTerm("*+-"));
    }

    [Fact]
    public void TitleOf_faelltAufTurnierUndKommentatorZurueck()
    {
        Assert.Equal("Turnier 1951", LibraryGameService.TitleOf(new LibraryGame
        { Event = "Turnier", PlayedOn = new DateOnly(1951, 6, 1), Pgn = "x" }));
        Assert.Equal("Aagaard,Jacob", LibraryGameService.TitleOf(new LibraryGame
        { Annotator = "Aagaard,Jacob", Pgn = "x" }));
        Assert.Equal("Partie", LibraryGameService.TitleOf(new LibraryGame { Pgn = "x" }));
    }
}
