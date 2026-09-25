using System.Text.Json.Nodes;
using RookHub.Api.DTOs;
using Serilog.Context;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Der eigene Broker, Seite des ANFRAGENDEN (in-Prozess, lila-engine <c>main.rs::analyse</c>): Auftrag
/// prüfen, in die Schlange des Selectors stellen, höchstens <see cref="LocalBrokerOptions.ProviderTimeout"/>
/// auf den Beginn des Uploads warten, dann die Zeilen als Strom liefern.
///
/// <para>Die Antwort-Codes sind dieselben wie bei lila-engine, weil Worker und Controller daran hängen:
/// <c>503</c> = kein Provider nimmt an (Timeout) oder Schlange voll — der Worker wechselt daraufhin die
/// Engine reihum, das Analysebrett fällt nach 12 s auf WASM zurück; <c>400</c> = ungültiger Auftrag.</para>
/// </summary>
public sealed class LocalEngineBroker(EngineHub hub, LocalBrokerOptions options, ILogger<LocalEngineBroker> logger)
{
    internal const string LogTags = "engine,broker";

    public async Task<EngineAnalysisSession> AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct)
    {
        var target = engine.Local ?? throw new ArgumentException("Not a local engine", nameof(engine));
        using var _ = LogContext.PushProperty("LogTags", LogTags);

        SanitizedWork sanitized;
        try
        {
            sanitized = WorkSanitizer.Sanitize(work, engine.MaxThreads, engine.MaxHash);
        }
        catch (InvalidWorkException ex)
        {
            logger.LogInformation("EngineBroker: Auftrag abgewiesen engine={EngineId}: {Reason}", engine.Id, ex.Message);
            return EngineAnalysisSession.Rejected(400, "invalid work: " + ex.Message);
        }

        var job = new PendingJob(target.Selector, engine.Id, EngineJson(target.Engine), sanitized.Work, sanitized.RootFen);
        hub.Stats.Note(engine.Id, c => c.Jobs++);
        if (!hub.Submit(job))
        {
            hub.Stats.Note(engine.Id, c => c.QueueFull++);
            logger.LogWarning("EngineBroker: Schlange voll engine={EngineId} ({Max} Auftraege)", engine.Id, options.MaxQueuedPerEngine);
            return EngineAnalysisSession.Rejected(503, "too many pending requests for this provider");
        }

        try
        {
            await job.Accepted.Task.WaitAsync(options.ProviderTimeout, ct);
        }
        catch (TimeoutException)
        {
            job.CancelRequester();
            hub.Stats.Note(engine.Id, c => c.ProviderTimeouts++);
            // Information, nicht Warning: bei einem ausgeschalteten Rechner ist das der Normalfall, und der
            // Worker wechselt ohnehin die Engine — sonst loeste jede Pause einen warn_spike aus.
            logger.LogInformation("EngineBroker: ProviderTimeout engine={EngineId} — kein Provider binnen {Timeout}",
                engine.Id, options.ProviderTimeout);
            return EngineAnalysisSession.Rejected(503, "provider did not pick up work");
        }
        catch (OperationCanceledException)
        {
            job.CancelRequester();
            throw;
        }

        return new EngineAnalysisSession(200, new JobNdjsonStream(job), dispose: () =>
        {
            job.CancelRequester();
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Das <c>engine</c>-Objekt der Abhol-Antwort in der Form von lila-engine <c>Engine</c>.</summary>
    public static JsonObject EngineJson(ExternalEngineRegistrationDto e) => new()
    {
        ["id"] = e.Id,
        ["name"] = e.Name,
        ["clientSecret"] = e.ClientSecret,
        ["userId"] = e.UserId,
        ["maxThreads"] = e.MaxThreads,
        ["maxHash"] = e.MaxHash,
        ["variants"] = new JsonArray([.. e.Variants.Select(v => (JsonNode?)JsonValue.Create(v))]),
        ["providerData"] = e.ProviderData,
    };
}
