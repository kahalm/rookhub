using Microsoft.EntityFrameworkCore;
using System.Globalization;
using RookHub.Api.Exceptions;
using RookHub.Api.Services.Tournaments.Live;
using RookHub.Api.Validation;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Link-Vorschau der Live-Bretter <c>/tournaments/{id}/live?round=&amp;team=&amp;player=</c> (Turnierseite, 1.024.0, Wunsch
/// 2026-11-26: einen bestimmten Livestream auch an Gäste teilen). Titel nennt Begegnung bzw. Spieler, Turnier und Runde, die
/// Beschreibung den Stand und wie viele Bretter übertragen werden. Gelesen wird nur, was die Seite ohne Anmeldung ohnehin
/// zeigt — und OHNE neue Lichess-Verbindung: keine Suche nach einer Übertragung (<see cref="TournamentLiveService.GetLinkedStatusAsync"/>),
/// Partien nur, wenn der Server sie gerade ohnehin im Speicher hat (<see cref="BroadcastLiveHub.Peek"/>). Texte deutsch wie die
/// übrigen Vorschauen der Turnierseite (Turnier, Kalender).
/// </summary>
public partial class OgMetaService
{
    internal const string LiveKind = "live";
    internal const string LiveViewKind = "liveview";

    private readonly TournamentLiveService? _live;
    private readonly ILiveTournamentData? _liveData;
    private readonly BroadcastLiveHub? _liveHub;

    private async Task<OgPage?> ResolveLivePageAsync(string id, string? path, string baseUrl, string canonical, CancellationToken ct)
    {
        if (!TournamentIdValidator.IsValid(id)) return null;
        // Bild: dasselbe Turnier-Motiv wie /t/{id} — eine Live-Stellung wäre im Messenger nach Minuten veraltet (Bilder werden
        // nach der Adresse gemerkt).
        var img = $"{baseUrl}/api/og/img/tournament/{Uri.EscapeDataString(id)}.png";
        var status = _live is null ? null : await _live.GetLinkedStatusAsync(id, ct);
        var tourName = status?.TourName ?? await TryTournamentNameAsync(id, ct) ?? "Schachturnier";
        if (status is null)
            return new OgPage($"Live-Bretter · {tourName}", "Live-Bretter mit Uhren auf der Turnierseite", img, canonical);

        var round = int.TryParse(QueryParam(path, "round"), NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r > 0
            ? r : status.CurrentRound;
        var info = round is null ? null : status.Rounds.FirstOrDefault(x => x.Round == round);
        var team = Clean(QueryParam(path, "team"));
        var player = Clean(QueryParam(path, "player"));

        CrRound? cr = null;
        if (round is { } rn && _liveData is not null)
        {
            try { cr = await _liveData.GetRoundAsync(id, rn, ct); }
            catch (CrawlerRequestException) { /* ohne Paarung: Titel ohne Begegnung */ }
        }
        var games = info is null ? null : _liveHub?.Peek(info.LichessRoundId);
        var layout = cr is null || games is null ? null : LiveBoardAssembler.Assemble(cr, games);

        var where = round is null ? tourName : $"{tourName}, Runde {round}";
        // Hinzugefügte Bretter (1.025.0): ?add= aus derselben Runde, ?ext= aus fremden Übertragungen — nur gezählt, ohne
        // Lichess-Abruf (der Vorschau-Abrufer soll keine fremde Runde prüfen lassen).
        var more = AddedBoards(QueryParam(path, "add")) + ExternalRoundRegistry.ParseRefs(QueryParam(path, "ext")).Count;
        var plus = more switch { 0 => "", 1 => " + 1 weiteres Brett", _ => $" + {more} weitere Bretter" };
        var parts = new List<string>();
        string title;
        var match = team is null ? null : cr?.Matches.FirstOrDefault(m => SameTeam(m.Home, team) || SameTeam(m.Away, team));
        if (match is not null)
        {
            title = $"Live: {match.Home} – {match.Away}{plus} · {where}";
            var live = layout?.Matches.FirstOrDefault(m => m.MatchNumber == match.MatchNumber);
            var home = match.HomeScore ?? live?.LiveHome;
            var away = match.AwayScore ?? live?.LiveAway;
            if (home is not null && away is not null && (match.HomeScore is not null || live!.Boards.Any(b => b.GameId is not null)))
                parts.Add($"Stand {Score(home.Value)} : {Score(away.Value)}");
            if (live is not null) parts.Add(Boards(live.Boards.Count(b => b.GameId is not null)));
        }
        else if (team is not null)
            title = $"Live: {team}{plus} · {where}";
        else if (player is not null)
            title = $"Live: {player}{plus} · {where}";
        else
        {
            title = $"Live-Bretter{plus} · {where}";
            if (cr is { Team: true, Matches.Count: > 0 }) parts.Add(cr.Matches.Count == 1 ? "1 Begegnung" : $"{cr.Matches.Count} Begegnungen");
            if (games is not null) parts.Add(Boards(games.Count));
        }
        if (info is not null) parts.Add(RoundState(info));
        parts.Add("Live-Bretter mit Uhren auf der Turnierseite");
        return new OgPage(title, string.Join(" · ", parts), img, canonical);
    }

    /// <summary>
    /// Vorschau des kurzen Links <c>/live/{token}</c> (1.029.0): dieselbe Karte wie die lange Adresse, gebaut aus dem aktuellen Stand
    /// der geteilten Ansicht (<c>SharedLiveViews</c>, ohne „zuletzt gesehen“ nachzuführen — ein Vorschau-Abrufer ist kein Zuschauer).
    /// </summary>
    private async Task<OgPage?> ResolveLiveViewPageAsync(string token, string baseUrl, string canonical, CancellationToken ct)
    {
        if (!SharedLiveViews.IsToken(token)) return null;
        var row = await _db.SharedLiveViews.AsNoTracking().FirstOrDefaultAsync(v => v.Token == token, ct);
        if (row is null) return null;
        return await ResolveLivePageAsync(row.CrawlerTournamentId, LongPath(row.CrawlerTournamentId, SharedLiveViews.Parse(row.ViewJson)),
            baseUrl, canonical, ct);
    }

    /// <summary>Die lange Adresse einer Ansicht (<c>/tournaments/{id}/live?round=&amp;team=…</c>) — Eingabe der bestehenden Vorschau.</summary>
    internal static string LongPath(string id, DTOs.Tournaments.LiveViewState v)
    {
        var q = new List<string>();
        if (v.Round is { } r) q.Add("round=" + r.ToString(CultureInfo.InvariantCulture));
        if (v.Team is { } t) q.Add("team=" + Uri.EscapeDataString(t));
        if (v.Player is { } p) q.Add("player=" + Uri.EscapeDataString(p));
        if (v.Add.Count > 0) q.Add("add=" + string.Join(',', v.Add));
        if (v.Ext.Count > 0) q.Add("ext=" + string.Join(',', v.Ext));
        return $"/tournaments/{id}/live" + (q.Count > 0 ? "?" + string.Join('&', q) : "");
    }

    /// <summary>Zahl der gültigen Einträge in <c>?add=</c> (Regel in <see cref="LiveView.ParseAdd"/>, gedeckelt wie auf der Seite).</summary>
    internal static int AddedBoards(string? csv) => LiveView.ParseAdd(csv).Count;

    /// <summary>„läuft gerade“ / „beendet“ / „Beginn 27.11.2026, 14:00“ (Wiener Zeit).</summary>
    internal static string RoundState(LiveRoundInfo info)
    {
        if (info.Ongoing) return "Runde läuft gerade";
        if (info.Finished) return "Runde beendet";
        if (info.StartsAt is not { } at) return "Runde noch nicht begonnen";
        DateTime local;
        try { local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna")); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { local = at; }
        return $"Beginn {local.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture)}";
    }

    private static string Boards(int n) => n == 1 ? "1 Brett übertragen" : $"{n} Bretter übertragen";

    /// <summary>Mannschaftsstand wie auf der Seite in deutscher Schreibweise: „3,5“, „2“.</summary>
    internal static string Score(decimal v) => v.ToString("0.#", CultureInfo.GetCultureInfo("de-AT"));

    private static bool SameTeam(string a, string b) =>
        string.Equals(LiveBoardAssembler.NameKey(a), LiveBoardAssembler.NameKey(b), StringComparison.Ordinal);

    /// <summary>Freitext aus der Adresse: getrimmt, auf 80 Zeichen gekürzt (er landet im Titel), leer → null.</summary>
    private static string? Clean(string? s)
    {
        s = s?.Replace('+', ' ').Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length > 80 ? s[..80] : s;
    }
}
