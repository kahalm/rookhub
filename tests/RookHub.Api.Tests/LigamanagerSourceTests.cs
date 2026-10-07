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
/// SBV-Ligamanager (Bayern) als zweite Liga-Quelle: Leser gegen synthetische Seiten in der Struktur des echten Ligamanagers
/// (<c>Fixtures/Ligamanager</c>, erfundene Namen), Import-Rundlauf in die LeagueHub-Tabellen und Aktualisieren.
/// </summary>
public class LigamanagerSourceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ligamanager", name);
    private static string Html(string name) => File.ReadAllText(Fixture(name), Encoding.UTF8);
    private static readonly LigamanagerSource.LeagueRef Ref = LigamanagerSource.LeagueRef.Parse("bsb/2026-2027/landesliga-testgau-4711")!;
    /// <summary>Tnr der Test-Liga (Ligamanager-Id 4711) bzw. ihrer Vorsaison (4600) — versetzt um <see cref="LigamanagerSource.TnrOffset"/>.</summary>
    private const int T = LigamanagerSource.TnrOffset + 4711, TPrev = LigamanagerSource.TnrOffset + 4600;

    // ── Adresse + Stufe ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LeagueRef_ParsesUrlPathAndParts()
    {
        var a = LigamanagerSource.LeagueRef.Parse("https://ligamanager.schachbund-bayern.de/bsb/2026-2027/landesliga-sued-2573/spielplan");
        Assert.Equal(new LigamanagerSource.LeagueRef("bsb", "2026-2027", "landesliga-sued", 2573), a);
        Assert.Equal("bsb/2026-2027/landesliga-sued-2573", a!.Path);
        Assert.Equal("2026/27", a.SeasonLabel);
        Assert.Equal(a, LigamanagerSource.LeagueRef.Parse("bsb/2026-2027/landesliga-sued-2573"));
        Assert.Equal(a, LigamanagerSource.LeagueRef.Of("bsb", "2026/27", "landesliga-sued-2573"));
        Assert.Equal(a, LigamanagerSource.LeagueRef.Of("BSB", "2026-2027", "Landesliga-Sued-2573"));
        Assert.Equal(new LigamanagerSource.LeagueRef("innchiem", "2026-2027", "b-klasse-nord", 2613),
            LigamanagerSource.LeagueRef.Parse("/innchiem/2026-2027/b-klasse-nord-2613"));
        // Nur der Ligamanager — kein Abruf fremder Adressen.
        Assert.Null(LigamanagerSource.LeagueRef.Parse("https://evil.example/bsb/2026-2027/landesliga-sued-2573"));
        Assert.Null(LigamanagerSource.LeagueRef.Parse("bsb/2026-2028/landesliga-sued-2573"));     // keine Saison
        Assert.Null(LigamanagerSource.LeagueRef.Of("bsb", "2026/27", "landesliga-sued"));            // ohne Id
        Assert.Null(LigamanagerSource.LeagueRef.Of("../x", "2026/27", "landesliga-sued-2573"));
    }

    [Theory]
    [InlineData("oberliga", 1)]
    [InlineData("regionalliga-sued-ost", 2)]
    [InlineData("landesliga-sued", 3)]
    [InlineData("u20-landesliga-sued", 3)]
    [InlineData("bezirksliga", 4)]
    [InlineData("bezirksoberliga", 4)]
    [InlineData("schwabenliga-1", 4)]
    [InlineData("kreisliga-2", 5)]
    [InlineData("kreisklasse-a", 6)]
    [InlineData("a-klasse", 6)]
    [InlineData("b-klasse-nord", 7)]
    [InlineData("c-klasse", 8)]
    public void LevelOf_MapsTheBavarianLeagues(string slug, int level)
    {
        Assert.Equal(level, LigamanagerSource.LevelOf(slug));
        Assert.True(level <= LeagueLevels.Max);
    }

    // ── Seiten lesen ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSchedule_ReadsRoundsMatchesAndBoards()
    {
        var s = LigamanagerSource.ParseSchedule(Html("spielplan.html"));
        Assert.Equal("Landesliga Testgau", s.Title);
        Assert.Equal(new[] { new LigamanagerSource.RoundInfo(1, "11.10.2026", "10:00 Uhr"), new LigamanagerSource.RoundInfo(2, "25.10.2026", null) },
            s.Rounds);
        Assert.Equal(4, s.Matches.Count);
        var m1 = s.Matches[0];
        Assert.Equal((1, 1, "SK Musterstadt 1", "SC Gräfing 1", 2.5, 1.5), (m1.Round, m1.MatchNo, m1.Home, m1.Away, m1.HomePts!.Value, m1.AwayPts!.Value));
        Assert.Equal(4, m1.Boards.Count);
        Assert.Equal(new LigamanagerSource.BoardRow(1, 1, "Muster, Max", null, 1, "Örtel, Ömer", "IM", "½ - ½"), m1.Boards[0]);
        Assert.Equal("Beispiel, Bernd Dr.", m1.Boards[1].HomeName);                       // „, Dr." → „ Dr."
        Assert.Equal("FM", m1.Boards[1].HomeTitle);
        Assert.Equal("+ - -", m1.Boards[2].Result);
        var m3 = s.Matches[2];
        Assert.Equal((2, 1, "SC Gräfing 1", "TSV Beispiel 2"), (m3.Round, m3.MatchNo, m3.Home, m3.Away));
        Assert.Null(m3.HomePts);
        Assert.Empty(m3.Boards);
        Assert.Equal("Pfarrheim, Kirchplatz 2, 99999 Gräfing", m3.Venue);
    }

    [Fact]
    public void ParseRoster_ReadsNumbersTitlesRatingsAndFideIds()
    {
        var r = LigamanagerSource.ParseRoster(Html("mannschaften.html"));
        Assert.Equal(20, r.Count);
        Assert.Equal(new LigamanagerSource.RosterEntry("SK Musterstadt 1", 1, "Muster, Max", null, 2101, 2150, "90000001"), r[0]);
        Assert.Equal(new LigamanagerSource.RosterEntry("SK Musterstadt 1", 2, "Beispiel, Bernd Dr.", "FM", 2050, 2080, "90000002"), r[1]);
        Assert.Equal(new LigamanagerSource.RosterEntry("SK Musterstadt 1", 3, "Probe, Paula", null, 1990, null, null), r[2]);   // ELO „-"
        Assert.Equal("SC Gräfing 1", r[5].Team);
        Assert.Equal("beispiel, bernd", LeagueNames.NameKey(r[1].Name));
    }

    [Fact]
    public void DecodePgn_Windows1252AndUtf8KeepUmlauts()
    {
        var bytes = File.ReadAllBytes(Fixture("alle.pgn"));
        Assert.Contains("SC Gräfing 1", LigamanagerSource.DecodePgn(bytes, "windows-1252"));
        Assert.Contains("SC Gräfing 1", LigamanagerSource.DecodePgn(bytes));                 // ohne Angabe: kein UTF-8 → 1252
        Assert.Contains("Örtel", LigamanagerSource.DecodePgn(Encoding.UTF8.GetBytes("[White \"Örtel, Ömer\"]")));
        var games = LigamanagerSource.ParsePgn(LigamanagerSource.DecodePgn(bytes, "windows-1252"));
        Assert.Equal(8, games.Count);
        Assert.Equal((1, 2, "Beispiel, Bernd Dr.", "SK Musterstadt 1", "0-1"), (games[1].Round, games[1].Board, games[1].White, games[1].WhiteTeam, games[1].Result));
        Assert.Equal("2026.10.11", games[0].Date);
        Assert.Equal(10, games[0].Sans.Count);
        Assert.Equal("--+", games[2].Result);                                                  // kampflos …
        Assert.Equal(7, LigamanagerSource.ProfilePgn(games, (_, _) => null).Games);             // … nicht in die Karten
    }

    [Fact]
    public void Build_TakesColorsFromThePgnAndOtherwiseTheBavarianRule()
    {
        var p = Parse();
        var c = p.Counts;
        Assert.Equal((2, 4, 8, 8, 0, 20, 10), (c.Rounds, c.Matches, c.BoardGames, c.BoardGamesPlayed, c.BoardPlayersUnmatched, c.Players, c.PlayersWithFide));
        Assert.Equal((8, 7, 0, 8, 1, 4), (c.PgnGames, c.PgnGamesWithMoves, c.PgnGamesUnmatched, c.ColorFromPgn, c.ColorRuleMismatches, c.Boards!.Value));
        var g = p.Pages.Games;
        Assert.Equal("s", g.Single(x => x.MatchNo == 1 && x.Board == 1).HomeColor);   // Heim schwarz an Brett 1
        Assert.Equal("w", g.Single(x => x.MatchNo == 1 && x.Board == 2).HomeColor);
        Assert.Equal("w", g.Single(x => x.MatchNo == 2 && x.Board == 1).HomeColor);   // laut PGN gegen die Regel
        var forfeit = g.Single(x => x.MatchNo == 1 && x.Board == 3);
        Assert.Equal((1, 1.0, 0.0), (forfeit.Forfeit, forfeit.HomeScore!.Value, forfeit.AwayScore!.Value));
        Assert.Equal("Beispiel, Bernd Dr.", g.Single(x => x.MatchNo == 1 && x.Board == 2).HomePlayer);   // Name aus der Meldeliste
        var t = p.Tournament;
        Assert.Equal((T, "Landesliga Testgau 2026/2027", "2026/27", 3, "Landesliga Testgau", "ligamanager", "bsb/2026-2027/landesliga-testgau-4711"),
            (t.Tnr, t.Name, t.Season, t.Level, t.League, t.Source!, t.SourceRef!));
        Assert.Equal(("11.10.2026", "25.10.2026", 2), (t.Start!, t.End!, t.Rounds!.Value));
        var stat = p.Pages.Stats.Single(s => s.Name == "Örtel, Ömer");
        Assert.Equal((0.5, 1), (stat.Points!.Value, stat.Games!.Value));
    }

    [Fact]
    public void Build_WithoutPlayedRoundsHasNoBoardsYet()
    {
        var s = LigamanagerSource.ParseSchedule(Html("spielplan.html"));
        s = s with { Matches = s.Matches.Select(m => m with { Boards = new(), HomePts = null, AwayPts = null }).ToList() };
        var p = LigamanagerSource.Build(Ref, s, LigamanagerSource.ParseRoster(Html("mannschaften.html")), new());
        Assert.Null(p.Tournament.Boards);
        Assert.Empty(p.Pages.Games);
        Assert.Empty(p.Pages.Stats);
    }

    [Fact]
    public void WhiteBlackResult_TurnsTheHomeViewForTheFixtureTable()
    {
        var p = Parse();
        var b1 = p.Pages.Games.Single(x => x.MatchNo == 2 && x.Board == 3);              // Heim (Charlie) schwarz, verliert
        Assert.Equal(("s", "0 - 1"), (b1.HomeColor!, b1.Result));
        Assert.Equal("1 - 0", LeagueFixtureGames.WhiteBlackResult(b1.Result, b1.HomeColor == "w"));
    }

    private static LigamanagerSource.Parsed Parse() => LigamanagerSource.Build(Ref,
        LigamanagerSource.ParseSchedule(Html("spielplan.html")), LigamanagerSource.ParseRoster(Html("mannschaften.html")),
        LigamanagerSource.ParsePgn(LigamanagerSource.DecodePgn(File.ReadAllBytes(Fixture("alle.pgn")), "windows-1252")));

    // ── Import ──────────────────────────────────────────────────────────────────────────────────

    private sealed class Factory(Func<string, HttpRequestMessage, HttpResponseMessage> answer) : IHttpClientFactory
    {
        public List<string> Calls { get; } = new();
        public HttpClient CreateClient(string name) =>
            new(new Handler(r => { Calls.Add($"{name} {r.RequestUri!.AbsolutePath}"); return answer(name, r); }))
            {
                BaseAddress = new Uri(name == LigamanagerSource.ClientName ? LigamanagerSource.SiteUrl + "/" : "http://crawler/"),
            };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(f(request));
    }

    /// <summary>Der Ligamanager mit den Fixtures; <paramref name="pgn"/> = false → PGN 404 (noch keine Partien).</summary>
    private static HttpResponseMessage Site(HttpRequestMessage r, bool pgn = true)
    {
        var path = r.RequestUri!.AbsolutePath;
        if (path.EndsWith("/spielplan")) return new(HttpStatusCode.OK) { Content = new StringContent(Html("spielplan.html"), Encoding.UTF8, "text/html") };
        if (path.EndsWith("/mannschaften")) return new(HttpStatusCode.OK) { Content = new StringContent(Html("mannschaften.html"), Encoding.UTF8, "text/html") };
        if (pgn && path.EndsWith("/alle.pgn"))
        {
            var c = new ByteArrayContent(File.ReadAllBytes(Fixture("alle.pgn")));
            c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-chess-pgn") { CharSet = "windows-1252" };
            return new(HttpStatusCode.OK) { Content = c };
        }
        return new(HttpStatusCode.NotFound) { Content = new StringContent("<html>404</html>", Encoding.UTF8, "text/html") };
    }

    private LeagueService League() => new(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);

    private LigamanagerSource Source(IHttpClientFactory http) =>
        new(_db, http, League(), NullLogger<LigamanagerSource>.Instance, () => Now) { Pause = TimeSpan.Zero };

    [Fact]
    public async Task Import_FillsTheTablesLinksBoardsAndCards()
    {
        var res = await Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: false, default);

        Assert.False(res.DryRun);
        Assert.Equal((T, "2026/27", 3), (res.Tnr, res.Season, res.Level));
        Assert.Equal((7, 5), (res.ProfileGames, res.ProfileGamesWithFide));
        Assert.Equal(1, res.Views);
        var t = _db.LeagueTournaments.Single();
        Assert.Equal(("ligamanager", "bsb/2026-2027/landesliga-testgau-4711", 4, Now), (t.Source!, t.SourceRef!, t.Boards!.Value, t.UpdatedAt));
        Assert.Equal(2, _db.LeagueRounds.Count());
        Assert.Equal(new DateOnly(2026, 10, 11), _db.LeagueRounds.Single(r => r.Round == 1).Date);
        Assert.Equal(4, _db.LeagueMatches.Count());
        Assert.Equal(20, _db.LeaguePlayers.Count());
        var games = _db.LeagueGames.OrderBy(g => g.MatchNo).ThenBy(g => g.Board).ToList();
        Assert.Equal(8, games.Count);
        // Verknüpfung Brett ↔ Meldeliste über (Team, NameKey): FIDE-ID, Meldebrett, Elo (FIDE, sonst DWZ)
        Assert.Equal(("90000001", 1, 2150, "90000011", 1, 2350), (games[0].HomeFide!, games[0].HomeRb!.Value, games[0].HomeElo!.Value,
            games[0].AwayFide!, games[0].AwayRb!.Value, games[0].AwayElo!.Value));
        Assert.Equal(("90000002", "FM"), (games[1].HomeFide!, games[1].HomeTitle!));                    // „Dr." trifft die Meldeliste
        Assert.Equal((1990, (string?)null), (games[2].HomeElo!.Value, games[2].HomeFide));              // ohne ELO die DWZ
        Assert.Equal("s", games[0].HomeColor);
        // Spielerkarte: Partie mit eigener Quelle
        var card = _db.LeaguePlayerProfiles.Single(p => p.FideId == "90000001");
        Assert.Contains("[LeagueSource \"Ligamanager\"]", card.Pgn);
        Assert.Contains("[WhiteFideId \"90000011\"]", card.Pgn);
        Assert.Equal(1, card.GameCount);
        // Ansicht gerechnet, Link auf den Ligamanager
        var view = _db.LeagueViews.Single();
        Assert.Equal(T, view.Tnr);
        Assert.Contains("\"source\":\"https://ligamanager.schachbund-bayern.de/bsb/2026-2027/landesliga-testgau-4711/spielplan\"", view.Json);
        Assert.DoesNotContain("chess-results.com/tnr", view.Json);      // die versetzte Tnr taugt nie für chess-results
        Assert.Contains("\"boards\":4", view.Json);
    }

    [Fact]
    public async Task Import_Twice_KeepsOneCopyOfEverything()
    {
        var src = Source(new Factory((_, r) => Site(r)));
        await src.ImportAsync(Ref, dryRun: false, default);
        await src.ImportAsync(Ref, dryRun: false, default);
        Assert.Equal(8, _db.LeagueGames.Count());
        Assert.Equal(20, _db.LeaguePlayers.Count());
        Assert.Equal(1, _db.LeaguePlayerProfiles.Single(p => p.FideId == "90000001").GameCount);
    }

    [Fact]
    public async Task Import_DryRun_WritesNothing()
    {
        var res = await Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: true, default);
        Assert.True(res.DryRun);
        Assert.Equal(8, res.Counts.BoardGames);
        Assert.Empty(_db.LeagueTournaments);
        Assert.Empty(_db.LeagueGames);
        Assert.Empty(_db.LeaguePlayerProfiles);
    }

    [Fact]
    public async Task Import_RefusesANumberThatBelongsToAChessResultsLeague()
    {
        // Sicherheitsnetz: unter der VERSETZTEN Nummer steht (künstlich) eine Liga fremder Quelle → 409, nichts geschrieben.
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = T, Name = "TMM", Season = "2026/27", Level = 1, League = "Landesliga" });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<LigamanagerSource.ConflictException>(() =>
            Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: false, default));
        Assert.Empty(_db.LeagueGames);
        Assert.Equal("TMM", _db.LeagueTournaments.Single().Name);
    }

    [Fact]
    public async Task Import_ChessResultsLeagueWithTheBareLigamanagerId_NoLongerCollides()
    {
        // Vor dem Versatz brach das mit 409 ab; jetzt liegen beide nebeneinander.
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 4711, Name = "TMM", Season = "2026/27", Level = 1, League = "Landesliga" });
        await _db.SaveChangesAsync();
        var res = await Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: false, default);
        Assert.Equal(T, res.Tnr);
        Assert.Equal(new[] { 4711, T }, _db.LeagueTournaments.OrderBy(t => t.Tnr).Select(t => t.Tnr).ToArray());
        Assert.Null(_db.LeagueTournaments.Single(t => t.Tnr == 4711).Source);
        Assert.All(_db.LeagueGames, g => Assert.Equal(T, g.Tnr));
        Assert.All(_db.LeaguePlayers, p => Assert.Equal(T, p.Tnr));
        Assert.All(_db.LeagueMatches, m => Assert.Equal(T, m.Tnr));
        Assert.All(_db.LeagueRounds, r => Assert.Equal(T, r.Tnr));
    }

    [Fact]
    public void Tnr_IsOffsetAndRoundTrips()
    {
        Assert.Equal(900_004_711, LigamanagerSource.TnrOf(4711));
        Assert.True(LigamanagerSource.IsLigamanagerTnr(T));
        Assert.Equal(4711, LigamanagerSource.LigamanagerIdOf(T));
        Assert.False(LigamanagerSource.IsLigamanagerTnr(4711));
        Assert.False(LigamanagerSource.IsLigamanagerTnr(1206271));                         // chess-results
        Assert.Null(LigamanagerSource.LigamanagerIdOf(1206271));
        // Der Bereich endet bei 909 999 999 — darüber liegt der Schachkreis Zugspitze (ZugspitzeSource.TnrOffset).
        Assert.Equal(909_999_999, LigamanagerSource.TnrOf(LigamanagerSource.MaxLigamanagerId));
        Assert.True(LigamanagerSource.TnrOf(LigamanagerSource.MaxLigamanagerId) < ZugspitzeSource.TnrOffset);
        Assert.False(LigamanagerSource.IsLigamanagerTnr(ZugspitzeSource.TnrOf(2026, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => LigamanagerSource.TnrOf(LigamanagerSource.MaxLigamanagerId + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LigamanagerSource.TnrOf(0));
        // Die Adresse lässt höchstens 7-stellige Ids zu — die größte passt mit Versatz weit in int.
        var big = LigamanagerSource.LeagueRef.Parse("bsb/2026-2027/xx-9999999")!;
        Assert.Equal(909_999_999, LigamanagerSource.TnrOf(big.Id));
        Assert.Null(LigamanagerSource.LeagueRef.Parse("bsb/2026-2027/xx-12345678"));
    }

    [Fact]
    public async Task LegacyTnrs_FindsLigamanagerLeaguesWithoutTheOffsetOnly()
    {
        _db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = 4711, Season = "2026/27", Level = 3, Source = LigamanagerSource.Source, SourceRef = Ref.Path },  // alt
            new LeagueTournament { Tnr = TPrev, Season = "2025/26", Level = 3, Source = LigamanagerSource.Source },                     // neu
            new LeagueTournament { Tnr = 1206271, Season = "2025/26", Level = 1 });                                                       // chess-results
        await _db.SaveChangesAsync();
        Assert.Equal(new[] { (4711, (string?)Ref.Path) }, (await LigamanagerSource.LegacyTnrsAsync(_db)).ToArray());
        await LigamanagerSource.WarnLegacyTnrsAsync(_db, NullLogger.Instance);    // wirft nicht
    }

    /// <summary>Ältere Saisonen (bis 2018/19) sperren den PGN-Download dauerhaft mit 403 — die Liga kommt trotzdem, Farben nach der
    /// Regel, keine Partien für die Karten (gefunden 07.10.2026 beim Laden der Trainings-Historie).</summary>
    [Fact]
    public async Task Import_PgnForbidden_ImportsWithoutGames()
    {
        var res = await Source(new Factory((_, r) => r.RequestUri!.AbsolutePath.EndsWith("/alle.pgn")
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("<html>Fehler</html>", Encoding.UTF8, "text/html") }
            : Site(r))).ImportAsync(Ref, dryRun: false, default, rebuildViews: false);
        Assert.Equal(0, res.Counts.PgnGames);
        Assert.Equal(8, _db.LeagueGames.Count());
        Assert.Empty(_db.LeaguePlayerProfiles);
        Assert.Null(res.Views);
    }

    [Fact]
    public async Task Import_WithoutProfiles_LeavesTheCardsAlone()
    {
        var res = await Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: false, default, rebuildViews: false, importProfiles: false);
        Assert.Equal((7, 5), (res.ProfileGames, res.ProfileGamesWithFide));   // gezählt, aber nicht eingespielt
        Assert.Equal(0, res.ProfilesTouched);
        Assert.Empty(_db.LeaguePlayerProfiles);
        Assert.Empty(_db.LeagueViews);
    }

    [Fact]
    public async Task Import_UnknownLeague_IsNotFound()
    {
        await Assert.ThrowsAsync<LigamanagerSource.NotFoundException>(() =>
            Source(new Factory((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound))).ImportAsync(Ref, dryRun: false, default));
    }

    [Fact]
    public async Task Import_BeforeTheFirstRound_TakesTheBoardsFromThePreviousSeason()
    {
        // Vorsaison derselben Liga mit 4 Brettern
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = TPrev, Season = "2025/26", Level = 3, League = "Landesliga Testgau",
            Source = LigamanagerSource.Source, SourceRef = "bsb/2025-2026/landesliga-testgau-4600" });
        _db.LeagueGames.Add(new LeagueGame { Tnr = TPrev, Round = 1, MatchNo = 1, Board = 4, HomeTeam = "a", AwayTeam = "b" });
        await _db.SaveChangesAsync();
        var noBoards = Html("spielplan.html");
        noBoards = System.Text.RegularExpressions.Regex.Replace(noBoards, @"<tr class=""text-center""> <th class=""brett-nr.*?</tr>", "",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var http = new Factory((_, r) => r.RequestUri!.AbsolutePath.EndsWith("/spielplan")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(noBoards, Encoding.UTF8, "text/html") }
            : Site(r, pgn: false));
        var res = await Source(http).ImportAsync(Ref, dryRun: false, default);
        Assert.Equal(0, res.Counts.BoardGames);
        Assert.Equal(4, _db.LeagueTournaments.Single(t => t.Tnr == T).Boards);
        Assert.Contains("\"boards\":4", _db.LeagueViews.Single(v => v.Tnr == T).Json);
    }

    [Fact]
    public async Task FillMissingFide_CarriesIdsToOlderSeasonsOfTheSameClub()
    {
        _db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = TPrev, Season = "2025/26", Level = 3, Source = LigamanagerSource.Source },
            new LeagueTournament { Tnr = 1206271, Season = "2025/26", Level = 1 });                       // chess-results: bleibt
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = TPrev, Team = "SK Musterstadt 2", Name = "Muster, Max", NameKey = "muster, max" },
            new LeaguePlayer { Tnr = TPrev, Team = "SC Anderswo 1", Name = "Probe, Paula", NameKey = "probe, paula" },
            new LeaguePlayer { Tnr = 1206271, Team = "Schwaz", Name = "Muster, Max", NameKey = "muster, max" });
        _db.LeagueGames.Add(new LeagueGame { Tnr = TPrev, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "SK Musterstadt 2", AwayTeam = "x",
            HomePlayer = "Muster, Max", AwayPlayer = "Unbekannt, U" });
        await _db.SaveChangesAsync();

        await Source(new Factory((_, r) => Site(r))).ImportAsync(Ref, dryRun: false, default);

        Assert.Equal("90000001", _db.LeaguePlayers.Single(p => p.Tnr == TPrev && p.NameKey == "muster, max").FideId);   // gleicher Verein
        Assert.Null(_db.LeaguePlayers.Single(p => p.Tnr == TPrev && p.NameKey == "probe, paula").FideId);              // anderer Verein
        Assert.Null(_db.LeaguePlayers.Single(p => p.Tnr == 1206271).FideId);                                          // nicht Ligamanager
        var g = _db.LeagueGames.Single(x => x.Tnr == TPrev);
        Assert.Equal(("90000001", (string?)null), (g.HomeFide!, g.AwayFide));
    }

    // ── Aktualisieren ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_FetchesLigamanagerLeaguesFromTheirSourceNotFromTheCrawler()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = T, Season = "2026/27", Level = 3, League = "Landesliga Testgau",
            Source = LigamanagerSource.Source, SourceRef = Ref.Path });
        await _db.SaveChangesAsync();
        var http = new Factory((name, r) => name == LigamanagerSource.ClientName ? Site(r) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var league = League();
        var refresh = new LeagueRefresh(_db, league, http, NullLogger<LeagueRefresh>.Instance, () => Now, Source(http));

        var msg = await refresh.RunAsync(default);

        Assert.StartsWith("1 Ligen neu geholt", msg);
        Assert.DoesNotContain(http.Calls, c => c.Contains($"api/league/{T}"));      // nicht über chess-results
        Assert.Contains(http.Calls, c => c.EndsWith("/bsb/2026-2027/landesliga-testgau-4711/spielplan"));
        Assert.Equal(8, _db.LeagueGames.Count(g => g.Tnr == T));
        Assert.Single(_db.LeagueViews);
    }

    [Fact]
    public async Task Refresh_WithoutTheReader_ReportsTheLeagueAsNotUpdated()
    {
        _db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = T, Season = "2026/27", Level = 3, Source = LigamanagerSource.Source, SourceRef = Ref.Path });
        await _db.SaveChangesAsync();
        var http = new Factory((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var refresh = new LeagueRefresh(_db, League(), http, NullLogger<LeagueRefresh>.Instance, () => Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.RunAsync(default));   // die einzige Liga scheitert
        Assert.DoesNotContain(http.Calls, c => c.Contains($"api/league/{T}"));
    }

    [Fact]
    public async Task Refresh_LeavesALegacyRowWithoutOffsetAlone()
    {
        // Altbestand (Import vor dem Versatz): Nummer 4711 passt nicht zur Quelle → nicht holen, sonst stünde die Liga doppelt da.
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 4711, Season = "2026/27", Level = 3, League = "Landesliga Testgau",
            Source = LigamanagerSource.Source, SourceRef = Ref.Path });
        await _db.SaveChangesAsync();
        var http = new Factory((name, r) => name == LigamanagerSource.ClientName ? Site(r) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var refresh = new LeagueRefresh(_db, League(), http, NullLogger<LeagueRefresh>.Instance, () => Now, Source(http));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.RunAsync(default));   // die einzige Liga scheitert
        Assert.Contains("4711", ex.Message);
        Assert.Empty(http.Calls);
        Assert.Single(_db.LeagueTournaments);
        Assert.Empty(_db.LeagueGames);
    }

    // ── Endpunkt ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Endpoint_ParsesTheRequestAndMapsErrors()
    {
        var ctl = new LeagueController(League(), null!, null!, null!);
        var src = Source(new Factory((_, r) => Site(r)));
        Assert.IsType<BadRequestObjectResult>(await ctl.LigamanagerImport(new("https://example.org/x", null, null, null, null), true, src, default));
        var ok = Assert.IsType<OkObjectResult>(await ctl.LigamanagerImport(
            new(null, "bsb", "2026/27", "landesliga-testgau-4711", null), true, src, default));
        Assert.True(Assert.IsType<LigamanagerSource.ImportResult>(ok.Value).DryRun);
        var missing = Source(new Factory((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.IsType<NotFoundObjectResult>(await ctl.LigamanagerImport(new(Ref.Path, null, null, null, null), false, missing, default));
        var down = Source(new Factory((_, _) => throw new HttpRequestException("weg")));
        Assert.Equal(503, Assert.IsType<ObjectResult>(await ctl.LigamanagerImport(new(Ref.Path, null, null, null, null), false, down, default)).StatusCode);
    }

    // ── Rechenkern: Stufen über 4 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Features_CountAppearancesInLowerBavarianLevels()
    {
        // Vorsaison: Spieler P spielte in einer Kreisliga (Stufe 5); jetzt in der Landesliga (3) gemeldet.
        var ts = new[]
        {
            new LeagueTournament { Tnr = 1, Season = "2025/26", Level = 5, Stage = "Liga", Source = LigamanagerSource.Source },
            new LeagueTournament { Tnr = 2, Season = "2026/27", Level = 3, Stage = "Liga", Source = LigamanagerSource.Source, Boards = 8 },
        };
        var matches = new[]
        {
            new LeagueMatch { Tnr = 1, Round = 1, Home = "A 2", Away = "B 1" },
            new LeagueMatch { Tnr = 2, Round = 1, Home = "A 1", Away = "C 1" },
        };
        var games = new[] { new LeagueGame { Tnr = 1, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "A 2", AwayTeam = "B 1", HomePlayer = "P, P",
            HomeFide = "9", HomeScore = 1, AwayScore = 0, AwayPlayer = "Q, Q" } };
        var players = new[] { new LeaguePlayer { Tnr = 2, Team = "A 1", Name = "P, P", NameKey = "p, p", FideId = "9", RosterBoard = 1 } };
        var w = new LeagueWorld(ts, Array.Empty<LeagueRound>(), matches, games, players);
        var row = Assert.Single(LeagueFeatures.RowsFor(w, 2, "A 1", 1));
        Assert.Equal(1.0, row.QLower);                                  // vorher (bis Stufe 4) 0
        Assert.Equal(0, row.NewEver);
        Assert.Equal(8, w.BoardsOf(2));                                 // Bretter aus der Quelle, nicht aus der Stufe
    }
}
