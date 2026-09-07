using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Wacht ueber die Pfad-Filter der CI. Sie entscheiden, welche Jobs ein Push ueberhaupt startet —
/// ein Fehler dort zeigt sich NICHT als roter Lauf, sondern als ausgelassener Job und als Image,
/// das still alt bleibt. Genau deshalb steht hier ein Test und nicht nur ein Kommentar.
/// </summary>
public class CiWorkflowTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Datei fehlt: {relativePath}");
        return File.ReadAllText(path);
    }

    private const string Filters = ".github/filters.yml";
    private const string Docker = ".github/workflows/docker.yml";
    private const string Tests = ".github/workflows/test.yml";

    /// <summary>Die Filternamen der Datei (Zeilenanfang, ohne Einrueckung, ggf. mit YAML-Anker).</summary>
    private static HashSet<string> FilterNames() =>
        Regex.Matches(ReadRepoFile(Filters), @"(?m)^([a-z][a-z0-9-]*):(?:\s*&\S+)?\s*$")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Alle in einem Workflow abgefragten Filter — beide Schreibweisen.</summary>
    private static IEnumerable<string> ReferencedFilters(string workflow) =>
        Regex.Matches(ReadRepoFile(workflow), @"needs\.changes\.outputs(?:\.([a-z][a-z0-9-]*)|\['([a-z][a-z0-9-]*)'\])")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Distinct();

    /// <summary>
    /// Ein Tippfehler im Filternamen ist der teuerste Fehler hier: ein nicht existierender Output
    /// ist leer, die Bedingung also `false` — der Job laeuft ab da NIE mehr, ohne jede Meldung.
    /// </summary>
    [Theory]
    [InlineData(Docker)]
    [InlineData(Tests)]
    public void EveryReferencedFilter_IsDefined(string workflow)
    {
        var defined = FilterNames();
        var referenced = ReferencedFilters(workflow).ToList();

        Assert.NotEmpty(referenced);
        foreach (var name in referenced)
            Assert.True(defined.Contains(name), $"{workflow} fragt Filter '{name}', {Filters} kennt ihn nicht");
    }

    /// <summary>
    /// Wer an einem Workflow schraubt, will alles laufen sehen. Faehrt ein Filter die CI-Dateien
    /// nicht mit, prueft sich eine Aenderung an ihnen selbst nicht.
    /// </summary>
    [Fact]
    public void EveryFilter_IncludesTheWorkflowFilesThemselves()
    {
        var text = ReadRepoFile(Filters);
        foreach (var name in FilterNames())
        {
            var block = Regex.Match(text, $@"(?ms)^{Regex.Escape(name)}:(?:\s*&\S+)?\s*$(.*?)(?=^\S|\z)").Groups[1].Value;
            Assert.True(
                block.Contains(".github/workflows/**", StringComparison.Ordinal) || block.Contains("*frontend", StringComparison.Ordinal),
                $"Filter '{name}' zieht die CI-Dateien nicht mit");
        }
    }

    /// <summary>
    /// Die zwei Angular-Projekte teilen sich `src/app` (Alias @rh/*). Deckt der turnier-Filter den
    /// geteilten Code nicht ab, bliebe das Turnier-Image nach einer Aenderung dort still alt —
    /// derselbe Fehler wie die fehlenden Symbole, nur unsichtbarer.
    /// </summary>
    [Fact]
    public void TurnierFilter_CoversTheSharedFrontendCode()
    {
        var text = ReadRepoFile(Filters);
        var block = Regex.Match(text, @"(?ms)^turnier:\s*$(.*?)(?=^\S|\z)").Groups[1].Value;

        Assert.Contains("*frontend", block);          // der geteilte Block
        Assert.Contains("src-turnier/**", block);     // und das Eigene
    }

    /// <summary>
    /// In einem Actions-AUSDRUCK ist der Bindestrich der MINUS-Operator:
    /// <c>outputs.engine-provider</c> liest GitHub als <c>outputs.engine</c> minus
    /// <c>provider</c>. Der Ausdruck ist damit ungueltig, und das kostet nicht einen Job,
    /// sondern den ganzen Lauf — `startup_failure`, kein einziger Schritt, kein Image, und in
    /// der Job-Liste steht nichts, was man anklicken koennte.
    ///
    /// <para>Live passiert (v0.434.2): EINE solche Zeile in den `outputs:` des
    /// `changes`-Jobs liess beide Workflows nicht mehr anlaufen — waehrend die YAML-Datei
    /// syntaktisch tadellos ist und jeder Linter sie durchwinkt. Richtig ist die
    /// Index-Schreibweise <c>outputs['engine-provider']</c>; an der EINEN Stelle, die den
    /// Filter abfragt, stand sie schon, in der Ausgabe-Zuweisung nicht.</para>
    /// </summary>
    [Theory]
    [InlineData(Docker)]
    [InlineData(Tests)]
    public void NoWorkflowExpression_ReadsAHyphenatedNameWithDotSyntax(string workflow)
    {
        var text = ReadRepoFile(workflow);

        // Nur INNERHALB von ${{ }} suchen: ein Kommentar, der die falsche Form ZITIERT (wie der
        // in test.yml, der genau davor warnt), ist kein Fehler — und ein Test, der daran
        // scheitert, verbietet das Erklaeren.
        var offenders = Regex.Matches(text, @"\$\{\{(.*?)\}\}", RegexOptions.Singleline)
            .SelectMany(expr => Regex.Matches(
                    expr.Groups[1].Value,
                    @"(?:outputs|inputs|vars|env)\.[A-Za-z_][A-Za-z0-9_]*-[A-Za-z0-9_]")
                .Select(m => m.Value))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Bindestrich = Minus im Ausdruck; stattdessen outputs['name-mit-strich'] benutzen: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Die Betriebs-Skripte liegen nicht im API-Baum, ihre Tests aber schon
    /// (<c>DeploymentConfigTests</c>). Faellt `scripts/**` aus dem api-Filter, startet eine
    /// Aenderung, die NUR ein Skript beruehrt, keinen einzigen Job — und ausgerechnet der Test,
    /// der dieses Skript festnagelt, bleibt stehen. Dieselbe Klasse Fehler wie Regel 1 der
    /// Filter-Datei, nur eine Ebene weiter.
    /// </summary>
    [Fact]
    public void ApiFilter_CoversTheOperationsScripts()
    {
        var text = ReadRepoFile(Filters);
        var block = Regex.Match(text, @"(?ms)^api:\s*$(.*?)(?=^\S|\z)").Groups[1].Value;

        Assert.Contains("tests/**", block);
        Assert.Contains("scripts/**", block);
    }

    /// <summary>
    /// Ein TAG-Lauf muss ALLE Images bauen: `:latest` entsteht nur dort, und beim Tag gibt es
    /// keinen Vorgaenger-Stand, gegen den ein Filter sinnvoll vergleichen koennte.
    /// </summary>
    [Theory]
    [InlineData("build-api")]
    [InlineData("build-frontend")]
    [InlineData("build-turnier")]
    public void EveryImageJob_StillBuildsOnATag(string job)
    {
        var text = ReadRepoFile(Docker);
        var block = Regex.Match(text, $@"(?ms)^  {Regex.Escape(job)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;

        Assert.Contains("needs.changes.outputs", block);
        Assert.Contains("startsWith(github.ref, 'refs/tags/v')", block);
    }

    /// <summary>
    /// `needs: tests` allein wuerde einen Image-Job mit-ueberspringen, sobald der aufgerufene
    /// Test-Workflow selbst nichts zu tun hatte — deshalb der ausgeschriebene Ergebnis-Vergleich
    /// statt der impliziten Regel.
    /// </summary>
    [Theory]
    [InlineData("build-api")]
    [InlineData("build-frontend")]
    [InlineData("build-turnier")]
    public void EveryImageJob_StillWaitsForTheTestGate(string job)
    {
        var text = ReadRepoFile(Docker);
        var block = Regex.Match(text, $@"(?ms)^  {Regex.Escape(job)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;

        Assert.Contains("needs: [changes, tests]", block);
        Assert.Contains("needs.tests.result != 'failure'", block);
        Assert.Contains("needs.tests.result != 'cancelled'", block);
    }
}
