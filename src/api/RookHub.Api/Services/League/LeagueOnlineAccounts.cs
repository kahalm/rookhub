using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die Seiten, auf denen Online-Konten liegen können (0.605.0). Heute Lichess und chess.com — eine weitere Seite braucht
/// hier einen Eintrag (Kürzel, Namensregel, Profiladresse) und in <see cref="LeagueOnlineSync"/> einen Abruf.
/// </summary>
public static partial class LeagueOnlineSites
{
    public const string Lichess = "lichess";
    public const string ChessCom = "chess.com";
    public static readonly string[] All = { Lichess, ChessCom };

    /// <summary>„lichess.org", „Lichess", „chesscom" … → das Kürzel, sonst <c>null</c>.</summary>
    public static string? Normalize(string? site) => (site ?? "").Trim().ToLowerInvariant() switch
    {
        "lichess" or "lichess.org" => Lichess,
        "chess.com" or "chesscom" or "chess com" => ChessCom,
        _ => null,
    };

    /// <summary>
    /// Seite + Name aus der Eingabe: ein Name oder die kopierte Profiladresse („https://lichess.org/@/Name",
    /// „https://www.chess.com/member/name") — die Adresse schlägt die gewählte Seite. <c>null</c> = kein gültiger Name.
    /// </summary>
    public static (string Site, string User)? Parse(string? site, string? input)
    {
        var text = (input ?? "").Trim().TrimEnd('/');
        var s = Normalize(site);
        var m = LichessUrl().Match(text);
        if (m.Success) { s = Lichess; text = m.Groups[1].Value; }
        else if ((m = ChessComUrl().Match(text)).Success) { s = ChessCom; text = m.Groups[1].Value; }
        text = text.TrimStart('@');
        if (s is null) return null;
        var ok = s == Lichess ? LichessName().IsMatch(text) : ChessComName().IsMatch(text);
        return ok ? (s, text) : null;
    }

    public static string ProfileUrl(string site, string user) => site == Lichess
        ? $"https://lichess.org/@/{Uri.EscapeDataString(user)}"
        : $"https://www.chess.com/member/{Uri.EscapeDataString(user)}";

    [GeneratedRegex(@"lichess\.org/@/([^/?#\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LichessUrl();
    [GeneratedRegex(@"chess\.com/(?:member|players|stats/[a-z]+/chess)/([^/?#\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ChessComUrl();
    /// <summary>Lichess: 2–30 Zeichen, Buchstaben, Ziffern, „_" und „-", beginnt mit Buchstabe oder Ziffer.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{1,29}$")]
    private static partial Regex LichessName();
    /// <summary>chess.com: 3–25 Zeichen, sonst wie Lichess.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{2,24}$")]
    private static partial Regex ChessComName();
}

/// <summary>
/// Online-Konten eines Ligaspielers pflegen (0.605.0, Wunsch 2026-09-30: „für einen User kann es eine Liste von
/// Onlinekonten geben — Name + Seite, gesichert oder unsicher, dazu Kommentare"). Gilt je FIDE-ID wie die Spielerkarte.
/// Nach jeder Änderung werden die fertigen Liga-Ansichten nachgezogen (die Meldeliste einer Begegnung zeigt die Konten) und
/// der Abruf geweckt.
/// </summary>
public sealed class LeagueOnlineAccountService(AppDbContext db, LeagueOnlineSyncSignal? signal = null)
{
    public const string Sure = "sicher";
    public const string Unsure = "wahrscheinlich";
    public const int MaxCommentLength = 1000;
    /// <summary>Mehr als eine Handvoll Konten je Spieler ist ein Irrtum, keine Recherche.</summary>
    public const int MaxPerPlayer = 20;

    public sealed record Input(string? Site, string? User, bool? Sure, string? Comment);

    /// <summary>Ein Konto als JSON der Spielerkarte. <paramref name="full"/> = mit Kommentar und Abrufstand (angemeldet);
    /// über einen Teilen-Link nur das, was schon vorher sichtbar war. <paramref name="hidden"/> = Konto eines Minderjährigen
    /// (<see cref="LeagueHiddenAccounts"/>): weder Seite noch Name, Adresse oder Kommentar — nur, DASS es eins gibt, und der
    /// Stand des Abrufs.</summary>
    public static JsonObject ToJson(LeagueOnlineAccount a, bool full, bool hidden = false)
    {
        var o = hidden
            ? new JsonObject { ["hidden"] = true, ["site"] = null, ["user"] = null, ["url"] = null, ["conf"] = a.Confidence }
            : new JsonObject { ["site"] = a.Site, ["user"] = a.UserName, ["url"] = a.Url, ["conf"] = a.Confidence };
        if (!full) return o;
        o["id"] = a.Id;
        o["comment"] = hidden ? null : a.Evidence;
        o["games"] = a.GameCount;
        o["syncedAt"] = a.SyncedAt is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("O") : null;
        o["error"] = a.SyncError;
        return o;
    }

    public async Task<(LeagueOnlineAccount? Account, string? Reason)> CreateAsync(string fide, Input req, CancellationToken ct)
    {
        fide = (fide ?? "").Trim();
        if (!await KnownPlayerAsync(fide, ct)) return (null, "unknownPlayer");
        if (LeagueOnlineSites.Parse(req.Site, req.User) is not { } parsed)
            return (null, LeagueOnlineSites.Normalize(req.Site) is null && !LooksLikeUrl(req.User) ? "invalidSite" : "invalidUser");
        var mine = await db.LeagueOnlineAccounts.Where(a => a.FideId == fide).ToListAsync(ct);
        if (mine.Count >= MaxPerPlayer) return (null, "tooMany");
        if (mine.Any(a => Same(a, parsed.Site, parsed.User))) return (null, "duplicate");
        var acc = new LeagueOnlineAccount
        {
            FideId = fide, Site = parsed.Site, UserName = parsed.User, Url = LeagueOnlineSites.ProfileUrl(parsed.Site, parsed.User),
            Confidence = req.Sure == true ? Sure : Unsure, Evidence = Comment(req.Comment), Manual = true, UpdatedAt = DateTime.UtcNow,
        };
        db.LeagueOnlineAccounts.Add(acc);
        // Ein Vorschlag für genau dieses Konto ist damit erledigt (auch wenn es von Hand eingetragen wurde).
        var user = parsed.User.ToLower();
        db.LeagueAccountSuggestions.RemoveRange(await db.LeagueAccountSuggestions
            .Where(x => x.FideId == fide && x.Site == parsed.Site && x.UserName.ToLower() == user).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        await PatchViewsAsync(fide, ct);
        signal?.Wake();
        return (acc, null);
    }

    /// <summary>Ändern — fehlende Felder bleiben. Ein anderer Name oder eine andere Seite ist ein anderes Konto: die schon
    /// geholten Partien gehen, der Abruf beginnt von vorn.</summary>
    public async Task<(LeagueOnlineAccount? Account, string? Reason)> UpdateAsync(int id, Input req, CancellationToken ct)
    {
        var acc = await db.LeagueOnlineAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (acc is null) return (null, "notFound");
        if (req.Site is not null || req.User is not null)
        {
            if (LeagueOnlineSites.Parse(req.Site ?? acc.Site, req.User ?? acc.UserName) is not { } parsed) return (null, "invalidUser");
            if (!Same(acc, parsed.Site, parsed.User))
            {
                if (await db.LeagueOnlineAccounts.AnyAsync(a => a.FideId == acc.FideId && a.Id != acc.Id && a.Site == parsed.Site
                        && a.UserName.ToLower() == parsed.User.ToLower(), ct))
                    return (null, "duplicate");
                await DeleteGamesAsync(acc.Id, ct);
                acc.Site = parsed.Site;
                acc.UserName = parsed.User;
                acc.Url = LeagueOnlineSites.ProfileUrl(parsed.Site, parsed.User);
                acc.SyncedAt = null;
                acc.SyncCursor = 0;
                acc.SyncError = null;
                acc.GameCount = 0;
            }
            else if (acc.UserName != parsed.User) acc.UserName = parsed.User;       // nur die Schreibweise
        }
        if (req.Sure is { } sure) acc.Confidence = sure ? Sure : Unsure;
        if (req.Comment is not null) acc.Evidence = Comment(req.Comment);
        acc.Manual = true;
        acc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await PatchViewsAsync(acc.FideId, ct);
        if (acc.SyncedAt is null) signal?.Wake();
        return (acc, null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var acc = await db.LeagueOnlineAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (acc is null) return false;
        await DeleteGamesAsync(acc.Id, ct);                                  // InMemory kaskadiert nicht
        db.LeagueOnlineAccounts.Remove(acc);
        // Entfernt = gehört nicht zu diesem Spieler: die Konto-Suche (0.607.0) soll es nicht wieder vorschlagen.
        var lower = acc.UserName.ToLower();
        var sugg = await db.LeagueAccountSuggestions.FirstOrDefaultAsync(x => x.FideId == acc.FideId && x.Site == acc.Site
            && x.UserName.ToLower() == lower, ct);
        if (sugg is null)
            db.LeagueAccountSuggestions.Add(sugg = new LeagueAccountSuggestion
            {
                FideId = acc.FideId, Site = acc.Site, UserName = acc.UserName, Url = acc.Url, Evidence = "Konto entfernt",
                CreatedAt = DateTime.UtcNow,
            });
        sugg.Status = LeagueSuggestionStatus.Rejected;
        sugg.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await PatchViewsAsync(acc.FideId, ct);
        return true;
    }

    /// <summary>Ein Konto erneut abrufen lassen (nach einem Fehler oder „jetzt holen").</summary>
    public async Task<LeagueOnlineAccount?> RequestSyncAsync(int id, CancellationToken ct)
    {
        var acc = await db.LeagueOnlineAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (acc is null) return null;
        acc.SyncedAt = null;
        acc.SyncError = null;
        await db.SaveChangesAsync(ct);
        signal?.Wake();
        return acc;
    }

    // ── Vorschläge der Konto-Suche (0.607.0) ────────────────────────────────────────────────────

    /// <summary>Ein Vorschlag als JSON; <paramref name="names"/> = Name und Mannschaft je FIDE-ID (für die Übersicht).
    /// <paramref name="hidden"/> = Minderjähriger: ohne Seite, Name, Adresse und Profilangaben — entschieden wird nach den Hinweisen.</summary>
    public static JsonObject SuggestionJson(LeagueAccountSuggestion x, IReadOnlyDictionary<string, (string Name, string? Team)>? names = null,
        bool hidden = false)
    {
        var o = hidden
            ? new JsonObject
            {
                ["id"] = x.Id, ["fide"] = x.FideId, ["hidden"] = true, ["site"] = null, ["user"] = null, ["url"] = null, ["score"] = x.Score,
                ["evidence"] = x.Evidence, ["profileName"] = null, ["location"] = null, ["lastActive"] = null,
            }
            : new JsonObject
            {
                ["id"] = x.Id, ["fide"] = x.FideId, ["site"] = x.Site, ["user"] = x.UserName, ["url"] = x.Url, ["score"] = x.Score,
                ["evidence"] = x.Evidence, ["profileName"] = x.ProfileName, ["location"] = x.Location,
                ["lastActive"] = x.LastActive is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("O") : null,
            };
        if (names is not null && names.TryGetValue(x.FideId, out var n)) { o["name"] = n.Name; o["team"] = n.Team; }
        return o;
    }

    /// <summary>
    /// Offene Vorschläge — eines Spielers oder aller (<paramref name="fide"/> leer), stärkste zuerst; ohne die, zu denen es
    /// das Konto inzwischen gibt. Für die Übersicht dazu der Stand der Suche: abgesucht / Spieler der laufenden Saison.
    /// </summary>
    public async Task<JsonObject> SuggestionsAsync(string? fide, CancellationToken ct)
    {
        var q = db.LeagueAccountSuggestions.AsNoTracking().Where(x => x.Status == LeagueSuggestionStatus.Open);
        if (!string.IsNullOrEmpty(fide)) q = q.Where(x => x.FideId == fide);
        var list = await q.OrderByDescending(x => x.Score).ThenBy(x => x.FideId).ThenBy(x => x.Id).ToListAsync(ct);
        var fides = list.Select(x => x.FideId).Distinct().ToList();
        var taken = (await db.LeagueOnlineAccounts.AsNoTracking().Where(a => fides.Contains(a.FideId))
            .Select(a => new { a.FideId, a.Site, a.UserName }).ToListAsync(ct))
            .Select(a => $"{a.FideId}|{a.Site}|{a.UserName.ToLowerInvariant()}").ToHashSet();
        list = list.Where(x => !taken.Contains($"{x.FideId}|{x.Site}|{x.UserName.ToLowerInvariant()}")).ToList();

        var names = (await (from p in db.LeaguePlayers.AsNoTracking()
                            join t in db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                            where p.FideId != null && fides.Contains(p.FideId)
                            orderby t.Season descending
                            select new { p.FideId, p.Name, p.Team }).ToListAsync(ct))
            .GroupBy(x => x.FideId!).ToDictionary(g => g.Key, g => (g.First().Name, (string?)g.First().Team));
        var hidden = await LeagueHiddenAccounts.FidesAsync(db, fides, ct);
        var res = new JsonObject { ["items"] = new JsonArray(list.Select(x => (JsonNode)SuggestionJson(x, names, hidden.Contains(x.FideId))).ToArray()) };
        if (string.IsNullOrEmpty(fide))
        {
            var season = await db.LeagueTournaments.AsNoTracking().MaxAsync(t => (string?)t.Season, ct);
            var current = await (from p in db.LeaguePlayers.AsNoTracking()
                                 join t in db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                                 where t.Season == season && p.FideId != null && p.FideId != ""
                                 select p.FideId!).Distinct().ToListAsync(ct);
            var due = DateTime.UtcNow.AddDays(-LeagueAccountFinder.RescanDays);
            res["total"] = current.Count;
            res["scanned"] = await db.LeagueAccountScans.AsNoTracking().CountAsync(x => current.Contains(x.FideId) && x.ScannedAt >= due, ct);
        }
        return res;
    }

    /// <summary>Vorschlag übernehmen → ein Konto („gesichert" oder „unsicher"), die Hinweise werden der Kommentar.</summary>
    public async Task<(LeagueOnlineAccount? Account, string? Reason)> AcceptSuggestionAsync(int id, bool sure, CancellationToken ct)
    {
        var x = await db.LeagueAccountSuggestions.FirstOrDefaultAsync(s => s.Id == id && s.Status == LeagueSuggestionStatus.Open, ct);
        if (x is null) return (null, "notFound");
        var r = await CreateAsync(x.FideId, new Input(x.Site, x.UserName, sure, "Vorschlag der Konto-Suche: " + x.Evidence), ct);
        if (r.Reason == "duplicate")
        {
            db.LeagueAccountSuggestions.Remove(x);                           // das Konto gibt es schon — Vorschlag erledigt
            await db.SaveChangesAsync(ct);
        }
        return r;
    }

    /// <summary>Vorschlag verwerfen — er bleibt als verworfen stehen und kommt bei der nächsten Suche nicht wieder.</summary>
    public async Task<bool> RejectSuggestionAsync(int id, CancellationToken ct)
    {
        var x = await db.LeagueAccountSuggestions.FirstOrDefaultAsync(s => s.Id == id && s.Status == LeagueSuggestionStatus.Open, ct);
        if (x is null) return false;
        x.Status = LeagueSuggestionStatus.Rejected;
        x.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Die Konten in den fertigen Ansichten (<c>roster[].acc</c>) nachziehen — sonst stünden sie in der Meldeliste
    /// erst nach dem nächsten „Daten aktualisieren".</summary>
    public async Task PatchViewsAsync(string fide, CancellationToken ct)
    {
        // Konten Minderjähriger stehen nie in der Meldeliste (die sieht jeder mit Leserecht und jeder Teilen-Link).
        var hidden = (await LeagueHiddenAccounts.FidesAsync(db, new[] { fide }, ct)).Contains(fide);
        var acc = new JsonArray(hidden ? Array.Empty<JsonNode>()
            : (await db.LeagueOnlineAccounts.AsNoTracking().Where(a => a.FideId == fide).OrderBy(a => a.Id).ToListAsync(ct))
                .Select(a => (JsonNode)ToJson(a, full: false)).ToArray());
        foreach (var view in await db.LeagueViews.ToListAsync(ct))
        {
            if (!view.Json.Contains($"\"{fide}\"", StringComparison.Ordinal)) continue;
            if (JsonNode.Parse(view.Json) is not JsonObject root || root["fixtures"] is not JsonObject teams) continue;
            var changed = false;
            foreach (var (_, rounds) in teams)
                foreach (var (_, fx) in rounds?.AsObject() ?? new JsonObject())
                    foreach (var r in fx?["roster"] as JsonArray ?? new JsonArray())
                    {
                        if (r?["fide"]?.GetValue<string>() != fide) continue;
                        r["acc"] = acc.DeepClone();
                        changed = true;
                    }
            if (changed) view.Json = root.ToJsonString();
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ein Konto als JSON für die Antwort an den Verwalter — verborgen, wenn es einem Minderjährigen gehört.</summary>
    public async Task<JsonObject> JsonAsync(LeagueOnlineAccount a, CancellationToken ct) =>
        ToJson(a, full: true, hidden: (await LeagueHiddenAccounts.FidesAsync(db, new[] { a.FideId }, ct)).Contains(a.FideId));

    private async Task DeleteGamesAsync(int accountId, CancellationToken ct)
    {
        if (db.Database.IsRelational()) await db.LeagueOnlineGames.Where(g => g.AccountId == accountId).ExecuteDeleteAsync(ct);
        else db.LeagueOnlineGames.RemoveRange(await db.LeagueOnlineGames.Where(g => g.AccountId == accountId).ToListAsync(ct));
    }

    private async Task<bool> KnownPlayerAsync(string fide, CancellationToken ct) =>
        fide.Length is > 0 and <= 16 && (await db.LeaguePlayers.AnyAsync(p => p.FideId == fide, ct)
                                         || await db.LeaguePlayerProfiles.AnyAsync(p => p.FideId == fide, ct));

    private static bool Same(LeagueOnlineAccount a, string site, string user) =>
        a.Site == site && string.Equals(a.UserName, user, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeUrl(string? s) => (s ?? "").Contains("://", StringComparison.Ordinal) || (s ?? "").Contains(".org/") || (s ?? "").Contains(".com/");

    private static string? Comment(string? c)
    {
        var t = (c ?? "").Trim();
        return t.Length == 0 ? null : t.Length > MaxCommentLength ? t[..MaxCommentLength] : t;
    }
}

/// <summary>
/// Konten Minderjähriger (0.610.0, Wunsch 2026-09-30: „du linkst sie, aber zeigst niemandem den Namen/Account"): die
/// Konto-Suche sucht auch sie, und ihre Partien kommen in den Eröffnungsbaum — aber Seite, Nutzername, Adresse, Profilangaben
/// und Kommentar verlassen den Server nie (Karte, Meldeliste, Teilen-Links, Vorschläge). Verborgen ist, wessen Jahrgang laut
/// Konto-Suche unter <see cref="LeagueAccountFinder.AdultAge"/> liegt. Ein UNBEKANNTER Jahrgang verbirgt seit 0.616.0 nicht mehr
/// (Wunsch des Nutzers: „alle mit gesichertem Geburtsdatum unter 18 ausblenden, alle anderen anzeigen"); ebenso gilt ein Konto
/// ohne Such-Eintrag (nie abgesucht) als sichtbar. Mit 18 wird es von selbst sichtbar.
/// </summary>
public static class LeagueHiddenAccounts
{
    public static bool Hides(int? birthYear, DateTime? now = null) =>
        birthYear is { } y && (now ?? DateTime.UtcNow).Year - y < LeagueAccountFinder.AdultAge;

    /// <summary>Die FIDE-IDs aus <paramref name="fides"/> (<c>null</c> = alle), deren Konten verborgen bleiben.</summary>
    public static async Task<HashSet<string>> FidesAsync(AppDbContext db, IEnumerable<string>? fides, CancellationToken ct)
    {
        var limit = DateTime.UtcNow.Year - LeagueAccountFinder.AdultAge;
        var q = db.LeagueAccountScans.AsNoTracking().Where(s => s.BirthYear != null && s.BirthYear > limit);
        if (fides is not null)
        {
            var list = fides.Distinct().ToList();
            q = q.Where(s => list.Contains(s.FideId));
        }
        return (await q.Select(s => s.FideId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
    }
}
