namespace RookHub.Api.Services.Games;

/// <summary>
/// Zählt die Würfe („Neu würfeln“) je Nutzer über 24 Stunden (G2-001). Der Tagesdeckel zählte vorher nur GameRoasts-ZEILEN;
/// ein neuer Wurf derselben Partie/Sprache/Stil überschreibt aber dieselbe Zeile, und der Zähler stieg nie — Dauerklicken
/// auf eine Partie war unbegrenzt, jeder Wurf ein voller Aufruf der geteilten Spark. Als Singleton im Prozess (eine
/// API-Instanz); nach einem Neustart zählt zusätzlich weiter die Zeilen-Zählung im Dienst.
/// </summary>
public sealed class GameRoastRolls
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(1);
    private readonly Dictionary<int, Queue<DateTime>> _byUser = new();
    private readonly Func<DateTime> _utcNow;

    public GameRoastRolls() : this(() => DateTime.UtcNow) { }
    internal GameRoastRolls(Func<DateTime> utcNow) => _utcNow = utcNow;

    /// <summary>Einen Wurf buchen, wenn der Nutzer in den letzten 24 h weniger als <paramref name="max"/> hatte.
    /// <c>false</c> = Deckel erreicht (nichts gebucht).</summary>
    public bool TryTake(int userId, int max)
    {
        var now = _utcNow();
        lock (_byUser)
        {
            if (!_byUser.TryGetValue(userId, out var q)) _byUser[userId] = q = new Queue<DateTime>();
            while (q.Count > 0 && q.Peek() <= now - Window) q.Dequeue();
            if (q.Count >= max) return false;
            q.Enqueue(now);
            if (_byUser.Count > 10_000) Prune(now);
            return true;
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var (user, q) in _byUser.ToList())
        {
            while (q.Count > 0 && q.Peek() <= now - Window) q.Dequeue();
            if (q.Count == 0) _byUser.Remove(user);
        }
    }
}
