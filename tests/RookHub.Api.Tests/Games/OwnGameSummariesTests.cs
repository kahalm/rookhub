using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.Games;

/// <summary>Zusammenfassungen eigener Partien (0.923.0). Die Anforderung selbst braucht eine laufende Analyse-Kette; hier zählt,
/// was daran hängt: Kontingent, Zuständigkeit, Warteschlange, Fallback auf die Vereinspartie.</summary>
public class OwnGameSummariesTests : IDisposable
{
    private const string Pgn = "[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 Nc6 1-0";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private OwnGameSummaries Svc(int? daily = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["OwnGameSummaries:DailyPerUser"] = daily?.ToString() }).Build();
        return new OwnGameSummaries(_db, null!, new ClubGameSummaries(_db), null!, null, config);
    }

    private SavedGame Game(int user = 7, int? analysis = null, int? club = null)
    {
        var g = new SavedGame { UserId = user, Source = "pgn", Pgn = Pgn, White = "A", Black = "B", Result = "1-0", GameAnalysisId = analysis, LeagueClubGameId = club };
        _db.SavedGames.Add(g);
        _db.SaveChanges();
        return g;
    }

    private GameAnalysis Analysis(GameAnalysisStatus status = GameAnalysisStatus.Done, GameAnalysisOrigin origin = GameAnalysisOrigin.SavedGame, DateTime? comparisons = null)
    {
        var a = new GameAnalysis { UserId = 7, Pgn = Pgn, StartFen = "x", Status = status, Origin = origin, MistakeComparisonsAt = comparisons };
        _db.GameAnalyses.Add(a);
        _db.SaveChanges();
        return a;
    }

    [Fact]
    public async Task Get_fremdePartie_null_ohneZeile_none_mitKontingent()
    {
        var g = Game();
        Assert.Null(await Svc().GetAsync(8, g.Id, false));
        var dto = await Svc().GetAsync(7, g.Id, false);
        Assert.Equal("none", dto!.Status);
        Assert.Equal(2, dto.QuotaLeft);
        Assert.Null((await Svc().GetAsync(7, g.Id, true))!.QuotaLeft);
    }

    [Fact]
    public async Task Request_KontingentVerbraucht_lehntAb_gestrigeZaehlenNicht()
    {
        var g = Game();
        _db.OwnGameSummaryRequests.AddRange(
            new OwnGameSummaryRequest { UserId = 7, SavedGameId = 1, At = DateTime.UtcNow },
            new OwnGameSummaryRequest { UserId = 7, SavedGameId = 2, At = DateTime.UtcNow });
        _db.SaveChanges();

        var (dto, error) = await Svc().RequestAsync(7, g.Id, false, null);

        Assert.Null(dto);
        Assert.Equal(OwnGameSummaries.RequestError.QuotaExceeded, error);
        Assert.Equal(0, (await Svc().GetAsync(7, g.Id, false))!.QuotaLeft);
        foreach (var r in _db.OwnGameSummaryRequests) r.At = DateTime.UtcNow.AddDays(-1);
        _db.SaveChanges();
        Assert.Equal(2, (await Svc().GetAsync(7, g.Id, false))!.QuotaLeft);
        Assert.Equal(5, (await Svc(5).GetAsync(7, g.Id, false))!.QuotaLeft);
    }

    [Fact]
    public async Task Request_nochOffen_kostetNichts()
    {
        var g = Game();
        _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = g.Id, UserId = 7, Status = ClubGameSummaryStatus.Requested, RequestedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var (dto, error) = await Svc(0).RequestAsync(7, g.Id, false, null);

        Assert.Equal(OwnGameSummaries.RequestError.None, error);
        Assert.Equal("requested", dto!.Status);
        Assert.Empty(_db.OwnGameSummaryRequests);
    }

    [Fact]
    public async Task Queue_nurBereite_undStartVerweigertWartende()
    {
        var ready = Game(analysis: Analysis(comparisons: DateTime.UtcNow).Id);
        var waiting = Game(analysis: Analysis(GameAnalysisStatus.Running).Id);
        foreach (var g in new[] { ready, waiting })
            _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = g.Id, UserId = 7, Status = ClubGameSummaryStatus.Requested, RequestedAt = DateTime.UtcNow });
        _db.SaveChanges();

        Assert.Equal(ready.Id, Assert.Single(await Svc().QueueAsync()).SavedGameId);
        Assert.Equal(OwnGameSummaries.Outcome.Conflict, await Svc().StartAsync(waiting.Id));
        Assert.Equal(OwnGameSummaries.Outcome.Ok, await Svc().StartAsync(ready.Id));
        Assert.Empty(await Svc().QueueAsync());
    }

    [Fact]
    public async Task Complete_undKorrektur_meldetVeraltet()
    {
        var g = Game(analysis: Analysis().Id);
        _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = g.Id, UserId = 7, Status = ClubGameSummaryStatus.Running, RequestedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow });
        _db.SaveChanges();

        Assert.Equal(OwnGameSummaries.Outcome.Invalid, await Svc().CompleteAsync(g.Id, " ", "fable"));
        Assert.Equal(OwnGameSummaries.Outcome.Ok, await Svc().CompleteAsync(g.Id, "# Text", "fable"));
        var done = await Svc().GetAsync(7, g.Id, false);
        Assert.Equal("done", done!.Status);
        Assert.False(done.Stale);

        g.Pgn = "[White \"A\"]\n[Black \"B\"]\n\n1. d4 d5 *";
        _db.SaveChanges();
        Assert.True((await Svc().GetAsync(7, g.Id, false))!.Stale);
    }

    [Fact]
    public async Task VerbundeneVereinspartie_ohneEigenenText_zeigtDenDesVereins()
    {
        var g = Game(club: 239);
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = 239, Status = ClubGameSummaryStatus.Done, Markdown = "# Verein", RequestedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var dto = await Svc().GetAsync(7, g.Id, false);

        Assert.Equal("club", dto!.Source);
        Assert.Equal("# Verein", dto.Markdown);
    }

    [Fact]
    public async Task WatcherGame_hatDieFormDesDigests()
    {
        var g = Game();
        var w = await Svc().WatcherGameAsync(g.Id);
        Assert.Equal("e2e4 e7e5 g1f3 b8c6", w!.Uci);
        Assert.Equal("A", w.White);
    }

    [Fact]
    public async Task Vergleiche_eigenePartie_nurMitOffenerZusammenfassung()
    {
        var a = new GameAnalysis { UserId = 7, Pgn = Pgn, StartFen = "x", Status = GameAnalysisStatus.Done, Origin = GameAnalysisOrigin.SavedGame };
        _db.GameAnalyses.Add(a);
        _db.SaveChanges();
        var g = Game(analysis: a.Id);

        Assert.False(ClubMistakeComparisons.Applies(a));
        Assert.Empty(await ClubMistakeComparisons.OwnSummaryAnalyses(_db).ToListAsync());
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        Assert.Null(a.MistakeComparisonsAt);

        _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = g.Id, UserId = 7, Status = ClubGameSummaryStatus.Requested, RequestedAt = DateTime.UtcNow });
        _db.SaveChanges();

        Assert.Equal(a.Id, Assert.Single(await ClubMistakeComparisons.OwnSummaryAnalyses(_db).ToListAsync()));
        Assert.True(ClubMistakeComparisons.Applies(a, ownSummaryWanted: true));
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        Assert.NotNull(a.MistakeComparisonsAt);
        Assert.True(await ClubMistakeComparisons.SummaryWantedAsync(_db, a.Id));
    }
}
