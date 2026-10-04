using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

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
/// <item><see cref="PublishAsync"/>: fertige Taktiken als Aufgaben in einen Kurs — Vereinspartien in „Taktiken aus
/// Vereinspartien" (Kapitel je Ligarunde, Wunsch 2026-10-04; sichtbar für die Gruppen mit LeagueHub-Leserecht),
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
    public const string JobTitle = "Taktik-Ernte";
    public const string ClubBook = "tactics-club.pgn";
    public const string MasterBook = "tactics-masters.pgn";
    public static string OwnBook(int userId) => $"tactics-u{userId}.pgn";
    public const string BookPrefix = "tactics-";
    public const string OtherGamesChapter = "Andere Partien";

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
            .Where(a => a.Status == GameAnalysisStatus.Done && a.TacticsScannedAt == null && Sources.Contains(a.Origin))
            // Vereinspartien zuerst, die neuesten vorneweg (0.657.1: die Partien vom Liga-Wochenende sollen nicht hinter
            // 3.600 älteren Meisterpartien warten), dann eigene Partien, dann der Rest
            .OrderBy(a => a.Origin == GameAnalysisOrigin.Club ? 0 : a.Origin == GameAnalysisOrigin.SavedGame ? 1 : 2)
            .ThenByDescending(a => a.Id).Take(ScanBatch).ToListAsync(ct);
        if (analyses.Count == 0) return 0;
        var ids = analyses.Select(a => a.Id).ToList();
        var positions = (await db.GameAnalysisPositions.AsNoTracking().Where(p => ids.Contains(p.GameAnalysisId))
                .Select(p => new { p.GameAnalysisId, p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson }).ToListAsync(ct))
            .GroupBy(p => p.GameAnalysisId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Ply).ToList());
        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var a in analyses)
        {
            a.TacticsScannedAt = now;
            if (!positions.TryGetValue(a.Id, out var ps)) continue;
            for (var i = 1; i < ps.Count; i++)
            {
                if (ps[i].Ply != ps[i - 1].Ply + 1) continue;
                var found = TacticHarvest.Detect(TacticHarvest.Parse(ps[i - 1].CandidatesJson), TacticHarvest.Parse(ps[i].CandidatesJson));
                if (found is null) continue;
                var c = Start(a, ps[i - 1].Fen, ps[i - 1].GameMoveUci, ps[i].Ply, ps[i].Fen, ps[i].GameMoveUci, found, now);
                if (c is null) continue;
                db.TacticCandidates.Add(c);
                added++;
            }
        }
        await db.SaveChangesAsync(ct);
        if (added > 0) log.LogInformation("Taktik-Ernte: {Count} Kandidaten aus {Games} Analysen", added, analyses.Count);
        return added;
    }

    /// <summary>Kandidat anlegen: erster Zug (normiert), gefunden ja/nein, nächster Schritt aus der Hauptvariante.</summary>
    internal static TacticCandidate? Start(GameAnalysis a, string prevFen, string blunderUci, int ply, string fen, string gameUci,
        TacticHarvest.Found found, DateTime now)
    {
        var first = TacticHarvest.Play(fen, found.Best.Uci);
        if (first is null || TacticHarvest.Play(prevFen, blunderUci) is null) return null;
        var bestUci = GamePlies.ToUci(first.Value.Move);
        var played = TacticHarvest.Play(fen, gameUci);
        var c = new TacticCandidate
        {
            GameAnalysisId = a.Id, Ply = ply, Origin = a.Origin, PrevFen = prevFen, BlunderUci = blunderUci, Fen = fen,
            SolverWhite = fen.Split(' ').ElementAtOrDefault(1) == "w", GameMoveUci = gameUci,
            Found = played is not null && played.Value.Fen == first.Value.Fen, Kind = found.Kind, Moves = bestUci,
            EvalText = TacticHarvest.EvalText(found.Best), Status = TacticCandidateStatus.Verifying, CreatedAt = now, UpdatedAt = now,
        };
        Continue(c, first.Value.Fen, first.Value.Over, found.Best.Pv);
        return c;
    }

    /// <summary>Nach einem bestätigten Zug des Lösers: weiter mit der Antwort aus der Hauptvariante, oder fertig.</summary>
    private static void Continue(TacticCandidate c, string fenAfterSolver, bool over, IReadOnlyList<string> pv)
    {
        var solverMoves = (c.Moves.Split(' ').Length + 1) / 2;
        var reply = pv.Count > 1 ? pv[1] : null;
        var next = !over && solverMoves < TacticHarvest.MaxSolverMoves && reply is not null ? TacticHarvest.Play(fenAfterSolver, reply) : null;
        if (next is null || next.Value.Over)
        {
            Finish(c);
            return;
        }
        c.PendingReplyUci = GamePlies.ToUci(next.Value.Move);
        c.NextFen = next.Value.Fen;
        c.AnalysisJobId = null;
        c.Attempts = 0;
    }

    private static void Finish(TacticCandidate c)
    {
        c.Status = TacticCandidateStatus.Done;
        c.PendingReplyUci = null;
        c.NextFen = null;
        c.AnalysisJobId = null;
        var solution = c.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        c.Themes = string.Join(',', TacticHarvest.Themes(c.Fen, solution, c.Kind));
        c.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Ergebnis der Stellung <see cref="TacticCandidate.NextFen"/> (Löser am Zug): eindeutig → weiter, sonst fertig.</summary>
    internal static void Step(TacticCandidate c, IReadOnlyList<TacticHarvest.Cand> cands)
    {
        var mate = c.Kind == "mate";
        if (cands.Count == 0 || c.NextFen is null || c.PendingReplyUci is null || !TacticHarvest.IsUnique(cands, mate)
            || (mate && cands[0].Mate is not > 0)
            || (!mate && cands[0].Mate is null && (cands[0].Cp ?? 0) < TacticHarvest.MinAdvantageCp))
        {
            Finish(c);
            return;
        }
        var played = TacticHarvest.Play(c.NextFen, cands[0].Uci);
        if (played is null)
        {
            Finish(c);
            return;
        }
        c.Moves = $"{c.Moves} {c.PendingReplyUci} {GamePlies.ToUci(played.Value.Move)}";
        c.UpdatedAt = DateTime.UtcNow;
        Continue(c, played.Value.Fen, played.Value.Over, cands[0].Pv);
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

    public async Task PumpAsync(int engineOwner, CancellationToken ct)
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
                    Step(c, parsed.Select(p => new TacticHarvest.Cand(p.Uci, p.Cp, p.Mate, p.Pv ?? Array.Empty<string>())).ToList());
                    db.AnalysisJobs.Remove(job);
                }
                else if (job.Status == AnalysisJobStatus.Failed)
                {
                    db.AnalysisJobs.Remove(job);
                    c.AnalysisJobId = null;
                    if (++c.Attempts >= MaxAttempts) Finish(c);
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

    // ── 3. Veröffentlichen ──

    public async Task<int> PublishAsync(CancellationToken ct)
    {
        await RetireOrphansAsync(ct);
        var done = await db.TacticCandidates.Include(c => c.GameAnalysis)
            .Where(c => c.Status == TacticCandidateStatus.Done).OrderBy(c => c.Id).Take(200).ToListAsync(ct);
        if (done.Count == 0) return 0;
        var books = new Dictionary<string, Book>();
        var rounds = new Dictionary<string, int>();
        var published = 0;
        foreach (var c in done)
        {
            var a = c.GameAnalysis!;
            var (file, name, owner) = a.Origin switch
            {
                GameAnalysisOrigin.Club => (ClubBook, "Taktiken aus Vereinspartien", (int?)null),
                GameAnalysisOrigin.Library => (MasterBook, "Taktiken aus Meisterpartien", (int?)null),
                _ => (OwnBook(a.UserId), "Taktiken aus meinen Partien", (int?)a.UserId),
            };
            if (!books.TryGetValue(file, out var book))
            {
                book = await EnsureBookAsync(file, name, owner, a.Origin == GameAnalysisOrigin.Club, ct);
                books[file] = book;
                rounds[file] = await db.BookPuzzles.CountAsync(p => p.BookFileName == file, ct);
            }
            var (title, chapter) = await DescribeAsync(c, a, ct);
            var san = c.Found ? null : MoveComparisonService.SanOf(c.Fen, c.GameMoveUci);
            var comment = (c.Found ? "In der Partie gefunden." : $"In der Partie verpasst — gespielt wurde {san ?? c.GameMoveUci}.")
                + $" Nach dem ersten Zug: {c.EvalText}.";
            var round = rounds[file] = rounds[file] + 1;
            var lineId = $"{file}:t{c.Id}";
            db.BookPuzzles.Add(new BookPuzzle
            {
                LineId = lineId, BookFileName = file, BookId = book.Id, Round = round.ToString(), Fen = c.PrevFen,
                Moves = $"{c.BlunderUci} {c.Moves}", StartPly = 0, Title = title, Chapter = chapter, Comment = comment,
                Tags = string.Join(',', new[] { c.Found ? "gefunden" : "verpasst" }.Concat((c.Themes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))),
                Source = "tactic-harvest",
            });
            c.Status = TacticCandidateStatus.Published;
            c.LineId = lineId;
            c.UpdatedAt = DateTime.UtcNow;
            book.UpdatedAt = DateTime.UtcNow;
            published++;
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("Taktik-Ernte: {Count} Aufgaben in die Kurse gelegt", published);
        return published;
    }

    private async Task<Book> EnsureBookAsync(string file, string name, int? owner, bool club, CancellationToken ct)
    {
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
        if (club)
        {
            // sichtbar für die Gruppen, deren Rollen LeagueHub lesen dürfen (die Vereinsgruppe)
            var roleIds = await db.RolePermissions.Where(r => r.Permission == Permissions.LeagueView).Select(r => r.RoleId).ToListAsync(ct);
            var groupIds = await db.GroupRoles.Where(g => roleIds.Contains(g.RoleId)).Select(g => g.GroupId).Distinct().ToListAsync(ct);
            foreach (var g in groupIds) db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = g });
            await db.SaveChangesAsync(ct);
        }
        return book;
    }

    /// <summary>Titel „Weiß – Schwarz (Jahr), Zug n" und Kapitel: bei Vereinspartien die Ligarunde, sonst gefunden/verpasst.</summary>
    private async Task<(string Title, string Chapter)> DescribeAsync(TacticCandidate c, GameAnalysis a, CancellationToken ct)
    {
        var moveNo = c.Ply / 2 + 1;
        var tail = $", Zug {moveNo}";
        if (a.Origin == GameAnalysisOrigin.Club && a.LeagueClubGameId is { } gid
            && await db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct) is { } g)
        {
            var title = $"{g.White} – {g.Black}{(g.Year is { } y ? $" ({y})" : "")}{tail}";
            return (title, await LeagueRoundChapterAsync(db, g, ct) ?? OtherGamesChapter);
        }
        return ($"{a.White ?? "?"} – {a.Black ?? "?"}{tail}", c.Found ? "Gefunden" : "Verpasst");
    }

    /// <summary>
    /// Die Ligarunde einer Vereinspartie (Wunsch 2026-10-04: „pro Liga-Runde ein Kapitel"): eine Liga-Partie mit denselben
    /// Spielern in denselben Farben — die „Schwaz"-Seite einer anonymisierten Partie passt zu jedem Spieler des eigenen
    /// Vereins —, in der Saison des Jahres; bei mehreren gewinnt die mit gleichem Ergebnis. → „2026/27 · Landesliga ·
    /// Runde 1", sonst <c>null</c>.
    /// </summary>
    internal static async Task<string?> LeagueRoundChapterAsync(AppDbContext db, LeagueClubGame g, CancellationToken ct)
    {
        var fides = new[] { g.WhiteFide, g.BlackFide }.Where(f => !string.IsNullOrEmpty(f)).ToList();
        if (fides.Count == 0) return null;
        var rows = await (from lg in db.LeagueGames.AsNoTracking()
                          join t in db.LeagueTournaments.AsNoTracking() on lg.Tnr equals t.Tnr
                          where fides.Contains(lg.HomeFide!) || fides.Contains(lg.AwayFide!)
                          select new { lg, t.Season, t.League, t.Grp }).ToListAsync(ct);
        bool Side(string? clubFide, string clubName, string? fide, string team) =>
            clubFide != null ? clubFide == fide : clubName == LeagueClubService.AnonymousName && team.StartsWith(LeagueRefresh.OwnTeam);
        bool InSeason(string season) => g.Year is not { } y || (int.TryParse(season.Split('/')[0], out var s) && (s == y || s + 1 == y));
        var hits = rows.Where(r =>
        {
            var homeWhite = r.lg.HomeColor == "w";
            var (wf, wt, bf, bt) = homeWhite
                ? (r.lg.HomeFide, r.lg.HomeTeam, r.lg.AwayFide, r.lg.AwayTeam)
                : (r.lg.AwayFide, r.lg.AwayTeam, r.lg.HomeFide, r.lg.HomeTeam);
            return Side(g.WhiteFide, g.White, wf, wt) && Side(g.BlackFide, g.Black, bf, bt) && InSeason(r.Season);
        }).ToList();
        if (hits.Count == 0) return null;
        string Norm(string r) => r.Replace(" ", "").Replace("½", "1/2");
        var best = hits.OrderByDescending(r => Norm(r.lg.Result) == Norm(g.Result) ? 1 : 0).ThenByDescending(r => r.Season).First();
        var league = string.IsNullOrEmpty(best.Grp) ? best.League : $"{best.League} {best.Grp}";
        return $"{best.Season} · {league} · Runde {best.lg.Round}";
    }

    /// <summary>Aufgaben, deren Taktik es nicht mehr gibt (Partie gelöscht → Analyse → Taktik per Cascade), still legen.</summary>
    private async Task RetireOrphansAsync(CancellationToken ct)
    {
        var lines = await db.BookPuzzles.Where(p => p.BookFileName.StartsWith(BookPrefix) && !p.Retired && p.Source == "tactic-harvest")
            .Select(p => new { p.Id, p.LineId }).ToListAsync(ct);
        if (lines.Count == 0) return;
        var known = (await db.TacticCandidates.Where(c => c.LineId != null).Select(c => c.LineId!).ToListAsync(ct)).ToHashSet();
        var orphanIds = lines.Where(l => !known.Contains(l.LineId)).Select(l => l.Id).ToList();
        if (orphanIds.Count == 0) return;
        foreach (var p in await db.BookPuzzles.Where(p => orphanIds.Contains(p.Id)).ToListAsync(ct)) p.Retired = true;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Taktik-Ernte: {Count} Aufgaben stillgelegt (Partie entfernt)", orphanIds.Count);
    }
}
