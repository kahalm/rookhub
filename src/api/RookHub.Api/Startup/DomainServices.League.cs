
namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>LeagueHub: Ligen, Vereine, Online-Konten, Liga-Quellen, ChessBase-Import.</summary>
    private static void AddLeagueServices(IServiceCollection services, IConfiguration configuration)
    {
        // LeagueHub (TMM-Aufstellungs-Prognosen): Modell einmal laden, Aktualisieren als Einzel-Lauf
        services.AddSingleton(_ => RookHub.Api.Services.League.LeagueModel.FromEmbedded());
        // Zerlegte Karten-Partien (Baum, Profil, letzte Partien, PGN) und Meldelisten geteilter Begegnungen: eigener Cache mit
        // Größengrenze in Partien (Codereview N4-003) — wie beim Repertoire (AddRepertoireServices), der allgemeine IMemoryCache hat keine.
        services.AddKeyedSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(RookHub.Api.Services.League.LeagueProfileStore.CacheServiceKey,
            (_, _) => new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions
            {
                SizeLimit = RookHub.Api.Services.League.LeagueProfileStore.CacheSizeLimit,
            }));
        services.AddScoped<RookHub.Api.Services.League.LeagueService>();
        services.AddScoped<RookHub.Api.Services.League.LeagueRefresh>();
        services.AddScoped<RookHub.Api.Services.League.ChessResultsLeagueImport>();
        services.AddScoped<RookHub.Api.Services.League.LeagueClubService>();
        // Vereine als Mandanten (2026-10-07): die EINE Stelle, die den Verein einer Anfrage bestimmt, und die Verwaltung der Vereine.
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueClubResolver(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(), sp.GetService<RookHub.Api.Services.Accounts.PermissionResolver>()));
        services.AddScoped<RookHub.Api.Services.League.LeagueClubAdminService>();
        services.AddScoped<RookHub.Api.Services.League.LeagueBatchUploadService>();
        // Deckel der Uploads über Teilen-Links (je Link und Tag, im Arbeitsspeicher) — MUSS ein Singleton sein, sonst hätte
        // jeder Request seinen eigenen Zähler (LeagueClubService fällt ohne auf einen eigenen je Dienst zurück).
        services.AddSingleton<RookHub.Api.Services.League.LeagueShareUploadQuota>();
        // Entwürfe der PGN-Importe (0.595.0): liegen online, bis alles importiert oder verworfen ist.
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueClubDraftService(sp.GetRequiredService<RookHub.Api.Data.AppDbContext>()));
        services.AddScoped<RookHub.Api.Services.League.LeagueMegaPlayers>();
        services.AddScoped<RookHub.Api.Services.League.LeagueFixtureGames>();   // Paarungen samt Partien (0.673.0)
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueGameMoves(   // Aufstellungen + erste Züge (2026-10-08)
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(), null,                     // + Partie je Brett (0.724.0)
            sp.GetRequiredService<RookHub.Api.Services.League.LeagueFixtureGames>()));
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueGameSources(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(), sp.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
        services.AddScoped<RookHub.Api.Services.League.LichessStudySource>();
        // Online-Konten der Ligaspieler + Abruf ihrer Partien (Lichess/chess.com, 0.605.0).
        services.AddSingleton<RookHub.Api.Services.League.LeagueOnlineSyncSignal>();
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueOnlineAccountService(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(), sp.GetRequiredService<RookHub.Api.Services.League.LeagueOnlineSyncSignal>()));
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueOnlineSync(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(RookHub.Api.Services.League.LeagueOnlineSync.ClientName),
            sp.GetRequiredService<ILogger<RookHub.Api.Services.League.LeagueOnlineSync>>(), sp.GetRequiredService<IConfiguration>()));
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueAccountFinder(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(RookHub.Api.Services.League.LeagueOnlineSync.ClientName),
            sp.GetRequiredService<ILogger<RookHub.Api.Services.League.LeagueAccountFinder>>(), sp.GetRequiredService<IConfiguration>()));
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueBroadcastImport(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(RookHub.Api.Services.League.LeagueOnlineSync.ClientName),
            sp.GetRequiredService<ILogger<RookHub.Api.Services.League.LeagueBroadcastImport>>(), sp.GetRequiredService<IConfiguration>()));
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueAccountChecks(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(RookHub.Api.Services.League.LeagueOnlineSync.ClientName),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), sp.GetRequiredService<IConfiguration>()));
        services.AddScoped(sp => new RookHub.Api.Services.League.LeagueTeamScout(
            sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(RookHub.Api.Services.League.LeagueOnlineSync.ClientName),
            sp.GetRequiredService<ILogger<RookHub.Api.Services.League.LeagueTeamScout>>(), sp.GetRequiredService<IConfiguration>()));
        // ChessBase-Datenbanken (.cbh/.2cbh) → PGN für die Vereins-Datenbank (0.598.0).
        services.AddSingleton<RookHub.Api.Services.ChessBase.ChessBaseImportService>();
        services.AddSingleton<RookHub.Api.Services.League.LeagueUpdateService>();
        services.AddScoped<RookHub.Api.Services.League.LigamanagerSource>();
        services.AddScoped<RookHub.Api.Services.League.ZugspitzeSource>();
        // Meldungen aus dem Online-Bereich des Schachkreises Zugspitze/Bezirks Oberbayern (0.716.0): Kreis-Seite + Lichess-Ergebnisse.
        services.AddScoped<RookHub.Api.Services.League.ZugspitzeOnlineReports>();
        services.AddSingleton<RookHub.Api.Services.League.LeagueAnalysisQueue>();   // Liga-Partien für den Stapel (0.665.0)
    }

    /// <summary>ClubHub und Taktik-Ernte.</summary>
    private static void AddClubServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RookHub.Api.Services.Club.ClubGameCorrectionService>();   // Vereinspartie + Kopien korrigieren (0.660.0)
        // Taktik-Ernte (0.657.0): fertige Analysen → Aufgaben in Kursen
        services.AddScoped<RookHub.Api.Services.Tactics.TacticHarvestService>();
        // ClubHub (Kartei der Kinder und Jugendlichen): Regeln + Sichtbarkeit, Lernstand aus dem verknüpften Konto
        services.AddScoped<RookHub.Api.Services.Club.ClubService>();
        services.AddScoped<RookHub.Api.Services.Club.ClubProgressService>();
    }

    /// <summary>Spielervorbereitung und Trainingslinien.</summary>
    private static void AddPrepServices(IServiceCollection services, IConfiguration configuration)
    {
        // Spielervorbereitung (Prep): Partiebestand aus Megabase + Lumbra paketweise einspielen, suchen, Karte.
        services.AddScoped<RookHub.Api.Services.Prep.PrepImportService>();
        services.AddScoped<RookHub.Api.Services.Prep.PrepPlayerSearch>();
        // Online-Konto-Suche der Spielervorbereitung: eine Suche zur Zeit, Obergrenze je Verwalter und Stunde (Phase 4).
        services.AddSingleton<RookHub.Api.Services.Prep.PrepAccountSearchGate>();
        services.AddScoped<RookHub.Api.Services.Prep.PrepAccountSearch>();
        // Grenzen der Karte (jüngste N Partien, „alle“ höchstens M) einstellbar — siehe PrepCardService.
        services.AddScoped(sp => new RookHub.Api.Services.Prep.PrepCardService(sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), sp.GetRequiredService<RookHub.Api.Services.League.LeagueService>(),
            RookHub.Api.Services.Prep.PrepCardService.LimitFrom(configuration), RookHub.Api.Services.Prep.PrepCardService.MaxFrom(configuration)));
        // Trainingslinien gegen einen Gegner — ein Dienst für Spielervorbereitung und LeagueHub (0.701.0).
        // Schätzung der Lücken über den Lichess-Explorer (lokal, sonst online wie der Lochfinder).
        services.AddScoped<RookHub.Api.Services.Prep.ITrainingExplorer, RookHub.Api.Services.Prep.TrainingExplorer>();
        // Fortsetzung unvollständiger Schätzungen im Hintergrund (0.725.2): ein Durchlauf je Nutzer, eigener DI-Bereich.
        services.AddSingleton<RookHub.Api.Services.Prep.TrainingLinesContinuation>();
        services.AddScoped<RookHub.Api.Services.Prep.TrainingLinesService>();
    }
}
