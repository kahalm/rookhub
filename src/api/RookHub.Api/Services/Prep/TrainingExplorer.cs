namespace RookHub.Api.Services.Prep;

/// <summary>Zughäufigkeiten des Lichess-Explorers für die Schätzung der Trainingslinien — als Schnittstelle, damit Tests nie
/// echte Abfragen schicken.</summary>
public interface ITrainingExplorer
{
    /// <summary>Die Stellungen <paramref name="positions"/> im Wertungsband des Gegners (<paramref name="elo"/>).</summary>
    Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, CancellationToken ct);
}

/// <param name="Band">Anzeige des Bands, z. B. „2000–2300".</param>
public sealed record TrainingExplorerResult(IReadOnlyDictionary<string, ExplorerPositionStats> Stats, IReadOnlySet<string> Pending, string Band);

/// <summary>
/// Explorer-Anbindung der Trainingslinien (Wunsch 2026-10-07: „nimm lichesspartien, +100 - +400 elo"): Spieler im Band
/// Gegner-Elo +100 bis +400 (Lichess-Wertungen liegen über FIDE — dieselbe Regel wie die Konto-Prüfung), Blitz/Schnell/Klassisch,
/// über <see cref="RepertoireExplorerService.BatchStatsAsync"/> — lokal, wenn <c>LichessExplorer:LocalUrl</c> gesetzt ist (dann nur
/// dessen Stufen), sonst online mit Token, Leitung und Budget des Lochfinders.
/// </summary>
public sealed class TrainingExplorer(RepertoireExplorerService explorer, LocalExplorerClient local) : ITrainingExplorer
{
    /// <summary>Ohne bekannte Elo.</summary>
    public const int DefaultElo = 1800;
    public static readonly IReadOnlyList<string> Speeds = ["blitz", "rapid", "classical"];

    public async Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, CancellationToken ct)
    {
        var useLocal = local.IsConfigured;
        var stages = Stages(elo, useLocal ? LocalExplorerClient.LocalRatings : ExplorerQuery.AllowedRatings);
        var query = ExplorerQuery.Create(ExplorerQuery.Lichess, stages, Speeds);
        var r = await explorer.BatchStatsAsync(userId, positions.Select(n => (n.Key, n.Fen)).ToList(), query, useLocal, ct);
        return new TrainingExplorerResult(r.Stats, r.Pending, Band(elo));
    }

    /// <summary>„2000–2300" für Elo 1900.</summary>
    public static string Band(int elo) => $"{elo + 100}–{elo + 400}";

    /// <summary>
    /// Die Explorer-Stufen, deren Bereich das Band [Elo+100, Elo+400] schneidet — eine Stufe reicht von ihrem Wert bis vor die
    /// nächste, die oberste ist nach oben offen. Mindestens eine: liegt das Band ganz unter bzw. über allen, die nächstgelegene.
    /// </summary>
    public static List<int> Stages(int elo, IReadOnlyList<int> available)
    {
        var lo = elo + 100;
        var hi = elo + 400;
        var sorted = available.OrderBy(x => x).ToList();
        var hit = new List<int>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var from = sorted[i];
            var to = i + 1 < sorted.Count ? sorted[i + 1] : int.MaxValue;   // [from, to)
            if (from <= hi && to > lo) hit.Add(from);
        }
        if (hit.Count == 0) hit.Add(hi < sorted[0] ? sorted[0] : sorted[^1]);
        return hit;
    }
}
