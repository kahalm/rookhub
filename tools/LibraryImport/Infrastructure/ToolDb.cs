using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Tools.LibraryImport.Infrastructure;

/// <summary>
/// Oeffnet je Aufruf einen frischen <see cref="AppDbContext"/> gegen die MariaDB aus
/// <c>ConnectionStrings__DefaultConnection</c> — ohne Change-Tracker-Automatik und ohne API-Instanz
/// (eine zweite RookHub.Api gegen dieselbe Datenbank stritte sich mit dem Auftrags-Worker um die Engines).
/// </summary>
public sealed class ToolDb(string connectionString)
{
    public string ConnectionString => connectionString;

    /// <summary>Neuer Kontext; <paramref name="commandTimeout"/> ersetzt den 30-s-Standard fuer lange Abfragen.</summary>
    public AppDbContext New(TimeSpan? commandTimeout = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 0, 0)),
                o => { if (commandTimeout is { } t) o.CommandTimeout((int)t.TotalSeconds); })
            .EnableSensitiveDataLogging(false)
            .Options;
        var db = new AppDbContext(options);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        return db;
    }
}
