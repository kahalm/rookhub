using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Startup;

/// <summary>DbContext gegen MariaDB (feste Server-Fassung, Retry-Strategie).</summary>
public static class DatabaseSetup
{
    public static IServiceCollection AddRookHubDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
            // Feste Server-Fassung statt AutoDetect: AutoDetect oeffnete je Scope eine eigene Verbindung ausserhalb der
            // Retry-Strategie unten und warf bei einem MariaDB-Neustart schon beim Aufloesen des DbContext (A9-010).
            options.UseMySql(connectionString, DbServerVersion.Current,
                // Transiente DB-Fehler (z. B. kurzer Verbindungsverlust beim MariaDB-Neustart/Recreate)
                // automatisch wiederholen, statt Background-Tasks/Requests hart fehlschlagen zu lassen.
                mySql => mySql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null))
            // Der Code fängt DbUpdateException an vielen Stellen bewusst ab, um idempotente Races auf
            // Unique-Indizes aufzulösen (z. B. paralleler Erstinsert von anonymem Endless-Progress,
            // CoursePuzzleResult, CourseInfoView, CourseProgress …). EF Core loggt den fehlgeschlagenen
            // SaveChanges-Versuch trotzdem auf Error → das trippt den Log-Watcher grundlos. Genuine,
            // NICHT abgefangene Fehler bleiben über das HTTP-500-Request-Log sichtbar; daher hier nur den
            // erwarteten SaveChanges-Fehl-Event auf Information herabstufen (kein Rauschen mehr).
            .ConfigureWarnings(w => w.Log(
                (Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.SaveChangesFailed, LogLevel.Information))));
        return services;
    }
}
