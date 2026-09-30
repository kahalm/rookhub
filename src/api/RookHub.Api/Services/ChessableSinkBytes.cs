using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Byte-Stand der Chessable-Roh-Senken. Deren Deckel zählen BYTES, nicht Zeilen: ein Zeilendeckel allein ließ in
/// die offene Anon-Senke 200 000 × 256 K Zeichen ≈ 51 GB zu, und die Senken eines Kontos (getReview-Linien,
/// Sitzungszüge, schwierige Züge) hatten gar keinen Mengendeckel. Gezählt wird, was MariaDB ablegt — UTF-8-Bytes
/// (<c>LENGTH</c>, nicht <c>CHAR_LENGTH</c>: sonst zählt ein Drei-Byte-Zeichen als eines).
///
/// <para>Die Summe über LONGTEXT liest jede Zeile. Mit Cache (als Singleton über <see cref="IMemoryCache"/>) wird
/// sie deshalb höchstens alle <see cref="CacheFor"/> neu gezählt und dazwischen um das hochgezählt, was dieser
/// Prozess selbst schreibt. Ohne Cache (Tests) zählt jeder Aufruf neu.</para>
/// </summary>
public sealed class ChessableSinkBytes
{
    /// <summary>Wie lange eine gezählte Summe gilt, bevor sie neu aus der Datenbank kommt.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    /// <summary>Byte-Kontingent je Konto über alle drei Roh-Senken zusammen. Gemessen am 29.09.2026 auf Prod: das
    /// größte Konto hält 19 MB getReview-Linien und 5 MB Sitzungszüge — 200 MiB lassen gut das Achtfache Luft. Vorher
    /// schrieb ein Gratis-Konto bis 12,8 GB Sitzungszüge und unbegrenzt getReview-Linien und schwierige Züge.</summary>
    public const long MaxUserBytes = 200L * 1024 * 1024;

    /// <summary>Aufschlag je Zeile eines Kontos: Schlüssel, Zeitstempel und Indexeinträge kosten auch ohne JSON Platz —
    /// ohne ihn legte ein Konto beliebig viele Zeilen ohne Zug-Detail an (schwierige Züge nur mit nHard).</summary>
    public const int RowOverheadBytes = 128;

    private const string AnonKey = "chessable-sink-bytes:anon";
    private static string UserKey(int userId) => $"chessable-sink-bytes:user:{userId}";
    private readonly IMemoryCache? _cache;

    public ChessableSinkBytes(IMemoryCache? cache = null) => _cache = cache;

    /// <summary>UTF-8-Bytes eines Texts — so viel legt MariaDB (utf8mb4) dafür ab.</summary>
    public static long Utf8(string? s) => string.IsNullOrEmpty(s) ? 0 : Encoding.UTF8.GetByteCount(s);

    /// <summary>Bytes aller JSON-Antworten der Anon-Senke (<see cref="Models.AnonymousChessableReviewLine"/>).</summary>
    public Task<long> AnonTotalAsync(AppDbContext db, CancellationToken ct = default) =>
        GetAsync(AnonKey, () => CountAnonAsync(db, ct));

    /// <summary>Selbst geschriebene Bytes auf die gezählte Summe der Anon-Senke aufschlagen (negativ = Platz frei).</summary>
    public void AddAnon(long delta) => Add(AnonKey, delta);

    /// <summary>Bytes der drei Roh-Senken eines Kontos zusammen, je Zeile mit <see cref="RowOverheadBytes"/>.</summary>
    public Task<long> UserTotalAsync(AppDbContext db, int userId, CancellationToken ct = default) =>
        GetAsync(UserKey(userId), () => CountUserAsync(db, userId, ct));

    /// <summary>Selbst geschriebene Bytes auf die gezählte Summe eines Kontos aufschlagen.</summary>
    public void AddUser(int userId, long delta) => Add(UserKey(userId), delta);

    /// <summary>Buchführung EINES Batches gegen den Byte-Deckel der Anon-Senke.</summary>
    public Budget ForAnon(AppDbContext db, long cap, CancellationToken ct = default) =>
        new(() => AnonTotalAsync(db, ct), AddAnon, cap);

    /// <summary>Buchführung EINES Batches gegen das Kontingent eines Kontos.</summary>
    public Budget ForUser(AppDbContext db, int userId, long cap, CancellationToken ct = default) =>
        new(() => UserTotalAsync(db, userId, ct), delta => AddUser(userId, delta), cap);

    /// <summary>
    /// Buchführung eines Batches gegen einen Byte-Deckel: der Stand wird erst gezählt, wenn ein Eintrag ihn wachsen
    /// ließe (die Summe über LONGTEXT ist teuer), und nach dem Speichern um die verbuchten Bytes fortgeschrieben.
    /// </summary>
    public sealed class Budget
    {
        private readonly Func<Task<long>> _count;
        private readonly Action<long> _record;
        private readonly long _cap;
        private long? _used;
        private long _taken;

        internal Budget(Func<Task<long>> count, Action<long> record, long cap)
        {
            _count = count;
            _record = record;
            _cap = cap;
        }

        /// <summary>Darf ein Eintrag geschrieben werden, der den Stand um <paramref name="delta"/> Bytes ändert?
        /// Schrumpfen und Gleichbleiben immer, Wachsen nur, solange der Deckel hält. Bei <c>true</c> ist er verbucht.</summary>
        public async Task<bool> TryTakeAsync(long delta)
        {
            if (delta > 0)
            {
                var used = _used ??= await _count();
                if (used + delta > _cap) return false;
            }
            if (_used is long known) _used = known + delta;
            _taken += delta;
            return true;
        }

        /// <summary>Nach erfolgreichem Speichern: die verbuchten Bytes auf die gemerkte Summe aufschlagen.</summary>
        public void Commit() => _record(_taken);
    }

    private async Task<long> GetAsync(string key, Func<Task<long>> count)
    {
        if (_cache is not null && _cache.TryGetValue(key, out Tally? cached) && cached is not null)
            return Interlocked.Read(ref cached.Bytes);
        var bytes = await count();
        _cache?.Set(key, new Tally { Bytes = bytes }, CacheFor);
        return bytes;
    }

    private void Add(string key, long delta)
    {
        if (delta != 0 && _cache is not null && _cache.TryGetValue(key, out Tally? cached) && cached is not null)
            Interlocked.Add(ref cached.Bytes, delta);
    }

    private static async Task<long> CountAnonAsync(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational())   // InMemory (Tests) kennt kein LENGTH
            return (await db.AnonymousChessableReviewLines.Select(r => r.Json).ToListAsync(ct)).Sum(Utf8);
        return (await db.Database.SqlQueryRaw<long>(
                "SELECT CAST(COALESCE(SUM(LENGTH(`Json`)), 0) AS SIGNED) AS `Value` FROM `AnonymousChessableReviewLines`")
            .ToListAsync(ct)).Single();
    }

    private static async Task<long> CountUserAsync(AppDbContext db, int userId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())   // InMemory (Tests) kennt kein LENGTH
        {
            var jsons = (await db.ChessableReviewLines.Where(r => r.UserId == userId).Select(r => r.Json).ToListAsync(ct))
                .Concat(await db.ChessableSessionMoves.Where(r => r.UserId == userId).Select(r => r.MovesJson).ToListAsync(ct))
                .Concat(await db.ChessableProblemMoves.Where(r => r.UserId == userId).Select(r => r.ProblemMovesJson).ToListAsync(ct));
            return jsons.Sum(j => Utf8(j) + RowOverheadBytes);
        }
        return (await db.Database.SqlQuery<long>($"""
                SELECT CAST(
                    (SELECT COALESCE(SUM(LENGTH(`Json`)), 0) + COUNT(*) * {RowOverheadBytes}
                       FROM `ChessableReviewLines` WHERE `UserId` = {userId})
                  + (SELECT COALESCE(SUM(LENGTH(`MovesJson`)), 0) + COUNT(*) * {RowOverheadBytes}
                       FROM `ChessableSessionMoves` WHERE `UserId` = {userId})
                  + (SELECT COALESCE(SUM(LENGTH(`ProblemMovesJson`)), 0) + COUNT(*) * {RowOverheadBytes}
                       FROM `ChessableProblemMoves` WHERE `UserId` = {userId})
                  AS SIGNED) AS `Value`
                """)
            .ToListAsync(ct)).Single();
    }

    /// <summary>Veränderliche Summe im Cache — <see cref="Interlocked"/> statt Neu-Setzen, damit parallele
    /// Schreiber einander nichts überschreiben.</summary>
    private sealed class Tally { public long Bytes; }
}
