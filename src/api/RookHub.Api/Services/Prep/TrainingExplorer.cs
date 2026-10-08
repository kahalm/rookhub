namespace RookHub.Api.Services.Prep;

/// <summary>Zughäufigkeiten des Lichess-Explorers für die Schätzung der Trainingslinien — als Schnittstelle, damit Tests nie
/// echte Abfragen schicken.</summary>
public interface ITrainingExplorer
{
    /// <summary>Gibt es eine Quelle? Ohne bleibt jede Lücke ohne Quelle (Auffüllregel), und es wird nichts abgefragt.</summary>
    bool Available { get; }

    /// <summary>Die Stellungen <paramref name="positions"/> im Wertungsband des Gegners (<paramref name="elo"/>).</summary>
    /// <param name="positions">In der Reihenfolge ihrer Wichtigkeit — bei knapper Frist kommen die ersten zuerst dran.</param>
    /// <param name="budget">Höchstens so lange abfragen; der Rest bleibt offen.</param>
    /// <param name="maxNew">Höchstens so viele noch nicht gespeicherte neu anfragen; gespeicherte zählen nicht dagegen.</param>
    /// <param name="parallelism">Gleichzeitige Abfragen (Hintergrund weniger als Vordergrund); <c>null</c> = Vorgabe.</param>
    Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, TimeSpan budget, int maxNew,
        CancellationToken ct, int? parallelism = null);
}

/// <param name="Band">Anzeige des Bands, z. B. „2000–2300".</param>
public sealed record TrainingExplorerResult(IReadOnlyDictionary<string, ExplorerPositionStats> Stats, IReadOnlySet<string> Pending, string Band,
    int FromMemory = 0, int Asked = 0, int Capped = 0);

/// <summary>
/// Explorer-Anbindung der Trainingslinien (Wunsch 2026-10-07: „nimm lichesspartien, +100 - +400 elo"): Spieler im Band
/// Gegner-Elo +100 bis +400 (Lichess-Wertungen liegen über FIDE — dieselbe Regel wie die Konto-Prüfung), Blitz/Schnell/Klassisch,
/// über <see cref="RepertoireExplorerService.BatchStatsAsync"/> — NUR der lokale Explorer (<c>LichessExplorer:LocalUrl</c>, Dienst
/// <c>rookhub-explorer</c>, nur dessen Stufen), nie explorer.lichess.ovh (Vorgabe des Users 2026-10-08). Ohne <c>LocalUrl</c> gibt es
/// keine Schätzung: <see cref="Available"/> ist falsch, nichts wird abgefragt, kein Token nötig.
/// </summary>
public sealed class TrainingExplorer(RepertoireExplorerService explorer, LocalExplorerClient local) : ITrainingExplorer
{
    /// <summary>Ohne bekannte Elo.</summary>
    public const int DefaultElo = 1800;
    public static readonly IReadOnlyList<string> Speeds = ["blitz", "rapid", "classical"];

    public bool Available => local.IsConfigured;

    public async Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, TimeSpan budget,
        int maxNew, CancellationToken ct, int? parallelism = null)
    {
        if (!Available)
            return new TrainingExplorerResult(new Dictionary<string, ExplorerPositionStats>(), new HashSet<string>(), Band(elo));
        var query = ExplorerQuery.Create(ExplorerQuery.Lichess, Stages(elo, LocalExplorerClient.LocalRatings), Speeds);
        var r = await explorer.BatchStatsAsync(positions.Select(n => (n.Key, n.Fen)).ToList(), query, ct, budget, maxNew, parallelism);
        return new TrainingExplorerResult(r.Stats, r.Pending, Band(elo), r.FromMemory, r.Asked, r.Capped);
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
