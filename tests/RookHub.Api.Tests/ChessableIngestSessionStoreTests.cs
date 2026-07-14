using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der In-Memory-Puffer des kapitelweisen Browser-Imports: Akkumulation über Chunks,
/// Entnahme beim Abschluss, Isolation je (User, SessionId).</summary>
public class ChessableIngestSessionStoreTests
{
    private static ChessableIngestChapter Ch(params string[] lines)
        => new("{\"list\":{\"name\":\"Ch\",\"data\":[]}}", lines.ToList());

    [Fact]
    public void AddChapter_AccumulatesChaptersAndLines()
    {
        var store = new ChessableIngestSessionStore();
        var (s1, e1) = store.AddChapter(7, "sess1", "424242", "book", "My Course", Ch("{\"game\":{}}", "{\"game\":{}}"));
        Assert.Null(e1);
        Assert.Equal(1, s1!.Chapters.Count);

        var (s2, e2) = store.AddChapter(7, "sess1", "424242", "book", "My Course", Ch("{\"game\":{}}"));
        Assert.Null(e2);
        Assert.Equal(2, s2!.Chapters.Count);
        Assert.Equal(3, s2.Chapters.Sum(c => c.Lines!.Count));
        Assert.Equal("424242", s2.Bid);
        Assert.Equal("book", s2.Target);
        Assert.Equal("My Course", s2.CourseName);
    }

    [Fact]
    public void FirstChunkFixesBidTargetName_LaterChunksDoNotOverride()
    {
        var store = new ChessableIngestSessionStore();
        store.AddChapter(7, "s", "111", "book", "First", Ch("{\"game\":{}}"));
        // spätere Chunks mit abweichenden Metadaten dürfen die Session nicht umbiegen
        var (s, _) = store.AddChapter(7, "s", "999", "repertoire", "Other", Ch("{\"game\":{}}"));
        Assert.Equal("111", s!.Bid);
        Assert.Equal("book", s.Target);
        Assert.Equal("First", s.CourseName);
    }

    [Fact]
    public void Take_RemovesSession_SecondTakeIsNull()
    {
        var store = new ChessableIngestSessionStore();
        store.AddChapter(7, "s", "1", "repertoire", null, Ch("{\"game\":{}}"));
        var taken = store.Take(7, "s");
        Assert.NotNull(taken);
        Assert.Single(taken!.Chapters);
        Assert.Null(store.Take(7, "s"));   // schon entnommen
    }

    [Fact]
    public void Sessions_AreIsolatedPerUser()
    {
        var store = new ChessableIngestSessionStore();
        store.AddChapter(7, "same", "1", "book", null, Ch("{\"game\":{}}"));
        store.AddChapter(8, "same", "2", "book", null, Ch("{\"game\":{}}"));
        var u7 = store.Take(7, "same");
        var u8 = store.Take(8, "same");
        Assert.Equal("1", u7!.Bid);
        Assert.Equal("2", u8!.Bid);
    }

    [Fact]
    public void Discard_DropsSessionWithoutImport()
    {
        var store = new ChessableIngestSessionStore();
        store.AddChapter(7, "s", "1", "book", null, Ch("{\"game\":{}}"));
        store.Discard(7, "s");
        Assert.Null(store.Take(7, "s"));
    }
}
