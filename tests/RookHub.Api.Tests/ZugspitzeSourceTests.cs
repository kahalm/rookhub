using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Schachkreis Zugspitze als dritte Liga-Quelle: Leser gegen synthetische Seiten in der Struktur der echten WordPress-Seite
/// (<c>Fixtures/Zugspitze</c>, erfundene Vereine und Namen), Tnr-Schlüssel, Import-Rundlauf, FIDE-IDs aus dem Ligamanager,
/// Aktualisieren und die Region Bayern im Rechenkern.
/// </summary>
public class ZugspitzeSourceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private static string Html(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Zugspitze", name), Encoding.UTF8);
    private static readonly ZugspitzeSource.LeagueRef Ref = new(7, 2026);
    /// <summary>Tnr der Test-Liga: A-Klasse (Liga-Id 7), Saison 2026/27.</summary>
    private const int T = ZugspitzeSource.TnrOffset + 2026 * 1000 + 7;

    // ── Adresse, Nummer, Stufe ──────────────────────────────────────────────────────────────────

    [Fact]
    public void LeagueRef_ParsesUrlsStoredPathsAndParts()
    {
        Assert.Equal(new ZugspitzeSource.LeagueRef(1, 2026), ZugspitzeSource.LeagueRef.Parse("https://schachkreis-zugspitze.de/ergebnisse/?Saison=2026&Liga=1"));
        Assert.Equal(new ZugspitzeSource.LeagueRef(1, 2026), ZugspitzeSource.LeagueRef.Parse("https://schachkreis-zugspitze.de/ergebnisse/?Saison=2026&Liga=1&Runde=3#p2"));
        Assert.Equal(new ZugspitzeSource.LeagueRef(8, null), ZugspitzeSource.LeagueRef.Parse("https://schachkreis-zugspitze.de/ergebnisse/?Liga=8"));
        Assert.Equal(new ZugspitzeSource.LeagueRef(2, null), ZugspitzeSource.LeagueRef.Parse("https://www.schachkreis-zugspitze.de/ligadaten/?Liga=2"));
        var r = ZugspitzeSource.LeagueRef.Parse("zugspitze/2026-27/1")!;
        Assert.Equal(new ZugspitzeSource.LeagueRef(1, 2026), r);
        Assert.Equal(("zugspitze/2026-27/1", "2026/27", "https://schachkreis-zugspitze.de/ergebnisse/?Saison=2026&Liga=1"), (r.Path, r.SeasonLabel, r.Url));
        Assert.Equal("zugspitze/1999-00/3", new ZugspitzeSource.LeagueRef(3, 1999).Path);                  // Jahrhundertwechsel
        Assert.Equal(new ZugspitzeSource.LeagueRef(3, 1999), ZugspitzeSource.LeagueRef.Parse("zugspitze/1999-00/3"));
        Assert.Equal(new ZugspitzeSource.LeagueRef(5, 2026), ZugspitzeSource.LeagueRef.Of(5, "2026/27"));
        Assert.Equal(new ZugspitzeSource.LeagueRef(5, 2026), ZugspitzeSource.LeagueRef.Of(5, "2026-2027"));
        Assert.Equal(new ZugspitzeSource.LeagueRef(5, 2026), ZugspitzeSource.LeagueRef.Of(5, "2026"));
        Assert.Equal(new ZugspitzeSource.LeagueRef(5, null), ZugspitzeSource.LeagueRef.Of(5, null));
        // Nur der Schachkreis, nur gültige Teile
        Assert.Null(ZugspitzeSource.LeagueRef.Parse("https://evil.example/ergebnisse/?Saison=2026&Liga=1"));
        Assert.Null(ZugspitzeSource.LeagueRef.Parse("https://schachkreis-zugspitze.de/ergebnisse/"));        // ohne Liga
        Assert.Null(ZugspitzeSource.LeagueRef.Parse("zugspitze/2026-28/1"));                                   // Saison passt nicht
        Assert.Null(ZugspitzeSource.LeagueRef.Of(5, "2026/28"));
        Assert.Null(ZugspitzeSource.LeagueRef.Of(0, "2026"));
        Assert.Null(ZugspitzeSource.LeagueRef.Of(1000, "2026"));
        Assert.Null(ZugspitzeSource.LeagueRef.Of(1, "1989"));
        Assert.Null(ZugspitzeSource.LeagueRef.Of(1, "2200"));
    }

    [Fact]
    public void Tnr_KeysSeasonAndLeagueAndStaysClearOfTheOtherSources()
    {
        Assert.Equal(912_026_001, ZugspitzeSource.TnrOf(2026, 1));
        Assert.Equal(912_025_001, ZugspitzeSource.TnrOf(2025, 1));                         // dieselbe Liga-Id, andere Saison
        Assert.Equal((2026, 1), ZugspitzeSource.RefOf(912_026_001));
        Assert.Equal((1990, 1), ZugspitzeSource.RefOf(ZugspitzeSource.TnrOf(1990, 1)));
        Assert.Equal((2199, 999), ZugspitzeSource.RefOf(ZugspitzeSource.TnrOf(2199, 999)));
        Assert.Equal(912_199_999, ZugspitzeSource.TnrOf(ZugspitzeSource.MaxSeason, ZugspitzeSource.MaxLigaId));   // weit unter int.MaxValue
        Assert.Throws<ArgumentOutOfRangeException>(() => ZugspitzeSource.TnrOf(2026, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZugspitzeSource.TnrOf(2026, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZugspitzeSource.TnrOf(1989, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZugspitzeSource.TnrOf(2200, 1));
        // Bereiche schneiden sich nicht: chess-results < Ligamanager (bis 909 999 999) < Zugspitze
        Assert.False(ZugspitzeSource.IsZugspitzeTnr(1206271));
        Assert.False(ZugspitzeSource.IsZugspitzeTnr(LigamanagerSource.TnrOf(LigamanagerSource.MaxLigamanagerId)));
        Assert.False(LigamanagerSource.IsLigamanagerTnr(912_026_001));
        Assert.Null(LigamanagerSource.LigamanagerIdOf(912_026_001));
        Assert.False(ZugspitzeSource.IsZugspitzeTnr(ZugspitzeSource.TnrOffset + 2026 * 1000));          // Liga-Id 0 gibt es nicht
        Assert.Null(ZugspitzeSource.RefOf(int.MaxValue));
    }

    [Theory]
    [InlineData("Zugspitzliga", 5)]
    [InlineData("Kreisklasse", 6)]
    [InlineData("A-Klasse", 7)]
    [InlineData("B-Klasse", 8)]
    [InlineData("C-Klasse, Vorrunde Nord", 9)]
    [InlineData("C-Klasse, Endrunde A", 9)]
    [InlineData("Senioren-Kreisliga", null)]
    [InlineData("U16-Kreisliga", null)]
    [InlineData("U12-Kreisliga", null)]
    [InlineData("4er-Pokal", null)]
    [InlineData("Landesliga Süd", null)]
    [InlineData("Bezirksliga Oberbayern", null)]
    public void LevelOf_TheAdultLeaguesOfTheDistrictOnly(string title, int? level)
    {
        Assert.Equal(level, ZugspitzeSource.LevelOf(title));
        if (level is { } l) Assert.True(l <= LeagueLevels.Max);
    }

    // ── Seiten lesen ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseResults_ReadsTitleSeasonRoundsAndMatches()
    {
        var r = ZugspitzeSource.ParseResults(Html("ergebnisse.html"));
        Assert.Equal(("A-Klasse", 2026, 2026), (r.Title, r.Season!.Value, r.NewestSeason!.Value));
        Assert.Equal(new[] { new ZugspitzeSource.RoundInfo(1, "27.09.2026", "10:00 Uhr"), new ZugspitzeSource.RoundInfo(2, "18.10.2026", "10:00 Uhr") }, r.Rounds);
        Assert.Equal(6, r.Matches.Count);
        Assert.Equal(("SC Anderswo", "spielfrei", (double?)null), (r.Matches[0].Home, r.Matches[0].Away, r.Matches[0].HomePts));
        var m2 = r.Matches[1];
        Assert.Equal((1, 2, "SK Musterstadt II", "SC Gröfing", 2.5, 1.5, false), (m2.Round, m2.MatchNo, m2.Home, m2.Away, m2.HomePts!.Value, m2.AwayPts!.Value, m2.Forfeit));
        Assert.Empty(m2.Boards);                                                           // Bretter nur auf der Runden-Seite
        Assert.Equal((3.0, 0.0, true), (r.Matches[2].HomePts!.Value, r.Matches[2].AwayPts!.Value, r.Matches[2].Forfeit));   // „3:0 kl *"
        // „spielfrei - SC Gröfing" wird zu „SC Gröfing - spielfrei" (Freilos nur als Gast)
        Assert.Equal((2, "SC Gröfing", "spielfrei"), (r.Matches[3].Round, r.Matches[3].Home, r.Matches[3].Away));
        Assert.Null(r.Matches[4].HomePts);                                                 // noch nicht gespielt
    }

    [Fact]
    public void ParseResults_RoundPageReadsBoardsColorsAndResults()
    {
        var r = ZugspitzeSource.ParseResults(Html("runde1.html"));
        Assert.Single(r.Rounds);
        var b = r.Matches.Single(m => m.MatchNo == 2).Boards;
        Assert.Equal(4, b.Count);
        Assert.Equal(new ZugspitzeSource.BoardRow(1, 1, "Muster, Max", "IM", 2201, false, 1, "Gröfinger, Gerd", null, 2105, "1 - 0"), b[0]);
        Assert.Equal(("Pappenheim, Rainer Dr.", true, "½ - ½"), (b[1].HomeName, b[1].HomeWhite!.Value, b[1].Result));
        Assert.Equal((22, "Ersatz, Erwin", "0 - 1"), (b[2].HomeNr!.Value, b[2].HomeName, b[2].Result));
        var kl = r.Matches.Single(m => m.MatchNo == 3).Boards;
        Assert.Equal(new[] { "+ - -", "+ - -", "+ - -", "- - -" }, kl.Select(x => x.Result));
    }

    [Fact]
    public void ParseLigaData_ReadsRostersTitlesAndVenueWithoutContacts()
    {
        var d = ZugspitzeSource.ParseLigaData(Html("ligadaten.html"));
        Assert.Equal(2026, d.Season);
        Assert.Equal(19, d.Roster.Count);
        Assert.Equal(new ZugspitzeSource.RosterEntry("SK Musterstadt II", 1, "Muster, Max", "IM", 2201), d.Roster[0]);
        Assert.Equal(new ZugspitzeSource.RosterEntry("SK Musterstadt II", 2, "Pappenheim, Rainer Dr.", null, 1950), d.Roster[1]);
        Assert.Equal("SC Gröfing", d.Roster[4].Team);
        Assert.Equal("Rathaus Musterstadt, Hauptstraße 1, 82000 Musterstadt", d.Venues["SK Musterstadt II"]);   // ohne Telefon
        Assert.DoesNotContain(d.Venues.Values, v => v.Contains("Tel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1:0", "1 - 0")]
    [InlineData("0:1", "0 - 1")]
    [InlineData("½", "½ - ½")]
    [InlineData("&#189;", "½ - ½")]
    [InlineData("+:-", "+ - -")]
    [InlineData("-:+", "- - +")]
    [InlineData("0:0kl", "- - -")]
    [InlineData("", "")]
    public void NormalizeResult_HomeViewLikeChessResults(string raw, string expected) =>
        Assert.Equal(expected, ZugspitzeSource.NormalizeResult(raw));

    private static ZugspitzeSource.Parsed Parse(ZugspitzeSource.LigaData? ligaData = null)
    {
        var round = ZugspitzeSource.ParseResults(Html("runde1.html"));
        var boards = round.Matches.Where(m => m.Boards.Count > 0).ToDictionary(m => (m.Round, m.MatchNo), m => m.Boards);
        return ZugspitzeSource.Build(Ref, ZugspitzeSource.ParseResults(Html("ergebnisse.html")), boards,
            ligaData ?? ZugspitzeSource.ParseLigaData(Html("ligadaten.html")));
    }

    [Fact]
    public void Build_LinksBoardsToTheRosterAndCountsEverything()
    {
        var p = Parse();
        var c = p.Counts;
        Assert.Equal((2, 1, 6, 8, 8, 1, 19, "ligadaten"), (c.Rounds, c.RoundsPlayed, c.Matches, c.BoardGames, c.BoardGamesPlayed,
            c.BoardPlayersUnmatched, c.Players, c.RosterFrom));
        Assert.Equal((8, 0, 4), (c.ColorFromPage, c.ColorRuleMismatches, c.Boards!.Value));   // Feldfarbe = bayerische Regel
        var g = p.Pages.Games;
        Assert.Equal("s", g.Single(x => x.MatchNo == 2 && x.Board == 1).HomeColor);           // Heim schwarz an Brett 1
        Assert.Equal("w", g.Single(x => x.MatchNo == 2 && x.Board == 2).HomeColor);
        var kl = g.Single(x => x.MatchNo == 3 && x.Board == 4);
        Assert.Equal((2, 0.0, 0.0), (kl.Forfeit, kl.HomeScore!.Value, kl.AwayScore!.Value));
        Assert.Equal("Rathaus Musterstadt, Hauptstraße 1, 82000 Musterstadt", p.Pages.Matches.Single(m => m.Round == 1 && m.MatchNo == 2).Venue);
        Assert.Null(p.Pages.Matches.Single(m => m.Round == 1 && m.MatchNo == 1).Venue);         // spielfrei
        var t = p.Tournament;
        Assert.Equal((T, "A-Klasse 2026/27", "2026/27", 7, "A-Klasse", "", "Liga", "zugspitze", "zugspitze/2026-27/7"),
            (t.Tnr, t.Name, t.Season, t.Level, t.League, t.Grp, t.Stage, t.Source!, t.SourceRef!));
        Assert.Equal(("27.09.2026", "18.10.2026", 2), (t.Start!, t.End!, t.Rounds!.Value));
        var r1 = p.Pages.Roster.First();
        Assert.Equal(("Muster, Max", "IM", (int?)null, 2201, 1), (r1.Name, r1.Title, r1.EloI, r1.EloN!.Value, r1.RosterBoard!.Value));   // DWZ = EloN
        var stat = p.Pages.Stats.Single(s => s.Name == "Pappenheim, Rainer Dr.");
        Assert.Equal((0.5, 1), (stat.Points!.Value, stat.Games!.Value));
    }

    [Fact]
    public void Build_OlderSeasonTakesTheRosterFromTheBoards()
    {
        // Ligadaten einer anderen Saison zählen nicht (die Seite zeigt immer die laufende)
        var other = ZugspitzeSource.ParseLigaData(Html("ligadaten.html")) with { Season = 2027 };
        var p = Parse(other);
        Assert.Equal(("boards", 0), (p.Counts.RosterFrom, p.Counts.BoardPlayersUnmatched));
        Assert.Equal(16, p.Counts.Players);                                                    // wer gespielt hat
        Assert.Contains(p.Pages.Roster, r => r.Name == "Ersatz, Erwin" && r.RosterBoard == 22 && r.EloN == 1700);
        Assert.All(p.Pages.Matches, m => Assert.Null(m.Venue));
    }

    [Fact]
    public void Build_CDivisionGroupSplitsLeagueAndGroup()
    {
        var html = Html("ergebnisse.html").Replace("Ergebnisse A-Klasse 2026/27", "Ergebnisse C-Klasse, Vorrunde Nord 2026/27");
        var p = ZugspitzeSource.Build(Ref, ZugspitzeSource.ParseResults(html), new Dictionary<(int, int), List<ZugspitzeSource.BoardRow>>(), null);
        Assert.Equal(("C-Klasse", "Vorrunde Nord", 9), (p.Tournament.League, p.Tournament.Grp, p.Tournament.Level));
        Assert.Null(p.Tournament.Boards);
        Assert.Empty(p.Pages.Games);
    }

    // ── Import ──────────────────────────────────────────────────────────────────────────────────

    private sealed class Factory(Func<string, HttpRequestMessage, HttpResponseMessage> answer) : IHttpClientFactory
    {
        public List<string> Calls { get; } = new();
        public HttpClient CreateClient(string name) =>
            new(new Handler(r => { Calls.Add($"{name} {r.RequestUri!.PathAndQuery}"); return answer(name, r); }))
            {
                BaseAddress = new Uri(name == ZugspitzeSource.ClientName ? ZugspitzeSource.SiteUrl + "/"
                    : name == LigamanagerSource.ClientName ? LigamanagerSource.SiteUrl + "/" : "http://crawler/"),
            };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(f(request));
    }

    private static HttpResponseMessage Ok(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    /// <summary>Der Schachkreis mit den Fixtures; <paramref name="overview"/> ersetzt die Übersicht.</summary>
    private static HttpResponseMessage Site(HttpRequestMessage r, string? overview = null)
    {
        var q = r.RequestUri!.PathAndQuery;
        if (q.StartsWith("/ergebnisse/", StringComparison.Ordinal))
            return Ok(q.Contains("Runde=1") ? Html("runde1.html") : q.Contains("Runde=") ? Html("ergebnisse.html") : overview ?? Html("ergebnisse.html"));
        if (q.StartsWith("/ligadaten/", StringComparison.Ordinal)) return Ok(Html("ligadaten.html"));
        return new(HttpStatusCode.NotFound) { Content = new StringContent("<html>404</html>", Encoding.UTF8, "text/html") };
    }

    private LeagueService League() => new(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);

    private ZugspitzeSource Source(IHttpClientFactory http) =>
        new(_db, http, League(), NullLogger<ZugspitzeSource>.Instance, () => Now) { Pause = TimeSpan.Zero };

    [Fact]
    public async Task Import_FillsTheTablesAndComputesTheView()
    {
        var http = new Factory((_, r) => Site(r));
        var res = await Source(http).ImportAsync(new ZugspitzeSource.LeagueRef(7, null), dryRun: false, default);

        Assert.False(res.DryRun);
        Assert.Equal((T, "2026/27", 7), (res.Tnr, res.Season, res.Level));
        Assert.Equal(1, res.Views);
        // Übersicht ohne Saison (= laufende), dann NUR die gespielte Runde 1, dann die Ligadaten
        Assert.Equal(new[] { "Zugspitze /ergebnisse/?Liga=7", "Zugspitze /ergebnisse/?Saison=2026&Liga=7&Runde=1", "Zugspitze /ligadaten/?Liga=7" }, http.Calls);
        var t = _db.LeagueTournaments.Single();
        Assert.Equal(("zugspitze", "zugspitze/2026-27/7", 4, "Liga", Now), (t.Source!, t.SourceRef!, t.Boards!.Value, t.Stage, t.UpdatedAt));
        Assert.Equal(2, _db.LeagueRounds.Count());
        Assert.Equal(new DateOnly(2026, 9, 27), _db.LeagueRounds.Single(r => r.Round == 1).Date);
        Assert.Equal(6, _db.LeagueMatches.Count());
        Assert.Equal(19, _db.LeaguePlayers.Count());
        var games = _db.LeagueGames.Where(g => g.MatchNo == 2).OrderBy(g => g.Board).ToList();
        Assert.Equal(4, games.Count);
        // Spieler ↔ Brett über (Team, NameKey): Meldebrett und DWZ als Elo
        Assert.Equal((1, 2201, 1, 2105), (games[0].HomeRb!.Value, games[0].HomeElo!.Value, games[0].AwayRb!.Value, games[0].AwayElo!.Value));
        Assert.Equal(("Ersatz, Erwin", (int?)null), (games[2].HomePlayer!, games[2].HomeRb));      // Ersatz ohne Meldeliste
        Assert.All(games, g => Assert.Null(g.HomeFide));                                            // die Quelle kennt keine FIDE-IDs
        var view = _db.LeagueViews.Single();
        Assert.Contains("\"source\":\"https://schachkreis-zugspitze.de/ergebnisse/?Saison=2026\\u0026Liga=7\"", view.Json);
        Assert.DoesNotContain("chess-results.com/tnr", view.Json);
        Assert.Contains("\"boards\":4", view.Json);
    }

    [Fact]
    public async Task Import_Twice_KeepsOneCopyOfEverything()
    {
        var src = Source(new Factory((_, r) => Site(r)));
        await src.ImportAsync(Ref, dryRun: false, default);
        await src.ImportAsync(Ref, dryRun: false, default);
        Assert.Single(_db.LeagueTournaments);
        Assert.Equal(8, _db.LeagueGames.Count());
        Assert.Equal(19, _db.LeaguePlayers.Count());
    }

    [Fact]
    public async Task Import_DryRun_WritesNothing()
    {
        var res = await Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: true, default);
        Assert.True(res.DryRun);
        Assert.Equal((8, 19), (res.Counts.BoardGames, res.Counts.Players));
        Assert.Empty(_db.LeagueTournaments);
        Assert.Empty(_db.LeagueGames);
    }

    [Fact]
    public async Task Import_OlderSeason_SkipsTheLigaDataAndGetsItsOwnNumber()
    {
        var old = Html("ergebnisse.html").Replace("Ergebnisse A-Klasse 2026/27", "Ergebnisse A-Klasse 2025/26");
        var http = new Factory((_, r) => Site(r, old));
        var res = await Source(http).ImportAsync(new ZugspitzeSource.LeagueRef(7, 2025), dryRun: false, default);
        Assert.Equal((ZugspitzeSource.TnrOf(2025, 7), "2025/26", "boards"), (res.Tnr, res.Season, res.Counts.RosterFrom));
        Assert.DoesNotContain(http.Calls, c => c.Contains("ligadaten"));
        Assert.Equal(16, _db.LeaguePlayers.Count());
    }

    [Fact]
    public async Task Import_RefusesSeniorsYouthAndCup()
    {
        var sen = Html("ergebnisse.html").Replace("Ergebnisse A-Klasse 2026/27", "Ergebnisse Senioren-Kreisliga 2026/27");
        await Assert.ThrowsAsync<ZugspitzeSource.UnsupportedException>(() =>
            Source(new Factory((_, r) => Site(r, sen))).ImportAsync(Ref, dryRun: true, default));
        Assert.Empty(_db.LeagueTournaments);
    }

    [Fact]
    public async Task Import_UnknownLeagueOrWrongSeason_IsNotFound()
    {
        await Assert.ThrowsAsync<ZugspitzeSource.NotFoundException>(() =>
            Source(new Factory((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound))).ImportAsync(Ref, dryRun: false, default));
        // Eine leere Seite (WordPress liefert 200 auch für unbekannte Ligen)
        await Assert.ThrowsAsync<ZugspitzeSource.NotFoundException>(() =>
            Source(new Factory((_, _) => Ok("<html><body>nichts</body></html>"))).ImportAsync(Ref, dryRun: false, default));
        // Der Kreis zeigt eine andere Saison als verlangt
        await Assert.ThrowsAsync<ZugspitzeSource.NotFoundException>(() =>
            Source(new Factory((_, r) => Site(r))).ImportAsync(new ZugspitzeSource.LeagueRef(7, 2019), dryRun: false, default));
    }

    [Fact]
    public async Task Import_RefusesANumberThatBelongsToAnotherSource()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = T, Name = "Fremd", Season = "2026/27", Level = 1, League = "X" });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ZugspitzeSource.ConflictException>(() =>
            Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: false, default));
        Assert.Empty(_db.LeagueGames);
    }

    [Fact]
    public async Task Import_BeforeTheFirstRound_TakesTheBoardsFromThePreviousSeason()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = ZugspitzeSource.TnrOf(2025, 7), Season = "2025/26", Level = 7,
            League = "A-Klasse", Source = ZugspitzeSource.Source, SourceRef = "zugspitze/2025-26/7" });
        _db.LeagueGames.Add(new LeagueGame { Tnr = ZugspitzeSource.TnrOf(2025, 7), Round = 1, MatchNo = 1, Board = 6, HomeTeam = "a", AwayTeam = "b" });
        await _db.SaveChangesAsync();
        var unplayed = System.Text.RegularExpressions.Regex.Replace(Html("ergebnisse.html"), @"<center>(2&#189;:1&#189;|3:0 kl \*)</td>", "<center></td>");
        var http = new Factory((_, r) => Site(r, unplayed));
        var res = await Source(http).ImportAsync(Ref, dryRun: false, default);
        Assert.Equal(0, res.Counts.BoardGames);
        Assert.DoesNotContain(http.Calls, c => c.Contains("Runde="));                  // keine gespielte Runde → keine Runden-Seite
        Assert.Equal(6, _db.LeagueTournaments.Single(t => t.Tnr == T).Boards);
    }

    [Fact]
    public async Task Import_FillsFideIdsFromTheLigamanagerRosterOfTheSameClub()
    {
        // Ligamanager: SK Musterstadt 1 meldet „Muster, Max" mit FIDE-ID; ein gleichnamiger Spieler eines ANDEREN Vereins und
        // ein Tiroler chess-results-Eintrag bleiben außen vor.
        var lm = LigamanagerSource.TnrOf(4711);
        _db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = lm, Season = "2026/27", Level = 3, Stage = "Liga", Source = LigamanagerSource.Source },
            new LeagueTournament { Tnr = 1206271, Season = "2026/27", Level = 1, Stage = "Liga" });
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = lm, Team = "SK Musterstadt 1", Name = "Muster, Max", NameKey = "muster, max", FideId = "90000001" },
            new LeaguePlayer { Tnr = lm, Team = "SC Anderswo 1", Name = "Huber, Hans", NameKey = "huber, hans", FideId = "90000009" },
            new LeaguePlayer { Tnr = 1206271, Team = "SC Gröfing", Name = "Lang, Lukas", NameKey = "lang, lukas", FideId = "1111" });
        await _db.SaveChangesAsync();

        var res = await Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: false, default);

        Assert.Equal((1, 1), (res.FideFilled, res.PlayersWithFide));
        Assert.Equal("90000001", _db.LeaguePlayers.Single(p => p.Tnr == T && p.NameKey == "muster, max").FideId);   // SK Musterstadt II ↔ 1
        Assert.Null(_db.LeaguePlayers.Single(p => p.Tnr == T && p.NameKey == "huber, hans").FideId);              // anderer Verein
        Assert.Null(_db.LeaguePlayers.Single(p => p.Tnr == T && p.NameKey == "lang, lukas").FideId);              // Tirol zählt nicht
        Assert.Equal("90000001", _db.LeagueGames.Single(g => g.Tnr == T && g.MatchNo == 2 && g.Board == 1).HomeFide);
    }

    // ── Aktualisieren ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_FetchesZugspitzeLeaguesFromTheirSource()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = T, Season = "2026/27", Level = 7, League = "A-Klasse", Stage = "Liga",
            Source = ZugspitzeSource.Source, SourceRef = Ref.Path });
        await _db.SaveChangesAsync();
        var http = new Factory((name, r) => name == ZugspitzeSource.ClientName ? Site(r) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var refresh = new LeagueRefresh(_db, League(), http, NullLogger<LeagueRefresh>.Instance, () => Now, zugspitze: Source(http));

        var msg = await refresh.RunAsync(default);

        Assert.StartsWith("1 Ligen neu geholt", msg);
        Assert.DoesNotContain(http.Calls, c => c.Contains($"api/league/{T}"));
        Assert.Contains(http.Calls, c => c == "Zugspitze /ergebnisse/?Saison=2026&Liga=7");
        Assert.Equal(8, _db.LeagueGames.Count(g => g.Tnr == T));
        Assert.Single(_db.LeagueViews);
    }

    [Fact]
    public async Task Refresh_RefusesANumberThatDoesNotMatchTheSource()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = ZugspitzeSource.TnrOf(2026, 8), Season = "2026/27", Level = 7,
            Source = ZugspitzeSource.Source, SourceRef = Ref.Path });
        await _db.SaveChangesAsync();
        var http = new Factory((_, r) => Site(r));
        var refresh = new LeagueRefresh(_db, League(), http, NullLogger<LeagueRefresh>.Instance, () => Now, zugspitze: Source(http));
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.RunAsync(default));
        Assert.Empty(http.Calls);
    }

    // ── Endpunkt ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Endpoint_ParsesTheRequestAndMapsErrors()
    {
        var ctl = new LeagueController(League(), null!, null!);
        var src = Source(new Factory((_, r) => Site(r)));
        Assert.IsType<BadRequestObjectResult>(await ctl.ZugspitzeImport(new("https://example.org/?Liga=1", null, null, null), true, src, default));
        Assert.IsType<BadRequestObjectResult>(await ctl.ZugspitzeImport(new(null, null, "2026", null), true, src, default));
        var ok = Assert.IsType<OkObjectResult>(await ctl.ZugspitzeImport(new(null, 7, "2026/27", null), true, src, default));
        Assert.True(Assert.IsType<ZugspitzeSource.ImportResult>(ok.Value).DryRun);
        Assert.IsType<OkObjectResult>(await ctl.ZugspitzeImport(new("https://schachkreis-zugspitze.de/ergebnisse/?Saison=2026&Liga=7", null, null, null), true, src, default));
        var sen = Html("ergebnisse.html").Replace("Ergebnisse A-Klasse 2026/27", "Ergebnisse U12-Kreisliga 2026/27");
        var bad = Assert.IsType<BadRequestObjectResult>(await ctl.ZugspitzeImport(new(null, 10, null, null), true,
            Source(new Factory((_, r) => Site(r, sen))), default));
        Assert.Contains("unsupportedLeague", System.Text.Json.JsonSerializer.Serialize(bad.Value));
        var missing = Source(new Factory((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.IsType<NotFoundObjectResult>(await ctl.ZugspitzeImport(new(null, 7, null, null), false, missing, default));
        var down = Source(new Factory((_, _) => throw new HttpRequestException("weg")));
        Assert.Equal(503, Assert.IsType<ObjectResult>(await ctl.ZugspitzeImport(new(null, 7, null, null), false, down, default)).StatusCode);
    }

    // ── Rechenkern: Region Bayern ───────────────────────────────────────────────────────────────

    [Fact]
    public void Club_BavarianTeamsDropArabicAndRomanNumbers()
    {
        Assert.Equal("SK Weilheim", LeagueNames.Club("SK Weilheim 1", LigamanagerSource.Source));
        Assert.Equal("SK Weilheim", LeagueNames.Club("SK Weilheim II", ZugspitzeSource.Source));
        Assert.Equal("SF Windach", LeagueNames.Club("SF Windach IV", ZugspitzeSource.Source));
        Assert.Equal("SK Weilheim II S", LeagueNames.Club("SK Weilheim II S", ZugspitzeSource.Source));   // Senioren bleiben getrennt
        Assert.Equal("Bad Reichenhall", LeagueNames.Club("Bad Reichenhall", ZugspitzeSource.Source));    // keine Tiroler Regel
        Assert.Equal("Rum/Hall/Mils", LeagueNames.Club("Hall 2"));                                         // Tirol unverändert
    }

    [Fact]
    public void Regions_MapSourcesInOnePlace()
    {
        Assert.Equal(LeagueRegions.Tirol, LeagueRegions.Of(null));
        Assert.Equal(LeagueRegions.Bayern, LeagueRegions.Of(LigamanagerSource.Source));
        Assert.Equal(LeagueRegions.Bayern, LeagueRegions.Of(ZugspitzeSource.Source));
        Assert.Equal(new string?[] { null }, LeagueRegions.SourcesOf(LeagueRegions.Tirol));
        Assert.Equal(new string?[] { LigamanagerSource.Source, ZugspitzeSource.Source }, LeagueRegions.SourcesOf(LeagueRegions.Bayern));
        Assert.True(LeagueRegions.Valid("bayern"));
        Assert.False(LeagueRegions.Valid("ligamanager"));
        Assert.Equal("ZL", LeagueLevels.Short(ZugspitzeSource.Source, 5));
        Assert.Equal("LL", LeagueLevels.Short(ZugspitzeSource.Source, 3));                   // höhere Stufen unter Ligamanager-Namen
        Assert.Equal("C-Kl", LeagueLevels.Short(LigamanagerSource.Source, 9));
        Assert.Equal(Enumerable.Range(1, 9), LeagueLevels.Of(ZugspitzeSource.Source));
    }

    [Fact]
    public void Features_SeeBothBavarianSourcesForTheSameClub()
    {
        // SK Weilheim 1 (Ligamanager, Landesliga) und SK Weilheim II (Zugspitze, Zugspitzliga) spielen am SELBEN Tag; Spieler P
        // (FIDE 9) steht in beiden Meldelisten und spielte in der Vorsaison zweimal für die Zweite.
        var day = new DateOnly(2026, 10, 11);
        var lm = LigamanagerSource.TnrOf(2573);
        var zg = ZugspitzeSource.TnrOf(2026, 1);
        var zgPrev = ZugspitzeSource.TnrOf(2025, 1);
        var ts = new[]
        {
            new LeagueTournament { Tnr = lm, Season = "2026/27", Level = 3, Stage = "Liga", Source = LigamanagerSource.Source, Boards = 8 },
            new LeagueTournament { Tnr = zg, Season = "2026/27", Level = 5, Stage = "Liga", Source = ZugspitzeSource.Source, Boards = 6 },
            new LeagueTournament { Tnr = zgPrev, Season = "2025/26", Level = 5, Stage = "Liga", Source = ZugspitzeSource.Source },
        };
        var rounds = new[] { new LeagueRound { Tnr = lm, Round = 1, Date = day }, new LeagueRound { Tnr = zg, Round = 1, Date = day } };
        var matches = new[]
        {
            new LeagueMatch { Id = 1, Tnr = lm, Round = 1, Home = "SK Weilheim 1", Away = "SC Anders 1" },
            new LeagueMatch { Id = 2, Tnr = zg, Round = 1, Home = "SK Weilheim II", Away = "SF Fremd" },
            new LeagueMatch { Id = 3, Tnr = zgPrev, Round = 1, Home = "SK Weilheim II", Away = "SF Fremd" },
            new LeagueMatch { Id = 4, Tnr = zgPrev, Round = 2, Home = "SF Fremd", Away = "SK Weilheim II" },
        };
        var games = new[]
        {
            new LeagueGame { Id = 1, Tnr = zgPrev, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "SK Weilheim II", AwayTeam = "SF Fremd",
                HomePlayer = "P, P", HomeFide = "9", AwayPlayer = "Q, Q", HomeScore = 1, AwayScore = 0 },
            new LeagueGame { Id = 2, Tnr = zgPrev, Round = 2, MatchNo = 1, Board = 1, HomeTeam = "SF Fremd", AwayTeam = "SK Weilheim II",
                HomePlayer = "Q, Q", AwayPlayer = "P, P", AwayFide = "9", HomeScore = 0, AwayScore = 1 },
        };
        var players = new[]
        {
            new LeaguePlayer { Id = 1, Tnr = lm, Team = "SK Weilheim 1", Name = "P, P", NameKey = "p, p", FideId = "9", RosterBoard = 8 },
            new LeaguePlayer { Id = 2, Tnr = zg, Team = "SK Weilheim II", Name = "P, P", NameKey = "p, p", FideId = "9", RosterBoard = 1 },
        };
        var w = new LeagueWorld(ts, rounds, matches, games, players);
        Assert.Equal(new[] { (lm, "SK Weilheim 1"), (zg, "SK Weilheim II") }.OrderBy(x => x.Item1),
            w.ClubTeamsOf(lm, "SK Weilheim 1")!.OrderBy(x => x.Item1));                        // EIN Verein über beide Quellen
        Assert.Equal(2, w.MptOf(ZugspitzeSource.Source, "2025/26", 5));
        Assert.Equal(2, w.MptOf(LigamanagerSource.Source, "2025/26", 5));                     // derselbe Nenner in der Region
        var row = Assert.Single(LeagueFeatures.RowsFor(w, lm, "SK Weilheim 1", 1));
        Assert.Equal(1.0, row.QLower);                                                        // Einsätze der Zweiten aus dem Schachkreis
        Assert.Equal(0, row.NewEver);
        Assert.True(row.ConflictLo > 0);                                                      // selber Tag: die Zweite spielt auch
    }
}
