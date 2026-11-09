using System.Collections.Concurrent;

namespace RookHub.Api.Services.Engines;

/// <summary>Welche Vergleiche gerade erklärt werden — damit die Pumpe keinen zweiten Lauf startet. Nur Arbeitsspeicher:
/// nach einem Neustart startet die Pumpe den Lauf neu.</summary>
public sealed class MoveComparisonExplainJobs
{
    private readonly ConcurrentDictionary<int, byte> _running = new();
    public bool TryStart(int id) => _running.TryAdd(id, 0);
    public void Finish(int id) => _running.TryRemove(id, out _);
    public bool IsRunning(int id) => _running.ContainsKey(id);
}
