using Microsoft.Extensions.Configuration;
using RookHub.Api.Controllers.TournamentDirectory;
using RookHub.Api.Data;
using RookHub.Api.Services.Social;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>Baut den Admin-Controller des Turnierverzeichnisses fuer Tests, die ohne Netz auskommen.</summary>
internal static class AdminTournamentDirectoryTestKit
{
    /// <summary>
    /// Die Pflege-Endpunkte brauchen nur die Datenbank und den Geocoder; die Quellen-Ausloeser
    /// holen ihren Dienst je Aufruf (<c>[FromServices]</c>) und kommen hier nicht vor. Der
    /// HTTP-Client ist eine Attrappe, die nie angesprochen wird.
    /// </summary>
    internal static AdminTournamentDirectoryController Controller(AppDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Crawler:BaseUrl"] = "http://crawler" }).Build();
        var geocoding = new GeocodingService(db);
        return new AdminTournamentDirectoryController(
            new TournamentDirectoryService(db, new UnusedClientFactory(), geocoding,
                new NotificationService(db, new NoOpTaskQueue()),
                new TestLogger<TournamentDirectoryService>()),
            new TournamentDirectoryMaintenanceService(db, geocoding),
            new GazetteerImportService(new HttpClient(new UnusedHandler()), db,
                new TestLogger<GazetteerImportService>(), config));
    }

    /// <summary>Wird in diesen Tests nie benutzt — ein Aufruf ist ein Fehler, kein Zufall.</summary>
    private sealed class UnusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Diese Tests erwarten keinen HTTP-Aufruf.");
    }

    private sealed class UnusedClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new UnusedHandler());
    }

}
