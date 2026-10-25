using RookHub.Api.Services;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Social;
using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Startup;

/// <summary>
/// Alle HttpClients (benannte und typisierte) an einer Stelle (Zerlegung Z2). Die Clients sind voneinander unabhängig;
/// ungültige Basis-Adressen (<c>Chessable:ApiUrl</c>, <c>Crawler:BaseUrl</c>) brechen den Start hier ab.
/// </summary>
public static class HttpClientsSetup
{
    public static IServiceCollection AddRookHubHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        AddLeagueSourceClients(services, configuration);
        AddModelAndSiteClients(services);
        AddPlatformClients(services, configuration);
        AddCrawlerClients(services, configuration);
        AddLichessAndLookupClients(services, configuration);
        return services;
    }


    /// <summary>LeagueHub: Online-Konten, Lichess-Studien und die direkten Liga-Quellen (Ligamanager, Zugspitze).</summary>
    private static void AddLeagueSourceClients(IServiceCollection services, IConfiguration configuration)
    {
        // chess.com verlangt einen aussagekräftigen User-Agent (sonst 403); Lichess streamt 500 Partien in rund 25 s.
        services.AddHttpClient(RookHub.Api.Services.League.LeagueOnlineSync.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub-LeagueHub/1.0 (+https://rookhub.oberschmid.homes)");
        });
        // Öffentliche Lichess-Studien für den Import in die Vereins-Datenbank (nur die feste API, siehe LichessStudySource).
        services.AddHttpClient(RookHub.Api.Services.League.LichessStudySource.ClientName, client =>
        {
            client.BaseAddress = new Uri(configuration["Lichess:SiteUrl"] ?? "https://lichess.org");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub-LeagueHub/1.0 (+https://rookhub.oberschmid.homes)");
        });
        // Zweite Liga-Quelle: SBV-Ligamanager (Bayern, 2026-10-07) — direkt, höflich, ohne Crawler/VPN.
        services.AddHttpClient(RookHub.Api.Services.League.LigamanagerSource.ClientName, client =>
        {
            client.BaseAddress = new Uri(RookHub.Api.Services.League.LigamanagerSource.SiteUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub-LeagueHub/1.0 (+https://rookhub.oberschmid.homes)");
        });
        // Dritte Liga-Quelle: Schachkreis Zugspitze (Bayern, 2026-10-07) — ebenso direkt und höflich.
        services.AddHttpClient(RookHub.Api.Services.League.ZugspitzeSource.ClientName, client =>
        {
            client.BaseAddress = new Uri(RookHub.Api.Services.League.ZugspitzeSource.SiteUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub-LeagueHub/1.0 (+https://rookhub.oberschmid.homes)");
        });
    }

    /// <summary>IP-Land, Text-Modell (Spark/Claude), Embedding-Modell, Frontend für die Link-Vorschau.</summary>
    private static void AddModelAndSiteClients(IServiceCollection services)
    {
        services.AddHttpClient(nameof(IpCountryService));
        // Tipps + Übersetzung: eigene Hardware (TextLlm:BaseUrl, OpenAI-kompatibel — DGX Spark) vor Claude
        // (Anthropic:TextApiKey); ohne beides aus. Eigener langer Timeout: das Modell streamt minutenlang.
        services.AddHttpClient("text-llm", c => c.Timeout = TimeSpan.FromMinutes(30));
        // „Frag die Kommentare" (0.536.0): Embedding-Modell (Embedding:BaseUrl, OpenAI-kompatibel) + Vektorsuche.
        services.AddHttpClient("embedding", c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient("og-frontend");
    }

    /// <summary>GitHub-Actions, Spielzeit (Lichess/chess.com), SchachBot-Webhook, Chessable-Proxy (piratechess).</summary>
    private static void AddPlatformClients(IServiceCollection services, IConfiguration configuration)
    {
        // GitHub-Actions-Übersicht (Admin-CI-Seite). Token pro Request in GithubActionsService gesetzt.
        services.AddHttpClient<GithubActionsService>(client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub-CI/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });

        // Externe Spielzeit (Lichess/chess.com) → Kategorie „Spielen" im Trainingsziele-Tracker.
        // chess.com verlangt einen aussagekraeftigen User-Agent, sonst 403.
        services.AddHttpClient<PlayTimeService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub/1.0 (+https://rookhub; training-goals)");
        });

        // SchachBot-Webhook (Solver-Updates fuer Daily-Puzzle live an den Bot pushen).
        // URL + Secret optional → ohne Konfig stillschweigend deaktiviert.
        services.AddHttpClient<SchachBotWebhookService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // Chessable Proxy HttpClient — leitet User-Bearer an piratechess-API durch.
        // Default-URL passt zum Container-Namen im chessable-bridge-Docker-Netz.
        var chessableBaseUrl = configuration["Chessable:ApiUrl"] ?? "http://piratechess-api:8080";
        if (!Uri.TryCreate(chessableBaseUrl, UriKind.Absolute, out var chessableUri))
            throw new InvalidOperationException($"Invalid Chessable:ApiUrl: {chessableBaseUrl}");
        var chessableServiceKey = configuration["Chessable:ServiceKey"];
        services.AddHttpClient<ChessableProxyService>(client =>
        {
            client.BaseAddress = chessableUri;
            // /direct/test + /direct/courses sind schnell; der Kurs-Abruf läuft als Job (/direct/course/start
            // + Polling), der fetch-freie Parse (/direct/course/parse) eines großen Kurses kann aber
            // dauern → großzügiger Timeout (Import läuft im Hintergrund).
            client.Timeout = TimeSpan.FromMinutes(15);
            if (!string.IsNullOrEmpty(chessableServiceKey))
                client.DefaultRequestHeaders.Add("X-Service-Key", chessableServiceKey);
        });
    }

    /// <summary>Der Crawler: Live-Proxy, Verzeichnis-Sweep und LeagueHub je mit eigener Leine.</summary>
    private static void AddCrawlerClients(IServiceCollection services, IConfiguration configuration)
    {
        // Crawler Proxy HttpClient
        var crawlerBaseUrl = configuration["Crawler:BaseUrl"] ?? "http://host.docker.internal:8080";
        if (!Uri.TryCreate(crawlerBaseUrl, UriKind.Absolute, out var crawlerUri))
            throw new InvalidOperationException($"Invalid Crawler:BaseUrl: {crawlerBaseUrl}");
        var crawlerApiKey = configuration["Crawler:ApiKey"];
        services.AddHttpClient<CrawlerProxyService>(client =>
        {
            client.BaseAddress = crawlerUri;
            client.Timeout = TimeSpan.FromSeconds(30);
            if (!string.IsNullOrEmpty(crawlerApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", crawlerApiKey);
        });

        // Eigener Client fuer den Verzeichnis-Sweep: eine Turniersuche wartet legitim hinter dem
        // prozessweiten Rate-Limiter des Crawlers und ggf. einer VPN-Rotation (Tunnel-Neustart alle
        // 20 Anfragen). Mit den 30 s des geteilten Clients lief in Dev die neunte Foederation in
        // Folge zuverlaessig in den Timeout. Der Live-Pfad behaelt seine kurze Leine.
        //
        // Die Vorgabe und ihre Messung stehen bei TournamentDirectoryService.DefaultCrawlerTimeoutSeconds
        // — sie muss die LANGSAMSTE Zusatzquelle aushalten (England: 196 s, weil dessen robots.txt
        // zehn Sekunden zwischen den Seiten verlangt), nicht die schnellste.
        var directoryTimeout = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("TournamentDirectory:CrawlerTimeoutSeconds",
                TournamentDirectoryService.DefaultCrawlerTimeoutSeconds),
            30, 900));
        services.AddHttpClient(TournamentDirectoryService.CrawlerClientName, client =>
        {
            client.BaseAddress = crawlerUri;
            client.Timeout = directoryTimeout;
            if (!string.IsNullOrEmpty(crawlerApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", crawlerApiKey);
        });

        // LeagueHub: eine Liga = vier chess-results-Seiten hintereinander (Rate-Limiter + ggf. VPN-Rotation
        // im Crawler), die PGN-Suche eines starken Spielers liefert bis zu ~1 MB — daher eigener, langer Timeout.
        services.AddHttpClient(RookHub.Api.Services.League.LeagueRefresh.CrawlerClient, client =>
        {
            client.BaseAddress = crawlerUri;
            client.Timeout = TimeSpan.FromMinutes(5);
            if (!string.IsNullOrEmpty(crawlerApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", crawlerApiKey);
        });
    }

    /// <summary>Lichess (External-Engine, Endspiel-Datenbank, Explorer online/lokal), FIDE-Suche, GeoNames-Import.</summary>
    private static void AddLichessAndLookupClients(IServiceCollection services, IConfiguration configuration)
    {
        // Lichess External-Engine (Client-Modus): Engine-Liste via lichess.org, Analyse-Streams via
        // Broker (engine.lichess.ovh). Timeout bewusst unendlich — Analyse-Streams laufen lange und
        // enden über den Abbruch des Browsers (RequestAborted); die kurze Engine-Liste schützt sich
        // selbst mit einem eigenen 15-s-Timeout (LichessEngineService.ListEnginesAsync).
        services.AddHttpClient<LichessEngineService>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // Endspiel-Datenbank von Lichess (0.729.0): kurz warten — die Live-Analyse rechnet daneben ohnehin mit Stockfish.
        services.AddHttpClient(TablebaseService.ClientName, client =>
        {
            client.BaseAddress = new Uri(configuration["Lichess:TablebaseUrl"] ?? TablebaseService.DefaultBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub/1.0 (+https://rookhub.oberschmid.homes)");
        });

        services.AddHttpClient<LichessExplorerClient>(client =>
        {
            client.BaseAddress = new Uri(LichessExplorerClient.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub/1.0 (+https://rookhub.oberschmid.homes)");
        });
        // Lokaler Explorer (lila-openingexplorer im Stack): ohne Adresse bleibt BaseAddress leer, und die
        // Quelle „lokal" gibt es nicht (LocalExplorerClient.IsConfigured).
        services.AddHttpClient<LocalExplorerClient>(client =>
        {
            var localUrl = configuration[LocalExplorerClient.ConfigKey];
            if (!string.IsNullOrWhiteSpace(localUrl))
                client.BaseAddress = new Uri(localUrl.Trim().TrimEnd('/') + "/");
            // Großzügig: während eines Imports kompaktiert der Explorer auf der Platte (Spitzen um 11 s).
            // Die Runde selbst begrenzt RepertoireExplorerService (Budget + LocalLayerFloor).
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // FIDE search HttpClient
        services.AddHttpClient("FideSearch", client =>
        {
            client.BaseAddress = new Uri("https://api.chesstools.org");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Gazetteer-Import (GeoNames, CC BY 4.0). Grosszuegiges Timeout: cities15000 sind ~11 MB,
        // und der Import laeuft nur, wenn ein Admin ihn ausloest - nie im Anfrageweg eines Nutzers.
        services.AddHttpClient<GazetteerImportService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
    }
}
