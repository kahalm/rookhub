using System.Threading.RateLimiting;

namespace RookHub.Api.Services;

/// <summary>
/// EIN Limiter je Partition, der ein festes Fenster UND einen Gleichzeitigkeitsdeckel prüft — eine benannte Policy
/// kann je Endpunkt nur einen Limiter liefern (Baummodus/Ähnlichkeitssuche, <see cref="RateLimitPartitions.RepertoireScan"/>).
///
/// <para><b>Reihenfolge:</b> erst der Platz (<see cref="ConcurrencyLimiter"/>), dann das Fenster. Wer am Platz scheitert
/// oder als älterer Wartender verdrängt wird, verbraucht so kein Fenster-Kontingent; scheitert das Fenster, gibt die
/// Kette den schon erhaltenen Platz sofort zurück.</para>
///
/// <para><b>Warum eine eigene Klasse statt <see cref="RateLimiter.CreateChained"/> allein:</b> Das Fenster läuft ohne
/// eigenen Timer (<c>AutoReplenishment = false</c>) wie bei <see cref="RateLimitPartition.GetFixedWindowLimiter{TKey}"/>;
/// nachgefüllt wird es vom Herzschlag des partitionierten Limiters, der dafür nur <see cref="ReplenishingRateLimiter"/>
/// anspricht — eine nackte Kette wäre das nicht, und das Fenster bliebe nach dem ersten Ausschöpfen für immer leer.
/// Außerdem entsorgt die Kette ihre Glieder nicht; das tut diese Klasse, wenn der partitionierte Limiter eine untätige
/// Partition wegwirft.</para>
/// </summary>
public sealed class WindowAndConcurrencyLimiter : ReplenishingRateLimiter
{
    private readonly ConcurrencyLimiter _concurrency;
    private readonly FixedWindowRateLimiter _window;
    private readonly RateLimiter _chain;

    public WindowAndConcurrencyLimiter(int permitPerWindow, TimeSpan window, int concurrent, int queue)
    {
        _concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = concurrent,
            QueueLimit = queue,
            // Der jüngste Wartende zuerst; ist die Schlange voll, fliegt der älteste mit einer Absage heraus.
            QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
        });
        _window = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitPerWindow,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = false,
        });
        _chain = CreateChained(_concurrency, _window);
    }

    public override bool IsAutoReplenishing => false;
    public override TimeSpan ReplenishmentPeriod => _window.ReplenishmentPeriod;
    public override bool TryReplenish() => _window.TryReplenish();

    /// <summary>Untätig erst, wenn BEIDE es sind: kein Platz belegt, keiner wartet, und das Fenster ist wieder voll.</summary>
    public override TimeSpan? IdleDuration =>
        _concurrency.IdleDuration is { } a && _window.IdleDuration is { } b ? (a < b ? a : b) : null;

    public override RateLimiterStatistics? GetStatistics() => _chain.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => _chain.AttemptAcquire(permitCount);

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        _chain.AcquireAsync(permitCount, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _chain.Dispose();
        _concurrency.Dispose();
        _window.Dispose();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _chain.DisposeAsync().ConfigureAwait(false);
        await _concurrency.DisposeAsync().ConfigureAwait(false);
        await _window.DisposeAsync().ConfigureAwait(false);
    }
}
