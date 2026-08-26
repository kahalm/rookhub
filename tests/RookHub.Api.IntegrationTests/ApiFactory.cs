using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Faehrt die ECHTE Anwendung gegen ein Wegwerf-Schema hoch — mit demselben DI-Container, denselben
/// Service-Registrierungen und demselben EF-Provider wie in Produktion. Nur so pruefen die Tests
/// die Abfragen, die der Code wirklich absetzt, statt einer nachgebauten Kopie davon, die
/// auseinanderlaufen kann.
///
/// Hintergrundarbeiter werden entfernt: sie wuerden waehrend der Tests losrechnen (Importe
/// fortsetzen, Turniere pollen, Heartbeats senden) und haben mit der Abfrage-Uebersetzung nichts zu tun.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        // Einzige Pflichtangabe beim Start; Inhalt egal, nur mindestens 32 Byte.
        builder.UseSetting("Jwt:Key", new string('k', 48));
        // Ebenfalls Pflicht: der Dienst wirft beim Start lieber laut, als den fremden
        // Chessable-Bearer mit einem oeffentlich bekannten Fixwert scheinzuverschluesseln.
        builder.UseSetting("Encryption:Key", "integrationstest-schluessel-egal-welcher");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}
