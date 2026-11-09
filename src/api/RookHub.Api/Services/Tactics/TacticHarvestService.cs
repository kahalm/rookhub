using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.DTOs.Engines;

namespace RookHub.Api.Services.Tactics;

/// <summary>
/// Taktik-Ernte (0.657.0, Wunsch 2026-10-04: „kann ich Taktiken aus den Partien automatisch ernten … plan und bau").
/// Drei Schritte je Takt (<see cref="TacticHarvestScheduler"/>):
/// <list type="number">
/// <item><see cref="ScanAsync"/>: fertige Analysen von Vereinspartien, Meisterpartien und eigenen gespeicherten Partien
/// (NICHT Punktepartien und Handanalysen — das sind private Analysen einzelner Nutzer) nach <see cref="TacticHarvest.Detect"/>
/// durchsuchen — ohne Engine, die Kandidaten liegen schon da. Gefunden UND verpasst werden geerntet, vermerkt wird beides.</item>
/// <item><see cref="PumpAsync"/>: je Kandidat die Lösung verlängern. Die Antwort des Gegners kommt aus der Hauptvariante,
/// danach rechnet ein Auftrag (zwei Linien, Hintergrund, nicht in der Ruhezeit) die Stellung des Lösers; ist der beste Zug
/// wieder eindeutig, geht es weiter, sonst endet die Lösung beim letzten eindeutigen Zug.</item>
/// <item><see cref="PublishAsync"/>: fertige Taktiken als Aufgaben in einen Kurs — Vereinspartien je VEREIN in „Taktiken aus
/// Vereinspartien – {Verein}" (<see cref="ClubBookOf"/>, Mandanten-Schritt 2026-10-07; Kapitel je Ligarunde, Wunsch
/// 2026-10-04; sichtbar für die Gruppen des Vereins, <see cref="LeagueClubMember"/>),
/// Meisterpartien in „Taktiken aus Meisterpartien" (vorerst nur Admins), eigene Partien in „Taktiken aus meinen Partien"
/// des Besitzers. Verschwindet eine Partie, wird ihre Aufgabe stillgelegt (<see cref="BookPuzzle.Retired"/>).</item>
/// </list>
/// </summary>
public sealed class TacticHarvestService(AppDbContext db, AnalysisJobService jobs, QuietHours quiet, IConfiguration config,
    ILogger<TacticHarvestService> log)
{
    public const int ScanBatch = 40;
    public const int MaxOpenJobs = 8;
    public const int Depth = 22;
    public const int MaxAttempts = 3;
    public const int SecondMaxOpenJobs = 4;
    public const int DefaultSecondNodes = 50_000;
    public const int SecondMultiPv = 3;
    public const string JobTitle = "Taktik-Ernte";
    /// <summary>Der Taktik-Kurs EINES Vereins (Mandanten-Schritt 2026-10-07): <c>tactics-club-{id}.pgn</c>. Das frühere
    /// gemeinsame Buch <c>tactics-club.pgn</c> hat die Migration <c>LeagueClubs</c> zu dem von Verein 1 (SK Schwaz) umbenannt
    /// (Datei + Aufgaben; die <see cref="BookPuzzle.LineId"/> der alten Aufgaben behält ihr „tactics-club.pgn:"-Präfix —
    /// die Kennung ist nur eindeutig, nicht lesbar, und Fortschritt hängt an Buch- und Aufgaben-Id).</summary>
    public static string ClubBookOf(int clubId) => $"tactics-club-{clubId}.pgn";
    /// <summary>Gemeinsamer Anfang aller Vereinskurse.</summary>
    public const string ClubBookPrefix = "tactics-club-";
    public static string ClubBookName(LeagueClub club) => $"Taktiken aus Vereinspartien – {club.Name}";
    public const string MasterBook = "tactics-masters.pgn";
    public static string OwnBook(int userId) => $"tactics-u{userId}.pgn";
    public const string BookPrefix = "tactics-";
    public const string OtherGamesChapter = "Andere Partien";

    private readonly TacticHarvestUpkeep _upkeep = new(db, log);

    private static readonly GameAnalysisOrigin[] Sources =
        { GameAnalysisOrigin.Club, GameAnalysisOrigin.Library, GameAnalysisOrigin.SavedGame };

    public async Task RunAsync(CancellationToken ct)
    {
        await ScanAsync(ct);
        if (await EngineOwnerAsync(ct) is { } owner) await PumpAsync(owner, ct);
        await PublishAsync(ct);
    }

    // ── 1. Ernten ──

    public async Task<int> ScanAsync(CancellationToken ct)
    {
        var analyses = await db.GameAnalyses
            .Where(a => a.Status == GameAnalysisStatus.Done && a.TacticsScannedAt == null && Sources.Contains(a.Origin)
                // die zweite Engine der Vereinspartien (0.684.0, Lc0) wird eigens geerntet (Variante "lc0"), nicht als zweites Mal dasselbe
                && (a.EngineId == null || a.Origin == GameAnalysisOrigin.Club))
            // Vereinspartien zuerst, die neuesten vorneweg (0.657.1: die Partien vom Liga-Wochenende sollen nicht hinter
            // 3.600 älteren Meisterpartien warten), dann eigene Partien, dann der Rest
            .OrderBy(a => a.Origin == GameAnalysisOrigin.Club ? 0 : a.Origin == GameAnalysisOrigin.SavedGame ? 1 : 2)
            .ThenBy(a => a.EngineId == null ? 0 : 1).ThenByDescending(a => a.Id).Take(ScanBatch).ToListAsync(ct);
        if (analyses.Count == 0) return 0;
        var ids = analyses.Select(a => a.Id).ToList();
        var positions = (await db.GameAnalysisPositions.AsNoTracking().Where(p => ids.Contains(p.GameAnalysisId) && p.GameMoveUci != "")
                .Select(p => new { p.GameAnalysisId, p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson }).ToListAsync(ct))
            .GroupBy(p => p.GameAnalysisId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Ply).ToList());
        // Stellen, die für eine Vereinspartie schon einen Kandidaten haben (andere Analyse/Engine/Stufe) — nicht doppelt ernten
        var gameIds = analyses.Where(a => a.LeagueClubGameId != null).Select(a => a.LeagueClubGameId!.Value).Distinct().ToList();
        var seen = (await (from c in db.TacticCandidates.AsNoTracking()
                           join g in db.GameAnalyses.AsNoTracking() on c.GameAnalysisId equals g.Id
                           where g.LeagueClubGameId != null && gameIds.Contains(g.LeagueClubGameId.Value)
                           select new { Game = g.LeagueClubGameId!.Value, c.Ply }).ToListAsync(ct))
            .Select(x => (x.Game, x.Ply)).ToHashSet();
        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var a in analyses)
        {
            a.TacticsScannedAt = now;
            if (!positions.TryGetValue(a.Id, out var ps)) continue;
            var lc0 = a.EngineId != null;
            for (var i = 1; i < ps.Count; i++)
            {
                if (ps[i].Ply != ps[i - 1].Ply + 1) continue;
                var before = TacticHarvest.Parse(ps[i - 1].CandidatesJson);
                var here = TacticHarvest.Parse(ps[i].CandidatesJson);
                // Stockfish: erst streng, dann locker; Lc0 (nur Vereinspartien): gleich die lockeren Schwellen
                string? variant = lc0 ? "lc0" : null;
                var found = lc0 ? TacticHarvest.Detect(before, here, TacticHarvest.Relaxed) : TacticHarvest.Detect(before, here);
                if (found is null && !lc0) { found = TacticHarvest.Detect(before, here, TacticHarvest.Relaxed); variant = "relaxed"; }
                if (found is null) continue;
                if (a.LeagueClubGameId is { } gid && seen.Contains((gid, ps[i].Ply))) continue;
                var c = TacticCandidateSteps.Start(a, ps[i - 1].Fen, ps[i - 1].GameMoveUci, ps[i].Ply, ps[i].Fen, ps[i].GameMoveUci, found, now, variant);
                if (c is null) continue;
                db.TacticCandidates.Add(c);
                if (a.LeagueClubGameId is { } g2) seen.Add((g2, ps[i].Ply));
                added++;
            }
        }
        await db.SaveChangesAsync(ct);
        if (added > 0) log.LogInformation("Taktik-Ernte: {Count} Kandidaten aus {Games} Analysen", added, analyses.Count);
        return added;
    }

    // ── 2. Prüfen ──

    /// <summary>Wessen Engine rechnet: <c>TacticHarvest:OwnerUserId</c>, sonst <c>MasterAnalysis:OwnerUserId</c>, sonst die
    /// Haus-Engine eines Admins (wie die Meisterpartien-Analyse).</summary>
    public async Task<int?> EngineOwnerAsync(CancellationToken ct)
    {
        var configured = config.GetValue<int?>("TacticHarvest:OwnerUserId") ?? config.GetValue<int?>("MasterAnalysis:OwnerUserId");
        if (configured is { } id) return await EngineOwnerResolver.ResolveAsync(db, id, ct);
        var admin = await db.AppUsers.AsNoTracking().Where(u => u.IsAdmin && u.DeletedAt == null).OrderBy(u => u.Id)
            .Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        return admin is { } a ? await EngineOwnerResolver.ResolveAsync(db, a, ct) : null;
    }

    /// <summary><c>TacticHarvest:SecondEngineId</c>: die Engine der Zweitprüfung (Phase 2, lc0); leer = aus, alles wie vorher.</summary>
    public string? SecondEngineId => config["TacticHarvest:SecondEngineId"] is { Length: > 0 } id && !string.IsNullOrWhiteSpace(id) ? id.Trim() : null;
    public int SecondNodes => Math.Max(1000, config.GetValue<int?>("TacticHarvest:SecondEngineNodes") ?? DefaultSecondNodes);

    public async Task PumpAsync(int engineOwner, CancellationToken ct)
    {
        await PumpFirstAsync(engineOwner, ct);
        await PumpSecondAsync(engineOwner, ct);
    }

    private async Task PumpFirstAsync(int engineOwner, CancellationToken ct)
    {
        // Ergebnisse einsammeln
        var waiting = await db.TacticCandidates
            .Where(c => c.Status == TacticCandidateStatus.Verifying && c.AnalysisJobId != null).ToListAsync(ct);
        if (waiting.Count > 0)
        {
            var jobIds = waiting.Select(c => c.AnalysisJobId!.Value).ToList();
            var byId = await db.AnalysisJobs.Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, ct);
            foreach (var c in waiting)
            {
                if (!byId.TryGetValue(c.AnalysisJobId!.Value, out var job)) { c.AnalysisJobId = null; continue; }
                if (job.Status == AnalysisJobStatus.Done)
                {
                    var parsed = BrokerCandidates.Parse(job.ResultJson, c.NextFen!) ?? new();
                    TacticCandidateSteps.Step(c, parsed.Select(p => new TacticHarvest.Cand(p.Uci, p.Cp, p.Mate, p.Pv ?? Array.Empty<string>())).ToList());
                    db.AnalysisJobs.Remove(job);
                }
                else if (job.Status == AnalysisJobStatus.Failed)
                {
                    db.AnalysisJobs.Remove(job);
                    c.AnalysisJobId = null;
                    if (++c.Attempts >= MaxAttempts) TacticCandidateSteps.Finish(c);
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // Nachfüttern — im Hintergrund, nicht in der Ruhezeit
        if (quiet.IsQuietNow()) return;
        var open = await db.TacticCandidates.CountAsync(c => c.Status == TacticCandidateStatus.Verifying && c.AnalysisJobId != null, ct);
        var free = MaxOpenJobs - open;
        if (free <= 0) return;
        var next = await db.TacticCandidates
            .Where(c => c.Status == TacticCandidateStatus.Verifying && c.AnalysisJobId == null && c.NextFen != null)
            .OrderBy(c => c.Origin == GameAnalysisOrigin.Club ? 0 : c.Origin == GameAnalysisOrigin.SavedGame ? 1 : 2).ThenBy(c => c.Id)
            .Take(free).ToListAsync(ct);
        foreach (var c in next)
        {
            try
            {
                var job = await jobs.CreateAsync(engineOwner,
                    new CreateAnalysisJobRequest { Fen = c.NextFen, Title = JobTitle, TargetDepth = Depth, MultiPv = 2 },
                    ct, remember: false, engineOwnerUserId: engineOwner, background: true);
                c.AnalysisJobId = job.Id;
                c.UpdatedAt = DateTime.UtcNow;
            }
            catch (ArgumentException)
            {
                c.Status = TacticCandidateStatus.Rejected;
                c.RejectReason = "illegalFen";
            }
            catch (InvalidOperationException ex)
            {
                log.LogInformation("Taktik-Ernte: keine Aufträge gerade ({Reason})", ex.Message);
                break;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    // ── 2b. Zweitprüfung (lc0, Schritte in TacticCandidateSteps) ──

    private async Task PumpSecondAsync(int engineOwner, CancellationToken ct)
    {
        if (SecondEngineId is not { } engineId) return;

        var waiting = await db.TacticCandidates
            .Where(c => c.Status == TacticCandidateStatus.Done && c.SecondAgrees == null && c.SecondJobId != null).ToListAsync(ct);
        if (waiting.Count > 0)
        {
            var jobIds = waiting.Select(c => c.SecondJobId!.Value).ToList();
            var byId = await db.AnalysisJobs.Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, ct);
            foreach (var c in waiting)
            {
                if (!byId.TryGetValue(c.SecondJobId!.Value, out var job)) { c.SecondJobId = null; continue; }
                if (job.Status == AnalysisJobStatus.Done)
                {
                    var fen = TacticCandidateSteps.SecondFen(c, c.SecondStage);
                    var parsed = fen is null ? new() : BrokerCandidates.Parse(job.ResultJson, fen) ?? new();
                    TacticCandidateSteps.SecondStep(c, parsed.Select(p => new TacticHarvest.Cand(p.Uci, p.Cp, p.Mate, p.Pv ?? Array.Empty<string>())).ToList());
                    db.AnalysisJobs.Remove(job);
                }
                else if (job.Status == AnalysisJobStatus.Failed)
                {
                    db.AnalysisJobs.Remove(job);
                    c.SecondJobId = null;
                    if (++c.SecondAttempts >= MaxAttempts) TacticCandidateSteps.Dispute(c, "lc0Failed");
                }
            }
            await db.SaveChangesAsync(ct);
        }

        if (quiet.IsQuietNow()) return;
        // Die Engine muss in der Hintergrund-Liste des Besitzers stehen (A4-003) — sonst wartet die Prüfung, statt zu werfen.
        var cred = await db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == engineOwner, ct);
        if (cred is null || !cred.BackgroundEngines.Contains(engineId))
        {
            log.LogWarning("Taktik-Ernte: Zweitprüfungs-Engine {EngineId} steht nicht in der Hintergrund-Liste des Engine-Besitzers {Owner} — Zweitprüfung wartet",
                engineId, engineOwner);
            return;
        }
        var open = await db.TacticCandidates.CountAsync(c => c.Status == TacticCandidateStatus.Done && c.SecondAgrees == null && c.SecondJobId != null, ct);
        var free = SecondMaxOpenJobs - open;
        if (free <= 0) return;
        var next = await db.TacticCandidates
            .Where(c => c.Status == TacticCandidateStatus.Done && c.SecondAgrees == null && c.SecondJobId == null)
            .OrderBy(c => c.Origin == GameAnalysisOrigin.Club ? 0 : c.Origin == GameAnalysisOrigin.SavedGame ? 1 : 2).ThenBy(c => c.Id)
            .Take(free).ToListAsync(ct);
        foreach (var c in next)
        {
            var fen = TacticCandidateSteps.SecondFen(c, c.SecondStage);
            if (fen is null) { TacticCandidateSteps.Dispute(c, "lc0Fen"); continue; }
            try
            {
                var job = await jobs.CreateAsync(engineOwner,
                    new CreateAnalysisJobRequest { Fen = fen, Title = JobTitle, TargetDepth = Depth, MultiPv = SecondMultiPv, EngineId = engineId, TargetNodes = SecondNodes },
                    ct, remember: false, engineOwnerUserId: engineOwner, background: true);
                c.SecondJobId = job.Id;
                c.UpdatedAt = DateTime.UtcNow;
            }
            catch (ArgumentException)
            {
                TacticCandidateSteps.Dispute(c, "illegalFen");
            }
            catch (InvalidOperationException ex)
            {
                log.LogInformation("Taktik-Ernte: keine Zweitprüfungs-Aufträge gerade ({Reason})", ex.Message);
                break;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    // ── 3. Veröffentlichen ──

    public async Task<int> PublishAsync(CancellationToken ct)
    {
        await _upkeep.RetireOrphansAsync(ct);
        await _upkeep.RetitleClubAsync(ct);
        await _upkeep.LinkGamesAsync(ct);
        // Mit Zweitprüfung nur, was sie bestätigt hat (Disputed hat einen eigenen Status, Unentschiedene warten).
        var secondOn = SecondEngineId is not null;
        var done = await db.TacticCandidates.Include(c => c.GameAnalysis)
            .Where(c => c.Status == TacticCandidateStatus.Done && (!secondOn || c.SecondAgrees == true))
            .OrderBy(c => c.Id).Take(200).ToListAsync(ct);
        if (done.Count == 0) return 0;
        var books = new Dictionary<string, Book>();
        var rounds = new Dictionary<string, int>();
        var published = 0;
        // Vereinspartien: der Kurs des Vereins der Partie (Mandanten-Schritt 2026-10-07)
        var clubGameIds = done.Where(c => c.GameAnalysis!.Origin == GameAnalysisOrigin.Club && c.GameAnalysis.LeagueClubGameId != null)
            .Select(c => c.GameAnalysis!.LeagueClubGameId!.Value).Distinct().ToList();
        var clubOfGame = await db.LeagueClubGames.AsNoTracking().IgnoreQueryFilters().Where(g => clubGameIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.ClubId, ct);
        var clubs = await db.LeagueClubs.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        foreach (var c in done)
        {
            var a = c.GameAnalysis!;
            var club = a.Origin == GameAnalysisOrigin.Club && a.LeagueClubGameId is { } gid && clubOfGame.TryGetValue(gid, out var cid)
                ? clubs.GetValueOrDefault(cid) : null;
            if (a.Origin == GameAnalysisOrigin.Club && club is null) continue;   // Partie (samt Verein) weg — die Taktik bleibt liegen
            var (file, name, owner) = a.Origin switch
            {
                GameAnalysisOrigin.Club => (ClubBookOf(club!.Id), ClubBookName(club), (int?)null),
                GameAnalysisOrigin.Library => (MasterBook, "Taktiken aus Meisterpartien", (int?)null),
                _ => (OwnBook(a.UserId), "Taktiken aus meinen Partien", (int?)a.UserId),
            };
            if (!books.TryGetValue(file, out var book))
            {
                book = await EnsureBookAsync(file, name, owner, club?.Id, ct);
                books[file] = book;
                rounds[file] = await db.BookPuzzles.CountAsync(p => p.BookFileName == file, ct);
            }
            var (title, chapter) = await DescribeAsync(c, a, club, ct);
            var san = c.Found ? null : MoveComparisonService.SanOf(c.Fen, c.GameMoveUci);
            var comment = (c.Found ? "In der Partie gefunden." : $"In der Partie verpasst — gespielt wurde {san ?? c.GameMoveUci}.")
                + " " + Outcome(c.EvalText) + (c.Variant == "lc0" ? " Von Lc0 entdeckt." : "");
            var round = rounds[file] = rounds[file] + 1;
            var lineId = $"{file}:t{c.Id}";
            db.BookPuzzles.Add(new BookPuzzle
            {
                LineId = lineId, BookFileName = file, BookId = book.Id, Round = round.ToString(), Fen = c.PrevFen,
                Moves = $"{c.BlunderUci} {c.Moves}", StartPly = 0, Title = title, Chapter = chapter, Comment = comment,
                Tags = string.Join(',', new[] { c.Found ? "gefunden" : "verpasst" }.Concat(c.Variant is null ? Array.Empty<string>() : new[] { c.Variant }).Concat((c.Themes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))),
                Source = "tactic-harvest", SourceGame = await TacticHarvestUpkeep.SourceGameAsync(db, c, a, ct),
            });
            c.Status = TacticCandidateStatus.Published;
            c.LineId = lineId;
            c.UpdatedAt = DateTime.UtcNow;
            book.UpdatedAt = DateTime.UtcNow;
            published++;
        }
        await db.SaveChangesAsync(ct);
        foreach (var file in books.Keys.Where(f => f.StartsWith(ClubBookPrefix, StringComparison.Ordinal))) await _upkeep.SortClubChaptersAsync(file, ct);
        log.LogInformation("Taktik-Ernte: {Count} Aufgaben in die Kurse gelegt", published);
        return published;
    }

    /// <summary>Was die Lösung bringt, in Worten statt Zahl (Wunsch 2026-10-05): „Die Lösung bringt einen Vorteil." /
    /// „… führt zur Gewinnstellung." / „… setzt in 3 Zügen matt." Aus der Bewertung des Lösers (<c>+5.3</c> / <c>#3</c>).</summary>
    internal static string Outcome(string? eval)
    {
        if (eval is { Length: > 1 } && eval[0] == '#' && int.TryParse(eval[1..], out var n))
            return n == 1 ? "Die Lösung setzt matt." : $"Die Lösung setzt in {n} Zügen matt.";
        if (!double.TryParse(eval, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pawns))
            return "Die Lösung bringt einen Vorteil.";
        return pawns < 1.5 ? "Die Lösung bringt einen leichten Vorteil."
            : pawns < 3 ? "Die Lösung bringt einen klaren Vorteil."
            : "Die Lösung führt zur Gewinnstellung.";
    }

    /// <param name="clubId">Ein Vereinskurs: Beschreibung und Freigabe an die Gruppen dieses Vereins.</param>
    private async Task<Book> EnsureBookAsync(string file, string name, int? owner, int? clubId, CancellationToken ct)
    {
        var club = clubId is not null;
        var book = await db.Books.FirstOrDefaultAsync(b => b.FileName == file, ct);
        if (book is not null) return book;
        var now = DateTime.UtcNow;
        book = new Book
        {
            FileName = file, DisplayName = name, OwnerUserId = owner, Kind = BookKind.Puzzle, Themes = "tactics",
            Tags = "tactics,harvest", ImportVersion = ImportPipeline.CurrentVersion, CreatedAt = now, UpdatedAt = now,
            Description = club
                ? "Taktiken, die in Partien von Vereinsmitgliedern auf dem Brett standen — gefunden und verpasst, je Ligarunde ein Kapitel."
                : "Automatisch aus analysierten Partien geerntete Taktiken — gefunden und verpasst.",
        };
        book.Source = new BookSource();
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);
        if (clubId is int cid)
        {
            // sichtbar für die Gruppen DES VEREINS (Mandanten-Schritt 2026-10-07; vorher: alle Gruppen mit league.view) —
            // eine später zugeordnete Gruppe bekommt die Freigabe über LeagueClubAdminService.AddGroupAsync
            foreach (var g in await LeagueClubResolver.GroupIdsOfAsync(db, cid, ct))
                db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = g });
            await db.SaveChangesAsync(ct);
        }
        return book;
    }

    /// <summary>Titel „Weiß – Schwarz (Jahr), Zug n" und Kapitel: bei Vereinspartien die Ligarunde, sonst gefunden/verpasst.</summary>
    private async Task<(string Title, string Chapter)> DescribeAsync(TacticCandidate c, GameAnalysis a, LeagueClub? club, CancellationToken ct)
    {
        var moveNo = c.Ply / 2 + 1;
        var tail = $", Zug {moveNo}";
        if (a.Origin == GameAnalysisOrigin.Club && club is not null && a.LeagueClubGameId is { } gid
            && await db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct) is { } g)
        {
            var round = await TacticLeagueRounds.LeagueRoundAsync(db, g, club, ct);
            return (TacticLeagueRounds.ClubTitle(g, round, club) + tail, round?.Chapter ?? OtherGamesChapter);
        }
        return ($"{a.White ?? "?"} – {a.Black ?? "?"}{tail}", c.Found ? "Gefunden" : "Verpasst");
    }
}
