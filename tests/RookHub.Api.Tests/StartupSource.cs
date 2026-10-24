using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RookHub.Api.Startup;

namespace RookHub.Api.Tests;

/// <summary>
/// Quelltext der Startkonfiguration für Verdrahtungs-Tests. Program.cs hält nur noch die Reihenfolge der Aufrufe, die
/// Registrierungen je Belang stehen unter <c>src/api/RookHub.Api/Startup/</c> (Zerlegung Z2). Program.cs startet im Test
/// nicht (Migrate braucht MariaDB) — deshalb prüfen diese Tests den Quelltext.
/// </summary>
internal static class StartupSource
{
    /// <summary>Ordner <c>src/api/RookHub.Api</c>.</summary>
    public static string ApiDir([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api");
            if (File.Exists(Path.Combine(candidate, "Program.cs"))) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("src/api/RookHub.Api/Program.cs nicht gefunden");
        return "";
    }

    /// <summary>Nur Program.cs — für Prüfungen der Pipeline-REIHENFOLGE (app.Use…/Map…).</summary>
    public static string Program() => Read("Program.cs");

    /// <summary>Eine Datei relativ zu <c>src/api/RookHub.Api</c>, z. B. <c>Startup/RateLimitingSetup.cs</c>.</summary>
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(ApiDir(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Program.cs und alle Dateien unter <c>Startup/</c> hintereinander — für „ist registriert"-Prüfungen, die
    /// nicht daran hängen, in welcher Datei die Registrierung steht.</summary>
    public static string All()
    {
        var api = ApiDir();
        var startup = Path.Combine(api, "Startup");
        var files = new List<string> { Path.Combine(api, "Program.cs") };
        if (Directory.Exists(startup))
            files.AddRange(Directory.GetFiles(startup, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        return string.Join("\n", files.Select(File.ReadAllText));
    }

    /// <summary>Die Hosted Services, die <see cref="BackgroundServicesSetup.AddRookHubBackgroundServices"/> mit diesem
    /// Chessable-Schalter registriert (Typen; per Fabrik registrierte Singletons wie AnalysisJobWorker fehlen).</summary>
    public static List<Type> HostedServices(bool chessableEnabled) =>
        new ServiceCollection().AddRookHubBackgroundServices(chessableEnabled)
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .OfType<Type>()
            .ToList();
}
