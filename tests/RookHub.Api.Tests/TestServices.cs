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
        RepertoireAnalyzeService? analyze = null)
    {
        var notifications = Notifications(db);
        return new RepertoireService(db, analyze ?? Analyze(db, cache), Friends(db, notifications), notifications, positionLookup);
    }

    public static CourseService Course(
        AppDbContext db, ILogger<CourseService>? logger = null,
        BookAdminService? bookAdmin = null, RepertoireService? repertoire = null)
    {
        var notifications = Notifications(db);
        return new CourseService(
            db,
            logger ?? NullLogger<CourseService>.Instance,
            new PgnImportService(db),
            bookAdmin ?? new BookAdminService(db),
            repertoire ?? Repertoire(db),
            Friends(db, notifications),
            notifications);
    }

    public static ProfileService Profile(
        AppDbContext db, IBackgroundTaskQueue queue, ILogger<ProfileService>? logger = null)
        => new(db, queue, logger ?? NullLogger<ProfileService>.Instance, new BookAdminService(db));
}
