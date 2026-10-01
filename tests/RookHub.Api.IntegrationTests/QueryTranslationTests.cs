using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
public class QueryTranslationTests(QueryTranslationFixture fixture)
    : IAsyncLifetime, IClassFixture<QueryTranslationFixture>
{
    private IServiceScope _scope = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _scope = fixture.Factory.Services.CreateScope();
    }

    public Task DisposeAsync()
    {
        _scope?.Dispose();
        return Task.CompletedTask;
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

    /// <summary>
    /// Partieformular (0.529.0): die Partienliste holt die Einlesung je Partie als korrelierte Unterabfrage,
    /// die Einlesungs-Liste projiziert OHNE das Foto (LONGBLOB) in die Entität, und das Löschen der Partie
    /// leert die Einlesung über einen Platzhalter (seit 0.568.1: Zeile bleibt fürs Kontingent) — alle drei nur
    /// gegen echtes SQL prüfbar. Die Einlesung
    /// steht auf Done, damit der laufende Worker der Test-Anwendung sie nicht anfasst.
    /// </summary>
    [MySqlFact]
    public async Task LeagueHub_PartienJeQuelle_ZaehltOnlinePartienJeSeiteOhneDoppelte()
    {
        // GroupBy über die Navigation + Count(Distinct) — muss als COUNT(DISTINCT …) beim Server landen (0.626.0).
        var a = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "a", Url = "u", Confidence = "sicher" };
        var b = new LeagueOnlineAccount { FideId = "333", Site = "lichess", UserName = "b", Url = "u", Confidence = "sicher" };
        var c = new LeagueOnlineAccount { FideId = "222", Site = "chess.com", UserName = "c", Url = "u", Confidence = "sicher" };
        Db.LeagueOnlineAccounts.AddRange(a, b, c);
        await Db.SaveChangesAsync();
        LeagueOnlineGame G(LeagueOnlineAccount x, string id) => new()
        {
            AccountId = x.Id, FideId = x.FideId, ExternalId = id, PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4",
        };
        Db.LeagueOnlineGames.AddRange(G(a, "x1"), G(b, "x1"), G(a, "x2"), G(c, "c1"));
        Db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max",
            Pgn = "[White \"Muster, Max\"]\n[Black \"Huber, Franz\"]\n[Date \"2024.03.01\"]\n\n1. e4 1-0\n" });
        await Db.SaveChangesAsync();

        var r = await new RookHub.Api.Services.League.LeagueGameSources(Db).GetAsync(default);
        Assert.Equal(3, r["onlineTotal"]!.GetValue<int>());
        Assert.Equal(1, r["boardTotal"]!.GetValue<int>());
    }

    [MySqlFact]
    public async Task Partieformular_ListeTraegtScanId_UndLoeschenLaedtDasFotoNicht()
    {
        var userId = await SeedUserAsync("sheet");
        var games = Get<SavedGameService>();
        var game = await games.CreateGeneratedAsync(userId, SavedGameService.ScoresheetSource, new[] { "e4", "e5" }, null,
            new RookHub.Api.DTOs.GameHeaderInput("Simultan", null, "2026-06-05", "1", "A", "B", "0-1"));
        Db.ScoresheetScans.Add(new ScoresheetScan
        {
            UserId = userId, SavedGameId = game.Id, Photo = new byte[] { 1, 2, 3 }, Status = ScoresheetScanStatus.Done,
            ResolutionJson = "{\"plies\":[]}",
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var list = await games.ListAsync(userId);
        var scanId = Assert.Single(list).ScanId;
        Assert.NotNull(scanId);
        var scans = Get<ScoresheetScanService>();
        Assert.Single(await scans.ListAsync(userId));
        Assert.Equal("done", (await scans.GetAsync(userId, scanId!.Value))!.Status);
        Assert.Equal(3, (await scans.PhotoForGameAsync(userId, game.Id))!.Value.Data.Length);

        // Seit 0.568.1 bleibt die Einlesung als Zeile fürs Tageskontingent stehen (DetachWithoutLoading) — ohne
        // Partie, Foto und Modell-Antwort. Vorher ging sie ganz, und löschen + neu hochladen umging die Tageszahl.
        Assert.True(await games.DeleteAsync(userId, game.Id));
        Db.ChangeTracker.Clear();
        var kept = Assert.Single(await Db.ScoresheetScans.ToListAsync());
        Assert.Null(kept.SavedGameId);
        Assert.Empty(kept.Photo);
        Assert.Null(kept.ResolutionJson);
        Assert.Null(kept.TranscriptionJson);
        Assert.False(await Db.SavedGames.AnyAsync(g => g.Id == game.Id));
    }

    /// <summary>
    /// Der Publikumsfilter des Turnierverzeichnisses rechnet BITWEISE: die Alters-/Nachwuchsklassen
    /// liegen als Bitfeld in einer Spalte, und ein Turnier passt, wenn sich Gesuchtes und
    /// Gefuehrtes ueberschneiden. Genau die Sorte Ausdruck, die EF InMemory im Speicher ausrechnet
    /// und deren Uebersetzung erst gegen echtes MariaDB auffaellt — dasselbe gilt fuer die
    /// Gegenprobe „gar keine Jugendklasse" des Schalters „nur Erwachsene".
    /// </summary>
    [MySqlFact]
    public async Task Turnierverzeichnis_UebersetztDenBitweisenPublikumsfilter()
    {
        var query = Get<TournamentDirectoryQueryService>();
        Db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "it-audience-1", ChessResultsId = "it-audience-1", Name = "Landesmeisterschaft U10 U12",
                Federation = "AUT", StartDate = new DateOnly(2026, 10, 10),
                EndDate = new DateOnly(2026, 10, 12),
                AgeGroups = TournamentAgeGroups.U10 | TournamentAgeGroups.U12,
                Gender = TournamentGender.Female, Kind = TournamentKind.Individual,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "it-audience-2", ChessResultsId = "it-audience-2", Name = "Open Braunau",
                Federation = "AUT", StartDate = new DateOnly(2026, 10, 10),
                EndDate = new DateOnly(2026, 10, 12),
                Kind = TournamentKind.Team, IsLeague = true,
            });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var overlap = await query.SearchAsync(new DirectorySearchQuery
        {
            Federation = "AUT", AgeGroups = TournamentAgeGroups.U12,
            Genders = [TournamentGender.Female], Kinds = [TournamentKind.Individual],
        });
        Assert.Equal("it-audience-1", Assert.Single(overlap.Items).Entry.ChessResultsId);

        var adults = await query.SearchAsync(new DirectorySearchQuery
        {
            Federation = "AUT", AdultsOnly = true,
        });
        Assert.Equal("it-audience-2", Assert.Single(adults.Items).Entry.ChessResultsId);

        var withoutLeagues = await query.SearchAsync(new DirectorySearchQuery
        {
            Federation = "AUT", HideLeagues = true,
        });
        Assert.Equal("it-audience-1", Assert.Single(withoutLeagues.Items).Entry.ChessResultsId);
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

    /// <summary>
    /// Die Bewertungskurve der gespeicherten Partie vergleicht PGN-Texte (LONGTEXT) — beim
    /// Wiederverwenden als Parameter, beim Rueckfall auf die eigene Analyse als UNTERABFRAGE
    /// (<c>a.Pgn IN (SELECT Pgn FROM SavedGames …)</c>), damit das PGN bei jedem Nachfragen der Seite
    /// nicht zur API und zurueck wandert. Beides muss MariaDB uebersetzen koennen.
    /// </summary>
    [MySqlFact]
    public async Task GespeichertePartie_WiederverwendungUndKurve_UebersetzenDenPgnVergleich()
    {
        var userId = await SeedUserAsync("evals");
        var svc = Get<SavedGameService>();
        var game = await svc.SaveAsync(userId, new RookHub.Api.DTOs.SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "a", Black = "b", ExternalId = "it-evals",
        });
        var analysis = new GameAnalysis
        {
            UserId = userId, Pgn = game.Pgn, StartFen = "startpos", PlyCount = 2,
            Status = GameAnalysisStatus.Running, Origin = GameAnalysisOrigin.Guess,
        };
        analysis.Positions.Add(new GameAnalysisPosition
        {
            Ply = 0, Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            GameMoveUci = "e2e4", GameMoveSan = "e4", CandidatesJson = """[{"uci":"e2e4","cp":30}]""",
        });
        Db.GameAnalyses.Add(analysis);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        // Rueckfall (Unterabfrage): nicht verknuepft, aber eigene Analyse mit gleichem PGN.
        var evals = await svc.GetSharedEvalsAsync(game.ShareToken, userId);
        Assert.Equal(analysis.Id, evals!.AnalysisId);
        Assert.Equal(30, Assert.Single(evals.Plies).Cp);

        // Wiederverwenden (Parameter-Vergleich) — ohne Engine, weil nichts neu eingereiht wird.
        var result = await svc.AnalyzeAsync(userId, game.Id);
        Assert.True(result!.Reused);
        Assert.Equal(analysis.Id, result.Analysis!.Id);
    }

    [MySqlFact]
    public async Task Kursstatistik_UebersetztInklusiveKapitelNormalisierung()
    {
        var userId = await SeedUserAsync("kurs");
        var book = new Book { FileName = "it-course.pgn", DisplayName = "IT", OwnerUserId = userId, Source = new BookSource() };
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
                PublicId = "1", ChessResultsId = "1", Name = "Nah am Mittelpunkt", Federation = "AUT",
                StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 12),
                StartsOnWeekend = true, Lat = 47.80, Lon = 13.04, Speed = TournamentSpeed.Standard,
                PlayerCount = 30, LocationText = "Salzburg", GeoSource = GeoSource.City,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "2", ChessResultsId = "2", Name = "Weit weg", Federation = "AUT",
                StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 12),
                StartsOnWeekend = true, Lat = 48.21, Lon = 16.37, Speed = TournamentSpeed.Standard,
                PlayerCount = 30, LocationText = "Wien", GeoSource = GeoSource.City,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "3", ChessResultsId = "3", Name = "Langlaeufer ragt herein", Federation = "AUT",
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
        Assert.Equal(2, pins.Items.Count);
        Assert.False(pins.Truncated);

        Assert.NotNull((await svc.GetAsync("1"))?.Entry);
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
            PublicId = id, ChessResultsId = id, Name = name, BaseName = TournamentNameGrouping.BaseName(name),
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


    /// <summary>
    /// UX-039: Die Liste stellt im Zeitraum Beginnendes vor schon Laufendes (CASE im ORDER BY ueber
    /// der Gruppierung) und blendet unplausible Laufzeiten aus (DATE_ADD + EXISTS ueber die
    /// Spieltermine). InMemory rechnet beides in C# — ob MariaDB es uebersetzt und die Seiten
    /// dabei richtig zaehlt, zeigt nur dieser Test. Die Karte nutzt denselben Filter.
    /// </summary>
    [MySqlFact]
    public async Task Turnierverzeichnis_KommendeVorLaufenden_UnplausibleAusgeblendet()
    {
        var liga = new TournamentDirectoryEntry
        {
            PublicId = "it-ux039-liga", ChessResultsId = "it-ux039-liga", Name = "Landesliga", Federation = "AUT",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 10, 15), Lat = 47.8, Lon = 13.04,
            RoundDates = [new TournamentDirectoryRound { Number = 1, Date = new DateOnly(2026, 10, 18) }],
        };
        Db.TournamentDirectoryEntries.AddRange(
            liga,
            new TournamentDirectoryEntry
            {
                PublicId = "it-ux039-open", ChessResultsId = "it-ux039-open", Name = "Open", Federation = "AUT",
                StartDate = new DateOnly(2026, 10, 17), EndDate = new DateOnly(2026, 10, 18), Lat = 47.8, Lon = 13.04,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "it-ux039-ewig", ChessResultsId = "it-ux039-ewig", Name = "Ewig", Federation = "AUT",
                StartDate = new DateOnly(2007, 12, 26), EndDate = new DateOnly(2026, 12, 10), Lat = 47.8, Lon = 13.04,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "it-ux039-lang", ChessResultsId = "it-ux039-lang", Name = "Lang ohne Termine", Federation = "AUT",
                StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 10, 15), Lat = 47.8, Lon = 13.04,
            });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var svc = Get<TournamentDirectoryQueryService>();
        var window = new DirectorySearchQuery
        {
            Federation = "AUT", From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 12, 31), PageSize = 1,
        };

        var first = await svc.SearchAsync(window);
        Assert.Equal(2, first.Total);
        Assert.Equal("it-ux039-open", Assert.Single(first.Items).Entry.PublicId);
        var second = await svc.SearchAsync(window with { Page = 2 });
        var ongoing = Assert.Single(second.Items);
        Assert.Equal("it-ux039-liga", ongoing.Entry.PublicId);
        Assert.True(ongoing.Ongoing);

        var all = await svc.SearchAsync(window with { PageSize = 50, IncludeImplausible = true });
        Assert.Equal(["it-ux039-ewig", "it-ux039-lang"],
            all.Items.Where(i => i.Implausible).Select(i => i.Entry.PublicId).Order());

        var pins = await svc.MapPinsAsync(window, 47.0, 48.0, 12.0, 14.0);
        Assert.Equal(["it-ux039-liga", "it-ux039-open"], pins.Items.Select(i => i.Entry.PublicId).Order());
    }

    /// <summary>
    /// Mehrere Foederationen/Bedenkzeiten aus einem Suchprofil landen als `Contains` einer Liste
    /// im Where. InMemory wertet das in C# aus und winkt es durch — hier muss MySQL es wirklich
    /// uebersetzen (IN-Liste; die Bedenkzeit ist ein Enum und geht als Zahl raus).
    /// </summary>
    [MySqlFact]
    public async Task Search_WithSeveralFederationsAndSpeeds_TranslatesToSql()
    {
        Db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "a", ChessResultsId = "a", Name = "AUT Standard", Federation = "AUT",
                Speed = TournamentSpeed.Standard, StartDate = new DateOnly(2026, 10, 10),
                EndDate = new DateOnly(2026, 10, 12),
            },
            new TournamentDirectoryEntry
            {
                PublicId = "b", ChessResultsId = "b", Name = "GER Rapid", Federation = "GER",
                Speed = TournamentSpeed.Rapid, StartDate = new DateOnly(2026, 10, 10),
                EndDate = new DateOnly(2026, 10, 12),
            },
            new TournamentDirectoryEntry
            {
                PublicId = "c", ChessResultsId = "c", Name = "ITA Blitz", Federation = "ITA",
                Speed = TournamentSpeed.Blitz, StartDate = new DateOnly(2026, 10, 10),
                EndDate = new DateOnly(2026, 10, 12),
            });
        await Db.SaveChangesAsync();

        var svc = Get<TournamentDirectoryQueryService>();
        var result = await svc.SearchAsync(new DirectorySearchQuery
        {
            Federations = ["AUT", "GER"],
            Speeds = [TournamentSpeed.Standard, TournamentSpeed.Rapid],
        });

        Assert.Equal(["a", "b"], result.Items.Select(i => i.Entry.ChessResultsId).Order());
    }

    /// <summary>
    /// Die Umkreissuche muss ein Turnier auch ueber seinen ZWEITEN Spielort finden. Die Bedingung
    /// dafuer ist eine Unterabfrage (`e.Venues.Any(...)`) — InMemory wertet die in C# aus und
    /// winkt sie durch; hier muss MySQL sie wirklich uebersetzen.
    /// </summary>
    [MySqlFact]
    public async Task Search_FindsTournamentByItsSecondVenue()
    {
        var liga = new TournamentDirectoryEntry
        {
            PublicId = "liga", ChessResultsId = "liga", Name = "Frauenbundesliga", Federation = "AUT",
            StartDate = new DateOnly(2026, 11, 27), EndDate = new DateOnly(2027, 3, 14),
            // Hauptort Mayrhofen (Tirol) …
            Lat = 47.17, Lon = 11.87, GeoSource = GeoSource.City, GeoPlaceName = "Mayrhofen",
            Venues =
            [
                new TournamentDirectoryVenue { Ordinal = 0, Name = "Mayrhofen", Lat = 47.17, Lon = 11.87, GeoSource = GeoSource.City },
                // … zweiter Ort St. Veit an der Glan (Kaernten), 250 km entfernt.
                new TournamentDirectoryVenue { Ordinal = 1, Name = "St. Veit an der Glan", Lat = 46.77, Lon = 14.36, GeoSource = GeoSource.PostalCode },
            ],
        };
        Db.TournamentDirectoryEntries.Add(liga);
        await Db.SaveChangesAsync();

        var svc = Get<TournamentDirectoryQueryService>();

        // Umkreis um Klagenfurt: nur der ZWEITE Spielort liegt darin.
        var nearKlagenfurt = await svc.SearchAsync(new DirectorySearchQuery
        {
            Lat = 46.62, Lon = 14.31, RadiusKm = 40,
        });
        var found = Assert.Single(nearKlagenfurt.Items);
        Assert.Equal("liga", found.Entry.ChessResultsId);
        // Die Entfernung ist die zum NAECHSTEN Spielort, nicht die zum Hauptort.
        Assert.NotNull(found.DistanceKm);
        Assert.True(found.DistanceKm < 40, $"Entfernung {found.DistanceKm} km sollte die zum zweiten Ort sein");

        // Und ein Umkreis, in dem KEINER der beiden liegt, findet es nicht.
        var nearVienna = await svc.SearchAsync(new DirectorySearchQuery
        {
            Lat = 48.21, Lon = 16.37, RadiusKm = 30,
        });
        Assert.Empty(nearVienna.Items);
    }

    /// <summary>
    /// Meisterpartien-Analyse (2026-09-28): der Takt sucht die naechste Bibliothekspartie OHNE Analyse (NOT EXISTS ueber
    /// GameAnalyses, IN-Liste der unspielbaren), zaehlt die offenen Stellungen der laufenden Stapel-Analysen (Summe
    /// korrelierter Zaehlungen), und die Auftragsliste blendet deren Auftraege ueber eine korrelierte Unterabfrage aus.
    /// InMemory wertet das alles im Speicher aus — ob MariaDB es uebersetzt, zeigt nur dieser Test.
    /// </summary>
    [MySqlFact]
    public async Task Meisterpartien_TaktUndAuftragsliste_uebersetzenSichNachMariaDb()
    {
        var owner = await SeedUserAsync("haus");
        (await Db.AppUsers.FindAsync(owner))!.IsAdmin = true;
        var cred = new LichessEngineCredential { UserId = owner, EncryptedToken = "", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(["rhe_a", "rhe_b"]);
        Db.LichessEngineCredentials.Add(cred);
        Db.LibraryGames.Add(new LibraryGame { SourceFile = "t.pgn", MovesHash = "mh1", CommentedPlies = 2,
            Pgn = "[White \"A\"]\n[Black \"B\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *" });
        await Db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var scheduler = new MasterAnalysisScheduler(null!, new QuietHours(""), config, NullLogger<MasterAnalysisScheduler>.Instance);
        var id = await scheduler.TickOnceAsync(Db, Get<GameAnalysisService>(), default);
        Assert.NotNull(id);
        Assert.Null(await scheduler.TickOnceAsync(Db, Get<GameAnalysisService>(), default));   // 6 offen ≥ 2 Engines

        // Ob der Takt selbst einreiht, haengt an der Uhrzeit (Sperrzeit der Spark gilt auch hier). Hat er nichts
        // eingereiht, einen Auftrag von Hand an eine Stellung haengen — die Unterabfrage der Auftragsliste soll auf
        // jeden Fall etwas filtern. Nie an eine schon verknuepfte Stellung: deren Auftrag verloere sonst den Bezug.
        if (!await Db.GameAnalysisPositions.AnyAsync(p => p.GameAnalysisId == id && p.AnalysisJobId != null))
        {
            var position = await Db.GameAnalysisPositions.OrderBy(p => p.Ply).FirstAsync(p => p.GameAnalysisId == id);
            var job = new AnalysisJob { UserId = owner, Fen = position.Fen, EngineId = "rhe_a", TargetDepth = 20, MultiPv = 5,
                Background = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            Db.AnalysisJobs.Add(job);
            await Db.SaveChangesAsync();
            position.AnalysisJobId = job.Id;
            await Db.SaveChangesAsync();
        }

        Assert.Empty(await Get<AnalysisJobService>().ListAsync(owner));   // Auftraege der Meisterpartie ausgeblendet
        Assert.NotEmpty(await Db.AnalysisJobs.Where(j => j.UserId == owner).ToListAsync());
    }

    /// <summary>
    /// Vereinspartien (2026-09-28): der Takt nimmt sie VOR den Meisterpartien — NOT EXISTS ueber
    /// <c>GameAnalysis.LeagueClubGameId</c> (neue Spalte), die Zaehlung der offenen Stellungen ueber beide Etiketten —,
    /// und das Loeschen der Vereinspartie raeumt ihre Analyse mit ab.
    /// </summary>
    [MySqlFact]
    public async Task Vereinspartien_TaktUndLoeschen_uebersetzenSichNachMariaDb()
    {
        var owner = await SeedUserAsync("haus");
        (await Db.AppUsers.FindAsync(owner))!.IsAdmin = true;
        var cred = new LichessEngineCredential { UserId = owner, EncryptedToken = "", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(["rhe_a", "rhe_b"]);
        Db.LichessEngineCredentials.Add(cred);
        Db.LibraryGames.Add(new LibraryGame { SourceFile = "t.pgn", MovesHash = "mh1", CommentedPlies = 2,
            Pgn = "[White \"A\"]\n[Black \"B\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *" });
        var club = new LeagueClubGame { White = "Schwaz", Black = "Hengl, Philip", Year = 2025, Plies = 6, MovesHash = "c1",
            Pgn = "[White \"Schwaz\"]\n[Black \"Hengl, Philip\"]\n[Result \"1-0\"]\n\n1. d4 d5 2. c4 e6 3. Nc3 Nf6 1-0" };
        Db.LeagueClubGames.Add(club);
        await Db.SaveChangesAsync();

        var scheduler = new MasterAnalysisScheduler(null!, new QuietHours(""), new ConfigurationBuilder().Build(),
            NullLogger<MasterAnalysisScheduler>.Instance);
        var id = await scheduler.TickOnceAsync(Db, Get<GameAnalysisService>(), default);
        var analysis = await Db.GameAnalyses.AsNoTracking().SingleAsync(g => g.Id == id);
        Assert.Equal((GameAnalysisOrigin.Club, (int?)club.Id), (analysis.Origin, analysis.LeagueClubGameId));
        Assert.Null(await scheduler.TickOnceAsync(Db, Get<GameAnalysisService>(), default));   // 6 offen ≥ 2 Engines

        // Für alle im Verein (0.589.0): Stand in der Liste (IN über die nullbare Verknüpfung + gruppierte Zählung),
        // Bewertungen und Detail.
        var clubService = Get<RookHub.Api.Services.League.LeagueClubService>();
        var row = Assert.Single((await clubService.ListAsync(owner, false, null, null, 1, default)).Items);
        Assert.Equal(6, row.Analysis!.Total);
        Assert.Equal(id, (await clubService.EvalsAsync(club.Id))!.AnalysisId);
        Assert.Equal(club.Pgn, (await clubService.GetAsync(owner, false, club.Id))!.Pgn);

        Assert.Equal(RookHub.Api.Services.League.LeagueClubService.DeleteResult.Deleted,
            await Get<RookHub.Api.Services.League.LeagueClubService>().DeleteAsync(owner, true, club.Id));
        Assert.False(await Db.GameAnalyses.AnyAsync(g => g.Id == id));
        Assert.False(await Db.GameAnalysisPositions.AnyAsync(p => p.GameAnalysisId == id));
    }

    /// <summary>
    /// Online-Konten der Ligaspieler (0.605.0): der Eröffnungsbaum sucht die Online-Partien per Präfix (`LIKE 'e4 %'`),
    /// filtert auf eine Liste von Bedenkzeiten (Parameter-Sammlung), auf das Datum und über das Konto auf „sicher";
    /// Anlegen/Umbenennen vergleicht den Namen ohne Groß/klein, der Abruf wählt die fälligen Konten nach Stand.
    /// </summary>
    [MySqlFact]
    public async Task LigaOnlineKonten_BaumFilter_Umbenennen_UndFaelligeKonten()
    {
        Db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster max", FideId = "222" });
        await Db.SaveChangesAsync();
        var accounts = Get<RookHub.Api.Services.League.LeagueOnlineAccountService>();
        var (sure, _) = await accounts.CreateAsync("222", new("lichess", "Max_Muster", true, "Profil nennt den Verein"), default);
        var (unsure, _) = await accounts.CreateAsync("222", new("chess.com", "maxm", false, null), default);
        Assert.Equal("duplicate", (await accounts.CreateAsync("222", new("lichess", "max_muster", true, null), default)).Reason);
        Db.LeagueOnlineGames.AddRange(
            new LeagueOnlineGame { AccountId = sure!.Id, FideId = "222", ExternalId = "a", PlayedAt = DateTime.UtcNow.AddDays(-3),
                Speed = "blitz", White = true, Result = "1-0", Line = "e4 c5 Nf3", Moves = "e4 c5 Nf3", Plies = 3 },
            new LeagueOnlineGame { AccountId = sure.Id, FideId = "222", ExternalId = "b", PlayedAt = DateTime.UtcNow.AddYears(-3),
                Speed = "rapid", White = true, Result = "0-1", Line = "e4 e5", Moves = "e4 e5", Plies = 2 },
            new LeagueOnlineGame { AccountId = unsure!.Id, FideId = "222", ExternalId = "c", PlayedAt = DateTime.UtcNow.AddDays(-1),
                Speed = "blitz", White = true, Result = "1-0", Line = "e4", Moves = "e4", Plies = 1 });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var store = new RookHub.Api.Services.League.LeagueProfileStore(Db);
        var all = (await store.TreeAsync("222", "w", "e4", default,
            RookHub.Api.Services.League.LeagueProfileStore.TreeFilter.Parse("online", "blitz,rapid", 5, onlySure: false)))!;
        Assert.Equal((3, 1), (all["total"]!.GetValue<int>(), all["ended"]!.GetValue<int>()));
        var shared = (await store.TreeAsync("222", "w", "e4", default,
            RookHub.Api.Services.League.LeagueProfileStore.TreeFilter.Parse("online", "blitz", 1, onlySure: true)))!;
        Assert.Equal(1, shared["online"]!.GetValue<int>());

        var (renamed, reason) = await accounts.UpdateAsync(unsure.Id, new(null, "MAXM", null, "Blitz"), default);
        Assert.Null(reason);
        Assert.Equal(1, await Db.LeagueOnlineGames.CountAsync(g => g.AccountId == renamed!.Id));   // nur die Schreibweise
        (_, reason) = await accounts.UpdateAsync(unsure.Id, new(null, "anderer", null, null), default);
        Assert.Null(reason);
        Assert.Equal(0, await Db.LeagueOnlineGames.CountAsync(g => g.AccountId == unsure.Id));

        await Db.LeagueOnlineAccounts.ExecuteUpdateAsync(u => u.SetProperty(a => a.SyncedAt, DateTime.UtcNow));
        Db.ChangeTracker.Clear();
        var sync = new RookHub.Api.Services.League.LeagueOnlineSync(Db, new HttpClient(), NullLogger<RookHub.Api.Services.League.LeagueOnlineSync>.Instance);
        Assert.False(await sync.RunOnceAsync(TimeSpan.FromHours(12), TimeSpan.FromMinutes(1), default));   // nichts fällig, kein Abruf
    }

    /// <summary>
    /// Konto-Vorschläge (0.607.0): die Übersicht (Saison per MAX über einen Text, Namen per Join, Stand der Suche), das
    /// Erledigen eines Vorschlags beim Anlegen (Vergleich ohne Groß/klein), das Merken beim Entfernen und die Auswahl der
    /// fälligen Spieler der Konto-Suche.
    /// </summary>
    [MySqlFact]
    public async Task LigaKontoVorschlaege_Uebersicht_Uebernehmen_Verwerfen_UndFaelligeSpieler()
    {
        Db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = 1, Name = "Landesliga", Season = "2026/27", League = "LL", Stage = "x" },
            new LeagueTournament { Tnr = 2, Name = "Landesliga", Season = "2025/26", League = "LL", Stage = "x" });
        Db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "222", EloI = 1900 },
            new LeaguePlayer { Tnr = 2, Team = "Alt 1", Name = "Alt, Otto", NameKey = "alt, otto", FideId = "444" });
        Db.LeagueAccountSuggestions.AddRange(
            new LeagueAccountSuggestion { FideId = "222", Site = "lichess", UserName = "MaxMuster", Url = "u", Score = 5, Evidence = "e", CreatedAt = DateTime.UtcNow },
            new LeagueAccountSuggestion { FideId = "222", Site = "chess.com", UserName = "Max_Muster", Url = "u", Score = 2, Evidence = "e", CreatedAt = DateTime.UtcNow });
        Db.LeagueAccountScans.Add(new LeagueAccountScan { FideId = "222", BirthYear = 1987, ScannedAt = DateTime.UtcNow });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var accounts = Get<RookHub.Api.Services.League.LeagueOnlineAccountService>();
        var overview = await accounts.SuggestionsAsync(null, default);
        Assert.Equal((2, 1, 1), (overview["items"]!.AsArray().Count, overview["scanned"]!.GetValue<int>(), overview["total"]!.GetValue<int>()));
        Assert.Equal("Kufstein 1", overview["items"]![0]!["team"]!.GetValue<string>());

        var first = await Db.LeagueAccountSuggestions.AsNoTracking().SingleAsync(x => x.UserName == "MaxMuster");
        var (acc, reason) = await accounts.AcceptSuggestionAsync(first.Id, sure: true, default);
        Assert.Null(reason);
        await accounts.CreateAsync("222", new("chess.com", "MAX_MUSTER", false, null), default);   // erledigt den zweiten
        Assert.Empty((await accounts.SuggestionsAsync("222", default))["items"]!.AsArray());
        Assert.True(await accounts.DeleteAsync(acc!.Id, default));
        Assert.Equal(LeagueSuggestionStatus.Rejected,
            (await Db.LeagueAccountSuggestions.AsNoTracking().SingleAsync(x => x.UserName == "MaxMuster")).Status);

        // Fällig ist niemand: 222 ist eben abgesucht, 444 spielt nicht in der laufenden Saison.
        var finder = new RookHub.Api.Services.League.LeagueAccountFinder(Db, new HttpClient(),
            NullLogger<RookHub.Api.Services.League.LeagueAccountFinder>.Instance) { PlayerPause = TimeSpan.Zero, ChessComPause = TimeSpan.Zero };
        Assert.False(await finder.RunOnceAsync(TimeSpan.FromMinutes(1), default));
        Assert.Equal(("Muster, Max", (int?)1900), ((await finder.PlayerAsync("222", default))!.Name, (await finder.PlayerAsync("222", default))!.Elo));
    }

    /// <summary>
    /// Lichess-Übertragungen (0.608.0): die Auswahl der fälligen Übertragungen (nullbare Zeiten, Sortierung nach „schon
    /// eingespielt") und die Liste für die Verwaltung. Fällig ist hier nichts — fertig bzw. noch nicht begonnen —, also auch
    /// kein Abruf bei Lichess.
    /// </summary>
    [MySqlFact]
    public async Task LigaUebertragungen_FaelligeAuswahl_UndListe()
    {
        Db.LeagueBroadcasts.AddRange(
            new LeagueBroadcast { TourId = "done0001", Name = "Tirol Open", FoundAt = DateTime.UtcNow, StartsAt = DateTime.UtcNow.AddDays(-30),
                ImportedAt = DateTime.UtcNow.AddDays(-20), Finished = true, Games = 88 },
            new LeagueBroadcast { TourId = "later001", Name = "Kufstein Open", FoundAt = DateTime.UtcNow, StartsAt = DateTime.UtcNow.AddDays(5) },
            new LeagueBroadcast { TourId = "fresh001", Name = "Bundesliga", FoundAt = DateTime.UtcNow, StartsAt = DateTime.UtcNow.AddDays(-1),
                ImportedAt = DateTime.UtcNow.AddHours(-1) });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var imp = new RookHub.Api.Services.League.LeagueBroadcastImport(Db, new HttpClient(),
            NullLogger<RookHub.Api.Services.League.LeagueBroadcastImport>.Instance) { Pause = TimeSpan.Zero };
        Assert.False(await imp.RunOnceAsync(TimeSpan.FromMinutes(1), discover: false, default));
        Assert.Equal(3, (await imp.ListAsync(default)).Count);
    }

    /// <summary>
    /// Vereinspartien über einen Teilen-Link (Codereview 2026-09-29, A2-009): die neue Spalte <c>UploadShareHash</c> wird
    /// geschrieben, „alle Partien dieses Links entfernen" zählt und löscht über sie — und der Deckel kommt als EIN Singleton
    /// aus der DI (sonst hätte jeder Request seinen eigenen Zähler).
    /// </summary>
    [MySqlFact]
    public async Task VereinspartienUeberTeilenLink_VermerkUndRueckbau_uebersetzenSichNachMariaDb()
    {
        foreach (var (name, fide) in new[] { ("Hengl, Philip", "222"), ("Schnabl, Andreas", "333") })
            Db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Absam", Name = name,
                NameKey = RookHub.Api.Services.League.LeagueNames.NameKey(name), FideId = fide });
        await Db.SaveChangesAsync();
        var club = Get<RookHub.Api.Services.League.LeagueClubService>();

        var result = await club.ImportViaShareAsync("tokA",
            "[White \"Hengl, Philip\"]\n[Black \"Schnabl, Andreas\"]\n[Date \"2024.05.12\"]\n[Result \"1-0\"]\n\n"
            + "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. Qc2 O-O 5. a3 Bxc3+ 6. Qxc3 b6 7. Bg5 Bb7 8. f3 h6 9. Bh4 d5 10. e3 Nbd7 1-0\n", null);

        Assert.Equal(1, result.Added);
        Assert.Equal(RookHub.Api.Services.League.LeagueClubService.ShareHashOf("tokA"),
            (await Db.LeagueClubGames.AsNoTracking().SingleAsync()).UploadShareHash);
        Assert.Equal(1, await club.DeleteByShareAsync("tokA", dryRun: true));
        Assert.Equal(0, await club.DeleteByShareAsync("tokB", dryRun: false));
        Assert.Equal(1, await club.DeleteByShareAsync("tokA", dryRun: false));
        Assert.False(await Db.LeagueClubGames.AnyAsync());
        Assert.Same(Get<RookHub.Api.Services.League.LeagueShareUploadQuota>(),
            fixture.Factory.Services.GetRequiredService<RookHub.Api.Services.League.LeagueShareUploadQuota>());
    }

    /// <summary>
    /// Nacharbeit A2-009: <c>LeagueShares.Token</c> vergleicht in MariaDB groß/klein- und akzent-blind (Collation der
    /// Datenbank) — „abc…" und „Ábc…" sind derselbe gültige Link wie „AbC…". Vermerk an den Partien, Deckel je Link und
    /// Rückbau hängen deshalb am Token der Link-ZEILE: sonst bekäme jede Schreibweise ihren eigenen Topf mit 200 am Tag,
    /// und „alle Partien dieses Links entfernen" per Original fände ihre Partien nicht. InMemory vergleicht
    /// case-sensitiv und kann das nicht zeigen.
    /// </summary>
    [MySqlFact]
    public async Task VereinspartienUeberTeilenLink_AndereSchreibweise_selberVermerkDeckelUndRueckbau()
    {
        foreach (var (name, fide) in new[] { ("Hengl, Philip", "222"), ("Schnabl, Andreas", "333") })
            Db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Absam", Name = name,
                NameKey = RookHub.Api.Services.League.LeagueNames.NameKey(name), FideId = fide });
        const string link = "AbCdEfGhIjKlMnOpQrStUvWx";                 // wie LeagueService.NewToken: base64url, gemischt
        Db.LeagueShares.Add(new LeagueShare { Token = link, Tnr = 7, Round = 1, Team = "Absam",
            Expires = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7), CreatedAt = DateTime.UtcNow });
        await Db.SaveChangesAsync();
        var league = Get<RookHub.Api.Services.League.LeagueService>();
        var club = Get<RookHub.Api.Services.League.LeagueClubService>();
        var quota = Get<RookHub.Api.Services.League.LeagueShareUploadQuota>();
        var lower = link.ToLowerInvariant();
        var accented = "Á" + link[1..];

        // Die Voraussetzung: beide Schreibweisen sind in MariaDB gültig — und liefern das Token der Zeile.
        Assert.Equal(link, await league.ValidShareTokenAsync(lower, default));
        Assert.Equal(link, await league.ValidShareTokenAsync(accented, default));

        var controller = new RookHub.Api.Controllers.LeagueShareClubController(league, club, Get<ScoresheetScanService>(),
            Get<ScoresheetScanSignal>());
        var imported = await controller.Import(lower, new RookHub.Api.DTOs.LeagueClubImportRequest
        {
            Pgn = "[White \"Hengl, Philip\"]\n[Black \"Schnabl, Andreas\"]\n[Date \"2024.05.12\"]\n[Result \"1-0\"]\n\n"
                + "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. Qc2 O-O 5. a3 Bxc3+ 6. Qxc3 b6 7. Bg5 Bb7 8. f3 h6 9. Bh4 d5 10. e3 Nbd7 1-0\n",
        }, default);
        var result = Assert.IsType<RookHub.Api.DTOs.LeagueClubImportResultDto>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(imported.Result).Value);
        Assert.Equal(1, result.Added);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Add(accented, new RookHub.Api.DTOs.LeagueClubGameRequest
        {
            Moves = new() { "e4", "e5" }, White = "Hengl, Philip", Black = "Schnabl, Andreas", Year = 2024,
        }, null, default));

        // Derselbe Vermerk wie beim Original …
        var hash = RookHub.Api.Services.League.LeagueClubService.ShareHashOf(link);
        Assert.Equal(new[] { hash, hash }, await Db.LeagueClubGames.AsNoTracking().Select(g => g.UploadShareHash).ToListAsync());
        // … derselbe Deckel-Topf (die Schreibweisen haben keinen eigenen) …
        using (var rest = quota.Reserve(hash, 500))
            Assert.Equal(RookHub.Api.Services.League.LeagueShareUploadQuota.PerLinkPerDay - 2, rest.Granted);
        foreach (var variant in new[] { lower, accented })
            using (var own = quota.Reserve(RookHub.Api.Services.League.LeagueClubService.ShareHashOf(variant), 500))
                Assert.Equal(RookHub.Api.Services.League.LeagueShareUploadQuota.PerLinkPerDay, own.Granted);
        // … und der Rückbau trifft beide Partien, per Original wie per Schreibweise.
        Assert.Equal(2, await club.DeleteByShareAsync(link.ToUpperInvariant(), dryRun: true));
        Assert.Equal(2, await club.DeleteByShareAsync(link, dryRun: false));
        Assert.False(await Db.LeagueClubGames.AnyAsync());
    }

    /// <summary>
    /// ClubHub (0.613.0): die Kartei gegen echtes SQL — Sichtbarkeit des Trainers (<c>Any</c> über eine Id-Liste),
    /// Gruppenliste mit <c>GroupBy</c>/<c>Max</c> über ein <c>DateOnly</c>, Anwesenheitstabelle, der eindeutige Index
    /// (Gruppe, Tag) und die Fremdschlüssel: ein hart gelöschtes Konto löst die Verknüpfung (SetNull) und nimmt die
    /// Trainer-Zuteilung mit (Cascade), das Blatt bleibt. InMemory prüft nichts davon.
    /// </summary>
    [MySqlFact]
    public async Task ClubHub_Kartei_Anwesenheit_UndFremdschluessel_uebersetzenSichNachMariaDb()
    {
        var club = Get<RookHub.Api.Services.Club.ClubService>();
        var (leitung, tina, kind) = (await SeedUserAsync("leitung"), await SeedUserAsync("tina"), await SeedUserAsync("daniel2015"));
        var manager = new RookHub.Api.Services.Club.ClubActor(leitung, true, false);
        var trainer = new RookHub.Api.Services.Club.ClubActor(tina, false, true);

        var mine = await club.CreateGroupAsync(manager, new RookHub.Api.DTOs.ClubGroupInputDto { Name = "Anfänger", Weekday = 5 });
        var other = await club.CreateGroupAsync(manager, new RookHub.Api.DTOs.ClubGroupInputDto { Name = "Turnier" });
        await club.AddTrainerAsync(manager, mine.Id, "TINA");                     // Groß/klein egal — hier über die Kollation
        var daniel = await club.CreateMemberAsync(trainer, new RookHub.Api.DTOs.ClubMemberInputDto
        {
            FirstName = "Daniel", LastName = "Huber", BirthDate = "2015-03-12", GroupIds = [mine.Id],
            Contacts = [new() { Kind = "phone", Value = "0660 111 22 33", Label = "Mutter Daniela" },
                        new() { Kind = "phone", Value = "0512/58 12 34", Label = "Vater Franz" },
                        new() { Kind = "email", Value = "daniela@example.org" }],
        });
        var anna = await club.CreateMemberAsync(manager, new RookHub.Api.DTOs.ClubMemberInputDto { FirstName = "Anna", LastName = "Šarić", BirthYear = 2016, GroupIds = [other.Id] });
        // Ohne Nachnamen (optional): geordnet wird dann nach dem Vornamen — die CASE-Sortierung muss MariaDB übersetzen.
        var emil = await club.CreateMemberAsync(manager, new RookHub.Api.DTOs.ClubMemberInputDto { FirstName = "Emil", GroupIds = [other.Id] });
        Assert.Equal(["Emil", "Huber", "Šarić"], (await club.ListMembersAsync(manager, null, false)).Select(m => m.LastName == "" ? m.FirstName : m.LastName));
        Assert.Equal(["Emil", "Anna"], (await club.GetGroupAsync(manager, other.Id)).Members.Select(m => m.FirstName));
        await club.DeleteMemberAsync(manager, emil.Id);
        Db.ChangeTracker.Clear();

        // Trainer: nur das Kind der eigenen Gruppe, Kontakte in der Reihenfolge der Eingabe.
        var seen = Assert.Single(await club.ListMembersAsync(trainer, null, false));
        Assert.Equal(["0660 111 22 33", "0512/58 12 34", "daniela@example.org"], seen.Contacts.Select(c => c.Value));
        Assert.Equal(2, (await club.ListMembersAsync(manager, null, false)).Count);
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.NotFoundException>(() => club.GetMemberAsync(trainer, anna.Id));
        Assert.Equal("Anfänger", Assert.Single(await club.ListGroupsAsync(trainer)).Name);

        // Anwesenheit: eine Einheit je Tag (zweites Speichern ersetzt), Tabelle, Quote, Liste mit Max(Date).
        var att = new List<RookHub.Api.DTOs.ClubAttendanceInputDto> { new() { MemberId = daniel.Id, Status = "present" } };
        var first = await club.SaveSessionAsync(trainer, mine.Id, new RookHub.Api.DTOs.ClubSessionInputDto { Date = "2026-09-18", Attendance = att });
        await club.SaveSessionAsync(trainer, mine.Id, new RookHub.Api.DTOs.ClubSessionInputDto { Date = "2026-09-25", Topic = "Gabel", Attendance = att });
        att[0].Status = "absent";
        var again = await club.SaveSessionAsync(trainer, mine.Id, new RookHub.Api.DTOs.ClubSessionInputDto { Date = "2026-09-18", Attendance = att });
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.Id, (await club.GetSessionByDateAsync(trainer, mine.Id, "2026-09-18"))!.Id);
        Assert.Null(await club.GetSessionByDateAsync(trainer, mine.Id, "2026-09-19"));
        Db.ChangeTracker.Clear();
        var group = await club.GetGroupAsync(trainer, mine.Id);
        Assert.Equal(["2026-09-18", "2026-09-25"], group.Sessions.Select(x => x.Date));
        Assert.Equal(["absent", "present"], Assert.Single(group.Members).Statuses);
        Assert.Equal((1, 2), (group.Members[0].Present, group.Members[0].Recorded));
        var row = (await club.ListGroupsAsync(manager)).Single(g => g.Id == mine.Id);
        Assert.Equal((1, 2, "2026-09-25", "tina"), (row.MemberCount, row.SessionCount, row.LastSession, Assert.Single(row.Trainers).Username));
        var sheet = await club.GetMemberAsync(trainer, daniel.Id);
        Assert.Equal((1, 1, "2015-03-12"), (sheet.Attendance.Present, sheet.Attendance.Absent, sheet.BirthDate));

        // Trainer als Person: ohne Gruppe, in jeder Gruppe unter den Kindern, abhakbar.
        var coach = await club.CreateMemberAsync(trainer, new RookHub.Api.DTOs.ClubMemberInputDto { FirstName = "Bernhard", IsTrainer = true });
        Assert.Equal("Bernhard", Assert.Single((await club.GetGroupAsync(trainer, mine.Id)).Coaches).FirstName);
        Assert.Equal("Bernhard", Assert.Single((await club.GetGroupAsync(manager, other.Id)).Coaches).FirstName);
        var withCoach = await club.SaveSessionAsync(trainer, mine.Id, new RookHub.Api.DTOs.ClubSessionInputDto
        { Date = "2026-10-02", Attendance = [new() { MemberId = coach.Id, Status = "present" }, new() { MemberId = daniel.Id, Status = "absent" }] });
        Assert.Equal((1, 1), (withCoach.Present, withCoach.Absent));
        await club.DeleteMemberAsync(manager, coach.Id);
        Db.ChangeTracker.Clear();

        // Fotos zur Einheit: LONGBLOB/MEDIUMBLOB schreiben und lesen, Kennungen ohne Bytes, Löschen ohne Laden.
        using (var bmp = new SkiaSharp.SKBitmap(900, 600))
        {
            using var img = SkiaSharp.SKImage.FromBitmap(bmp);
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
            var photo = await club.AddPhotoAsync(trainer, withCoach.Id, data.ToArray());
            Assert.Equal((900, 600), (photo.Width, photo.Height));
            Assert.True((await club.GetPhotoAsync(trainer, withCoach.Id, photo.Id, thumb: true)).Length > 100);
            Assert.Equal(photo.Id, Assert.Single((await club.GetSessionAsync(trainer, withCoach.Id)).Photos).Id);
            await club.DeleteSessionAsync(trainer, withCoach.Id);
            Db.ChangeTracker.Clear();
            Assert.False(await Db.ClubSessionPhotos.AnyAsync());
        }

        // Verknüpfung + Notiz, dann das Konto HART löschen: SetNull und Cascade feuern, Blatt und Notiz bleiben.
        await club.RedeemAsync(kind, (await club.CreateLinkCodeAsync(trainer, daniel.Id)).Code);
        await club.AddNoteAsync(trainer, daniel.Id, "kann die Gabel");
        Assert.Equal((kind, "daniel2015"), await club.LinkedAccountAsync(manager, daniel.Id));
        Db.ChangeTracker.Clear();
        await Db.AppUsers.Where(u => u.Id == kind || u.Id == tina).ExecuteDeleteAsync();
        Assert.False(await Db.ClubGroupTrainers.AnyAsync());
        var after = await club.GetMemberAsync(manager, daniel.Id);
        Assert.Equal((false, null, "kann die Gabel", null), (after.Linked, after.LinkedUsername, Assert.Single(after.NoteEntries).Text, after.NoteEntries[0].Author));

        // Blatt und Gruppe löschen: nichts bleibt hängen.
        await club.DeleteMemberAsync(manager, daniel.Id);
        await club.DeleteGroupAsync(manager, mine.Id);
        Db.ChangeTracker.Clear();
        Assert.False(await Db.ClubContacts.AnyAsync());
        Assert.False(await Db.ClubAttendances.AnyAsync());
        Assert.False(await Db.ClubSessions.AnyAsync());
        Assert.False(await Db.ClubNotes.AnyAsync());
        Assert.Equal("Šarić", (await Db.ClubMembers.SingleAsync()).LastName);
    }
}
