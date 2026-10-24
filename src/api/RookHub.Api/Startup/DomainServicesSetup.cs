namespace RookHub.Api.Startup;

/// <summary>
/// Registrierungen der Fachdienste, je Fachbereich eine Methode (Dateien <c>DomainServices.&lt;Bereich&gt;.cs</c>,
/// Zerlegung Z2). Hosted Services stehen NICHT hier, sondern gesammelt in <see cref="BackgroundServicesSetup"/>;
/// HttpClients in <see cref="HttpClientsSetup"/>. Ein neuer Dienst gehört in die Methode seines Bereichs
/// (<c>Services/&lt;Bereich&gt;/</c>); die Reihenfolge zwischen den Bereichen ist ohne Bedeutung, solange kein Diensttyp
/// zweimal registriert wird.
/// </summary>
public static partial class DomainServicesSetup
{
    public static IServiceCollection AddRookHubDomainServices(this IServiceCollection services, IConfiguration configuration)
    {
        AddInfrastructureServices(services, configuration);
        AddAccountServices(services, configuration);
        AddSocialServices(services, configuration);
        AddTournamentServices(services, configuration);
        AddTournamentDirectoryServices(services, configuration);
        AddPuzzleServices(services, configuration);
        AddCourseServices(services, configuration);
        AddKidsServices(services, configuration);
        AddRepertoireServices(services, configuration);
        AddChessableServices(services, configuration);
        AddEbookServices(services, configuration);
        AddLeagueServices(services, configuration);
        AddClubServices(services, configuration);
        AddPrepServices(services, configuration);
        AddEngineServices(services, configuration);
        AddEngineBrokerServices(services, configuration);
        AddGameServices(services, configuration);
        AddLibraryServices(services, configuration);
        AddScoresheetServices(services, configuration);
        return services;
    }
}
