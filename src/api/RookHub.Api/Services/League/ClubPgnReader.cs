using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.League.ClubPgnText;
using static RookHub.Api.Services.League.ClubSideResolver;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die Einlesewege der Vereins-Datenbank (aus <see cref="LeagueClubService"/> gezogen, Zerlegung Z14): Übersicht vor dem
/// PGN-Import, der Import selbst (mit Konto oder über den Teilen-Link, gedeckelt) und eine einzelne Partie aus einem
/// Partieformular. Zerlegen und Nachspielen macht <see cref="ClubPgnText"/>, den Seiten-Abgleich
/// <see cref="ClubSideResolver"/>, gespeichert wird über <see cref="ClubGameWriter"/>.
/// </summary>
internal sealed class ClubPgnReader
{
    /// <summary>Grund für Partien über dem Deckel der Teilen-Link-Uploads (<see cref="LeagueShareUploadQuota"/>).</summary>
    public const string ShareLimitReason = "shareLimit";

    private readonly AppDbContext _db;
    private readonly ILogger _log;
    private readonly Func<DateTime> _now;
    private readonly LeagueShareUploadQuota _shareQuota;
    private readonly LeaguePairingFinder _pairings;
    private readonly ClubGameReader _reader;
    private readonly ClubGameWriter _writer;

    public ClubPgnReader(AppDbContext db, ILogger log, Func<DateTime> now, LeagueShareUploadQuota shareQuota,
        LeaguePairingFinder pairings, ClubGameReader reader, ClubGameWriter writer)
    {
        _db = db; _log = log; _now = now; _shareQuota = shareQuota; _pairings = pairings; _reader = reader; _writer = writer;
    }

    /// <summary>Die schon gespeicherten Partien mit den Zügen dieser Partien (Hash, Jahr, Namen) — für alle Partien eines
    /// Aufrufs zusammen, je 500 Hashes eine Abfrage. Vorher fragte jede Partie einzeln (bis 500 Rundreisen je Übersicht
    /// oder Import, auch anonym über den Teilen-Link; Codereview 2026-09-29, N4-004).</summary>
    private async Task<ILookup<string, LeagueClubGame>> StoredByHashAsync(LeagueClub club, IEnumerable<LeagueClubGame> games, CancellationToken ct)
    {
        var hashes = games.Select(g => g.MovesHash).Distinct(StringComparer.Ordinal).ToList();
        var rows = new List<LeagueClubGame>();
        foreach (var chunk in hashes.Chunk(500))
            rows.AddRange(await ClubGameReader.GamesOf(_db, club).AsNoTracking().Where(x => chunk.Contains(x.MovesHash))
                .Select(x => new LeagueClubGame { MovesHash = x.MovesHash, Year = x.Year, White = x.White, Black = x.Black })
                .ToListAsync(ct));
        return rows.ToLookup(x => x.MovesHash, StringComparer.Ordinal);
    }

    /// <summary>Gleiche Partie schon da (Datenbank — <paramref name="stored"/> aus <see cref="StoredByHashAsync"/> — oder
    /// derselbe Upload)?</summary>
    private static bool IsDuplicate(LeagueClubGame g, List<LeagueClubGame> pending, ILookup<string, LeagueClubGame> stored)
    {
        bool Same(LeagueClubGame x) => x.MovesHash == g.MovesHash && x.Year == g.Year
            && (g.Plies >= ShortGamePlies || (x.White == g.White && x.Black == g.Black));
        return pending.Any(Same) || stored[g.MovesHash].Any(Same);
    }

    /// <summary>
    /// Übersicht vor dem Import (Wunsch 2026-09-28: „nach Import Übersicht wer gegen wen, unerkannte/falsche Spieler
    /// korrigieren, je Partie markieren, ob sie importiert wird"): liest und gleicht ab, speichert NICHTS. Je Partie die
    /// Seiten mit Abgleich und der Vorgabe „ersetzen", ob sie schon da ist, und was sie unübernehmbar macht. Ob eine
    /// Partie übernommen wird, entscheidet die Seite danach aus den Seiten (dieselbe Regel wie <see cref="Build"/>).
    /// </summary>
    public async Task<LeagueClubPreviewDto> PreviewAsync(LeagueClub club, int? userId, string pgn, CancellationToken ct = default)
    {
        var parsed = ParseAll(pgn, out var truncated);
        var lk = await _reader.LookupsAsync(await _reader.RosterAsync(club, ct), parsed.SelectMany(p => new[] { p.H("White"), p.H("Black") }),
            parsed.SelectMany(p => new[] { p.H("WhiteFideId"), p.H("BlackFideId") }), ct);
        var owner = await _reader.OwnerAsync(userId, ct);
        var now = _now();
        var pending = new List<LeagueClubGame>();
        var dto = new LeagueClubPreviewDto { Truncated = truncated };
        var similar = new Dictionary<string, List<LeagueRosterPersonDto>>();
        var candidates = new List<(LeagueClubPreviewGameDto Dto, LeagueClubGame Game)>();
        foreach (var p in parsed)
        {
            var (w, b, ownerSide) = Defaults(p, owner, lk);
            var g = new LeagueClubPreviewGameDto
            {
                Index = p.Index, Year = YearOf(p.H("Date"), now), Result = p.H("Result") is { } r && ClubPgnText.Results.Contains(r) ? r : "*",
                Event = p.H("Event"), Plies = p.Sans?.Count ?? 0, Opening = p.Sans is null ? "" : OpeningOfSans(p.Sans),
                Error = p.Error, White = SideDto(w, ownerSide == "white"), Black = SideDto(b, ownerSide == "black"),
                Pgn = p.Error == null ? p.Pgn : null,
            };
            AddSimilar(g.White.Match, g.White.Raw, lk.Roster, similar);
            AddSimilar(g.Black.Match, g.Black.Raw, lk.Roster, similar);
            if (p.Sans != null && Build(club, w, b, p.Sans, g.Year, p.H("Result"), p.H("Event")).Game is { } built)
                candidates.Add((g, built));
            dto.Games.Add(g);
        }
        var stored = await StoredByHashAsync(club, candidates.Select(c => c.Game), ct);
        foreach (var (g, built) in candidates)
        {
            g.Duplicate = IsDuplicate(built, pending, stored);
            pending.Add(built);
        }
        // Brettpaarungen (0.678.0): EINE Abfrage für die ganze Übersicht
        var dates = parsed.ToDictionary(p => p.Index, p => FullDateOf(p.H("Date")));
        var options = await _pairings.ForManyAsync(
            candidates.Select(c => LeaguePairingFinder.QueryOf(c.Game, dates.GetValueOrDefault(c.Dto.Index))).ToList(), ct, club);
        for (var i = 0; i < candidates.Count; i++)
        {
            candidates[i].Dto.Pairings = options[i].Select(LeagueClubPairingDto.Of).ToList();
            candidates[i].Dto.PairingId = LeaguePairingFinder.AutoPick(options[i]);
        }
        return dto;
    }

    /// <summary>PGN-Import nach der Übersicht: je Partie in <paramref name="decisions"/> die festgelegten Seiten
    /// (<c>null</c> = alle Partien mit den Vorgaben der Übersicht). Wirft nicht bei einzelnen Partien — die stehen mit
    /// Grund in <c>Failed</c>.</summary>
    public Task<LeagueClubImportResultDto> ImportPgnAsync(LeagueClub club, int? userId, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct = default) =>
        ImportAsync(club, userId, null, pgn, decisions, ct);

    /// <summary>Derselbe Import OHNE Konto über den Teilen-Link <paramref name="shareToken"/>: jede Partie trägt den Link
    /// (<see cref="ShareHashOf"/>), und gespeichert werden höchstens <see cref="LeagueShareUploadQuota.PerCall"/> je
    /// Aufruf und <see cref="LeagueShareUploadQuota.PerLinkPerDay"/> je Link und Tag — der Rest steht mit Grund
    /// <c>shareLimit</c> in <c>Failed</c>.</summary>
    /// <param name="shareToken">Das Token der Link-ZEILE (<see cref="LeagueService.ValidShareTokenAsync"/>), nicht der Wert
    /// aus der Route: MariaDB findet den Link auch in anderer Groß/Kleinschreibung, und jede Schreibweise bekäme sonst
    /// ihren eigenen Hash und Deckel.</param>
    public async Task<LeagueClubImportResultDto> ImportViaShareAsync(LeagueClub club, string shareToken, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct = default)
    {
        var claimKey = ClubGameWriter.NewClaimKey();
        var r = await ImportAsync(club, null, ClubGameWriter.ShareHashOf(shareToken), pgn, decisions, ct, ClubGameWriter.ClaimHashOf(claimKey));
        if (r.Ids.Count > 0) r.ClaimKey = claimKey;
        return r;
    }

    private async Task<LeagueClubImportResultDto> ImportAsync(LeagueClub club, int? userId, string? shareHash, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct, string? claimHash = null)
    {
        var result = new LeagueClubImportResultDto();
        var split = SplitAll(pgn, out var truncated);
        result.Truncated = truncated;
        var work = decisions?.Where(d => d != null).DistinctBy(d => d.Index).Select(d => (d.Index, (LeagueClubImportGameDecision?)d)).ToList()
            ?? split.Select(p => (p.Index, (LeagueClubImportGameDecision?)null)).ToList();
        // Über einen Teilen-Link: vorher reservieren, was dieser Aufruf höchstens speichern darf (nie mehr, als er Partien
        // hat — sonst hielte eine 10er-Portion 50 Plätze, und ein gleichzeitiger Upload über denselben Link bekäme kurz vor
        // dem Deckel unnötig shareLimit); der Rest geht am Ende zurück.
        using var lease = shareHash is null ? null
            : _shareQuota.Reserve(shareHash, Math.Min(LeagueShareUploadQuota.PerCall, work.Count));
        var budget = lease?.Granted ?? int.MaxValue;
        // Am Deckel speichert dieser Aufruf nichts — dann weder nachspielen noch abgleichen noch Dubletten suchen, jede
        // Partie gleich „shareLimit" (Codereview 2026-09-29, N4-004: vorher lief die ganze Arbeit durch).
        if (budget == 0 && work.Count > 0) return RefuseAll(result, work.Select(x => x.Index), split, shareHash!);
        var parsed = CheckAll(split);
        var byIndex = parsed.ToDictionary(p => p.Index);
        var chosen = (decisions ?? Array.Empty<LeagueClubImportGameDecision>()).Where(d => d != null)
            .SelectMany(d => new[] { d.White, d.Black }).Where(s => s != null).ToList();
        var lk = await _reader.LookupsAsync(await _reader.RosterAsync(club, ct),
            parsed.SelectMany(p => new[] { p.H("White"), p.H("Black") }).Concat(chosen.Select(s => s.Name)),
            parsed.SelectMany(p => new[] { p.H("WhiteFideId"), p.H("BlackFideId") }).Concat(chosen.Select(s => s.Fide)), ct);
        var owner = await _reader.OwnerAsync(userId, ct);
        var remember = new List<(string Raw, LeagueNameAliases.Entry Target)>();
        var now = _now();
        var pending = new List<LeagueClubGame>();
        // Erst alle Partien bauen, dann die Dubletten aller zusammen nachschlagen (N4-004), dann in Reihenfolge entscheiden.
        var built = new List<(int Index, Parsed? P, LeagueClubGame? Game, string? Reason)>();
        var choices = new Dictionary<int, int?>();
        foreach (var (index, decision) in work)
        {
            if (!byIndex.TryGetValue(index, out var p))
            {
                built.Add((index, null, null, "notFound"));
                continue;
            }
            if (p.Error != null || p.Sans == null) { built.Add((index, p, null, p.Error ?? "illegal")); continue; }
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
            var (game, reason) = Build(club, w, b, p.Sans, YearOf(p.H("Date"), now), p.H("Result"), p.H("Event"));
            built.Add((index, p, game, reason));
            choices[index] = decision?.LeagueGameId;
        }
        var stored = await StoredByHashAsync(club, built.Where(x => x.Game != null).Select(x => x.Game!), ct);
        foreach (var (index, p, game, reason) in built)
        {
            if (game == null) { result.Failed.Add(FailureOf(index, p, reason!)); continue; }
            if (IsDuplicate(game, pending, stored)) { result.Duplicates++; continue; }
            if (pending.Count >= budget) { result.Failed.Add(FailureOf(index, p, ShareLimitReason)); continue; }
            ClubGameWriter.Stamp(game, userId, now, shareHash, claimHash);
            pending.Add(game);
        }
        if (lease != null) lease.Kept = pending.Count;
        var kept = pending.ToHashSet();
        await _writer.ApplyPairingsAsync(club, built.Where(x => x.Game != null && kept.Contains(x.Game))
            .Select(x => (x.Game!, choices.GetValueOrDefault(x.Index), FullDateOf(x.P!.H("Date")), (IReadOnlyList<string>)x.P!.Sans!))
            .ToList(), ct);
        await _writer.SaveAsync(pending, ct);
        await _writer.RebalanceAsync(club, pending, ct);
        if (shareHash != null && result.Failed.Count(f => f.Reason == ShareLimitReason) is > 0 and var refused)
            _log.LogWarning("Vereins-Datenbank: Teilen-Link {Link} am Deckel — {Refused} Partien nicht übernommen",
                shareHash[..8], refused);
        result.Remembered = await _writer.RememberAsync(remember, ct);
        result.Added = pending.Count;
        result.Anonymized = pending.Count(g => g.Anonymized);
        result.Ids = pending.Select(g => g.Id).ToList();
        _log.LogInformation("Vereins-Datenbank {Club}: {Added} Partien hochgeladen ({Anon} mit „{AnonName}“), {Dup} doppelt, {Failed} abgelehnt, {Remembered} Zuordnungen gemerkt{Via}",
            club.Id, result.Added, result.Anonymized, club.AnonName, result.Duplicates, result.Failed.Count, result.Remembered, userId == null ? " (Teilen-Link)" : "");
        return result;

        // Eine Korrektur (Spieler gewählt oder Name getippt), die bei jemand ANDEREM landet als die Vorgabe: merken.
        void Remember(string? raw, LeagueClubSideDecision? d, Side decided, Side byDefault)
        {
            if (string.IsNullOrWhiteSpace(raw) || d is null || (string.IsNullOrWhiteSpace(d.Name) && string.IsNullOrWhiteSpace(d.Fide))) return;
            if (decided.Identity is { } id && id != byDefault.Identity) remember.Add((raw, id));
        }
    }

    /// <summary>Eine abgelehnte Partie: Nummer, die Namen der Kopfzeile (gekürzt) und der Grund; <paramref name="p"/>
    /// <c>null</c> = die Nummer gibt es nicht (dann nur Nummer und Grund).</summary>
    private static LeagueClubFailureDto FailureOf(int index, Parsed? p, string reason) => new()
    {
        Index = index, White = p?.H("White") is { } w0 ? Clip(w0, 120) : null, Black = p?.H("Black") is { } b0 ? Clip(b0, 120) : null,
        Reason = reason,
    };

    /// <summary>Ein Aufruf über einen Teilen-Link, dessen Deckel schon voll ist: jede Partie <c>shareLimit</c> (eine
    /// unbekannte Nummer <c>notFound</c>), ohne sie nachzuspielen, abzugleichen oder nach Dubletten zu suchen (N4-004).</summary>
    private LeagueClubImportResultDto RefuseAll(LeagueClubImportResultDto result, IEnumerable<int> work, List<Parsed> split,
        string shareHash)
    {
        var byIndex = split.ToDictionary(p => p.Index);
        foreach (var index in work)
            result.Failed.Add(byIndex.TryGetValue(index, out var p) ? FailureOf(index, p, ShareLimitReason) : FailureOf(index, null, "notFound"));
        _log.LogWarning("Vereins-Datenbank: Teilen-Link {Link} am Deckel — {Refused} Partien nicht übernommen",
            shareHash[..8], result.Failed.Count(f => f.Reason == ShareLimitReason));
        return result;
    }

    /// <summary>Eine Partie aus der Korrektur eines Partieformulars. <c>Reason</c> ≠ null = abgelehnt
    /// (<c>illegal</c> samt Meldung, sonst wie beim Import, dazu <c>duplicate</c>).</summary>
    public Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameAsync(LeagueClub club, int? userId, LeagueClubGameRequest req,
        CancellationToken ct = default) => AddAsync(club, userId, null, req, ct);

    /// <summary>Dasselbe ohne Konto über den Teilen-Link <paramref name="shareToken"/> (das Token der Link-Zeile, wie bei
    /// <see cref="ImportViaShareAsync"/>) — zählt gegen denselben Deckel wie der PGN-Import (dieser Weg braucht kein Foto),
    /// darüber <c>shareLimit</c>.</summary>
    /// <param name="claimKey">Zuordnungs-Schlüssel für den Browser (0.656.0, <see cref="NewClaimKey"/>) — die Partie trägt
    /// seinen Hash.</param>
    public Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameViaShareAsync(LeagueClub club, string shareToken,
        LeagueClubGameRequest req, CancellationToken ct = default, string? claimKey = null) =>
        AddAsync(club, null, ClubGameWriter.ShareHashOf(shareToken), req, ct, claimKey is null ? null : ClubGameWriter.ClaimHashOf(claimKey));

    internal async Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddAsync(LeagueClub club, int? userId, string? shareHash,
        LeagueClubGameRequest req, CancellationToken ct, string? claimHash = null)
    {
        var moves = (req.Moves ?? new()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();
        if (moves.Count == 0) return (null, "noMoves", null);
        if (moves.Count > MaxPlies) return (null, "tooLong", null);
        List<string> sans;
        try { sans = SavedGameService.LegalSans(moves); }
        catch (ArgumentException ex) { return (null, "illegal", ex.Message); }
        var now = _now();
        var year = req.Year is { } y && y >= 1900 && y <= now.Year + 1 ? y : (int?)null;
        var lk = await _reader.LookupsAsync(await _reader.RosterAsync(club, ct), new[] { req.White, req.Black }, new[] { req.WhiteFide, req.BlackFide }, ct);
        var w = Decided(new LeagueClubSideDecision { Name = req.White, Fide = req.WhiteFide, Replace = req.WhiteReplace }, req.White, null, req.WhiteElo, lk);
        var b = Decided(new LeagueClubSideDecision { Name = req.Black, Fide = req.BlackFide, Replace = req.BlackReplace }, req.Black, null, req.BlackElo, lk);
        var (game, reason) = Build(club, w, b, sans, year, req.Result, req.Event);
        if (game == null) return (null, reason, null);
        if (IsDuplicate(game, new(), await StoredByHashAsync(club, new[] { game }, ct))) return (null, "duplicate", null);
        using var lease = shareHash is null ? null : _shareQuota.Reserve(shareHash, 1);
        if (lease is { Granted: 0 })
        {
            _log.LogWarning("Vereins-Datenbank: Teilen-Link {Link} am Deckel — {Refused} Partien nicht übernommen", shareHash![..8], 1);
            return (null, ShareLimitReason, null);
        }
        if (lease != null) lease.Kept = 1;
        ClubGameWriter.Stamp(game, userId, now, shareHash, claimHash);
        await _writer.ApplyPairingsAsync(club, new[] { (game, req.LeagueGameId, FullDateOf(req.Date), (IReadOnlyList<string>)sans) }, ct);
        await _writer.SaveAsync(new List<LeagueClubGame> { game }, ct);
        await _writer.RebalanceAsync(club, new[] { game }, ct);
        game.Replaced = await _writer.ArchiveOlderVersionsAsync(game, sans, now, ct);
        _log.LogInformation("Vereins-Datenbank {Club}: eine Partie aus einem Partieformular ({Anon}{Via})",
            club.Id, game.Anonymized ? $"mit „{club.AnonName}“" : "mit Namen", userId == null ? ", Teilen-Link" : "");
        return (game, null, null);
    }
}
