using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Services.Tactics;

/// <summary>
/// Pflege der schon veröffentlichten Aufgaben der Taktik-Ernte (aus <see cref="TacticHarvestService"/> herausgezogen,
/// 2026-11-09): verwaiste stilllegen, Vereinstitel nachziehen, alte Kommentare umformulieren, Partie-Verweise nachtragen,
/// Kapitel der Vereinskurse ordnen. Läuft in jedem Takt vor bzw. nach <see cref="TacticHarvestService.PublishAsync"/>.
/// Protokolliert unter der Kategorie des Dienstes.
/// </summary>
internal sealed class TacticHarvestUpkeep(AppDbContext db, ILogger<TacticHarvestService> log)
{
    /// <summary>
    /// Die Kapitel des Vereinskurses nach Jahrgang ordnen (Wunsch 2026-10-06): neueste Saison oben, darin Liga alphabetisch,
    /// Runden aufsteigend, „Andere Partien" zuletzt. Die Lesereihenfolge hängt an <see cref="BookPuzzle.Round"/>
    /// (<see cref="ChapterOrder"/>), die hier fortlaufend NEU vergeben wird — über alle Zeilen des Buchs, auch stillgelegte, damit die
    /// nächste Aufgabe (Anzahl + 1) hinten anschließt. Innerhalb eines Kapitels bleibt die bisherige Reihenfolge. Ändert sich
    /// nichts, wird nicht geschrieben.
    /// </summary>
    internal async Task SortClubChaptersAsync(string bookFile, CancellationToken ct)
    {
        var rows = await db.BookPuzzles.Where(p => p.BookFileName == bookFile).ToListAsync(ct);
        if (rows.Count < 2) return;
        var ordered = rows.OrderBy(p => p, Comparer<BookPuzzle>.Create(CompareClubRows)).ToList();
        var changed = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            var round = (i + 1).ToString();
            if (ordered[i].Round == round) continue;
            ordered[i].Round = round;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
    }

    private static int CompareClubRows(BookPuzzle a, BookPuzzle b)
    {
        var (ga, sa, la, ra) = ClubChapterKey(a.Chapter);
        var (gb, sb, lb, rb) = ClubChapterKey(b.Chapter);
        var c = ga.CompareTo(gb);
        if (c == 0) c = sb.CompareTo(sa);                          // neueste Saison zuerst
        if (c == 0) c = string.Compare(la, lb, StringComparison.CurrentCultureIgnoreCase);
        if (c == 0) c = ra.CompareTo(rb);
        return c != 0 ? c : ChapterOrder.Compare(a.Round, a.Id, b.Round, b.Id);
    }

    /// <summary>„2026/27 · Landesliga · Runde 1" → (0, 2026, „Landesliga", 1); „Andere Partien" und alles Unlesbare → Gruppe 1 (hinten).</summary>
    internal static (int Group, int Season, string League, int Round) ClubChapterKey(string? chapter)
    {
        var m = chapter is null ? null : System.Text.RegularExpressions.Regex.Match(chapter, @"^(\d{4})/\d{2} · (.+?) · Runde (\d+)$");
        return m is { Success: true } ? (0, int.Parse(m.Groups[1].Value), m.Groups[2].Value, int.Parse(m.Groups[3].Value)) : (1, 0, chapter ?? "", 0);
    }

    /// <summary>Verweis auf die Partie (<see cref="BookPuzzle.SourceGame"/>): Vereinspartie → <c>club</c>, eigene gespeicherte
    /// Partie → <c>own</c>; Meisterpartien und Handanalysen haben keine Partie-Seite → <c>null</c>. Der Halbzug ist die
    /// Stellung nach dem Fehler, also die, in der der Löser am Zug ist.</summary>
    internal static async Task<string?> SourceGameAsync(AppDbContext db, TacticCandidate c, GameAnalysis a, CancellationToken ct)
    {
        var ply = c.Ply;   // Zahl der gespielten Halbzüge bis zur Stellung nach dem Fehler
        if (a.Origin == GameAnalysisOrigin.Club && a.LeagueClubGameId is { } gid) return $"club:{gid}:{ply}";
        if (a.Origin == GameAnalysisOrigin.SavedGame
            && await db.SavedGames.AsNoTracking().Where(g => g.GameAnalysisId == a.Id).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct) is { } sid)
            return $"own:{sid}:{ply}";
        return null;
    }

    // Beide Abfragen laufen in JEDEM Takt (minütlich) und fangen mit dem Buch-Präfix an: über den Index auf BookFileName
    // sind es die paar hundert Aufgaben der Taktik-Bücher, ohne ihn ein Vollscan über alle BookPuzzles samt Kommentaren —
    // auf Prod (156 000 Zeilen) 20 s im Leerlauf, unter Last über dem Command-Timeout von 30 s: „Taktik-Ernte: Takt
    // fehlgeschlagen" alle 1–2 Minuten (gemeldet 2026-10-07), und die Veröffentlichung dahinter lief nie.

    /// <summary>Aufgaben mit altem Kommentar-Wortlaut (<see cref="RewordCommentsAsync"/>).</summary>
    internal static IQueryable<BookPuzzle> OldCommentPuzzles(IQueryable<BookPuzzle> puzzles)
        => puzzles.Where(p => p.BookFileName.StartsWith(TacticHarvestService.BookPrefix) && p.Source == "tactic-harvest" && p.Comment != null
            && (p.Comment.Contains("Nach dem ersten Zug: ") || p.Comment.Contains("Die Lösung bringt +") || p.Comment.Contains("Die Lösung bringt -")));

    /// <summary>Veröffentlichte Aufgaben ohne Partie-Verweis (<see cref="LinkGamesAsync"/>); die Meisterpartien haben keine Partie-Seite.</summary>
    internal static IQueryable<BookPuzzle> UnlinkedPuzzles(IQueryable<BookPuzzle> puzzles)
        => puzzles.Where(p => p.BookFileName.StartsWith(TacticHarvestService.BookPrefix) && p.Source == "tactic-harvest" && p.SourceGame == null && !p.Retired
            && p.BookFileName != TacticHarvestService.MasterBook);

    private static readonly System.Text.RegularExpressions.Regex OldOutcome =
        new(@" (?:Nach dem ersten Zug: |Die Lösung bringt )([+-]?\d+(?:\.\d+)?|#\d+)\.$");

    /// <summary>Alte Kommentare mit Zahl („… Nach dem ersten Zug: +5.3." bzw. „Die Lösung bringt +5.3.") auf die Worte von
    /// <see cref="TacticHarvestService.Outcome"/> umstellen (Matt-Texte „#3" der ersten Fassung ebenso).</summary>
    internal async Task RewordCommentsAsync(CancellationToken ct)
    {
        var puzzles = await OldCommentPuzzles(db.BookPuzzles).Take(200).ToListAsync(ct);
        foreach (var p in puzzles)
        {
            var m = OldOutcome.Match(p.Comment!);
            if (m.Success) p.Comment = p.Comment![..m.Index] + " " + TacticHarvestService.Outcome(m.Groups[1].Value);
        }
        if (puzzles.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>Schon veröffentlichte Aufgaben ohne Partie-Verweis nachtragen (0.672.0) — je Takt höchstens 200; was keinen
    /// Verweis bekommen kann, bleibt <c>null</c> und wird jedes Mal wieder angesehen, bleibt aber billig (nur Meisterpartien).</summary>
    internal async Task LinkGamesAsync(CancellationToken ct)
    {
        await RewordCommentsAsync(ct);
        var puzzles = await UnlinkedPuzzles(db.BookPuzzles).OrderBy(p => p.Id).Take(200).ToListAsync(ct);
        if (puzzles.Count == 0) return;
        var lineIds = puzzles.Select(p => p.LineId).ToList();
        var cands = await db.TacticCandidates.Include(c => c.GameAnalysis).Where(c => c.LineId != null && lineIds.Contains(c.LineId))
            .ToDictionaryAsync(c => c.LineId!, ct);
        var changed = 0;
        foreach (var p in puzzles)
        {
            if (!cands.TryGetValue(p.LineId, out var c) || c.GameAnalysis is null) continue;
            if (await SourceGameAsync(db, c, c.GameAnalysis, ct) is not { } link) continue;
            p.SourceGame = link;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>Schon veröffentlichte Aufgaben der Vereinskurse, die noch den Vereinsnamen (<see cref="LeagueClub.AnonName"/>)
    /// im Titel tragen, nach der Regel von <see cref="TacticLeagueRounds.ClubTitle"/> umbenennen (0.657.2) — einmal je Aufgabe, solange eine
    /// Ligarunde passt; je Verein sein Kurs und sein Name.</summary>
    internal async Task RetitleClubAsync(CancellationToken ct)
    {
        foreach (var club in await db.LeagueClubs.AsNoTracking().ToListAsync(ct)) await RetitleClubAsync(club, ct);
    }

    private async Task RetitleClubAsync(LeagueClub club, CancellationToken ct)
    {
        var anon = club.AnonName;
        var file = TacticHarvestService.ClubBookOf(club.Id);
        var puzzles = await db.BookPuzzles.Where(p => p.BookFileName == file && p.Source == "tactic-harvest" && !p.Retired
                && p.Title != null && (p.Title.StartsWith(anon + " –") || p.Title.Contains("– " + anon)))
            .Take(100).ToListAsync(ct);
        if (puzzles.Count == 0) return;
        var lineIds = puzzles.Select(p => p.LineId).ToList();
        var cands = await db.TacticCandidates.AsNoTracking().Include(c => c.GameAnalysis)
            .Where(c => c.LineId != null && lineIds.Contains(c.LineId)).ToDictionaryAsync(c => c.LineId!, ct);
        var changed = 0;
        foreach (var p in puzzles)
        {
            if (!cands.TryGetValue(p.LineId, out var c) || c.GameAnalysis?.LeagueClubGameId is not { } gid) continue;
            var g = await db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(x => x.Id == gid && x.ClubId == club.Id, ct);
            if (g is null || await TacticLeagueRounds.LeagueRoundAsync(db, g, club, ct) is not { } round) continue;
            var title = TacticLeagueRounds.ClubTitle(g, round, club) + $", Zug {c.Ply / 2 + 1}";
            if (title == p.Title) continue;
            p.Title = title;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>Aufgaben, deren Taktik es nicht mehr gibt (Partie gelöscht → Analyse → Taktik per Cascade), still legen.</summary>
    internal async Task RetireOrphansAsync(CancellationToken ct)
    {
        var lines = await db.BookPuzzles.Where(p => p.BookFileName.StartsWith(TacticHarvestService.BookPrefix) && !p.Retired && p.Source == "tactic-harvest")
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
