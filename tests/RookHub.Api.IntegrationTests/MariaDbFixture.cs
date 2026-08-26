using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Legt je Test ein eigenes, frisches Schema an und raeumt es hinterher weg. Bewusst nicht
/// EnsureCreated: geprueft werden soll genau der Weg, den auch der Deploy geht (Migrationen).
/// </summary>
public sealed class MariaDbSchema : IAsyncDisposable
{
    private readonly string _baseConn;
    public string SchemaName { get; }
    public string ConnectionString { get; }

    private MariaDbSchema(string baseConn, string schema)
    {
        _baseConn = baseConn;
        SchemaName = schema;
        ConnectionString = $"{baseConn.TrimEnd(';')};database={schema}";
    }

    public static async Task<MariaDbSchema> CreateAsync(string suffix)
    {
        var baseConn = MySqlFactAttribute.ConnectionBase
            ?? throw new InvalidOperationException("ROOKHUB_TEST_MYSQL fehlt");
        // Kurz + eindeutig: MySQL-Bezeichner duerfen hoechstens 64 Zeichen haben.
        var schema = $"rh_it_{suffix}_{Guid.NewGuid():N}"[..Math.Min(60, 6 + suffix.Length + 33)];
        await using (var admin = new MySqlConnector.MySqlConnection(baseConn))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci";
            await cmd.ExecuteNonQueryAsync();
        }
        return new MariaDbSchema(baseConn, schema);
    }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, new MySqlServerVersion(new Version(11, 0, 0)))
            .Options;
        return new AppDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var admin = new MySqlConnector.MySqlConnection(_baseConn);
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS `{SchemaName}`";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* Aufraeumen ist best-effort; das Schema traegt einen eindeutigen Namen */ }
    }
}
