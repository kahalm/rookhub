using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.League.ClubPgnText;
using static RookHub.Api.Services.League.ClubSideResolver;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die Lesewege der Vereins-Datenbank (aus <see cref="LeagueClubService"/> gezogen, Zerlegung Z14): der gecachte
/// Meldelisten-Index und was ein Abgleich nachschlägt, Liste, Einzelpartie, Analyse-Stand, Export, Namensvorschläge und
/// Brettpaarungs-Vorschläge. Jeder Weg beginnt bei <see cref="GamesOf"/> — nur die Partien des Vereins der Anfrage.
/// </summary>
internal sealed class ClubGameReader
{
    public const int PageSize = 50;

    private readonly AppDbContext _db;
    private readonly ILogger _log;
    private readonly IMemoryCache? _cache;
    private readonly LeaguePairingFinder _pairings;

    /// <summary>So lange gilt ein gecachter Meldelisten-Index höchstens (<see cref="RosterAsync"/>) — auch falls eine Änderung
    /// den Schlüssel einmal nicht träfe.</summary>
    internal static readonly TimeSpan RosterCacheTime = TimeSpan.FromMinutes(10);

    public ClubGameReader(AppDbContext db, ILogger log, IMemoryCache? cache, LeaguePairingFinder pairings)
    {
        _db = db; _log = log; _cache = cache; _pairings = pairings;
    }

    /// <summary>Die Partien EINES Vereins — jeder Lese- und Schreibweg der Vereins-Datenbank beginnt hier (Mandanten-Schritt
    /// 2026-10-07; bewusst kein globaler Filter, siehe <c>LeagueClubGameConfiguration</c>).</summary>
    internal static IQueryable<LeagueClubGame> GamesOf(AppDbContext db, LeagueClub club) => db.LeagueClubGames.Where(g => g.ClubId == club.Id);

    private IQueryable<LeagueClubGame> Games(LeagueClub club) => GamesOf(_db, club);

    private sealed record RosterKey(int Club, string Prefix, int Players, int? MaxPlayerId, int Tournaments);

    /// <summary>
    /// Die Meldelisten aller Saisonen als Abgleich-Index. Gecacht (Codereview 2026-09-29, F7-007): Suche und Namensabgleich
    /// fragen das bei jeder Tipp-Pause, auch ohne Konto über den Teilen-Link — vorher las jeder Aufruf alle Meldelisten-Zeilen
    /// und baute den Index neu. Schlüssel: Anzahl und höchste Id der Meldelisten-Zeilen und Anzahl der Ligen. Meldelisten
    /// werden nie an Ort und Stelle geändert, nur gelöscht und neu angelegt (<see cref="LeagueRefresh.ReplaceAsync"/> und die
    /// Quell-Leser) — jede Änderung bringt neue Ids, der Index wird neu gebaut.
    /// <para>Je VEREIN (Mandanten-Schritt 2026-10-07): wer „einer von uns" ist (<see cref="LeagueRosterIndex.Person.OwnClub"/>),
    /// sagt <see cref="LeagueClub.OwnsTeam"/> — der Index gilt für alle Ligen und Saisonen, aber je Verein.</para>
    /// </summary>
    public async Task<LeagueRosterIndex> RosterAsync(LeagueClub club, CancellationToken ct)
    {
        RosterKey? key = null;
        if (_cache != null)
        {
            key = new RosterKey(club.Id, club.TeamPrefix, await _db.LeaguePlayers.CountAsync(ct), await _db.LeaguePlayers.MaxAsync(p => (int?)p.Id, ct),
                await _db.LeagueTournaments.CountAsync(ct));
            if (_cache.TryGetValue(key, out LeagueRosterIndex? hit) && hit != null) return hit;
        }
        var seasons = await _db.LeagueTournaments.AsNoTracking().Select(t => new { t.Tnr, t.Season })
            .ToDictionaryAsync(t => t.Tnr, t => t.Season, ct);
        var rows = await _db.LeaguePlayers.AsNoTracking().Select(p => new { p.Tnr, p.Team, p.Name, p.NameKey, p.FideId }).ToListAsync(ct);
        var roster = new LeagueRosterIndex(rows.Select(r => new LeagueRosterIndex.Row(r.Tnr, r.Team, r.Name, r.NameKey, r.FideId,
            seasons.GetValueOrDefault(r.Tnr) ?? "")), club.OwnsTeam);
        if (key != null) _cache!.Set(key, roster, RosterCacheTime);
        return roster;
    }

    /// <summary>Gemerkte Zuordnungen und das Megabase-Verzeichnis für diese Namen und FIDE-IDs — die Zuordnungen zuerst,
    /// ihre Spieler schlägt das Verzeichnis gleich mit nach.</summary>
    internal async Task<Lookups> LookupsAsync(LeagueRosterIndex roster, IEnumerable<string?> names, IEnumerable<string?> fides,
        CancellationToken ct)
    {
        var nameList = names.ToList();
        var aliases = await new LeagueNameAliases(_db).LoadAsync(nameList, ct);
        var mega = await new LeagueMegaPlayers(_db).LookupAsync(nameList.Concat(aliases.Values.Select(a => (string?)a.Name)),
            fides.Concat(aliases.Values.Select(a => a.Fide)), ct);
        return new Lookups(roster, mega, aliases);
    }

    /// <summary>Wer hochlädt (Profil) — ohne Konto (Teilen-Link) niemand.</summary>
    internal async Task<Owner> OwnerAsync(int? userId, CancellationToken ct)
    {
        if (userId is not int uid) return Owner.Nobody;
        var p = await _db.UserProfiles.AsNoTracking().Where(x => x.UserId == uid)
            .Select(x => new { x.FideId, x.LastName, x.DisplayName, x.FirstName }).FirstOrDefaultAsync(ct);
        return new Owner(string.IsNullOrWhiteSpace(p?.FideId) ? null : p!.FideId!.Trim(),
            new[] { p?.LastName, p?.DisplayName, p?.FirstName });
    }

    /// <summary>Vorschläge für eine Partie, die noch nicht gespeichert ist (Formular, 0.678.0) — Namen werden abgeglichen wie beim
    /// Hochladen, eine Seite „Schwaz" zählt als eigener Verein.</summary>
    public async Task<List<LeagueClubPairingDto>> SuggestPairingsAsync(LeagueClub club, LeagueClubPairingQuery q, CancellationToken ct)
    {
        var lk = await LookupsAsync(await RosterAsync(club, ct), new[] { q.White, q.Black }, new[] { q.WhiteFide, q.BlackFide }, ct);
        (string Name, string? Fide) One(string? name, string? fide)
        {
            if (!string.IsNullOrWhiteSpace(fide)) return (name ?? "", fide.Trim());
            if (string.IsNullOrWhiteSpace(name)) return ("", null);
            var (hit, mega, _) = Resolve(name, null, lk);
            return (name.Trim(), hit.Person?.Fide ?? mega?.Fide);
        }
        var (wn, wf) = One(q.White, q.WhiteFide);
        var (bn, bf) = One(q.Black, q.BlackFide);
        var date = FullDateOf(q.Date);
        var options = await _pairings.ForAsync(new LeaguePairingFinder.Query(wn, wf, bn, bf, date, date?.Year ?? q.Year), club, ct);
        return options.Select(LeagueClubPairingDto.Of).ToList();
    }

    /// <summary>Vorschläge für eine gespeicherte Partie (Bearbeiten) — nur wer sie bearbeiten darf; sonst <c>null</c>.</summary>
    public async Task<List<LeagueClubPairingDto>?> PairingsForGameAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct)
    {
        var g = await Games(club).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null || !ClubGameWriter.CanDelete(g, userId, canManage)) return null;
        var options = await _pairings.ForAsync(LeaguePairingFinder.QueryOf(g, null), club, ct);
        // die schon zugeordnete steht immer drin, auch wenn sie nicht mehr unter den Vorschlägen wäre
        // (über den Schlüssel aufgelöst — eine tote Id fände keine Paarung, 0.716.1)
        if (await LeagueGameLinks.FindAsync(_db, g, ct, _log) is { Id: var cur } && options.All(o => o.Id != cur)
            && await _pairings.ByIdAsync(cur, ct, club) is { } o0) options.Insert(0, o0);
        return options.Select(LeagueClubPairingDto.Of).ToList();
    }

    /// <summary>Der Verein einer Partie (<c>null</c> = gibt es nicht) — nur, damit RookHubs Partie-Seite ohne Vereinswahl den
    /// richtigen Verein vorschlagen kann (<see cref="LeagueClubResolver.ResolveAsync"/> prüft die Zugehörigkeit).</summary>
    public Task<int?> ClubOfGameAsync(int id, CancellationToken ct = default) =>
        _db.LeagueClubGames.AsNoTracking().Where(g => g.Id == id).Select(g => (int?)g.ClubId).FirstOrDefaultAsync(ct);

    private IQueryable<LeagueClubGame> Filter(LeagueClub club, string? fide, string? q)
    {
        var query = Games(club).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fide)) query = query.Where(g => g.WhiteFide == fide || g.BlackFide == fide);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(g => g.White.Contains(t) || g.Black.Contains(t) || (g.Event != null && g.Event.Contains(t)));
        }
        return query;
    }

    /// <param name="mine">Nur die eigenen (0.652.0, Lasche „Meine Partien"): mit Konto hochgeladen. Mit „Schwaz" hochgeladene
    /// stehen nicht dabei — bei denen ist absichtlich nicht gespeichert, von wem sie stammen.</param>
    public async Task<LeagueClubListDto> ListAsync(LeagueClub club, int userId, bool canManage, string? fide, string? q, int page, CancellationToken ct,
        bool mine = false)
    {
        var query = Filter(club, fide, q);
        if (mine) query = query.Where(g => g.UploadedByUserId == userId);
        var total = await query.CountAsync(ct);
        (page, _) = Paging.Normalize(page, PageSize, PageSize);
        var rows = await query.OrderByDescending(g => g.Year ?? 0).ThenByDescending(g => g.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var items = rows.Select(g => ToDto(g, userId, canManage)).ToList();
        await FillAnalysisAsync(items, ct);
        await FillRosterAsync(club, items, ct);
        await FillPairingLabelsAsync(club, items, ct);
        if (canManage) await FillDuplicatesAsync(club, rows, items, ct);   // UI-Sweep 2026-10-11 (r2-l-dup-game)
        return new LeagueClubListDto { Total = total, Page = page, PageSize = PageSize, Items = items };
    }

    /// <summary>Seiten OHNE FIDE-ID, deren Name in einer Meldeliste steht (0.594.0): ein Ligaspieler, der selbst keine
    /// FIDE-ID hat (Kinsiz, Atlas bei Schach ohne Grenzen) — da gibt es nichts zuzuordnen, die Seite zeigt den Bleistift
    /// nur bei Namen, die niemand kennt. Die Meldelisten werden nur geladen, wenn die Seite so eine Zeile hat.</summary>
    private async Task FillRosterAsync(LeagueClub club, List<LeagueClubGameDto> items, CancellationToken ct)
    {
        bool Open(string name, string? fide, bool anonymized) => fide == null && !(anonymized && club.IsAnon(name));
        if (!items.Any(i => Open(i.White, i.WhiteFide, i.Anonymized) || Open(i.Black, i.BlackFide, i.Anonymized))) return;
        var roster = await RosterAsync(club, ct);
        foreach (var i in items)
        {
            i.WhiteInRoster = Open(i.White, i.WhiteFide, i.Anonymized) && roster.Match(i.White, null).League;
            i.BlackInRoster = Open(i.Black, i.BlackFide, i.Anonymized) && roster.Match(i.Black, null).League;
        }
    }

    /// <summary>
    /// UI-Sweep 2026-10-11 (r2-l-dup-game): „möglicherweise doppelt" — nur für Verwalter. Zwei Fassungen derselben Partie
    /// (Regel wie <see cref="ClubGameWriter.ArchiveOlderVersionsAsync"/>) blieben stehen, wenn sie vor 0.691.0 eingegeben wurden (Anlass
    /// 153/164, für die das Archivieren gebaut wurde, nur greift es erst beim NÄCHSTEN Formular) oder über den PGN-Import
    /// kamen (dort bewusst kein automatisches Archivieren: viele Partien, kein Mensch schaut hin). Je Zeile die Ids der
    /// anderen lebenden Fassungen; der Verwalter entscheidet mit „ältere archivieren" (<see cref="ClubGameWriter.ArchiveOlderAsync"/>).
    /// </summary>
    private async Task FillDuplicatesAsync(LeagueClub club, List<LeagueClubGame> rows, List<LeagueClubGameDto> items, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var years = rows.Select(r => r.Year).Distinct().ToList();
        var names = rows.SelectMany(r => new[] { r.White, r.Black }).Distinct().ToList();
        var fides = rows.SelectMany(r => new[] { r.WhiteFide, r.WhiteRealFide, r.BlackFide, r.BlackRealFide })
            .Where(f => f != null).Distinct().ToList();
        var ids = rows.Select(r => r.Id).ToList();
        var cands = await Games(club).AsNoTracking().Where(c => !ids.Contains(c.Id) && years.Contains(c.Year)
                && (names.Contains(c.White) || fides.Contains(c.WhiteFide) || fides.Contains(c.WhiteRealFide))
                && (names.Contains(c.Black) || fides.Contains(c.BlackFide) || fides.Contains(c.BlackRealFide)))
            .ToListAsync(ct);
        var all = rows.Concat(cands).ToList();   // auch zwei Fassungen auf derselben Seite
        var live = await LeagueGameLinks.ResolveAsync(_db, all, ct, _log);
        var sans = new Dictionary<int, IReadOnlyList<string>>();
        IReadOnlyList<string> Sans(LeagueClubGame g) => sans.TryGetValue(g.Id, out var s) ? s : sans[g.Id] = SansOf(g.Pgn);
        var byId = items.ToDictionary(i => i.Id);
        foreach (var r in rows)
        {
            var mine = live.GetValueOrDefault(r.Id)?.Id;
            var dups = all.Where(c => c.Id != r.Id && SameVersion(c, live.GetValueOrDefault(c.Id)?.Id, r, mine, Sans(c), Sans(r)))
                .Select(c => c.Id).Distinct().OrderBy(x => x).ToList();
            if (dups.Count > 0 && byId.TryGetValue(r.Id, out var dto)) dto.DuplicateIds = dups;
        }
    }

    // ── Analyse (0.593.0, Wunsch „die Partien sollen allen aus dem Verein zur Verfügung stehen") ──

    /// <summary>Die Analyse einer Vereinspartie: die jüngste mit <see cref="GameAnalysisOrigin.Club"/>. Lesen darf, wer die
    /// Vereinspartien sieht (<c>league.view</c>) — die Analyse gehört dem Haus-Engine-Konto, der Zugang hängt an der Partie.</summary>
    private IQueryable<GameAnalysis> ClubAnalyses(int clubGameId) =>
        _db.GameAnalyses.AsNoTracking()
            .Where(a => a.LeagueClubGameId == clubGameId && a.Origin == GameAnalysisOrigin.Club && a.EngineId == null)
            .OrderByDescending(a => a.Id);

    /// <summary>Stand der Analyse je Zeile (Fortschritt, Genauigkeit) — zwei Abfragen für die ganze Seite.</summary>
    private async Task FillAnalysisAsync(List<LeagueClubGameDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var ids = items.Select(i => i.Id).ToList();
        var links = await _db.GameAnalyses.AsNoTracking()
            .Where(a => a.LeagueClubGameId != null && ids.Contains(a.LeagueClubGameId.Value)
                && a.Origin == GameAnalysisOrigin.Club && a.EngineId == null)
            .Select(a => new { Game = a.LeagueClubGameId!.Value, a.Id })
            .ToListAsync(ct);
        var latest = links.GroupBy(l => l.Game).ToDictionary(g => g.Key, g => g.Max(l => l.Id));
        var states = await GameEvalsStore.StatesAsync(_db, latest.Values.ToList(), SavedGameService.AccuracyBackfillPerCall, ct);
        foreach (var item in items)
            if (latest.TryGetValue(item.Id, out var analysisId) && states.TryGetValue(analysisId, out var state))
                item.Analysis = state;
    }

    /// <summary>Eine Vereinspartie (samt PGN) mit frischem Stand der Analyse; <c>null</c> = gibt es nicht.</summary>
    public async Task<LeagueClubGameDto?> GetAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default)
    {
        var g = await Games(club).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return null;
        var dto = ToDto(g, userId, canManage);
        await FillAnalysisAsync(new List<LeagueClubGameDto> { dto }, ct);
        await FillPairingLabelsAsync(club, new List<LeagueClubGameDto> { dto }, ct);
        return dto;
    }

    /// <summary>Bewertungen der Vereinspartie für Kurve, Genauigkeit und Zug-Klassen (dieselben wie in „Meine Partien",
    /// <see cref="GameEvalsStore"/>) — ohne Buchzüge: die hingen an den Repertoires des Betrachters. <c>null</c> = Partie
    /// unbekannt; eine Partie ohne Analyse bekommt ein leeres Ergebnis (Status <c>none</c>).</summary>
    public async Task<GameEvalsDto?> EvalsAsync(LeagueClub club, int id, CancellationToken ct = default)
    {
        if (!await Games(club).AnyAsync(g => g.Id == id, ct)) return null;
        var head = await GameEvalsStore.Heads(ClubAnalyses(id)).FirstOrDefaultAsync(ct);
        return head is null ? new GameEvalsDto() : await GameEvalsStore.ReadAsync(_db, head, null, ct);
    }

    /// <summary>Höchstzahl Partien je <see cref="ReviewInputsAsync"/>-Aufruf.</summary>
    public const int MaxReviewInputs = 60;

    /// <summary>
    /// PGN + Bewertungen mehrerer Vereinspartien des Vereins in EINEM Aufruf (0.849.0, Vereinsübersicht: „!!"-Marker) — der
    /// Browser rechnet daraus die Zug-Klassen wie auf der Partieseite. Schlank: ohne Kandidatenlisten, Varianten und Buchzüge
    /// (die Klassen brauchen nur Bester/Gespielter/Zweitbester). Nur Partien MIT Analyse; fremde und unbekannte Ids fehlen.
    /// </summary>
    public async Task<Dictionary<int, ClubGameReviewInput>> ReviewInputsAsync(LeagueClub club, IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<int, ClubGameReviewInput>();
        if (ids.Count == 0) return result;
        var games = await Games(club).AsNoTracking().Where(g => ids.Contains(g.Id)).Select(g => new { g.Id, g.Pgn }).ToListAsync(ct);
        var gameIds = games.Select(g => g.Id).ToList();
        var latest = (await _db.GameAnalyses.AsNoTracking()
                .Where(a => a.LeagueClubGameId != null && gameIds.Contains(a.LeagueClubGameId.Value)
                    && a.Origin == GameAnalysisOrigin.Club && a.EngineId == null)
                .Select(a => new { Game = a.LeagueClubGameId!.Value, a.Id })
                .ToListAsync(ct))
            .GroupBy(a => a.Game).ToDictionary(g => g.Key, g => g.Max(a => a.Id));
        foreach (var g in games)
        {
            if (!latest.TryGetValue(g.Id, out var analysisId)) continue;
            var head = await GameEvalsStore.Heads(_db.GameAnalyses.AsNoTracking().Where(a => a.Id == analysisId)).FirstAsync(ct);
            var evals = await GameEvalsStore.ReadAsync(_db, head, null, ct);
            if (evals.Plies.Count == 0) continue;
            foreach (var p in evals.Plies) p.Candidates = new();
            result[g.Id] = new ClubGameReviewInput(g.Pgn, evals);
        }
        return result;
    }

    internal static LeagueClubGameDto ToDto(LeagueClubGame g, int userId, bool canManage) => new()
    {
        Id = g.Id, Year = g.Year, White = g.White, Black = g.Black, WhiteFide = g.WhiteFide, BlackFide = g.BlackFide,
        WhiteElo = g.WhiteElo, BlackElo = g.BlackElo, Result = g.Result, Event = g.Event, Plies = g.Plies,
        Opening = OpeningOf(g.Pgn), Anonymized = g.Anonymized, CanDelete = ClubGameWriter.CanDelete(g, userId, canManage), Uci = UciOf(g.Pgn), Pgn = g.Pgn,
        LeagueGameId = ClubGameWriter.CanDelete(g, userId, canManage) ? g.LeagueGameId : null,
        LeaguePairingProvisional = ClubGameWriter.CanDelete(g, userId, canManage) && g.LeaguePairingProvisional,
    };

    /// <summary>Die Bezeichnung der zugeordneten Paarung („2026/27 · Landesliga · Runde 2 · Brett 4") — nur wo sie gezeigt werden
    /// darf (<see cref="LeagueClubGameDto.LeagueGameId"/> ist dann gesetzt).</summary>
    private async Task FillPairingLabelsAsync(LeagueClub club, List<LeagueClubGameDto> items, CancellationToken ct)
    {
        var ids = items.Where(i => i.LeagueGameId != null).Select(i => i.Id).ToList();
        if (ids.Count == 0) return;
        // Die gespeicherte Id kann tot sein (vor 0.716.1 legte jedes Aktualisieren die Paarungen neu an): über den Schlüssel
        // auflösen, sonst gilt die Partie als nicht zugeordnet — die Auswahl zeigte sonst eine Paarung, die es nicht gibt.
        var games = await _db.LeagueClubGames.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var live = await LeagueGameLinks.ResolveAsync(_db, games, ct, _log);
        foreach (var i in items.Where(i => i.LeagueGameId != null))
        {
            i.LeagueGameId = live.GetValueOrDefault(i.Id)?.Id;
            if (i.LeagueGameId is { } id) i.LeagueGameLabel = (await _pairings.ByIdAsync(id, ct, club))?.Label;
        }
    }


    /// <summary>Darf <paramref name="userId"/> diese Vereinspartie korrigieren? (Hochladender oder Verwalter)</summary>
    public async Task<bool> CanCorrectAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default) =>
        await Games(club).AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct) is { } g && ClubGameWriter.CanDelete(g, userId, canManage);

    public async Task<string> ExportAsync(LeagueClub club, string? fide, string? q, CancellationToken ct)
    {
        var pgns = await Filter(club, fide, q).OrderByDescending(g => g.Year ?? 0).ThenByDescending(g => g.Id)
            .Select(g => g.Pgn).ToListAsync(ct);
        return string.Join("\n", pgns.Select(p => p.TrimEnd() + "\n"));
    }

    /// <summary>Spieler zum Korrigieren eines Namens: die Ligaspieler, mit <paramref name="all"/> dazu das
    /// Spielerverzeichnis der ganzen Megabase (Treffer mit der FIDE-ID eines Ligaspielers gelten als dieser).</summary>
    public async Task<List<LeagueRosterPersonDto>> SuggestAsync(LeagueClub club, string q, bool all, CancellationToken ct)
    {
        var roster = await RosterAsync(club, ct);
        var list = roster.Suggest(q, 15).Select(PersonDto).ToList();
        if (all) AddMega(list, roster, await new LeagueMegaPlayers(_db).SearchAsync(q, 25, ct));
        // Die Elo zum Vorbelegen (Wunsch 2026-10-05: „warum wird die nicht ausgefüllt, wenn ich den Spieler auswähle?"):
        // die der jüngsten Meldeliste (international, sonst national).
        var fides = list.Where(p => p.League && p.Fide != null).Select(p => p.Fide!).Distinct().ToList();
        if (fides.Count > 0)
        {
            var rows = await _db.LeaguePlayers.AsNoTracking().Where(p => p.FideId != null && fides.Contains(p.FideId))
                .Select(p => new { p.FideId, p.Tnr, Elo = p.EloI ?? p.EloN }).ToListAsync(ct);
            var latest = rows.Where(r => r.Elo is > 0).GroupBy(r => r.FideId!)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Tnr).First().Elo);
            foreach (var p in list)
                if (p.Fide != null && latest.TryGetValue(p.Fide, out var elo)) p.Elo = elo;
        }
        return list;
    }

    private static void AddMega(List<LeagueRosterPersonDto> list, LeagueRosterIndex roster, IEnumerable<LeagueMegaPlayer> mega)
    {
        var seen = list.Where(p => p.Fide != null).Select(p => p.Fide!).ToHashSet(StringComparer.Ordinal);
        foreach (var m in mega)
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
    }

    public async Task<LeagueClubMatchDto> MatchAsync(LeagueClub club, string? white, string? black, CancellationToken ct)
    {
        var lk = await LookupsAsync(await RosterAsync(club, ct), new[] { white, black }, Array.Empty<string?>(), ct);
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
}

/// <summary>PGN und Bewertungen einer Vereinspartie für einen Rückblick im Browser (<c>reviewGame</c>, 0.849.0).</summary>
public sealed record ClubGameReviewInput(string Pgn, GameEvalsDto Evals);
