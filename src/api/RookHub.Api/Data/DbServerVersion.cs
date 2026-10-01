using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Data;

/// <summary>
/// EINE Server-Fassung fuer Laufzeit (Program.cs), Design-Time (<see cref="DesignTimeDbContextFactory"/>) und
/// alle Tests, die SQL erzeugen (Codereview 2026-09-29, A9-010). Vorher: Laufzeit per
/// <c>ServerVersion.AutoDetect</c> im AddDbContext-Lambda (je Scope eine eigene Verbindung AUSSERHALB von
/// EnableRetryOnFailure — bei einem MariaDB-Neustart warf schon das Aufloesen des DbContext), Design-Time und die
/// meisten Tests mit dem MySQL-Dialekt 11.0, einzelne Tests mit MariaDB 11.4. Die Integrationstests (auch
/// MigrationsTests) liefen so mit einem anderen Dialekt als Prod.
/// <para>Muss zum Image passen: <c>mariadb:11.8</c> in den compose-Dateien und -Vorlagen,
/// <c>mariadb:11</c> in CI und E2E (<c>DbServerVersionTests</c> prueft das). Wer die DB hebt, hebt die Fassung HIER.</para>
/// </summary>
public static class DbServerVersion
{
    public static readonly ServerVersion Current = new MariaDbServerVersion(new Version(11, 8));
}
