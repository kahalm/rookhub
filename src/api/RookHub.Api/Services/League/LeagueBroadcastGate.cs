namespace RookHub.Api.Services.League;

/// <summary>
/// Immer nur EIN Einspielen einer Lichess-Übertragung zugleich (1.017.0): der schnelle Takt (<see cref="LeagueBroadcastLiveScheduler"/>),
/// der 6-Stunden-Takt (<see cref="LeagueOnlineSyncScheduler"/>) und „per Link hinzufügen“ dürften sonst dieselbe Partie gleichzeitig
/// in Spielerkarten und Vereins-Datenbank schreiben. Singleton; ohne (Tests) hat jeder <see cref="LeagueBroadcastImport"/> sein eigenes.
/// </summary>
public sealed class LeagueBroadcastGate
{
    private readonly SemaphoreSlim _one = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _one.WaitAsync(ct);
        return new Release(_one);
    }

    private sealed class Release(SemaphoreSlim s) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) s.Release();
        }
    }
}
