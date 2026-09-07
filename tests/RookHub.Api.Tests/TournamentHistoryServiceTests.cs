using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Turnierverlauf. Zwei chess-results-Seiten tragen ihn, und sie kosten sehr
/// unterschiedlich viel: die Spielersuche liefert in EINEM Abruf alle Teilnahmen (mit Platz und
/// Startnummer), die Spielerkarte liefert Punkte und Performance — aber einen Abruf je Turnier.
/// An einem echten Konto gemessen: 23 Turniere, 12 davon gespielt.
/// </summary>
public class TournamentHistoryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RoutingHandler _handler = new();

    public TournamentHistoryServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private TournamentHistoryService CreateService(IBackgroundTaskQueue? queue = null) =>
        new(_db, new ClientFactory(_handler), queue ?? new CountingQueue(),
            new TestLogger<TournamentHistoryService>());

    private async Task<int> CreateUserAsync(
        string username = "spieler", string? lastName = "Oberschmid", string? firstName = "Patrik",
        string? fideId = "1693034", string? chessResultsId = "144749")
    {
        var user = new AppUser { Username = username, PasswordHash = "x", Email = $"{username}@example.com" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        _db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id, LastName = lastName, FirstName = firstName,
            FideId = fideId, ChessResultsId = chessResultsId,
        });
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private const string TwoRows = """
        [{"tournamentId":"1107064","tournamentName":"Schach Tirol Open 2025","endDate":"2025/08/30",
          "snr":44,"identNumber":"144749","fideId":"1693034","rank":56,"rounds":9,"playerCount":56},
         {"tournamentId":"1479344","tournamentName":"TMM 1.Klasse 2026/2027","endDate":"2027/04/17",
          "snr":118,"identNumber":"144749","fideId":"1693034","rank":null,"rounds":11,"playerCount":226}]
        """;

    /// <summary>
    /// Ein MANNSCHAFTSturnier, wie die Spielersuche es liefert: gespielt, mit Startnummer, aber
    /// OHNE Platz — chess-results weist dort keinen Einzelplatz aus.
    /// </summary>
    private static string TeamRow =>
        $$"""
        [{"tournamentId":"1206267","tournamentName":"TMM 2.Klasse","endDate":"{{DateTime.UtcNow.AddMonths(-5):yyyy/MM/dd}}",
          "snr":73,"identNumber":"144749","fideId":"1693034","rank":null,"rounds":11,"playerCount":211}]
        """;

    /// <summary>
    /// Ein Turnier, das erst noch stattfindet — kein Platz, kein Ergebnis. Das Datum ist RELATIV:
    /// ein fest eingetragenes Jahr macht den Test irgendwann still zum Gegenteil seiner Aussage.
    /// </summary>
    private static string FutureRow =>
        $$"""
        [{"tournamentId":"1479344","tournamentName":"TMM 1.Klasse","endDate":"{{DateTime.UtcNow.AddYears(1):yyyy/MM/dd}}",
          "snr":118,"identNumber":"144749","fideId":"1693034","rank":null,"rounds":11,"playerCount":226}]
        """;

    private const string Card = """
        {"points":1.5,"rank":56,"performanceRating":1740,"ratingChange":-51.6,
         "ratingInternational":1923,"hasResult":true}
        """;

    // ----- Die Liste --------------------------------------------------------

    [Fact]
    public async Task GetAsync_FirstCall_StoresTheWholeList()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal(TournamentHistoryService.HistoryStatus.Ok, history.Status);
        Assert.Equal(2, history.Results.Count);
        // Neueste zuerst — das letzte Turnier ist das, dessen Ergebnis man sucht.
        Assert.Equal("1479344", history.Results[0].ChessResultsId);
        Assert.Equal(44, history.Results.Single(r => r.ChessResultsId == "1107064").Snr);
    }

    /// <summary>
    /// Ein Turnier kommt Wochen vor dem Termin in die Liste und verschwindet nie. Bei jedem
    /// Seitenaufruf nachzufragen waere nichts als Last bei chess-results.
    /// </summary>
    [Fact]
    public async Task GetAsync_SecondCallWithinTheTtl_DoesNotFetchAgain()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;

        await CreateService().GetAsync([userId]);
        await CreateService().GetAsync([userId]);

        Assert.Equal(1, _handler.HistoryCalls);
    }

    [Fact]
    public async Task GetAsync_StaleList_IsFetchedAgain()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        await CreateService().GetAsync([userId]);

        var sync = await _db.PlayerHistorySyncs.SingleAsync();
        sync.LastFetchedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        await CreateService().GetAsync([userId]);

        Assert.Equal(2, _handler.HistoryCalls);
    }

    /// <summary>
    /// Ohne Nachnamen laesst sich die chess-results-Spielersuche nicht stellen — sie sucht ueber
    /// den NAMEN. Ein Grund ist besser als eine leere Tabelle: „trage deinen Namen ein" ist eine
    /// Handlungsanweisung, „keine Turniere" waere eine Falschaussage.
    /// </summary>
    [Fact]
    public async Task GetAsync_ProfileWithoutName_SaysWhy()
    {
        var userId = await CreateUserAsync(lastName: null);

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal(TournamentHistoryService.HistoryStatus.NoName, history.Status);
        Assert.Empty(history.Results);
        Assert.Equal(0, _handler.HistoryCalls);
    }

    /// <summary>
    /// Ist chess-results nicht erreichbar, gilt der Zwischenspeicher weiter — eine alte Liste ist
    /// besser als keine — und der Zeitstempel bleibt ALT, damit der naechste Aufruf es wieder
    /// versucht statt zwoelf Stunden zu warten.
    /// </summary>
    [Fact]
    public async Task GetAsync_SourceDown_KeepsTheCacheAndRetriesNextTime()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        await CreateService().GetAsync([userId]);

        var sync = await _db.PlayerHistorySyncs.SingleAsync();
        sync.LastFetchedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        _handler.HistoryStatus = HttpStatusCode.InternalServerError;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal(TournamentHistoryService.HistoryStatus.SourceUnavailable, history.Status);
        Assert.Equal(2, history.Results.Count);           // Zwischenspeicher steht

        // Und der naechste Aufruf versucht es erneut, statt den Fehlschlag als frisch zu werten.
        var before = _handler.HistoryCalls;
        await CreateService().GetAsync([userId]);
        Assert.Equal(before + 1, _handler.HistoryCalls);
    }

    // ----- Wer ist der Spieler? --------------------------------------------

    /// <summary>
    /// Die Suche geht ueber den NAMEN und liefert damit auch Namensgleiche. Die FIDE-ID
    /// entscheidet — und sie hat Vorrang, weil bei einem AUSLANDS-Turnier in der
    /// chess-results-Ident-Spalte „0" steht und dann allein sie die Identitaet traegt.
    /// </summary>
    [Fact]
    public async Task GetAsync_NamesakeInTheResults_IsFilteredOut()
    {
        var userId = await CreateUserAsync();
        _handler.History = """
            [{"tournamentId":"1","tournamentName":"Meines","endDate":"2025/08/30","snr":44,
              "identNumber":"144749","fideId":"1693034","rank":10,"rounds":7,"playerCount":50},
             {"tournamentId":"2","tournamentName":"Fremdes","endDate":"2025/08/30","snr":7,
              "identNumber":"999999","fideId":"9999999","rank":3,"rounds":7,"playerCount":50}]
            """;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal("Meines", Assert.Single(history.Results).TournamentName);
    }

    /// <summary>
    /// Bei einem Auslandsturnier ist die Ident-Nummer „0" — die Zeile gehoert trotzdem dazu, weil
    /// die FIDE-ID passt. Ohne diese Regel fehlten genau die Turniere im Ausland.
    /// </summary>
    [Fact]
    public async Task GetAsync_ForeignTournamentWithoutIdentNumber_IsKept()
    {
        var userId = await CreateUserAsync();
        _handler.History = """
            [{"tournamentId":"3","tournamentName":"Tegernsee","endDate":"2025/04/15","snr":30,
              "identNumber":"0","fideId":"1693034","rank":30,"rounds":7,"playerCount":165}]
            """;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal("Tegernsee", Assert.Single(history.Results).TournamentName);
    }

    [Theory]
    [InlineData("1693034", "144749", "fide:1693034")]
    [InlineData(null, "144749", "cr:144749")]
    [InlineData("1693034", null, "fide:1693034")]
    public void IdentityOf_PrefersTheFideId(string? fide, string? ident, string expectedKey)
    {
        var identity = TournamentHistoryService.IdentityOf("Oberschmid", "Patrik", fide, ident);

        Assert.Equal(expectedKey, identity!.Key);
    }

    /// <summary>„0" ist bei chess-results „keine Nummer" und darf nicht als Kennung gelten.</summary>
    [Fact]
    public void IdentityOf_ZeroIdentNumber_IsNotAnIdentity()
    {
        var identity = TournamentHistoryService.IdentityOf("Oberschmid", "Patrik", null, "0");

        Assert.StartsWith("name:", identity!.Key);
        Assert.Null(identity.IdentNumber);
    }

    // ----- Die Spielerkarten ------------------------------------------------

    /// <summary>
    /// Ein KUENFTIGES Turnier bekommt keinen Kartenabruf: es hat noch kein Ergebnis, und der Platz
    /// in der Trefferliste steht auf „-". Bei dem gemessenen Konto spart das elf von 23 Abrufen.
    /// </summary>
    [Fact]
    public async Task GetAsync_OnlyPlayedTournaments_AreQueuedForTheirCard()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        var queue = new CountingQueue();

        var history = Assert.Single(await CreateService(queue).GetAsync([userId]));

        Assert.Equal(1, history.PendingResults);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task FetchCardAsync_StoresPointsAndPerformance()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = Card;
        await CreateService().GetAsync([userId]);

        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        var result = await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1107064");
        Assert.Equal(1.5m, result.Points);
        Assert.Equal(1740, result.PerformanceRating);
        Assert.Equal(-51.6m, result.RatingChange);
        Assert.Equal(1923, result.RatingInternational);
        Assert.NotNull(result.CardFetchedAt);
    }

    /// <summary>Ein abgeschlossenes Turnier aendert sich nie wieder — die Karte kommt einmal.</summary>
    [Fact]
    public async Task GetAsync_AfterTheCardArrived_QueuesNothingMore()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = Card;
        await CreateService().GetAsync([userId]);
        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        var queue = new CountingQueue();
        var history = Assert.Single(await CreateService(queue).GetAsync([userId]));

        Assert.Equal(0, history.PendingResults);
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// Auch ein LEERES Kartenergebnis wird vermerkt: sonst wird dieselbe Seite bei jedem
    /// Seitenaufruf erneut geholt. Ein NETZfehler dagegen nicht — der ist keine Auskunft.
    /// </summary>
    [Fact]
    public async Task FetchCardAsync_EmptyCard_IsRememberedButFailureIsNot()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = """{"hasResult":false}""";
        await CreateService().GetAsync([userId]);

        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);
        Assert.NotNull((await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1107064")).CardFetchedAt);

        _handler.CardStatus = HttpStatusCode.InternalServerError;
        await CreateService().FetchCardAsync("fide:1693034", "1479344", 118);
        Assert.Null((await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1479344")).CardFetchedAt);
    }

    /// <summary>
    /// Ein zweiter Listen-Abruf darf die schon geholten ERGEBNISSE nicht leeren — die Liste kennt
    /// sie nicht, und sie zu ueberschreiben hiesse, die Kartenabrufe wegzuwerfen.
    /// </summary>
    [Fact]
    public async Task GetAsync_ListRefresh_KeepsTheFetchedResults()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = Card;
        await CreateService().GetAsync([userId]);
        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        var sync = await _db.PlayerHistorySyncs.SingleAsync();
        sync.LastFetchedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        await CreateService().GetAsync([userId]);

        Assert.Equal(1.5m, (await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1107064")).Points);
    }

    // ----- Mannschaftsturniere ----------------------------------------------

    /// <summary>
    /// Eine Zeile OHNE Platz in der Trefferliste ist nicht zwingend ein kuenftiges Turnier:
    /// chess-results weist bei MANNSCHAFTSturnieren keinen Einzelplatz aus, die SPIELERKARTE
    /// kennt Punkte und Performance dort aber sehr wohl. Am echten Konto waren das acht von elf
    /// offenen Zeilen, die dauerhaft „noch kein Ergebnis" zeigten. Das Kriterium ist deshalb der
    /// TERMIN, nicht der Platz.
    /// </summary>
    [Fact]
    public async Task GetAsync_PlayedTeamTournamentWithoutRank_IsQueuedAnyway()
    {
        var userId = await CreateUserAsync();
        _handler.History = TeamRow;
        var queue = new CountingQueue();

        var history = Assert.Single(await CreateService(queue).GetAsync([userId]));

        Assert.Equal(1, history.PendingResults);
        Assert.Equal(1, queue.Count);
    }

    /// <summary>Ein KUENFTIGES Turnier bleibt aussen vor — es hat noch kein Ergebnis.</summary>
    [Fact]
    public async Task GetAsync_FutureTournament_IsNotQueued()
    {
        var userId = await CreateUserAsync();
        _handler.History = FutureRow;
        var queue = new CountingQueue();

        var history = Assert.Single(await CreateService(queue).GetAsync([userId]));

        Assert.Equal(0, history.PendingResults);
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// Der Platz der KARTE ist der genauere — ein zweiter Listen-Abruf darf ihn nicht wieder auf
    /// „kein Platz" zuruecksetzen. Genau das passierte bei Mannschaftsturnieren: die Trefferliste
    /// laesst die Spalte dort dauerhaft leer.
    /// </summary>
    [Fact]
    public async Task GetAsync_ListRefresh_KeepsTheRankFromTheCard()
    {
        var userId = await CreateUserAsync();
        _handler.History = TeamRow;
        _handler.Card = """{"points":3,"rank":34,"performanceRating":2013,"hasResult":true}""";
        await CreateService().GetAsync([userId]);
        await CreateService().FetchCardAsync("fide:1693034", "1206267", 73);

        var sync = await _db.PlayerHistorySyncs.SingleAsync();
        sync.LastFetchedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        await CreateService().GetAsync([userId]);

        Assert.Equal(34, (await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1206267")).Rank);
    }

    // ----- Der Hintergrund-Durchgang ----------------------------------------

    /// <summary>
    /// Der naechtliche Durchgang holt Liste UND Karten, ohne dass jemand die Seite offen hat —
    /// vorher entstand der Verlauf ausschliesslich beim Ansehen.
    /// </summary>
    [Fact]
    public async Task RefreshAllAsync_FetchesListAndCards_WithoutAnyoneWatching()
    {
        await CreateUserAsync();
        _handler.History = TeamRow;
        _handler.Card = """{"points":3,"rank":34,"performanceRating":2013,"hasResult":true}""";

        var sweep = await CreateService().RefreshAllAsync(50);

        Assert.Equal(1, sweep.Players);
        Assert.Equal(1, sweep.Cards);
        Assert.Equal(1, sweep.TimeControls);
        // Die Bedenkzeit gehoert dem TURNIER und wird gleich mit eingeordnet.
        var speed = await _db.TournamentTimeControls.SingleAsync();
        Assert.Equal("90 min + 30 sec", speed.TimeControlText);
        Assert.Equal(TournamentSpeed.Standard, speed.Speed);
        var result = await _db.PlayerTournamentResults.SingleAsync();
        Assert.Equal(3m, result.Points);
        Assert.Equal(2013, result.PerformanceRating);
    }

    /// <summary>
    /// Der Deckel gilt fuer den GANZEN Lauf: ein Vielspieler soll die uebrigen Konten nicht
    /// aushungern. Der Rest kommt in der naechsten Nacht.
    /// </summary>
    [Fact]
    public async Task RefreshAllAsync_StopsAtTheCardLimit()
    {
        await CreateUserAsync();
        var played = DateTime.UtcNow.AddMonths(-5).ToString("yyyy-MM-dd");
        _handler.History = $$"""
            [{"tournamentId":"1206267","tournamentName":"TMM 2.Klasse","endDate":"{{played}}",
              "snr":73,"identNumber":"144749","fideId":"1693034","rank":null,"rounds":11,"playerCount":211},
             {"tournamentId":"1206271","tournamentName":"TMM Landesliga","endDate":"{{played}}",
              "snr":68,"identNumber":"144749","fideId":"1693034","rank":null,"rounds":9,"playerCount":195}]
            """;
        _handler.Card = """{"points":3,"rank":34,"performanceRating":2013,"hasResult":true}""";

        var sweep = await CreateService().RefreshAllAsync(1);

        Assert.Equal(1, sweep.Cards);
        // Der Deckel gilt fuer die SUMME der Abrufe: nach der einen Karte ist Schluss, die
        // Bedenkzeiten kommen in der naechsten Nacht.
        Assert.Equal(0, sweep.TimeControls);
        Assert.Equal(1, await _db.PlayerTournamentResults.CountAsync(r => r.CardFetchedAt != null));
    }

    /// <summary>
    /// Zwei Konten desselben Spielers teilen sich den Zwischenspeicher — ein zweiter Abruf fuer
    /// denselben Schluessel braechte nichts.
    /// </summary>
    [Fact]
    public async Task RefreshAllAsync_SamePlayerTwice_FetchesTheListOnce()
    {
        await CreateUserAsync("konto1");
        await CreateUserAsync("konto2");
        _handler.History = TeamRow;

        var sweep = await CreateService().RefreshAllAsync(0);

        Assert.Equal(1, sweep.Players);
        Assert.Equal(1, _handler.HistoryCalls);
    }

    /// <summary>Ohne Nachnamen gibt es keine Spielersuche — das Konto wird uebersprungen.</summary>
    [Fact]
    public async Task RefreshAllAsync_ProfileWithoutName_IsSkipped()
    {
        await CreateUserAsync("ohnename", lastName: null, fideId: null, chessResultsId: null);
        _handler.History = TeamRow;

        var sweep = await CreateService().RefreshAllAsync(50);

        Assert.Equal(0, sweep.Players);
        Assert.Equal(0, _handler.HistoryCalls);
    }

    /// <summary>
    /// chess-results schreibt eine <c>0</c> in die Performance-Spalte, wenn es sie NICHT
    /// berechnet hat (Gegner ohne Wertung, sehr wenige Partien, 0 % oder 100 %). Als Wertung
    /// uebernommen zieht sie jeden Schnitt nach unten — am Dev-Konto vier Turniere, die Punkte und
    /// Elo-Aenderung tragen, aber keine Performance.
    /// </summary>
    [Fact]
    public async Task FetchCardAsync_APerformanceOfZero_IsNotAValue()
    {
        var userId = await CreateUserAsync();
        _handler.History = TeamRow;
        _handler.Card = """{"points":0.5,"rank":34,"performanceRating":0,"ratingChange":-8,"hasResult":true}""";
        await CreateService().GetAsync([userId]);

        await CreateService().FetchCardAsync("fide:1693034", "1206267", 73);

        var result = await _db.PlayerTournamentResults.SingleAsync();
        Assert.Null(result.PerformanceRating);
        // Punkte und Elo-Aenderung stehen sehr wohl — nur die Performance fehlt.
        Assert.Equal(0.5m, result.Points);
        Assert.Equal(-8m, result.RatingChange);
    }

    /// <summary>
    /// Der BESTAND traegt die alten Nullen noch, und ein abgeschlossenes Turnier wird nie wieder
    /// geholt — sie muessen also ohne Abruf verschwinden.
    /// </summary>
    [Fact]
    public async Task ClearImplausiblePerformancesAsync_RemovesThePlaceholders()
    {
        _db.PlayerTournamentResults.AddRange(
            new PlayerTournamentResult { PlayerKey = "k", ChessResultsId = "1", PerformanceRating = 0, Points = 0.5m },
            new PlayerTournamentResult { PlayerKey = "k", ChessResultsId = "2", PerformanceRating = 1800 });
        await _db.SaveChangesAsync();

        var cleared = await CreateService().ClearImplausiblePerformancesAsync();

        Assert.Equal(1, cleared);
        Assert.Null((await _db.PlayerTournamentResults.SingleAsync(r => r.ChessResultsId == "1")).PerformanceRating);
        Assert.Equal(1800, (await _db.PlayerTournamentResults.SingleAsync(r => r.ChessResultsId == "2")).PerformanceRating);
        Assert.Equal(0, _handler.CardCalls);
    }

    // ----- Die Bedenkzeit ---------------------------------------------------

    /// <summary>
    /// Die Bedenkzeit gehoert dem TURNIER, nicht der Teilnahme: zwei Konten im selben Open teilen
    /// sie sich, und der Abruf faellt nur einmal an.
    /// </summary>
    [Fact]
    public async Task FetchTimeControlAsync_IsFetchedOncePerTournament()
    {
        await CreateService().FetchTimeControlAsync("1206267");
        await CreateService().FetchTimeControlAsync("1206267");

        Assert.Equal(1, _handler.InfoCalls);
        Assert.Equal(TournamentSpeed.Standard, (await _db.TournamentTimeControls.SingleAsync()).Speed);
    }

    /// <summary>
    /// Auch OHNE gefundene Bedenkzeit wird ein Ergebnis vermerkt — sonst wuerde dieselbe Seite bei
    /// jedem Durchgang erneut geholt. Ein NETZfehler legt dagegen nichts an.
    /// </summary>
    [Fact]
    public async Task FetchTimeControlAsync_EmptyIsRemembered_ButFailureIsNot()
    {
        _handler.Info = """{"tournamentId":"1","timeControl":null}""";
        await CreateService().FetchTimeControlAsync("1206267");

        var stored = await _db.TournamentTimeControls.SingleAsync();
        Assert.Null(stored.TimeControlText);
        Assert.Equal(TournamentSpeed.Unknown, stored.Speed);

        _handler.InfoStatus = HttpStatusCode.InternalServerError;
        await CreateService().FetchTimeControlAsync("1479344");
        Assert.False(await _db.TournamentTimeControls.AnyAsync(t => t.ChessResultsId == "1479344"));
    }

    /// <summary>
    /// chess-results NENNT die Klasse selbst (in Klammern hinter der Beschriftung). Diese Angabe
    /// schlaegt die Ableitung aus dem Freitext: eine Bedenkzeit wie „90 Min. / 40 Zuege + 30 Min."
    /// richtig zu addieren ist Raten, „(Standard)" ist eine Aussage.
    /// </summary>
    [Theory]
    [InlineData("Standard", TournamentSpeed.Standard)]
    [InlineData("Rapid", TournamentSpeed.Rapid)]
    [InlineData("Blitz", TournamentSpeed.Blitz)]
    public async Task FetchTimeControlAsync_PrefersTheClassTheSourceStates(string kind, TournamentSpeed expected)
    {
        // Der TEXT wuerde auf Turnierschach hinauslaufen — die Quelle sagt etwas anderes.
        _handler.Info = $$"""{"tournamentId":"1","timeControl":"90 min + 30 sec","timeControlKind":"{{kind}}"}""";

        await CreateService().FetchTimeControlAsync("1206267");

        Assert.Equal(expected, (await _db.TournamentTimeControls.SingleAsync()).Speed);
    }

    /// <summary>Ohne genannte Klasse rechnet der Klassifizierer aus dem Freitext.</summary>
    [Fact]
    public async Task FetchTimeControlAsync_WithoutAStatedClass_FallsBackToTheText()
    {
        _handler.Info = """{"tournamentId":"1","timeControl":"5 min + 3 sec"}""";

        await CreateService().FetchTimeControlAsync("1206267");

        Assert.Equal(TournamentSpeed.Blitz, (await _db.TournamentTimeControls.SingleAsync()).Speed);
    }

    /// <summary>
    /// „Einmal geholt, nie wieder" friert einen PARSER-Fehler fuer immer ein — eine Zeile mit
    /// `Unknown` saehe danach aus wie „das Turnier nennt keine Bedenkzeit". Genau das ist
    /// passiert (der GET lieferte die Turnierdetails gar nicht). Eine aeltere Fassung wird
    /// deshalb einmal nachgeholt.
    /// </summary>
    [Fact]
    public async Task FetchTimeControlAsync_AnOlderVersion_IsFetchedAgain()
    {
        _db.TournamentTimeControls.Add(new TournamentTimeControl
        {
            ChessResultsId = "1206267", Speed = TournamentSpeed.Unknown, Version = 1,
        });
        await _db.SaveChangesAsync();

        await CreateService().FetchTimeControlAsync("1206267");

        var row = await _db.TournamentTimeControls.SingleAsync();
        Assert.Equal(TournamentSpeed.Standard, row.Speed);
        Assert.Equal(TournamentHistoryService.CurrentTimeControlVersion, row.Version);
        Assert.Equal(1, _handler.InfoCalls);
    }

    /// <summary>
    /// Dieselbe Falle bei der Spielerkarte: ein spaeter ergaenztes Feld (die Partienzahl) bekaeme
    /// der Bestand nie, weil ein abgeschlossenes Turnier sonst nur einmal geholt wird.
    /// </summary>
    [Fact]
    public async Task FetchCardAsync_AnOlderCardVersion_IsFetchedAgain()
    {
        var userId = await CreateUserAsync();
        _handler.History = TeamRow;
        _handler.Card = """{"points":3,"rank":34,"performanceRating":2013,"gamesPlayed":7,"hasResult":true}""";
        await CreateService().GetAsync([userId]);

        var stored = await _db.PlayerTournamentResults.SingleAsync();
        stored.CardFetchedAt = DateTime.UtcNow.AddDays(-1);
        stored.CardVersion = 1;
        await _db.SaveChangesAsync();

        await CreateService().FetchCardAsync("fide:1693034", "1206267", 73);

        var after = await _db.PlayerTournamentResults.SingleAsync();
        Assert.Equal(7, after.GamesPlayed);
        Assert.Equal(TournamentHistoryService.CurrentCardVersion, after.CardVersion);
    }

    /// <summary>Die Klasse reist mit dem Verlauf mit — die Ansicht trennt danach ihre Auswertung.</summary>
    [Fact]
    public async Task GetAsync_CarriesTheSpeedOfEachTournament()
    {
        var userId = await CreateUserAsync();
        _handler.History = TeamRow;
        _db.TournamentTimeControls.Add(new TournamentTimeControl
        {
            ChessResultsId = "1206267", TimeControlText = "5 min + 3 sec", Speed = TournamentSpeed.Blitz,
        });
        await _db.SaveChangesAsync();

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal(TournamentSpeed.Blitz, history.Speeds["1206267"]);
    }

    /// <summary>
    /// Eine verbesserte Einordnungsregel erreicht den BESTAND ohne einen einzigen Abruf — genau
    /// dafuer liegt der Rohtext neben der Klasse. Am Dev-Stand blieben sechs Turniere ohne Klasse,
    /// obwohl die Bedenkzeit dastand („90'/40m + 30'/end", „90+30", „10 minuta po igraču").
    /// </summary>
    [Fact]
    public async Task ReclassifyTimeControlsAsync_UsesTheStoredText_WithoutFetching()
    {
        _db.TournamentTimeControls.AddRange(
            new TournamentTimeControl { ChessResultsId = "1", TimeControlText = "90+30", Speed = TournamentSpeed.Unknown },
            new TournamentTimeControl { ChessResultsId = "2", TimeControlText = "10 minuta po igraču", Speed = TournamentSpeed.Unknown });
        await _db.SaveChangesAsync();

        var changed = await CreateService().ReclassifyTimeControlsAsync();

        Assert.Equal(2, changed);
        Assert.Equal(TournamentSpeed.Standard, (await _db.TournamentTimeControls.SingleAsync(t => t.ChessResultsId == "1")).Speed);
        Assert.Equal(TournamentSpeed.Rapid, (await _db.TournamentTimeControls.SingleAsync(t => t.ChessResultsId == "2")).Speed);
        // Kein Netz im Spiel.
        Assert.Equal(0, _handler.InfoCalls);
    }

    /// <summary>
    /// Angefasst werden nur die HEUTE unbekannten. Eine schon eingeordnete Zeile nachtraeglich
    /// umzuschreiben waere eine stille Korrektur an Daten, die jemand bereits gesehen hat — und
    /// ein Text, aus dem weiterhin nichts zu lesen ist, bleibt unbekannt.
    /// </summary>
    [Fact]
    public async Task ReclassifyTimeControlsAsync_LeavesTheAlreadyClassifiedAndTheUnreadableAlone()
    {
        _db.TournamentTimeControls.AddRange(
            new TournamentTimeControl { ChessResultsId = "1", TimeControlText = "5 min", Speed = TournamentSpeed.Standard },
            new TournamentTimeControl { ChessResultsId = "2", TimeControlText = "nach Vereinbarung", Speed = TournamentSpeed.Unknown });
        await _db.SaveChangesAsync();

        var changed = await CreateService().ReclassifyTimeControlsAsync();

        Assert.Equal(0, changed);
        Assert.Equal(TournamentSpeed.Standard, (await _db.TournamentTimeControls.SingleAsync(t => t.ChessResultsId == "1")).Speed);
        Assert.Equal(TournamentSpeed.Unknown, (await _db.TournamentTimeControls.SingleAsync(t => t.ChessResultsId == "2")).Speed);
    }

    // ----- Der Zeitplan -----------------------------------------------------

    /// <summary>
    /// 04:30 UTC liegt bewusst NACH dem Verzeichnis-Sweep (03:00): beide sprechen ueber denselben
    /// prozessweiten Rate-Limiter mit chess-results.
    /// </summary>
    [Fact]
    public void TimeUntilNextRun_BeforeTheRunTime_WaitsUntilToday()
    {
        var delay = PlayerHistoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 7, 2, 30, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void TimeUntilNextRun_AfterTheRunTime_WaitsUntilTomorrow()
    {
        var delay = PlayerHistoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 7, 6, 30, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(22), delay);
    }

    /// <summary>Null Wartezeit wuerde die Schleife in derselben Sekunde erneut feuern.</summary>
    [Fact]
    public void TimeUntilNextRun_ExactlyAtRunTime_DoesNotReturnZero()
    {
        var delay = PlayerHistoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 7, 4, 30, 0, DateTimeKind.Utc));
        Assert.True(delay >= TimeSpan.FromSeconds(1));
    }

    // ----- Attrappen --------------------------------------------------------

    /// <summary>Antwortet je nach Pfad — Trefferliste oder Spielerkarte.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public string History { get; set; } = "[]";
        public string Card { get; set; } = """{"hasResult":false}""";
        /// <summary>Die Turnierdetails — dort steht die Bedenkzeit, die Trefferliste kennt sie nicht.</summary>
        public string Info { get; set; } = """{"tournamentId":"1","timeControl":"90 min + 30 sec","timeControlKind":"Standard"}""";
        public HttpStatusCode HistoryStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode CardStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode InfoStatus { get; set; } = HttpStatusCode.OK;
        public int HistoryCalls { get; private set; }
        public int CardCalls { get; private set; }
        public int InfoCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("player-card", StringComparison.Ordinal))
            {
                CardCalls++;
                return Reply(CardStatus, Card);
            }
            if (url.Contains("tournament-info", StringComparison.Ordinal))
            {
                InfoCalls++;
                return Reply(InfoStatus, Info);
            }
            HistoryCalls++;
            return Reply(HistoryStatus, History);
        }

        private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>Zaehlt die eingereihten Auftraege, ohne sie auszufuehren.</summary>
    private sealed class CountingQueue : IBackgroundTaskQueue
    {
        public int Count { get; private set; }

        public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            Count++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
