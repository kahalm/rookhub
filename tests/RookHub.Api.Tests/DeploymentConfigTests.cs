using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Wacht ueber Deployment-/CI-Dateien, die kein Compiler prueft. Alle drei Punkte hier
/// waren echte Fehler: Frontend-Port 80 in den Beispiel-Composes (nginx lauscht als
/// non-root auf 8080 -> toter Stack), ein 9.0.x-SDK-Pin fuer net10-Projekte (lief nur
/// zufaellig ueber das vorinstallierte Runner-SDK) und ein Floating-NuGet-Range.
/// </summary>
public class DeploymentConfigTests
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

    [Theory]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void ExampleCompose_MapsFrontendToContainerPort8080(string file)
    {
        var text = ReadRepoFile(file);

        Assert.Contains("${FRONTEND_PORT}:8080", text);
        Assert.DoesNotContain("${FRONTEND_PORT}:80\"", text);
    }

    [Theory]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void ExampleCompose_CarriesDataProtectionVolumeAndNewerSettings(string file)
    {
        var text = ReadRepoFile(file);

        // Ohne persistentes /keys sind DataProtection-Keys nach jedem Neustart weg.
        Assert.Contains("dataprotection-keys:/keys", text);
        Assert.Contains("Encryption__Key:", text);
        Assert.Contains("App__BaseUrl:", text);
        Assert.Contains("Email__SmtpHost:", text);
        Assert.Contains("Discord__LinkSecret:", text);
    }

    /// <summary>Die Dateien, mit denen wirklich deployed wird (Kopfzeile: <c>docker compose -f …</c>)
    /// — der frühere Test sah NUR die <c>*.example</c>-Dateien, und genau dort driftete es
    /// auseinander: der <c>:?</c>-Startschutz des Encryption-Keys stand im Beispiel, in den echten
    /// Dateien nicht.</summary>
    [Theory]
    [InlineData("compose.vpn.yml")]
    [InlineData("compose.dev.yml")]
    [InlineData("compose.dev.vpn.yml")]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void EveryCompose_GuardsEncryptionKey_AndPassesOptionalSecrets(string file)
    {
        var text = ReadRepoFile(file);

        // Leerer Encryption-Key = Schein-Verschlüsselung mit SHA256("") → Start muss abbrechen,
        // und zwar mit einer COMPOSE-Meldung statt einer Neustartschleife des Containers.
        Assert.Contains("Encryption__Key: ${ENCRYPTION_KEY:?", text);
        // Dasselbe für den JWT-Schlüssel: leer heißt hier nicht „Feature aus", sondern
        // unsignierbare Tokens — der Abbruch gehört in `docker compose`, nicht in eine
        // Neustartschleife des Containers.
        Assert.Contains("Jwt__Key: ${JWT_KEY:?", text);
        // Fail-closed-Endpoints brauchen ihr Secht im Container, sonst bleibt der Admin-CI-Tab
        // ohne Push-Daten und der GitHub-Webhook antwortet still 401.
        Assert.Contains("CI__BuildReportSecret:", text);
        Assert.Contains("CI__GithubWebhookSecret:", text);
        // „Leer = Feature aus" gilt nur, wenn die Variable den Container überhaupt erreicht.
        Assert.Contains("WebPush__PublicKey:", text);
        Assert.Contains("GitHub__Token:", text);
        Assert.Contains("Kibana__Url:", text);
        // Der Log-Sink darf den API-Start nicht blockieren (ES rot ⇒ App startet trotzdem).
        // Nur im api-Block geprüft: Kibana braucht ein gesundes Elasticsearch zu Recht. Die
        // Beispiel-Stacks bringen gar kein ES mit (externe URL) — dort gibt es nichts zu prüfen.
        var api = ServiceBlock(text, "api");
        Assert.DoesNotContain("""
      elasticsearch:
        condition: service_healthy
""", api);
    }

    /// <summary>Schneidet EINEN Service-Block (<c>  name:</c> bis zum nächsten Eintrag derselben
    /// Einrückung) aus einer Compose-Datei — damit eine Aussage über den api-Service nicht von den
    /// Nachbar-Services (Kibana!) beantwortet wird.</summary>
    private static string ServiceBlock(string compose, string service)
    {
        var start = compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Service '{service}' nicht gefunden");
        start++;
        var next = Regex.Match(compose[(start + 1)..], @"^  [A-Za-z0-9_-]+:$", RegexOptions.Multiline);
        return next.Success ? compose.Substring(start, next.Index + 1) : compose[start..];
    }

    /// <summary>
    /// Ohne Test-Tor pushen die Image-Jobs ohne einen einzigen Test — und Watchtower zieht die
    /// Images in derselben Nacht.
    ///
    /// <para>Geprueft wird der NAME `tests` in `needs`, nicht mehr die Zeile `needs: tests`:
    /// seit 0.434.2 steht dort die Listenform `needs: [changes, tests]`, und der alte
    /// Zeichenketten-Vergleich schlug damit fehl, obwohl das Tor genau da war. Was die
    /// Image-Jobs mit dem Ergebnis machen, pruefen die feineren Faelle in
    /// <c>CiWorkflowTests</c>.</para>
    /// </summary>
    [Fact]
    public void DockerWorkflow_KeepsTheTestGate()
    {
        var text = ReadRepoFile(".github/workflows/docker.yml");

        Assert.Matches(@"needs:\s*(tests\b|\[[^\]]*\btests\b)", text);
    }

    [Fact]
    public void TestWorkflow_PinsDotnet10_AndRunsFrontendSpecs()
    {
        var text = ReadRepoFile(".github/workflows/test.yml");

        Assert.DoesNotContain("dotnet-version: '9.", text);
        Assert.Contains("dotnet-version: '10.0.x'", text);
        // Ohne diesen Schritt liefe im Image-Gate kein einziger Frontend-Spec.
        Assert.Contains("ng test", text);
        // Die drei engine-provider-Tests liefen zuvor in KEINEM Workflow — ein Bump von
        // PROVIDER_SHA oder ein Umbau von patch_provider.py/entrypoint.sh war damit ungeprüft.
        Assert.Contains("test/entrypoint.test.sh", text);
        Assert.Contains("test/supervisor.test.sh", text);
        Assert.Contains("test/heartbeat.test.py", text);
    }

    [Fact]
    public void AuditWorkflow_ScansAllThreeEcosystems()
    {
        var text = ReadRepoFile(".github/workflows/audit.yml");

        Assert.Contains("--vulnerable", text);
        Assert.Contains("npm audit", text);
        Assert.Contains("pip-audit", text);
        // Muss melden, nicht blockieren — sonst faellt der Release-Pfad auf ein
        // fremdes Advisory herein.
        Assert.Contains("continue-on-error: true", text);
    }

    [Fact]
    public void TwaWorkflow_RefusesToInventTheReleaseTag()
    {
        var text = ReadRepoFile(".github/workflows/android-twa.yml");

        // action-gh-release wuerde einen fehlenden Tag selbst anlegen; fuer den
        // wird dann nie ein :latest-Image gebaut.
        Assert.Contains("git ls-remote --exit-code --tags origin", text);
    }

    [Fact]
    public void ApiProject_HasNoFloatingPackageVersions()
    {
        var text = ReadRepoFile("src/api/RookHub.Api/RookHub.Api.csproj");

        var floating = Regex.Matches(text, "<PackageReference[^>]*Version=\"([^\"]*[*][^\"]*)\"")
            .Select(m => m.Value)
            .ToList();

        Assert.True(floating.Count == 0,
            "Floating-Versionen ziehen bei jedem Restore unbemerkt neue Pakete: "
            + string.Join(", ", floating));
    }

    /// <summary>
    /// Der Healthcheck des api-Dienstes muss ein Werkzeug aufrufen, das das API-Image wirklich
    /// installiert. Das Image baut auf <c>aspnet</c> (Debian) und bringt per <c>apt-get install</c>
    /// nur <c>curl</c> mit — ein <c>wget</c>-Healthcheck scheitert dort bei JEDEM Versuch, der
    /// Container gilt nie als gesund, und alles, was per <c>service_healthy</c> auf ihn wartet,
    /// startet nicht.
    ///
    /// <para>So geschehen im E2E-Stack: sein Healthcheck rief <c>wget</c> auf, Dev und Prod laengst
    /// <c>curl</c>. Sichtbar wurde es erst, nachdem der Datenbank-Fehler davor behoben war
    /// („dependency failed to start: container e2e-api is unhealthy"). Die <c>wget</c>-Healthchecks
    /// der Frontend- und Crawler-Dienste sind davon nicht betroffen — deren Alpine-Images haben es.</para>
    /// </summary>
    [Theory]
    [InlineData("compose.vpn.yml")]
    [InlineData("compose.dev.yml")]
    [InlineData("compose.dev.vpn.yml")]
    [InlineData("compose.e2e.yml")]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void ApiHealthcheck_UsesAToolTheImageInstalls(string file)
    {
        var api = ServiceBlock(ReadRepoFile(file), "api");
        var werkzeug = Regex.Match(api, "\"CMD-SHELL\",\\s*\"(\\S+)");
        Assert.True(werkzeug.Success, $"{file}: kein CMD-SHELL-Healthcheck im api-Dienst gefunden");

        var dockerfile = ReadRepoFile("src/api/RookHub.Api/Dockerfile");
        var tool = werkzeug.Groups[1].Value;
        Assert.True(Regex.IsMatch(dockerfile, $@"apt-get install[^&]*\b{Regex.Escape(tool)}\b"),
            $"{file}: der api-Healthcheck ruft '{tool}' auf, das API-Image installiert es aber nicht.");
    }

    /// <summary>Auch der E2E-Stack reicht einen Verschluesselungsschluessel durch. Ohne ihn werfen
    /// Analyse-Pumpe und Auftrags-Worker bei jedem Durchlauf „Encryption:Key not configured" — die
    /// API bleibt gesund, aber das Log, in dem man einen echten E2E-Fehler suchen muss, ist voll davon.</summary>
    [Fact]
    public void E2eCompose_PassesAnEncryptionKey()
    {
        Assert.Contains("Encryption__Key: ${ENCRYPTION_KEY:?", ReadRepoFile("compose.e2e.yml"));
        Assert.Matches(@"(?m)^ENCRYPTION_KEY=\S+", ReadRepoFile(".env.e2e"));
    }

    /// <summary>
    /// <c>init-db.sh</c> darf NICHT ausfuehrbar sein. Das offizielle MariaDB-Image FUEHRT eine
    /// ausfuehrbare <c>.sh</c> in <c>docker-entrypoint-initdb.d</c> als eigenen Prozess AUS und
    /// liest nur eine nicht ausfuehrbare per <c>.</c> ein — und nur eingelesen kennt das Skript die
    /// Hilfsfunktion <c>docker_process_sql</c> des Entrypoints. Ausfuehrbar stirbt der Container
    /// beim ersten Start mit „docker_process_sql: command not found" (Exit 127).
    ///
    /// <para>So geschehen: 4be56d1b (2026-09-04) setzte den Modus auf 755 und legte im selben Commit
    /// den naechtlichen E2E-Lauf an — der ist darum an keinem einzigen Tag gruen gewesen. Dev und
    /// Prod traf es nur deshalb nicht, weil ihre Volumes laengst initialisiert sind; ein frisches
    /// Volume waere genauso gescheitert.</para>
    ///
    /// <para>Auf Windows ohne Aussage (dort gibt es keinen Unix-Modus); die CI laeuft auf Linux und
    /// checkt den Modus aus dem Git-Index aus.</para>
    /// </summary>
    [Fact]
    public void InitDbScript_IsSourcedNotExecuted()
    {
        var script = ReadRepoFile("init-db.sh");
        Assert.Contains("docker_process_sql", script);   // der Grund, warum es eingelesen werden muss

        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(Path.Combine(RepoRoot(), "init-db.sh"));
        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        Assert.True((mode & execute) == 0,
            $"init-db.sh ist ausfuehrbar ({mode}) — MariaDB fuehrt es dann aus statt es einzulesen, und docker_process_sql fehlt.");
    }

    [Fact]
    public void OperationsScripts_AreShipped()
    {
        // Backup + Log-Retention gab es lange gar nicht — hier festnageln, damit sie
        // nicht wieder still verschwinden.
        Assert.Contains("mariadb-dump", ReadRepoFile("scripts/backup-db.sh"));
        Assert.Contains("_ilm/policy", ReadRepoFile("scripts/es_log_retention.py"));
        Assert.Contains("Restore", ReadRepoFile("docs/backup.md"));
    }

    /// <summary>
    /// Der Rundenplan-Schritt des Nachtrags MUSS `retryEmpty` durchreichen — und zwar als
    /// Variable, nicht als festen Wert.
    ///
    /// <para><b>Warum das einen Test wert ist.</b> Ohne den Schalter nimmt der Endpunkt nur
    /// Eintraege ohne `RoundPlanCheckedAt` vor. War das Holen selbst kaputt (auf Dev gemessen:
    /// 452 als geprueft vermerkt, 0 Spieltermine, weil der Parser die Wrapper- statt der
    /// Datentabelle nahm), dann verhindert genau dieser Vermerk jede Wiederholung — fuer immer,
    /// lautlos, und der Kalender zeigt die Liga weiter an 200 spielfreien Tagen. Ein Skript, das
    /// den Schalter nicht anbietet, laesst den Bestand also nicht reparieren.</para>
    /// </summary>
    [Fact]
    public void DirectoryBackfill_PassesRetryEmptyThroughToRoundPlans()
    {
        var text = ReadRepoFile("scripts/directory-backfill.sh");

        Assert.Contains("round-plans?limit=$LIMIT&retryEmpty=$RETRY_EMPTY", text);
    }

    /// <summary>
    /// Und der Schalter ist AUS, solange ihn niemand setzt: jeder erneut vorgenommene Eintrag
    /// kostet einen Seitenabruf, und „kein veroeffentlichter Plan" ist der haeufige Fall. Ein
    /// unbedacht voreingestelltes `true` machte aus dem Nachtrag jedes Mal einen Lauf ueber den
    /// halben Bestand.
    /// </summary>
    [Fact]
    public void DirectoryBackfill_LeavesRetryEmptyOffByDefault()
    {
        var text = ReadRepoFile("scripts/directory-backfill.sh");

        Assert.Contains("RETRY_EMPTY=\"${3:-false}\"", text);
    }

    /// <summary>
    /// Ein Tippfehler im Schalter muss ABBRECHEN, nicht still als „nicht true" durchgehen: der
    /// Lauf saehe erfolgreich aus, taete aber nichts, und auffallen wuerde es erst an der
    /// Datenbank. Geprueft wird VOR der Passwortabfrage.
    /// </summary>
    [Fact]
    public void DirectoryBackfill_RejectsAnUnknownRetryEmptyValue()
    {
        var text = ReadRepoFile("scripts/directory-backfill.sh");

        var check = text.IndexOf("case \"$RETRY_EMPTY\" in", StringComparison.Ordinal);
        var prompt = text.IndexOf("read -rsp", StringComparison.Ordinal);

        Assert.True(check >= 0, "Keine Pruefung von RETRY_EMPTY gefunden.");
        Assert.Contains("true|false)", text);
        Assert.True(check < prompt,
            "Die Pruefung muss vor der Passwortabfrage stehen — sonst tippt man erst das "
            + "Passwort und erfaehrt danach, dass das Argument falsch war.");
    }
}
