using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// DER GRUND, warum es dieses Projekt gibt.
///
/// Die 2045 Unit-Tests laufen gegen EF InMemory. Das wertet JEDE LINQ-Abfrage im Speicher aus —
/// auch eine, die MySQL nicht uebersetzen kann. Solche Abfragen sind dort gruen und werfen erst
/// in Produktion. Beim Optimieren von Kursstatistik, Partienliste und Chessable-Abgleich musste
/// die Uebersetzbarkeit deshalb jedes Mal von Hand ueber ToQueryString() belegt werden.
///
/// Hier laufen dieselben Methoden gegen eine echte MariaDB. Wirft eine davon
/// InvalidOperationException("could not be translated"), faellt der Test — automatisch und ohne
/// dass jemand daran denken muss.
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class QueryTranslationTests : IAsyncLifetime
{
    private MariaDbSchema _schema = null!;
    private ApiFactory _factory = null!;
    private IServiceScope _scope = null!;

    public async Task InitializeAsync()
    {
        _schema = await MariaDbSchema.CreateAsync("q");
        await using (var db = _schema.NewContext()) await db.Database.MigrateAsync();
        _factory = new ApiFactory(_schema.ConnectionString);
        _scope = _factory.Services.CreateScope();
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        _factory?.Dispose();
        if (_schema is not null) await _schema.DisposeAsync();
    }

    private T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();
    private AppDbContext Db => Get<AppDbContext>();

    private async Task<int> SeedUserAsync(string name)
    {
        var u = new AppUser { Username = name, Email = $"{name}@t.local", PasswordHash = "x" };
        Db.AppUsers.Add(u);
        await Db.SaveChangesAsync();
        return u.Id;
    }

    [MySqlFact]
    public async Task Partienliste_UebersetztUndZaehltDieZuegeNach()
    {
        var userId = await SeedUserAsync("games");
        var svc = Get<SavedGameService>();
        await svc.SaveAsync(userId, new RookHub.Api.DTOs.SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5", "Nf3" }, ExternalId = "it-1",
        });
        // Altbestand nachstellen: Zaehler fehlt, das PGN muss dafuer geholt werden.
        var row = await Db.SavedGames.FirstAsync();
        row.MoveCount = null;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var list = await svc.ListAsync(userId);

        Assert.Equal(3, Assert.Single(list).MoveCount);
        Db.ChangeTracker.Clear();
        Assert.Equal(3, (await Db.SavedGames.FirstAsync()).MoveCount);   // nachgetragen
    }

    [MySqlFact]
    public async Task Kursstatistik_UebersetztInklusiveKapitelNormalisierung()
    {
        var userId = await SeedUserAsync("kurs");
        var book = new Book { FileName = "it-course.pgn", DisplayName = "IT", OwnerUserId = userId };
        Db.Books.Add(book);
        await Db.SaveChangesAsync();
        // Leer / nur Leerzeichen / NULL muessen als EIN Kapitel zaehlen — die Normalisierung
        // steckt im SQL-Ausdruck (CASE WHEN ... TRIM(...)), nicht mehr im Speicher.
        foreach (var ch in new string?[] { "", "   ", null })
            Db.BookPuzzles.Add(new BookPuzzle
            {
                BookId = book.Id, BookFileName = book.FileName, Chapter = ch,
                LineId = Guid.NewGuid().ToString("N"), Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1",
            });
        await Db.SaveChangesAsync();

        var next = await Get<CourseService>().GetNextAsync(userId, book.Id, "sequential", null, null, isAdmin: true);

        Assert.Equal(3, next.Book!.Total);
        Assert.Null(next.Chapter);        // ein Kapitel -> kein eigener Kapitel-Block
    }

    [MySqlFact]
    public async Task ChessableAbgleich_UebersetztDenKennungsZwischenspeicher()
    {
        var userId = await SeedUserAsync("chessable");
        var svc = Get<ChessableImportService>();
        await svc.AppendLiveAsync(userId, "990001",
            "[Event \"C\"]\n[ChessableOid \"4711\"]\n[White \"A\"]\n[Result \"*\"]\n\n1. e4 e5 *\n",
            "IT-Kurs", "repertoire");

        var (oids, _, hasRep) = await svc.GetImportedOidsAsync(userId, "990001");

        Assert.True(hasRep);
        Assert.Contains("4711", oids);
        Db.ChangeTracker.Clear();
        var file = await Db.RepertoireFiles.FirstAsync();
        Assert.Equal("4711", file.ChessableOidsCache);
        Assert.Equal(file.PgnContent.Length, file.ChessableOidsPgnLength);
    }

    [MySqlFact]
    public async Task ThemenFilter_MaskiertSqlWildcards()
    {
        // Ersetzt den frueheren InMemory-„Test" `GetRandom_ThemeWithSqlWildcard_DoesNotMatch`, der
        // auf `Assert.True(true)` endete: ohne `EF.Functions.Like`-Uebersetzung kann er die
        // Maskierung nicht pruefen und konnte per Konstruktion nicht rot werden. Hier laeuft echtes
        // SQL — ein entferntes `SanitizeLikeInput` faellt sofort auf, weil `_` dann als
        // Ein-Zeichen-Joker jedes „mateIn2" trifft.
        var userId = await SeedUserAsync("wildcard");
        Db.Puzzles.Add(new Puzzle
        {
            LichessId = "wc1", Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1b1",
            Rating = 1500, Themes = "mateIn2",
        });
        await Db.SaveChangesAsync();
        var svc = Get<PuzzleService>();

        // Gegenprobe zuerst: das WÖRTLICHE Thema muss gefunden werden (sonst prüft der Test nichts).
        Assert.NotNull(await svc.GetRandomAsync(userId, null, null, themes: "mateIn2", excludeSolved: false));

        // `_` ist in LIKE ein Ein-Zeichen-Joker; maskiert darf „mate_n2" NICHT auf „mateIn2" passen.
        Assert.Null(await svc.GetRandomAsync(userId, null, null, themes: "mate_n2", excludeSolved: false));
        // `%` ebenso (Mehrzeichen-Joker).
        Assert.Null(await svc.GetRandomAsync(userId, null, null, themes: "mate%2", excludeSolved: false));
        // Und über den ODER-Pfad (themesAny), der dieselbe Maskierung benutzt.
        Assert.Null(await svc.GetRandomAsync(userId, null, null, themes: null, excludeSolved: false,
            themesAny: "mate_n2"));
    }
}
