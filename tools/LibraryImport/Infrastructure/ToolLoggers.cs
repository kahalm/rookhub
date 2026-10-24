namespace RookHub.Tools.LibraryImport.Infrastructure;

/// <summary>Logger fuer die Dienste der API, die ein Befehl direkt anlegt.</summary>
public static class ToolLoggers
{
    /// <summary>
    /// Nur Warnungen und Fehler auf die Konsole: ein gescheiterter Aufruf (Modell weg, abgeschnitten, zu kurz) gibt
    /// sonst nur eine 0 zurueck — genau wie „nichts zu tun", und ein Tage-Lauf liefe blind.
    /// </summary>
    public static Serilog.Extensions.Logging.SerilogLoggerFactory WarningsToConsole() => new(
        Serilog.ConsoleLoggerConfigurationExtensions.Console(
                new Serilog.LoggerConfiguration().MinimumLevel.Warning().WriteTo,
                outputTemplate: "{Timestamp:HH:mm:ss} {Level:u3} {Message:lj}{NewLine}{Exception}")
            .CreateLogger(), dispose: true);
}
