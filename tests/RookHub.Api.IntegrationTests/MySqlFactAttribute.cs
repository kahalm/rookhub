using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Wie <see cref="FactAttribute"/>, ueberspringt den Test aber, wenn keine MariaDB erreichbar
/// konfiguriert ist. So bleibt `dotnet test` ohne Docker gruen: die Unit-Suite laeuft weiter,
/// nur die Integrationstests melden sich als uebersprungen statt als rot.
///
/// Verbindungszeichenfolge ueber die Umgebungsvariable ROOKHUB_TEST_MYSQL, z. B.
///   server=127.0.0.1;port=3307;user=root;password=test
/// OHNE database= — jeder Test legt sich sein eigenes Schema an und raeumt es wieder weg.
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public const string EnvVar = "ROOKHUB_TEST_MYSQL";

    public static string? ConnectionBase => Environment.GetEnvironmentVariable(EnvVar);

    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionBase))
            Skip = $"{EnvVar} nicht gesetzt - keine MariaDB fuer den Integrationstest.";
    }
}
