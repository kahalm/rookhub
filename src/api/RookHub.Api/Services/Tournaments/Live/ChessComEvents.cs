using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// chess.com-Übertragungen („Events“, 1.040.0, Wunsch 2026-11-28 „kann ich bei den Livebrettern auch chess.com-Broadcast-Links
/// einfügen?“) — der reine Teil: Links erkennen, Kennungen bauen/zerlegen, die JSON-Antworten lesen, PGN bauen. Abgerufen wird
/// in <see cref="ChessComLiveSource"/>.
/// <para>chess.com hat KEINE offizielle Broadcast-API. Die Events-Seite selbst liest (gemessen 28.11.2026, Bündel
/// <c>events-view.js</c>) <c>POST https://www.chess.com/events/v1/api/room/{eventSlug}</c> (Event, Runden, ALLE Partien mit
/// Ergebnis, Uhren, <c>currentFEN</c>, <c>revision</c> — live aktuell) und für die Züge einer Partie
/// <c>POST …/game/{eventSlug}/{roundSlug}/{gameSlug}</c> (<c>moves[]</c> mit <c>cbn</c> = „e2e4_e4“ und <c>clock</c> in ms);
/// <c>POST …/index/{indexSlug}</c> listet die Räume einer Event-Gruppe. Der PGN-Download <c>/events/pgn/…</c> verlangt eine
/// Anmeldung, die Züge im Browser kommen sonst über einen Socket.IO-Kanal — beides nicht genutzt.</para>
/// <para>Kennungen (in der Adresse <c>?ext=roundId.gameId</c> und im Pfad von <c>/api/live-boards/pgn/…</c>, daher ohne
/// Punkt/Komma/Schrägstrich): Turnier <c>cc~{eventSlug}</c>, Runde <c>cc~{eventSlug}~{roundSlug}</c>, Partie = der
/// chess.com-Partie-Slug (<c>Gukesh_D-Sindarov_Javokhir</c>). Lichess-Kennungen haben nie ein „~“.</para>
/// </summary>
public static partial class ChessComEvents
{
    public const string Prefix = "cc~";
    public const string Site = "https://www.chess.com";

    /// <summary>Was ein chess.com-Link nennt (Slugs). <see cref="Index"/> = Event-Gruppe (<c>/events/info/{slug}</c>) — der
    /// Aufrufer wählt dann einen Raum.</summary>
    public sealed record Link(string? Room, string? Round, string? Game, string? Index = null);

    private static readonly HashSet<string> Tabs = new(StringComparer.OrdinalIgnoreCase)
        { "games", "results", "schedule", "info", "news", "players", "pairings", "standings", "dashboard", "bracket", "teams" };

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "current", "finished", "favorites", "results", "pgn", "v1", "tab", "embed", "graphics", "upcoming", "past" };

    /// <summary>
    /// Link einer chess.com-Übertragung: <c>chess.com/events/{event}</c> (auch <c>/games</c>, <c>/results</c> … dahinter),
    /// <c>/events/{event}/{runde}/{partie}</c> (auch über <c>/dashboard/</c>), <c>/events/{event}/{runde}</c>,
    /// <c>/events/info/{gruppe}</c>; Sprachpräfix (<c>/de/events/…</c>) und Sprach-Subdomain erlaubt. Nur Wirt chess.com bzw.
    /// <c>*.chess.com</c>. <c>null</c> = kein chess.com-Event-Link.
    /// </summary>
    public static Link? LinkOf(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.Length is 0 or > 400) return null;
        if (!Uri.TryCreate(s.Contains("://", StringComparison.Ordinal) ? s : "https://" + s, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        var host = uri.Host.ToLowerInvariant();
        if (host != "chess.com" && !host.EndsWith(".chess.com", StringComparison.Ordinal)) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        if (parts.Count > 0 && LocaleRe().IsMatch(parts[0]) && !parts[0].Equals("events", StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(0);
        if (parts.Count < 2 || !parts[0].Equals("events", StringComparison.OrdinalIgnoreCase)) return null;
        parts.RemoveAt(0);
        if (parts[0].Equals("info", StringComparison.OrdinalIgnoreCase))
            return parts.Count >= 2 && SlugRe().IsMatch(parts[1]) ? new Link(null, null, null, parts[1]) : null;
        var room = parts[0];
        if (Reserved.Contains(room) || !SlugRe().IsMatch(room)) return null;
        var rest = parts.Skip(1).ToList();
        if (rest.Count > 0 && rest[0].Equals("dashboard", StringComparison.OrdinalIgnoreCase) && rest.Count >= 3) rest.RemoveAt(0);
        if (rest.Count == 0 || rest.Count == 1 && Tabs.Contains(rest[0])) return new Link(room, null, null);
        if (rest.Count == 1) return RoundSlugRe().IsMatch(rest[0]) ? new Link(room, rest[0], null) : null;
        if (Tabs.Contains(rest[0])) return new Link(room, null, null);
        return RoundSlugRe().IsMatch(rest[0]) && GameSlugRe().IsMatch(rest[1]) ? new Link(room, rest[0], rest[1]) : null;
    }

    public static string TourId(string room) => Prefix + room;
    public static string RoundId(string room, string round) => $"{Prefix}{room}~{round}";

    /// <summary><c>cc~{event}</c>?</summary>
    public static bool IsTourId(string? s) => s is not null && TourIdRe().IsMatch(s);
    /// <summary><c>cc~{event}~{runde}</c>?</summary>
    public static bool IsRoundId(string? s) => s is not null && RoundIdRe().IsMatch(s);
    /// <summary>chess.com-Partie-Slug?</summary>
    public static bool IsGameId(string? s) => s is not null && GameSlugRe().IsMatch(s);

    /// <summary>Gehört die Kennung (Turnier oder Runde) zu chess.com?</summary>
    public static bool IsChessCom(string? id) => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary><c>cc~{event}</c> → Event-Slug.</summary>
    public static string? RoomOfTour(string tourId) => IsTourId(tourId) ? tourId[Prefix.Length..] : null;

    /// <summary><c>cc~{event}~{runde}</c> → (Event, Runde).</summary>
    public static (string Room, string Round)? SplitRound(string roundId)
    {
        if (!IsRoundId(roundId)) return null;
        var rest = roundId[Prefix.Length..];
        var i = rest.LastIndexOf('~');
        return (rest[..i], rest[(i + 1)..]);
    }

    /// <summary>Link der Event-Seite.</summary>
    public static string RoomUrl(string room) => $"{Site}/events/{room}";
    /// <summary>Link der Partie bei chess.com.</summary>
    public static string GameUrl(string room, string round, string game) => $"{Site}/events/{room}/{round}/{game}";

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z]{2})?$")]
    private static partial Regex LocaleRe();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,99}$")]
    private static partial Regex SlugRe();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex RoundSlugRe();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,80}$")]
    private static partial Regex GameSlugRe();
    [GeneratedRegex(@"^cc~[A-Za-z0-9][A-Za-z0-9-]{0,99}$")]
    private static partial Regex TourIdRe();
    [GeneratedRegex(@"^cc~[A-Za-z0-9][A-Za-z0-9-]{0,99}~[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex RoundIdRe();

    // ───────────────────────── JSON lesen ─────────────────────────

    public sealed record Player(string Name, string? Title, int? Elo, string? FideId, string? Team);

    /// <summary>Eine Partie aus der Raum-Antwort (ohne Züge). Uhren in ms; <c>Fen</c> = aktuelle Stellung (fehlt vor dem ersten Zug).</summary>
    public sealed record Game(long Id, long RoundId, string Slug, long Revision, string Result, int? Board, string? Fen,
        long? WhiteClockMs, long? BlackClockMs, Player White, Player Black, DateTime? LastMoveAt)
    {
        public bool Finished => Result is "1-0" or "0-1" or "1/2-1/2";
    }

    public sealed record Round(long Id, string Slug, string Name, DateTime? StartAt);

    public sealed record Room(string Slug, string Name, DateTime? StartAt, DateTime? EndAt, IReadOnlyList<Round> Rounds,
        IReadOnlyList<Game> Games)
    {
        public IEnumerable<Game> GamesOf(Round r) => Games.Where(g => g.RoundId == r.Id);
        public Round? RoundBySlug(string slug) => Rounds.FirstOrDefault(r => r.Slug == slug);
    }

    /// <summary>Ein Zug: SAN und Uhr nach dem Zug (ms).</summary>
    public sealed record Move(string San, long? ClockMs);

    /// <summary>Ein Raum einer Event-Gruppe (Antwort von <c>index/{slug}</c>).</summary>
    public sealed record IndexRoom(string Slug, string Name, DateTime? StartAt, DateTime? EndAt, DateTime? UpdateAt);

    /// <summary><c>room/{slug}</c> → Event, Runden, Partien; <c>null</c> ohne <c>room</c>.</summary>
    public static Room? ParseRoom(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("room", out var room) || room.ValueKind != JsonValueKind.Object) return null;
        var slug = Str(room, "slug");
        if (slug is null) return null;
        var rounds = new List<Round>();
        if (r.TryGetProperty("rounds", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var x in rs.EnumerateArray())
            {
                if (Long(x, "id") is not { } id || Str(x, "slug") is not { } rslug) continue;
                rounds.Add(new Round(id, rslug, Str(x, "name") ?? RoundName(rslug), Date(x, "startAt")));
            }
        var games = new List<Game>();
        if (r.TryGetProperty("games", out var gs) && gs.ValueKind == JsonValueKind.Array)
            foreach (var x in gs.EnumerateArray())
                if (ParseGame(x) is { } g) games.Add(g);
        return new Room(slug, Str(room, "name") ?? slug, Date(room, "startAt"), Date(room, "endAt"), rounds, games);
    }

    /// <summary>Runde ohne Namen („01“) → „Round 1“.</summary>
    private static string RoundName(string slug) =>
        int.TryParse(slug, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? $"Round {n}" : slug;

    private static Game? ParseGame(JsonElement x)
    {
        if (x.ValueKind != JsonValueKind.Object || Long(x, "id") is not { } id || Long(x, "roundId") is not { } rid
            || Str(x, "slug") is not { } slug || !GameSlugRe().IsMatch(slug)) return null;
        var result = Str(x, "result") ?? "*";
        if (result is not ("1-0" or "0-1" or "1/2-1/2")) result = "*";
        return new Game(id, rid, slug, Long(x, "revision") ?? 0, result, (int?)Long(x, "board"), Str(x, "currentFEN"),
            Long(x, "whiteClock"), Long(x, "blackClock"),
            PlayerOf(x, "white", Str(x, "whiteTeam"), Str(x, "whiteTitle"), (int?)Long(x, "whiteElo")),
            PlayerOf(x, "black", Str(x, "blackTeam"), Str(x, "blackTitle"), (int?)Long(x, "blackElo")),
            Date(x, "lastMoveAt"));
    }

    private static Player PlayerOf(JsonElement game, string side, string? team, string? title, int? elo)
    {
        if (!game.TryGetProperty(side, out var p) || p.ValueKind != JsonValueKind.Object)
            return new Player("?", title, elo, null, team);
        var fed = Str(p, "fed");
        var fide = Long(p, "fideId") is { } f and > 0 ? f.ToString(CultureInfo.InvariantCulture) : null;
        // Einzelturniere tragen als „Team“ die Föderation (IND, UZB) — das ist keine Mannschaft.
        var realTeam = team is not null && !team.Equals(fed, StringComparison.OrdinalIgnoreCase) ? team : null;
        return new Player(Str(p, "name") ?? Str(p, "preferredName") ?? "?", Str(p, "title") ?? title,
            (int?)Long(p, "elo") is { } e and > 0 ? e : elo, fide, realTeam);
    }

    /// <summary><c>game/{event}/{runde}/{partie}</c> → Partie (wie im Raum) und Züge. <c>null</c> ohne <c>game</c>.</summary>
    public static (Game Game, IReadOnlyList<Move> Moves)? ParseGameDetail(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("game", out var g) || ParseGame(g) is not { } game) return null;
        var moves = new List<(int Ply, Move Move)>();
        if (r.TryGetProperty("moves", out var ms) && ms.ValueKind == JsonValueKind.Array)
            foreach (var m in ms.EnumerateArray())
            {
                var cbn = Str(m, "cbn");
                var us = cbn?.IndexOf('_') ?? -1;
                if (cbn is null || us < 0 || us == cbn.Length - 1) continue;
                moves.Add(((int)(Long(m, "ply") ?? moves.Count), new Move(cbn[(us + 1)..], Long(m, "clock"))));
            }
        return (game, moves.OrderBy(x => x.Ply).Select(x => x.Move).ToList());
    }

    /// <summary><c>index/{slug}</c> → Räume der Gruppe.</summary>
    public static List<IndexRoom> ParseIndex(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<IndexRoom>();
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("rooms", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var x in rs.EnumerateArray())
                if (Str(x, "slug") is { } slug && SlugRe().IsMatch(slug))
                    list.Add(new IndexRoom(slug, Str(x, "name") ?? slug, Date(x, "startAt"), Date(x, "endAt"), Date(x, "updateAt")));
        return list;
    }

    /// <summary>Raum einer Gruppe für einen Gruppen-Link: der zuletzt geänderte, der schon begonnen hat (sonst der erste).</summary>
    public static IndexRoom? PickRoom(IReadOnlyList<IndexRoom> rooms, DateTime now) =>
        rooms.Where(r => r.StartAt is not { } s || s <= now).OrderByDescending(r => r.UpdateAt ?? DateTime.MinValue).FirstOrDefault()
        ?? rooms.FirstOrDefault();

    // ───────────────────────── Zustand der Runden ─────────────────────────

    /// <summary>Alle Partien mit Ergebnis (und es gibt welche).</summary>
    public static bool IsFinished(Room room, Round r)
    {
        var games = room.GamesOf(r).ToList();
        return games.Count > 0 && games.All(g => g.Finished);
    }

    /// <summary>Läuft: begonnen (Termin erreicht oder schon ein Zug) und nicht fertig.</summary>
    public static bool IsOngoing(Room room, Round r, DateTime now) =>
        !IsFinished(room, r) && room.GamesOf(r).Any() && (r.StartAt is { } s && s <= now || room.GamesOf(r).Any(g => g.LastMoveAt is not null));

    /// <summary>
    /// Der Raum in der Form von Lichess' <c>/api/broadcast/{tourId}</c> (<c>{ tour, rounds }</c>), damit Verknüpfen, Rundenliste und
    /// „Brett hinzufügen“ denselben Leser (<see cref="LichessBroadcasts.ParseTourDetail"/>) nutzen.
    /// </summary>
    public static string ToTourJson(Room room, DateTime now)
    {
        static long? Ms(DateTime? d) => d is { } x ? new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Utc)).ToUnixTimeMilliseconds() : null;
        var dates = new List<long>();
        if (Ms(room.StartAt) is { } a) dates.Add(a);
        if (Ms(room.EndAt) is { } b) dates.Add(b);
        var payload = new
        {
            tour = new { id = TourId(room.Slug), name = room.Name, dates, info = new { website = RoomUrl(room.Slug) } },
            rounds = room.Rounds.Select(r => new
            {
                id = RoundId(room.Slug, r.Slug), name = r.Name, startsAt = Ms(r.StartAt),
                ongoing = IsOngoing(room, r, now), finished = IsFinished(room, r),
            }),
        };
        return JsonSerializer.Serialize(payload);
    }

    // ───────────────────────── PGN ─────────────────────────

    /// <summary>
    /// PGN einer Partie, wie es der Übertragungs-Leser (<see cref="LiveGameParser"/>) erwartet: Kopfzeilen (Namen, Elo, Titel,
    /// FIDE-ID, Mannschaft, <c>Round "r.Brett"</c>, <c>GameURL</c> mit dem Slug als Kennung), Züge mit <c>[%clk]</c>. Ohne Züge
    /// (noch nicht geladen) steht die aktuelle Stellung als <c>[FEN]</c> da — das Brett stimmt, Zugliste/Uhren fehlen bis zum
    /// nächsten Abruf der Partie.
    /// </summary>
    public static string ToPgn(Room room, Round round, Game g, IReadOnlyList<Move>? moves)
    {
        var sb = new StringBuilder();
        void H(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) sb.Append('[').Append(k).Append(" \"").Append(v.Replace("\\", "").Replace("\"", "'")).Append("\"]\n"); }
        var roundNo = int.TryParse(round.Slug, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : round.Slug;
        H("Event", room.Name);
        H("Site", "chess.com");
        H("Round", g.Board is { } b ? $"{roundNo}.{b}" : roundNo);
        H("White", g.White.Name);
        H("Black", g.Black.Name);
        H("Result", g.Result);
        H("WhiteElo", g.White.Elo?.ToString(CultureInfo.InvariantCulture));
        H("BlackElo", g.Black.Elo?.ToString(CultureInfo.InvariantCulture));
        H("WhiteTitle", g.White.Title);
        H("BlackTitle", g.Black.Title);
        H("WhiteFideId", g.White.FideId);
        H("BlackFideId", g.Black.FideId);
        H("WhiteTeam", g.White.Team);
        H("BlackTeam", g.Black.Team);
        H("GameURL", GameUrl(room.Slug, round.Slug, g.Slug));
        if (moves is null && g.Fen is { Length: > 0 } fen && fen != StartFen)
        {
            H("SetUp", "1");
            H("FEN", fen);
        }
        sb.Append('\n');
        if (moves is not null)
            for (var i = 0; i < moves.Count; i++)
            {
                if (i % 2 == 0) sb.Append(i / 2 + 1).Append(". ");
                sb.Append(moves[i].San).Append(' ');
                if (moves[i].ClockMs is { } ms and >= 0) sb.Append("{ [%clk ").Append(Clock(ms)).Append("] } ");
            }
        sb.Append(g.Result).Append("\n\n");
        return sb.ToString();
    }

    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static string Clock(long ms)
    {
        var s = ms / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}");
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static long? Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
        return null;
    }

    private static DateTime? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime : null;
}
