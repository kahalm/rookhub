using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Der Deploy migriert automatisch. Eine Migration, die auf MariaDB nicht durchlaeuft, faellt
/// deshalb erst beim Ausrollen auf — und dann steht die API. Die Unit-Suite kann das nicht
/// sehen: sie laeuft gegen EF InMemory, wo es ueberhaupt keine Migrationen gibt.
/// </summary>
public class MigrationsTests
{
    [MySqlFact]
    public async Task AlleMigrationen_LaufenAufEinemLeerenSchemaDurch()
    {
        await using var schema = await MariaDbSchema.CreateAsync("mig");
        await using var db = schema.NewContext();

        var geplant = db.Database.GetMigrations().ToList();
        Assert.NotEmpty(geplant);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(geplant.Count, (await db.Database.GetAppliedMigrationsAsync()).Count());

        // Codereview A9-009: „laeuft durch" heisst nicht „hat alles angelegt". Eine nach dem Rebase
        // neu erzeugte Migration, der nur EIN AddColumn fehlt (Rest nicht leer), ist im Inventar-Waechter
        // gruen, laeuft hier fehlerfrei durch und laesst den Snapshot-Modell-Vergleich unberuehrt — auf
        // Dev/Prod endet dann jede Abfrage in „Unknown column" (WorksheetSharing/WorksheetThemes, 8 Tage
        // HTTP 500). Deshalb: die migrierte Datenbank selbst gegen das Modell halten.
        var abweichungen = await SchemaGegenModellAsync(db);
        Assert.True(abweichungen.Count == 0,
            "Migriertes Schema passt nicht zum Modell — fehlt eine Operation in einer Migration "
            + "(leerer/halber Rumpf nach Rebase?):\n  " + string.Join("\n  ", abweichungen));

        // Gegenprobe auf demselben Schema (spart einen zweiten Migrationslauf): genau der Vorfall —
        // eine fehlende Spalte — und eine abweichende Nullbarkeit muessen gemeldet werden.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Worksheets` DROP COLUMN `SharedAt`");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Worksheets` MODIFY `Themes` varchar(300) NULL");
        Assert.Equal(
            ["Worksheets.SharedAt fehlt", "Worksheets.Themes: Datenbank NULL, Modell NOT NULL"],
            await SchemaGegenModellAsync(db));
    }

    /// <summary>
    /// Haelt jede Spalte jeder gemappten Tabelle des Modells gegen <c>information_schema.COLUMNS</c> des
    /// aktuellen Schemas: fehlt sie, oder stimmt die Nullbarkeit nicht? Spalten, die nur in der Datenbank
    /// stehen, zaehlen nicht („nie loeschen, nur ausblenden"). Namen ohne Gross-/Kleinschreibung, wie MariaDB
    /// Spaltennamen vergleicht. Liefert die Abweichungen sortiert, je Zeile „Tabelle.Spalte …".
    /// </summary>
    private static async Task<List<string>> SchemaGegenModellAsync(DbContext db)
    {
        var ist = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT TABLE_NAME, COLUMN_NAME, IS_NULLABLE FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ist[$"{reader.GetString(0)}.{reader.GetString(1)}"] = reader.GetString(2) == "YES";
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        var abweichungen = new List<string>();
        foreach (var tabelle in db.Model.GetRelationalModel().Tables)
        foreach (var spalte in tabelle.Columns)
        {
            var name = $"{tabelle.Name}.{spalte.Name}";
            if (!ist.TryGetValue(name, out var nullbar))
                abweichungen.Add($"{name} fehlt");
            // Berechnete Spalten (z. B. Friendships.PairLow/PairHigh) fuehrt MariaDB immer als nullbar —
            // NOT NULL ist dort fuer generierte Spalten nicht erlaubt; das ist kein Drift.
            else if (nullbar != spalte.IsNullable && spalte.ComputedColumnSql is null)
                abweichungen.Add($"{name}: Datenbank {(nullbar ? "NULL" : "NOT NULL")}, Modell {(spalte.IsNullable ? "NULL" : "NOT NULL")}");
        }
        abweichungen.Sort(StringComparer.Ordinal);
        return abweichungen;
    }

    /// <summary>
    /// Eine Migration, die auf einem LEEREN Schema durchlaeuft, kann auf dem BESTAND scheitern.
    ///
    /// <para>Genau so passiert bei <c>AddDirectoryPublicId</c>: die Spalte kommt mit dem
    /// Vorgabewert „" hinzu, danach ein UNIQUE-Index darauf. Auf einem leeren Schema kein
    /// Problem, auf 4930 bestehenden Zeilen tausende Dubletten — die Migration braeche beim
    /// Start der API ab, und zwar bei JEDEM Start. Der Nachtrag zwischen Spalte und Index ist
    /// das, was dieser Test prueft.</para>
    ///
    /// <para>Der Weg dahin: bis zur VORHERGEHENDEN Migration migrieren, Zeilen einfuegen, dann
    /// den Rest laufen lassen. Ein Test mit leerem Schema kann das nicht sehen.</para>
    /// </summary>
    [MySqlFact]
    public async Task Migrationen_LaufenAuchUeberBestehendeZeilen()
    {
        await using var schema = await MariaDbSchema.CreateAsync("bestand");
        await using var db = schema.NewContext();

        var alle = db.Database.GetMigrations().ToList();
        var index = alle.FindIndex(m => m.EndsWith("AddDirectoryPublicId", StringComparison.Ordinal));
        Assert.True(index > 0, "Migration AddDirectoryPublicId nicht gefunden");

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(alle[index - 1]);

        // Zwei Zeilen mit chess-results-Nummer, wie sie auf Dev und Prod stehen.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO TournamentDirectoryEntries
                (ChessResultsId, Name, StartsOnWeekend, MissedSweeps, FirstSeenAt, LastSeenAt,
                 CreatedAt, UpdatedAt, Speed, GeoSource, Kind, IsLeague, AgeGroups, Gender)
            VALUES ('1457129', 'Open Braunau', 0, 0, NOW(), NOW(), NOW(), NOW(), 1, 2, 0, 0, 0, 0),
                   ('1405166', 'Frauenbundesliga', 0, 0, NOW(), NOW(), NOW(), NOW(), 1, 2, 0, 0, 0, 0)
            """);

        // Und ein Nutzer, der sich eines davon weggeklickt hat. Diese Entscheidung muss die
        // Umstellung ueberleben — ein Spaltentausch per DropColumn/AddColumn wuerde sie
        // stillschweigend wegwerfen.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AppUsers (Username, PasswordHash, CreatedAt, IsAdmin)
            VALUES ('ausblender', 'x', NOW(), 0)
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO TournamentDirectoryIgnores (UserId, ChessResultsId, CreatedAt)
            SELECT Id, '1405166', NOW() FROM AppUsers WHERE Username = 'ausblender'
            """);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(["1405166"], await db.TournamentDirectoryIgnores
            .Select(i => i.PublicId).ToListAsync());
        // Und die Identitaet ist die chess-results-Nummer — bestehende Adressen, Abos und
        // Teilen-Links bleiben damit gueltig.
        var ids = await db.TournamentDirectoryEntries
            .OrderBy(e => e.PublicId)
            .Select(e => new { e.PublicId, e.ChessResultsId })
            .ToListAsync();
        Assert.Equal(["1405166", "1457129"], ids.Select(i => i.PublicId));
        Assert.All(ids, i => Assert.Equal(i.PublicId, i.ChessResultsId));
    }

    /// <summary>
    /// <c>ChessableImportFromBrowser</c> markiert nur die LAUFENDEN bzw. pausierten Browser-Importe: Phase
    /// „importing" mit <c>Attempts = 0</c>. Ein Server-Import in derselben Phase hat schon gezaehlt, ein
    /// fortgesetzter wartet auf „queued", ein abgeschlossener spielt keine Rolle. Ohne den Nachtrag reihte der
    /// Resume-Dienst beim Start mit der Migration genau solche Saetze als Server-Import ein.
    /// </summary>
    [MySqlFact]
    public async Task ChessableImportFromBrowser_MarkiertNurLaufendeBrowserImporte()
    {
        await using var schema = await MariaDbSchema.CreateAsync("frombrowser");
        await using var db = schema.NewContext();

        var alle = db.Database.GetMigrations().ToList();
        var index = alle.FindIndex(m => m.EndsWith("ChessableImportFromBrowser", StringComparison.Ordinal));
        Assert.True(index > 0, "Migration ChessableImportFromBrowser nicht gefunden");
        await db.GetService<IMigrator>().MigrateAsync(alle[index - 1]);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AppUsers (Username, PasswordHash, CreatedAt, IsAdmin)
            VALUES ('browser', 'x', NOW(), 0)
            """);
        var zeilen = new (string Bid, string Status, string Phase, int Attempts, bool Erwartet)[]
        {
            ("1", "running", "importing", 0, true),     // Browser-Import mitten im Streamen
            ("2", "paused", "importing", 0, true),      // vom Nutzer pausierter Browser-Import
            ("3", "running", "importing", 1, false),    // Server-Import beim Einspielen
            ("4", "running", "queued", 0, false),       // fortgesetzter Server-Import
            ("5", "completed", "done", 0, false),       // abgeschlossener Browser-Import: egal
        };
        foreach (var z in zeilen)
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO ChessableImports
                    (UserId, Bid, CourseName, Target, Status, Phase, Attempts, ChaptersDone, ChaptersTotal,
                     LinesDone, LinesTotal, LineCount, Imported, Skipped, Invalid, QueueRound, CreatedAt)
                SELECT Id, {0}, 'C', 'book', {1}, {2}, {3}, 0, 0, 0, 0, 0, 0, 0, 0, 0, NOW()
                FROM AppUsers WHERE Username = 'browser'
                """, z.Bid, z.Status, z.Phase, z.Attempts);

        await db.Database.MigrateAsync();

        var markiert = await db.ChessableImports.AsNoTracking()
            .ToDictionaryAsync(i => i.Bid, i => i.FromBrowser);
        Assert.All(zeilen, z => Assert.Equal(z.Erwartet, markiert[z.Bid]));
    }

    /// <summary>
    /// <c>LeagueOnlineLinesWithoutCheckSigns</c> (Codereview N4-001): die schon gespeicherten Lichess-Partien trugen „+"/„#"
    /// in <c>Line</c> und <c>Moves</c>, Brett- und chess.com-Partien nicht — der Eroeffnungsbaum zeigte denselben Zug zweimal.
    /// Die Migration nimmt die Zeichen heraus und laesst saubere Zeilen und alle anderen Felder unberuehrt.
    /// </summary>
    [MySqlFact]
    public async Task LeagueOnlineLinesWithoutCheckSigns_BereinigtNurSchachUndMattzeichen()
    {
        await using var schema = await MariaDbSchema.CreateAsync("onlinesan");
        await using var db = schema.NewContext();

        var alle = db.Database.GetMigrations().ToList();
        var index = alle.FindIndex(m => m.EndsWith("LeagueOnlineLinesWithoutCheckSigns", StringComparison.Ordinal));
        Assert.True(index > 0, "Migration LeagueOnlineLinesWithoutCheckSigns nicht gefunden");
        await db.GetService<IMigrator>().MigrateAsync(alle[index - 1]);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO LeagueOnlineAccounts (FideId, Site, UserName, Url, Confidence, Manual, SyncCursor, SyncMore, GameCount)
            VALUES ('222', 'lichess', 'Max_Muster', 'u', 'sicher', 1, 0, 0, 3)
            """);
        var zeilen = new (string Id, string Vorher, string Nachher)[]
        {
            ("bogo", "d4 Nf6 c4 e6 Nf3 Bb4+ Bd2 Bxd2+ Qxd2", "d4 Nf6 c4 e6 Nf3 Bb4 Bd2 Bxd2 Qxd2"),
            ("matt", "e4 e5 Bc4 Nc6 Qh5 Nf6 Qxf7#", "e4 e5 Bc4 Nc6 Qh5 Nf6 Qxf7"),
            ("sauber", "e4 c5 Nf3 d6 O-O-O e8=Q", "e4 c5 Nf3 d6 O-O-O e8=Q"),    // chess.com/schon bereinigt: bleibt
        };
        foreach (var z in zeilen)
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO LeagueOnlineGames
                    (AccountId, FideId, ExternalId, PlayedAt, Speed, Rated, White, Result, Opponent, Line, Moves, Plies)
                SELECT Id, '222', {0}, NOW(), 'blitz', 1, 1, '1-0', 'O+#', {1}, {1}, 9
                FROM LeagueOnlineAccounts WHERE UserName = 'Max_Muster'
                """, z.Id, z.Vorher);

        await db.Database.MigrateAsync();

        var nachher = await db.LeagueOnlineGames.AsNoTracking()
            .ToDictionaryAsync(g => g.ExternalId, g => (g.Line, g.Moves, g.Opponent, g.Plies));
        Assert.All(zeilen, z => Assert.Equal((z.Nachher, z.Nachher, (string?)"O+#", 9), nachher[z.Id]));
    }

    /// <summary>
    /// Faengt den Fall ab, dass jemand eine Entitaet aendert und die Migration vergisst: das
    /// Modell traegt dann Aenderungen, die in keiner Migration stehen, und Prod liefe mit einem
    /// Schema, das nicht zum Code passt.
    /// </summary>
    [MySqlFact]
    public async Task ModellUndMigrationen_LaufenNichtAuseinander()
    {
        await using var schema = await MariaDbSchema.CreateAsync("drift");
        await using var db = schema.NewContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        // Der Snapshot ist ein Roh-Modell; ohne Finalisierung fehlen ihm die relationalen
        // Zusatzdaten und der Vergleich meldete Unterschiede, die es gar nicht gibt.
        var fertigerSnapshot = db.GetService<IModelRuntimeInitializer>()
            .Initialize((IModel)snapshot!.Model, designTime: true, validationLogger: null);

        var differ = db.GetService<IMigrationsModelDiffer>();
        var unterschiede = differ.GetDifferences(
            fertigerSnapshot.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.True(unterschiede.Count == 0,
            $"Modell und Migrations-Snapshot laufen auseinander ({unterschiede.Count} Unterschiede) — fehlt eine Migration?");
    }
}
