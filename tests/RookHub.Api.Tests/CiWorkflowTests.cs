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
    private const string Android = ".github/workflows/android-twa.yml";

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
    /// Outputs des <c>changes</c>-Jobs, die aus einem ANDEREN Schritt als dem Filter stammen
    /// (heute nur <c>release</c> in docker.yml) — sie sind ausdruecklich deklariert, kein Filtername.
    /// </summary>
    private static HashSet<string> NonFilterOutputs(string workflow) =>
        Regex.Matches(ReadRepoFile(workflow), @"(?m)^      ([a-z][a-z0-9-]*): \$\{\{ steps\.(?!filter\.)[a-z0-9_-]+\.outputs\.")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

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
        var referenced = ReferencedFilters(workflow).Except(NonFilterOutputs(workflow)).ToList();

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
    /// Dasselbe fuer KidHub (Kinderseite): es importiert Brett, HTTP-Kette und Sprachdateien per
    /// @rh/* aus `src/app` — ohne den geteilten Block bliebe sein Image nach einer Aenderung dort alt.
    /// </summary>
    [Fact]
    public void KidHubFilter_CoversTheSharedFrontendCode()
    {
        var text = ReadRepoFile(Filters);
        var block = Regex.Match(text, @"(?ms)^kidhub:\s*$(.*?)(?=^\S|\z)").Groups[1].Value;

        Assert.Contains("*frontend", block);
        Assert.Contains("src-kidhub/**", block);
    }

    /// <summary>LeagueHub importiert Anmeldung, HTTP-Kette und Sprachdateien per @rh/* — der Filter braucht den geteilten Block.</summary>
    [Fact]
    public void LeagueHubFilter_CoversTheSharedFrontendCode()
    {
        var text = ReadRepoFile(Filters);
        var block = Regex.Match(text, @"(?ms)^leaguehub:\s*$(.*?)(?=^\S|\z)").Groups[1].Value;

        Assert.Contains("*frontend", block);
        Assert.Contains("src-leaguehub/**", block);
    }

    /// <summary>ClubHub importiert Anmeldung, HTTP-Kette und Sprachdateien per @rh/* — der Filter braucht den geteilten Block.</summary>
    [Fact]
    public void ClubHubFilter_CoversTheSharedFrontendCode()
    {
        var text = ReadRepoFile(Filters);
        var block = Regex.Match(text, @"(?ms)^clubhub:\s*$(.*?)(?=^\S|\z)").Groups[1].Value;

        Assert.Contains("*frontend", block);
        Assert.Contains("src-clubhub/**", block);
        Assert.Contains("public-clubhub/**", block);
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
        // init-db.sh liegt im Repo-Root, sein Test in scripts/tests/ (Job test-scripts).
        Assert.Contains("'init-db.sh'", block);
    }

    /// <summary>
    /// Die Tests der Betriebs-Skripte (<c>scripts/tests/</c>) liefen in keinem Workflow (Codereview
    /// I1-015) — darunter der, der „kein Passwort im argv" fuer das Backup festhaelt. Jede Datei dort
    /// muss im Job <c>test-scripts</c> aufgerufen werden; ein neuer Test, den niemand eintraegt,
    /// faellt hier auf statt still nie zu laufen. Und der Job folgt dem api-Filter, der scripts/** traegt.
    /// </summary>
    [Fact]
    public void EveryScriptTest_RunsInTheTestWorkflow()
    {
        var job = TestJob("test-scripts");
        Assert.Contains("needs.changes.outputs.api == 'true'", job);
        // Ein haengender Skript-Test darf das Image-Gate nicht bis zum 6-h-Standard aufhalten.
        Assert.Matches(@"(?m)^    timeout-minutes: \d+\s*$", job);

        var dir = Path.Combine(RepoRoot(), "scripts", "tests");
        var files = Directory.GetFiles(dir, "test_*")
            .Where(f => f.EndsWith(".sh", StringComparison.Ordinal) || f.EndsWith(".py", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var runner = file!.EndsWith(".py", StringComparison.Ordinal) ? "python3" : "bash";
            // Zeilengenau: ein auskommentiertes "# - run: ..." zaehlt NICHT als eingetragen.
            var step = $@"(?m)^      - run: {Regex.Escape(runner)} {Regex.Escape($"scripts/tests/{file}")}\s*$";
            Assert.True(Regex.IsMatch(job, step),
                $"scripts/tests/{file} laeuft in keinem CI-Job — in test.yml/test-scripts eintragen");
        }
    }

    /// <summary>
    /// Ein TAG-Lauf muss ALLE Images bauen: `:latest` entsteht nur dort, und beim Tag gibt es
    /// keinen Vorgaenger-Stand, gegen den ein Filter sinnvoll vergleichen koennte.
    /// </summary>
    [Theory]
    [InlineData("build-api")]
    [InlineData("build-frontend")]
    [InlineData("build-turnier")]
    [InlineData("build-kidhub")]
    [InlineData("build-leaguehub")]
    [InlineData("build-clubhub")]
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
    [InlineData("build-kidhub")]
    [InlineData("build-leaguehub")]
    [InlineData("build-clubhub")]
    public void EveryImageJob_StillWaitsForTheTestGate(string job)
    {
        var text = ReadRepoFile(Docker);
        var block = Regex.Match(text, $@"(?ms)^  {Regex.Escape(job)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;

        Assert.Matches(@"needs: \[changes, tests[,\]]", block);
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
    /// Und dann muss er auch ALLE Images bauen — ein Handstart, der wieder nur einen Teil
    /// baut, loest genau das Problem nicht, fuer das er da ist.
    /// </summary>
    [Theory]
    [InlineData("build-api")]
    [InlineData("build-frontend")]
    [InlineData("build-turnier")]
    [InlineData("build-kidhub")]
    [InlineData("build-leaguehub")]
    [InlineData("build-clubhub")]
    public void EveryImageJob_AlsoBuildsOnAManualRun(string job)
    {
        var text = ReadRepoFile(Docker);
        var block = Regex.Match(text, $@"(?ms)^  {Regex.Escape(job)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;

        Assert.Contains("github.event_name == 'workflow_dispatch'", block);
    }

    /// <summary>
    /// Der Vorbau darf NUR den Hilfs-Tag pushen. Er laeuft absichtlich VOR dem Test-Gate — wuerde
    /// er `:dev`, `:latest` oder einen Semver-Tag setzen, waere das Gate lautlos ausgehebelt: der
    /// Deploy und Watchtower lesen genau diese Tags, und sie zeigten dann auf ein Image, das kein
    /// Test gesehen hat. Der `ci-*`-Tag dagegen wird von niemandem konsumiert.
    /// </summary>
    [Theory]
    [InlineData("api")]
    [InlineData("frontend")]
    [InlineData("turnier")]
    [InlineData("kidhub")]
    [InlineData("leaguehub")]
    [InlineData("clubhub")]
    public void EveryPrebuildJob_OnlyPushesTheStagingTag(string image)
    {
        var block = Job($"prebuild-{image}");

        Assert.Contains($"rookhub-{image}:ci-${{{{ github.run_id }}}}", block);
        Assert.DoesNotContain("value=dev", block);
        Assert.DoesNotContain("value=latest", block);
        Assert.DoesNotContain("type=semver", block);
    }

    /// <summary>
    /// Und er darf NICHT auf das Gate warten — sonst laege er auf demselben kritischen Pfad wie
    /// der Build frueher und waere wertlos. Genau das war der Sinn des Umbaus.
    /// </summary>
    [Theory]
    [InlineData("api")]
    [InlineData("frontend")]
    [InlineData("turnier")]
    [InlineData("kidhub")]
    [InlineData("leaguehub")]
    [InlineData("clubhub")]
    public void EveryPrebuildJob_DoesNotWaitForTheTestGate(string image)
    {
        var block = Job($"prebuild-{image}");

        Assert.Contains("needs: changes\n", block);
        Assert.DoesNotContain("needs: [changes, tests", block);
    }

    /// <summary>
    /// Der teuerste Fehler dieser Konstruktion: ein Vorbau, den die Pfadfilter bei einem TAG-Lauf
    /// auslassen. Dann faellt der zugehoerige Build-Job mangels `needs` aus — und es gibt kein
    /// `:latest`, also kein Release, obwohl alles gruen ist. Der Build-Job unten laesst Tag und
    /// Handstart durch (eigener Test); der Vorbau MUSS dieselben zwei Faelle durchlassen.
    /// </summary>
    [Theory]
    [InlineData("api")]
    [InlineData("frontend")]
    [InlineData("turnier")]
    [InlineData("kidhub")]
    [InlineData("leaguehub")]
    [InlineData("clubhub")]
    public void EveryPrebuildJob_AlsoRunsOnATagAndOnAManualRun(string image)
    {
        var block = Job($"prebuild-{image}");

        Assert.Contains("startsWith(github.ref, 'refs/tags/v')", block);
        Assert.Contains("github.event_name == 'workflow_dispatch'", block);
        Assert.Contains("needs.changes.outputs", block);
    }

    /// <summary>
    /// Hinter dem Gate wird NICHT mehr gebaut, sondern nur umgehaengt: `imagetools create` haengt
    /// die echten Tags an das Image, das der Vorbau schon gepusht hat. Ein wieder eingebautes
    /// `build-push-action` waere kein Fehler, den man sieht — es waere einfach wieder langsam
    /// (`build-api` lag bei 1:44 auf dem kritischen Pfad), deshalb steht es hier als Test.
    /// </summary>
    [Theory]
    [InlineData("api")]
    [InlineData("frontend")]
    [InlineData("turnier")]
    [InlineData("kidhub")]
    [InlineData("leaguehub")]
    [InlineData("clubhub")]
    public void EveryImageJob_OnlyRetagsThePrebuiltImage(string image)
    {
        var block = Job($"build-{image}");

        Assert.Contains("docker buildx imagetools create", block);
        Assert.Contains($"rookhub-{image}:ci-${{{{ github.run_id }}}}", block);
        Assert.Contains($"prebuild-{image}", block);        // ohne das Warten waere der Tag noch nicht da
        Assert.DoesNotContain("build-push-action", block);
    }

    /// <summary>
    /// Nur ein Release-Tag <c>vX.Y.Z</c> startet einen Tag-Lauf. Vorher stand im Ausloeser
    /// <c>v*</c> — ein Sicherungs-Tag wie <c>vorher-umbau</c> auf einem Feature-Branch haette
    /// alle Images gebaut und <c>:latest</c> an den Branch-Stand gehaengt; Watchtower rollt das
    /// nachts auf Prod aus, und <c>Database.Migrate()</c> wendet die Branch-Migrationen dort an.
    /// </summary>
    [Fact]
    public void DockerWorkflow_OnlyStartsOnSemverTags()
    {
        var push = Regex.Match(ReadRepoFile(Docker), @"(?ms)^  push:\s*$(.*?)(?=^  \S|\z)").Groups[1].Value;
        var tags = Regex.Matches(push, @"(?m)^    tags:\s*(\S.*)$").Select(m => m.Groups[1].Value.Trim()).ToList();

        Assert.Equal("['v[0-9]+.[0-9]+.[0-9]+']", Assert.Single(tags));
    }

    /// <summary>
    /// Der Ausloeser allein reicht nicht: ein Handstart auf einem beliebigen Tag und ein
    /// Release-Tag auf einem ungemergten Commit kaemen sonst durch. Der <c>changes</c>-Job prueft
    /// deshalb bei JEDEM Tag-Lauf die Form und ob der Commit auf master liegt, und bricht sonst ab
    /// — daran haengen Vorbau und Umhaengen per <c>needs</c>.
    /// </summary>
    [Fact]
    public void ChangesJob_RejectsTagsThatAreNotAReleaseOnMaster()
    {
        var block = Job("changes");
        var step = Regex.Match(block, @"(?ms)^      - name: Release-Tag pruefen[^\n]*$(.*?)(?=^      - |\z)").Groups[1].Value;

        Assert.Contains("release: ${{ steps.release.outputs.release }}", block);
        Assert.Contains("id: release", step);
        Assert.Contains("if: startsWith(github.ref, 'refs/tags/')", step);
        Assert.Contains(@"^v[0-9]+\.[0-9]+\.[0-9]+$", step);
        Assert.Contains("git fetch --no-tags --quiet origin +refs/heads/master:refs/remotes/origin/master", step);
        Assert.Contains("git merge-base --is-ancestor \"$GITHUB_SHA\" origin/master", step);
        Assert.Equal(2, Regex.Matches(step, @"exit 1").Count);
        Assert.Contains("echo \"release=true\" >> \"$GITHUB_OUTPUT\"", step);
    }

    /// <summary>
    /// <c>:latest</c> haengt NUR an der Freigabe des <c>changes</c>-Jobs, nicht am Praefix
    /// <c>refs/tags/v</c> — das liess jeden Tag durch, der mit v beginnt.
    /// </summary>
    [Theory]
    [InlineData("api")]
    [InlineData("frontend")]
    [InlineData("turnier")]
    [InlineData("kidhub")]
    [InlineData("leaguehub")]
    [InlineData("clubhub")]
    public void EveryImageJob_SetsLatestOnlyForAReleaseOnMaster(string image)
    {
        var block = Job($"build-{image}");
        var latest = Regex.Matches(block, @"(?m)^\s*type=raw,value=latest,.*$").Select(m => m.Value.Trim()).ToList();

        Assert.Equal("type=raw,value=latest,enable=${{ needs.changes.outputs.release == 'true' }}", Assert.Single(latest));
        Assert.Matches(@"needs: \[changes,", block);
    }

    /// <summary>
    /// Die Jobs von android-twa.yml, jeweils OHNE Kommentarzeilen — geprueft wird, was laeuft,
    /// nicht was erklaert wird.
    /// </summary>
    private static Dictionary<string, string> AndroidJobs()
    {
        var text = ReadRepoFile(Android);
        var jobs = text[text.IndexOf("\njobs:", StringComparison.Ordinal)..];
        return Regex.Matches(jobs, @"(?ms)^  (?<name>[a-z][a-z0-9-]*):\s*$(?<body>.*?)(?=^  [a-z]|\z)")
            .ToDictionary(
                m => m.Groups["name"].Value,
                m => string.Join('\n', m.Groups["body"].Value.Split('\n').Where(l => !l.TrimStart().StartsWith('#'))));
    }

    /// <summary>
    /// Der Android-Signaturschluessel ist fuer direkt verteilte APKs nicht rotierbar: wer ihn
    /// abgreift, signiert Updates der installierten App. Deshalb gibt es die Secrets NUR im
    /// <c>sign</c>-Job — ohne npm, ohne Bubblewrap, ohne Checkout, ohne Fremd-Action und ohne
    /// Schreibrecht. Vorher lagen Keystore und Passwoerter im selben Job wie
    /// <c>npm install -g @bubblewrap/cli</c>, zwei Fremd-Actions auf beweglichen Tags und ein
    /// GITHUB_TOKEN mit <c>contents: write</c>.
    /// </summary>
    [Fact]
    public void AndroidWorkflow_OnlyTheSignJobSeesTheSigningSecrets()
    {
        var jobs = AndroidJobs();

        Assert.Equal(["build", "release", "sign"], jobs.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, body) in jobs)
            Assert.True(name == "sign" || !body.Contains("secrets.", StringComparison.Ordinal),
                $"android-twa.yml: Job '{name}' greift auf Secrets zu — die gehoeren nur in den sign-Job");

        var sign = jobs["sign"];
        Assert.Contains("secrets.ANDROID_KEYSTORE_BASE64", sign);
        Assert.Contains("secrets.ANDROID_KEYSTORE_PASSWORD", sign);
        Assert.Contains("secrets.ANDROID_KEY_PASSWORD", sign);
        Assert.Contains("contents: read", sign);
        Assert.DoesNotContain("contents: write", sign);
        Assert.DoesNotContain("npm ", sign);
        Assert.DoesNotContain("bubblewrap", sign, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actions/checkout", sign);
        foreach (Match use in Regex.Matches(sign, @"uses:\s*(\S+)@"))
            Assert.Contains(use.Groups[1].Value, new[] { "actions/download-artifact", "actions/upload-artifact" });
        Assert.Contains("apksigner sign", sign);
        Assert.Contains("--ks-pass env:", sign);
        Assert.Contains("-storepass:env", sign);
    }

    /// <summary>
    /// Schreibrecht nur dort, wo das Release entsteht — und genau dort keine Secrets. Der
    /// Bau-Job, in dem der ganze npm-Baum laeuft, darf nur lesen.
    /// </summary>
    [Fact]
    public void AndroidWorkflow_WriteAccessOnlyInTheReleaseJob()
    {
        var jobs = AndroidJobs();

        foreach (var (name, body) in jobs)
        {
            Assert.Matches(@"(?m)^    permissions:\s*$", body);
            if (body.Contains("contents: write", StringComparison.Ordinal))
                Assert.Equal("release", name);
        }
        Assert.Contains("contents: read", jobs["build"]);
        Assert.Contains("contents: write", jobs["release"]);
        Assert.Contains("if: inputs.variant == 'prod'", jobs["release"]);
        Assert.Matches(@"needs: \[build, sign\]", jobs["release"]);
    }

    /// <summary>
    /// Bubblewrap baut UNSIGNIERT und kommt aus dem eingecheckten Lockfile: <c>npm ci</c> nimmt
    /// exakt diesen Baum (Integrity-Hashes), <c>--ignore-scripts</c> laesst keine Install-Skripte
    /// laufen. Bekommt ein Paket im Baum spaeter doch eins, faellt das hier auf, statt dass
    /// <c>--ignore-scripts</c> es still auslaesst.
    /// </summary>
    [Fact]
    public void AndroidWorkflow_BuildsUnsignedWithBubblewrapFromTheLockfile()
    {
        var build = AndroidJobs()["build"];
        var lockfile = ReadRepoFile("twa/bubblewrap/package-lock.json");

        Assert.Contains("npm ci --ignore-scripts", build);
        Assert.DoesNotContain("npm install", build);
        Assert.Contains("working-directory: twa/bubblewrap", build);
        Assert.Contains("bubblewrap build --skipPwaValidation --skipSigning", build);
        Assert.Contains("\"@bubblewrap/cli\": \"1.25.0\"", ReadRepoFile("twa/bubblewrap/package.json"));
        Assert.Matches(@"""node_modules/@bubblewrap/cli"":\s*\{\s*""version"":\s*""1\.25\.0""", lockfile);
        Assert.DoesNotContain("\"hasInstallScript\"", lockfile);
    }

    /// <summary>
    /// Jede Action per Commit-SHA (ein Tag wie <c>@v2</c> kann umgehaengt werden, Muster
    /// tj-actions 2025), und kein Checkout hinterlaesst das GITHUB_TOKEN fuer spaetere Schritte.
    /// </summary>
    [Fact]
    public void AndroidWorkflow_PinsActionsBySha_AndDropsCheckoutCredentials()
    {
        var text = ReadRepoFile(Android);
        var uses = Regex.Matches(text, @"(?m)^\s*(?:- )?uses:\s*(\S+)").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(uses);
        foreach (var use in uses)
            Assert.Matches(@"^[\w.-]+/[\w.-]+@[0-9a-f]{40}$", use);

        var checkouts = Regex.Matches(text, @"(?ms)uses:\s*actions/checkout@\S+[^\n]*\n(?<with>(?:[ ]{8,}\S[^\n]*\n)*)");
        Assert.NotEmpty(checkouts);
        foreach (Match checkout in checkouts)
            Assert.Contains("persist-credentials: false", checkout.Groups["with"].Value);
    }

    /// <summary>Ein Job-Block aus docker.yml, von seiner Zeile bis zum naechsten Job.</summary>
    private static string Job(string name)
    {
        var block = Regex.Match(ReadRepoFile(Docker), $@"(?ms)^  {Regex.Escape(name)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;
        Assert.True(block.Length > 0, $"docker.yml kennt keinen Job '{name}'");
        return block;
    }

    /// <summary>Ein Job-Block aus test.yml, von seiner Zeile bis zum naechsten Job.</summary>
    private static string TestJob(string name)
    {
        var block = Regex.Match(ReadRepoFile(Tests), $@"(?ms)^  {Regex.Escape(name)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;
        Assert.True(block.Length > 0, $"test.yml kennt keinen Job '{name}'");
        return block;
    }

    /// <summary>
    /// Der Crawler-Job im Gate prueft den VERTRAG gegen den zuletzt getaggten Crawler (Codereview
    /// I2-010) — nicht dessen eigene Test-Suite gegen main-HEAD: ein roter Crawler-Commit hielt so
    /// jedes rookhub-Image an, und ein echter Vertragsbruch blieb gruen. Und er setzt CRAWLER_REPO,
    /// sonst ueberspraenge sich der Abgleich gegen die Crawler-DTOs still.
    /// </summary>
    [Fact]
    public void CrawlerJob_ChecksTheContractAgainstTheLastCrawlerTag()
    {
        Assert.DoesNotContain("ChessResultsCrawler.Tests", ReadRepoFile(Tests));

        var job = TestJob("test-crawler-contract");
        Assert.Contains("repository: ${{ github.repository_owner }}/chessresults_crawler", job);
        Assert.Contains("fetch-depth: 0", job);
        Assert.Matches(@"git tag --list 'v\[0-9\]\*\.\[0-9\]\*\.\[0-9\]\*' --sort=-v:refname", job);
        Assert.Contains("git checkout --quiet \"$tag\"", job);
        Assert.Contains("--filter \"FullyQualifiedName~CrawlerContractTests\"", job);
        Assert.Matches(@"CRAWLER_REPO: \$\{\{ github\.workspace \}\}/_crawler", job);
        Assert.Contains("path: _crawler", job);
    }

    /// <summary>
    /// Der Vertrags-Job laeuft, wenn sich ein LESER aendert — auch die goldenen Dateien samt Test
    /// und die Frontend-Interfaces, die der Test gegen sie haelt.
    /// </summary>
    [Fact]
    public void CrawlerFilter_CoversEveryReaderOfTheContract()
    {
        var block = Regex.Match(ReadRepoFile(Filters), @"(?ms)^crawler:\s*$(.*?)(?=^\S|\z)").Groups[1].Value;

        Assert.Contains("'src/api/**'", block);
        Assert.Contains("'tests/RookHub.Api.Tests/**'", block);
        Assert.Contains("'src/frontend/app/src/app/core/models.ts'", block);
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
        var step = Regex.Match(text, @"(?ms)^      - uses: dorny/paths-filter@[^\n]*$(.*?)(?=^      - |^  \S|\z)")
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

    private const string E2e = ".github/workflows/e2e.yml";
    private const string Audit = ".github/workflows/audit.yml";

    /// <summary>Ein Job-Block aus einem beliebigen Workflow, von seiner Zeile bis zum naechsten Job.</summary>
    private static string WorkflowJob(string workflow, string name)
    {
        var block = Regex.Match(ReadRepoFile(workflow), $@"(?ms)^  {Regex.Escape(name)}:\s*$(.*?)(?=^  [a-z]|\z)").Groups[1].Value;
        Assert.True(block.Length > 0, $"{workflow} kennt keinen Job '{name}'");
        return block;
    }

    /// <summary>Ein Schritt (ab „- name:" bzw. „- id:"-Zeile) bis zum naechsten Schritt oder Job.</summary>
    private static string Step(string jobBlock, string marker)
    {
        var block = Regex.Match(jobBlock, $@"(?ms)^      - {marker}\s*$(.*?)(?=^      - |^  \S|\z)").Groups[1].Value;
        Assert.True(block.Length > 0, $"Schritt '{marker}' fehlt");
        return block;
    }

    /// <summary>
    /// Der naechtliche E2E-Lauf braucht einen EMPFAENGER (Codereview I1-014). Ab 4be56d1b (04.09.)
    /// war er neun Naechte rot, bis es am 13.09. jemand bemerkte — die Actions-Oberflaeche allein
    /// meldet nichts. Der Melde-Schritt steht NACH dem Lauf (sonst sieht failure() ihn nicht), nimmt
    /// den Abbruch am Job-Timeout mit und meldet nur den Nachtlauf.
    /// </summary>
    [Fact]
    public void NightlyE2e_ReportsARedRun()
    {
        var text = ReadRepoFile(E2e);
        Assert.Matches(@"(?m)^  schedule:\s*$", text);

        var job = WorkflowJob(E2e, "e2e");
        var step = Step(job, "name: Roten Nachtlauf melden");
        Assert.Contains("if: (failure() || cancelled()) && github.event_name == 'schedule'", step);
        Assert.Contains("WEBHOOK: ${{ secrets.DISCORD_CI_WEBHOOK }}", step);
        Assert.Contains("bash .github/scripts/ci_alert.sh", step);
        Assert.True(job.IndexOf("bash scripts/e2e.sh", StringComparison.Ordinal)
                    < job.IndexOf("Roten Nachtlauf melden", StringComparison.Ordinal),
            "der Melde-Schritt muss NACH dem E2E-Lauf stehen");

        Assert.True(File.Exists(Path.Combine(RepoRoot(), ".github", "scripts", "ci_alert.sh")),
            ".github/scripts/ci_alert.sh fehlt");
    }

    /// <summary>
    /// Der Audit bleibt nicht blockierend (PD-016), darf aber nicht per Konstruktion gruen sein
    /// (Codereview I1-014): vorher pipte jeder Scan in `tee` ohne pipefail — der Schritt war nie rot,
    /// und gelesen hat die Summary niemand. Jetzt endet ein Scan mit Befund rot (continue-on-error
    /// haelt den Job gruen), sein outcome wandert als Job-Output nach `melden`, und der meldet den
    /// Montagslauf nach Discord.
    /// </summary>
    [Theory]
    [InlineData("nuget")]
    [InlineData("npm")]
    [InlineData("pip")]
    public void AuditScan_CanTurnRed_AndHandsItsOutcomeOn(string job)
    {
        var block = WorkflowJob(Audit, job);
        Assert.Contains("befund: ${{ steps.scan.outcome }}", block);

        var scan = Regex.Match(block, @"(?ms)^      - name: [^\n]*\n        id: scan\s*$(.*?)(?=^      - |^  \S|\z)").Groups[1].Value;
        Assert.True(scan.Length > 0, $"audit.yml/{job}: kein Schritt mit id: scan");
        Assert.Contains("continue-on-error: true", scan);
        Assert.Contains("shell: bash", scan);
        Assert.Contains("::warning", scan);
        Assert.Contains("exit 1", scan);
        Assert.DoesNotContain("} | tee", scan);
    }

    /// <summary>
    /// Echte Ausgabe von <c>dotnet list package --vulnerable --include-transitive</c> (SDK 10.0.301).
    /// Das erste Advisory eines Pakets steht in der Zeile mit „&gt;“, jedes weitere als Folgezeile OHNE
    /// „&gt;“ darunter — und NICHT nach Schwere sortiert.
    /// </summary>
    private static readonly string[] NuGetFollowUpHigh =
    [
        "Project `p` has the following vulnerable packages",
        "   [net10.0]: ",
        "   Top-level Package          Requested   Resolved   Severity   Advisory URL                                     ",
        "   > System.Net.Security      4.0.0       4.0.0      Moderate   https://github.com/advisories/GHSA-ch6p-4jcm-h8vh",
        "                                                     High       https://github.com/advisories/GHSA-6xh7-4v2w-36q6",
        "                                                     High       https://github.com/advisories/GHSA-qhqf-ghgh-x2m4",
        "                                                     Moderate   https://github.com/advisories/GHSA-j8f4-2w4p-mhjc",
    ];

    private static readonly string[] NuGetCritical =
    [
        "Project `p` has the following vulnerable packages",
        "   [net10.0]: ",
        "   Top-level Package                Requested   Resolved   Severity   Advisory URL                                     ",
        "   > System.Text.Encodings.Web      4.5.0       4.5.0      Critical   https://github.com/advisories/GHSA-ghhp-997w-qr28",
    ];

    private static readonly string[] NuGetModerateOnly =
    [
        "Project `p` has the following vulnerable packages",
        "   [net10.0]: ",
        "   Top-level Package                      Requested   Resolved   Severity   Advisory URL                                     ",
        "   > System.IdentityModel.Tokens.Jwt      6.24.0      6.24.0     Moderate   https://github.com/advisories/GHSA-59j7-ghrg-fj52",
        "",
        "   Transitive Package                           Resolved   Severity   Advisory URL                                     ",
        "   > Microsoft.IdentityModel.JsonWebTokens      6.24.0     Moderate   https://github.com/advisories/GHSA-59j7-ghrg-fj52",
    ];

    private static readonly string[] NuGetClean =
    [
        "The given project `p` has no vulnerable packages given the current sources.",
    ];

    /// <summary>
    /// Das Erkennungsmuster des NuGet-Scans fachlich, nicht nur strukturell (Codereview I1-014, Nacharbeit):
    /// das erste Muster pruefte nur die „&gt;“-Zeile eines Pakets und uebersah ein High, das als Folgezeile
    /// darunter stand — der Scan endete gruen, gemeldet wurde nichts. Das Muster wird aus audit.yml gelesen
    /// und wie <c>grep -E</c> Zeile fuer Zeile auf echte Tabellen angewendet.
    /// </summary>
    [Fact]
    public void NuGetScan_FindsHighOrCritical_InEveryAdvisoryRow()
    {
        var scan = Regex.Match(WorkflowJob(Audit, "nuget"), @"(?ms)^      - name: [^\n]*\n        id: scan\s*$(.*?)(?=^      - |^  \S|\z)").Groups[1].Value;
        var patterns = Regex.Matches(scan, @"grep -Eq '([^']+)' <<<""\$out""").Select(m => m.Groups[1].Value).ToList();
        Assert.True(patterns.Count == 1, $"audit.yml/nuget: genau ein Schwere-Muster erwartet, gefunden {patterns.Count}");
        var severe = new Regex(patterns[0]);

        bool Hits(string[] lines) => lines.Any(severe.IsMatch);

        Assert.True(Hits(NuGetFollowUpHigh), "High als Folgezeile ohne „>“ muss den Scan rot machen");
        Assert.True(Hits(NuGetCritical), "Critical muss den Scan rot machen");
        Assert.False(Hits(NuGetModerateOnly), "nur Moderate darf den Scan nicht rot machen");
        Assert.False(Hits(NuGetClean), "ohne Befund darf der Scan nicht rot werden");
    }

    [Fact]
    public void AuditFinding_IsReportedOnTheWeeklyRun()
    {
        var block = WorkflowJob(Audit, "melden");
        Assert.Contains("needs: [nuget, npm, pip]", block);
        Assert.Contains("always()", block);
        Assert.Contains("github.event_name == 'schedule'", block);
        foreach (var job in new[] { "nuget", "npm", "pip" })
            Assert.Contains($"needs.{job}.outputs.befund != 'success'", block);
        Assert.Contains("WEBHOOK: ${{ secrets.DISCORD_CI_WEBHOOK }}", block);
        Assert.Contains("bash .github/scripts/ci_alert.sh", block);
    }

    /// <summary>
    /// Jedes Image traegt den Commit, aus dem es gebaut ist (Codereview I1-017). Bis dahin bekamen nur die
    /// Frontend-Images GIT_SHA/GIT_REF — das API-Image meldete seinen Stand nirgends, die Admin-CI-Seite
    /// markierte fuer rookhub den Frontend-Lauf, und ein ausgebliebener API-Build (Dev am 09.09. auf 0.452.1)
    /// war dort unsichtbar. Die build-args stehen im VORBAU: hinter dem Gate wird nicht mehr gebaut.
    /// </summary>
    [Theory]
    [InlineData("prebuild-api")]
    [InlineData("prebuild-frontend")]
    [InlineData("prebuild-turnier")]
    [InlineData("prebuild-kidhub")]
    [InlineData("prebuild-leaguehub")]
    [InlineData("prebuild-clubhub")]
    public void EveryPrebuildJob_StampsTheCommitIntoTheImage(string job)
    {
        var block = Job(job);
        Assert.Contains("GIT_SHA=${{ github.sha }}", block);
        Assert.Contains("GIT_REF=${{ github.ref_name }}", block);
    }

    /// <summary>Das API-Image legt die build-args in seine Umgebung — dort liest
    /// <c>GithubActionsService.OwnApiBuild</c> sie.</summary>
    [Fact]
    public void ApiImage_CarriesItsCommitInTheEnvironment()
    {
        var dockerfile = ReadRepoFile("src/api/RookHub.Api/Dockerfile");
        Assert.Matches(@"(?m)^ARG GIT_SHA=", dockerfile);
        Assert.Matches(@"(?m)^ARG GIT_REF=", dockerfile);
        Assert.Matches(@"BUILD_GIT_SHA=\$GIT_SHA\b", dockerfile);
        Assert.Matches(@"BUILD_GIT_REF=\$GIT_REF\b", dockerfile);
    }

    private const string FrontendApp = "src/frontend/app";

    /// <summary>
    /// Der Frontend-Linter (Codereview F8-004) laeuft im Gate — und blockiert in PHASE 1 nichts. Vorher
    /// gab es weder Linter noch Formatter; Default- neben OnPush-CD, Konstruktor-DI neben inject(),
    /// @Input() neben input() wuchsen ungezaehlt weiter. Der Schritt zaehlt die Warnungen je Regel in
    /// die Job-Summary; solange er per continue-on-error nicht blockiert, darf eslint.config.mjs keine
    /// Regel auf 'error' stellen (sonst waere ein roter Lint-Schritt Alltag und niemand saehe hin).
    /// Phase 2 (Ratsche) aendert diesen Test bewusst mit.
    /// </summary>
    [Fact]
    public void FrontendLint_RunsInTheGate_AndOnlyWarns()
    {
        var job = TestJob("build-frontend");
        var step = Step(job, Regex.Escape("name: ESLint (nur Warnungen, Zaehler in der Job-Summary)"));
        Assert.Contains("working-directory: src/frontend/app", step);
        Assert.Contains("continue-on-error: true", step);
        Assert.Contains("npx eslint . --format json", step);
        Assert.Contains("GITHUB_STEP_SUMMARY", step);
        Assert.True(job.IndexOf("run: npm ci", StringComparison.Ordinal)
                    < job.IndexOf("name: ESLint", StringComparison.Ordinal),
            "der Lint-Schritt braucht node_modules — er muss NACH npm ci stehen");

        var package = ReadRepoFile($"{FrontendApp}/package.json");
        Assert.Contains("\"lint\": \"eslint .\"", package);
        var lockfile = ReadRepoFile($"{FrontendApp}/package-lock.json");
        foreach (var pkg in new[] { "eslint", "angular-eslint", "typescript-eslint" })
        {
            Assert.Matches($@"""devDependencies"":\s*\{{[^}}]*""{Regex.Escape(pkg)}"":", package);
            Assert.Contains($"\"node_modules/{pkg}\": {{", lockfile);
        }

        var config = ReadRepoFile($"{FrontendApp}/eslint.config.mjs");
        Assert.Contains("files: ['src*/**/*.ts']", config);
        foreach (var rule in new[]
                 {
                     "@angular-eslint/prefer-on-push-component-change-detection",
                     "@angular-eslint/prefer-inject",
                     "@angular-eslint/prefer-signals",
                     "@typescript-eslint/no-explicit-any",
                     "'no-console'",
                     "'no-empty'",
                 })
            Assert.Contains(rule, config);
        Assert.DoesNotMatch(@"(?m)^\s*'[^']+':\s*\[?\s*(?:'error'|2\b)", config);
    }

    /// <summary>Alle Workflow-Dateien, als Repo-Pfad.</summary>
    private static List<string> AllWorkflows()
    {
        var files = Directory.GetFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml")
            .Select(p => ".github/workflows/" + Path.GetFileName(p))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);
        return files;
    }

    /// <summary>Die Jobs eines Workflows: Name → Block bis zum naechsten Job (ohne Kommentarzeilen).</summary>
    private static Dictionary<string, string> JobsOf(string workflow)
    {
        var text = ReadRepoFile(workflow);
        var jobs = text[text.IndexOf("\njobs:", StringComparison.Ordinal)..];
        return Regex.Matches(jobs, @"(?ms)^  (?<name>[a-z][a-z0-9-]*):\s*$(?<body>.*?)(?=^  [a-z]|\z)")
            .ToDictionary(
                m => m.Groups["name"].Value,
                m => string.Join('\n', m.Groups["body"].Value.Split('\n').Where(l => !l.TrimStart().StartsWith('#'))));
    }

    /// <summary>
    /// Gemeinsame Form aller Workflows, Teil 1 (Codereview I1-018): Rechte stehen NUR am Job, oben steht
    /// <c>permissions: {}</c>. Vorher setzten 5 von 6 Dateien oben nichts, zwei ihrer Jobs auch nicht —
    /// sie lebten von der Repo-Vorgabe <c>contents: read</c>. Stellt die jemand auf „read and write",
    /// haette jeder dieser Jobs (npm-Baum, Fremd-Actions) still Schreibrecht bekommen. Mit <c>{}</c> oben
    /// und einem Block je Job ist das Recht jedes Jobs in der Datei ablesbar und unabhaengig von der
    /// Einstellung. Ein neuer Job ohne eigenen Block faellt hier auf (und liefe sonst ganz ohne Rechte).
    /// </summary>
    [Fact]
    public void EveryWorkflow_GrantsRightsOnlyPerJob()
    {
        var problems = new List<string>();
        foreach (var workflow in AllWorkflows())
        {
            var text = ReadRepoFile(workflow);
            var topLevel = Regex.Matches(text, @"(?m)^permissions:.*$").Select(m => m.Value.TrimEnd()).ToList();
            if (topLevel is not ["permissions: {}"])
                problems.Add($"{workflow}: oben muss genau 'permissions: {{}}' stehen, gefunden: [{string.Join(" | ", topLevel)}]");

            foreach (var (job, body) in JobsOf(workflow))
            {
                if (!Regex.IsMatch(body, @"(?m)^    permissions:"))
                    problems.Add($"{workflow}: Job '{job}' nennt keine eigenen Rechte");
                if (Regex.IsMatch(body, @"(?m)^    permissions:\s*(?:write-all|read-all)\s*$"))
                    problems.Add($"{workflow}: Job '{job}' nimmt Pauschalrechte (write-all/read-all)");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Gemeinsame Form aller Workflows, Teil 2 (Codereview I1-018): jede Fremd-Action per Commit-SHA mit
    /// der Version als Kommentar (ein Tag wie <c>@v7</c> kann umgehaengt werden, Muster tj-actions 2025 —
    /// und docker.yml pusht mit <c>packages: write</c>), und dieselbe Action in ALLEN Dateien auf demselben
    /// SHA. Vorher pinnte nur android-twa.yml per SHA, die uebrigen 51 Stellen per Tag, und
    /// changelog-discord.yml lief noch auf checkout@v4 neben v6 ueberall sonst.
    /// </summary>
    [Fact]
    public void EveryWorkflow_PinsEachActionToOneSha()
    {
        var problems = new List<string>();
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var workflow in AllWorkflows())
            foreach (Match m in Regex.Matches(ReadRepoFile(workflow), @"(?m)^\s*(?:- )?uses:\s*(?<ref>\S+)(?<rest>[^\n]*)$"))
            {
                var reference = m.Groups["ref"].Value;
                if (reference.StartsWith("./", StringComparison.Ordinal))
                    continue;
                var pinned = Regex.Match(reference, @"^(?<action>[\w.-]+/[\w.-]+(?:/[\w.-]+)*)@(?<sha>[0-9a-f]{40})$");
                var version = Regex.Match(m.Groups["rest"].Value, @"^\s+#\s*(?<v>v\d+(?:\.\d+)*)\s*$");
                if (!pinned.Success || !version.Success)
                {
                    problems.Add($"{workflow}: '{reference}{m.Groups["rest"].Value}' ist nicht 'owner/action@<40-stelliger SHA> # vX.Y.Z'");
                    continue;
                }
                var action = pinned.Groups["action"].Value;
                if (!seen.TryGetValue(action, out var pins))
                    seen[action] = pins = new HashSet<string>(StringComparer.Ordinal);
                pins.Add($"{pinned.Groups["sha"].Value} # {version.Groups["v"].Value}");
            }

        foreach (var (action, pins) in seen.Where(kv => kv.Value.Count > 1))
            problems.Add($"{action} steht auf verschiedenen Staenden: {string.Join(" / ", pins)}");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Eine Node-Version fuer alle Workflows — dieselbe Hauptversion wie das Frontend-Image (Codereview
    /// I1-018). android-twa.yml baute noch mit Node 20 (seit April 2026 ohne Support), alle anderen mit 24.
    /// </summary>
    [Fact]
    public void EveryWorkflow_UsesTheNodeVersionOfTheFrontendImage()
    {
        var image = Regex.Match(ReadRepoFile("src/frontend/Dockerfile"), @"(?m)^FROM node:(?<major>\d+)\b");
        Assert.True(image.Success, "src/frontend/Dockerfile: keine Zeile 'FROM node:<Version>'");

        var versions = AllWorkflows()
            .SelectMany(w => Regex.Matches(ReadRepoFile(w), @"(?m)^\s*node-version:\s*'?(?<v>[^'\s]+)'?\s*$")
                .Select(m => $"{w}: {m.Groups["v"].Value}"))
            .ToList();
        Assert.NotEmpty(versions);
        foreach (var entry in versions)
            Assert.True(entry.EndsWith($": {image.Groups["major"].Value}", StringComparison.Ordinal),
                $"node-version weicht vom Frontend-Image (node:{image.Groups["major"].Value}) ab — {entry}");
    }
}
