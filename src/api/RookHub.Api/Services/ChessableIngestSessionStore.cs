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

    // Der Pro-Session-Deckel allein schützt NICHT: die Session-Id kommt vom Client, also konnte ein
    // einzelner authentifizierter Client beliebig viele Sessions öffnen (je bis 128 MB, TTL 30 min) und
    // damit den Heap füllen. Zusätzlich daher ein Deckel für die Anzahl offener Sessions je User und ein
    // prozessweites Byte-Budget; beide intern überschreibbar für Tests.
    internal int MaxSessionsPerUser = 3;
    internal long MaxTotalBytes = 512L * 1024 * 1024;
    /// <summary>Längen-Obergrenze der (client-vergebenen) Session-Id — sie ist Teil des Dictionary-Keys.</summary>
    public const int MaxSessionIdLength = 64;

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    private static string Key(int userId, string sessionId) => userId + ":" + sessionId;

    /// <summary>Aktuell gepufferte Bytes über alle Sessions (Diagnose/Tests).</summary>
    internal long TotalBytes => _sessions.Values.Sum(s => s.Bytes);

    /// <summary>Fügt ein Kapitel an die (lazily angelegte) Session an. bid/target/courseName kommen vom
    /// ERSTEN Chunk und bleiben fix. Liefert die aktualisierte Session oder eine Fehlermeldung
    /// (Deckel überschritten). Räumt nebenbei abgelaufene Sessions ab.</summary>
    public (Session? session, string? error) AddChapter(
        int userId, string sessionId, string bid, string target, string? courseName, ChessableIngestChapter chapter)
    {
        PurgeExpired();
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > MaxSessionIdLength)
            return (null, "Invalid sessionId.");

        var key = Key(userId, sessionId);
        // Deckel VOR dem Anlegen prüfen (nur für NEUE Sessions; eine laufende darf weiterlaufen).
        if (!_sessions.ContainsKey(key))
        {
            if (_sessions.Count(kv => kv.Value.UserId == userId) >= MaxSessionsPerUser)
                return (null, "Too many concurrent import sessions — finish or abort one first.");
            if (TotalBytes >= MaxTotalBytes)
                return (null, "Server is busy with other imports — please retry shortly.");
        }

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
            if (TotalBytes + size > MaxTotalBytes)
                return (null, "Server-side import buffer is full — please retry shortly.");

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
