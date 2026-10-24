using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace RookHub.Api.Startup;

/// <summary>Data Protection mit Schlüsseln auf dem gemounteten Volume (Fallback: In-Memory, mit Warnung beim Start).</summary>
public static class DataProtectionSetup
{
    public static IServiceCollection AddRookHubDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        // Data Protection — Keys auf gemountetes Volume persistieren, damit sie Neustarts
        // ueberleben (sonst ephemere In-Memory-Keys + "No XML encryptor"-Warnings bei jedem Boot).
        // Pfad konfigurierbar (DataProtection:KeyPath, Default /keys = Prod-Volume). SetApplicationName
        // haelt die Purpose-Strings stabil, auch wenn sich der Containername aendert.
        var dataProtection = services.AddDataProtection().SetApplicationName("RookHub");
        var keyPath = configuration["DataProtection:KeyPath"] ?? "/keys";
        try
        {
            Directory.CreateDirectory(keyPath);
            // FALLE: `CreateDirectory` wirft bei einem BESTEHENDEN Verzeichnis nicht — egal, welche
            // Rechte es hat — und `PersistKeysToFileSystem` prüft beim Registrieren gar nichts. Bei
            // einem Host-Bind-Mount (`- /data/keys:/keys`) gehört das Verzeichnis root, `appuser` darf
            // nicht schreiben (die `chown`-Rechte des Images wandern nur in ein FRISCHES benanntes
            // Volume), und der Fallback unten lief nie an: der Start meldete Erfolg, und der erste
            // „Passwort vergessen"-Request scheiterte Wochen später mit 500 statt einer Mail.
            // Deshalb hier EINMAL wirklich schreiben.
            var probe = Path.Combine(keyPath, ".write-probe");
            File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
            File.Delete(probe);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        }
        catch (Exception ex)
        {
            // Pfad nicht beschreibbar (z. B. lokale Dev ohne gemountetes /keys, oder ein root-eigener
            // Bind-Mount) → In-Memory-Keys (ephemer; ueberleben den Prozess nicht). Kein Startup-Crash,
            // aber die Warnung steht ab jetzt BEIM START im Log, nicht erst als 500 im Betrieb.
            Log.Warning(ex, "DataProtection: key path {KeyPath} not writable — falling back to in-memory keys", keyPath);
        }
        return services;
    }
}
