using Microsoft.Extensions.Logging;

namespace RookHub.Api.Tests;

/// <summary>Minimaler ILogger, der Log-Events samt strukturierter Properties mitschreibt — fuer Tests,
/// die pruefen, dass ein bestimmter messageTemplate-Event (z.B. fuer Kibana) emittiert wird.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(string Message, IReadOnlyDictionary<string, object?> State);

    public List<Entry> Events { get; } = new();

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var dict = new Dictionary<string, object?>();
        if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            foreach (var kv in kvps)
                dict[kv.Key] = kv.Value;
        Events.Add(new Entry(formatter(state, exception), dict));
    }
}
