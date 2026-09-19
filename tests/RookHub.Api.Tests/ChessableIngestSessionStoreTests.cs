using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Zustand einer laufenden Browser-Import-Sitzung: Zähler über Chunks hinweg, Kapitel-Versatz,
/// Isolation je (User, SessionId), Deckel für gleichzeitige Sitzungen — und die Inflight-Marke, die
/// den Import über die Request-Grenze hinweg als „lokal getrieben" hält.
/// <para>Bis v0.483.1 puffert der Store die rohen Kapitel im Speicher (128 MB je Sitzung). Genau das
/// ist weg: ein 1881-Linien-Kurs à 455 KB je Linie sprengte den Deckel nach ~288 Linien, und mit ihm
/// wurde der GANZE Puffer verworfen.</para>
/// </summary>
public class ChessableIngestSessionStoreTests
{
    [Fact]
    public void GetOrCreate_FirstChunkFixesBidTargetName_LaterChunksReuseSession()
    {
        var store = new ChessableIngestSessionStore();
        var (s1, e1) = store.GetOrCreate(7, "sess1", "424242", "book", "My Course");
        Assert.Null(e1);
        Assert.Equal("424242", s1!.Bid);
        Assert.Equal("book", s1.Target);
        Assert.Equal("My Course", s1.CourseName);

        var (s2, e2) = store.GetOrCreate(7, "sess1", "999999", "repertoire", "Anderer Name");
        Assert.Null(e2);
        Assert.Same(s1, s2);
        Assert.Equal("424242", s2!.Bid);      // vom ersten Chunk, nicht überschrieben
        Assert.Equal("book", s2.Target);
    }

    [Fact]
    public void NoteChapter_CountsChaptersLinesImported_AndKeepsHighestChapterNumber()
    {
        var store = new ChessableIngestSessionStore();
        var (s, _) = store.GetOrCreate(7, "sess1", "424242", "book", "C");

        store.NoteChapter(s!, chapterOffset: 3, linesSeen: 12, imported: 12, resultId: 55);
        store.NoteChapter(s!, chapterOffset: 5, linesSeen: 8, imported: 3, resultId: 55);
        // Ein Chunk ohne neue Kapitelnummer darf den Versatz NICHT zurückdrehen.
        store.NoteChapter(s!, chapterOffset: 0, linesSeen: 1, imported: 0, resultId: null);

        Assert.Equal(3, s!.ChaptersDone);
        Assert.Equal(21, s.LinesSeen);
        Assert.Equal(15, s.Imported);
        Assert.Equal(5, s.ChapterOffset);
        Assert.Equal(55, s.ResultId);
    }

    [Fact]
    public void Take_RemovesSession_SecondTakeIsNull()
    {
        var store = new ChessableIngestSessionStore();
        store.GetOrCreate(7, "sess1", "424242", "book", "C");
        Assert.NotNull(store.Take(7, "sess1"));
        Assert.Null(store.Take(7, "sess1"));
    }

    [Fact]
    public void Sessions_AreIsolatedPerUser()
    {
        var store = new ChessableIngestSessionStore();
        var (a, _) = store.GetOrCreate(7, "same-id", "111111", "book", "A");
        var (b, _) = store.GetOrCreate(8, "same-id", "222222", "repertoire", "B");
        Assert.NotSame(a, b);
        Assert.Equal("111111", a!.Bid);
        Assert.Equal("222222", b!.Bid);

        store.Take(7, "same-id");
        Assert.NotNull(store.Take(8, "same-id"));   // fremde Sitzung bleibt unberührt
    }

    [Fact]
    public void TooManySessionsPerUser_IsRejected_AndSlotIsFreeAfterTake()
    {
        var store = new ChessableIngestSessionStore { MaxSessionsPerUser = 2 };
        Assert.Null(store.GetOrCreate(7, "s1", "1", "book", null).error);
        Assert.Null(store.GetOrCreate(7, "s2", "1", "book", null).error);

        var (blocked, error) = store.GetOrCreate(7, "s3", "1", "book", null);
        Assert.Null(blocked);
        Assert.Contains("Too many", error);

        store.Take(7, "s1");
        Assert.Null(store.GetOrCreate(7, "s3", "1", "book", null).error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidSessionId_IsRejected(string id)
    {
        var store = new ChessableIngestSessionStore();
        var (s, error) = store.GetOrCreate(7, id, "424242", "book", "C");
        Assert.Null(s);
        Assert.Equal("Invalid sessionId.", error);
    }

    [Fact]
    public void OverlongSessionId_IsRejected()
    {
        var store = new ChessableIngestSessionStore();
        var (s, error) = store.GetOrCreate(7, new string('x', ChessableIngestSessionStore.MaxSessionIdLength + 1),
            "424242", "book", "C");
        Assert.Null(s);
        Assert.Equal("Invalid sessionId.", error);
    }

    [Fact]
    public void Inflight_HeldAcrossChunks_ReleasedOnTake()
    {
        // Zwischen zwei Chunks liegen Minuten; ohne diese Marke hielte der Watchdog den Import nach
        // seiner Karenz für verwaist und reihte ihn neu ein — mitten im laufenden Browser-Import.
        var store = new ChessableIngestSessionStore();
        var (s, _) = store.GetOrCreate(7, "sess1", "424242", "book", "C");
        store.AttachImport(s!, 4711, ChessableImportService.TrackInflight(4711));

        Assert.True(ChessableImportService.IsDrivenLocally(4711));
        store.Take(7, "sess1");
        Assert.False(ChessableImportService.IsDrivenLocally(4711));
    }

    [Fact]
    public void Discard_ReleasesInflightToo()
    {
        var store = new ChessableIngestSessionStore();
        var (s, _) = store.GetOrCreate(7, "sess1", "424242", "book", "C");
        store.AttachImport(s!, 4712, ChessableImportService.TrackInflight(4712));

        store.Discard(7, "sess1");
        Assert.False(ChessableImportService.IsDrivenLocally(4712));
    }

    [Fact]
    public void TakeExpired_ReturnsStaleSessions_AndReleasesTheirInflightMark()
    {
        var store = new ChessableIngestSessionStore { Ttl = TimeSpan.Zero };
        var (s, _) = store.GetOrCreate(7, "sess1", "424242", "book", "C");
        store.AttachImport(s!, 4713, ChessableImportService.TrackInflight(4713));
        store.NoteChapter(s!, 2, 10, 10, 55);

        var expired = store.TakeExpired();

        var only = Assert.Single(expired);
        Assert.Equal(4713, only.ImportId);
        Assert.Equal(10, only.Imported);
        Assert.False(ChessableImportService.IsDrivenLocally(4713));
        Assert.Equal(0, store.Count);
        Assert.Empty(store.TakeExpired());   // idempotent
    }

    [Fact]
    public void TakeExpired_LeavesFreshSessionsAlone()
    {
        var store = new ChessableIngestSessionStore();
        store.GetOrCreate(7, "fresh", "424242", "book", "C");
        Assert.Empty(store.TakeExpired());
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Discard_ReturnsTheSession_SoItsImportCanBeClosed()
    {
        var store = new ChessableIngestSessionStore();
        var (s, _) = store.GetOrCreate(7, "sess1", "424242", "book", "C");
        store.AttachImport(s!, 4714, ChessableImportService.TrackInflight(4714));
        var dropped = store.Discard(7, "sess1");
        Assert.Same(s, dropped);
        Assert.Null(store.Discard(7, "sess1"));
    }
}
