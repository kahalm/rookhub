using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Legt je Test ein eigenes, frisches Schema an und raeumt es hinterher weg. Bewusst nicht
/// EnsureCreated: geprueft werden soll genau der Weg, den auch der Deploy geht (Migrationen).
///
/// <para><b>Vor dem ersten Schema eines Testprozesses</b> (Codereview N11-005) zwei Schritte, beide
/// einmal je Prozess: (1) ein Server, der ein Schema der Anwendung fuehrt (<see cref="AppSchemas"/> —
/// also Dev oder Prod), wird abgelehnt, ausser <c>ROOKHUB_TEST_MYSQL_ALLOW_SHARED=1</c>; (2)
/// verwaiste <c>rh_it_*</c>-Schemata, deren aelteste Tabelle aelter als <see cref="StaleAfter"/> ist,
/// werden entfernt. Anlass: auf der DEV-MariaDB lagen 17 bis 19 solcher Schemata mit je ~95 Tabellen —
/// ein abgebrochener Lauf (Strg+C, Werkzeug-Timeout) erreicht <see cref="DisposeAsync"/> nie, und der
/// stumme catch meldete auch ein gescheitertes DROP nicht.</para>
/// </summary>
public sealed class MariaDbSchema : IAsyncDisposable
{
    /// <summary>Vorsilbe aller Wegwerf-Schemata dieser Suite — der Aufraeumer fasst NUR sie an.</summary>
    public const string Prefix = "rh_it_";

    /// <summary>Wer das Ablehnen eines geteilten Servers bewusst abschaltet: <c>=1</c>.</summary>
    public const string AllowSharedEnvVar = "ROOKHUB_TEST_MYSQL_ALLOW_SHARED";

    /// <summary>Schemata, an denen ein Server mit echten Daten zu erkennen ist (rookhub, piratechess, Crawler).</summary>
    internal static readonly string[] AppSchemas = ["rookhub", "piratechess", "chessresults"];

    /// <summary>Ab diesem Alter gilt ein <c>rh_it_*</c>-Schema als verwaist. Ein Lauf dauert Minuten; die
    /// Grenze schuetzt die Schemata eines parallel laufenden zweiten Testprozesses.</summary>
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    private static readonly object PrepareGate = new();
    private static Task? _prepare;

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
        // Einmal je Prozess; schlaegt die Pruefung fehl, scheitert JEDES weitere Anlegen mit derselben Meldung.
        Task prepare;
        lock (PrepareGate) prepare = _prepare ??= PrepareServerAsync(baseConn);
        await prepare;
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
            .UseMySql(ConnectionString, DbServerVersion.Current)
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
        catch (Exception ex)
        {
            // Best-effort — aber nicht mehr stumm: was hier liegen bleibt, raeumt der naechste Lauf nach
            // StaleAfter weg (PrepareServerAsync), und bis dahin soll man es wenigstens gesehen haben.
            Console.Error.WriteLine($"[MariaDbSchema] Schema {SchemaName} nicht entfernt: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task PrepareServerAsync(string baseConn)
    {
        var allowShared = Environment.GetEnvironmentVariable(AllowSharedEnvVar) == "1";
        await EnsureThrowawayServerAsync(baseConn, AppSchemas, allowShared);
        var dropped = await DropStaleSchemasAsync(baseConn, Prefix, StaleAfter);
        if (dropped.Count > 0)
            Console.Error.WriteLine($"[MariaDbSchema] {dropped.Count} verwaiste Testschemata entfernt: {string.Join(", ", dropped)}");
    }

    /// <summary>
    /// Lehnt einen Server ab, der eines der <paramref name="appSchemas"/> fuehrt — dort liegen echte Daten, und
    /// jeder Lauf hinterliess bisher Schemata mit gut hundert Tabellen und 139 Migrationen Last.
    /// </summary>
    internal static async Task EnsureThrowawayServerAsync(string baseConn, IReadOnlyCollection<string> appSchemas, bool allowShared)
    {
        if (allowShared) return;
        var found = new List<string>();
        await using (var conn = new MySqlConnector.MySqlConnection(baseConn))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                if (appSchemas.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add(name);
            }
        }
        if (found.Count > 0)
            throw new InvalidOperationException(
                $"{MySqlFactAttribute.EnvVar} zeigt auf einen Server mit dem Schema {string.Join(", ", found)} — "
                + "also auf einen mit echten Daten (Dev/Prod). Die Integrationstests gehoeren auf einen "
                + "Wegwerf-Container (docker run --rm -e MARIADB_ROOT_PASSWORD=test -p 127.0.0.1:3307:3306 mariadb:11). "
                + $"Wer es trotzdem will: {AllowSharedEnvVar}=1.");
    }

    /// <summary>
    /// Entfernt Schemata mit <paramref name="prefix"/>, deren AELTESTE Tabelle mindestens <paramref name="olderThan"/>
    /// alt ist (information_schema.TABLES.CREATE_TIME, gerechnet auf dem Server — dort stehen Zeit und Zeitzone
    /// fest). Ein Schema ohne Tabelle hat kein Alter und bleibt: genau so sieht ein gerade angelegtes eines
    /// parallelen Laufs vor seinen Migrationen aus. Liefert die entfernten Namen.
    /// </summary>
    internal static async Task<List<string>> DropStaleSchemasAsync(string baseConn, string prefix, TimeSpan olderThan)
    {
        var stale = new List<string>();
        await using var conn = new MySqlConnector.MySqlConnection(baseConn);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            // LIKE nur als Vorfilter (der Unterstrich ist dort ein Joker) — entschieden wird unten per StartsWith.
            cmd.CommandText = """
                SELECT TABLE_SCHEMA FROM information_schema.TABLES
                WHERE TABLE_SCHEMA LIKE CONCAT(@prefix, '%')
                GROUP BY TABLE_SCHEMA
                HAVING TIMESTAMPDIFF(SECOND, MIN(CREATE_TIME), NOW()) >= @seconds
                """;
            cmd.Parameters.AddWithValue("@prefix", prefix);
            cmd.Parameters.AddWithValue("@seconds", (long)olderThan.TotalSeconds);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                if (name.StartsWith(prefix, StringComparison.Ordinal) && SafeName.IsMatch(name)) stale.Add(name);
            }
        }
        foreach (var name in stale)
        {
            await using var drop = conn.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`";
            await drop.ExecuteNonQueryAsync();
        }
        return stale;
    }

    /// <summary>Nur Namen, die gefahrlos in Backticks stehen koennen.</summary>
    private static readonly Regex SafeName = new("^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
}
