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

    [MySqlFact]
    public async Task Turnierverzeichnis_UmkreisUndFilterUebersetzen()
    {
        // Die Umkreissuche laeuft bewusst zweistufig: Bounding-Box in SQL, exakte Distanz danach
        // in C#. Hier zaehlt der SQL-Teil — Datumsueberlappung mit COALESCE, das vorberechnete
        // Wochenend-Flag (DateOnly.DayOfWeek uebersetzt der Provider NICHT) und die Lat/Lon-Box.
        Db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                ChessResultsId = "1", Name = "Nah am Mittelpunkt", Federation = "AUT",
                StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 12),
                StartsOnWeekend = true, Lat = 47.80, Lon = 13.04, Speed = TournamentSpeed.Standard,
                PlayerCount = 30, LocationText = "Salzburg", GeoSource = GeoSource.City,
            },
            new TournamentDirectoryEntry
            {
                ChessResultsId = "2", Name = "Weit weg", Federation = "AUT",
                StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 12),
                StartsOnWeekend = true, Lat = 48.21, Lon = 16.37, Speed = TournamentSpeed.Standard,
                PlayerCount = 30, LocationText = "Wien", GeoSource = GeoSource.City,
            },
            new TournamentDirectoryEntry
            {
                ChessResultsId = "3", Name = "Langlaeufer ragt herein", Federation = "AUT",
                StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2026, 11, 30),
                StartsOnWeekend = false, Lat = 47.81, Lon = 13.05, Speed = TournamentSpeed.Standard,
                PlayerCount = 30, LocationText = "Salzburg Umgebung", GeoSource = GeoSource.City,
            });
        await Db.SaveChangesAsync();

        var svc = Get<TournamentDirectoryQueryService>();

        var radius = await svc.SearchAsync(new DirectorySearchQuery
        {
            From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31),
            Lat = 47.80, Lon = 13.04, RadiusKm = 50,
            Federation = "AUT", Speed = TournamentSpeed.Standard, Text = "Salzburg", MinPlayers = 10,
        });
        Assert.Equal(2, radius.Total);   // "1" und der hereinragende Langlaeufer "3"

        var weekend = await svc.SearchAsync(new DirectorySearchQuery { WeekendOnly = true });
        Assert.Equal(2, weekend.Total);

        var pins = await svc.MapPinsAsync(new DirectorySearchQuery(), 47.0, 48.0, 12.0, 14.0);
        Assert.Equal(2, pins.Count);

        Assert.NotNull(await svc.GetAsync("1"));
    }

    [MySqlFact]
    public async Task Ortsvorschlaege_UebersetzenPraefixUndSortierung()
    {
        Db.GeoPlaces.AddRange(
            new GeoPlace { Country = "AT", PostalCode = "5400", Name = "Hallein", NameNormalized = "hallein", Lat = 47.68, Lon = 13.1, Kind = GeoPlaceKind.PostalCode },
            new GeoPlace { Country = "AT", Name = "Wien", NameNormalized = "wien", Lat = 48.21, Lon = 16.37, Kind = GeoPlaceKind.City, Population = 1_900_000 },
            new GeoPlace { Country = "AT", Name = "Wiener Neudorf", NameNormalized = "wiener neudorf", Lat = 48.08, Lon = 16.32, Kind = GeoPlaceKind.City, Population = 9_000 });
        await Db.SaveChangesAsync();

        var svc = Get<TournamentDirectoryQueryService>();

        Assert.Equal("Hallein", Assert.Single(await svc.SuggestPlacesAsync("54")).Name);

        // Die Sortierung enthaelt einen booleschen Ausdruck (exakter Treffer zuerst) — genau die
        // Sorte Klausel, die InMemory klaglos schluckt und ein Provider ablehnen kann.
        var byName = await svc.SuggestPlacesAsync("Wien");
        Assert.Equal("Wien", byName[0].Name);
    }


    [MySqlFact]
    public async Task Turnierverzeichnis_GruppierungUebersetzt()
    {
        // GroupBy mit COALESCE auf einen berechneten Schluessel plus Min/Count je Gruppe — genau
        // die Sorte Abfrage, die EF InMemory klaglos im Speicher rechnet und ein Provider ablehnen
        // kann. Zusaetzlich der Altbestand ohne Schluessel: NULL == NULL darf die Zeilen nicht in
        // einen Topf werfen.
        TournamentDirectoryEntry Entry(string name, string id, string? key) => new()
        {
            ChessResultsId = id, Name = name, BaseName = TournamentNameGrouping.BaseName(name),
            Federation = "AUT", StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 10),
            LocationText = "Ranshofen", PlayerCount = 10, GeoSource = GeoSource.None, GroupKey = key,
        };

        var a = Entry("Open Braunau 2026 A", "111", null);
        var b = Entry("Open Braunau 2026 B", "112", null);
        a.GroupKey = TournamentDirectoryService.ComputeGroupKey(a);
        b.GroupKey = TournamentDirectoryService.ComputeGroupKey(b);
        Db.TournamentDirectoryEntries.AddRange(a, b,
            Entry("Altbestand eins", "201", null),
            Entry("Altbestand zwei", "202", null));
        await Db.SaveChangesAsync();

        var svc = Get<TournamentDirectoryQueryService>();
        var result = await svc.SearchAsync(new DirectorySearchQuery());

        // Die zwei Gruppen von Braunau = ein Eintrag, die beiden schluessellosen je einer.
        Assert.Equal(3, result.Total);
        var braunau = result.Items.Single(i => i.Members.Count == 2);
        Assert.Equal("Open Braunau 2026", braunau.Entry.BaseName);
        Assert.Equal(["111", "112"], braunau.Members.Select(m => m.ChessResultsId));
    }

}
