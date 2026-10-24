using Elastic.Serilog.Sinks;
using RookHub.Api.Logging;
using Serilog;

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
}
