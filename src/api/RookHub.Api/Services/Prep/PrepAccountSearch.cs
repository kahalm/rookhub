using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Wer gerade sucht (Spielervorbereitung, Phase 4): EINE Suche zur Zeit — die Abrufe gehen über dieselbe Adresse wie der Abgleich
/// von LeagueHub — und höchstens <c>Prep:AccountSearchPerHour</c> je Verwalter und Stunde. Singleton; Tests bauen sich ihr eigenes.
/// </summary>
public sealed class PrepAccountSearchGate
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly Dictionary<int, List<DateTime>> _byUser = new();
    private readonly object _lock = new();

    private List<DateTime> Recent(int userId, DateTime now)
    {
        if (!_byUser.TryGetValue(userId, out var list)) _byUser[userId] = list = new();
        list.RemoveAll(t => t <= now.AddHours(-1));
        return list;
    }

    /// <summary>Wie viele Suchen dieser Verwalter in der laufenden Stunde noch hat.</summary>
    public int Remaining(int userId, int perHour, DateTime now)
    {
        lock (_lock) return Math.Max(0, perHour - Recent(userId, now).Count);
    }

    /// <summary>Eine Suche beginnen → <c>null</c> (los, danach <see cref="Exit"/>) oder der Grund: <c>limit</c> (Stunde aufgebraucht),
    /// <c>busy</c> (es sucht schon jemand).</summary>
    public string? TryEnter(int userId, int perHour, DateTime now)
    {
        lock (_lock)
        {
            var list = Recent(userId, now);
            if (list.Count >= perHour) return "limit";
            if (!_one.Wait(0)) return "busy";
            list.Add(now);
            return null;
        }
    }

    /// <summary>Eine Prüfung (i) beginnen: dieselbe „eine zur Zeit"-Sperre wie die Suche, aber ohne Stunden-Grenze → frei?</summary>
    public bool TryEnterOne() => _one.Wait(0);

    public void Exit() => _one.Release();
}

/// <summary>
/// Online-Konten für einen Spieler des Partiebestands suchen (Spielervorbereitung, Phase 4) — nur mit FIDE-ID, nur auf Knopfdruck,
/// nur mit <c>prep.manage</c> und eingeschaltetem <see cref="EnabledKey"/>. Gesucht wird mit der Konto-Suche von LeagueHub
/// (<see cref="LeagueAccountFinder"/>), Vorschläge und Konten liegen in deren Tabellen (ein Spieler, ein Kontenbestand), die Prüfung (i)
/// ist <see cref="LeagueAccountChecks"/>. Ein Spieler ohne Liga-Bezug wird als <c>Local = false</c> beurteilt: nur seine Föderation
/// zählt als Land, ein Tiroler Ort nicht — strenger, nie großzügiger als bei LeagueHub.
/// <para>Minderjährige (bekannter Jahrgang unter 18, <see cref="LeagueHiddenAccounts"/>): die Suche holt den Jahrgang vor jedem
/// Vorschlag (<see cref="LeagueAccountFinder.ScanRowAsync"/>); hier kommt weder ein Vorschlag noch eine Prüfung eines Minderjährigen
/// heraus, auch nicht für Verwalter — entscheiden kann darüber nur ein Admin in LeagueHub.</para>
/// </summary>
public sealed class PrepAccountSearch(AppDbContext db, LeagueAccountFinder finder, LeagueOnlineAccountService accounts,
    LeagueAccountChecks checks, PrepAccountSearchGate gate, IConfiguration config)
{
    public const string EnabledKey = "Prep:AccountSearch", PerHourKey = "Prep:AccountSearchPerHour";
    public const int DefaultPerHour = 20;

    public static bool IsEnabled(IConfiguration config) => config.GetValue(EnabledKey, false);
    public bool Enabled => IsEnabled(config);
    public int PerHour => Math.Clamp(config.GetValue(PerHourKey, DefaultPerHour), 1, 200);

    /// <summary>Uhr für die Stunden-Grenze (Tests stellen sie).</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    /// <summary>So schreibt die Prüfung (i), dass eine Seite gedrosselt hat (<see cref="LeagueAccountChecks"/>: Profil, Partien) — ein
    /// solches Ergebnis gibt die Spielervorbereitung nicht weiter, sondern meldet <c>rateLimited</c>.</summary>
    public const string ThrottledText = "bremst gerade";

    /// <summary>Der Spieler für Suche und Prüfung: ein Ligaspieler bleibt Ligaspieler (Meldeliste, Tirol), sonst der aus dem Bestand.</summary>
    private async Task<LeagueAccountFinder.Player?> SearchPlayerAsync(string fide, CancellationToken ct) =>
        await LeagueAccountFinder.PlayerAsync(db, fide, ct) ?? await PlayerAsync(db, fide, ct);

    /// <summary>
    /// Ein Spieler des Bestands als Spieler der Konto-Suche — ohne Liga-Bezug (<c>Local = false</c>), Elo = die jüngste aus seinen
    /// Partien (die höchste je, <see cref="PrepPlayer.MaxElo"/>, läge für den Vergleich mit Online-Wertungen meist zu hoch), sonst
    /// <c>MaxElo</c>. <c>null</c> = keiner mit dieser FIDE-ID. Nur der Weg der Spielervorbereitung nimmt ihn — LeagueHubs eigene
    /// Endpunkte kennen weiterhin nur Meldeliste und Liga-Karte.
    /// </summary>
    public static async Task<LeagueAccountFinder.Player?> PlayerAsync(AppDbContext db, string fide, CancellationToken ct)
    {
        var p = await db.PrepPlayers.AsNoTracking().Where(x => x.FideId == fide).OrderByDescending(x => x.Games)
            .Select(x => new { x.Id, x.Name, x.MaxElo }).FirstOrDefaultAsync(ct);
        if (p is null) return null;
        var elo = await LatestEloAsync(db, p.Id, ct) ?? p.MaxElo;
        return new LeagueAccountFinder.Player(fide, p.Name, null, elo, null, Local: false);
    }

    public static async Task<short?> LatestEloAsync(AppDbContext db, int playerId, CancellationToken ct)
    {
        var w = await db.PrepGames.AsNoTracking().Where(g => g.WhiteId == playerId && g.WhiteElo != null)
            .OrderByDescending(g => g.PlayedOn).ThenByDescending(g => g.Id).Select(g => new { g.PlayedOn, Elo = g.WhiteElo }).FirstOrDefaultAsync(ct);
        var b = await db.PrepGames.AsNoTracking().Where(g => g.BlackId == playerId && g.BlackElo != null)
            .OrderByDescending(g => g.PlayedOn).ThenByDescending(g => g.Id).Select(g => new { g.PlayedOn, Elo = g.BlackElo }).FirstOrDefaultAsync(ct);
        if (w is null) return b?.Elo;
        if (b is null) return w.Elo;
        return (w.PlayedOn ?? 0) >= (b.PlayedOn ?? 0) ? w.Elo : b.Elo;
    }

    private async Task<string?> FideAsync(int prepId, CancellationToken ct) =>
        await db.PrepPlayers.AsNoTracking().Where(p => p.Id == prepId).Select(p => p.FideId).FirstOrDefaultAsync(ct);

    /// <summary>Offene Vorschläge des Spielers — ohne die eines Minderjährigen — und wie viele Suchen der Verwalter noch hat.
    /// <c>(null, Grund)</c>: <c>notFound</c>, <c>noFide</c>.</summary>
    public async Task<(JsonObject? Result, string? Reason)> SuggestionsAsync(int prepId, int userId, CancellationToken ct)
    {
        if (!await db.PrepPlayers.AnyAsync(p => p.Id == prepId, ct)) return (null, "notFound");
        return await FideAsync(prepId, ct) is { } fide ? (await ListAsync(fide, userId, ct), null) : (null, "noFide");
    }

    private async Task<JsonObject> ListAsync(string fide, int userId, CancellationToken ct)
    {
        var all = await accounts.SuggestionsAsync(fide, ct, reveal: false, prep: true);
        var items = (all["items"] as JsonArray ?? new JsonArray())
            .Where(i => i?["hidden"]?.GetValue<bool>() != true).Select(i => i!.DeepClone()).ToArray();
        return new JsonObject
        {
            ["items"] = new JsonArray(items), ["perHour"] = PerHour, ["remaining"] = gate.Remaining(userId, PerHour, Now()),
        };
    }

    /// <summary>
    /// Jetzt suchen → die offenen Vorschläge samt <c>found</c>. <c>(null, Grund)</c>: <c>notFound</c>, <c>noFide</c>, <c>busy</c>
    /// (es sucht schon jemand), <c>limit</c> (Stunde aufgebraucht), <c>rateLimited</c> (eine Seite bremst — die Suche endet, kein
    /// zweiter Versuch), <c>unreachable</c>.
    /// </summary>
    public async Task<(JsonObject? Result, string? Reason)> ScanAsync(int prepId, int userId, CancellationToken ct)
    {
        if (!await db.PrepPlayers.AnyAsync(p => p.Id == prepId, ct)) return (null, "notFound");
        if (await FideAsync(prepId, ct) is not { } fide) return (null, "noFide");
        if (await SearchPlayerAsync(fide, ct) is not { } player) return (null, "notFound");
        if (gate.TryEnter(userId, PerHour, Now()) is { } why) return (null, why);
        LeagueAccountFinder.ScanResult r;
        try
        {
            r = await finder.ScanAsync(player, ct);
        }
        catch (LeagueOnlineSync.RateLimitedException)
        {
            db.ChangeTracker.Clear();
            return (null, "rateLimited");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            return (null, "unreachable");
        }
        finally
        {
            gate.Exit();
        }
        var res = await ListAsync(fide, userId, ct);
        res["found"] = r.Found;
        return (res, null);
    }

    /// <summary>Ein offener Vorschlag eines Spielers des Bestands, der kein Minderjähriger ist — sonst <c>null</c> (für den Verwalter
    /// „gibt es nicht").</summary>
    private async Task<LeagueAccountSuggestion?> OwnAsync(int suggestionId, CancellationToken ct)
    {
        var s = await db.LeagueAccountSuggestions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == suggestionId && x.Status == LeagueSuggestionStatus.Open, ct);
        if (s is null || !await db.PrepPlayers.AnyAsync(p => p.FideId == s.FideId, ct)) return null;
        return (await LeagueHiddenAccounts.FidesAsync(db, new[] { s.FideId }, ct)).Contains(s.FideId) ? null : s;
    }

    /// <summary>Übernehmen → das Konto (so, wie die Karte es zeigt); <c>(null, Grund)</c> wie bei LeagueHub, <c>notFound</c> für
    /// fremde, erledigte und verborgene.</summary>
    public async Task<(JsonObject? Account, string? Reason)> AcceptAsync(int suggestionId, bool sure, string? by, CancellationToken ct)
    {
        if (await OwnAsync(suggestionId, ct) is null) return (null, "notFound");
        var (acc, reason) = await accounts.AcceptSuggestionAsync(suggestionId, sure, ct, by, prep: true);
        return acc is null ? (null, reason) : (await accounts.JsonAsync(acc, ct, reveal: false), null);
    }

    public async Task<bool> RejectAsync(int suggestionId, CancellationToken ct) =>
        await OwnAsync(suggestionId, ct) is not null && await accounts.RejectSuggestionAsync(suggestionId, ct, prep: true);

    /// <summary>
    /// Die Prüfung (i) eines Vorschlags — nie mit <c>reveal</c>, mit dem Spieler aus dem Bestand. Sie holt Profil und bis zu 100 Partien,
    /// deshalb durch denselben Türsteher wie die Suche (eine zur Zeit, ohne Stunden-Grenze). <c>(null, Grund)</c>: <c>notFound</c>,
    /// <c>busy</c>, <c>rateLimited</c> (eine Seite hat gedrosselt — kein halbes Ergebnis).
    /// </summary>
    public async Task<(LeagueAccountChecks.Result? Result, string? Reason)> ChecksAsync(int suggestionId, CancellationToken ct)
    {
        if (await OwnAsync(suggestionId, ct) is not { } s) return (null, "notFound");
        if (!gate.TryEnterOne()) return (null, "busy");
        try
        {
            var player = await SearchPlayerAsync(s.FideId, ct);
            if (await checks.ForSuggestionAsync(suggestionId, ct, reveal: false, player) is not { } r) return (null, "notFound");
            return r.Items.Any(i => i.Text.Contains(ThrottledText, StringComparison.Ordinal)) ? (null, "rateLimited") : (r, null);
        }
        finally
        {
            gate.Exit();
        }
    }
}
