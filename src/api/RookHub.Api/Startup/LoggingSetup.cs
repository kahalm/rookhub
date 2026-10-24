using Elastic.Serilog.Sinks;
using RookHub.Api.Logging;
using System.Security.Claims;
using Serilog;
using Serilog.Events;

namespace RookHub.Api.Startup;

/// <summary>Serilog für den Host: Stufen je Quelle (Logging/SerilogLevels.cs), Anreicherung, Konsole und optional
/// Elasticsearch (Data-Stream).</summary>
public static class LoggingSetup
{
    public static IHostBuilder UseRookHubSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, services, configuration) =>
        {
            configuration
                // Grundlevel + Overrides (u. a. HttpClient auf Warning) in Logging/SerilogLevels.cs.
                .ApplyRookHubLevels()
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithProperty("Application", "RookHub")
                .WriteTo.Console();

            var esUrl = context.Configuration["Elasticsearch:Url"];
            if (!string.IsNullOrEmpty(esUrl))
            {
                // ECS-Schema (Elastic.Serilog.Sinks) in einen Data-Stream. Felder werden zentral per
                // Ingest-Pipeline normalisiert (siehe log-watcher/schema/logging-schema.md).
                // Data-Stream-Basisname aus dem bisherigen Monats-IndexFormat ableiten (Teil vor "{"),
                // damit dev/prod unter ihren bestehenden "*-logs-*"-Patterns bleiben (Kibana, log-watcher).
                var indexFormat = context.Configuration["Elasticsearch:IndexFormat"] ?? "rookhub-logs-{0:yyyy.MM}";
                var streamName = indexFormat.Split('{')[0].TrimEnd('-', '.', ' ');
                configuration.WriteTo.Elasticsearch([new Uri(esUrl)], opts =>
                {
                    opts.DataStream = new Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName(streamName);
                    opts.BootstrapMethod = Elastic.Ingest.Elasticsearch.BootstrapMethod.Silent;
                });
            }
        });

    /// <summary>Request-Completion-Log (eine Zeile je Anfrage) mit UserId/UserName und Stufe je Pfad/Status.</summary>
    public static IApplicationBuilder UseRookHubRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            // UserId/UserName ans Request-Completion-Log hängen. Die LogContext-Anreicherungs-Middleware
            // (RequestLogContextMiddleware) läuft VOR UseAuthorization — ApiToken-authentifizierte Requests (z. B. die RepCheck-
            // Extension mit `rkh_`-Token) werden aber erst in der Autorisierung authentifiziert, sodass
            // dort `HttpContext.User` noch anonym war und das Request-Log KEINE UserId trug. Diese
            // Enrichment-Lambda läuft erst NACH dem Endpoint (Completion), da ist `User` gesetzt →
            // z. B. die Extension-Nutzerzahl (distinct UserId über `/api/extension/*`) wird in Kibana
            // zählbar. Für JWT-Requests ist es idempotent (gleicher Wert wie via LogContext).
            options.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
            {
                if (httpCtx.User?.Identity?.IsAuthenticated != true) return;
                var uid = httpCtx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(uid)) diagCtx.Set("UserId", uid);
                if (!string.IsNullOrEmpty(httpCtx.User.Identity.Name))
                    diagCtx.Set("UserName", httpCtx.User.Identity.Name);
            };
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                var path = httpContext.Request.Path.Value ?? "";
                if (path.StartsWith("/health") || path.StartsWith("/swagger"))
                    return LogEventLevel.Debug;
                // Long-Poll der Engine-Provider (eigener Broker): 13 Provider = 78 Zeilen je Minute ohne Aussage.
                // Die abgeholten Auftraege protokolliert der Upload selbst (EngineBroker: Upload …).
                if (path.Equals("/api/external-engine/work", StringComparison.OrdinalIgnoreCase)
                    && httpContext.Response.StatusCode < 400 && ex == null)
                    return LogEventLevel.Debug;
                if (ex != null || httpContext.Response.StatusCode >= 500)
                    return LogEventLevel.Error;
                return LogEventLevel.Information;
            };
        });
}
