using Microsoft.Extensions.Logging;

namespace RookHub.Api.Tests;

/// <summary>
/// Einfacher ILogger, der die formatierten Log-Meldungen sammelt — für Assertions auf
/// strukturierte Logs (z. B. die Puzzle-Start-/Lösungszeit-Logs).
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();
    public List<LogLevel> Levels { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        Levels.Add(logLevel);
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
