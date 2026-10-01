using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Codereview N11-005: die Wegwerf-Schemata der Integrationstests leakten still — 17 bis 19 verwaiste
/// <c>rh_it_*</c>-Schemata auf der DEV-MariaDB, weil (1) jeder Server angenommen wurde, (2) ein abgebrochener
/// Lauf sein Schema nie entfernt und (3) niemand nach Altlasten sah. Die Tests arbeiten mit EIGENEN Namen
/// (nicht <c>rh_it_</c>, nicht <c>rookhub</c>), damit sie den prozessweiten Schritt vor dem ersten Schema und
/// parallel laufende Testklassen nicht beruehren.
/// </summary>
public class MariaDbSchemaHygieneTests
{
    private static string Base => MySqlFactAttribute.ConnectionBase!;

    [MySqlFact]
    public async Task EnsureThrowawayServer_RefusesAServerThatCarriesAnAppSchema()
    {
        var app = $"rh_guard{Guid.NewGuid():N}"[..24];
        await ExecAsync($"CREATE DATABASE `{app}`");
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => MariaDbSchema.EnsureThrowawayServerAsync(Base, [app], allowShared: false));
            Assert.Contains(app, ex.Message);
            Assert.Contains(MariaDbSchema.AllowSharedEnvVar, ex.Message);

            // Bewusst abgeschaltet: kein Einwand.
            await MariaDbSchema.EnsureThrowawayServerAsync(Base, [app], allowShared: true);
            // Ein Server ohne diese Schemata (der Wegwerf-Container) ist in Ordnung.
            await MariaDbSchema.EnsureThrowawayServerAsync(Base, [$"{app}_gibtsnicht"], allowShared: false);
        }
        finally { await ExecAsync($"DROP DATABASE IF EXISTS `{app}`"); }
    }

    [MySqlFact]
    public async Task DropStaleSchemas_RemovesOnlyOldSchemasWithThePrefix_AndLeavesEmptyOnes()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var prefix = $"rh_sweep{tag}_";
        var withTable = $"{prefix}a";
        var empty = $"{prefix}b";                   // noch ohne Tabelle: so sieht ein frisch angelegtes eines parallelen Laufs aus
        var otherPrefix = $"rh_sweepx{tag}";         // andere Vorsilbe — darf nie fallen
        await ExecAsync($"CREATE DATABASE `{withTable}`");
        await ExecAsync($"CREATE TABLE `{withTable}`.t (id INT PRIMARY KEY)");
        await ExecAsync($"CREATE DATABASE `{empty}`");
        await ExecAsync($"CREATE DATABASE `{otherPrefix}`");
        await ExecAsync($"CREATE TABLE `{otherPrefix}`.t (id INT PRIMARY KEY)");
        try
        {
            // Frisch angelegt: unter der echten Grenze von sechs Stunden faellt nichts.
            Assert.Empty(await MariaDbSchema.DropStaleSchemasAsync(Base, prefix, MariaDbSchema.StaleAfter));
            Assert.True(await SchemaExistsAsync(withTable));

            // Grenze 0: genau das Schema mit Tabelle und passender Vorsilbe faellt.
            var dropped = await MariaDbSchema.DropStaleSchemasAsync(Base, prefix, TimeSpan.Zero);

            Assert.Equal([withTable], dropped);
            Assert.False(await SchemaExistsAsync(withTable));
            Assert.True(await SchemaExistsAsync(empty));
            Assert.True(await SchemaExistsAsync(otherPrefix));
        }
        finally
        {
            foreach (var s in new[] { withTable, empty, otherPrefix })
                await ExecAsync($"DROP DATABASE IF EXISTS `{s}`");
        }
    }

    private static async Task ExecAsync(string sql)
    {
        await using var conn = new MySqlConnector.MySqlConnection(Base);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> SchemaExistsAsync(string name)
    {
        await using var conn = new MySqlConnector.MySqlConnection(Base);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @n";
        cmd.Parameters.AddWithValue("@n", name);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }
}
