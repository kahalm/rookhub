using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Quelltext-Wache über das Roh-PGN (<c>BookSource</c>, Tabellensplitting auf <c>Books</c>).
///
/// <para><b>Warum ein Scan und kein Verhaltenstest:</b> das 11-GB-Problem (0.508.3) war KEIN falsches
/// Ergebnis — jede Abfrage lieferte das Richtige, nur eben samt mehrerer MB Roh-PGN je Zeile. Die
/// InMemory-Suite sieht so etwas nie, und <c>BookSourceSplitSqlTests</c> prüft nur die Abfragen, die man
/// dort kennt. Ein NEUES <c>.Include(b =&gt; b.Source)</c> in irgendeiner Listen-Abfrage fiele erst in
/// Produktion am Speicher auf. Deshalb steht hier die vollständige Liste der Stellen, die den Text laden
/// dürfen; jede weitere macht diesen Test rot — und zwingt zu der Frage, ob der Text dort wirklich
/// gebraucht wird.</para>
/// </summary>
public class BookSourceIncludeGuardTests
{
    /// <summary>Include des Roh-PGN, erlaubt NUR hier (Datei → Anzahl). Wer eine Stelle hinzufügt oder
    /// entfernt, passt die Liste an — mit Begründung im Code, warum der Text dort gebraucht wird.</summary>
    private static readonly Dictionary<string, int> ErlaubteIncludes = new()
    {
        // GetBookPgnAsync, GetChapterPgnAsync, GetLinePgnAsync — Download/„Kurs → Repertoire" (EIN Buch).
        ["Services/CourseService.cs"] = 3,
        // ImportFileAsync + ReprocessFromStoredSourceAsync — Import/Neu-Aufbereitung EINES Buchs.
        ["Services/PgnImportService.cs"] = 2,
    };

    /// <summary>Direkter Zugriff auf <c>_db.BookSources</c> (außerhalb des DbSets selbst).</summary>
    private static readonly Dictionary<string, int> ErlaubteBookSourcesZugriffe = new()
    {
        // DeleteBookAsync: Stub anhängen, damit InMemory die Source mit löscht (lädt keinen Text).
        ["Services/BookAdminService.cs"] = 1,
    };

    // .Include(b => b.Source) / .ThenInclude(x => x.Source!) — Lambda-Form, auch mit Klammern um den Parameter.
    private static readonly Regex IncludeLambda = new(
        @"\.(Then)?Include\(\s*\(?\s*\w+\s*\)?\s*=>\s*\w+\s*\.\s*Source\b", RegexOptions.Compiled);

    // .Include("Source") / .Include(nameof(Book.Source)) — String-Form.
    private static readonly Regex IncludeString = new(
        @"\.(Then)?Include\(\s*(""Source""|nameof\(\s*[\w.]*\bSource\s*\))", RegexOptions.Compiled);

    private static readonly Regex BookSourcesZugriff = new(@"\.BookSources\b", RegexOptions.Compiled);

    private static string ApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");
    }

    private static readonly Regex BlockKommentar = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    // Zeilenkommentar (auch ///) — nur nach Zeilenanfang/Leerraum, damit „https://…" in Strings bleibt.
    private static readonly Regex ZeilenKommentar = new(@"(^|\s)//.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Alle C#-Quellen der API ohne Build-Ausgabe und Migrationen, Pfade relativ mit „/",
    /// OHNE Kommentare (die Doku darf die Muster nennen, ohne mitzuzählen).</summary>
    private static List<(string Pfad, string Text)> Quellen()
    {
        var root = ApiRoot();
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Pfad: Path.GetRelativePath(root, f).Replace('\\', '/'), Voll: f))
            .Where(f => !f.Pfad.StartsWith("bin/") && !f.Pfad.StartsWith("obj/") && !f.Pfad.StartsWith("Migrations/"))
            .Select(f => (f.Pfad, OhneKommentare(File.ReadAllText(f.Voll))))
            .ToList();
        Assert.True(files.Count > 50, $"Zu wenige Quellen gefunden ({files.Count}) — stimmt der Pfad {root}?");
        return files;
    }

    private static string OhneKommentare(string code) =>
        ZeilenKommentar.Replace(BlockKommentar.Replace(code, ""), "$1");

    private static Dictionary<string, int> Zaehle(Func<string, int> treffer) =>
        Quellen()
            .Select(q => (q.Pfad, Anzahl: treffer(q.Text)))
            .Where(q => q.Anzahl > 0)
            .ToDictionary(q => q.Pfad, q => q.Anzahl);

    [Fact]
    public void IncludeDerSource_NurAnDenErlaubtenStellen()
    {
        var gefunden = Zaehle(t => IncludeLambda.Matches(t).Count + IncludeString.Matches(t).Count);

        Assert.True(
            gefunden.OrderBy(k => k.Key).SequenceEqual(ErlaubteIncludes.OrderBy(k => k.Key)),
            "Include des Roh-PGN (BookSource) an nicht freigegebener Stelle bzw. in anderer Anzahl.\n" +
            $"Gefunden: {string.Join(", ", gefunden.Select(k => $"{k.Key}={k.Value}"))}\n" +
            $"Erlaubt:  {string.Join(", ", ErlaubteIncludes.Select(k => $"{k.Key}={k.Value}"))}\n" +
            "Der Text ist bis zu 6 MB groß — in einer Listen-Abfrage multipliziert er sich mit jeder Zeile. " +
            "Nur einschließen, wo EIN Buch mit Text gebraucht wird, und die Liste oben anpassen.");
    }

    [Fact]
    public void ThenIncludeDerSource_GibtEsNirgends()
    {
        // ThenInclude(…Source) hängt den Text an eine übergeordnete Abfrage (Puzzles → Book → Source):
        // genau die Form, in der er sich pro Zeile vervielfacht.
        var treffer = Quellen()
            .SelectMany(q => IncludeLambda.Matches(q.Text).Concat(IncludeString.Matches(q.Text))
                .Where(m => m.Groups[1].Success)
                .Select(m => $"{q.Pfad}: {m.Value}"))
            .ToList();

        Assert.Empty(treffer);
    }

    [Fact]
    public void DirekterBookSourcesZugriff_NurAnDenErlaubtenStellen()
    {
        var gefunden = Zaehle(t => BookSourcesZugriff.Matches(t).Count);
        gefunden.Remove("Data/AppDbContext.cs");   // das DbSet selbst

        Assert.True(
            gefunden.OrderBy(k => k.Key).SequenceEqual(ErlaubteBookSourcesZugriffe.OrderBy(k => k.Key)),
            "Direkter Zugriff auf _db.BookSources an nicht freigegebener Stelle.\n" +
            $"Gefunden: {string.Join(", ", gefunden.Select(k => $"{k.Key}={k.Value}"))}");
    }

    [Fact]
    public void Scanner_ErkenntDieFormen()
    {
        // Selbsttest der Muster — sonst wäre ein grüner Scan womöglich nur ein blinder.
        Assert.Matches(IncludeLambda, "_db.Books.Include(b => b.Source)");
        Assert.Matches(IncludeLambda, "q.Include( (b) => b .Source)");
        Assert.Matches(IncludeLambda, ".Include(bp => bp.Book).ThenInclude(b => b.Source!)");
        Assert.Matches(IncludeString, ".Include(\"Source\")");
        Assert.Matches(IncludeString, ".Include(nameof(Book.Source))");
        Assert.DoesNotMatch(IncludeLambda, ".Include(bp => bp.Book)");
        Assert.DoesNotMatch(IncludeLambda, "Where(s => s.SourcePgn != null)");
        Assert.True(IncludeLambda.Match(".ThenInclude(b => b.Source)").Groups[1].Success);
        Assert.Equal("var a = 1;  \nvar u = \"https://x\";",
            OhneKommentare("var a = 1;  // .Include(b => b.Source)\n/// <c>x</c>\nvar u = \"https://x\";").Replace("\n\n", "\n"));
    }
}
