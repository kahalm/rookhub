using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Wächter über die Migrations-Dateien. Anlass (2026-09-28): „WorksheetSharing" und
/// „WorksheetThemes" lagen mit LEEREM <c>Up()</c> im Verzeichnis — nach einem Rebase neu erzeugt,
/// während der Modell-Schnappschuss die Änderungen schon enthielt, also fand EF keinen Unterschied
/// mehr. Angewendet wurden sie trotzdem (Zeile in <c>__EFMigrationsHistory</c>), und weil eine
/// verbuchte Migration nie wiederholt wird, fehlten die Spalten auf Dev UND Prod dauerhaft: jeder
/// Aufruf eines Aufgabenblatts endete in „Unknown column 'w.ShareToken'".
/// </summary>
public class MigrationsInventoryTests
{
    /// <summary>
    /// Migrationen, deren leeres <c>Up()</c> in Ordnung ist bzw. Geschichte ist — mit Grund.
    /// Ein neuer Eintrag gehört begründet; ohne Grund ist eine leere Migration ein Datenverlust,
    /// der erst im Betrieb auffällt.
    /// </summary>
    private static readonly Dictionary<string, string> EmptyOnPurpose = new()
    {
        ["20260923102429_SplitBookSource"] =
            "Tabellensplitting: Books.SourcePgn wandert in eine eigene Entität AUF DERSELBEN Tabelle — "
            + "reine Mapping-Änderung, kein Schema-Eingriff.",
        ["20260920084440_WorksheetSharing"] =
            "Historisch: hätte ShareToken/SharedAt/SolutionMoves anlegen sollen, blieb leer. Bereits "
            + "als angewendet verbucht, wird daher NICHT nachträglich gefüllt — nachgeholt von "
            + "20260928194029_WorksheetColumnsRepair.",
        ["20260920092946_WorksheetThemes"] =
            "Historisch: hätte Themes/SourceThemes anlegen sollen, blieb leer. Siehe "
            + "20260928194029_WorksheetColumnsRepair.",
    };

    [Fact]
    public void Every_migration_actually_does_something()
    {
        var empty = new List<string>();

        foreach (var file in MigrationFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (EmptyOnPurpose.ContainsKey(name)) continue;

            var body = UpBody(File.ReadAllText(file));
            if (!body.Contains("migrationBuilder.", StringComparison.Ordinal)) empty.Add(name);
        }

        Assert.True(empty.Count == 0,
            "Migration(en) mit leerem Up() — das passiert, wenn eine Migration neu erzeugt wird, "
            + "während der Modell-Schnappschuss die Änderung schon enthält. Sie wird angewendet und "
            + "verbucht, ohne je etwas zu tun; das Schema bleibt für immer zurück. Migration neu "
            + "erzeugen (Schnappschuss zurücksetzen!) ODER mit Grund in EmptyOnPurpose eintragen:\n  "
            + string.Join("\n  ", empty));
    }

    [Fact]
    public void The_allowlist_has_no_leftovers_from_deleted_migrations()
    {
        var known = MigrationFiles().Select(Path.GetFileNameWithoutExtension).ToHashSet();
        var stale = EmptyOnPurpose.Keys.Where(k => !known.Contains(k)).ToList();

        Assert.True(stale.Count == 0, "EmptyOnPurpose nennt Migrationen, die es nicht mehr gibt:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void The_repair_migration_brings_back_every_column_the_empty_ones_owed()
    {
        var repair = File.ReadAllText(MigrationFiles().Single(f => f.EndsWith("WorksheetColumnsRepair.cs", StringComparison.Ordinal)));

        foreach (var column in new[] { "ShareToken", "SharedAt", "SolutionMoves", "Themes", "SourceThemes" })
            Assert.Contains(column, repair, StringComparison.Ordinal);

        // Ohne IF NOT EXISTS scheitert sie auf jeder von Hand reparierten Datenbank.
        Assert.Contains("IF NOT EXISTS", repair, StringComparison.Ordinal);
    }

    private static string UpBody(string source)
    {
        var match = Regex.Match(source,
            @"protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{(.*?)\n        \}", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static List<string> MigrationFiles([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Migrations");
            if (Directory.Exists(candidate))
                return Directory.GetFiles(candidate, "*.cs")
                    .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal)
                                && !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();
            dir = Path.GetDirectoryName(dir);
        }

        Assert.Fail("Migrations-Verzeichnis nicht gefunden — Projektstruktur geändert?");
        return new List<string>();
    }
}
