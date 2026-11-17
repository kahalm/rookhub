using Microsoft.Extensions.Options;
using RookHub.Api.Models;
using RookHub.Api.Services.TrainingVideos;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Trainingsvideos gegen ECHTES MariaDB: der Volltext (FULLTEXT + MATCH im Boolean-Modus, InMemory hat nur die
/// Contains-Krücke), die Unterabfrage der Sichtbarkeit, der Re-Import mit eindeutigem (UserId, Date, Part) und das Teilen einer alten
/// Stunde in privat/allgemein samt Umzug des Übungsstands (eindeutiger Index je Konto + Eintrag + Stellung).
/// </summary>
public class TrainingVideoSqlTests(TrainingVideoSqlFixture fixture) : IAsyncLifetime, IClassFixture<TrainingVideoSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Lesson = """
    {
      "schema": 1, "date": "2026-10-27", "video": { "file": "2026-10-27.mp4", "durationSec": 3737 },
      "summary": "Französisch",
      "games": [ { "id": "g1", "kind": "own", "white": "A", "black": "B", "rookhubGameId": GAMEID, "matchConfidence": "high" } ],
      "segments": [
        { "t": "0:04:32", "gameId": "g1", "fen": "r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 2 5",
          "move": "Bb5", "comment": "Mit dem Läuferzug nimmst du dem Springer den Halt.", "quote": "ja passt", "tags": ["key"] },
        { "t": "0:06:10", "gameId": "g1", "fen": "r1bqkbnr/pp3ppp/2n1p3/1B1p4/3NP3/8/PPPN1PPP/R1BQK2R b KQkq - 0 6",
          "comment": "Isolierter Bauer, der Plan fehlt.", "quote": "isolani", "tags": ["plan"],
          "question": { "prompt": "Was nun?", "answer": "Bd7" } }
      ],
      "notes": [ { "t": "0:30:00", "category": "zeit", "text": "Zu viel Bedenkzeit verbraucht.", "quote": "Uhr" } ]
    }
    """;

    private const string Pgn = "1. e4 e6 2. d4 d5 3. Nd2 c5 4. Ngf3 Nc6 5. Bb5 cxd4 6. Nxd4 Bd7 1-0";

    [MySqlFact]
    public async Task ImportTwice_SearchAndGameComments_TranslateOnMariaDb()
    {
        int userId, otherId, gameId;
        await using (var db = fixture.Schema.NewContext())
        {
            var u = new AppUser { Username = "tv-owner", Email = "o@test.invalid", PasswordHash = "x" };
            var o = new AppUser { Username = "tv-other", Email = "x@test.invalid", PasswordHash = "x" };
            db.AppUsers.AddRange(u, o);
            await db.SaveChangesAsync();
            var g = new SavedGame { UserId = u.Id, Source = "pgn", Pgn = Pgn, ShareToken = "tv1", MoveCount = 12, HeadersScanned = true };
            db.SavedGames.Add(g);
            await db.SaveChangesAsync();
            (userId, otherId, gameId) = (u.Id, o.Id, g.Id);
        }
        var json = Lesson.Replace("GAMEID", gameId.ToString());
        var options = Options.Create(new TrainingVideoOptions());

        int lessonId;
        await using (var db = fixture.Schema.NewContext())
            lessonId = (await new TrainingVideoImporter(db, options).ImportAsync(userId, json, false, default)).LessonId!.Value;
        await using (var db = fixture.Schema.NewContext())
        {
            var again = await new TrainingVideoImporter(db, options).ImportAsync(userId, json, false, default);
            Assert.True(again.Replaced);
            Assert.Equal(lessonId, again.LessonId);
        }

        await using (var db = fixture.Schema.NewContext())
        {
            var service = new TrainingVideoService(db, options);
            Assert.Single(await service.ListAsync(userId, false, default));
            var lesson = await service.GetAsync(userId, false, lessonId, default);
            Assert.Equal(2, lesson.Segments.Count);

            var text = (await service.SearchTextAsync(userId, false, "läufer", default)).Hits;
            Assert.Equal(272, Assert.Single(text).StartSec);
            Assert.Single((await service.SearchTextAsync(userId, false, "Bedenkzeit", default)).Hits, h => h.Kind == "note");
            Assert.Single((await service.SearchTextAsync(userId, false, "isol plan", default)).Hits);
            Assert.Empty((await service.SearchTextAsync(otherId, false, "läufer", default)).Hits);

            var pos = await service.SearchPositionAsync(userId, false, "r1bqkbnr/pp3ppp/2n1p3/1B1p4/3NP3/8/PPPN1PPP/R1BQK2R", default);
            Assert.Equal(370, Assert.Single(pos.Hits).StartSec);

            var comments = await service.GameCommentsAsync(userId, false, gameId, default);
            Assert.Equal(new[] { 9, 11 }, comments.Items.Select(i => i.FenIndex));
            Assert.Empty((await service.GameCommentsAsync(otherId, false, gameId, default)).Items);
        }
    }

    [MySqlFact]
    public async Task SplitLegacyLesson_MovesProgress_OnMariaDb()
    {
        int userId;
        await using (var db = fixture.Schema.NewContext())
        {
            var u = new AppUser { Username = "tv-split", Email = "s@test.invalid", PasswordHash = "x" };
            db.AppUsers.Add(u);
            await db.SaveChangesAsync();
            userId = u.Id;
        }
        var split = Lesson.Replace("\"rookhubGameId\": GAMEID, ", "").Replace("\"answer\": \"Bd7\" } }", "\"answer\": \"Bd7\" } }, { \"t\": \"1:00:30\", \"gameId\": \"g2\", "
            + "\"fen\": \"rnbqkbnr/ppp2ppp/4p3/3p4/3PP3/8/PPP2PPP/RNBQKBNR w KQkq - 0 3\", \"comment\": \"Franzose.\" }").Replace("\"matchConfidence\": \"high\" } ]",
            "\"matchConfidence\": \"high\" }, { \"id\": \"g2\", \"kind\": \"lesson\", \"white\": \"Karpov\", \"black\": \"Korchnoi\" } ]");
        Assert.Contains("Franzose.", split);
        Assert.Contains("\"g2\"", split);
        var legacy = split.Replace("\"kind\": \"lesson\"", "\"kind\": \"own\"");
        var options = Options.Create(new TrainingVideoOptions());

        int id;
        await using (var db = fixture.Schema.NewContext())
            id = (await new TrainingVideoImporter(db, options).ImportAsync(userId, legacy, false, default)).LessonId!.Value;
        await using (var db = fixture.Schema.NewContext())
        {
            var service = new TrainingVideoService(db, options);
            var segs = (await service.GetAsync(userId, false, id, default)).Segments;
            await service.SetProgressAsync(userId, false, id, segs.Single(s => s.StartSec == 370).Id, new("wrong", 1), default);
            await service.SetProgressAsync(userId, false, id, segs.Single(s => s.StartSec == 3630).Id, new("solved", 0), default);
        }

        await using (var db = fixture.Schema.NewContext())
        {
            var report = await new TrainingVideoImporter(db, options).ImportAsync(userId, split, false, default);
            Assert.Equal(1, report.ProgressMoved);
            Assert.Equal(new[] { "private", "general" }, report.Parts.Select(p => p.Part));
            Assert.Equal(id, report.Parts[0].LessonId);
        }

        await using (var db = fixture.Schema.NewContext())
        {
            var service = new TrainingVideoService(db, options);
            var list = await service.ListAsync(userId, false, default);
            Assert.Equal(new[] { "private", "general" }, list.Select(l => l.Part));
            var general = await service.GetAsync(userId, false, list[1].Id, default);
            Assert.Equal("solved", Assert.Single(general.Segments).Progress!.Status);
            var priv = await service.GetAsync(userId, false, id, default);
            Assert.Equal("wrong", priv.Segments.Single(s => s.StartSec == 370).Progress!.Status);
            Assert.Equal("general", Assert.Single((await service.SearchTextAsync(userId, false, "Franzose", default)).Hits).Part);
        }
    }
}

public sealed class TrainingVideoSqlFixture() : MariaDbClassFixture("trainingvideo", withApp: false);
