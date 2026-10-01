using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Den Partiebestand der Spielervorbereitung paketweise einspielen (<c>POST /api/prep/admin/games</c>, Skript
/// <c>scripts/prep-import.py</c>). Ein Paket ist eine Transaktion samt seiner <see cref="PrepImport"/>-Zeile: ein doppelt
/// geschicktes Paket wird erkannt und nicht noch einmal verbucht, ein abgebrochener Lauf setzt beim ersten fehlenden
/// Paket fort.
///
/// <para><b>Dubletten</b>: dieselbe Partie aus Megabase UND Lumbra ist EINE Zeile mit zwei Quellen-Bits. Gleich ist sie bei
/// gleicher Zugfolge und denselben zwei Spielern (<see cref="PrepPgn.SameGame"/>) — gesucht über <see cref="PrepGame.MovesHash"/>.
/// Nennt die neue Fassung eine FIDE-ID, die gespeicherte aber nicht, wandert die Partie zum Spieler MIT FIDE-ID.</para>
///
/// <para><b>Tempo</b>: Zeile für Zeile über EF wären es einige hundert Partien je Sekunde; relational schreibt der Dienst
/// deshalb Sammel-INSERTs (Spieler und Turniere als <c>INSERT … ON DUPLICATE KEY UPDATE</c>). Unter InMemory (Unit-Tests)
/// läuft dieselbe Logik über den Tracker.</para>
/// </summary>
public sealed class PrepImportService
{
    /// <summary>Mehr Partien nimmt ein Paket nicht an — das Skript schickt 5 000.</summary>
    public const int MaxGamesPerChunk = 20_000;
    /// <summary>Zeilen je INSERT-Anweisung.</summary>
    private const int InsertBatch = 1000;

    private readonly AppDbContext _db;
    private readonly ILogger<PrepImportService>? _log;

    public PrepImportService(AppDbContext db, ILogger<PrepImportService>? log = null)
    {
        _db = db;
        _log = log;
    }

    public sealed record ChunkResult(string Source, int Chunk, long FirstGame, int Read, int Added, int Duplicates, int Discarded,
        IReadOnlyDictionary<string, int> Reasons, int Millis, bool Already);

    /// <summary>Das Paket gab es schon — mit einer anderen ersten Partie (anderer Paketgröße im Skript).</summary>
    public sealed class ChunkMismatchException(string message) : Exception(message);

    /// <summary>Mehr als <see cref="MaxGamesPerChunk"/> Partien.</summary>
    public sealed class ChunkTooLargeException(int read) : Exception($"{read} Partien in einem Paket (höchstens {MaxGamesPerChunk})");

    /// <summary>Ein Paket einspielen. <paramref name="source"/> ist ein Bit aus <see cref="PrepSources"/>.</summary>
    public async Task<ChunkResult> ImportChunkAsync(byte source, int chunk, long firstGame, string pgn, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var sourceName = PrepSources.Name(source);
        var done = await _db.PrepImports.AsNoTracking().FirstOrDefaultAsync(i => i.Source == sourceName && i.Chunk == chunk, ct);
        if (done is not null) return Already(done, firstGame);

        var parsed = PrepPgn.Parse(pgn);
        if (parsed.Read > MaxGamesPerChunk) throw new ChunkTooLargeException(parsed.Read);

        // Dubletten INNERHALB des Pakets (die Megabase führt manche Partie zweimal).
        var unique = new List<PrepPgn.Game>(parsed.Games.Count);
        var byHash = new Dictionary<long, List<PrepPgn.Game>>();
        var duplicates = 0;
        foreach (var g in parsed.Games)
        {
            if (!byHash.TryGetValue(g.MovesHash, out var same)) byHash[g.MovesHash] = same = new();
            if (same.Any(x => PrepPgn.SameGame(x, g))) { duplicates++; continue; }
            same.Add(g);
            unique.Add(g);
        }

        var row = new PrepImport
        {
            Source = sourceName, Chunk = chunk, FirstGame = firstGame, Read = parsed.Read,
            Discarded = parsed.Discarded.Values.Sum(),
            DiscardReasons = parsed.Discarded.Count == 0 ? null : JsonSerializer.Serialize(parsed.Discarded),
            CreatedAt = DateTime.UtcNow,
        };

        if (_db.Database.IsRelational())
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var (added, dups) = await new Sql(_db, tx.GetDbTransaction()).WriteAsync(source, unique, ct);
                row.Added = added;
                row.Duplicates = duplicates + dups;
                row.Millis = (int)sw.ElapsedMilliseconds;
                _db.PrepImports.Add(row);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        else
        {
            var (added, dups) = await WriteTrackedAsync(source, unique, ct);
            row.Added = added;
            row.Duplicates = duplicates + dups;
            row.Millis = (int)sw.ElapsedMilliseconds;
            _db.PrepImports.Add(row);
            await _db.SaveChangesAsync(ct);
        }
        _db.ChangeTracker.Clear();
        _log?.LogInformation("Prep-Import {Source} Paket {Chunk}: {Read} gelesen, {Added} neu, {Duplicates} Dubletten, {Discarded} verworfen in {Millis} ms",
            sourceName, chunk, row.Read, row.Added, row.Duplicates, row.Discarded, row.Millis);
        return ToResult(row, already: false);
    }

    /// <summary>Die eingespielten Pakete einer Quelle (für das Fortsetzen im Skript), aufsteigend.</summary>
    public Task<List<PrepImport>> ImportsAsync(byte source, CancellationToken ct)
    {
        var name = PrepSources.Name(source);
        return _db.PrepImports.AsNoTracking().Where(i => i.Source == name).OrderBy(i => i.Chunk).ToListAsync(ct);
    }

    private static ChunkResult Already(PrepImport done, long firstGame)
    {
        if (done.FirstGame != firstGame)
            throw new ChunkMismatchException(
                $"Paket {done.Chunk} von {done.Source} begann mit Partie {done.FirstGame}, jetzt mit {firstGame} — andere Paketgröße?");
        return ToResult(done, already: true);
    }

    private static ChunkResult ToResult(PrepImport r, bool already) => new(r.Source, r.Chunk, r.FirstGame, r.Read, r.Added,
        r.Duplicates, r.Discarded,
        string.IsNullOrEmpty(r.DiscardReasons) ? new Dictionary<string, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(r.DiscardReasons) ?? new Dictionary<string, int>(),
        r.Millis, already);

    // ── Gemeinsame Entscheidung: welche Partie ist neu, welche eine Dublette einer gespeicherten? ─────────────────────

    /// <summary>Eine gespeicherte Partie mit gleichem Zug-Hash, samt ihren Spielern. Die Seiten sind veränderlich: wandert
    /// eine zum Spieler mit FIDE-ID, vergleicht die nächste Partie desselben Pakets schon gegen den neuen Stand.</summary>
    private sealed class Candidate(long id, long movesHash, short plies, byte sources, int? whiteId, int? blackId,
        PrepPgn.Side? white, PrepPgn.Side? black)
    {
        public long Id { get; } = id;
        public long MovesHash { get; } = movesHash;
        public short Plies { get; } = plies;
        public byte Sources { get; } = sources;
        public int? WhiteId { get; } = whiteId;
        public int? BlackId { get; } = blackId;
        public PrepPgn.Side? White { get; set; } = white;
        public PrepPgn.Side? Black { get; set; } = black;
    }

    /// <summary>Was mit einer Dublette geschieht: Quellen-Bit dazu, eine Seite ggf. zum Spieler mit FIDE-ID.</summary>
    private sealed record DupUpdate(Candidate Existing, PrepPgn.Side? NewWhite, PrepPgn.Side? NewBlack, PrepPgn.Game Game);

    private static PrepPgn.Side? StoredSide(int? id, IReadOnlyDictionary<int, (string Name, string? Fide)> players) =>
        id is { } i && players.TryGetValue(i, out var p)
            ? new PrepPgn.Side(p.Name, PrepPgn.NameKey(p.Name), p.Fide, PrepPgn.Surname(p.Name)) : null;

    /// <summary>Teilt die Partien in neue und Dubletten gespeicherter Partien.</summary>
    private static (List<PrepPgn.Game> Fresh, List<DupUpdate> Dups) Split(byte source, List<PrepPgn.Game> games, List<Candidate> candidates)
    {
        var byHash = candidates.GroupBy(c => c.MovesHash).ToDictionary(g => g.Key, g => g.ToList());
        var fresh = new List<PrepPgn.Game>();
        var dups = new List<DupUpdate>();
        foreach (var g in games)
        {
            var hit = byHash.TryGetValue(g.MovesHash, out var list)
                ? list.FirstOrDefault(c => c.Plies == g.Plies && PrepPgn.SameSide(c.White, g.White) && PrepPgn.SameSide(c.Black, g.Black))
                : null;
            if (hit is null) { fresh.Add(g); continue; }
            // Die Seite wandert nur, wenn die neue Fassung eine FIDE-ID kennt und die gespeicherte nicht — und höchstens
            // einmal: zwei Partien desselben Pakets mit verschiedenen FIDE-IDs fänden sonst beide dieselbe Zeile.
            var w = g.White?.FideId is not null && hit.White is { FideId: null } && hit.WhiteId is not null ? g.White : null;
            var b = g.Black?.FideId is not null && hit.Black is { FideId: null } && hit.BlackId is not null ? g.Black : null;
            if (w is not null) hit.White = w;
            if (b is not null) hit.Black = b;
            dups.Add(new DupUpdate(hit, w, b, g));
        }
        return (fresh, dups);
    }

    /// <summary>Spieler-Zähler eines Pakets: neue Partien (+1) und Partien, die zu einem anderen Spieler wandern.</summary>
    private sealed class PlayerAgg(PrepPgn.Side side)
    {
        public PrepPgn.Side Side { get; } = side;
        public int Games;
        public short? FirstYear, LastYear, MaxElo;

        public void Add(short? year, short? elo)
        {
            Games++;
            if (year is { } y)
            {
                if (FirstYear is null || y < FirstYear) FirstYear = y;
                if (LastYear is null || y > LastYear) LastYear = y;
            }
            if (elo is { } e && (MaxElo is null || e > MaxElo)) MaxElo = e;
        }
    }

    private static Dictionary<long, PlayerAgg> Aggregate(List<PrepPgn.Game> fresh, List<DupUpdate> dups)
    {
        var aggs = new Dictionary<long, PlayerAgg>();
        void Add(PrepPgn.Side? s, short? year, short? elo)
        {
            if (s is null) return;
            if (!aggs.TryGetValue(s.KeyHash, out var a)) aggs[s.KeyHash] = a = new PlayerAgg(s);
            a.Add(year, elo);
        }
        foreach (var g in fresh)
        {
            Add(g.White, g.Year, g.WhiteElo);
            Add(g.Black, g.Year, g.BlackElo);
        }
        foreach (var d in dups)
        {
            Add(d.NewWhite, d.Game.Year, d.Game.WhiteElo);
            Add(d.NewBlack, d.Game.Year, d.Game.BlackElo);
        }
        return aggs;
    }

    /// <summary>Wie viele Partien jeder Spieler abgibt, dessen Seite zu einem Spieler mit FIDE-ID wandert.</summary>
    private static Dictionary<int, int> Moved(List<DupUpdate> dups)
    {
        var lost = new Dictionary<int, int>();
        foreach (var d in dups)
        {
            if (d.NewWhite is not null && d.Existing.WhiteId is { } w) lost[w] = lost.GetValueOrDefault(w) + 1;
            if (d.NewBlack is not null && d.Existing.BlackId is { } b) lost[b] = lost.GetValueOrDefault(b) + 1;
        }
        return lost;
    }

    // ── InMemory (Unit-Tests): dieselbe Logik über den Tracker ─────────────────────────────────────────────────────

    private async Task<(int Added, int Duplicates)> WriteTrackedAsync(byte source, List<PrepPgn.Game> games, CancellationToken ct)
    {
        var hashes = games.Select(g => g.MovesHash).Distinct().ToList();
        var stored = await _db.PrepGames.Where(g => hashes.Contains(g.MovesHash)).ToListAsync(ct);
        var ids = stored.SelectMany(g => new[] { g.WhiteId, g.BlackId }).OfType<int>().Distinct().ToList();
        var players = await _db.PrepPlayers.Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.Name, p.FideId), ct);
        var candidates = stored.Select(g => new Candidate(g.Id, g.MovesHash, g.Plies, g.Sources, g.WhiteId, g.BlackId,
            StoredSide(g.WhiteId, players), StoredSide(g.BlackId, players))).ToList();
        var (fresh, dups) = Split(source, games, candidates);

        var aggs = Aggregate(fresh, dups);
        var keys = aggs.Keys.ToList();
        var existing = await _db.PrepPlayers.Where(p => keys.Contains(p.KeyHash)).ToDictionaryAsync(p => p.KeyHash, ct);
        foreach (var (hash, a) in aggs)
        {
            if (!existing.TryGetValue(hash, out var p))
            {
                existing[hash] = p = new PrepPlayer
                {
                    Name = a.Side.Name, NameKey = a.Side.NameKey, FideId = a.Side.FideId, KeyHash = hash,
                };
                _db.PrepPlayers.Add(p);
            }
            p.Games += a.Games;
            p.FirstYear = Min(p.FirstYear, a.FirstYear);
            p.LastYear = Max(p.LastYear, a.LastYear);
            p.MaxElo = Max(p.MaxElo, a.MaxElo);
        }
        foreach (var (id, n) in Moved(dups))
        {
            var p = await _db.PrepPlayers.FindAsync(new object[] { id }, ct);
            if (p is not null) p.Games = Math.Max(0, p.Games - n);
        }

        var eventHashes = fresh.Where(g => g.HasEvent).Select(g => g.EventHash).Distinct().ToList();
        var events = await _db.PrepEvents.Where(e => eventHashes.Contains(e.KeyHash)).ToDictionaryAsync(e => e.KeyHash, ct);
        foreach (var g in fresh.Where(g => g.HasEvent))
            if (!events.ContainsKey(g.EventHash))
            {
                var e = new PrepEvent { Name = g.Event ?? "", Site = g.Site, KeyHash = g.EventHash };
                events[g.EventHash] = e;
                _db.PrepEvents.Add(e);
            }
        await _db.SaveChangesAsync(ct);   // Ids der neuen Spieler und Turniere

        foreach (var g in fresh)
            _db.PrepGames.Add(NewRow(source, g, s => existing[s.KeyHash].Id, h => events[h].Id));
        var tracked = stored.ToDictionary(g => g.Id);
        foreach (var d in dups)
        {
            var row = tracked[d.Existing.Id];
            row.Sources |= source;
            if (d.NewWhite is not null) row.WhiteId = existing[d.NewWhite.KeyHash].Id;
            if (d.NewBlack is not null) row.BlackId = existing[d.NewBlack.KeyHash].Id;
        }
        await _db.SaveChangesAsync(ct);
        return (fresh.Count, dups.Count);
    }

    private static short? Min(short? a, short? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
    private static short? Max(short? a, short? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static PrepGame NewRow(byte source, PrepPgn.Game g, Func<PrepPgn.Side, int> player, Func<long, int> evt) => new()
    {
        WhiteId = g.White is null ? null : player(g.White),
        BlackId = g.Black is null ? null : player(g.Black),
        WhiteElo = g.WhiteElo, BlackElo = g.BlackElo, Result = g.Result, PlayedOn = g.PlayedOn,
        EventId = g.HasEvent ? evt(g.EventHash) : null, Round = g.Round, Eco = g.Eco,
        Plies = g.Plies, Moves = g.Moves, MovesHash = g.MovesHash, Sources = source,
    };

    // ── Relational: Sammel-SQL in der Transaktion des Kontexts ─────────────────────────────────────────────────────

    /// <summary>Die SQL-Seite. Zahlen stehen direkt im Text (aus <c>long</c>/<c>int</c> formatiert, also ohne
    /// Einschleus-Gefahr), alle Texte als Parameter.</summary>
    private sealed class Sql(AppDbContext db, DbTransaction tx)
    {
        private static string N(long? v) => v is { } x ? x.ToString(CultureInfo.InvariantCulture) : "NULL";

        private DbCommand Command(string sql)
        {
            var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.CommandTimeout = 600;
            return cmd;
        }

        private static string Param(DbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
            return name;
        }

        public async Task<(int Added, int Duplicates)> WriteAsync(byte source, List<PrepPgn.Game> games, CancellationToken ct)
        {
            var candidates = await CandidatesAsync(games.Select(g => g.MovesHash).Distinct().ToList(), ct);
            var (fresh, dups) = Split(source, games, candidates);
            var aggs = Aggregate(fresh, dups);
            var players = await UpsertPlayersAsync(aggs, ct);
            await ReleaseAsync(Moved(dups), ct);
            var events = await UpsertEventsAsync(fresh.Where(g => g.HasEvent)
                .GroupBy(g => g.EventHash).Select(g => g.First()).ToList(), ct);
            await InsertGamesAsync(source, fresh, players, events, ct);
            await UpdateDuplicatesAsync(source, dups, players, ct);
            return (fresh.Count, dups.Count);
        }

        private async Task<List<Candidate>> CandidatesAsync(List<long> hashes, CancellationToken ct)
        {
            var rows = new List<(long Id, long Hash, short Plies, byte Sources, int? W, int? B)>();
            foreach (var part in hashes.Chunk(2000))
            {
                await using var cmd = Command("SELECT Id, MovesHash, Plies, Sources, WhiteId, BlackId FROM PrepGames WHERE MovesHash IN ("
                    + string.Join(',', part.Select(x => N(x))) + ")");
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    rows.Add((r.GetInt64(0), r.GetInt64(1), r.GetInt16(2), r.GetByte(3),
                        r.IsDBNull(4) ? null : r.GetInt32(4), r.IsDBNull(5) ? null : r.GetInt32(5)));
            }
            if (rows.Count == 0) return new();
            var ids = rows.SelectMany(x => new[] { x.W, x.B }).OfType<int>().Distinct().ToList();
            var players = new Dictionary<int, (string Name, string? Fide)>();
            foreach (var part in ids.Chunk(2000))
            {
                await using var cmd = Command("SELECT Id, Name, FideId FROM PrepPlayers WHERE Id IN ("
                    + string.Join(',', part.Select(x => N(x))) + ")");
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    players[r.GetInt32(0)] = (r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
            }
            return rows.Select(x => new Candidate(x.Id, x.Hash, x.Plies, x.Sources, x.W, x.B,
                StoredSide(x.W, players), StoredSide(x.B, players))).ToList();
        }

        /// <summary>Spieler anlegen bzw. ihre Zähler fortschreiben, in EINER Anweisung je Portion → KeyHash → Id.</summary>
        private async Task<Dictionary<long, int>> UpsertPlayersAsync(Dictionary<long, PlayerAgg> aggs, CancellationToken ct)
        {
            foreach (var part in aggs.Values.Chunk(InsertBatch))
            {
                await using var cmd = Command("");
                var sb = new StringBuilder("INSERT INTO PrepPlayers (Name, NameKey, FideId, KeyHash, Games, FirstYear, LastYear, MaxElo) VALUES ");
                for (var i = 0; i < part.Length; i++)
                {
                    var a = part[i];
                    if (i > 0) sb.Append(',');
                    sb.Append('(').Append(Param(cmd, "@n" + i, a.Side.Name)).Append(',').Append(Param(cmd, "@k" + i, a.Side.NameKey))
                        .Append(',').Append(Param(cmd, "@f" + i, a.Side.FideId)).Append(',').Append(N(a.Side.KeyHash))
                        .Append(',').Append(N(a.Games)).Append(',').Append(N(a.FirstYear)).Append(',').Append(N(a.LastYear))
                        .Append(',').Append(N(a.MaxElo)).Append(')');
                }
                sb.Append(" ON DUPLICATE KEY UPDATE Games = Games + VALUES(Games),"
                    + " FirstYear = LEAST(COALESCE(FirstYear, VALUES(FirstYear)), COALESCE(VALUES(FirstYear), FirstYear)),"
                    + " LastYear = GREATEST(COALESCE(LastYear, VALUES(LastYear)), COALESCE(VALUES(LastYear), LastYear)),"
                    + " MaxElo = GREATEST(COALESCE(MaxElo, VALUES(MaxElo)), COALESCE(VALUES(MaxElo), MaxElo))");
                cmd.CommandText = sb.ToString();
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return await IdsAsync("PrepPlayers", aggs.Keys, ct);
        }

        private async Task ReleaseAsync(Dictionary<int, int> lost, CancellationToken ct)
        {
            if (lost.Count == 0) return;
            foreach (var part in lost.Chunk(InsertBatch))
            {
                await using var cmd = Command(string.Concat(part.Select(x =>
                    $"UPDATE PrepPlayers SET Games = GREATEST(Games - {N(x.Value)}, 0) WHERE Id = {N(x.Key)};")));
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        private async Task<Dictionary<long, int>> UpsertEventsAsync(List<PrepPgn.Game> games, CancellationToken ct)
        {
            foreach (var part in games.Chunk(InsertBatch))
            {
                await using var cmd = Command("");
                var sb = new StringBuilder("INSERT IGNORE INTO PrepEvents (Name, Site, KeyHash) VALUES ");
                for (var i = 0; i < part.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('(').Append(Param(cmd, "@e" + i, part[i].Event ?? "")).Append(',').Append(Param(cmd, "@s" + i, part[i].Site))
                        .Append(',').Append(N(part[i].EventHash)).Append(')');
                }
                cmd.CommandText = sb.ToString();
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return await IdsAsync("PrepEvents", games.Select(g => g.EventHash), ct);
        }

        private async Task<Dictionary<long, int>> IdsAsync(string table, IEnumerable<long> keyHashes, CancellationToken ct)
        {
            var ids = new Dictionary<long, int>();
            foreach (var part in keyHashes.Distinct().Chunk(2000))
            {
                await using var cmd = Command($"SELECT KeyHash, Id FROM {table} WHERE KeyHash IN (" + string.Join(',', part.Select(x => N(x))) + ")");
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) ids[r.GetInt64(0)] = r.GetInt32(1);
            }
            return ids;
        }

        private async Task InsertGamesAsync(byte source, List<PrepPgn.Game> games, Dictionary<long, int> players,
            Dictionary<long, int> events, CancellationToken ct)
        {
            foreach (var part in games.Chunk(InsertBatch))
            {
                await using var cmd = Command("");
                var sb = new StringBuilder("INSERT INTO PrepGames (WhiteId, BlackId, WhiteElo, BlackElo, Result, PlayedOn, EventId, Round, Eco, Plies, Moves, MovesHash, Sources) VALUES ");
                for (var i = 0; i < part.Length; i++)
                {
                    var g = part[i];
                    if (i > 0) sb.Append(',');
                    sb.Append('(')
                        .Append(N(g.White is null ? null : players[g.White.KeyHash])).Append(',')
                        .Append(N(g.Black is null ? null : players[g.Black.KeyHash])).Append(',')
                        .Append(N(g.WhiteElo)).Append(',').Append(N(g.BlackElo)).Append(',')
                        .Append(N(g.Result)).Append(',').Append(N(g.PlayedOn)).Append(',')
                        .Append(N(g.HasEvent ? events[g.EventHash] : null)).Append(',')
                        .Append(Param(cmd, "@r" + i, g.Round)).Append(',').Append(Param(cmd, "@c" + i, g.Eco)).Append(',')
                        .Append(N(g.Plies)).Append(',').Append(Param(cmd, "@m" + i, g.Moves)).Append(',')
                        .Append(N(g.MovesHash)).Append(',').Append(N(source)).Append(')');
                }
                cmd.CommandText = sb.ToString();
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        private async Task UpdateDuplicatesAsync(byte source, List<DupUpdate> dups, Dictionary<long, int> players, CancellationToken ct)
        {
            // Nur das Quellen-Bit: eine Anweisung je Portion. Mit wandernder Seite: je Partie eine.
            var plain = dups.Where(d => d.NewWhite is null && d.NewBlack is null && (d.Existing.Sources & source) == 0)
                .Select(d => d.Existing.Id).Distinct().ToList();
            foreach (var part in plain.Chunk(2000))
            {
                await using var cmd = Command($"UPDATE PrepGames SET Sources = Sources | {N(source)} WHERE Id IN ("
                    + string.Join(',', part.Select(x => N(x))) + ")");
                await cmd.ExecuteNonQueryAsync(ct);
            }
            var moved = dups.Where(d => d.NewWhite is not null || d.NewBlack is not null).ToList();
            foreach (var part in moved.Chunk(500))
            {
                await using var cmd = Command(string.Concat(part.Select(d =>
                {
                    var set = new StringBuilder($"UPDATE PrepGames SET Sources = Sources | {N(source)}");
                    if (d.NewWhite is not null) set.Append(", WhiteId = ").Append(N(players[d.NewWhite.KeyHash]));
                    if (d.NewBlack is not null) set.Append(", BlackId = ").Append(N(players[d.NewBlack.KeyHash]));
                    return set.Append(" WHERE Id = ").Append(N(d.Existing.Id)).Append(';').ToString();
                })));
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
