using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.League.ClubPgnText;
using static RookHub.Api.Services.League.ClubSideResolver;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die Schreibwege der Vereins-Datenbank (aus <see cref="LeagueClubService"/> gezogen, Zerlegung Z14): Brettpaarung
/// zuordnen und bestätigen, speichern samt Spielerkarten, ältere Fassungen archivieren, Namen/Ergebnis und Züge
/// korrigieren, nach dem Anmelden zuordnen, löschen (einzeln und je Teilen-Link). Die Einlesewege
/// (<see cref="ClubPgnReader"/>) speichern über <see cref="SaveAsync"/>, <see cref="ApplyPairingsAsync"/> und
/// <see cref="RebalanceAsync"/>.
/// </summary>
internal sealed class ClubGameWriter
{
    private readonly AppDbContext _db;
    private readonly ILogger _log;
    private readonly Func<DateTime> _now;
    private readonly GameAnalysisService? _analyses;
    private readonly LeaguePairingFinder _pairings;
    private readonly ClubGameReader _reader;

    public ClubGameWriter(AppDbContext db, ILogger log, Func<DateTime> now, GameAnalysisService? analyses,
        LeaguePairingFinder pairings, ClubGameReader reader)
    {
        _db = db; _log = log; _now = now; _analyses = analyses; _pairings = pairings; _reader = reader;
    }

    private IQueryable<LeagueClubGame> Games(LeagueClub club) => ClubGameReader.GamesOf(_db, club);

    /// <summary>Nach dem Speichern: die vorläufigen Zuordnungen (0.832.0) der Begegnungen dieser Partien gemeinsam neu verteilen —
    /// mit jeder weiteren Partie derselben Begegnung wird die Brettfolge genauer.</summary>
    internal Task<int> RebalanceAsync(LeagueClub club, IEnumerable<LeagueClubGame> games, CancellationToken ct) =>
        new LeagueProvisionalPairing(_db).RebalanceAsync(club, games, DateOnly.FromDateTime(_now()), ct, _log);

    /// <summary>
    /// Eine schon gespeicherte Vereinspartie OHNE Zuordnung nachträglich zuordnen (0.832.0) — wie beim Hinzufügen: der eindeutige
    /// genaue Treffer, sonst die vorläufige Zuordnung zur laufenden Runde. Für den manual-scan-Weg des Watchers mit
    /// <c>clubGameId</c> und den Nachtrag von Partien, die vor 0.832.0 eingelesen wurden. → ob es die Partie gibt, und die gesetzte Paarung.
    /// </summary>
    /// <param name="clubId">Nur eine Partie dieses Vereins (sonst <c>Found = false</c>).</param>
    public async Task<(bool Found, int? Id)> AssignPairingIfUnlinkedAsync(int clubGameId, CancellationToken ct = default, int? clubId = null)
    {
        var g = await _db.LeagueClubGames.FirstOrDefaultAsync(x => x.Id == clubGameId && (clubId == null || x.ClubId == clubId), ct);
        if (g is null) return (false, null);
        if (g.LeagueGameId != null || LeagueGameLinks.KeyOf(g) != null) return (true, null);
        var club = await _db.LeagueClubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == g.ClubId, ct);
        if (club is null) return (true, null);
        var date = FullDateOf(PgnParser.SplitGames(g.Pgn).FirstOrDefault().Headers?.GetValueOrDefault("Date"));
        await ApplyPairingsAsync(club, new[] { (g, (int?)null, date, SansOf(g.Pgn)) }, ct);
        if (g.LeagueGameId is null) return (true, null);
        await _db.SaveChangesAsync(ct);
        await RebalanceAsync(club, new[] { g }, ct);
        _log.LogInformation("Vereins-Datenbank: Partie {Id} nachträglich zugeordnet — Brettpaarung {Pairing}{Provisional}", g.Id,
            g.LeagueGameId, g.LeaguePairingProvisional ? " (vorläufig)" : "");
        return (true, g.LeagueGameId);
    }

    /// <summary>„Bestätigen" von Hand (0.832.0): die vorläufige Zuordnung gilt als fest. Nur wer die Partie bearbeiten darf.
    /// → <c>null</c> = unbekannt/anderer Verein, sonst ob sie vorher vorläufig war; <c>Reason</c> <c>forbidden</c>/<c>notLinked</c>.</summary>
    public async Task<(bool? Changed, string? Reason)> ConfirmPairingAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default)
    {
        var g = await Games(club).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return (null, "notFound");
        if (!CanDelete(g, userId, canManage)) return (null, "forbidden");
        if (g.LeagueGameId is null && LeagueGameLinks.KeyOf(g) is null) return (null, "notLinked");
        if (!g.LeaguePairingProvisional) return (false, null);
        g.LeaguePairingProvisional = false;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Vereins-Datenbank: vorläufige Ligapaarung der Partie {Id} von Hand bestätigt", g.Id);
        return (true, null);
    }

    /// <summary>Die Wahl der Paarung an die Partie: gewählt (<c>&gt; 0</c>), ausdrücklich keine (<c>0</c>) oder — fehlt die
    /// Angabe — die eindeutig erkannte. Ändert sich dabei das Jahr, wird das PGN neu geschrieben.</summary>
    internal async Task ApplyPairingsAsync(LeagueClub club,
        IReadOnlyList<(LeagueClubGame Game, int? Choice, DateOnly? Date, IReadOnlyList<string> Sans)> items, CancellationToken ct)
    {
        var auto = items.Where(x => x.Choice is null).ToList();
        var picks = auto.Count == 0 ? new List<List<LeaguePairingFinder.Option>>()
            : await _pairings.ForManyAsync(auto.Select(x => LeaguePairingFinder.QueryOf(x.Game, x.Date)).ToList(), ct, club);
        var autoIds = auto.Select((x, i) => (x.Game, LeaguePairingFinder.AutoPick(picks[i]))).ToDictionary(t => t.Game, t => t.Item2);
        foreach (var (game, choice, _, sans) in items)
        {
            var id = choice ?? autoIds.GetValueOrDefault(game);
            if (id is null) continue;
            var year = game.Year;
            await _pairings.ApplyAsync(game, id.Value, ct);
            if (game.Year != year) game.Pgn = PgnOf(game, sans);
        }
    }

    /// <summary>Der Vermerk eines Teilen-Links an seinen Partien: SHA-256 (hex) des Tokens — der Link selbst (144 Bit
    /// Zufall) lässt sich daraus nicht zurückgewinnen, der Verwalter mit dem Link findet seine Partien aber wieder.</summary>
    public static string ShareHashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();

    /// <summary>Gemerkte Zuordnungen speichern — NUR mit Konto (ein Teilen-Link soll nicht festlegen, wer ein Name für
    /// alle ist). Ein Fehler dabei (zwei gleichzeitige Uploads legen denselben Namen an) lässt den Import nicht scheitern.</summary>
    internal async Task<int> RememberAsync(List<(string Raw, LeagueNameAliases.Entry Target)> items, CancellationToken ct)
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

    /// <summary>Herkunft: der Teilen-Link (als Hash) IMMER, auch bei „Schwaz" — sonst entfernte der Rückbau
    /// (<see cref="DeleteByShareAsync"/>) genau diese Partien nicht. Zeitpunkt und Hochladender NUR bei Partien ohne
    /// „Schwaz" (siehe Klassenkommentar), der Hochladende nur mit Konto — ein Teilen-Link speichert nie, wer es war.</summary>
    internal static void Stamp(LeagueClubGame g, int? userId, DateTime now, string? shareHash, string? claimHash = null)
    {
        g.UploadShareHash = shareHash;
        if (userId is null) g.ClaimKeyHash = claimHash;   // auch bei „Schwaz": zugeordnet wird nur nach Zustimmung
        if (g.Anonymized) return;
        g.UploadedByUserId = userId;
        g.CreatedAt = now;
    }

    internal async Task SaveAsync(List<LeagueClubGame> games, CancellationToken ct)
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

    /// <summary>
    /// Ältere Fassungen DIESER Partie archivieren (2026-10-06, Wunsch: „wenn eine 2. Partie über ein Scoresheet hinzugefügt wird,
    /// die schon eingegeben ist, das alte archivieren, damit es nicht mehr aufscheint" — Anlass: Gruber–Schöler zweimal
    /// eingelesen, Partien 153 und 164). Dieselbe Partie heißt: dieselbe feste Ligapaarung, oder dasselbe Jahr, dieselben
    /// Spieler (FIDE-ID, auch die interne hinter „Schwaz", sonst der Name) und dieselben ersten
    /// <see cref="ClubSideResolver.SameGamePrefixPlies"/> Halbzüge. Die Analyse der alten Fassung geht (die neue wird gerechnet). Liefert die
    /// Zahl der archivierten.
    /// </summary>
    internal async Task<int> ArchiveOlderVersionsAsync(LeagueClubGame game, IReadOnlyList<string> sans, DateTime now, CancellationToken ct)
    {
        // Gleiche Paarung = gleiche Id ODER gleicher Schlüssel (Tnr, Runde, Begegnung, Brett) — eine ältere Fassung kann noch eine
        // Id von vor dem letzten Aktualisieren tragen (0.716.1).
        var (kt, kr, km, kb) = (game.LeagueTnr, game.LeagueRound, game.LeagueMatchNo, game.LeagueBoard);
        var cands = await _db.LeagueClubGames
            .Where(c => c.ClubId == game.ClubId)   // nur im eigenen Verein
            .Where(c => c.Id != game.Id && (game.LeagueGameId != null && c.LeagueGameId == game.LeagueGameId
                || kt != null && c.LeagueTnr == kt && c.LeagueRound == kr && c.LeagueMatchNo == km && c.LeagueBoard == kb
                || c.Year == game.Year && (c.White == game.White || game.WhiteFide != null && c.WhiteFide == game.WhiteFide)
                                       && (c.Black == game.Black || game.BlackFide != null && c.BlackFide == game.BlackFide)))
            .ToListAsync(ct);
        // Die Paarung jeder Kandidatin, wie sie HEUTE steht — eine tote Id zählt wie keine Zuordnung (darf nicht als „andere
        // feste Paarung" die Archivierung verhindern).
        // UI-Sweep 2026-10-11 (r2-l-dup-game): auch die eigene Paarung aufgelöst (eine tote Id ist keine feste Paarung) — die
        // Regel steht in SameVersion und gilt gleich für den Hinweis „möglicherweise doppelt" in der Liste.
        var live = await LeagueGameLinks.ResolveAsync(_db, cands.Append(game), ct, _log);
        var mine = live.GetValueOrDefault(game.Id)?.Id;
        var old = cands.Where(c => SameVersion(c, live.GetValueOrDefault(c.Id)?.Id, game, mine, SansOf(c.Pgn), sans)).ToList();
        if (old.Count == 0) return 0;
        foreach (var c in old)
        {
            c.ArchivedAt = now;
            c.ReplacedById = game.Id;
        }
        await _db.SaveChangesAsync(ct);
        if (_analyses != null) foreach (var c in old) await _analyses.DeleteForClubGameAsync(c.Id, ct);
        await RefreshCardsAsync(old.SelectMany(c => new[] { c.WhiteFide, c.BlackFide }), ct);
        _log.LogInformation("Vereins-Datenbank: Partie {Id} ersetzt ältere Fassung(en) {Old} — archiviert", game.Id,
            string.Join(",", old.Select(c => c.Id)));
        return old.Count;
    }

    /// <summary>UI-Sweep 2026-10-11 (r2-l-dup-game): Verwalter archiviert die anderen Fassungen dieser Partie (die behält er) —
    /// derselbe Weg wie nach einem Formular. <c>null</c> = gibt es (im Verein) nicht; sonst die Zahl der archivierten.</summary>
    public async Task<int?> ArchiveOlderAsync(LeagueClub club, int id, CancellationToken ct = default)
    {
        var g = await Games(club).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return null;
        return await ArchiveOlderVersionsAsync(g, SansOf(g.Pgn), _now(), ct);
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
    public async Task<(LeagueClubGame? Game, string? Reason)> UpdateAsync(LeagueClub club, int userId, bool canManage, int id,
        LeagueClubGameUpdateRequest req, CancellationToken ct = default)
    {
        var g = await Games(club).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return (null, "notFound");
        if (!CanDelete(g, userId, canManage)) return (null, "forbidden");
        bool Anon(string name, string? fide) => g.Anonymized && club.IsAnon(name, fide);
        if ((req.White != null && Anon(g.White, g.WhiteFide)) || (req.Black != null && Anon(g.Black, g.BlackFide)))
            return (null, "anonymous");
        if (req.Result is { } r0 && !ClubPgnText.Results.Contains(r0)) return (null, "invalidResult");
        var lk = await _reader.LookupsAsync(await _reader.RosterAsync(club, ct), new[] { g.White, g.Black, req.White?.Name, req.Black?.Name },
            new[] { g.WhiteFide, g.BlackFide, req.White?.Fide, req.Black?.Fide }, ct);
        Side Current(string name, string? fide, int? elo) => Anon(name, fide)
            ? new Side(name, LeagueRosterIndex.None, null, true)
            : new Side(name, LeagueRosterIndex.None, elo, false, fide, new LeagueMegaPlayers.Hit(name, fide));   // war schon angenommen
        // Eigener Verein → ersetzt: erzwingt Decided selbst, wie auf den Wegen Import und AddGame (F7-008).
        var w = req.White is { } dw ? Decided(dw, g.White, g.WhiteFide, g.WhiteElo, lk) : Current(g.White, g.WhiteFide, g.WhiteElo);
        var b = req.Black is { } db ? Decided(db, g.Black, g.BlackFide, g.BlackElo, lk) : Current(g.Black, g.BlackFide, g.BlackElo);
        var moveText = PgnParser.SplitGames(g.Pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty;
        var sans = PgnParser.ExtractMainlineSans(moveText);
        var (built, reason) = Build(club, w, b, sans, g.Year, req.Result ?? g.Result, g.Event);
        if (built == null) return (null, reason);
        var before = new[] { g.WhiteFide, g.BlackFide };
        var remember = new List<(string Raw, LeagueNameAliases.Entry Target)>();
        if (req.White != null && g.WhiteFide == null && w.Identity is { } iw) remember.Add((g.White, iw));
        if (req.Black != null && g.BlackFide == null && b.Identity is { } ib) remember.Add((g.Black, ib));
        (g.White, g.Black, g.WhiteFide, g.BlackFide, g.WhiteElo, g.BlackElo, g.Result, g.Event, g.Pgn) =
            (built.White, built.Black, built.WhiteFide, built.BlackFide, built.WhiteElo, built.BlackElo, built.Result, built.Event, built.Pgn);
        // Der echte Name hinter „Schwaz": eine geänderte Seite bringt ihn mit; eine unveränderte behält den bisherigen.
        if (req.White != null) (g.WhiteRealName, g.WhiteRealFide) = (built.WhiteRealName, built.WhiteRealFide);
        if (req.Black != null) (g.BlackRealName, g.BlackRealFide) = (built.BlackRealName, built.BlackRealFide);
        // Brettpaarung (0.678.0): fehlt = unverändert
        if (req.LeagueGameId is { } pairing)
        {
            var year = g.Year;
            await _pairings.ApplyAsync(g, pairing, ct);
            if (g.Year != year) g.Pgn = PgnOf(g, sans);
        }
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

    /// <summary>
    /// Die ZÜGE einer Vereinspartie korrigieren (0.660.0, Wunsch 2026-10-05: „einen Korrigieren-Knopf, der wie beim
    /// initialen Beheben von Unsicherheiten das Interface öffnet"). Gleiche Rechte wie <see cref="UpdateAsync"/>. Neu
    /// geschrieben werden PGN, Halbzüge und der Dubletten-Schlüssel; die Analyse wird auf die neue Zugfolge umgebaut
    /// (gleiche Stellungen bleiben gerechnet, die Pumpe rechnet nur die geänderten — samt Taktik-Ernte), die Spielerkarten werden neu gebaut. Die verbundenen Kopien
    /// zieht der Aufrufer nach (<c>ClubGameCorrectionService</c>). → (Partie, neue SAN) oder ein Grund: <c>notFound</c>,
    /// <c>forbidden</c>, <c>noMoves</c>, <c>tooLong</c>, <c>illegal</c>.
    /// </summary>
    public async Task<(LeagueClubGame? Game, IReadOnlyList<string>? Sans, string? Reason)> CorrectMovesAsync(LeagueClub club, int userId,
        bool canManage, int id, IReadOnlyList<string> moves, CancellationToken ct = default)
    {
        var g = await Games(club).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return (null, null, "notFound");
        if (!CanDelete(g, userId, canManage)) return (null, null, "forbidden");
        var clean = moves.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();
        if (clean.Count == 0) return (null, null, "noMoves");
        if (clean.Count > MaxPlies) return (null, null, "tooLong");
        List<string> sans;
        try { sans = SavedGameService.LegalSans(clean); }
        catch (ArgumentException) { return (null, null, "illegal"); }
        var old = PgnParser.ExtractMainlineSans(PgnParser.SplitGames(g.Pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty);
        if (old.SequenceEqual(sans)) return (g, sans, null);
        g.Pgn = PgnOf(g, sans);
        g.Plies = sans.Count;
        g.MovesHash = HashOf(sans);
        await _db.SaveChangesAsync(ct);
        // Die Analyse rechnete eine andere Zugfolge: auf die neuen Züge umbauen — gleiche Stellungen behalten ihr Ergebnis,
        // nur die geänderten rechnet die Pumpe neu (0.694.1; vorher wurde alles gelöscht und ganz neu gerechnet).
        if (_analyses != null) await _analyses.RebaseClubGameAsync(g, ct);
        await RefreshCardsAsync(new[] { g.WhiteFide, g.BlackFide }, ct);
        _log.LogInformation("Vereins-Datenbank: Züge von Partie {Id} korrigiert ({Old} → {New} Halbzüge)", g.Id, old.Count, sans.Count);
        return (g, sans, null);
    }

    /// <summary>Eigene Partien darf man bearbeiten und löschen — seit 0.656.0 auch eine „Schwaz"-Partie, wenn sie einem selbst
    /// gehört: einen Hochladenden trägt eine solche nur nach der Zuordnung mit Zustimmung (<see cref="ClaimAsync"/>).</summary>
    internal static bool CanDelete(LeagueClubGame g, int userId, bool canManage) =>
        canManage || g.UploadedByUserId == userId;

    // ── Zuordnen nach dem Anmelden (0.656.0, Wunsch 2026-10-04: „wenn ein Spieler über den anonymen Link hochlädt, merk dir
    //    das im Browser, damit du ihm — wenn er sich irgendwann einloggt — zum Bearbeiten zuordnen kannst") ──

    public const int MaxClaimKeys = 200;

    /// <summary>Ein neuer Zuordnungs-Schlüssel (128 Bit, 32 Hex) — nur der Browser kennt ihn, gespeichert wird sein Hash.</summary>
    public static string NewClaimKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static string ClaimHashOf(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim().ToLowerInvariant()))).ToLowerInvariant();

    private IQueryable<LeagueClubGame> Claimable(IEnumerable<string>? keys)
    {
        var hashes = (keys ?? Array.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length == 32)
            .Take(MaxClaimKeys).Select(ClaimHashOf).Distinct().ToList();
        return _db.LeagueClubGames.IgnoreQueryFilters().Where(g => g.ClaimKeyHash != null && hashes.Contains(g.ClaimKeyHash) && g.UploadedByUserId == null);
    }

    /// <summary>Was mit diesen Schlüsseln zuzuordnen wäre → (Partien, davon „Schwaz") — für die Rückfrage.</summary>
    public async Task<(int Games, int Anonymized)> ClaimPreviewAsync(IEnumerable<string>? keys, CancellationToken ct = default)
    {
        var rows = await Claimable(keys).AsNoTracking().Select(g => g.Anonymized).ToListAsync(ct);
        return (rows.Count, rows.Count(a => a));
    }

    /// <summary>Nach dem JA: die Partien dieser Schlüssel gehören ab jetzt <paramref name="userId"/>; die Schlüssel verfallen.
    /// → Zahl der zugeordneten.</summary>
    public async Task<int> ClaimAsync(int userId, IEnumerable<string>? keys, CancellationToken ct = default)
    {
        var games = await Claimable(keys).ToListAsync(ct);
        foreach (var g in games)
        {
            g.UploadedByUserId = userId;
            g.ClaimKeyHash = null;
        }
        await _db.SaveChangesAsync(ct);
        if (games.Count > 0) _log.LogInformation("Vereins-Datenbank: {Count} anonym hochgeladene Partien einem Konto zugeordnet", games.Count);
        return games.Count;
    }

    /// <summary>Nach dem NEIN: die Schlüssel verfallen, die Partien bleiben ohne Hochladenden.</summary>
    public async Task<int> ForgetClaimsAsync(IEnumerable<string>? keys, CancellationToken ct = default)
    {
        var games = await Claimable(keys).ToListAsync(ct);
        foreach (var g in games) g.ClaimKeyHash = null;
        await _db.SaveChangesAsync(ct);
        return games.Count;
    }

    // ── Löschen ─────────────────────────────────────────────────────

    public async Task<LeagueClubService.DeleteResult> DeleteAsync(LeagueClub club, int userId, bool canManage, int id, CancellationToken ct = default)
    {
        var g = await Games(club).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return LeagueClubService.DeleteResult.NotFound;
        if (!CanDelete(g, userId, canManage)) return LeagueClubService.DeleteResult.Forbidden;
        // Erst die Analyse (sie trägt die Namen der Partie und hat vielleicht noch Aufträge offen), dann die Partie.
        if (_analyses != null) await _analyses.DeleteForClubGameAsync(g.Id, ct);
        _db.LeagueClubGames.Remove(g);
        await _db.SaveChangesAsync(ct);
        await RefreshCardsAsync(new[] { g.WhiteFide, g.BlackFide }, ct);
        return LeagueClubService.DeleteResult.Deleted;
    }

    /// <summary>
    /// „Alle Partien dieses Links entfernen" (Verwalter, Codereview 2026-09-29, A2-009): jede Partie, die über den
    /// Teilen-Link <paramref name="shareToken"/> hochgeladen wurde — auch anonymisierte und auch nach Ablauf des Links —,
    /// samt Analyse; die Spielerkarten werden neu gerechnet. <paramref name="dryRun"/> zählt nur. → Anzahl.
    /// <para>Der Hash kommt aus dem Token der Link-ZEILE, so wie beim Hochladen (<see cref="ClubPgnReader.ImportViaShareAsync"/>): die
    /// Spalte vergleicht groß/klein- und akzent-blind, jede Schreibweise des Links trifft also dieselben Partien. Ohne
    /// Ablauf-Filter; ist die Zeile schon aufgeräumt, zählt der Wert, wie er kommt.</para>
    /// </summary>
    public async Task<int> DeleteByShareAsync(LeagueClub club, string shareToken, bool dryRun, CancellationToken ct = default)
    {
        var given = shareToken.Trim();
        var link = await _db.LeagueShares.AsNoTracking().Where(s => s.Token == given).Select(s => s.Token)
            .FirstOrDefaultAsync(ct) ?? given;
        var hash = ShareHashOf(link);
        // IgnoreQueryFilters nimmt nur den Archiv-Filter weg — der Verein bleibt ausdrücklich in der Bedingung.
        if (dryRun) return await _db.LeagueClubGames.IgnoreQueryFilters().CountAsync(g => g.ClubId == club.Id && g.UploadShareHash == hash, ct);
        var games = await _db.LeagueClubGames.IgnoreQueryFilters().Where(g => g.ClubId == club.Id && g.UploadShareHash == hash).ToListAsync(ct);
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
