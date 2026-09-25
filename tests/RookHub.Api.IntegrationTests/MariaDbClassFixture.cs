using System.Text;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Schema, Migrationen und — wo gebraucht — die hochgefahrene Anwendung: EINMAL je TESTKLASSE
/// statt einmal je Testmethode.
///
/// <para><b>Warum es das gibt.</b> xUnit legt je Testmethode eine neue Instanz der Testklasse an.
/// Stand der Aufbau (frisches Schema + <c>MigrateAsync</c> + <c>new ApiFactory</c>) direkt in
/// deren <c>InitializeAsync</c>, lief er deshalb je TEST. Gemessen am CI-Lauf vom 2026-09-20:
/// 22 Tests, davon 21 vollstaendige Durchlaeufe aller 139 Migrationen und 13 komplette
/// Anwendungsstarts — und genau vier Tests, die wirklich Zeit brauchen (die beiden
/// Migrations-Tests mit 9 s und 20 s, und die beiden Wire-Format-Tests). Die uebrigen 18 melden
/// 14 ms bis 945 ms; die Zeit dazwischen war ausschliesslich Aufbau. Der Testlauf brauchte
/// 1:20, der ganze Job 3:04.</para>
///
/// <para><b>Die Isolation bleibt.</b> Ein gemeinsames Schema, in dem die Zeilen des vorigen Tests
/// stehen, waere die Sorte Fehler, die lokal gruen ist und in der CI rot (Reihenfolge und
/// Parallelitaet entscheiden dann mit). Deshalb raeumt <see cref="ResetAsync"/> vor JEDEM Test
/// jede Tabelle leer — ein <c>DELETE</c> ueber gut hundert leere Tabellen kostet Millisekunden,
/// 139 Migrationen kosten Sekunden. Was NICHT zurueckgesetzt wird, sind die
/// AUTO_INCREMENT-Zaehler: Ids laufen ueber die Tests hinweg weiter, und ein Test, der sich auf
/// „die erste Id ist 1" verliesse, faellt damit sofort auf, statt es zufaellig zu ueberleben.</para>
///
/// <para><b>Ohne MariaDB passiert hier gar nichts.</b> Die Tests tragen <see cref="MySqlFactAttribute"/>
/// und ueberspringen sich ohne <c>ROOKHUB_TEST_MYSQL</c>; die Fixture darf in dem Fall nicht
/// werfen, sonst waere `dotnet test` ohne Docker rot statt gruen (bisher zugesichert).</para>
/// </summary>
public abstract class MariaDbClassFixture : IAsyncLifetime
{
    private readonly string _prefix;
    private readonly bool _withApp;
    private string[] _tables = [];

    protected MariaDbClassFixture(string prefix, bool withApp)
    {
        _prefix = prefix;
        _withApp = withApp;
    }

    /// <summary>Nur gueltig, wenn <c>ROOKHUB_TEST_MYSQL</c> gesetzt ist — sonst laeuft kein Test.</summary>
    public MariaDbSchema Schema { get; private set; } = null!;

    /// <summary>Die hochgefahrene Anwendung; nur bei <c>withApp</c>.</summary>
    public ApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(MySqlFactAttribute.ConnectionBase)) return;

        Schema = await MariaDbSchema.CreateAsync(_prefix);
        await using (var db = Schema.NewContext()) await db.Database.MigrateAsync();
        _tables = await LoadTableNamesAsync();
        if (_withApp) Factory = CreateFactory(Schema.ConnectionString);
    }

    /// <summary>Wie die Anwendung hochfährt — eine Unterklasse kann Einstellungen oder echten Kestrel wählen.</summary>
    protected virtual ApiFactory CreateFactory(string connectionString) => new(connectionString);

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (Schema is not null) await Schema.DisposeAsync();
    }

    /// <summary>
    /// Leert jede Tabelle — der Ersatz fuer „frisches Schema je Test". Fremdschluessel bleiben
    /// dabei aus: sonst muesste die Loeschreihenfolge dem Abhaengigkeitsgraphen folgen, und der
    /// aendert sich mit jeder neuen Entitaet. Der Schalter gilt nur fuer DIESE Verbindung und
    /// wird am Ende wieder gesetzt.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_tables.Length == 0) return;

        var sql = new StringBuilder("SET FOREIGN_KEY_CHECKS = 0;");
        foreach (var t in _tables) sql.Append($"DELETE FROM `{t}`;");
        sql.Append("SET FOREIGN_KEY_CHECKS = 1;");

        await using var conn = new MySqlConnector.MySqlConnection(Schema.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Die Tabellen kommen aus dem Schema selbst statt aus einer gepflegten Liste — eine Liste
    /// im Quelltext waere beim naechsten <c>DbSet</c> still unvollstaendig, und der uebrige Satz
    /// Zeilen faende sich erst als unerklaerlich roter Nachbar-Test wieder.
    /// </summary>
    private async Task<string[]> LoadTableNamesAsync()
    {
        var names = new List<string>();
        await using var conn = new MySqlConnector.MySqlConnection(Schema.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TABLE_NAME FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
              AND TABLE_NAME <> '__EFMigrationsHistory'
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        Assert.NotEmpty(names);
        return [.. names];
    }
}

// Je Testklasse eine eigene Fixture: `IClassFixture<T>` unterscheidet nach TYP, und zwei Klassen
// sollen sich weder Schema noch Anwendung teilen. Die beiden mit Anwendung liegen zusaetzlich in
// ApiFactoryCollection und laufen deshalb nacheinander — zwei WebApplicationFactory gleichzeitig
// vertragen sich nicht (siehe dort).
public sealed class CourseAccessFixture() : MariaDbClassFixture("acc", withApp: false);
public sealed class ImportStateFixture() : MariaDbClassFixture("wire", withApp: false);
public sealed class CourseStatsFixture() : MariaDbClassFixture("stats", withApp: true);
public sealed class QueryTranslationFixture() : MariaDbClassFixture("q", withApp: true);
public sealed class BookSourceSplitFixture() : MariaDbClassFixture("bsrc", withApp: true);
public sealed class RepertoireReprocessFixture() : MariaDbClassFixture("rrep", withApp: false);

/// <summary>Eigener Engine-Broker: auf ECHTEM Kestrel (die Fallen des Uploads — Mindest-Datenrate, chunked,
/// früher Abschluss — gibt es unter TestServer nicht) und mit kurzen Fristen.</summary>
public sealed class EngineBrokerFixture() : MariaDbClassFixture("brk", withApp: true)
{
    protected override ApiFactory CreateFactory(string connectionString)
    {
        var factory = new ApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Engine:LocalBroker:AcquireWaitSeconds"] = "1",
            ["Engine:LocalBroker:ProviderTimeoutSeconds"] = "5",
            ["AnalysisJobs:TickSeconds"] = "1",
            ["AnalysisJobs:IdleGraceSeconds"] = "0",
        });
        factory.UseKestrel(0);
        factory.StartServer();
        return factory;
    }
}
public sealed class CommentSearchFixture() : MariaDbClassFixture("vec", withApp: false);
