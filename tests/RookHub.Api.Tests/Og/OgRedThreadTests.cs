using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Og;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Og;

namespace RookHub.Api.Tests.Og;

/// <summary>
/// Roter Faden der Zusammenfassung als <c>og:description</c> (Wunsch 2026-11-25). Je Route: gekürzt und ohne Markdown; aus demselben,
/// schon gefilterten Text wie die Seite des Links; steht ein interner Name darin, bleibt es bei der bisherigen Beschreibung. Spieler
/// erfunden, Absätze nach den Prod-Fassungen.
/// </summary>
public class OgRedThreadTests : IDisposable
{
    private const string Base = "https://rh.example";
    private const string RealName = "Ackermann, Anna";
    private const int Tnr = 4711, Round = 2;
    private const string Pgn = "[Event \"Landesliga Runde 2\"]\n[White \"Ackermann, Anna\"]\n[Black \"Brunner, Bert\"]\n[Result \"1-0\"]\n"
        + "[WhiteElo \"1800\"]\n[BlackElo \"1750\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0\n";
    private const string Thread = "Der rote Faden ist das **Läuferpaar**. Schwarz bekam es im siebten Zug und spielte danach nur noch darauf, ihm Linien zu verschaffen.";
    private const string ThreadPlain = "Der rote Faden ist das Läuferpaar. Schwarz bekam es im siebten Zug und spielte danach nur noch darauf, ihm Linien zu verschaffen.";

    private static string Summary(string thread) =>
        $"# Testdorf – Brunner, Bert (1-0)\n\n## Die Partie\n\nDie Partie begann spanisch: 1.e4 e5 2.Sf3 Sc6 3.Lb5 a6\n\n{thread}\n\n## Was man mitnehmen kann\n\n- Läufer brauchen Linien.\n";

    private readonly AppDbContext _db = OgCardTests.NewDb();
    public void Dispose() => _db.Dispose();

    private OgMetaService Meta()
    {
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var clubService = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        var summaries = new ClubGameSummaries(_db);
        var games = new SavedGameService(_db, TestServices.GameAnalyses(_db), TestServices.Analyze(_db), NullLogger<SavedGameService>.Instance);
        return new(games, null!, null!, null!, _db, NullLogger<OgMetaService>.Instance,
            clubGames: new ClubGameLinks(_db, clubService, summaries, new ClubGameComparisons(_db, null!)),
            leagueClubGames: new LeagueShareClubGames(_db, league, new LeagueFixtureGames(_db), clubService),
            anonGames: new AnonGameShares(_db),
            ownSummaries: new OwnGameSummaries(_db, games, summaries, null!));
    }

    private async Task<LeagueClubGame> ClubGameAsync(bool anon, string markdown, string? token = "cgtok990801")
    {
        await TestClubs.SeedAsync(_db);
        var g = new LeagueClubGame
        {
            ClubId = TestClubs.HomeId, Year = 2026, White = anon ? "Testdorf" : "Hess, Max", Black = "Brunner, Bert", Result = "1-0", Plies = 6,
            Pgn = Pgn, MovesHash = "h990801", Anonymized = anon, Event = "Landesliga Runde 2", WhiteElo = 1800, BlackElo = 1750,
            WhiteRealName = anon ? RealName : null, ShareToken = token,
        };
        _db.LeagueClubGames.Add(g);
        await _db.SaveChangesAsync();
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = g.Id, Status = ClubGameSummaryStatus.Done, Markdown = markdown, MovesHash = "h990801", DoneAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return g;
    }

    [Theory]
    [InlineData("/cg/{0}/summary")]
    [InlineData("/cg/{0}")]
    public async Task ClubGameLink_RedThreadIsTheDescription(string pathFormat)
    {
        var g = await ClubGameAsync(anon: true, Summary(Thread));
        var page = await Meta().ResolvePageAsync(string.Format(pathFormat, g.ShareToken), Base);
        Assert.Equal(ThreadPlain, page!.Description);
        Assert.Equal("Testdorf – Brunner, Bert (Elo 1750)", page.Title);   // Titel und Bild wie bisher
        Assert.Contains($"og:description\" content=\"{ThreadPlain}\"", OgController.Inject("<html><head></head></html>", page));
    }

    [Fact]
    public async Task ClubGameLink_RealNameInText_OldDescription()
    {
        // ClubGameLinks.SummaryAsync gibt bei einem internen Namen IRGENDWO im Text keinen Text her — dann auch keinen roten Faden.
        var g = await ClubGameAsync(anon: true, Summary("Der rote Faden ist das Läuferpaar von Ackermann. Schwarz bekam es früh."));
        var page = await Meta().ResolvePageAsync($"/cg/{g.ShareToken}/summary", Base);
        Assert.Equal("1-0 · Partie auf RookHub nachspielen", page!.Description);
        Assert.DoesNotContain("Ackermann", OgController.Inject("<html><head></head></html>", page));
    }

    [Fact]
    public async Task ClubGameLink_TrainingLink_KeepsTrainingDescription()
    {
        var g = await ClubGameAsync(anon: false, Summary(Thread));
        var page = await Meta().ResolvePageAsync($"/cg/{g.ShareToken}?train=white", Base);
        Assert.StartsWith("Hess, Max – Brunner, Bert", page!.Description);
    }

    [Fact]
    public async Task ClubGameLink_NoRedThread_OldDescription()
    {
        var g = await ClubGameAsync(anon: false, Summary("Soweit die Notation trägt, fiel die Entscheidung im achten Zug."));
        var page = await Meta().ResolvePageAsync($"/cg/{g.ShareToken}/summary", Base);
        Assert.Equal("1-0 · Landesliga Runde 2 · Partie auf RookHub nachspielen", page!.Description);
    }

    private async Task<int> LeagueLinkGameAsync(bool anon, string markdown)
    {
        var g = await ClubGameAsync(anon, markdown, token: null);
        _db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = Round, Date = new DateOnly(2026, 10, 11) });
        var lg = new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = 1, Board = 1, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
            HomePlayer = RealName, AwayPlayer = "Brunner, Bert", HomeColor = "w", Result = "1 - 0" };
        _db.LeagueGames.Add(lg);
        LeagueGameLinks.Set(g, lg);
        _db.LeagueShares.Add(new LeagueShare { Token = "LINK1", ClubId = TestClubs.HomeId, Tnr = Tnr, Round = Round, Team = "Testdorf",
            Expires = new DateOnly(2099, 1, 1), CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return g.Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeagueLink_RedThreadOnGameAndSummary(bool anon)
    {
        var id = await LeagueLinkGameAsync(anon, Summary(Thread));
        foreach (var path in new[] { $"/s/LINK1/club-games/{id}", $"/s/LINK1/club-games/{id}/summary" })
            Assert.Equal(ThreadPlain, (await Meta().ResolvePageAsync(path, Base))!.Description);
    }

    [Fact]
    public async Task LeagueLink_Anonymized_RealNameInRedThread_OldDescription_PairingRemoved()
    {
        var id = await LeagueLinkGameAsync(anon: true, Summary("Der rote Faden in Runde 2 ist der Läufer von Anna. Er stand lange passiv."));
        var page = await Meta().ResolvePageAsync($"/s/LINK1/club-games/{id}/summary", Base);
        Assert.Equal("1-0 · Partie auf RookHub nachspielen", page!.Description);

        _db.ClubGameSummaries.Single().Markdown = Summary("Der rote Faden am Brett 1 ist der Läufer. Er stand lange passiv.");
        await _db.SaveChangesAsync();
        var cleaned = (await Meta().ResolvePageAsync($"/s/LINK1/club-games/{id}/summary", Base))!.Description;
        Assert.StartsWith("Der rote Faden am", cleaned);
        Assert.DoesNotContain("Brett", cleaned);
    }

    [Fact]
    public async Task AnonSummaryLink_RedThreadIsTheDescription()
    {
        var saved = new SavedGame { UserId = 10, Source = "Landesliga", Pgn = Pgn, White = RealName, Black = "Brunner, Bert", Result = "1-0",
            ShareToken = "normalTokenAAAAAAAAAA", AnonShareToken = "anonTokenAAAAAAAAAAAA" };
        _db.SavedGames.Add(saved);
        await _db.SaveChangesAsync();
        _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = saved.Id, UserId = 10, Status = ClubGameSummaryStatus.Done, Markdown = Summary(Thread), DoneAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        Assert.Equal(ThreadPlain, (await Meta().ResolvePageAsync("/ga/anonTokenAAAAAAAAAAAA/summary", Base))!.Description);
        Assert.Equal(ThreadPlain, (await Meta().ResolvePageAsync("/g/normalTokenAAAAAAAAAA/summary", Base))!.Description);
        // Partieseite /g/ ohne „Kurz erzählt“: ebenfalls der rote Faden; mit Nacherzählung bleibt die.
        Assert.Equal(ThreadPlain, (await Meta().ResolvePageAsync("/g/normalTokenAAAAAAAAAA", Base))!.Description);
        _db.GameRecaps.Add(new GameRecap { SavedGameId = saved.Id, Language = "de", Text = "Kurz erzählt: Weiß gewann spanisch.", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        Assert.Equal("Kurz erzählt: Weiß gewann spanisch.", (await Meta().ResolvePageAsync("/g/normalTokenAAAAAAAAAA", Base))!.Description);
        Assert.Equal(ThreadPlain, (await Meta().ResolvePageAsync("/g/normalTokenAAAAAAAAAA/summary", Base))!.Description);

        // Anonymer Link mit Klarnamen im roten Faden: AnonGameShares ersetzt/sperrt — kein Name in der Vorschau.
        _db.OwnGameSummaries.Single().Markdown = Summary("Der rote Faden ist der Läufer von Ackermann. Er stand lange passiv.");
        await _db.SaveChangesAsync();
        var anon = await Meta().ResolvePageAsync("/ga/anonTokenAAAAAAAAAAAA/summary", Base);
        Assert.DoesNotContain("Ackermann", anon!.Description);
    }

    [Fact]
    public void Description_LongRedThread_ShortenedToUsualLength()
    {
        var thread = SummaryRedThread.Extract(Summary("Der rote Faden ist die e-Linie. " + string.Join(" ", Enumerable.Repeat("Der Vorstoß des e-Bauern schuf das Ziel für den weißen f-Hebel.", 8))));
        var d = OgMetaService.RedThreadDescription(thread)!;
        Assert.True(d.Length <= OgMetaService.DescriptionMax);
        Assert.EndsWith("…", d);
        Assert.Null(OgMetaService.RedThreadDescription(null));
    }

    [Theory]
    [InlineData("/cg/abc/summary", true)]
    [InlineData("/s/a/club-games/5/summary?x=1", true)]
    [InlineData("/g/abc", false)]
    [InlineData("/g/abc?summary=1", false)]
    public void IsSummaryPath(string path, bool expected) => Assert.Equal(expected, OgMetaService.IsSummaryPath(path));
}
