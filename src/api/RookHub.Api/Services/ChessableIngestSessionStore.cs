using System.Collections.Concurrent;

namespace RookHub.Api.Services;

/// <summary>
/// Zustand einer LAUFENDEN Browser-Import-Sitzung (RepCheck): die Extension streamt einen Chessable-Kurs
/// Kapitel für Kapitel, und jeder Chunk wird SOFORT geparst und angehängt. Hier steht nur, was der nächste
/// Chunk dafür wissen muss — Ziel, Import-Datensatz, Kapitel-Versatz und Zähler. Singleton, prozessweit;
/// Sitzungen sind je (User, sessionId) isoliert und laufen nach <see cref="Ttl"/> ohne Aktivität ab.
///
/// <para><b>Warum nicht mehr puffern:</b> Bis v0.483.1 sammelte der Server die rohen Kapitel im
/// Arbeitsspeicher und importierte erst beim letzten Chunk den ganzen Kurs. Das hatte zwei Kanten, die
/// am 2026-09-19 einen Nutzer trafen: ein Deckel von 128 MB je Sitzung, und ein Verwerfen des GESAMTEN
/// Puffers, sobald er fiel. „Lifetime Repertoires: King's Indian Defense - Part 2" hat 1881 Linien à
/// gemessen 455 KB Rohdaten — 835 MB. Nach rund 288 Linien war Schluss, mit einer Fehlermeldung nach
/// 30–60 Minuten Crawlen und ohne eine einzige importierte Linie. Laufend importiert gibt es keinen
/// Deckel mehr, und was geholt wurde, bleibt auch nach einem Abbruch.</para>
/// </summary>
public class ChessableIngestSessionStore : IDisposable
{
    public sealed class Session
    {
        public int UserId { get; init; }
        public string Bid { get; init; } = string.Empty;
        public string Target { get; init; } = "repertoire";
        public string? CourseName { get; set; }

        /// <summary>Der EINE Import-Datensatz dieser Sitzung (erst mit dem ersten Kapitel angelegt).</summary>
        public int? ImportId { get; set; }
        /// <summary>Ziel-Id (Buch bzw. Repertoire), sobald der erste Chunk importiert ist.</summary>
        public int? ResultId { get; set; }

        /// <summary>Höchste bisher vergebene Kapitelnummer — der nächste Chunk setzt dahinter auf
        /// (siehe <see cref="ChessableRoundOffset"/>).</summary>
        public int ChapterOffset { get; set; }
        public int ChaptersDone { get; set; }
        public int LinesSeen { get; set; }
        public int Imported { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Hält den Import über die REQUEST-Grenze hinweg als „lokal getrieben" markiert. Ohne das
        /// hielte der Watchdog ihn nach <c>OrphanGrace</c> (10 min) für verwaist und reihte ihn neu ein —
        /// zwischen zwei Chunks liegen aber durchaus 13 Minuten, und die Fast-Lane würde dann einen echten
        /// Chessable-Abruf starten, während der Browser noch streamt.</summary>
        internal IDisposable? Inflight { get; set; }
    }

    /// <summary>Kapitel je Sitzung — nur noch als Reißleine gegen einen Client, der endlos streamt.</summary>
    private const int MaxChapters = 5000;
    /// <summary>Ohne Chunk in dieser Zeit gilt die Sitzung als abgebrochen (Browser zu, Netz weg). Intern
    /// setzbar für Tests.</summary>
    internal TimeSpan Ttl = TimeSpan.FromMinutes(30);

    /// <summary>Gleichzeitige Sitzungen je Nutzer: die sessionId kommt vom Client, also könnte ein einzelner
    /// Client sonst beliebig viele offene Importe erzeugen.</summary>
    internal int MaxSessionsPerUser = 3;
    /// <summary>Längen-Obergrenze der (client-vergebenen) Session-Id — sie ist Teil des Dictionary-Keys.</summary>
    public const int MaxSessionIdLength = 64;

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    private static string Key(int userId, string sessionId) => userId + ":" + sessionId;

    /// <summary>Offene Sitzungen (Diagnose/Tests).</summary>
    internal int Count => _sessions.Count;

    /// <summary>
    /// Sitzung holen oder anlegen. bid/target/courseName kommen vom ERSTEN Chunk und bleiben fix.
    /// Liefert <c>error</c> statt einer Sitzung, wenn die Id unbrauchbar ist oder der Nutzer schon zu viele
    /// offene Sitzungen hat. Räumt nebenbei abgelaufene Sitzungen ab.
    /// </summary>
    public (Session? session, string? error) GetOrCreate(
        int userId, string sessionId, string bid, string target, string? courseName)
    {
        PurgeExpired();
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > MaxSessionIdLength)
            return (null, "Invalid sessionId.");

        var key = Key(userId, sessionId);
        if (!_sessions.ContainsKey(key)
            && _sessions.Count(kv => kv.Value.UserId == userId) >= MaxSessionsPerUser)
            return (null, "Too many concurrent import sessions — finish or abort one first.");

        var s = _sessions.GetOrAdd(key, _ => new Session
        {
            UserId = userId,
            Bid = bid,
            Target = target == "book" ? "book" : "repertoire",
            CourseName = courseName,
        });
        if (s.ChaptersDone >= MaxChapters) return (null, "Too many chapters in one import session.");
        s.UpdatedAt = DateTime.UtcNow;
        return (s, null);
    }

    /// <summary>Verbindet die Sitzung mit ihrem Import-Datensatz und übernimmt dessen Inflight-Marke
    /// (sie wird beim Abschluss/Verwerfen/Ablauf freigegeben).</summary>
    public void AttachImport(Session s, int importId, IDisposable inflight)
    {
        lock (s)
        {
            s.ImportId = importId;
            s.Inflight?.Dispose();
            s.Inflight = inflight;
        }
    }

    /// <summary>Vermerkt einen importierten Chunk (Kapitel-Versatz + Zähler).</summary>
    public void NoteChapter(Session s, int chapterOffset, int linesSeen, int imported, int? resultId)
    {
        lock (s)
        {
            s.ChapterOffset = Math.Max(s.ChapterOffset, chapterOffset);
            s.ChaptersDone++;
            s.LinesSeen += linesSeen;
            s.Imported += imported;
            if (resultId is not null) s.ResultId = resultId;
            s.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Entnimmt (und entfernt) die Sitzung zum Abschluss. null, wenn unbekannt/abgelaufen.</summary>
    public Session? Take(int userId, string sessionId) => Remove(Key(userId, sessionId));

    /// <summary>Verwirft eine Sitzung (Abbruch/Fehler) und liefert sie zurück, damit der Aufrufer ihren
    /// Import-Datensatz schließen kann. Das bereits Importierte bleibt — es liegt in der DB.</summary>
    public Session? Discard(int userId, string sessionId) => Remove(Key(userId, sessionId));

    /// <summary>
    /// Entfernt alle Sitzungen ohne Chunk seit <see cref="Ttl"/> und liefert sie zurück. Der Aufrufer (Watchdog)
    /// schließt ihre Import-Datensätze ab — sonst stünde ein Import, dessen Browser mitten im Abruf zugemacht
    /// wurde, für immer auf „läuft": die Inflight-Marke fällt hier weg, und ohne sie hielte der Watchdog ihn
    /// für verwaist und reihte ihn neu ein (auf Prod hängt er dann nur, auf Dev startet ein echter Abruf).
    /// </summary>
    public IReadOnlyList<Session> TakeExpired()
    {
        if (_sessions.IsEmpty) return Array.Empty<Session>();
        var cutoff = DateTime.UtcNow - Ttl;
        var expired = new List<Session>();
        foreach (var kv in _sessions)
            if (kv.Value.UpdatedAt < cutoff && Remove(kv.Key) is { } s)
                expired.Add(s);
        return expired;
    }

    private Session? Remove(string key)
    {
        if (!_sessions.TryRemove(key, out var s)) return null;
        s.Inflight?.Dispose();
        s.Inflight = null;
        return s;
    }

    // Beim Anlegen nur aufräumen — die Import-Datensätze der Abgelaufenen schließt der Watchdog
    // (er hat den DB-Scope; hier gibt es keinen).
    private void PurgeExpired() => TakeExpired();

    /// <summary>Gibt die Inflight-Marken aller offenen Sitzungen frei (Shutdown).</summary>
    public void Dispose()
    {
        foreach (var kv in _sessions) Remove(kv.Key);
        GC.SuppressFinalize(this);
    }
}
