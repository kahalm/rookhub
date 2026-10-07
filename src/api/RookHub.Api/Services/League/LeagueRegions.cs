using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Liga-Regionen (2026-10-07, Schritt „Schachkreis Zugspitze als dritte Liga-Quelle"): eine Region fasst die Liga-Quellen
/// zusammen, deren Spieler sich überschneiden — <see cref="Tirol"/> = chess-results (<see cref="LeagueTournament.Source"/>
/// <c>null</c>), <see cref="Bayern"/> = SBV-Ligamanager (<see cref="LigamanagerSource"/>) + Schachkreis Zugspitze
/// (<see cref="ZugspitzeSource"/>). SK Weilheim spielt mit der ersten Mannschaft im Ligamanager und mit den unteren im
/// Schachkreis — derselbe Spieler steht in beiden Quellen, und „spielt am selben Tag für die Zweite" (<c>sameDay</c>) oder
/// „Einsätze eine Stufe tiefer" (QLower) müssen beide sehen. Deshalb hängen der Nenner der Einsatzquote
/// (<see cref="LeagueWorld.Mpt"/>), die Teams eines Vereins (<see cref="LeagueWorld.ClubTeams"/>), die Vereinsnamen-Regel
/// (<see cref="LeagueNames.Club"/>), das Nachfüllen der FIDE-IDs (<see cref="FillMissingFideAsync"/>) und — am Verein —
/// die Startseite an der REGION, nicht an der Quelle. <b>Die Abbildung Quelle → Region steht nur hier.</b>
/// </summary>
public static class LeagueRegions
{
    public const string Tirol = "tirol";
    public const string Bayern = "bayern";

    /// <summary>Alle Regionen in der Reihenfolge der Auswahl.</summary>
    public static readonly IReadOnlyList<string> All = [Tirol, Bayern];

    /// <summary>Region einer Liga-Quelle: <c>null</c> (chess-results) → Tirol, Ligamanager/Zugspitze → Bayern; eine
    /// unbekannte Quelle ist ihre eigene Region (mischt sich mit nichts).</summary>
    public static string Of(string? source) => source switch
    {
        null => Tirol,
        LigamanagerSource.Source or ZugspitzeSource.Source => Bayern,
        _ => source,
    };

    /// <summary>Die Quellen einer Region (Tirol: nur <c>null</c>).</summary>
    public static IReadOnlyList<string?> SourcesOf(string? region) => region switch
    {
        Tirol => [null],
        Bayern => [LigamanagerSource.Source, ZugspitzeSource.Source],
        _ => [],
    };

    public static bool Valid(string? region) => region is Tirol or Bayern;

    /// <summary>Die Ligen einer Region — als SQL-taugliche Bedingung (Tirol = <c>Source IS NULL</c>, sonst <c>IN (…)</c>).</summary>
    public static IQueryable<LeagueTournament> InRegion(this IQueryable<LeagueTournament> q, string? region)
    {
        if (region == Tirol) return q.Where(t => t.Source == null);
        var sources = SourcesOf(region).OfType<string>().ToList();
        return q.Where(t => t.Source != null && sources.Contains(t.Source));
    }

    /// <summary>
    /// Fehlende FIDE-IDs in ALLEN Ligen einer Region ergänzen (Meldeliste + Brettpaarungen): gleicher Verein
    /// (<see cref="LeagueNames.Club"/>, „SK Weilheim 1" und „SK Weilheim II" → „SK Weilheim") und gleicher
    /// <see cref="LeaguePlayer.NameKey"/>, und dazu steht in den Ligen der Region genau EINE ID. So bekommen ältere
    /// Ligamanager-Saisonen (verlinkt ist nur die laufende) und alle Zugspitze-Ligen (die kennen gar keine FIDE-IDs) die ID aus
    /// der Meldeliste des Ligamanagers. Speichert selbst. → ergänzte Meldelisten-Zeilen.
    /// </summary>
    public static async Task<int> FillMissingFideAsync(AppDbContext db, string region, CancellationToken ct)
    {
        var ts = await db.LeagueTournaments.InRegion(region).Select(t => new { t.Tnr, t.Source }).ToListAsync(ct);
        if (ts.Count == 0) return 0;
        var tnrs = ts.Select(t => t.Tnr).ToList();
        var srcOf = ts.ToDictionary(t => t.Tnr, t => t.Source);
        var players = await db.LeaguePlayers.Where(p => tnrs.Contains(p.Tnr)).ToListAsync(ct);
        string ClubOf(int tnr, string team) => LeagueNames.Club(team, srcOf[tnr]);
        var known = players.Where(p => !string.IsNullOrEmpty(p.FideId))
            .GroupBy(p => (ClubOf(p.Tnr, p.Team), p.NameKey))
            .Select(g => (g.Key, Ids: g.Select(p => p.FideId!).Distinct().ToList()))
            .Where(x => x.Ids.Count == 1).ToDictionary(x => x.Key, x => x.Ids[0]);
        var filled = 0;
        foreach (var p in players.Where(p => string.IsNullOrEmpty(p.FideId)))
            if (known.TryGetValue((ClubOf(p.Tnr, p.Team), p.NameKey), out var f)) { p.FideId = f; filled++; }
        if (filled == 0) return 0;
        var roster = players.Where(p => !string.IsNullOrEmpty(p.FideId))
            .GroupBy(p => (p.Tnr, p.Team, p.NameKey)).ToDictionary(g => g.Key, g => g.First().FideId);
        var games = await db.LeagueGames.Where(g => tnrs.Contains(g.Tnr)
            && ((g.HomePlayer != null && g.HomeFide == null) || (g.AwayPlayer != null && g.AwayFide == null))).ToListAsync(ct);
        foreach (var g in games)
        {
            if (g.HomePlayer is { } hp && g.HomeFide is null) g.HomeFide = roster.GetValueOrDefault((g.Tnr, g.HomeTeam, LeagueNames.NameKey(hp)));
            if (g.AwayPlayer is { } ap && g.AwayFide is null) g.AwayFide = roster.GetValueOrDefault((g.Tnr, g.AwayTeam, LeagueNames.NameKey(ap)));
        }
        await db.SaveChangesAsync(ct);
        return filled;
    }
}
