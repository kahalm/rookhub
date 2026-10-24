using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.Accounts;

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

    /// <summary>Auto-Migration beim Start + Seeds (Admin, RBAC-Rollen) + Start-Heilungen der Liga-Daten.</summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        await AdminSeeder.SeedAsync(db, app.Configuration);
        await RoleSeeder.SeedAsync(db);   // RBAC-Grundgerüst (System-Rollen + IsAdmin→admin-Rolle spiegeln)
        // Ligamanager-Ligen ohne Tnr-Versatz (Altbestand vor dem Versatz; erwartet: keine) nur melden, nicht umschreiben.
        await RookHub.Api.Services.League.LigamanagerSource.WarnLegacyTnrsAsync(db,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RookHub.Api.Startup"));
        // Feste Ligapaarungen der Vereinspartien heilen (0.716.1): Schlüssel nachtragen, tote Ids über den Schlüssel bzw. die
        // Paarungssuche neu finden oder leeren. Idempotent, wirft nie.
        await RookHub.Api.Services.League.LeagueGameLinks.HealOnStartupAsync(db,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RookHub.Api.Startup"));
    }
}
