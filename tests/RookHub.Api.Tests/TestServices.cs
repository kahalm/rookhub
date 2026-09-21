using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Baut die Dienste für Tests zusammen.
///
/// <para>Frueher trugen <see cref="CourseService"/>, <see cref="RepertoireService"/> und
/// <see cref="ProfileService"/> OPTIONALE Konstruktor-Parameter, die sich ihre Abhaengigkeit
/// sonst selbst bauten (<c>notifications ?? new NotificationService(db)</c>) — ausdruecklich
/// „damit bestehende Test-Konstruktionen ohne Aenderung kompilieren". Das hatte drei Haken:
/// ein Test konnte keinen Doppelgaenger einschleusen (er bekam immer den echten Dienst, der in
/// die Test-Datenbank schrieb), die Verdrahtung lag doppelt vor (einmal im DI-Container, einmal
/// im Konstruktor) und die Abhaengigkeit war von aussen unsichtbar.</para>
///
/// <para>Jetzt sind die Parameter verpflichtend, und diese Fabrik haelt die Testzeilen kurz.
/// Nebeneffekt: die laengste Konstruktion im Testprojekt war 527 Zeichen lang.</para>
/// </summary>
internal static class TestServices
{
    public static IMemoryCache Cache() => new MemoryCache(new MemoryCacheOptions());

    public static NotificationService Notifications(AppDbContext db) => new(db);

    public static FriendService Friends(AppDbContext db, NotificationService? notifications = null)
        => new(db, notifications ?? Notifications(db));

    public static RepertoireAnalyzeService Analyze(AppDbContext db, IMemoryCache? cache = null)
        => new(db, cache ?? Cache());

    /// <summary>
    /// <paramref name="analyze"/> nur setzen, wenn der Test DIESELBE Instanz auch selbst haelt —
    /// etwa um zu pruefen, dass ein Upload deren Cache invalidiert. Wird sie hier neu gebaut,
    /// prueft so ein Test versehentlich zwei getrennte Caches und ist wertlos.
    /// </summary>
    public static RepertoireService Repertoire(
        AppDbContext db, IMemoryCache? cache = null, RepertoirePositionLookupService? positionLookup = null,
        RepertoireAnalyzeService? analyze = null, IConfiguration? configuration = null)
    {
        var notifications = Notifications(db);
        return new RepertoireService(db, analyze ?? Analyze(db, cache), Friends(db, notifications), notifications, positionLookup, configuration);
    }

    /// <summary>Konfiguration mit gesetztem <c>Chessable:Enabled</c> — entscheidet, ob ein veralteter
    /// Eintrag noch holbar ist oder ein Showstopper (siehe <see cref="StaleContentRule"/>).</summary>
    public static IConfiguration ChessableSwitch(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Chessable:Enabled"] = enabled ? "true" : "false" })
            .Build();

    public static CourseService Course(
        AppDbContext db, ILogger<CourseService>? logger = null,
        BookAdminService? bookAdmin = null, IConfiguration? configuration = null)
    {
        var notifications = Notifications(db);
        return new CourseService(
            db,
            logger ?? NullLogger<CourseService>.Instance,
            new PgnImportService(db),
            bookAdmin ?? new BookAdminService(db),
            Friends(db, notifications),
            notifications,
            chessableProxy: null,
            configuration: configuration);
    }

    /// <summary>Kurs ⇄ Repertoire (beide Richtungen). Haengt bewusst an BEIDEN Diensten — im
    /// Container ist er der einzige Ort, an dem sie sich begegnen (RepertoireService darf
    /// CourseService nicht bekommen, sonst Zyklus).</summary>
    public static CourseRepertoireConversionService Conversion(
        AppDbContext db, CourseService? courses = null, RepertoireService? repertoire = null,
        BookAdminService? bookAdmin = null)
    {
        var admin = bookAdmin ?? new BookAdminService(db);
        return new CourseRepertoireConversionService(
            db, courses ?? Course(db, bookAdmin: admin), repertoire ?? Repertoire(db), admin);
    }

    public static ProfileService Profile(
        AppDbContext db, IBackgroundTaskQueue queue, ILogger<ProfileService>? logger = null)
        => new(db, queue, logger ?? NullLogger<ProfileService>.Instance, new BookAdminService(db));
}
