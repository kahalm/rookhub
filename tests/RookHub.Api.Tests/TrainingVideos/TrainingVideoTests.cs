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
        // Zwei Einträge: privat (eigene Partie g1 + alle Notizen) und allgemein (Lehrpartie g2), dasselbe Video.
        Assert.Equal(new[] { "private", "general" }, report.Parts.Select(p => p.Part));
        var lesson = await _db.TrainingLessons.Include(l => l.Segments).ThenInclude(s => s.Question).Include(l => l.Notes)
            .SingleAsync(l => l.Part == TrainingLessonParts.Private);
        var general = await _db.TrainingLessons.Include(l => l.Segments).Include(l => l.Games).Include(l => l.Notes)
            .SingleAsync(l => l.Part == TrainingLessonParts.General);
        Assert.Equal(report.LessonId, lesson.Id);
        Assert.Equal(new[] { 3630 }, general.Segments.Select(s => s.StartSec));
        Assert.Equal("g2", Assert.Single(general.Games).Key);
        Assert.Empty(general.Notes);
        Assert.Equal(lesson.VideoFile, general.VideoFile);
        Assert.Equal(3, lesson.Segments.Count);
        Assert.Equal(2, lesson.Notes.Count);
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
        Assert.Equal(first.Parts.Select(p => p.LessonId), second.Parts.Select(p => p.LessonId));
        Assert.All(second.Parts, p => Assert.True(p.Replaced));
        Assert.Equal(2, await _db.TrainingLessons.CountAsync());
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
        Assert.Equal(4, await _db.TrainingLessons.CountAsync());   // je Konto privat + allgemein
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
        Assert.All(await _db.TrainingLessons.ToListAsync(), l => Assert.Null(l.VideoFile));
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

        var own = await _service.ListAsync(owner.Id, false, default);
        Assert.Equal(new[] { "private", "general" }, own.Select(l => l.Part));
        Assert.Empty(await _service.ListAsync(other.Id, false, default));
        var adminList = await _service.ListAsync(admin.Id, true, default);
        Assert.Equal(2, adminList.Count);
        Assert.All(adminList, l => Assert.Equal("patrik", l.OwnerName));
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
        Assert.Equal("private", dto.Part);
        Assert.Equal(new[] { 272, 370, 480 }, dto.Segments.Select(s => s.StartSec));
        Assert.True(dto.Segments[1].HasQuestion);
        Assert.False(dto.HasVideo);
        var other = Assert.Single(dto.OtherParts!);
        Assert.Equal("general", other.Part);
        var general = await _service.GetAsync(u.Id, false, other.Id, default);
        Assert.Equal(new[] { 3630 }, general.Segments.Select(s => s.StartSec));
        Assert.Equal("1. e4 e6 2. d4 d5 *", general.Pgns["g2"]);
        Assert.Contains("(2... c5", general.AnalysisPgns["g2"]);
        Assert.Equal(id, Assert.Single(general.OtherParts!).Id);
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
        var general = await _service.GetAsync(u.Id, false, lesson.OtherParts!.Single().Id, default);
        Assert.Null(general.Segments.Single(s => s.StartSec == 3630).TaskFen);   // ohne Zug/Frage keine Aufgabe

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

    // ---------------------------------------------------------------- Teilung privat / allgemein

    [Fact]
    public async Task Split_OnlyLessonGames_GivesOneGeneralEntryWithTheNotes()
    {
        var u = await UserAsync("patrik");
        var json = _fixture.Replace("\"kind\": \"own\"", "\"kind\": \"exercise\"");
        var report = await _importer.ImportAsync(u.Id, json, false, default);
        var part = Assert.Single(report.Parts);
        Assert.Equal("general", part.Part);
        var lesson = await _db.TrainingLessons.Include(l => l.Notes).Include(l => l.Segments).SingleAsync();
        Assert.Equal(TrainingLessonParts.General, lesson.Part);
        Assert.Equal(4, lesson.Segments.Count);
        Assert.Equal(2, lesson.Notes.Count);
    }

    [Fact]
    public async Task Split_SegmentsWithoutGame_FollowTheirNeighbours_UnclearIsPrivate()
    {
        var all = new TrainingLesson
        {
            Games =
            {
                new TrainingLessonGame { Key = "o", Kind = "own" },
                new TrainingLessonGame { Key = "l", Kind = "lesson" },
            },
        };
        TrainingSegment Seg(int t, string? key) { var s = new TrainingSegment { StartSec = t, Position = t, GameKey = key }; all.Segments.Add(s); return s; }
        var intro = Seg(0, null);            // vor allem: nur ein Nachbar (privat)
        Seg(10, "o");
        var between = Seg(20, null);         // zwischen privat und allgemein: unklar → privat
        Seg(30, "l");
        var inLesson = Seg(40, null);        // zwischen zwei allgemeinen → allgemein
        Seg(50, "l");
        var unknown = Seg(60, "gibtsnicht"); // unbekannte Partie = ohne Partie; nur Nachbar davor → allgemein
        all.Notes.Add(new TrainingNote { Text = "Schwäche", GameKey = "l" });

        var parts = TrainingVideoImporter.SplitParts(all);

        var priv = parts.Single(p => p.Part == TrainingLessonParts.Private);
        var gen = parts.Single(p => p.Part == TrainingLessonParts.General);
        Assert.Contains(intro, priv.Segments);
        Assert.Contains(between, priv.Segments);
        Assert.Contains(inLesson, gen.Segments);
        Assert.Contains(unknown, gen.Segments);
        Assert.Single(priv.Notes);           // Notizen immer zum privaten Teil, auch mit Lehrpartie
        Assert.Empty(gen.Notes);
        Assert.Equal(new[] { "private", "general" }, parts.Select(p => p.Part));
    }

    [Fact]
    public void Split_NoGamesAtAll_IsOnePrivateEntry()
    {
        var all = new TrainingLesson { Segments = { new TrainingSegment { StartSec = 5 } }, Notes = { new TrainingNote { Text = "x" } } };
        var part = Assert.Single(TrainingVideoImporter.SplitParts(all));
        Assert.Equal(TrainingLessonParts.Private, part.Part);
        Assert.Single(part.Segments);
        Assert.Single(part.Notes);
    }

    /// <summary>Altbestand (eine Zeile je Datum, Migration setzt „private“) mit Übungsstand an eigenen UND Lehrpartie-
    /// Segmenten: der Re-Import teilt, die private Zeile behält ihre Id, der Stand der Lehrpartie zieht in den neuen
    /// allgemeinen Eintrag um — nichts geht verloren, auch nicht der Stand eines fremden Kontos (Admin).</summary>
    [Fact]
    public async Task Split_LegacyCombinedLesson_MovesProgressToTheMatchingPart()
    {
        var u = await UserAsync("patrik");
        var admin = await UserAsync("admin", admin: true);
        var legacy = _fixture.Replace("\"kind\": \"lesson\"", "\"kind\": \"own\"");   // alles in EINEM Eintrag
        var id = (await _importer.ImportAsync(u.Id, legacy, false, default)).LessonId!.Value;
        Assert.Single(_db.TrainingLessons);
        var q = await _db.TrainingSegments.SingleAsync(s => s.StartSec == 370);
        var french = await _db.TrainingSegments.SingleAsync(s => s.StartSec == 3630);
        await _service.SetProgressAsync(u.Id, false, id, q.Id, new("wrong", 2), default);
        await _service.SetProgressAsync(u.Id, false, id, french.Id, new("solved", 0), default);
        await _service.SetProgressAsync(u.Id, false, id, french.Id, new("solved", 1), default);
        await _service.SetProgressAsync(admin.Id, true, id, french.Id, new("skipped", 0), default);
        // Ein Stand ohne passendes Segment mehr (Datei geändert) bleibt am privaten Eintrag.
        _db.TrainingSegmentProgress.Add(new TrainingSegmentProgress { UserId = u.Id, LessonId = id, SegmentKey = "9999|alt", Status = "wrong", Plays = 3, UpdatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var report = await _importer.ImportAsync(u.Id, _fixture, false, default);

        Assert.Equal(2, report.ProgressMoved);
        Assert.Equal(id, report.Parts.Single(p => p.Part == "private").LessonId);
        var generalId = report.Parts.Single(p => p.Part == "general").LessonId!.Value;
        Assert.NotEqual(id, generalId);
        Assert.Equal(4, await _db.TrainingSegmentProgress.CountAsync());
        Assert.Equal(7, await _db.TrainingSegmentProgress.SumAsync(p => p.Plays));   // 1 + 2 + 1 + 3
        var priv = await _service.GetAsync(u.Id, false, id, default);
        Assert.Equal("wrong", priv.Segments.Single(s => s.StartSec == 370).Progress!.Status);
        var gen = await _service.GetAsync(u.Id, false, generalId, default);
        var fr = gen.Segments.Single(s => s.StartSec == 3630).Progress!;
        Assert.Equal(("solved", 1, 2), (fr.Status, fr.Attempts, fr.Plays));
        Assert.Equal("skipped", (await _service.GetAsync(admin.Id, true, generalId, default)).Segments.Single().Progress!.Status);
        Assert.True(await _db.TrainingSegmentProgress.AnyAsync(p => p.LessonId == id && p.SegmentKey == "9999|alt"));

        // Nochmals einlesen: nichts zieht mehr um, Ids bleiben.
        _db.ChangeTracker.Clear();
        var again = await _importer.ImportAsync(u.Id, _fixture, false, default);
        Assert.Equal(0, again.ProgressMoved);
        Assert.Equal(report.Parts.Select(p => p.LessonId), again.Parts.Select(p => p.LessonId));
        Assert.Equal(4, await _db.TrainingSegmentProgress.CountAsync());
    }

    /// <summary>Altbestand einer Stunde nur mit Lehrpartien, aber noch als „private“ markiert: die Zeile wird zum
    /// allgemeinen Eintrag (gleiche Id, Stand bleibt) — kein zweiter Eintrag.</summary>
    [Fact]
    public async Task Split_LegacyRowOfTheOtherPart_IsReused()
    {
        var u = await UserAsync("patrik");
        var json = _fixture.Replace("\"kind\": \"own\"", "\"kind\": \"lesson\"");
        var id = (await _importer.ImportAsync(u.Id, json, false, default)).LessonId!.Value;
        var row = await _db.TrainingLessons.SingleAsync();
        row.Part = TrainingLessonParts.Private;
        await _db.SaveChangesAsync();
        var seg = await _db.TrainingSegments.SingleAsync(s => s.StartSec == 370);
        await _service.SetProgressAsync(u.Id, false, id, seg.Id, new("wrong", 1), default);
        _db.ChangeTracker.Clear();

        var report = await _importer.ImportAsync(u.Id, json, false, default);

        Assert.Equal(id, Assert.Single(report.Parts).LessonId);
        var lesson = await _db.TrainingLessons.SingleAsync();
        Assert.Equal(TrainingLessonParts.General, lesson.Part);
        Assert.Equal("wrong", (await _service.GetAsync(u.Id, false, id, default)).Segments.Single(s => s.StartSec == 370).Progress!.Status);
    }

    /// <summary>Wird aus einer gemischten Stunde eine ohne Lehrpartie, verschwindet der allgemeine Eintrag — sein Stand
    /// zieht vorher in den privaten (dort gibt es das Segment jetzt).</summary>
    [Fact]
    public async Task Split_PartDisappears_ProgressMovesBeforeTheRowIsDeleted()
    {
        var u = await UserAsync("patrik");
        var report = await _importer.ImportAsync(u.Id, _fixture, false, default);
        var generalId = report.Parts.Single(p => p.Part == "general").LessonId!.Value;
        var seg = await _db.TrainingSegments.SingleAsync(s => s.StartSec == 3630);
        await _service.SetProgressAsync(u.Id, false, generalId, seg.Id, new("solved", 0), default);
        _db.ChangeTracker.Clear();

        var onlyOwn = _fixture.Replace("\"kind\": \"lesson\"", "\"kind\": \"own\"");
        var again = await _importer.ImportAsync(u.Id, onlyOwn, false, default);

        var part = Assert.Single(again.Parts);
        Assert.Equal(report.LessonId, part.LessonId);
        Assert.Single(_db.TrainingLessons);
        var p = await _db.TrainingSegmentProgress.SingleAsync();
        Assert.Equal(report.LessonId, p.LessonId);
        Assert.Equal("solved", (await _service.GetAsync(u.Id, false, part.LessonId!.Value, default))
            .Segments.Single(s => s.StartSec == 3630).Progress!.Status);
    }

    [Fact]
    public async Task Search_AndQuestionSet_CarryThePart()
    {
        var u = await UserAsync("patrik");
        await _importer.ImportAsync(u.Id, _fixture, false, default);
        var hits = (await _service.SearchTextAsync(u.Id, false, "Franzose", default)).Hits;
        Assert.Equal("general", Assert.Single(hits).Part);
        var note = (await _service.SearchTextAsync(u.Id, false, "Bedenkzeit", default)).Hits.Single();
        Assert.Equal(("note", "private"), (note.Kind, note.Part));
        Assert.Equal("Oberschmid, Patrik – Gegner, Max", note.GameLabel);
        Assert.Equal("private", Assert.Single(await _service.QuestionSetAsync(u.Id, false, false, default)).Part);
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
