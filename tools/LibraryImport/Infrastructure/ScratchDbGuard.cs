using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Tools.LibraryImport.Infrastructure;

/// <summary>
/// Sperre für <c>migrate</c> (R3-013): der Befehl ist nur für eine WEGWERF-MariaDB gedacht, dasselbe Werkzeug läuft aber
/// regelmäßig mit der Prod-Verbindung (pat-cap, translate, league-import). Ein <c>migrate</c> im falschen Terminal hätte der
/// Prod-DB Migrationen eingespielt, die das laufende API-Image nicht kennt. Wie bei den Integrationstests
/// (<c>ROOKHUB_TEST_MYSQL</c> lehnt Server mit den echten Schemata ab) verweigert der Befehl darum ein Ziel, das
/// <list type="bullet">
/// <item>einen der echten Schemanamen trägt (<see cref="RealSchemas"/>) oder</item>
/// <item>schon Nutzer hat (<c>AppUsers</c> nicht leer)</item>
/// </list>
/// — außer mit <see cref="OverrideFlag"/>.
/// </summary>
public static class ScratchDbGuard
{
    public const string OverrideFlag = "--i-know-this-is-a-scratch-db";

    /// <summary>Namen der echten Datenbanken (Prod/Dev, Crawler, piratechess).</summary>
    public static readonly IReadOnlySet<string> RealSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "rookhub", "rookhub_dev", "piratechess", "chessresults" };

    /// <summary>Datenbankname aus der Verbindungszeichenfolge (<c>database=</c>/<c>initial catalog=</c>); <c>null</c> ohne.</summary>
    public static string? DatabaseName(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            if (key is "database" or "initial catalog") return kv[1].Trim();
        }
        return null;
    }

    /// <summary>Begründung der Ablehnung, oder <c>null</c> = darf migrieren.</summary>
    public static async Task<string?> RefusalAsync(AppDbContext db, string connectionString, bool overridden, CancellationToken ct = default)
    {
        if (overridden) return null;
        var name = DatabaseName(connectionString);
        if (name != null && RealSchemas.Contains(name))
            return $"migrate verweigert: „{name}“ ist eine echte Datenbank (nur für eine Wegwerf-MariaDB; sonst {OverrideFlag}).";
        bool hasUsers;
        try { hasUsers = await db.AppUsers.AnyAsync(ct); }
        catch (DbException) { hasUsers = false; }   // Tabelle fehlt: frisches Schema
        catch (InvalidOperationException) { hasUsers = false; }
        return hasUsers
            ? $"migrate verweigert: die Datenbank hat schon Nutzer (AppUsers) — nur für eine Wegwerf-MariaDB; sonst {OverrideFlag}."
            : null;
    }
}
