using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Tests.TrainingVideos;

public sealed class TrainingVideoTests : IDisposable
{
    private const string GamePgn = "[White \"Oberschmid, Patrik\"]\n[Black \"Gegner, Max\"]\n[Result \"1-0\"]\n\n"
        + "1. e4 e6 2. d4 d5 3. Nd2 c5 4. Ngf3 Nc6 5. Bb5 cxd4 6. Nxd4 Bd7 1-0";

    private readonly AppDbContext _db;
    private readonly string _videoRoot;
    private readonly TrainingVideoOptions _options;
    private readonly TrainingVideoImporter _importer;
    private readonly TrainingVideoService _service;
    private readonly string _fixture;

    public TrainingVideoTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _videoRoot = Path.Combine(Path.GetTempPath(), "rh-tv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_videoRoot);
        _options = new TrainingVideoOptions { VideoRoot = _videoRoot };
        _importer = new TrainingVideoImporter(_db, Options.Create(_options));
        _service = new TrainingVideoService(_db, Options.Create(_options));
        _fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TrainingVideos", "2026-10-27.json"));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_videoRoot, true); } catch { /* egal */ }
    }

    private async Task<AppUser> UserAsync(string name, bool admin = false)
    {
        var u = new AppUser { Username = name, PasswordHash = "x", IsAdmin = admin, CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private async Task<SavedGame> GameAsync(int userId, int id = 4711)
    {
        var g = new SavedGame { Id = id, UserId = userId, Source = "otb", Pgn = GamePgn, ShareToken = Guid.NewGuid().ToString("N") };
        _db.SavedGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Import_DryRun_ReportsCountsAndWritesNothing()
    {
        var u = await UserAsync("patrik");
        var report = await _importer.ImportAsync(u.Id, _fixture, dryRun: true, default);

        Assert.True(report.Ok);
        Assert.True(report.DryRun);
        Assert.Equal("2026-10-27", report.Date);
        Assert.Equal(2, report.Games);
        Assert.Equal(4, report.Segments);
        Assert.Equal(1, report.Questions);
        Assert.Equal(2, report.Notes);
        Assert.Equal(1, report.InvalidFens);
        Assert.False(report.VideoFound);
        Assert.Contains(report.Warnings, w => w.Contains("4711"));   // Partie gehört (noch) niemandem
        Assert.Empty(_db.TrainingLessons);
    }

    [Fact]
    public async Task Import_Apply_StoresBoardKeyTimesQuestionAndNotes()
    {
        var u = await UserAsync("patrik");
        await GameAsync(u.Id);
        File.WriteAllText(Path.Combine(_videoRoot, "2026-10-27.mp4"), "x");

        var report = await _importer.ImportAsync(u.Id, _fixture, dryRun: false, default);

        Assert.True(report.Ok);
        Assert.True(report.VideoFound);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("4711"));
        var lesson = await _db.TrainingLessons.Include(l => l.Segments).ThenInclude(s => s.Question).Include(l => l.Notes).SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 27), lesson.Date);
        Assert.Equal(3737, lesson.DurationSec);
        var first = lesson.Segments.OrderBy(s => s.Position).First();
        Assert.Equal(272, first.StartSec);
        Assert.Equal(302, first.EndSec);
        Assert.Equal("r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R", first.BoardFen);
        Assert.Equal("opening,key", first.Tags);
        var q = lesson.Segments.Single(s => s.Question != null).Question!;
        Assert.Equal("Bd7", q.Answer);
        Assert.Equal(400, q.AnswerSec);
        Assert.Null(lesson.Segments.Single(s => s.StartSec == 480).BoardFen);   // „kaputt“
        Assert.Equal("allgemein", lesson.Notes.Single(n => n.StartSec == 2400).Category);
    }

    [Fact]
    public async Task Import_Twice_ReplacesChildrenAndKeepsLessonId()
    {
        var u = await UserAsync("patrik");
        var first = await _importer.ImportAsync(u.Id, _fixture, dryRun: false, default);
        _db.ChangeTracker.Clear();
        var changed = _fixture.Replace("Die Grundstellung des Franzosen.", "Neu formuliert.");
        var second = await _importer.ImportAsync(u.Id, changed, dryRun: false, default);

        Assert.True(second.Replaced);
        Assert.Equal(first.LessonId, second.LessonId);
        Assert.Single(_db.TrainingLessons);
        Assert.Equal(4, await _db.TrainingSegments.CountAsync());
        Assert.Equal(1, await _db.TrainingQuestions.CountAsync());
        Assert.Equal(2, await _db.TrainingNotes.CountAsync());
        Assert.Contains(_db.TrainingSegments, s => s.Comment == "Neu formuliert.");
    }

    [Fact]
    public async Task Import_SameDateOtherUser_IsSeparateLesson()
    {
        var a = await UserAsync("a");
        var b = await UserAsync("b");
        await _importer.ImportAsync(a.Id, _fixture, false, default);
        await _importer.ImportAsync(b.Id, _fixture, false, default);
        Assert.Equal(2, await _db.TrainingLessons.CountAsync());
    }

    [Theory]
    [InlineData("{\"schema\":3,\"date\":\"2026-10-27\"}", "Schema")]
    [InlineData("{\"schema\":1,\"date\":\"27.10.2026\"}", "Datum")]
    [InlineData("{kaputt", "JSON")]
    public async Task Import_BadFile_ReportsError(string json, string expected)
    {
        var u = await UserAsync("patrik");
        var report = await _importer.ImportAsync(u.Id, json, false, default);
        Assert.False(report.Ok);
        Assert.Contains(report.Errors, e => e.Contains(expected));
        Assert.Empty(_db.TrainingLessons);
    }

    [Fact]
    public async Task Import_VideoWithPath_IsIgnored()
    {
        var u = await UserAsync("patrik");
        var json = _fixture.Replace("\"file\": \"2026-10-27.mp4\"", "\"file\": \"../etc/passwd\"");
        var report = await _importer.ImportAsync(u.Id, json, false, default);
        Assert.Contains(report.Warnings, w => w.Contains("Dateiname"));
        Assert.Null((await _db.TrainingLessons.SingleAsync()).VideoFile);
    }

    [Fact]
    public async Task ResolveOwner_ByNameOrId_AndUnknownIs404()
    {
        var u = await UserAsync("patrik");
        Assert.Equal(u.Id, (await _importer.ResolveOwnerAsync("patrik", default)).Id);
        Assert.Equal("patrik", (await _importer.ResolveOwnerAsync(u.Id.ToString(), default)).Username);
        await Assert.ThrowsAsync<NotFoundException>(() => _importer.ResolveOwnerAsync("niemand", default));
    }

    [Fact]
    public async Task Visibility_OwnerAndAdminOnly()
    {
        var owner = await UserAsync("patrik");
        var other = await UserAsync("fremd");
        var admin = await UserAsync("admin", admin: true);
        var report = await _importer.ImportAsync(owner.Id, _fixture, false, default);
        var id = report.LessonId!.Value;

        Assert.Single(await _service.ListAsync(owner.Id, false, default));
        Assert.Empty(await _service.ListAsync(other.Id, false, default));
        var adminList = await _service.ListAsync(admin.Id, true, default);
        Assert.Equal("patrik", Assert.Single(adminList).OwnerName);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(other.Id, false, id, default));
        Assert.Empty((await _service.SearchPositionAsync(other.Id, false,
            "r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 2 5", default)).Hits);
        Assert.Empty((await _service.SearchTextAsync(other.Id, false, "Läufer", default)).Hits);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ResolveVideoAsync(other.Id, false, id, default));
    }

    [Fact]
    public async Task Get_ReturnsSegmentsInOrderWithPgnOfLessonGame()
    {
        var u = await UserAsync("patrik");
        var id = (await _importer.ImportAsync(u.Id, _fixture, false, default)).LessonId!.Value;
        var dto = await _service.GetAsync(u.Id, false, id, default);
        Assert.Equal(new[] { 272, 370, 480, 3630 }, dto.Segments.Select(s => s.StartSec));
        Assert.True(dto.Segments[1].HasQuestion);
        Assert.Equal("1. e4 e6 2. d4 d5 *", dto.Pgns["g2"]);
        Assert.Contains("(2... c5", dto.AnalysisPgns["g2"]);
        Assert.False(dto.HasVideo);
    }

    [Fact]
    public async Task SearchPosition_BoardOnly_MatchesAndReportsSide()
    {
        var u = await UserAsync("patrik");
        await _importer.ImportAsync(u.Id, _fixture, false, default);

        var boardOnly = await _service.SearchPositionAsync(u.Id, false, "r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R", default);
        var hit = Assert.Single(boardOnly.Hits);
        Assert.Equal(272, hit.StartSec);
        Assert.Null(hit.SameSide);
        Assert.Equal("Oberschmid, Patrik – Gegner, Max", hit.GameLabel);

        var otherSide = await _service.SearchPositionAsync(u.Id, false, "r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R b - - 0 1", default);
        Assert.False(Assert.Single(otherSide.Hits).SameSide);

        await Assert.ThrowsAsync<DomainValidationException>(() => _service.SearchPositionAsync(u.Id, false, "quatsch", default));
    }

    [Fact]
    public async Task SearchText_FindsSegmentsAndNotes_AllWordsRequired()
    {
        var u = await UserAsync("patrik");
        await _importer.ImportAsync(u.Id, _fixture, false, default);

        var hits = (await _service.SearchTextAsync(u.Id, false, "Läufer", default)).Hits;
        Assert.Contains(hits, h => h.Kind == "segment" && h.StartSec == 272);
        Assert.Contains(hits, h => h.Kind == "note" && h.Text!.Contains("Springer vor Läufer"));

        var both = (await _service.SearchTextAsync(u.Id, false, "Plan Mittelspiel", default)).Hits;
        Assert.Equal(370, Assert.Single(both).StartSec);
        Assert.Empty((await _service.SearchTextAsync(u.Id, false, "Plan Endspiel", default)).Hits);
        await Assert.ThrowsAsync<DomainValidationException>(() => _service.SearchTextAsync(u.Id, false, "ab", default));   // zwei Zeichen
    }

    [Fact]
    public async Task GameComments_MapSegmentsToPositionsOfTheGame()
    {
        var u = await UserAsync("patrik");
        await GameAsync(u.Id);
        await _importer.ImportAsync(u.Id, _fixture, false, default);

        var result = await _service.GameCommentsAsync(u.Id, false, 4711, default);

        // Segment 1: Stellung vor 5.Lb5 mit Zug „Bb5“ → nach dem 9. Halbzug (5.Lb5) = Index 9.
        var bb5 = result.Items.Single(i => i.StartSec == 272);
        Assert.Equal(9, bb5.FenIndex);
        Assert.Equal("move", bb5.MatchedBy);
        // Segment 2: Stellung nach 6.Sxd4 ohne Zug → Index 11, nur Stellung.
        var plan = result.Items.Single(i => i.StartSec == 370);
        Assert.Equal(11, plan.FenIndex);
        Assert.Equal("position", plan.MatchedBy);
        Assert.Equal(2, result.Items.Count);          // ungültige FEN und Lehrpartie g2 gehören nicht dazu
        Assert.Equal(0, result.Unmatched);

        var stranger = await UserAsync("fremd");
        Assert.Empty((await _service.GameCommentsAsync(stranger.Id, false, 4711, default)).Items);
    }

    [Fact]
    public async Task GameComments_MoveNamedAfterThePosition_AttachesToThatPosition()
    {
        var u = await UserAsync("patrik");
        await GameAsync(u.Id);
        // FEN schon NACH 5.Lb5, Zug „Bb5+“ (mit Zeichen) → Index 9.
        var json = _fixture.Replace("r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 2 5",
            "r1bqkbnr/pp3ppp/2n1p3/1Bpp4/3PP3/5N2/PPPN1PPP/R1BQK2R b KQkq - 3 5").Replace("\"move\": \"Bb5\"", "\"move\": \"Bb5+\"");
        await _importer.ImportAsync(u.Id, json, false, default);
        var item = (await _service.GameCommentsAsync(u.Id, false, 4711, default)).Items.Single(i => i.StartSec == 272);
        Assert.Equal(9, item.FenIndex);
        Assert.Equal("move", item.MatchedBy);
    }

    [Fact]
    public async Task GameComments_AnalysisMove_StaysAtThePosition()
    {
        var u = await UserAsync("patrik");
        await GameAsync(u.Id);
        // Derselbe Zug „Bb5“, aber als Analysezug markiert → an der Stellung VOR dem Zug (Index 8), nicht am Partiezug.
        var json = _fixture.Replace("\"move\": \"Bb5\",", "\"move\": \"Bb5\", \"moveKind\": \"analysis\", \"quoteFrom\": \"0:04:40\",");
        var report = await _importer.ImportAsync(u.Id, json, false, default);
        Assert.True(report.Ok);
        var seg = await _db.TrainingSegments.SingleAsync(s => s.StartSec == 272);
        Assert.Equal("analysis", seg.MoveKind);
        Assert.Equal(280, seg.QuoteStartSec);
        var item = (await _service.GameCommentsAsync(u.Id, false, 4711, default)).Items.Single(i => i.StartSec == 272);
        Assert.Equal(8, item.FenIndex);
        Assert.Equal("analysis", item.MatchedBy);
    }

    [Fact]
    public async Task GameComments_PositionOutsideTheGame_AttachesAsVariationToLastMatch()
    {
        var u = await UserAsync("patrik");
        await GameAsync(u.Id);
        // Lehrpartie-Segment auf g1 umhängen: die Franzosen-Grundstellung kommt in der Partie vor (Index 4) — darum eine
        // Stellung, die es in der Partie nicht gibt (Variante 6…Lb4 statt 6…Ld7).
        var json = _fixture.Replace("\"t\": \"1:00:30\", \"gameId\": \"g2\", \"fen\": \"rnbqkbnr/ppp2ppp/4p3/3p4/3PP3/8/PPP2PPP/RNBQKBNR w KQkq - 0 3\"",
            "\"t\": \"1:00:30\", \"gameId\": \"g1\", \"fen\": \"r1bqk1nr/pp3ppp/2n1p3/1B1p4/1b1NP3/8/PPPN1PPP/R1BQK2R w KQkq - 1 7\"");
        await _importer.ImportAsync(u.Id, json, false, default);
        var result = await _service.GameCommentsAsync(u.Id, false, 4711, default);
        var variation = result.Items.Single(i => i.StartSec == 3630);
        Assert.Equal("variation", variation.MatchedBy);
        Assert.Equal(11, variation.FenIndex);           // hinter dem zuletzt zugeordneten Segment (nach 6.Sxd4)
        Assert.StartsWith("r1bqk1nr/", variation.Fen);
    }

    [Fact]
    public async Task Progress_SavedPerUser_SurvivesReimport_AndFeedsQuestionSet()
    {
        var u = await UserAsync("patrik");
        var other = await UserAsync("fremd");
        var id = (await _importer.ImportAsync(u.Id, _fixture, false, default)).LessonId!.Value;
        var question = await _db.TrainingSegments.SingleAsync(s => s.StartSec == 370);

        var saved = await _service.SetProgressAsync(u.Id, false, id, question.Id, new("wrong", 2), default);
        Assert.Equal(1, saved.Plays);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.SetProgressAsync(other.Id, false, id, question.Id, new("solved", 0), default));
        await Assert.ThrowsAsync<DomainValidationException>(() => _service.SetProgressAsync(u.Id, false, id, question.Id, new("maybe", 0), default));

        var set = await _service.QuestionSetAsync(u.Id, false, onlyWrong: false, default);
        var item = Assert.Single(set);
        Assert.Equal("Was spielt Schwarz hier?", item.Segment.Question!.Prompt);
        Assert.Equal("wrong", item.Segment.Progress!.Status);
        Assert.Single(await _service.QuestionSetAsync(u.Id, false, onlyWrong: true, default));
        Assert.Empty(await _service.QuestionSetAsync(other.Id, false, onlyWrong: false, default));

        // Re-Import: neue Segment-Ids, der Stand bleibt (Schlüssel Startzeit + Figurenfeld).
        _db.ChangeTracker.Clear();
        await _importer.ImportAsync(u.Id, _fixture, false, default);
        var lesson = await _service.GetAsync(u.Id, false, id, default);
        var again = lesson.Segments.Single(s => s.StartSec == 370);
        Assert.NotEqual(question.Id, again.Id);
        Assert.Equal("wrong", again.Progress!.Status);
        Assert.Equal(2, again.Progress.Attempts);

        var solved = await _service.SetProgressAsync(u.Id, false, id, again.Id, new("solved", 0), default);
        Assert.Equal(2, solved.Plays);
        Assert.Empty(await _service.QuestionSetAsync(u.Id, false, onlyWrong: true, default));
    }

    [Fact]
    public async Task Drill_TaskFenIsThePositionBeforeTheMove_EvenWhenTheFileShowsTheOneAfter()
    {
        var u = await UserAsync("patrik");
        await GameAsync(u.Id);
        const string before = "r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 2 5";
        const string after = "r1bqkbnr/pp3ppp/2n1p3/1Bpp4/3PP3/5N2/PPPN1PPP/R1BQK2R b KQkq - 3 5";
        var json = _fixture.Replace(before, after).Replace("\"move\": \"Bb5\",", "\"move\": \"Bb5\", \"moveKind\": \"played\",");
        var id = (await _importer.ImportAsync(u.Id, json, false, default)).LessonId!.Value;
        var lesson = await _service.GetAsync(u.Id, false, id, default);

        var played = lesson.Segments.Single(s => s.StartSec == 272);
        Assert.Equal(after, played.Fen);
        Assert.Equal(before, played.TaskFen);            // aus der Partie zurückgerechnet
        Assert.Equal("Bb5", played.TaskMove);
        var question = lesson.Segments.Single(s => s.StartSec == 370);
        Assert.Equal(question.Fen, question.TaskFen);    // Frage: Antwort Ld7 aus der eigenen Stellung
        Assert.Equal("Bd7", question.TaskMove);
        Assert.Null(lesson.Segments.Single(s => s.StartSec == 3630).TaskFen);   // ohne Zug/Frage keine Aufgabe

        var set = await _service.QuestionSetAsync(u.Id, false, false, default);
        Assert.Equal(4711, Assert.Single(set).RookhubGameId);
    }

    [Fact]
    public async Task Drill_WithoutGame_PlayedMoveLegalFromFen_IsTask_IllegalIsNot()
    {
        var u = await UserAsync("patrik");      // keine RookHub-Partie 4711 → nur die Legalität aus der FEN zählt
        var json = _fixture.Replace("\"move\": \"Bb5\",", "\"move\": \"Bb5\", \"moveKind\": \"played\",");
        var id = (await _importer.ImportAsync(u.Id, json, false, default)).LessonId!.Value;
        var seg = (await _service.GetAsync(u.Id, false, id, default)).Segments.Single(s => s.StartSec == 272);
        Assert.Equal("Bb5", seg.TaskMove);

        var bad = _fixture.Replace("\"move\": \"Bb5\",", "\"move\": \"Qh5\", \"moveKind\": \"played\",").Replace("2026-10-27\"", "2026-10-28\"");
        var id2 = (await _importer.ImportAsync(u.Id, bad, false, default)).LessonId!.Value;
        Assert.Null((await _service.GetAsync(u.Id, false, id2, default)).Segments.Single(s => s.StartSec == 272).TaskFen);
    }

    [Fact]
    public async Task SearchText_MinLengthCountsAllLetters_ShortWordsAsSubstring()
    {
        var u = await UserAsync("patrik");
        await _importer.ImportAsync(u.Id, _fixture, false, default);
        await Assert.ThrowsAsync<DomainValidationException>(() => _service.SearchTextAsync(u.Id, false, "a b", default));
        var hits = (await _service.SearchTextAsync(u.Id, false, "Lb 5", default)).Hits;   // zusammen 3 Zeichen, beide kurz
        Assert.Equal(272, Assert.Single(hits).StartSec);
    }

    [Fact]
    public async Task ResolveVideo_OnlyExistingFileInsideRoot()
    {
        var u = await UserAsync("patrik");
        var id = (await _importer.ImportAsync(u.Id, _fixture, false, default)).LessonId!.Value;
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ResolveVideoAsync(u.Id, false, id, default));
        File.WriteAllText(Path.Combine(_videoRoot, "2026-10-27.mp4"), "x");
        Assert.EndsWith("2026-10-27.mp4", await _service.ResolveVideoAsync(u.Id, false, id, default));
    }

    [Theory]
    [InlineData("0:04:32", 272)]
    [InlineData("1:00:14", 3614)]
    [InlineData("4:32", 272)]
    [InlineData("272", 272)]
    [InlineData("0:04:32.7", 272)]
    [InlineData("x", null)]
    [InlineData("", null)]
    public void TrainingTime_Parses(string text, int? expected) => Assert.Equal(expected, TrainingTime.ParseSeconds(text));

    [Fact]
    public void TrainingTime_Formats() => Assert.Equal("1:00:14", TrainingTime.Format(3614));

    [Fact]
    public void AccessToken_RoundTrip()
    {
        var tokens = new TrainingVideoAccessTokens(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var (token, _) = tokens.Issue(42);
        Assert.True(tokens.TryRead(token, out var id));
        Assert.Equal(42, id);
        Assert.False(tokens.TryRead(token + "x", out _));
        Assert.False(tokens.TryRead(null, out _));
    }
}
