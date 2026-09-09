namespace RookHub.Api.Tests;

/// <summary>
/// Eine QUELLTEXT-Wache: jede selbst geoeffnete Transaktion muss in der Execution-Strategy laufen.
///
/// <para>Der Grund ist ein Fehler, den kein Unit-Test sehen kann. Die Verbindung ist mit
/// <c>EnableRetryOnFailure</c> konfiguriert (Program.cs), und die dadurch aktive
/// <c>MySqlRetryingExecutionStrategy</c> WIRFT bei <c>BeginTransactionAsync</c>: bei einem
/// Wiederholversuch waere unklar, ob nur die einzelne Anweisung oder der ganze Block erneut laufen
/// soll. Die Tests laufen aber gegen die InMemory-Datenbank, und die kennt weder Transaktionen noch
/// Wiederholversuche — der betroffene Code nimmt dort den Nicht-relationalen Zweig und ist grün.</para>
///
/// <para>Am 2026-09-09 kostete das den kompletten Postleitzahlen-Import: alle sieben Laender
/// antworteten mit 500 („does not support user-initiated transactions"), und die Verortung blieb
/// auf dem Ortsnamen sitzen. Der Fehler war seit dem Bau der Funktion drin und in keinem Testlauf
/// zu sehen. Diese Wache liest deshalb den Quelltext.</para>
/// </summary>
public class TransactionStrategyTests
{
    private static string ApiSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");
        Assert.True(Directory.Exists(src), $"Quellordner fehlt: {src}");
        return src;
    }

    /// <summary>Wie viele Zeilen vor <c>BeginTransaction…</c> nach der Strategy gesucht wird.
    /// Grosszuegig, damit ein Kommentar dazwischen nicht als Verstoss zaehlt — der Block MUSS
    /// aber im selben Aufrufkoerper liegen, sonst waere die Suche wertlos.</summary>
    private const int LookBackLines = 15;

    [Fact]
    public void JedeSelbstGeoeffneteTransaktion_laeuftInDerExecutionStrategy()
    {
        var offenders = new List<string>();
        var files = Directory.GetFiles(ApiSourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f);

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("BeginTransaction")) continue;

                var from = Math.Max(0, i - LookBackLines);
                var window = string.Join('\n', lines[from..i]);
                if (window.Contains("CreateExecutionStrategy")) continue;

                offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Transaktion ohne Execution-Strategy — bei aktivem EnableRetryOnFailure wirft EF dort:\n"
            + string.Join('\n', offenders)
            + "\n\nMuster (siehe AdminService.ClearPuzzlesAsync / GazetteerImportService.ReplaceAsync):\n"
            + "  var strategy = _db.Database.CreateExecutionStrategy();\n"
            + "  await strategy.ExecuteAsync(async () => { await using var tx = ...; ...; await tx.CommitAsync(ct); });");
    }

    [Fact]
    public void DieWacheGreiftUeberhaupt_esGibtTransaktionenImQuelltext()
    {
        // Ohne diese Gegenprobe wuerde der Test oben auch dann gruen sein, wenn die Suche ins Leere
        // liest (falscher Pfad, umbenannte Methode) — eine Wache, die nie etwas findet, ist keine.
        var hits = Directory.GetFiles(ApiSourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Count(f => File.ReadAllText(f).Contains("BeginTransaction"));
        Assert.True(hits >= 2, $"Nur {hits} Datei(en) mit BeginTransaction gefunden — Suche prueft den falschen Ort?");
    }
}
