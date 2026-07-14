using System.Collections.Concurrent;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// In-Memory-Puffer für den KAPITELWEISEN Browser-Import (RepCheck): die Extension streamt einen
/// Chessable-Kurs Kapitel für Kapitel (bounded pro Request), der Server sammelt die rohen Kapitel hier
/// und parst/importiert sie erst beim letzten Chunk als GANZEN Kurs (→ korrekte Round-Reihenfolge über
/// Kapitel hinweg, ohne den piratechess-Parser anzufassen). Singleton, prozessweit; Sessions sind pro
/// (User, sessionId) isoliert und laufen nach <see cref="Ttl"/> ohne Aktivität ab (Leak-Schutz, wenn der
/// Browser mitten im Crawl schließt). Analog zu piratechess' CourseFetchJobStore, nur ohne DB.
/// </summary>
public class ChessableIngestSessionStore
{
    public sealed class Session
    {
        public int UserId { get; init; }
        public string Bid { get; set; } = string.Empty;
        public string Target { get; set; } = "repertoire";
        public string? CourseName { get; set; }
        public List<ChessableIngestChapter> Chapters { get; } = new();
        public long Bytes { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // Deckel je Session (großzügig, aber gegen Endlos-Wachstum/OOM). Ein einzelner Kurs bleibt darunter.
    private const int MaxChapters = 2000;
    private const long MaxBytes = 128L * 1024 * 1024;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    private static string Key(int userId, string sessionId) => userId + ":" + sessionId;

    /// <summary>Fügt ein Kapitel an die (lazily angelegte) Session an. bid/target/courseName kommen vom
    /// ERSTEN Chunk und bleiben fix. Liefert die aktualisierte Session oder eine Fehlermeldung
    /// (Deckel überschritten). Räumt nebenbei abgelaufene Sessions ab.</summary>
    public (Session? session, string? error) AddChapter(
        int userId, string sessionId, string bid, string target, string? courseName, ChessableIngestChapter chapter)
    {
        PurgeExpired();
        var key = Key(userId, sessionId);
        var s = _sessions.GetOrAdd(key, _ => new Session
        {
            UserId = userId,
            Bid = bid,
            Target = target == "book" ? "book" : "repertoire",
            CourseName = courseName,
        });

        lock (s)
        {
            var size = (long)(chapter.ChapterJson?.Length ?? 0)
                + (chapter.Lines?.Sum(l => (long)(l?.Length ?? 0)) ?? 0);
            if (s.Chapters.Count >= MaxChapters)
                return (null, "Too many chapters in one import session.");
            if (s.Bytes + size > MaxBytes)
                return (null, "Import session exceeds size limit.");

            s.Chapters.Add(chapter);
            s.Bytes += size;
            s.UpdatedAt = DateTime.UtcNow;
            return (s, null);
        }
    }

    /// <summary>Entnimmt (und entfernt) die Session zum Abschluss. null, wenn unbekannt/abgelaufen.</summary>
    public Session? Take(int userId, string sessionId)
        => _sessions.TryRemove(Key(userId, sessionId), out var s) ? s : null;

    /// <summary>Verwirft eine Session ohne Import (Abbruch/Fehler).</summary>
    public void Discard(int userId, string sessionId) => _sessions.TryRemove(Key(userId, sessionId), out _);

    private void PurgeExpired()
    {
        if (_sessions.IsEmpty) return;
        var cutoff = DateTime.UtcNow - Ttl;
        foreach (var kv in _sessions)
            if (kv.Value.UpdatedAt < cutoff)
                _sessions.TryRemove(kv.Key, out _);
    }
}
