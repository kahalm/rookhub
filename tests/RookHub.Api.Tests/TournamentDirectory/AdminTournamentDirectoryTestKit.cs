using Microsoft.Extensions.Configuration;
using RookHub.Api.Controllers.TournamentDirectory;
using RookHub.Api.Data;
using RookHub.Api.Services.Social;
using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>Baut den Admin-Controller des Turnierverzeichnisses fuer Tests, die ohne Netz auskommen.</summary>
internal static class AdminTournamentDirectoryTestKit
{
    /// <summary>
    /// Nur die Datenbank und der Geocoder werden hier gebraucht — die uebrigen Abhaengigkeiten des
    /// Controllers gehoeren zu anderen Endpunkten und bekommen deshalb Attrappen, die nie
    /// angesprochen werden.
    /// </summary>
    internal static AdminTournamentDirectoryController Controller(AppDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Crawler:BaseUrl"] = "http://crawler" }).Build();
        var geocoding = new GeocodingService(db);
        var factory = new UnusedClientFactory();
        return new AdminTournamentDirectoryController(
            db,
            new TournamentDirectoryService(db, factory, geocoding,
                new NotificationService(db, new NoOpTaskQueue()),
                new TestLogger<TournamentDirectoryService>()),
            new GazetteerImportService(new HttpClient(new UnusedHandler()), db,
                new TestLogger<GazetteerImportService>(), config),
            geocoding,
            new VenueDisambiguationService(db, factory,
                new TestLogger<VenueDisambiguationService>()),
            new TournamentRoundPlanService(db, factory,
                new TestLogger<TournamentRoundPlanService>()),
            new FideDirectorySweepService(db, factory, geocoding,
                new TestLogger<FideDirectorySweepService>()),
            new FideEventDetailService(db, factory, geocoding,
                new TestLogger<FideEventDetailService>()),
            new TournamentCalendarSweepService(db, factory,
                new TestLogger<TournamentCalendarSweepService>()),
            new FsiDirectorySweepService(db, factory, geocoding,
                new TestLogger<FsiDirectorySweepService>()),
            new SzsDirectorySweepService(db, factory, geocoding,
                new TestLogger<SzsDirectorySweepService>()),
            new ChessSkDirectorySweepService(db, factory, geocoding,
                new TestLogger<ChessSkDirectorySweepService>()),
            new ChessHuDirectorySweepService(db, factory, geocoding,
                new TestLogger<ChessHuDirectorySweepService>()),
            new ChessCzDirectorySweepService(db, factory, geocoding,
                new TestLogger<ChessCzDirectorySweepService>()),
            new ChessArbiterDirectorySweepService(db, factory, geocoding, config,
                new TestLogger<ChessArbiterDirectorySweepService>()),
            new SchachbundDirectorySweepService(db, factory, geocoding,
                new TestLogger<SchachbundDirectorySweepService>()),
            new EcfDirectorySweepService(db, factory, geocoding,
                new TestLogger<EcfDirectorySweepService>()),
            new IcuDirectorySweepService(db, factory, geocoding, config,
                new TestLogger<IcuDirectorySweepService>()),
            new FfeDirectorySweepService(db, factory, geocoding, config,
                new TestLogger<FfeDirectorySweepService>()),
            new SjakkDirectorySweepService(db, factory, geocoding, config,
                new TestLogger<SjakkDirectorySweepService>()),
            new ChessScotlandDirectorySweepService(db, factory, geocoding, config,
                new TestLogger<ChessScotlandDirectorySweepService>()),
            new FrsahDirectorySweepService(db, factory, geocoding,
                new TestLogger<FrsahDirectorySweepService>()),
            new WcuDirectorySweepService(db, factory, geocoding,
                new TestLogger<WcuDirectorySweepService>()),
            new CfcDirectorySweepService(db, factory, geocoding,
                new TestLogger<CfcDirectorySweepService>()),
            new KnsbDirectorySweepService(db, factory,
                new TestLogger<KnsbDirectorySweepService>()));
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
