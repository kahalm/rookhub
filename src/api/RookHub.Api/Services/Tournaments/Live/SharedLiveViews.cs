using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Tournaments;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// Geteilte, lebende Live-Ansichten (1.029.0, Tabelle <c>SharedLiveViews</c>, <c>docs/api/live-bretter.md</c> „Geteilte Ansicht“):
/// anlegen (auch ohne Konto — der Browser bekommt einmal einen Bearbeitungsschlüssel), lesen, ändern. Jede Änderung meldet
/// <see cref="LiveViewChanges"/>, damit offene Ströme (<c>…/views/{token}/stream</c>) sofort umhängen.
/// </summary>
public sealed class SharedLiveViews
{
    /// <summary>Länge der Kennung im Link (base62, ~59 bit — nicht zu erraten, kurz genug zum Abtippen).</summary>
    public const int TokenLength = 10;
    /// <summary>Länge des Bearbeitungsschlüssels (base62, ~143 bit).</summary>
    public const int EditKeyLength = 24;
    /// <summary>Der Aufruf zählt höchstens so oft als „zuletzt gesehen“ (ein Schreibzugriff je Zuschauer und Stunde reicht für die Frist).</summary>
    public static readonly TimeSpan TouchEvery = TimeSpan.FromHours(1);

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly LiveViewChanges _changes;

    public SharedLiveViews(AppDbContext db, LiveViewChanges changes)
    {
        _db = db;
        _changes = changes;
    }

    /// <summary>Uhr (Tests setzen eine feste).</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public static bool IsToken(string? s) => s is { Length: TokenLength } && s.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');

    /// <summary>Prüft und deckelt eine Ansicht nach denselben Regeln wie die lange Adresse (Runde 1–999, Texte ≤ 200 Zeichen,
    /// <see cref="LiveView.ParseAdd"/>, <see cref="ExternalRoundRegistry.ParseRefs"/>).</summary>
    public static LiveViewState Normalize(LiveViewState? v)
    {
        v ??= new LiveViewState();
        static string? Text(string? s)
        {
            s = s?.Trim();
            return string.IsNullOrEmpty(s) ? null : s.Length > 200 ? s[..200] : s;
        }
        return new LiveViewState
        {
            Round = v.Round is >= 1 and <= 999 ? v.Round : null,
            Team = Text(v.Team),
            Player = Text(v.Player),
            Add = LiveView.ParseAdd(string.Join(',', v.Add ?? [])),
            Ext = ExternalRoundRegistry.ParseRefs(string.Join(',', v.Ext ?? [])).Select(r => $"{r.RoundId}.{r.GameId}").ToList(),
        };
    }

    public static LiveViewState Parse(string json)
    {
        try { return Normalize(JsonSerializer.Deserialize<LiveViewState>(json, Json)); }
        catch (JsonException) { return new LiveViewState(); }
    }

    private static string Serialize(LiveViewState v) => JsonSerializer.Serialize(v, Json);

    internal static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Darf dieser Aufrufer ändern? Konto des Erstellers ODER der richtige Schlüssel (zeitkonstant verglichen).</summary>
    public static bool CanEdit(SharedLiveView row, int? userId, string? key)
    {
        if (row.OwnerUserId is { } owner && userId == owner) return true;
        if (string.IsNullOrEmpty(key) || key.Length > 64) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(key)), Encoding.ASCII.GetBytes(row.EditKeyHash));
    }

    /// <summary>Neue Ansicht; liefert die Zeile und den Schlüssel im Klartext (nur dieses eine Mal).</summary>
    public async Task<(SharedLiveView Row, string EditKey)> CreateAsync(string tournamentId, LiveViewState? view, int? userId, CancellationToken ct)
    {
        var key = RandomNumberGenerator.GetString(Alphabet, EditKeyLength);
        var now = Clock();
        for (var attempt = 0; ; attempt++)
        {
            var token = RandomNumberGenerator.GetString(Alphabet, TokenLength);
            if (await _db.SharedLiveViews.AnyAsync(v => v.Token == token, ct) && attempt < 5) continue;
            var row = new SharedLiveView
            {
                Token = token, CrawlerTournamentId = tournamentId, ViewJson = Serialize(Normalize(view)), OwnerUserId = userId,
                EditKeyHash = Hash(key), CreatedAt = now, UpdatedAt = now, LastViewedAt = now,
            };
            _db.SharedLiveViews.Add(row);
            await _db.SaveChangesAsync(ct);
            return (row, key);
        }
    }

    /// <summary>Lesen; führt „zuletzt gesehen“ höchstens stündlich nach.</summary>
    public async Task<SharedLiveView?> GetAsync(string token, CancellationToken ct, bool touch = true)
    {
        if (!IsToken(token)) return null;
        var row = await _db.SharedLiveViews.FirstOrDefaultAsync(v => v.Token == token, ct);
        if (row is not null && touch && Clock() - row.LastViewedAt > TouchEvery)
        {
            row.LastViewedAt = Clock();
            await _db.SaveChangesAsync(ct);
        }
        return row;
    }

    /// <summary>Nur lesen, ohne Tracking (der Strom liest bei jeder Änderung neu).</summary>
    public Task<SharedLiveView?> PeekAsync(string token, CancellationToken ct) =>
        IsToken(token) ? _db.SharedLiveViews.AsNoTracking().FirstOrDefaultAsync(v => v.Token == token, ct) : Task.FromResult<SharedLiveView?>(null);

    public enum UpdateResult { Ok, NotFound, Forbidden }

    /// <summary>Ändern (nur Besitzer); meldet die Änderung an alle offenen Ströme dieser Ansicht.</summary>
    public async Task<(UpdateResult Result, SharedLiveView? Row)> UpdateAsync(string token, LiveViewState? view, int? userId, string? key,
        CancellationToken ct)
    {
        var row = await GetAsync(token, ct, touch: false);
        if (row is null) return (UpdateResult.NotFound, null);
        if (!CanEdit(row, userId, key)) return (UpdateResult.Forbidden, row);
        var json = Serialize(Normalize(view));
        if (json != row.ViewJson)
        {
            row.ViewJson = json;
            row.UpdatedAt = row.LastViewedAt = Clock();
            await _db.SaveChangesAsync(ct);
            _changes.Publish(token);
        }
        return (UpdateResult.Ok, row);
    }

    /// <summary>Aufbewahrungsfrist (Aufräumlauf): ohne Aufruf seit <c>Retention:LiveViewDays</c> weg, dazu ein Gesamtdeckel
    /// <c>Retention:LiveViewCap</c> (älteste zuerst) gegen ein Vollschreiben über den offenen Anlege-Weg.</summary>
    public static async Task<int> PruneAsync(AppDbContext db, DateTime now, RetentionOptions options, int chunkSize = RetentionDelete.ChunkSize,
        CancellationToken ct = default)
    {
        var cutoff = now.AddDays(-options.LiveViewDays);
        var removed = await RetentionDelete.AllAsync(db, db.SharedLiveViews.Where(v => v.LastViewedAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.OverCapAsync(db, db.SharedLiveViews, v => v.LastViewedAt, options.LiveViewCap, chunkSize, ct);
        return removed;
    }
}

/// <summary>
/// Benachrichtigt offene Ströme einer geteilten Ansicht, wenn sie sich ändert (Singleton, im Speicher — die API läuft als EINE
/// Instanz; ein zweiter Rechner bräuchte hier einen gemeinsamen Kanal). Je Kennung ein <see cref="CancellationTokenSource"/>,
/// das bei einer Änderung ausgelöst und ersetzt wird; Einträge leben nur, solange jemand zuschaut (Zählung).
/// </summary>
public sealed class LiveViewChanges
{
    internal sealed class Entry
    {
        public CancellationTokenSource Cts = new();
        public int Refs;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Wie viele Kennungen gerade beobachtet werden (Tests, Diagnose).</summary>
    public int Watched { get { lock (_entries) return _entries.Count; } }

    /// <summary>Beobachten. <see cref="Watcher.Current"/> VOR dem Lesen der Ansicht holen, dann geht keine Änderung verloren.</summary>
    public Watcher Watch(string token)
    {
        lock (_entries)
        {
            if (!_entries.TryGetValue(token, out var e)) _entries[token] = e = new Entry();
            e.Refs++;
            return new Watcher(this, token, e);
        }
    }

    /// <summary>Änderung melden: wer gerade wartet, wird geweckt; neue Wartende bekommen ein frisches Zeichen.</summary>
    public void Publish(string token)
    {
        CancellationTokenSource? old;
        lock (_entries)
        {
            if (!_entries.TryGetValue(token, out var e)) return;
            old = e.Cts;
            e.Cts = new CancellationTokenSource();
        }
        old.Cancel();
        old.Dispose();
    }

    private void Release(string token, Entry e)
    {
        lock (_entries)
        {
            if (--e.Refs > 0) return;
            _entries.Remove(token);
            e.Cts.Dispose();
        }
    }

    public sealed class Watcher : IDisposable
    {
        private readonly LiveViewChanges _owner;
        private readonly string _token;
        private readonly Entry _entry;
        private bool _disposed;

        internal Watcher(LiveViewChanges owner, string token, Entry entry) => (_owner, _token, _entry) = (owner, token, entry);

        /// <summary>Wird bei der NÄCHSTEN Änderung ausgelöst.</summary>
        public CancellationToken Current { get { lock (_owner._entries) return _entry.Cts.Token; } }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.Release(_token, _entry);
        }
    }
}
