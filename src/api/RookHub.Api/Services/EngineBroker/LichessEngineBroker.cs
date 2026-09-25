namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Hülle um den bisherigen Lichess-Weg (<see cref="LichessEngineService.AnalyseAsync"/>): dieselbe
/// Anfrage an <c>engine.lichess.ovh</c>, dieselben Ausnahmen (Netzfehler, Abbruch) — nur als
/// <see cref="EngineAnalysisSession"/> verpackt, damit Controller und Worker beide Quellen gleich behandeln.
/// Die Semantik des Lichess-Pfads ändert sich dadurch nicht: der Broker bekommt dasselbe <c>work</c>, und
/// jede Antwort außer 2xx kommt mit ihrem Status zurück (503/504 ⇒ der Worker wechselt die Engine).
/// </summary>
public sealed class LichessEngineBroker(LichessEngineService lichess)
{
    public async Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct)
    {
        var target = engine.Lichess ?? throw new ArgumentException("Not a Lichess engine", nameof(engine));
        var upstream = await lichess.AnalyseAsync(target, work.ToJson(), ct);
        if (!upstream.IsSuccessStatusCode)
        {
            var code = (int)upstream.StatusCode;
            upstream.Dispose();
            return EngineAnalysisSession.Rejected(code, $"broker answered {code}");
        }
        try
        {
            var stream = await upstream.Content.ReadAsStreamAsync(ct);
            return new EngineAnalysisSession(200, stream, dispose: () =>
            {
                upstream.Dispose();
                return ValueTask.CompletedTask;
            });
        }
        catch
        {
            upstream.Dispose();
            throw;
        }
    }
}

/// <summary>Leitet je Quelle an den richtigen Broker weiter: <c>rhe_</c> → eigener Broker, sonst Lichess.</summary>
public sealed class EngineBrokerRouter(LocalEngineBroker local, LichessEngineBroker lichess) : IEngineBroker
{
    public Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct) =>
        engine.Source == EngineSource.Local
            ? local.AnalyseAsync(engine, work, ct)
            : lichess.AnalyseAsync(engine, work, ct);
}
