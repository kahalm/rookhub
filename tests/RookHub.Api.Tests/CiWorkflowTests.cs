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
    /// Jedes Recht, das ein Job im AUFGERUFENEN Workflow verlangt, muss der aufrufende
    /// <c>tests:</c>-Job in <c>docker.yml</c> ebenfalls gewaehren.
    ///
    /// <para><b>Warum das der teuerste Fehler dieser Datei ist.</b> Ein aufgerufener Workflow
    /// darf nicht mehr Rechte haben als sein Aufrufer, und die Repo-Vorgabe ist
    /// <c>contents: read</c>. Verlangt ein Job in <c>test.yml</c> mehr — <c>pull-requests: read</c>,
    /// das dorny/paths-filter im pull_request-Fall braucht — und der <c>tests:</c>-Job nennt es
    /// nicht, dann weist GitHub den GANZEN Lauf ab, BEVOR ein Schritt laeuft: `startup_failure`,
    /// null Jobs, kein Test, kein Image. Es gibt dazu keine Annotation in der API und keine
    /// anklickbare Zeile in der Job-Liste, und die Datei ist einwandfreies YAML — actionlint
    /// winkt sie durch. Passiert bei v0.434.2 (Rechte in test.yml dazugekommen, Aufrufer nicht
    /// nachgezogen) und deshalb auch bei v0.434.3 unbemerkt geblieben.</para>
    /// </summary>
    [Fact]
    public void CallerJob_GrantsEveryPermissionTheCalledWorkflowAsksFor()
    {
        var wanted = PermissionScopes(ReadRepoFile(Tests));
        var granted = PermissionScopes(CallerTestsJob());

        var missing = wanted.Except(granted).ToList();

        Assert.True(missing.Count == 0,
            "test.yml verlangt Rechte, die der `tests:`-Job in docker.yml nicht gewaehrt — der "
            + "Lauf startet dann gar nicht: " + string.Join(", ", missing));
    }

    /// <summary>Alle <c>scope: stufe</c>-Paare unter einem <c>permissions:</c>-Block.</summary>
    private static HashSet<string> PermissionScopes(string yaml) =>
        Regex.Matches(yaml, @"(?ms)^(?<indent>\s*)permissions:\s*$(?<body>(?:\n\k<indent>\s+\S.*)+)")
            .SelectMany(block => Regex.Matches(block.Groups["body"].Value,
                    @"^\s*(?<scope>[a-z-]+):\s*(?<level>read|write|none)\s*$",
                    RegexOptions.Multiline)
                .Select(m => $"{m.Groups["scope"].Value}: {m.Groups["level"].Value}"))
            .ToHashSet();

    /// <summary>Der <c>tests:</c>-Job aus docker.yml — der Aufrufer des Test-Workflows.</summary>
    private static string CallerTestsJob()
    {
        var text = ReadRepoFile(Docker);
        var block = Regex.Match(text, @"(?ms)^  tests:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;

        Assert.Contains("uses: ./.github/workflows/test.yml", block);
        return block;
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

    /// <summary>
    /// Der Handstart (`gh workflow run docker.yml`) muss es GEBEN — sonst gibt es keinen Weg zu
    /// einem Image, wenn die Pfadfilter den Job weggelassen haben.
    ///
    /// <para>Der Fall ist real und nicht theoretisch: ein Push auf master faellt rot aus (hier
    /// geerbt von einem fremden Test), der reparierende Push beruehrt nur Frontend-Pfade, und
    /// damit hat `build-api` zwei Versionen lang nicht gebaut. master ist gruen, der Code ist
    /// gepusht — und auf Dev laeuft trotzdem der Stand von vorgestern. Ohne diesen Knopf ist der
    /// einzige Ausweg ein Alibi-Commit in einen Pfad, der den Filter trifft.</para>
    /// </summary>
    [Fact]
    public void DockerWorkflow_CanBeStartedByHand() =>
        Assert.Matches(@"(?m)^  workflow_dispatch:\s*$", ReadRepoFile(Docker));

    /// <summary>
    /// Und dann muss er auch ALLE drei Images bauen — ein Handstart, der wieder nur einen Teil
    /// baut, loest genau das Problem nicht, fuer das er da ist.
    /// </summary>
    [Theory]
    [InlineData("build-api")]
    [InlineData("build-frontend")]
    [InlineData("build-turnier")]
    public void EveryImageJob_AlsoBuildsOnAManualRun(string job)
    {
        var text = ReadRepoFile(Docker);
        var block = Regex.Match(text, $@"(?ms)^  {Regex.Escape(job)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;

        Assert.Contains("github.event_name == 'workflow_dispatch'", block);
    }

    /// <summary>
    /// Beim Handstart bleibt der Filter-Schritt AUS. dorny/paths-filter braucht einen
    /// Vorgaenger-Stand; bei `workflow_dispatch` haelt es master gegen master und setzt jeden
    /// Filter auf `false`. Liefe der Schritt, entschiede also ausgerechnet beim Handstart ein
    /// leerer Vergleich — und es liefe gar nichts.
    /// </summary>
    [Theory]
    [InlineData(Docker)]
    [InlineData(Tests)]
    public void OnAManualRun_ThePathFilterStepIsSkipped(string workflow)
    {
        var text = ReadRepoFile(workflow);
        var step = Regex.Match(text, @"(?ms)^      - uses: dorny/paths-filter@v3\s*$(.*?)(?=^      - |^  \S|\z)")
            .Groups[1].Value;

        Assert.Contains("filters: .github/filters.yml", step);
        Assert.Contains("if: github.event_name != 'workflow_dispatch'", step);
    }

    /// <summary>
    /// Weil der Filter-Schritt beim Handstart ausbleibt, muss JEDER Job in `test.yml`, der einen
    /// Filter abfragt, den Handstart selbst durchlassen. Sonst waere das Test-Gate bei einem
    /// Handstart leer und es entstuende ein Image, das kein einziger Test gesehen hat — das
    /// Gegenteil dessen, was der Knopf soll.
    /// </summary>
    [Fact]
    public void OnAManualRun_EveryTestJob_StillRuns()
    {
        var text = ReadRepoFile(Tests);

        foreach (Match job in Regex.Matches(text, @"(?ms)^  (?<name>[a-z][a-z0-9-]*):\s*$(?<body>.*?)(?=^  [a-z]|\z)"))
        {
            var body = job.Groups["body"].Value;
            if (!body.Contains("needs.changes.outputs", StringComparison.Ordinal)) continue;

            Assert.True(body.Contains("github.event_name == 'workflow_dispatch'", StringComparison.Ordinal),
                $"test.yml-Job '{job.Groups["name"].Value}' laeuft bei einem Handstart nicht mit — "
                + "das Image entstuende ungeprueft");
        }
    }
}
