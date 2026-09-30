using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Vereins-Datenbank in LeagueHub (Wunsch 2026-09-28): Mitglieder von SK Schwaz laden Partien hoch — viele auf einmal als
/// PGN oder einzeln aus einem eingelesenen Partieformular, angemeldet ODER ohne Konto über einen Teilen-Link — und die
/// Spielerkarten der Gegner zeigen sie mit.
///
/// <para>Regeln, alle vom Nutzer vorgegeben:</para>
/// <list type="bullet">
/// <item><b>Beide Namen gegen die Meldelisten</b> (<see cref="LeagueRosterIndex"/>), und wer dort fehlt, gegen das
/// Spielerverzeichnis der Megabase (<see cref="LeagueMegaPlayers"/>, eindeutig über Name oder FIDE-ID — Wunsch
/// 2026-09-28: „standardmäßig auf Megabase matchen, wenn in Tirol kein Treffer"). Ist keine Seite bekannt, wird die
/// Partie abgelehnt (<c>noLeaguePlayer</c>); bleibt nach dem Ersetzen keine bekannte übrig, ebenso (<c>onlyOwnClub</c>).
/// Eine Partie, deren Gegner nur die Megabase kennt, ist übernehmbar, die Übersicht wählt sie aber nicht vor.</item>
/// <item><b>Spieler von Schwaz werden durch „Schwaz" ersetzt</b> (Vorgabe: jeder, der in seiner jüngsten Saison für
/// Schwaz gemeldet ist, dazu der Hochladende laut Profil) — ohne Elo und FIDE-ID, die Veranstaltung fällt weg, und es
/// wird WEDER gespeichert, wer dahinter steht, NOCH wer hochgeladen hat oder wann (auch nicht versteckt). Beim PGN-Import
/// zeigt eine Übersicht (<see cref="PreviewAsync"/>) je Partie wer gegen wen, die Vorgaben und ob sie übernommen würde;
/// der Nutzer korrigiert Spieler und Ersetzen, übernommen wird mit seinen Entscheidungen (<see cref="ImportPgnAsync"/>).</item>
/// <item><b>Nur das JAHR</b> des Datums.</item>
/// <item>Gespeichert wird nur die Hauptvariante ohne Kommentare (Kommentare tragen oft Namen und Uhrzeiten), und nur ab
/// der Grundstellung (<c>fromPosition</c>) — die Karten zählen Eröffnungen.</item>
/// <item><b>Zweimal hochgeladen = einmal da</b>: gleiche Züge (<see cref="LeagueClubGame.MovesHash"/>) im gleichen Jahr;
/// bei kurzen Partien (unter <see cref="ShortGamePlies"/> Halbzügen) zusätzlich gleiche Namen — dieselben zwölf
/// Eröffnungszüge spielen viele.</item>
/// <item><b>Über einen Teilen-Link</b> (ohne Konto, <see cref="ImportViaShareAsync"/>, <see cref="AddGameViaShareAsync"/>):
/// gedeckelt je Aufruf und je Link und Tag (<see cref="LeagueShareUploadQuota"/>), und jede Partie trägt den Link als
/// Hash — ein Verwalter entfernt damit alles, was über einen weitergereichten Link kam (<see cref="DeleteByShareAsync"/>,
/// Codereview 2026-09-29, A2-009).</item>
/// </list>
/// </summary>
public sealed class LeagueClubService
{
    public const string AnonymousName = LeagueRefresh.OwnTeam;
    public const int MaxImportGames = 500;
    public const int MaxImportChars = 5_000_000;
    public const int MaxPlies = 600;
    public const int ShortGamePlies = 20;
    public const int PageSize = 50;
    private const string StartPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";
    private static readonly HashSet<string> Results = new(StringComparer.Ordinal) { "1-0", "0-1", "1/2-1/2", "*" };

    private readonly AppDbContext _db;
    private readonly ILogger<LeagueClubService> _log;
    private readonly Func<DateTime> _now;
    private readonly GameAnalysisService? _analyses;
    private readonly LeagueShareUploadQuota _shareQuota;

    /// <param name="analyses">Die Analysen des Stapels (<see cref="GameAnalysisOrigin.Club"/>) ziehen beim Korrigieren
    /// und Löschen einer Partie nach; ohne (Tests) bleibt es bei der Partie.</param>
    /// <param name="shareQuota">Der Deckel der Teilen-Link-Uploads — in der App ein Singleton (Program.cs), ohne (Tests)
    /// ein eigener je Dienst.</param>
    public LeagueClubService(AppDbContext db, ILogger<LeagueClubService> log, Func<DateTime>? now = null,
        GameAnalysisService? analyses = null, LeagueShareUploadQuota? shareQuota = null)
    {
        _db = db; _log = log; _now = now ?? (() => DateTime.UtcNow); _analyses = analyses;
        _shareQuota = shareQuota ?? new LeagueShareUploadQuota();
    }

    /// <summary>Der Vermerk eines Teilen-Links an seinen Partien: SHA-256 (hex) des Tokens — der Link selbst (144 Bit
    /// Zufall) lässt sich daraus nicht zurückgewinnen, der Verwalter mit dem Link findet seine Partien aber wieder.</summary>
    public static string ShareHashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();

    public async Task<LeagueRosterIndex> RosterAsync(CancellationToken ct)
    {
        var seasons = await _db.LeagueTournaments.AsNoTracking().Select(t => new { t.Tnr, t.Season })
            .ToDictionaryAsync(t => t.Tnr, t => t.Season, ct);
        var rows = await _db.LeaguePlayers.AsNoTracking().Select(p => new { p.Tnr, p.Team, p.Name, p.NameKey, p.FideId }).ToListAsync(ct);
        return new(rows.Select(r => new LeagueRosterIndex.Row(r.Tnr, r.Team, r.Name, r.NameKey, r.FideId,
            seasons.GetValueOrDefault(r.Tnr) ?? "")));
    }

    /// <summary>Gemerkte Zuordnungen und das Megabase-Verzeichnis für diese Namen und FIDE-IDs — die Zuordnungen zuerst,
    /// ihre Spieler schlägt das Verzeichnis gleich mit nach.</summary>
    private async Task<Lookups> LookupsAsync(LeagueRosterIndex roster, IEnumerable<string?> names, IEnumerable<string?> fides,
        CancellationToken ct)
    {
        var nameList = names.ToList();
        var aliases = await new LeagueNameAliases(_db).LoadAsync(nameList, ct);
        var mega = await new LeagueMegaPlayers(_db).LookupAsync(nameList.Concat(aliases.Values.Select(a => (string?)a.Name)),
            fides.Concat(aliases.Values.Select(a => a.Fide)), ct);
        return new Lookups(roster, mega, aliases);
    }

    // ── Hochladen ───────────────────────────────────────────────────

    private sealed record Owner(string? Fide, string?[] Names)
    {
        public static readonly Owner Nobody = new(null, Array.Empty<string?>());
    }

    /// <summary>Wer hochlädt (Profil) — ohne Konto (Teilen-Link) niemand.</summary>
    private async Task<Owner> OwnerAsync(int? userId, CancellationToken ct)
    {
        if (userId is not int uid) return Owner.Nobody;
        var p = await _db.UserProfiles.AsNoTracking().Where(x => x.UserId == uid)
            .Select(x => new { x.FideId, x.LastName, x.DisplayName, x.FirstName }).FirstOrDefaultAsync(ct);
        return new Owner(string.IsNullOrWhiteSpace(p?.FideId) ? null : p!.FideId!.Trim(),
            new[] { p?.LastName, p?.DisplayName, p?.FirstName });
    }

    /// <summary>Welche Seite spielt der Hochladende? FIDE-ID vor Name; bei Gleichstand keine.</summary>
    private static string? OwnerSideOf(Owner o, string? white, string? black, string? whiteFide, string? blackFide,
        LeagueRosterIndex.Hit hw, LeagueRosterIndex.Hit hb, LeagueMegaPlayers.Hit? mw, LeagueMegaPlayers.Hit? mb)
    {
        if (o.Fide != null)
        {
            var w = o.Fide == whiteFide || o.Fide == hw.Person?.Fide || o.Fide == mw?.Fide;
            var b = o.Fide == blackFide || o.Fide == hb.Person?.Fide || o.Fide == mb?.Fide;
            if (w != b) return w ? "white" : "black";
        }
        return o.Names.Length == 0 ? null : ScoresheetScanService.GuessOwnerSide(white, black, o.Names);
    }

    /// <summary>Eine Partie des PGN-Textes: die Kopfzeilen und die geprüfte Hauptvariante — oder warum sie sich gar nicht
    /// übernehmen lässt (<c>illegal</c>, <c>noMoves</c>, <c>tooLong</c>, <c>fromPosition</c>).</summary>
    private sealed record Parsed(int Index, Dictionary<string, string> Headers, List<string>? Sans, string? Error, string MoveText = "")
    {
        /// <summary>Die Partie als eigener PGN-Text (0.590.0): Kopfzeilen ROH (der Leser entschlüsselt nichts, also wird
        /// auch nichts verschlüsselt) und der Zugtext unverändert — zurückgeschickt ergibt er genau diese Partie. Damit
        /// importiert die Seite portionsweise, ohne die ganze Datei je Portion noch einmal zu schicken.</summary>
        public string Pgn => string.Concat(Headers.Select(kv => $"[{kv.Key} \"{kv.Value}\"]\n")) + "\n" + MoveText.Trim() + "\n";

        public string? H(string key) => Headers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && v.Trim() != "?" ? v.Trim() : null;
        public int? Elo(string key) => int.TryParse(H(key), out var e) ? e : null;
    }

    /// <summary>Die Partien in Reihenfolge, 1-basiert nummeriert — Übersicht und Übernahme zählen gleich.</summary>
    private static List<Parsed> ParseAll(string pgn, out bool truncated)
    {
        var games = PgnParser.SplitGames(pgn).Where(g => !string.IsNullOrWhiteSpace(g.MoveText)).ToList();
        truncated = games.Count > MaxImportGames;
        var start = new Chess.ChessBoard().ToFen();
        var result = new List<Parsed>();
        var index = 0;
        foreach (var (rawHeaders, moveText) in games.Take(MaxImportGames))
        {
            index++;
            var h = rawHeaders ?? new Dictionary<string, string>();
            string? Error()
            {
                if (h.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)
                    && string.Join(' ', fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4)) != StartPosition)
                    return "fromPosition";
                return null;
            }
            var err = Error();
            List<string>? sans = null;
            if (err == null)
            {
                try
                {
                    // Nur ein Ergebnis („1-0", „*") ist KEINE Partie mit illegalem Zug — TryExtractUciMainline meldet beides null.
                    var uci = PgnParser.ExtractMainlineSans(moveText).Count == 0 ? new List<string>()
                        : PgnParser.TryExtractUciMainline(start, moveText);
                    if (uci == null) err = "illegal";
                    else if (uci.Count == 0) err = "noMoves";
                    else if (uci.Count > MaxPlies) err = "tooLong";
                    else
                    {
                        sans = SavedGameService.SansOf(start, uci);
                        if (sans.Count != uci.Count) { err = "illegal"; sans = null; }
                    }
                }
                catch (Exception) { err = "illegal"; sans = null; }
            }
            result.Add(new Parsed(index, h, sans, err, moveText));
        }
        return result;
    }

    /// <summary>Eine Seite, wie sie gespeichert würde. <see cref="Fide"/> = gewählte FIDE-ID eines Spielers, der KEIN
    /// Ligaspieler ist; <see cref="Mega"/> = so ein Spieler, im Megabase-Verzeichnis gefunden — dann ist die Seite
    /// „bekannt", auch ohne Liga.</summary>
    private sealed record Side(string? Name, LeagueRosterIndex.Hit Hit, int? Elo, bool Replace, string? Fide = null,
        LeagueMegaPlayers.Hit? Mega = null, bool Alias = false)
    {
        public bool Known => Hit.League || Mega != null;

        /// <summary>Wer es ist (für die gemerkten Zuordnungen): der Ligaspieler bzw. der Megabase-Eintrag.</summary>
        public LeagueNameAliases.Entry? Identity => Hit.Person is { } p ? new(p.Fide, p.Name)
            : Mega is { } m ? new(m.Fide, m.Name) : null;
    }

    /// <summary>Alles, was ein Abgleich nachschlägt: Meldelisten, Megabase-Verzeichnis, gemerkte Zuordnungen.</summary>
    private sealed record Lookups(LeagueRosterIndex Roster, LeagueMegaPlayers.Lookup Mega,
        IReadOnlyDictionary<string, LeagueNameAliases.Entry> Aliases);

    /// <summary>
    /// Wer ist das? (1) Eine FIDE-ID aus der Partie, die ein Ligaspieler trägt; (2) eine gemerkte Zuordnung dieses
    /// Namens (<see cref="LeagueNameAliases"/> — sie schlägt den Namensabgleich, dafür wurde sie ja korrigiert); (3) die
    /// Meldelisten; (4) die Megabase. Zeigt eine Zuordnung auf jemanden, den Liste und Verzeichnis nicht (mehr) kennen,
    /// gilt ihr gemerkter Name samt FIDE-ID.
    /// </summary>
    private static (LeagueRosterIndex.Hit Hit, LeagueMegaPlayers.Hit? Mega, bool Alias) Resolve(string? name, string? fide, Lookups lk)
    {
        var hit = lk.Roster.Match(name, fide);
        if (lk.Roster.ByFide(fide) is null && lk.Aliases.TryGetValue(LeagueNameAliases.KeyOf(name), out var a))
        {
            if (a.Fide != null && lk.Roster.ByFide(a.Fide) is { } p) return (new LeagueRosterIndex.Hit(true, p, new[] { p }), null, true);
            if (a.Fide != null) return (LeagueRosterIndex.None, lk.Mega.ByFide(a.Fide) ?? new LeagueMegaPlayers.Hit(a.Name, a.Fide), true);
            var byName = lk.Roster.Match(a.Name, null);
            if (byName.League && !byName.Ambiguous) return (byName with { LastNameOnly = false }, null, true);
            return (LeagueRosterIndex.None, lk.Mega.ByName(a.Name) ?? new LeagueMegaPlayers.Hit(a.Name, null), true);
        }
        return (hit, MegaOf(hit, name, fide, lk.Mega), false);
    }

    /// <summary>Wer kein Ligaspieler ist, wird im Megabase-Verzeichnis gesucht: über die FIDE-ID, sonst den Namen. Eine
    /// FIDE-ID, die das Verzeichnis nicht kennt, lässt den Namen nur gelten, wenn er ihr nicht widerspricht.</summary>
    private static LeagueMegaPlayers.Hit? MegaOf(LeagueRosterIndex.Hit hit, string? name, string? fide, LeagueMegaPlayers.Lookup mega)
    {
        if (hit.League) return null;
        if (string.IsNullOrWhiteSpace(fide)) return mega.ByName(name);
        return mega.ByFide(fide) ?? (mega.ByName(name) is { } n && (n.Fide is null || n.Fide == fide.Trim())
            ? n with { Fide = fide.Trim() is { Length: <= 16 } f ? f : n.Fide } : null);
    }

    /// <summary>Die Vorgabe der Übersicht: Abgleich über Kopfzeile, ersetzt wird ein Spieler von Schwaz und der
    /// Hochladende selbst (Wunsch 2026-09-28: „alle Spieler vom Verein Schwaz").</summary>
    private static (Side White, Side Black, string? OwnerSide) Defaults(Parsed p, Owner owner, Lookups lk)
    {
        var (hw, mw, aw) = Resolve(p.H("White"), p.H("WhiteFideId"), lk);
        var (hb, mb, ab) = Resolve(p.H("Black"), p.H("BlackFideId"), lk);
        var ownerSide = OwnerSideOf(owner, p.H("White"), p.H("Black"), p.H("WhiteFideId"), p.H("BlackFideId"), hw, hb, mw, mb);
        return (new Side(p.H("White"), hw, p.Elo("WhiteElo"), hw.OwnClub || ownerSide == "white", null, mw, aw),
            new Side(p.H("Black"), hb, p.Elo("BlackElo"), hb.OwnClub || ownerSide == "black", null, mb, ab), ownerSide);
    }

    /// <summary>Was der Nutzer für eine Seite festgelegt hat: ein gewählter Ligaspieler (FIDE-ID) schlägt den Namen, ein
    /// getippter Name wird neu abgeglichen, ohne Angabe gilt die Kopfzeile.</summary>
    private static Side Decided(LeagueClubSideDecision? d, string? raw, string? rawFide, int? elo, Lookups lk)
    {
        d ??= new LeagueClubSideDecision();
        if (lk.Roster.ByFide(d.Fide) is { } p) return new Side(p.Name, new LeagueRosterIndex.Hit(true, p, new[] { p }), elo, d.Replace);
        var typed = !string.IsNullOrWhiteSpace(d.Name);
        var name = typed ? d.Name!.Trim() : raw;
        // Eine FIDE-ID, die kein Ligaspieler trägt (aus dem Megabase-Verzeichnis gewählt): bleibt an der Partie stehen —
        // und schlägt eine gemerkte Zuordnung (der Nutzer hat gerade selbst gewählt).
        var chosen = string.IsNullOrWhiteSpace(d.Fide) ? null : d.Fide.Trim();
        if (chosen != null)
        {
            var hit = lk.Roster.Match(name, chosen);
            return new Side(name, hit, elo, d.Replace, chosen.Length <= 16 ? chosen : null, MegaOf(hit, name, chosen, lk.Mega));
        }
        var (h, m, alias) = Resolve(name, typed ? null : rawFide, lk);
        return new Side(name, h, elo, d.Replace, null, m, alias);
    }

    /// <summary>Aus einer geprüften Zugfolge + den Seiten die zu speichernde Zeile (oder den Ablehnungsgrund).</summary>
    private static (LeagueClubGame? Game, string? Reason) Build(Side w, Side b, IReadOnlyList<string> sans, int? year,
        string? result, string? evt)
    {
        if (!w.Known && !b.Known) return (null, "noLeaguePlayer");
        if (!(w.Known && !w.Replace) && !(b.Known && !b.Replace)) return (null, "onlyOwnClub");
        (string Name, string? Fide, int? Elo) Out(Side s) => s.Replace ? (AnonymousName, null, null)
            : (Clip(s.Hit.Person?.Name ?? s.Mega?.Name ?? LeagueNames.Clean(s.Name), 120) is { Length: > 0 } n ? n : "?",
                s.Hit.Person?.Fide ?? s.Fide ?? s.Mega?.Fide, Elo(s.Elo));
        var (wn, wf, we) = Out(w);
        var (bn, bf, be) = Out(b);
        var anonymized = w.Replace || b.Replace;
        var game = new LeagueClubGame
        {
            Year = year,
            White = wn, Black = bn, WhiteFide = wf, BlackFide = bf, WhiteElo = we, BlackElo = be,
            Result = result is { } r && Results.Contains(r) ? r : "*",
            Event = anonymized ? null : Clip(LeagueNames.Clean(evt), 200) is { Length: > 0 } e && e != "?" ? e : null,
            Plies = sans.Count,
            MovesHash = HashOf(sans),
            Anonymized = anonymized,
        };
        game.Pgn = PgnOf(game, sans);
        return (game, null);
    }

    internal static string HashOf(IReadOnlyList<string> sans) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', sans)))).ToLowerInvariant();

    internal static string PgnOf(LeagueClubGame g, IReadOnlyList<string> sans)
    {
        var sb = new StringBuilder();
        sb.Append(PgnWriter.Tag("Event", g.Event ?? "?"));
        sb.Append(PgnWriter.Tag("Site", "?"));
        sb.Append(PgnWriter.Tag("Date", g.Year is { } y ? $"{y}.??.??" : "????.??.??"));
        sb.Append(PgnWriter.Tag("Round", "?"));
        sb.Append(PgnWriter.Tag("White", g.White));
        sb.Append(PgnWriter.Tag("Black", g.Black));
        sb.Append(PgnWriter.Tag("Result", g.Result));
        if (g.WhiteElo is { } we) sb.Append(PgnWriter.Tag("WhiteElo", we.ToString()));
        if (g.BlackElo is { } be) sb.Append(PgnWriter.Tag("BlackElo", be.ToString()));
        // Dieselben Namen wie bei Lumbra — die Spielerkarte erkennt die Farbe daran (LeagueProfileBuilder.ColorOf).
        if (g.WhiteFide != null) sb.Append(PgnWriter.Tag("WhiteFideId", g.WhiteFide));
        if (g.BlackFide != null) sb.Append(PgnWriter.Tag("BlackFideId", g.BlackFide));
        sb.Append('\n').Append(PgnWriter.MoveText(sans, null, null, g.Result)).Append('\n');
        return sb.ToString();
    }

    private static int? Elo(int? e) => e is >= 500 and <= 3000 ? e : null;

    private static string Clip(string? s, int max) => s is null ? string.Empty : s.Length <= max ? s : s[..max];

    /// <summary>Jahr aus einem PGN-Datum („2024.05.12", „2024.??.??"); unplausibel = unbekannt.</summary>
    internal static int? YearOf(string? date, DateTime now) =>
        date is { Length: >= 4 } && int.TryParse(date[..4], out var y) && y >= 1900 && y <= now.Year + 1 ? y : null;

    /// <summary>Gleiche Partie schon da (Datenbank oder derselbe Upload)?</summary>
    private async Task<bool> IsDuplicateAsync(LeagueClubGame g, List<LeagueClubGame> pending, CancellationToken ct)
    {
        bool Same(LeagueClubGame x) => x.MovesHash == g.MovesHash && x.Year == g.Year
            && (g.Plies >= ShortGamePlies || (x.White == g.White && x.Black == g.Black));
        if (pending.Any(Same)) return true;
        var candidates = await _db.LeagueClubGames.AsNoTracking().Where(x => x.MovesHash == g.MovesHash && x.Year == g.Year)
            .Select(x => new LeagueClubGame { MovesHash = x.MovesHash, Year = x.Year, White = x.White, Black = x.Black })
            .ToListAsync(ct);
        return candidates.Any(Same);
    }

    private static LeagueClubPreviewSideDto SideDto(Side s, bool owner) => new()
    {
        Raw = s.Name, Elo = s.Elo, Match = MatchDto(s.Hit, s.Mega, s.Alias), Owner = owner, Replace = s.Replace,
    };

    internal static LeagueClubSideMatchDto MatchDto(LeagueRosterIndex.Hit h, LeagueMegaPlayers.Hit? mega = null, bool alias = false) => new()
    {
        League = h.League, Ambiguous = h.Ambiguous, Mega = !h.League && mega != null, Alias = alias,
        Name = h.Person?.Name ?? (h.League ? null : mega?.Name), Fide = h.Person?.Fide ?? (h.League ? null : mega?.Fide),
        Club = h.OwnClub, LastNameOnly = h.LastNameOnly,
        Candidates = h.Ambiguous ? h.Candidates.Take(8).Select(PersonDto).ToList() : new(),
    };

    /// <summary>Nicht erkannt (weder Liga noch Megabase noch gemerkt): ähnlich geschriebene Ligaspieler zur Schnellauswahl
    /// (0.596.0). Je PGN-Name einmal gerechnet — in einer Datei steht derselbe Name oft dutzendfach.</summary>
    private static void AddSimilar(LeagueClubSideMatchDto m, string? raw, LeagueRosterIndex roster,
        Dictionary<string, List<LeagueRosterPersonDto>> cache)
    {
        if (m.League || m.Mega || m.Alias || string.IsNullOrWhiteSpace(raw)) return;
        var key = raw.Trim().ToLowerInvariant();
        if (!cache.TryGetValue(key, out var list))
            cache[key] = list = roster.Similar(raw).Select(PersonDto).ToList();
        m.Similar = list;
    }

    internal static LeagueRosterPersonDto PersonDto(LeagueRosterIndex.Person p) =>
        new() { Name = p.Name, Fide = p.Fide, Teams = p.Teams.Take(3).ToList(), Club = p.OwnClub };

    /// <summary>
    /// Übersicht vor dem Import (Wunsch 2026-09-28: „nach Import Übersicht wer gegen wen, unerkannte/falsche Spieler
    /// korrigieren, je Partie markieren, ob sie importiert wird"): liest und gleicht ab, speichert NICHTS. Je Partie die
    /// Seiten mit Abgleich und der Vorgabe „ersetzen", ob sie schon da ist, und was sie unübernehmbar macht. Ob eine
    /// Partie übernommen wird, entscheidet die Seite danach aus den Seiten (dieselbe Regel wie <see cref="Build"/>).
    /// </summary>
    public async Task<LeagueClubPreviewDto> PreviewAsync(int? userId, string pgn, CancellationToken ct = default)
    {
        var parsed = ParseAll(pgn, out var truncated);
        var lk = await LookupsAsync(await RosterAsync(ct), parsed.SelectMany(p => new[] { p.H("White"), p.H("Black") }),
            parsed.SelectMany(p => new[] { p.H("WhiteFideId"), p.H("BlackFideId") }), ct);
        var owner = await OwnerAsync(userId, ct);
        var now = _now();
        var pending = new List<LeagueClubGame>();
        var dto = new LeagueClubPreviewDto { Truncated = truncated };
        var similar = new Dictionary<string, List<LeagueRosterPersonDto>>();
        foreach (var p in parsed)
        {
            var (w, b, ownerSide) = Defaults(p, owner, lk);
            var g = new LeagueClubPreviewGameDto
            {
                Index = p.Index, Year = YearOf(p.H("Date"), now), Result = p.H("Result") is { } r && Results.Contains(r) ? r : "*",
                Event = p.H("Event"), Plies = p.Sans?.Count ?? 0, Opening = p.Sans is null ? "" : OpeningOfSans(p.Sans),
                Error = p.Error, White = SideDto(w, ownerSide == "white"), Black = SideDto(b, ownerSide == "black"),
                Pgn = p.Error == null ? p.Pgn : null,
            };
            AddSimilar(g.White.Match, g.White.Raw, lk.Roster, similar);
            AddSimilar(g.Black.Match, g.Black.Raw, lk.Roster, similar);
            if (p.Sans != null && Build(w, b, p.Sans, g.Year, p.H("Result"), p.H("Event")).Game is { } built)
            {
                g.Duplicate = await IsDuplicateAsync(built, pending, ct);
                pending.Add(built);
            }
            dto.Games.Add(g);
        }
        return dto;
    }

    /// <summary>PGN-Import nach der Übersicht: je Partie in <paramref name="decisions"/> die festgelegten Seiten
    /// (<c>null</c> = alle Partien mit den Vorgaben der Übersicht). Wirft nicht bei einzelnen Partien — die stehen mit
    /// Grund in <c>Failed</c>.</summary>
    public Task<LeagueClubImportResultDto> ImportPgnAsync(int? userId, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct = default) =>
        ImportAsync(userId, null, pgn, decisions, ct);

    /// <summary>Derselbe Import OHNE Konto über den Teilen-Link <paramref name="shareToken"/>: jede Partie trägt den Link
    /// (<see cref="ShareHashOf"/>), und gespeichert werden höchstens <see cref="LeagueShareUploadQuota.PerCall"/> je
    /// Aufruf und <see cref="LeagueShareUploadQuota.PerLinkPerDay"/> je Link und Tag — der Rest steht mit Grund
    /// <c>shareLimit</c> in <c>Failed</c>.</summary>
    public Task<LeagueClubImportResultDto> ImportViaShareAsync(string shareToken, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct = default) =>
        ImportAsync(null, ShareHashOf(shareToken), pgn, decisions, ct);

    private async Task<LeagueClubImportResultDto> ImportAsync(int? userId, string? shareHash, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct)
    {
        var result = new LeagueClubImportResultDto();
        var parsed = ParseAll(pgn, out var truncated);
        result.Truncated = truncated;
        var byIndex = parsed.ToDictionary(p => p.Index);
        var chosen = (decisions ?? Array.Empty<LeagueClubImportGameDecision>()).Where(d => d != null)
            .SelectMany(d => new[] { d.White, d.Black }).Where(s => s != null).ToList();
        var lk = await LookupsAsync(await RosterAsync(ct),
            parsed.SelectMany(p => new[] { p.H("White"), p.H("Black") }).Concat(chosen.Select(s => s.Name)),
            parsed.SelectMany(p => new[] { p.H("WhiteFideId"), p.H("BlackFideId") }).Concat(chosen.Select(s => s.Fide)), ct);
        var owner = await OwnerAsync(userId, ct);
        var remember = new List<(string Raw, LeagueNameAliases.Entry Target)>();
        var now = _now();
        var pending = new List<LeagueClubGame>();
        var work = decisions?.Where(d => d != null).DistinctBy(d => d.Index).Select(d => (d.Index, (LeagueClubImportGameDecision?)d)).ToList()
            ?? parsed.Select(p => (p.Index, (LeagueClubImportGameDecision?)null)).ToList();
        // Über einen Teilen-Link: vorher reservieren, was dieser Aufruf höchstens speichern darf; der Rest geht am Ende zurück.
        using var lease = shareHash is null ? null : _shareQuota.Reserve(shareHash, LeagueShareUploadQuota.PerCall);
        var budget = lease?.Granted ?? int.MaxValue;
        foreach (var (index, decision) in work)
        {
            if (!byIndex.TryGetValue(index, out var p))
            {
                result.Failed.Add(new LeagueClubFailureDto { Index = index, Reason = "notFound" });
                continue;
            }
            void Fail(string reason) => result.Failed.Add(new LeagueClubFailureDto
            {
                Index = index, White = p.H("White") is { } w0 ? Clip(w0, 120) : null, Black = p.H("Black") is { } b0 ? Clip(b0, 120) : null,
                Reason = reason,
            });
            if (p.Error != null || p.Sans == null) { Fail(p.Error ?? "illegal"); continue; }
            Side w, b;
            if (decision == null) (w, b, _) = Defaults(p, owner, lk);
            else
            {
                w = Decided(decision.White, p.H("White"), p.H("WhiteFideId"), p.Elo("WhiteElo"), lk);
                b = Decided(decision.Black, p.H("Black"), p.H("BlackFideId"), p.Elo("BlackElo"), lk);
                if (userId != null)
                {
                    var (dw, db, _) = Defaults(p, owner, lk);
                    Remember(p.H("White"), decision.White, w, dw);
                    Remember(p.H("Black"), decision.Black, b, db);
                }
            }
            var (game, reason) = Build(w, b, p.Sans, YearOf(p.H("Date"), now), p.H("Result"), p.H("Event"));
            if (game == null) { Fail(reason!); continue; }
            if (await IsDuplicateAsync(game, pending, ct)) { result.Duplicates++; continue; }
            if (pending.Count >= budget) { Fail(ShareLimitReason); continue; }
            Stamp(game, userId, now, shareHash);
            pending.Add(game);
        }
        if (lease != null) lease.Kept = pending.Count;
        await SaveAsync(pending, ct);
        if (shareHash != null && result.Failed.Count(f => f.Reason == ShareLimitReason) is > 0 and var refused)
            _log.LogWarning("Vereins-Datenbank: Teilen-Link {Link} am Deckel — {Refused} Partien nicht übernommen",
                shareHash[..8], refused);
        result.Remembered = await RememberAsync(remember, ct);
        result.Added = pending.Count;
        result.Anonymized = pending.Count(g => g.Anonymized);
        result.Ids = pending.Select(g => g.Id).ToList();
        _log.LogInformation("Vereins-Datenbank: {Added} Partien hochgeladen ({Anon} mit „Schwaz“), {Dup} doppelt, {Failed} abgelehnt, {Remembered} Zuordnungen gemerkt{Via}",
            result.Added, result.Anonymized, result.Duplicates, result.Failed.Count, result.Remembered, userId == null ? " (Teilen-Link)" : "");
        return result;

        // Eine Korrektur (Spieler gewählt oder Name getippt), die bei jemand ANDEREM landet als die Vorgabe: merken.
        void Remember(string? raw, LeagueClubSideDecision? d, Side decided, Side byDefault)
        {
            if (string.IsNullOrWhiteSpace(raw) || d is null || (string.IsNullOrWhiteSpace(d.Name) && string.IsNullOrWhiteSpace(d.Fide))) return;
            if (decided.Identity is { } id && id != byDefault.Identity) remember.Add((raw, id));
        }
    }

    /// <summary>Gemerkte Zuordnungen speichern — NUR mit Konto (ein Teilen-Link soll nicht festlegen, wer ein Name für
    /// alle ist). Ein Fehler dabei (zwei gleichzeitige Uploads legen denselben Namen an) lässt den Import nicht scheitern.</summary>
    private async Task<int> RememberAsync(List<(string Raw, LeagueNameAliases.Entry Target)> items, CancellationToken ct)
    {
        if (items.Count == 0) return 0;
        try { return await new LeagueNameAliases(_db).SaveAsync(items, _now(), ct); }
        catch (DbUpdateException ex)
        {
            _log.LogWarning(ex, "Vereins-Datenbank: gemerkte Zuordnungen nicht gespeichert");
            _db.ChangeTracker.Clear();
            return 0;
        }
    }

    /// <summary>Eine Partie aus der Korrektur eines Partieformulars. <c>Reason</c> ≠ null = abgelehnt
    /// (<c>illegal</c> samt Meldung, sonst wie beim Import, dazu <c>duplicate</c>).</summary>
    public Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameAsync(int? userId, LeagueClubGameRequest req,
        CancellationToken ct = default) => AddAsync(userId, null, req, ct);

    /// <summary>Dasselbe ohne Konto über den Teilen-Link <paramref name="shareToken"/> — zählt gegen denselben Deckel wie der
    /// PGN-Import (dieser Weg braucht kein Foto), darüber <c>shareLimit</c>.</summary>
    public Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameViaShareAsync(string shareToken,
        LeagueClubGameRequest req, CancellationToken ct = default) => AddAsync(null, ShareHashOf(shareToken), req, ct);

    private async Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddAsync(int? userId, string? shareHash,
        LeagueClubGameRequest req, CancellationToken ct)
    {
        var moves = (req.Moves ?? new()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();
        if (moves.Count == 0) return (null, "noMoves", null);
        if (moves.Count > MaxPlies) return (null, "tooLong", null);
        List<string> sans;
        try { sans = SavedGameService.LegalSans(moves); }
        catch (ArgumentException ex) { return (null, "illegal", ex.Message); }
        var now = _now();
        var year = req.Year is { } y && y >= 1900 && y <= now.Year + 1 ? y : (int?)null;
        var lk = await LookupsAsync(await RosterAsync(ct), new[] { req.White, req.Black }, new[] { req.WhiteFide, req.BlackFide }, ct);
        var w = Decided(new LeagueClubSideDecision { Name = req.White, Fide = req.WhiteFide, Replace = req.WhiteReplace }, req.White, null, req.WhiteElo, lk);
        var b = Decided(new LeagueClubSideDecision { Name = req.Black, Fide = req.BlackFide, Replace = req.BlackReplace }, req.Black, null, req.BlackElo, lk);
        var (game, reason) = Build(w, b, sans, year, req.Result, req.Event);
        if (game == null) return (null, reason, null);
        if (await IsDuplicateAsync(game, new(), ct)) return (null, "duplicate", null);
        using var lease = shareHash is null ? null : _shareQuota.Reserve(shareHash, 1);
        if (lease is { Granted: 0 })
        {
            _log.LogWarning("Vereins-Datenbank: Teilen-Link {Link} am Deckel — {Refused} Partien nicht übernommen", shareHash![..8], 1);
            return (null, ShareLimitReason, null);
        }
        if (lease != null) lease.Kept = 1;
        Stamp(game, userId, now, shareHash);
        await SaveAsync(new List<LeagueClubGame> { game }, ct);
        _log.LogInformation("Vereins-Datenbank: eine Partie aus einem Partieformular ({Anon}{Via})",
            game.Anonymized ? "mit „Schwaz“" : "mit Namen", userId == null ? ", Teilen-Link" : "");
        return (game, null, null);
    }

    /// <summary>Grund für Partien über dem Deckel der Teilen-Link-Uploads (<see cref="LeagueShareUploadQuota"/>).</summary>
    public const string ShareLimitReason = "shareLimit";

    /// <summary>Herkunft: der Teilen-Link (als Hash) IMMER, auch bei „Schwaz" — sonst entfernte der Rückbau
    /// (<see cref="DeleteByShareAsync"/>) genau diese Partien nicht. Zeitpunkt und Hochladender NUR bei Partien ohne
    /// „Schwaz" (siehe Klassenkommentar), der Hochladende nur mit Konto — ein Teilen-Link speichert nie, wer es war.</summary>
    private static void Stamp(LeagueClubGame g, int? userId, DateTime now, string? shareHash)
    {
        g.UploadShareHash = shareHash;
        if (g.Anonymized) return;
        g.UploadedByUserId = userId;
        g.CreatedAt = now;
    }

    private async Task SaveAsync(List<LeagueClubGame> games, CancellationToken ct)
    {
        if (games.Count == 0) return;
        _db.LeagueClubGames.AddRange(games);
        await _db.SaveChangesAsync(ct);
        await RefreshCardsAsync(games.SelectMany(g => new[] { g.WhiteFide, g.BlackFide }), ct);
    }

    /// <summary>Spielerkarten der betroffenen Ligaspieler neu rechnen und die Partienzahl in den Ansichten nachziehen.</summary>
    private async Task RefreshCardsAsync(IEnumerable<string?> fides, CancellationToken ct)
    {
        var ids = fides.Where(f => !string.IsNullOrEmpty(f)).Select(f => f!).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return;
        var store = new LeagueProfileStore(_db);
        var now = _now();
        foreach (var f in ids) await store.RebuildAsync(f, ct, now: now);
        await _db.SaveChangesAsync(ct);
        await store.PatchViewCountsAsync(ids, ct);
    }

    // ── Lesen ───────────────────────────────────────────────────────

    private IQueryable<LeagueClubGame> Filter(string? fide, string? q)
    {
        var query = _db.LeagueClubGames.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fide)) query = query.Where(g => g.WhiteFide == fide || g.BlackFide == fide);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(g => g.White.Contains(t) || g.Black.Contains(t) || (g.Event != null && g.Event.Contains(t)));
        }
        return query;
    }

    public async Task<LeagueClubListDto> ListAsync(int userId, bool canManage, string? fide, string? q, int page, CancellationToken ct)
    {
        var query = Filter(fide, q);
        var total = await query.CountAsync(ct);
        page = Math.Max(1, page);
        var rows = await query.OrderByDescending(g => g.Year ?? 0).ThenByDescending(g => g.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var items = rows.Select(g => ToDto(g, userId, canManage)).ToList();
        await FillAnalysisAsync(items, ct);
        await FillRosterAsync(items, ct);
        return new LeagueClubListDto { Total = total, Page = page, PageSize = PageSize, Items = items };
    }

    /// <summary>Seiten OHNE FIDE-ID, deren Name in einer Meldeliste steht (0.594.0): ein Ligaspieler, der selbst keine
    /// FIDE-ID hat (Kinsiz, Atlas bei Schach ohne Grenzen) — da gibt es nichts zuzuordnen, die Seite zeigt den Bleistift
    /// nur bei Namen, die niemand kennt. Die Meldelisten werden nur geladen, wenn die Seite so eine Zeile hat.</summary>
    private async Task FillRosterAsync(List<LeagueClubGameDto> items, CancellationToken ct)
    {
        bool Open(string name, string? fide, bool anonymized) => fide == null && !(anonymized && name == AnonymousName);
        if (!items.Any(i => Open(i.White, i.WhiteFide, i.Anonymized) || Open(i.Black, i.BlackFide, i.Anonymized))) return;
        var roster = await RosterAsync(ct);
        foreach (var i in items)
        {
            i.WhiteInRoster = Open(i.White, i.WhiteFide, i.Anonymized) && roster.Match(i.White, null).League;
            i.BlackInRoster = Open(i.Black, i.BlackFide, i.Anonymized) && roster.Match(i.Black, null).League;
        }
    }

    // ── Analyse (0.593.0, Wunsch „die Partien sollen allen aus dem Verein zur Verfügung stehen") ──

    /// <summary>Die Analyse einer Vereinspartie: die jüngste mit <see cref="GameAnalysisOrigin.Club"/>. Lesen darf, wer die
    /// Vereinspartien sieht (<c>league.view</c>) — die Analyse gehört dem Haus-Engine-Konto, der Zugang hängt an der Partie.</summary>
    private IQueryable<GameAnalysis> ClubAnalyses(int clubGameId) =>
        _db.GameAnalyses.AsNoTracking()
            .Where(a => a.LeagueClubGameId == clubGameId && a.Origin == GameAnalysisOrigin.Club)
            .OrderByDescending(a => a.Id);

    /// <summary>Stand der Analyse je Zeile (Fortschritt, Genauigkeit) — zwei Abfragen für die ganze Seite.</summary>
    private async Task FillAnalysisAsync(List<LeagueClubGameDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var ids = items.Select(i => i.Id).ToList();
        var links = await _db.GameAnalyses.AsNoTracking()
            .Where(a => a.LeagueClubGameId != null && ids.Contains(a.LeagueClubGameId.Value)
                && a.Origin == GameAnalysisOrigin.Club)
            .Select(a => new { Game = a.LeagueClubGameId!.Value, a.Id })
            .ToListAsync(ct);
        var latest = links.GroupBy(l => l.Game).ToDictionary(g => g.Key, g => g.Max(l => l.Id));
        var states = await GameEvalsStore.StatesAsync(_db, latest.Values.ToList(), SavedGameService.AccuracyBackfillPerCall, ct);
        foreach (var item in items)
            if (latest.TryGetValue(item.Id, out var analysisId) && states.TryGetValue(analysisId, out var state))
                item.Analysis = state;
    }

    /// <summary>Eine Vereinspartie (samt PGN) mit frischem Stand der Analyse; <c>null</c> = gibt es nicht.</summary>
    public async Task<LeagueClubGameDto?> GetAsync(int userId, bool canManage, int id, CancellationToken ct = default)
    {
        var g = await _db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return null;
        var dto = ToDto(g, userId, canManage);
        await FillAnalysisAsync(new List<LeagueClubGameDto> { dto }, ct);
        return dto;
    }

    /// <summary>Bewertungen der Vereinspartie für Kurve, Genauigkeit und Zug-Klassen (dieselben wie in „Meine Partien",
    /// <see cref="GameEvalsStore"/>) — ohne Buchzüge: die hingen an den Repertoires des Betrachters. <c>null</c> = Partie
    /// unbekannt; eine Partie ohne Analyse bekommt ein leeres Ergebnis (Status <c>none</c>).</summary>
    public async Task<GameEvalsDto?> EvalsAsync(int id, CancellationToken ct = default)
    {
        if (!await _db.LeagueClubGames.AnyAsync(g => g.Id == id, ct)) return null;
        var head = await GameEvalsStore.Heads(ClubAnalyses(id)).FirstOrDefaultAsync(ct);
        return head is null ? new GameEvalsDto() : await GameEvalsStore.ReadAsync(_db, head, null, ct);
    }

    internal static LeagueClubGameDto ToDto(LeagueClubGame g, int userId, bool canManage) => new()
    {
        Id = g.Id, Year = g.Year, White = g.White, Black = g.Black, WhiteFide = g.WhiteFide, BlackFide = g.BlackFide,
        WhiteElo = g.WhiteElo, BlackElo = g.BlackElo, Result = g.Result, Event = g.Event, Plies = g.Plies,
        Opening = OpeningOf(g.Pgn), Anonymized = g.Anonymized, CanDelete = CanDelete(g, userId, canManage), Uci = UciOf(g.Pgn), Pgn = g.Pgn,
    };

    /// <summary>Die Hauptvariante als UCI („e2e4 e7e5 …") — für den Knopf „Analyse", der RookHubs Analysebrett mit
    /// <c>?moves=</c> öffnet (Wunsch 2026-09-28). Gespeichert wird nur ab der Grundstellung.</summary>
    internal static string UciOf(string pgn)
    {
        var moveText = PgnParser.SplitGames(pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty;
        try { return string.Join(' ', PgnParser.TryExtractUciMainline(new Chess.ChessBoard().ToFen(), moveText) ?? new List<string>()); }
        catch (Exception) { return string.Empty; }
    }

    // ── Korrigieren ─────────────────────────────────────────────────

    /// <summary>
    /// Namen und Ergebnis einer gespeicherten Partie korrigieren (Wunsch 2026-09-28: „auf /verein die Namen anpassen,
    /// Ergebnis soll auch anpassbar sein"). Wer darf: wie beim Löschen. Eine Seite „Schwaz" bleibt anonym (<c>anonymous</c>).
    /// Eine geänderte Seite wird abgeglichen wie beim Import (Ligaspieler, Megabase, gemerkte Zuordnung); die unveränderte
    /// gilt weiter als bekannt. Es gelten dieselben Regeln wie beim Hochladen (<c>noLeaguePlayer</c>, <c>onlyOwnClub</c>).
    /// War ein Name vorher niemandem zugeordnet (ohne FIDE-ID), wird die Zuordnung gemerkt — die nächste Übersicht erkennt ihn.
    /// Ist der neue Spieler einer von Schwaz (oder setzt der Nutzer „ersetzen"), wird die Seite zu „Schwaz" — wie beim
    /// Hochladen, und dann fällt auch der Hochladende weg (gemeldet 2026-09-28: korrigiert auf „Oberschmid, Patrik", der
    /// Name blieb stehen). Eine Korrektur kann nur anonymisieren, nie einen Namen zurückholen.
    /// </summary>
    public async Task<(LeagueClubGame? Game, string? Reason)> UpdateAsync(int userId, bool canManage, int id,
        LeagueClubGameUpdateRequest req, CancellationToken ct = default)
    {
        var g = await _db.LeagueClubGames.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return (null, "notFound");
        if (!CanDelete(g, userId, canManage)) return (null, "forbidden");
        bool Anon(string name, string? fide) => g.Anonymized && fide == null && name == AnonymousName;
        if ((req.White != null && Anon(g.White, g.WhiteFide)) || (req.Black != null && Anon(g.Black, g.BlackFide)))
            return (null, "anonymous");
        if (req.Result is { } r0 && !Results.Contains(r0)) return (null, "invalidResult");
        var lk = await LookupsAsync(await RosterAsync(ct), new[] { g.White, g.Black, req.White?.Name, req.Black?.Name },
            new[] { g.WhiteFide, g.BlackFide, req.White?.Fide, req.Black?.Fide }, ct);
        Side Current(string name, string? fide, int? elo) => Anon(name, fide)
            ? new Side(name, LeagueRosterIndex.None, null, true)
            : new Side(name, LeagueRosterIndex.None, elo, false, fide, new LeagueMegaPlayers.Hit(name, fide));   // war schon angenommen
        Side Changed(LeagueClubSideDecision d, string name, string? fide, int? elo)
        {
            var s = Decided(d, name, fide, elo, lk);
            return s with { Replace = d.Replace || s.Hit.OwnClub };
        }
        var w = req.White is { } dw ? Changed(dw, g.White, g.WhiteFide, g.WhiteElo) : Current(g.White, g.WhiteFide, g.WhiteElo);
        var b = req.Black is { } db ? Changed(db, g.Black, g.BlackFide, g.BlackElo) : Current(g.Black, g.BlackFide, g.BlackElo);
        var moveText = PgnParser.SplitGames(g.Pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty;
        var sans = PgnParser.ExtractMainlineSans(moveText);
        var (built, reason) = Build(w, b, sans, g.Year, req.Result ?? g.Result, g.Event);
        if (built == null) return (null, reason);
        var before = new[] { g.WhiteFide, g.BlackFide };
        var remember = new List<(string Raw, LeagueNameAliases.Entry Target)>();
        if (req.White != null && g.WhiteFide == null && w.Identity is { } iw) remember.Add((g.White, iw));
        if (req.Black != null && g.BlackFide == null && b.Identity is { } ib) remember.Add((g.Black, ib));
        (g.White, g.Black, g.WhiteFide, g.BlackFide, g.WhiteElo, g.BlackElo, g.Result, g.Event, g.Pgn) =
            (built.White, built.Black, built.WhiteFide, built.BlackFide, built.WhiteElo, built.BlackElo, built.Result, built.Event, built.Pgn);
        if (built.Anonymized && !g.Anonymized)
        {
            // Jetzt mit „Schwaz": weder wer hochgeladen hat noch wann (Klassenkommentar).
            g.Anonymized = true;
            g.UploadedByUserId = null;
            g.CreatedAt = null;
        }
        await _db.SaveChangesAsync(ct);
        // Die Analyse der Partie trägt deren Namen — ein „Schwaz" muss auch dort ankommen.
        if (_analyses != null) await _analyses.SyncClubGameAsync(g, ct);
        await RefreshCardsAsync(before.Concat(new[] { g.WhiteFide, g.BlackFide }), ct);
        await RememberAsync(remember, ct);
        _log.LogInformation("Vereins-Datenbank: Partie {Id} korrigiert", g.Id);
        return (g, null);
    }

    private static bool CanDelete(LeagueClubGame g, int userId, bool canManage) =>
        canManage || (!g.Anonymized && g.UploadedByUserId == userId);

    internal static string OpeningOf(string pgn)
    {
        var moveText = PgnParser.SplitGames(pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty;
        return OpeningOfSans(PgnParser.ExtractMainlineSans(moveText));
    }

    internal static string OpeningOfSans(IEnumerable<string> all)
    {
        var sans = all.Take(6).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < sans.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append('.');
            sb.Append(sans[i]);
        }
        return sb.ToString();
    }

    public async Task<string> ExportAsync(string? fide, string? q, CancellationToken ct)
    {
        var pgns = await Filter(fide, q).OrderByDescending(g => g.Year ?? 0).ThenByDescending(g => g.Id)
            .Select(g => g.Pgn).ToListAsync(ct);
        return string.Join("\n", pgns.Select(p => p.TrimEnd() + "\n"));
    }

    /// <summary>Spieler zum Korrigieren eines Namens: die Ligaspieler, mit <paramref name="all"/> dazu das
    /// Spielerverzeichnis der ganzen Megabase (Treffer mit der FIDE-ID eines Ligaspielers gelten als dieser).</summary>
    public async Task<List<LeagueRosterPersonDto>> SuggestAsync(string q, bool all, CancellationToken ct)
    {
        var roster = await RosterAsync(ct);
        var list = roster.Suggest(q, 15).Select(PersonDto).ToList();
        if (!all) return list;
        var seen = list.Where(p => p.Fide != null).Select(p => p.Fide!).ToHashSet(StringComparer.Ordinal);
        foreach (var m in await new LeagueMegaPlayers(_db).SearchAsync(q, 25, ct))
        {
            if (m.FideId != null && !seen.Add(m.FideId)) continue;
            var league = roster.ByFide(m.FideId);
            list.Add(new LeagueRosterPersonDto
            {
                Name = league?.Name ?? m.Name, Fide = m.FideId, Teams = league?.Teams.Take(3).ToList() ?? new(),
                Club = league?.OwnClub ?? false, League = league != null, Source = "mega",
                Games = m.Games, LastYear = m.LastYear, MaxElo = m.MaxElo,
            });
        }
        return list;
    }

    public async Task<LeagueClubMatchDto> MatchAsync(string? white, string? black, CancellationToken ct)
    {
        var lk = await LookupsAsync(await RosterAsync(ct), new[] { white, black }, Array.Empty<string?>(), ct);
        var similar = new Dictionary<string, List<LeagueRosterPersonDto>>();
        LeagueClubSideMatchDto One(string? name)
        {
            var (hit, mega, alias) = Resolve(name, null, lk);
            var m = MatchDto(hit, mega, alias);
            AddSimilar(m, name, lk.Roster, similar);
            return m;
        }
        return new LeagueClubMatchDto { White = One(white), Black = One(black) };
    }

    // ── Löschen ─────────────────────────────────────────────────────

    public enum DeleteResult { Deleted, NotFound, Forbidden }

    public async Task<DeleteResult> DeleteAsync(int userId, bool canManage, int id, CancellationToken ct = default)
    {
        var g = await _db.LeagueClubGames.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return DeleteResult.NotFound;
        if (!CanDelete(g, userId, canManage)) return DeleteResult.Forbidden;
        // Erst die Analyse (sie trägt die Namen der Partie und hat vielleicht noch Aufträge offen), dann die Partie.
        if (_analyses != null) await _analyses.DeleteForClubGameAsync(g.Id, ct);
        _db.LeagueClubGames.Remove(g);
        await _db.SaveChangesAsync(ct);
        await RefreshCardsAsync(new[] { g.WhiteFide, g.BlackFide }, ct);
        return DeleteResult.Deleted;
    }

    /// <summary>
    /// „Alle Partien dieses Links entfernen" (Verwalter, Codereview 2026-09-29, A2-009): jede Partie, die über den
    /// Teilen-Link <paramref name="shareToken"/> hochgeladen wurde — auch anonymisierte und auch nach Ablauf des Links —,
    /// samt Analyse; die Spielerkarten werden neu gerechnet. <paramref name="dryRun"/> zählt nur. → Anzahl.
    /// </summary>
    public async Task<int> DeleteByShareAsync(string shareToken, bool dryRun, CancellationToken ct = default)
    {
        var hash = ShareHashOf(shareToken);
        if (dryRun) return await _db.LeagueClubGames.CountAsync(g => g.UploadShareHash == hash, ct);
        var games = await _db.LeagueClubGames.Where(g => g.UploadShareHash == hash).ToListAsync(ct);
        if (games.Count == 0) return 0;
        if (_analyses != null)
            foreach (var g in games) await _analyses.DeleteForClubGameAsync(g.Id, ct);
        _db.LeagueClubGames.RemoveRange(games);
        await _db.SaveChangesAsync(ct);
        await RefreshCardsAsync(games.SelectMany(g => new[] { g.WhiteFide, g.BlackFide }), ct);
        _log.LogInformation("Vereins-Datenbank: {Count} Partien des Teilen-Links {Link} entfernt", games.Count, hash[..8]);
        return games.Count;
    }
}
