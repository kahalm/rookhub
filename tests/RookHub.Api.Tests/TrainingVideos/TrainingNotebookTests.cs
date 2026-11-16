using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Tests.TrainingVideos;

/// <summary>Trainer-Notizbuch: Notizen stundenübergreifend, wiederkehrende Themen, Eröffnungsstellungen + Repertoire.</summary>
public sealed class TrainingNotebookTests : IDisposable
{
    /// <summary>Französisch nach 1.e4 e6 2.d4 d5 (Weiß am Zug).</summary>
    private const string FrenchFen = "rnbqkbnr/ppp2ppp/4p3/3p4/3PP3/8/PPP2PPP/RNBQKBNR w KQkq - 0 3";

    private readonly AppDbContext _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TrainingVideoOptions _options = new();
    private readonly TrainingNotebookService _notebook;
    private readonly TrainingVideoService _videos;

    public TrainingNotebookTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _videos = new TrainingVideoService(_db, Options.Create(_options));
        _notebook = new TrainingNotebookService(_db, _videos, new RepertoireBoardContinuations(new RepertoireLineSource(_db, _cache)));
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private async Task<AppUser> UserAsync(string name, bool admin = false)
    {
        var u = new AppUser { Username = name, PasswordHash = "x", IsAdmin = admin, CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private async Task<TrainingLesson> LessonAsync(int userId, string date, params (string Category, string Text, int Sec)[] notes)
    {
        var l = new TrainingLesson { UserId = userId, Date = DateOnly.Parse(date), ImportedAt = DateTime.UtcNow };
        l.Games.Add(new TrainingLessonGame { Key = "g1", Kind = "own", White = "Ich", Black = "Gegner", RookhubGameId = 77 });
        var pos = 0;
        foreach (var (cat, text, sec) in notes)
            l.Notes.Add(new TrainingNote { Position = pos++, StartSec = sec, GameKey = "g1", Category = cat, Text = text, SearchText = text });
        _db.TrainingLessons.Add(l);
        await _db.SaveChangesAsync();
        return l;
    }

    private async Task RepertoireAsync(int ownerId, string name, string pgn)
    {
        var rep = new Repertoire { UserId = ownerId, Name = name, Kind = RepertoireKind.Opening };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "r.pgn", PgnContent = pgn, FileSize = pgn.Length });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Import_EndspielCategory_IsKept()
    {
        var u = await UserAsync("patrik");
        var json = """
            { "schema": 1, "date": "2026-07-21", "video": { "file": "2026-07-21.mp4" }, "games": [], "segments": [],
              "notes": [ { "t": "0:10:00", "category": "endspiel", "text": "Turm hinter den Freibauern." } ] }
            """;
        var report = await new TrainingVideoImporter(_db, Options.Create(_options)).ImportAsync(u.Id, json, dryRun: false, default);

        Assert.True(report.Ok);
        Assert.Equal("endspiel", (await _db.TrainingNotes.SingleAsync()).Category);
    }

    [Fact]
    public void Tokens_SkipMovesStopwordsAndStemEndings()
    {
        var t = TrainingNoteThemes.Tokens("Gegen das Läuferpaar: 14...Bxg5 Nd4 und dann halbieren, immer die Läuferpaares!");

        Assert.Contains("läuferpaar", t);
        Assert.Contains("halbier", t);
        Assert.DoesNotContain("gegen", t);
        Assert.DoesNotContain(t, x => x.StartsWith("bxg", StringComparison.Ordinal));
        Assert.Equal(2, t.Count);
    }

    [Fact]
    public void Themes_GroupSimilarNotesFromSeveralLessons_IgnoreSingleLessonRepeats()
    {
        var d1 = new DateOnly(2025, 5, 20);
        var d2 = new DateOnly(2025, 10, 28);
        var d3 = new DateOnly(2026, 8, 18);
        var notes = new List<TrainingNoteThemes.Input>
        {
            new(1, 10, d1, 0, "plan", "Hat der Gegner das Läuferpaar, ist die wirksamste Strategie, es zu halbieren."),
            new(2, 20, d2, 0, "plan", "Gegen das Läuferpaar ist es die effektivste Strategie, es zu halbieren."),
            new(3, 30, d3, 0, "allgemein", "Die effektivste Methode gegen das Läuferpaar: es halbieren, also einen Läufer abtauschen."),
            new(4, 10, d1, 1, "zeit", "Zu viel Bedenkzeit in der Eröffnung verbraucht."),
            new(5, 20, d2, 1, "psychologie", "Remisangebote nicht sofort annehmen, wenn man besser steht."),
            // dieselbe Aussage zweimal in EINER Stunde ist kein wiederkehrendes Thema
            new(6, 30, d3, 1, "endspiel", "Turmendspiele mit Freibauern: Turm hinter den Freibauern stellen."),
            new(7, 30, d3, 2, "endspiel", "Der Turm gehört hinter den Freibauern, auch im Turmendspiel."),
        };

        var themes = TrainingNoteThemes.Find(notes);

        var theme = Assert.Single(themes);
        Assert.Equal(3, theme.Lessons);
        Assert.Equal(new[] { 3, 2, 1 }, theme.NoteIds);   // neueste zuerst
        Assert.Equal("plan", theme.Category);
        Assert.Contains(theme.RepresentativeId, new[] { 1, 2, 3 });
    }

    [Fact]
    public void Nearest_PrefersSegmentContainingTime_ElseClosestStart()
    {
        var segs = new List<(int, int?)> { (100, 200), (150, 400), (600, null) };

        Assert.Equal(1, TrainingNotebookService.Nearest(segs, 180));   // in beiden Spannen: das zuletzt begonnene
        Assert.Equal(2, TrainingNotebookService.Nearest(segs, 560));
        Assert.Equal(0, TrainingNotebookService.Nearest(segs, null));
        Assert.Null(TrainingNotebookService.Nearest(new List<(int, int?)>(), 10));
    }

    [Fact]
    public async Task GetAsync_ShowsOnlyOwnLessons_AdminSeesAll()
    {
        var me = await UserAsync("patrik");
        var other = await UserAsync("fremd");
        var admin = await UserAsync("admin", admin: true);
        await LessonAsync(me.Id, "2026-01-15", ("plan", "Eigene Notiz.", 60));
        await LessonAsync(other.Id, "2026-02-15", ("plan", "Fremde Notiz.", 60));

        var mine = await _notebook.GetAsync(me.Id, false, default);
        var all = await _notebook.GetAsync(admin.Id, true, default);

        var note = Assert.Single(mine.Notes);
        Assert.Equal("Eigene Notiz.", note.Text);
        Assert.Equal("own", note.GameKind);
        Assert.Equal(77, note.RookhubGameId);
        Assert.Equal("Ich", note.GameWhite);
        Assert.Equal(2, all.Notes.Count);
        Assert.Equal("2026-02-15", all.Notes[0].Date);   // neueste Stunde zuerst
    }

    [Fact]
    public async Task GetAsync_Opening_TakesNearestSegmentOfSameGame_AndOwnRepertoireMoves()
    {
        var me = await UserAsync("patrik");
        var friend = await UserAsync("freund");
        var lesson = await LessonAsync(me.Id, "2026-02-18", ("eroeffnung", "Gegen Französisch spiele ich Tarrasch.", 130));
        _db.TrainingSegments.AddRange(
            new TrainingSegment { LessonId = lesson.Id, Position = 0, StartSec = 100, EndSec = 160, GameKey = "g1", Fen = FrenchFen, BoardFen = TrainingBoardKey.Of(FrenchFen), Move = "Nd2" },
            new TrainingSegment { LessonId = lesson.Id, Position = 1, StartSec = 125, EndSec = 140, GameKey = "g2", Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", BoardFen = "8/8/8/8/8/8/8/K6k" });
        await _db.SaveChangesAsync();
        await RepertoireAsync(me.Id, "Weiß gegen Französisch", "[White \"Tarrasch\"]\n[Black \"Französisch\"]\n\n1. e4 e6 2. d4 d5 3. Nd2 (3. Nc3 Nf6) c5 *");
        // Geteiltes Repertoire eines Freundes zählt nicht als „eigenes“.
        await RepertoireAsync(friend.Id, "Fremd", "1. e4 e6 2. d4 d5 3. e5 *");
        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = _db.Repertoires.Single(r => r.Name == "Fremd").Id, OwnerId = friend.Id, RecipientId = me.Id });
        await _db.SaveChangesAsync();

        var nb = await _notebook.GetAsync(me.Id, false, default);

        Assert.True(nb.HasRepertoire);
        var opening = Assert.Single(nb.Openings);
        Assert.Equal(FrenchFen, opening.Fen);              // g2-Segment liegt näher, gehört aber zu einer anderen Partie
        var hit = Assert.Single(opening.Repertoire);
        Assert.Equal("Weiß gegen Französisch", hit.RepertoireName);
        Assert.Equal(new[] { "Nd2", "Nc3" }, hit.Moves);
        Assert.Equal(4, hit.Ply);
        Assert.Equal("Französisch", hit.Chapter);
        Assert.True(hit.SameSide);
    }

    [Fact]
    public async Task GetAsync_WithoutOwnRepertoire_ReportsNoRepertoire()
    {
        var me = await UserAsync("patrik");
        var lesson = await LessonAsync(me.Id, "2026-02-18", ("eroeffnung", "Gegen Französisch spiele ich Tarrasch.", 130));
        _db.TrainingSegments.Add(new TrainingSegment { LessonId = lesson.Id, Position = 0, StartSec = 100, GameKey = "g1", Fen = FrenchFen, BoardFen = TrainingBoardKey.Of(FrenchFen) });
        await _db.SaveChangesAsync();

        var nb = await _notebook.GetAsync(me.Id, false, default);

        Assert.False(nb.HasRepertoire);
        Assert.Empty(Assert.Single(nb.Openings).Repertoire);
    }

    [Fact]
    public async Task SearchText_NoteHit_CarriesNoteId()
    {
        var me = await UserAsync("patrik");
        var lesson = await LessonAsync(me.Id, "2026-02-18", ("plan", "Das Läuferpaar halbieren.", 30));

        var result = await _videos.SearchTextAsync(me.Id, false, "läuferpaar", default);

        var hit = Assert.Single(result.Hits);
        Assert.Equal(lesson.Notes[0].Id, hit.NoteId);
    }
}
