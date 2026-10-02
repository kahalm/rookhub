using System.Text;
using System.Text.Json;
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
        // Lochfinder: leer = Nutzer-Token aus dem Profil; ohne die Zeile gäbe es nie einen Server-Token.
        Assert.Contains("LichessExplorer__Token: ${LICHESS_EXPLORER_TOKEN:-}", text);
        Assert.Contains("LichessExplorer__LocalUrl: ${LICHESS_EXPLORER_LOCAL_URL:-}", text);
        // Tipps + Übersetzung über eigene Hardware (DGX Spark) — optional, leer = Claude bzw. aus.
        Assert.Contains("TextLlm__BaseUrl: ${TEXT_LLM_BASE_URL:-}", text);
        Assert.Contains("TextLlm__ApiKey: ${TEXT_LLM_API_KEY:-}", text);
        Assert.Contains("TextLlm__Model: ${TEXT_LLM_MODEL:-}", text);
        // Kurs-Übersetzung automatisch (0.548.0): leer = aus — nur Prod setzt de,en; ohne die Zeile ginge es nie an.
        Assert.Contains("CourseTranslation__AutoLanguages: ${COURSE_TRANSLATION_AUTO_LANGUAGES:-}", text);
        // „Frag die Kommentare" (0.536.0): Embedding-Modell — optional, leer = Suche aus.
        Assert.Contains("Embedding__BaseUrl: ${EMBEDDING_BASE_URL:-}", text);
        Assert.Contains("Embedding__ApiKey: ${EMBEDDING_API_KEY:-}", text);
        Assert.Contains("Embedding__Model: ${EMBEDDING_MODEL:-}", text);
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
    /// Kein veröffentlichter Port ohne Bind-Adresse (Codereview 2026-09-29, I1-003). Ein Eintrag wie
    /// <c>"${DB_EXTERNAL_PORT}:3306"</c> lauscht auf 0.0.0.0 UND [::]: Datenbank und API waren so aus
    /// LAN und VPN direkt erreichbar — an NPM vorbei, mit Passwort-Login als App-Benutzer bzw. root.
    /// Jeder Eintrag nennt deshalb seine Adresse (Variable mit Vorgabe); für Datenbank, API,
    /// Elasticsearch und Kibana ist die Vorgabe der Host selbst. Wer mehr braucht (API für Bot und
    /// NPM auf der LAN-Adresse), setzt die Variable in der .env bewusst.
    /// </summary>
    [Theory]
    [InlineData("compose.vpn.yml")]
    [InlineData("compose.dev.yml")]
    [InlineData("compose.dev.vpn.yml")]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void EveryPublishedPort_NamesABindAddress(string file)
    {
        var text = ReadRepoFile(file);

        var ports = PublishedPorts(text);
        Assert.NotEmpty(ports);
        foreach (var port in ports)
        {
            // Variablen zuerst ausblenden: die Vorgabe in ${X:-127.0.0.1} bringt eigene Doppelpunkte mit.
            var parts = Regex.Replace(port, @"\$\{[^}]*\}", "X").Split(':');
            Assert.True(parts.Length == 3,
                $"{file}: '{port}' hat keine Bind-Adresse und lauscht damit auf allen Interfaces.");
        }

        var mariadb = ServiceBlock(text, "mariadb");
        Assert.Contains("\"${DB_BIND:-127.0.0.1}:${DB_EXTERNAL_PORT}:3306\"", mariadb);
        // Ohne die Variable legt das Image root@'%' an: root-Login von überall, wo der Port hinreicht.
        Assert.Contains("MARIADB_ROOT_HOST: ${MARIADB_ROOT_HOST:-localhost}", mariadb);
        // init-db.sh braucht die Adressbereiche der App-Benutzer im Container (leer = seine Vorgabe).
        Assert.Contains("DB_APP_HOSTS: ${DB_APP_HOSTS:-}", mariadb);
        Assert.Contains("\"${API_BIND:-127.0.0.1}:${API_PORT}:8080\"", ServiceBlock(text, "api"));
        // Elasticsearch (ohne Security, schreibbar) und Kibana (ohne Login) nicht mehr mit Vorgabe 0.0.0.0.
        Assert.DoesNotMatch(@"\$\{(ES|KIBANA)_BIND:-0\.0\.0\.0\}", text);
    }

    /// <summary>
    /// MariaDB läuft auf einer EXAKTEN Minor-Version, nicht auf dem gleitenden Major-Tag
    /// (Codereview 2026-09-29, Begleitteil zu I1-002). Watchtower zieht nachts um 02:00 jedes neue
    /// Image; mit <c>mariadb:11</c> wäre ein Sprung auf 11.9 ein ungeplantes Upgrade des
    /// Datenverzeichnisses ohne Weg zurück — und der einzige Dump läuft erst danach.
    /// </summary>
    [Theory]
    [InlineData("compose.vpn.yml")]
    [InlineData("compose.dev.yml")]
    [InlineData("compose.dev.vpn.yml")]
    [InlineData("compose.yml.example")]
    [InlineData("compose.vpn.example")]
    public void MariaDbImage_IsPinnedToAMinorVersion(string file)
    {
        var mariadb = ServiceBlock(ReadRepoFile(file), "mariadb");
        Assert.Matches(@"(?m)^    image: mariadb:\d+\.\d+(\.\d+)?\s*$", mariadb);
    }

    /// <summary>Die Einträge unter jedem <c>ports:</c>-Schlüssel (Kurzform), ohne Anführungszeichen
    /// und angehängten Kommentar. Kommentarzeilen im Block werden übersprungen; ein Eintrag in
    /// Langform (<c>- target: …</c>) kommt als „target: …" zurück und fällt damit im Test auf.</summary>
    private static List<string> PublishedPorts(string compose)
    {
        var result = new List<string>();
        int? portsIndent = null;
        foreach (var raw in compose.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var content = line.Trim();
            if (content.Length == 0 || content.StartsWith('#')) continue;
            var indent = line.Length - line.TrimStart().Length;

            if (portsIndent is int blockIndent)
            {
                if (indent >= blockIndent && content.StartsWith("- ", StringComparison.Ordinal))
                {
                    var entry = content[2..];
                    var comment = entry.IndexOf(" #", StringComparison.Ordinal);
                    if (comment >= 0) entry = entry[..comment];
                    result.Add(entry.Trim().Trim('"', '\''));
                    continue;
                }
                if (indent > blockIndent) continue;   // Folgezeile eines Langform-Eintrags
                portsIndent = null;
            }

            if (content == "ports:") portsIndent = indent;
        }
        return result;
    }

    /// <summary>
    /// I1-003: Die App-Benutzer hießen '…'@'%' — Anmeldung von jeder Adresse, die den Port erreicht.
    /// Sie gelten jetzt nur für die Adressbereiche der Docker-Netze (<c>DB_APP_HOSTS</c>; das
    /// erzeugte SQL prüft <c>scripts/tests/test_init_db.sh</c>). Die unbenutzte init-db.sql mit den
    /// Standardpasswörtern der Anfangszeit und GRANT ALL ist weg — die Git-Historie behält sie, die
    /// Passwörter bestehender Installationen gehören deshalb abgeglichen.
    /// </summary>
    [Fact]
    public void InitDb_RestrictsAppUsersToTheDockerNetworks_AndTheOldSeedIsGone()
    {
        var script = ReadRepoFile("init-db.sh");
        // Nur der Code zählt — der Kommentar darf erklären, wie man ein altes '%'-Konto loswird.
        var code = string.Join('\n', script.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));

        Assert.DoesNotContain("@'%'", code);
        Assert.Contains("DB_APP_HOSTS", code);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "init-db.sql")),
            "init-db.sql ist zurück — sie trug öffentliche Standardpasswörter mit GRANT ALL und wird von nichts benutzt.");
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
        // Die engine-provider-Tests liefen zuvor in KEINEM Workflow — ein Bump von PROVIDER_SHA oder
        // ein Umbau von entrypoint.sh war damit ungeprüft. provider.test.py hält den Vertrag mit dem
        // Broker fest (bestmove am Suchende, Lebenszeichen): genau daran hing der Pin bis 0.478.10.
        Assert.Contains("test/entrypoint.test.sh", text);
        Assert.Contains("test/supervisor.test.sh", text);
        Assert.Contains("test/provider.test.py", text);
        // Der eine Eingriff in den Provider (frische Verbindung je Upload) wird VOR dem Vertragstest
        // angewandt — sonst prüft die CI einen anderen Provider als das Image ausliefert.
        Assert.Contains("patch_force_close.py", text);
        Assert.DoesNotContain("heartbeat.test.py", text);
    }

    /// <summary>
    /// Die Wege OHNE Docker (README Linux/macOS + Windows, <c>windows/run_provider.ps1</c>) holen dasselbe Skript wie
    /// das Image — und muessen dieselben drei Sicherungen tragen (Codereview A4-017): die aiohttp-Fassung, die
    /// Pruefsumme des gepinnten Provider-Skripts und <c>patch_force_close.py</c> (frische Verbindung je Upload; ohne
    /// sie endet ein Teil der Suchen nach 15 s mit 503). Die Werte kommen aus dem Dockerfile bzw. (aiohttp, seit
    /// Codereview I1-011) aus <c>engine-provider/requirements.txt</c>, damit ein Pin-Wechsel nur dort beginnt und
    /// hier sofort auffaellt.
    /// </summary>
    [Fact]
    public void EngineProvider_WegeOhneDocker_tragenDieselbenSicherungenWieDasImage()
    {
        var dockerfile = ReadRepoFile("engine-provider/Dockerfile");
        var sha = Regex.Match(dockerfile, @"ARG PROVIDER_SHA=(\S+)").Groups[1].Value;
        var sum = Regex.Match(dockerfile, @"ARG PROVIDER_SHA256=(\S+)").Groups[1].Value;
        var aiohttp = Regex.Match(ReadRepoFile("engine-provider/requirements.txt"), @"(?m)^(aiohttp==\S+)\s*$").Groups[1].Value;
        Assert.Matches("^[0-9a-f]{40}$", sha);
        Assert.Matches("^[0-9a-f]{64}$", sum);
        Assert.StartsWith("aiohttp==", aiohttp);

        var readme = ReadRepoFile("engine-provider/README.md");
        // Jeder Abruf des Provider-Skripts nennt den Pin des Images — kein zweiter, abweichender Commit.
        var pins = Regex.Matches(readme, @"lichess-org/external-engine/([0-9a-f]{40})/example-provider\.py");
        Assert.Equal(2, pins.Count);   // Linux/macOS und Windows
        Assert.All(pins, m => Assert.Equal(sha, m.Groups[1].Value));
        // … gefolgt von der Pruefsumme und dem Patch, je Weg einmal.
        Assert.Equal(2, Regex.Matches(readme, Regex.Escape(sum), RegexOptions.IgnoreCase).Count);
        Assert.Equal(2, Regex.Matches(readme, @"patch_force_close\.py example-provider\.py").Count);
        // Die Pruefsumme muss etwas VERHINDERN, nicht nur melden: jede Pruefzeile verwirft die Datei bei Abweichung,
        // sonst laufen beim Einfuegen des ganzen Blocks Patch und Start mit dem Token trotzdem (Nacharbeit A4-017).
        var sumLines = readme.Split('\n').Where(l => l.Contains(sum, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(sumLines, l => l.Contains("sha256sum -c - || rm -f example-provider.py"));             // Linux
        Assert.Single(sumLines, l => l.Contains(@"Remove-Item .\example-provider.py") && l.Contains("throw")); // Windows
        Assert.Contains("`shasum -a 256 -c - || rm -f example-provider.py`", readme);                          // macOS-Hinweis
        // … und steht vor Patch und Start: Pruefung, Patch, Pruefung, Patch (je Weg in dieser Reihenfolge).
        var order = Regex.Matches(readme, Regex.Escape(sum) + @"|patch_force_close\.py example-provider\.py", RegexOptions.IgnoreCase)
            .Select(m => m.Value.StartsWith("patch_") ? "patch" : "sum");
        Assert.Equal(new[] { "sum", "patch", "sum", "patch" }, order);
        // Jedes pip install in einem Befehl pinnt aiohttp wie das Image (die Erwaehnung im Fliesstext steht in Backticks).
        var installs = Regex.Matches(readme, @"pip install(?!`)[^\r\n]*");
        Assert.Equal(2, installs.Count);
        Assert.All(installs, m => Assert.Contains($"\"{aiohttp}\"", m.Value));

        // Der Auto-Restart-Wrapper (vom README fuer den Dauerbetrieb empfohlen) prueft und holt den Patch nach.
        var wrapper = ReadRepoFile("engine-provider/windows/run_provider.ps1");
        Assert.Contains("patch_force_close.py", wrapper);
        Assert.Contains("force_close=True", wrapper);
    }

    /// <summary>
    /// pip-audit prueft nur requirements-Dateien — eine Fassung im Dockerfile oder ein „pip install …" im
    /// Skript-Kommentar sieht es nicht. Vorher gab es keine einzige Datei, der Audit meldete still „nichts zu
    /// pruefen", und 0 von 5 Fremdpaketen waren abgedeckt, darunter aiohttp im Provider-Image, das beim Nutzer mit
    /// seinem RookHub-Token laeuft (Codereview I1-011). Die Fassung steht deshalb EINMAL in der requirements-Datei:
    /// das Image, die CI und der Audit lesen dieselbe.
    /// </summary>
    [Fact]
    public void PipAudit_SeesEveryPythonDependency()
    {
        foreach (var file in new[] { "engine-provider/requirements.txt", "scripts/requirements.txt" })
        {
            var reqs = Regex.Matches(ReadRepoFile(file), @"(?m)^[A-Za-z0-9][A-Za-z0-9._-]*").Select(m => m.Value).ToList();
            Assert.True(reqs.Count > 0, $"{file} nennt kein Paket");
        }

        var scripts = ReadRepoFile("scripts/requirements.txt");
        foreach (var pkg in new[] { "chess", "requests", "anthropic", "pymysql" })
            Assert.Matches($@"(?m)^{pkg}\b", scripts);

        // Das Image installiert aus der Datei und pinnt nichts daneben.
        var dockerfile = ReadRepoFile("engine-provider/Dockerfile");
        Assert.Contains("COPY requirements.txt /opt/requirements.txt", dockerfile);
        Assert.Contains("pip install --no-cache-dir -r /opt/requirements.txt", dockerfile);
        Assert.DoesNotMatch(@"pip install[^\n]*==", dockerfile);

        // Die CI testet den Provider mit derselben Datei.
        Assert.Contains("pip install --quiet -r requirements.txt", ReadRepoFile(".github/workflows/test.yml"));

        // Der Audit sucht die Dateien und meldet es, wenn keine da ist, statt still „nichts zu pruefen".
        var audit = ReadRepoFile(".github/workflows/audit.yml");
        Assert.Contains("find . -name 'requirements*.txt'", audit);
        Assert.DoesNotContain("nichts zu pruefen.' | tee", audit);
        var empty = Regex.Match(audit, @"(?s)if \[ -z ""\$files"" \]; then(.*?)\bfi\b").Groups[1].Value;
        Assert.Contains("exit 1", empty);
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
    public void OgPreviewLocation_FallsBackToTheSpaOnRateLimit()
    {
        // Die Link-Vorschau-Location reicht Seitenaufrufe (/puzzles, /g/, /t/) an den OG-Renderer
        // der API. Antwortet der mit einem Fehler, muss nginx die normale SPA ausliefern — sonst steht
        // der Nutzer vor einer weissen Seite. 429 (Limiter der API je IP) war der fehlende Fall: im
        // E2E-Lauf bekam ein Test statt der App die Absage als Dokument.
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var og = Regex.Match(nginx, @"location ~ \^/\(g\|t\|puzzles\)\(/\|\$\) \{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);
        Assert.True(og.Success, "OG-Location in nginx.conf nicht gefunden");
        var body = og.Groups["body"].Value;

        Assert.Contains("proxy_intercept_errors on;", body);
        var errorPage = Regex.Match(body, @"error_page ([0-9 ]+)= @og_fallback;");
        Assert.True(errorPage.Success, "error_page -> @og_fallback fehlt");
        var codes = errorPage.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var code in new[] { "429", "500", "502", "503", "504" })
            Assert.Contains(code, codes);
    }

    /// <summary>
    /// F8-012: dieselbe nginx.conf steckt in allen fuenf Images, die OG-Weiche schickte /g, /t, /puzzles aber auf JEDEM
    /// Host an den Renderer, und die Host-Map kannte nur die Turnierseite. KidHub/LeagueHub/ClubHub bekamen RookHubs
    /// Shell mit Bundle-Namen, die es dort nicht gibt — weisse Seite. Dazu standen zwei widerspruechliche
    /// Cache-Control-Header auf der Antwort (API und map).
    /// </summary>
    [Fact]
    public void OgPreview_IsOnlyForRookHubAndTheTournamentSite_OtherSitesServeTheirOwnShell()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var map = Regex.Match(nginx, @"map \$host \$rookhub_og_site \{(?<body>[^}]*)\}");
        Assert.True(map.Success, "map $host $rookhub_og_site fehlt");
        Assert.Contains("default \"\";", map.Groups["body"].Value);
        var rules = Regex.Matches(map.Groups["body"].Value, @"~\*(?<re>\S+)\s+""(?<v>[^""]*)"";")
            .Select(m => (Re: new Regex(m.Groups["re"].Value, RegexOptions.IgnoreCase), Value: m.Groups["v"].Value))
            .ToList();
        Assert.NotEmpty(rules);
        // Wie nginx: der erste passende Regex-Eintrag gewinnt, sonst default.
        string Site(string host) => rules.FirstOrDefault(r => r.Re.IsMatch(host)).Value ?? "";

        foreach (var host in new[] { "kidhub.oberschmid.homes", "kidhub-dev.oberschmid.homes", "leaguehub.oberschmid.homes",
                     "leaguehub-dev.oberschmid.homes", "clubhub.oberschmid.homes", "clubhub-dev.oberschmid.homes" })
            Assert.Equal("none", Site(host));
        foreach (var host in new[] { "tournament.oberschmid.homes", "turnier.oberschmid.homes", "turnier-dev.oberschmid.homes" })
            Assert.Equal("turnier", Site(host));
        foreach (var host in new[] { "rookhub.oberschmid.homes", "rookhub-dev.oberschmid.homes", "localhost", "172.18.0.5" })
            Assert.Equal("", Site(host));

        var og = Regex.Match(nginx, @"location ~ \^/\(g\|t\|puzzles\)\(/\|\$\) \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(og.Success, "OG-Location in nginx.conf nicht gefunden");
        var body = og.Groups["body"].Value;
        // Seiten ohne Vorschau bekommen ihre eigene index.html, BEVOR etwas an die API geht.
        var rewrite = body.IndexOf("if ($rookhub_og_site = none) { rewrite ^ /index.html last; }", StringComparison.Ordinal);
        Assert.True(rewrite >= 0, "OG-Location schickt Seiten ohne Vorschau nicht auf ihre eigene Shell");
        Assert.True(rewrite < body.IndexOf("proxy_pass ", StringComparison.Ordinal));
        // Genau EIN Cache-Control: das der map; das der API wird verworfen.
        Assert.Contains("proxy_hide_header Cache-Control;", body);
        var cache = Regex.Match(nginx, @"map \$uri \$rookhub_cache_control \{(?<body>.*?)\n\}", RegexOptions.Singleline);
        Assert.True(cache.Success);
        Assert.Contains("~^/(g|t|l|puzzles|", cache.Groups["body"].Value);
        Assert.Contains("~^/(index\\.html|", cache.Groups["body"].Value);
    }

    [Fact]
    public void Csp_AllowsBlobImages_ButNoForeignImageOrigin()
    {
        // Das Formular-Foto (0.529.0) kommt ueber den HttpClient mit Anmelde-Token und wird als blob:-URL angezeigt;
        // ohne blob: in img-src blockiert der Browser das Bild lautlos — das Foto-Fenster war leer (0.531.1).
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var csp = Regex.Match(nginx, "Content-Security-Policy \"(?<v>[^\"]+)\"");
        Assert.True(csp.Success, "CSP fehlt in nginx.conf");
        var img = Regex.Match(csp.Groups["v"].Value, "img-src (?<v>[^;]+);");
        Assert.True(img.Success, "img-src fehlt");
        var sources = img.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("blob:", sources);
        // Und weiterhin keine fremde Herkunft (Kacheln laufen ueber /tiles/, Schriften ueber /fonts/).
        Assert.All(sources, s => Assert.Contains(s, new[] { "'self'", "data:", "blob:" }));
    }

    /// <summary>
    /// F8-017: ein <c>add_header</c> IN einer location ersetzt alle serverweiten <c>add_header</c> — die exakte
    /// assetlinks-Location setzte Cache-Control selbst und lieferte die Datei ohne CSP, nosniff, HSTS und
    /// X-Frame-Options aus. Dazu erlaubte worker-src ungenutzte blob:-Worker, und X-XSS-Protection stand auf dem
    /// veralteten „1; mode=block".
    /// </summary>
    [Fact]
    public void SecurityHeaders_ReachAssetlinks_NoBlobWorkers_NoLegacyXssFilter()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");

        var assetlinks = Regex.Match(nginx, @"location = /\.well-known/assetlinks\.json \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(assetlinks.Success, "assetlinks-Location fehlt in nginx.conf");
        Assert.DoesNotContain("add_header", assetlinks.Groups["body"].Value);
        var map = Regex.Match(nginx, @"map \$uri \$rookhub_cache_control \{(?<body>.*?)\n\}", RegexOptions.Singleline);
        Assert.True(map.Success, "map $rookhub_cache_control fehlt");
        Assert.Contains("/.well-known/assetlinks.json  \"public, max-age=3600\";", map.Groups["body"].Value);

        // Jede location, die doch eigene add_header braucht (Kacheln), muss mindestens nosniff wiederholen.
        foreach (Match loc in Regex.Matches(nginx, @"\n    location (?<head>[^\n]*?) \{\n(?<body>.*?)\n    \}", RegexOptions.Singleline))
            if (loc.Groups["body"].Value.Contains("add_header"))
                Assert.True(loc.Groups["body"].Value.Contains("add_header X-Content-Type-Options \"nosniff\" always;"),
                    $"location {loc.Groups["head"].Value.Trim()} setzt add_header, verliert damit nosniff");

        var csp = Regex.Match(nginx, "Content-Security-Policy \"(?<v>[^\"]+)\"");
        Assert.True(csp.Success, "CSP fehlt in nginx.conf");
        var worker = Regex.Match(csp.Groups["v"].Value, "worker-src (?<v>[^;]+);");
        Assert.True(worker.Success, "worker-src fehlt");
        Assert.Equal(new[] { "'self'" }, worker.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.DoesNotContain("X-XSS-Protection \"1", nginx);
        Assert.Contains("add_header X-XSS-Protection \"0\" always;", nginx);
    }

    [Fact]
    public void ScannerPaths_Get404_WhileAppFilesAndApiStayUntouched()
    {
        // 2026-09-17: ein .env-Scan (313 Anfragen) bekam fuer 291 Pfade 200 und die Startseite, weil
        // der SPA-Fallback jeden unbekannten Pfad so beantwortet. Preisgegeben war nichts, fuer den
        // Scanner war trotzdem jeder davon ein Treffer. Die Regeln muessen solche Pfade treffen und
        // duerfen KEINE Datei des Bundles und keine App-Route erwischen.
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var rules = Regex.Matches(nginx, @"location (?<op>~\*?) (?<re>\S+) \{\s*return 404;\s*\}")
            .Select(m => new Regex(m.Groups["re"].Value,
                m.Groups["op"].Value == "~*" ? RegexOptions.IgnoreCase : RegexOptions.None))
            .ToList();
        Assert.NotEmpty(rules);
        bool Blocked(string path) => rules.Any(r => r.IsMatch(path));

        foreach (var probe in new[]
        {
            "/.env", "/.git/config", "/data/.env", "/backend/.env.local", "/.env.production",
            "/env.old", "/env.production", "/config.env", "/config.php.bak", "/wp-config.php",
            "/wp-config.txt", "/docker-compose.yml", "/config.py", "/setup.php", "/src/PHPInfo.php",
            "/db/dump.sql", "/backup.tar.gz", "/wp-admin/install.php", "/phpmyadmin/", "/web.config",
        })
            Assert.True(Blocked(probe), $"Scanner-Pfad nicht abgefangen: {probe}");

        foreach (var legit in new[]
        {
            "/", "/index.html", "/main-AB12CD34.js", "/chunk-XYZ12345.js", "/styles-QWERTY12.css",
            "/ngsw.json", "/ngsw-worker.js", "/manifest.webmanifest", "/favicon.ico", "/i18n/de.json",
            "/assets/stockfish/stockfish.wasm", "/fonts/inter-abc123.woff2", "/media/board-abc123.png",
            "/courses/12", "/courses/12/calc", "/meinkurs/1.e4", "/tournaments/pl2026-123",
            "/g/Ab3dEf9h", "/t/1474416", "/puzzles/book/77", "/analysis", "/tiles/5/17/11.png",
            // Maia-Sparring: onnxruntime-Laufzeit, Worker und Modell.
            "/assets/ort/ort.wasm.min.js", "/assets/ort/ort-wasm-simd-threaded.mjs",
            "/assets/ort/ort-wasm-simd-threaded.wasm", "/assets/maia/maia-worker.js",
            "/assets/maia/maia3_simplified.onnx",
        })
            Assert.False(Blocked(legit), $"App-Pfad wuerde faelschlich 404: {legit}");

        // Die Punkt-Regel traefe auch /.well-known/assetlinks.json — die Datei steht deshalb als
        // exakte Location in der Konfiguration, und exakte Treffer gewinnen vor jeder Regex.
        Assert.Contains("location = /.well-known/assetlinks.json {", nginx);

        // Bei Regex-Locations gewinnt der ERSTE Treffer: die Regeln stehen vor der OG-Weiche. Die
        // /api/-Locations tragen ^~, sonst fingen die Regeln /api/.env ab, bevor die API es loggt.
        // Verankert an den Scanner-Locations selbst, nicht am ersten "return 404;" der Datei: das
        // steht seit F8-002 in der Kachel-Location und liegt immer vor der OG-Weiche. ALLE Regeln
        // muessen davor stehen, deshalb zaehlt die letzte.
        var lastRule = Regex.Matches(nginx, @"location ~\*? \S+ \{\s*return 404;\s*\}").Max(m => m.Index);
        Assert.True(lastRule > 0 && lastRule < nginx.IndexOf("location ~ ^/(g|t|puzzles)", StringComparison.Ordinal),
            "Scanner-Regeln muessen vor der OG-Location stehen");
        Assert.True(lastRule < nginx.IndexOf("location / {", StringComparison.Ordinal));
        foreach (var api in new[] { "/api/", "/api/engine/", "/api/extension/chessable/", "/api/external-engine/" })
        {
            Assert.Contains($"location ^~ {api} {{", nginx);
            Assert.DoesNotContain($"location {api} {{", nginx);
        }
    }

    /// <summary>
    /// Maia-Sparring im Analysebrett: das Modell (45 MB) liegt NICHT im Repo, der Docker-Build holt es per Pin.
    /// Drei Stellen muessen dabei zusammenpassen, und keine davon prueft ein Compiler:
    /// <list type="number">
    /// <item><b>Pin ↔ Browser.</b> <c>fetch.sh</c> ist die einzige Stelle fuer Commit/Pruefsumme/Groesse;
    /// <c>maia-model.ts</c> traegt die ersten acht Zeichen der Pruefsumme (Cache-Schluessel) und die Groesse. Die
    /// Groesse ist im Browser der EINZIGE Beweis, dass das Modell da ist — eine fehlende Datei beantwortet der
    /// SPA-Fallback mit 200 und der index.html. Ein neuer Pin nur in fetch.sh hiesse: jedes Modell gilt als falsch.</item>
    /// <item><b>.mjs ↔ nginx.</b> nginx:alpine kennt die Endung nicht und liefert application/octet-stream; ein Modul
    /// mit falschem Typ lehnt der Browser ab („Failed to fetch dynamically imported module", live nachgestellt).
    /// Ein add_header in der Location ersetzte die serverweiten Header samt CSP.</item>
    /// <item><b>Dockerfile.</b> Nur das RookHub-Image holt das Modell, und das ARG muss VOR dem Schritt stehen —
    /// sonst ist $APP_PROJECT dort leer, die Bedingung nie wahr, und das Image kaeme still ohne Modell.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Maia_ModelPin_AndDelivery_StayConsistent()
    {
        var fetch = ReadRepoFile("src/frontend/app/maia-model/fetch.sh");
        var commit = Regex.Match(fetch, @"(?m)^COMMIT=(?<v>[0-9a-f]{40})\s*$");
        var sha = Regex.Match(fetch, @"(?m)^SHA256=(?<v>[0-9a-f]{64})\s*$");
        var bytes = Regex.Match(fetch, @"(?m)^BYTES=(?<v>\d+)\s*$");
        Assert.True(commit.Success, "COMMIT=<40 Hex> fehlt in fetch.sh");
        Assert.True(sha.Success, "SHA256=<64 Hex> fehlt in fetch.sh");
        Assert.True(bytes.Success, "BYTES=<Zahl> fehlt in fetch.sh");
        Assert.Contains("set -eu", fetch);
        Assert.Contains("NAME=maia3_simplified.onnx", fetch);

        var model = ReadRepoFile("src/frontend/app/src/app/features/analysis/maia/maia-model.ts");
        var decl = Regex.Match(model,
            @"MAIA_MODEL = \{ url: '(?<url>[^']+)', version: '(?<version>[^']+)', bytes: (?<bytes>\d+) \}");
        Assert.True(decl.Success, "MAIA_MODEL-Deklaration in maia-model.ts nicht gefunden");
        Assert.Equal(sha.Groups["v"].Value[..8], decl.Groups["version"].Value);
        Assert.Equal(bytes.Groups["v"].Value, decl.Groups["bytes"].Value);
        Assert.Equal("/assets/maia/maia3_simplified.onnx", decl.Groups["url"].Value);

        // Ein geaenderter Pin muss das Image neu bauen (Pfadfilter der CI) — und die Attribution nennt den Commit,
        // dessen Modell tatsaechlich ausgeliefert wird.
        Assert.Contains("'src/frontend/app/maia-model/**'", ReadRepoFile(".github/filters.yml"));
        Assert.Contains(commit.Groups["v"].Value, ReadRepoFile("src/frontend/app/public/CHESS-ASSETS.md"));

        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var mjs = Regex.Match(nginx, @"location ~\* \\\.mjs\$ \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(mjs.Success, "location ~* \\.mjs$ fehlt in nginx.conf");
        Assert.Contains("types { application/javascript mjs; }", mjs.Groups["body"].Value);
        Assert.Contains("default_type application/javascript;", mjs.Groups["body"].Value);
        Assert.DoesNotContain("add_header", mjs.Groups["body"].Value);

        var docker = ReadRepoFile("src/frontend/Dockerfile");
        var runs = Regex.Matches(docker, @"(?m)^RUN .*maia-model/fetch\.sh.*$");
        Assert.Single(runs);
        Assert.Equal("RUN if [ \"$APP_PROJECT\" = \"app\" ]; then sh maia-model/fetch.sh; fi", runs[0].Value.TrimEnd());
        var arg = docker.IndexOf("ARG APP_PROJECT=app", StringComparison.Ordinal);
        // Am Zeilenanfang gesucht: der Kommentar ueber dem Schritt nennt `COPY app/ .` selbst.
        var copyAll = Regex.Match(docker, @"(?m)^COPY app/ \.\s*$");
        Assert.True(copyAll.Success, "`COPY app/ .` fehlt im Dockerfile");
        Assert.True(arg >= 0 && arg < runs[0].Index, "ARG APP_PROJECT muss vor dem Modell-Schritt stehen");
        Assert.True(runs[0].Index < copyAll.Index, "Das Modell kommt vor `COPY app/ .` (Layer-Cache bei Quelltext-Aenderungen)");
        Assert.Contains("COPY app/maia-model/fetch.sh maia-model/fetch.sh", docker);
        Assert.Contains("app/maia-model/*.onnx", ReadRepoFile("src/frontend/.dockerignore"));
    }

    /// <summary>
    /// Die Laufzeit (onnxruntime-web) und das Modell gehoeren NUR ins RookHub-Bundle, und das Modell NIE in den
    /// Angular-Service-Worker: der ngsw puffert eine Datei ganz, ohne dass die Seite einen Fortschritt saehe — die
    /// 45 MB legt eigener Code in die Cache API. Laufzeit und Worker stehen in einer `lazy`-Gruppe (offline nach dem
    /// ersten Benutzen, aber kein Download fuer jeden, der RookHub nur oeffnet). onnxruntime-web ist EXAKT gepinnt:
    /// die drei kopierten Dateien sind ein Vertrag mit dem Worker, ein Minor-Sprung kann sie umbenennen.
    /// </summary>
    [Fact]
    public void Maia_RuntimeAndModel_OnlyInTheRookHubBundle_AndNeverInTheServiceWorker()
    {
        const string modelPath = "/assets/maia/maia3_simplified.onnx";
        using var ngsw = JsonDocument.Parse(ReadRepoFile("src/frontend/app/ngsw-config.json"));
        JsonElement? maiaGroup = null;
        foreach (var group in ngsw.RootElement.GetProperty("assetGroups").EnumerateArray())
        {
            var files = group.GetProperty("resources").TryGetProperty("files", out var f)
                ? f.EnumerateArray().Select(x => x.GetString()!).ToList()
                : new List<string>();
            // Ausschluss-Muster (`!…`) werden bewusst nicht ausgewertet: das Modell soll gar nicht erst unter ein
            // Einschluss-Muster fallen.
            foreach (var pattern in files.Where(p => !p.StartsWith('!')))
                Assert.False(NgswGlob(pattern).IsMatch(modelPath),
                    $"ngsw-Gruppe '{group.GetProperty("name").GetString()}' trifft das Modell ({pattern})");
            if (group.GetProperty("name").GetString() == "maia") maiaGroup = group;
        }
        if (ngsw.RootElement.TryGetProperty("dataGroups", out var dataGroups))
            foreach (var group in dataGroups.EnumerateArray())
                foreach (var url in group.GetProperty("urls").EnumerateArray())
                    Assert.False(NgswGlob(url.GetString()!).IsMatch(modelPath), $"ngsw-dataGroup trifft das Modell ({url})");

        Assert.NotNull(maiaGroup);
        var maia = maiaGroup.Value;
        Assert.Equal("lazy", maia.GetProperty("installMode").GetString());
        Assert.Equal("lazy", maia.GetProperty("updateMode").GetString());
        var maiaPatterns = maia.GetProperty("resources").GetProperty("files").EnumerateArray()
            .Select(x => NgswGlob(x.GetString()!)).ToList();
        foreach (var asset in new[]
        {
            "/assets/ort/ort.wasm.min.js", "/assets/ort/ort-wasm-simd-threaded.mjs",
            "/assets/ort/ort-wasm-simd-threaded.wasm", "/assets/maia/maia-worker.js",
        })
            Assert.True(maiaPatterns.Any(p => p.IsMatch(asset)), $"ngsw-Gruppe 'maia' deckt {asset} nicht ab");

        // Der Nachbau der ngsw-Globs selbst: `*` bleibt in EINEM Pfadstueck, `**` geht ueber beliebig viele.
        Assert.DoesNotMatch(NgswGlob("/*.js"), "/assets/maia/maia-worker.js");
        Assert.Matches(NgswGlob("/*.js"), "/main-AB12CD34.js");
        Assert.Matches(NgswGlob("/assets/stockfish/**"), "/assets/stockfish/stockfish-18-lite-single.wasm");

        // angular.json: die drei Eintraege stehen NUR beim Projekt `app` (in dessen Build) — die anderen vier
        // Bundles (turnier, kidhub, leaguehub, clubhub) haben kein Sparring.
        using var angular = JsonDocument.Parse(ReadRepoFile("src/frontend/app/angular.json"));
        var expected = new[]
        {
            ("{ort.wasm.min.js,ort-wasm-simd-threaded.mjs,ort-wasm-simd-threaded.wasm}", "node_modules/onnxruntime-web/dist", "/assets/ort"),
            ("maia-worker.js", "src/app/features/analysis/maia", "/assets/maia"),
            ("*.onnx", "maia-model", "/assets/maia"),
        };
        foreach (var project in angular.RootElement.GetProperty("projects").EnumerateObject())
        {
            var found = new List<(string Target, string Glob, string Input, string Output)>();
            foreach (var target in project.Value.GetProperty("architect").EnumerateObject())
            {
                if (!target.Value.TryGetProperty("options", out var options)
                    || !options.TryGetProperty("assets", out var assets)) continue;
                foreach (var asset in assets.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
                {
                    var input = asset.GetProperty("input").GetString()!;
                    if (!input.Contains("onnxruntime", StringComparison.Ordinal)
                        && !input.Contains("maia", StringComparison.Ordinal)) continue;
                    found.Add((target.Name, asset.GetProperty("glob").GetString()!, input,
                        asset.TryGetProperty("output", out var o) ? o.GetString()! : ""));
                }
            }
            if (project.Name == "app")
                Assert.Equal(expected.Select(e => ("build", e.Item1, e.Item2, e.Item3)), found);
            else
                Assert.True(found.Count == 0, $"Projekt '{project.Name}' kopiert Maia/onnxruntime-Dateien");
        }

        using var package = JsonDocument.Parse(ReadRepoFile("src/frontend/app/package.json"));
        var ort = package.RootElement.GetProperty("dependencies").GetProperty("onnxruntime-web").GetString()!;
        Assert.Matches(@"^\d+\.\d+\.\d+$", ort);   // kein ^ oder ~
    }

    /// <summary>Nachbau von <c>globToRegex</c> aus @angular/service-worker (config/src/glob.ts): `**` als ganzes
    /// Pfadstueck = beliebig viele Stuecke, `*` = beliebig viele Zeichen ohne `/`, `?` = ein Zeichen ohne `/`;
    /// Klammern und `|` gehen als Regex durch (so nutzt ngsw-config.json sie fuer Endungslisten).</summary>
    private static Regex NgswGlob(string glob)
    {
        var segments = glob.Split('/');
        var regex = new StringBuilder("^");
        for (var i = 0; i < segments.Length; i++)
        {
            var last = i == segments.Length - 1;
            if (segments[i] == "**")
            {
                regex.Append(last ? ".*" : @"(?:.+\/)?");
                continue;
            }
            regex.Append(segments[i].Replace(".", @"\.").Replace("+", @"\+").Replace("*", "[^/]*").Replace("?", "[^/]"));
            if (!last) regex.Append(@"\/");
        }
        return new Regex(regex.Append('$').ToString());
    }

    /// <summary>
    /// Eigener Engine-Broker: der Upload des Providers ist ein CHUNKED-Strom, der so lange läuft, wie die Engine
    /// rechnet. Ohne <c>proxy_request_buffering off</c> sammelt nginx ihn bis zum Ende — der Anfragende sähe die
    /// erste Zeile erst nach der Suche (Plan-Kapitel 6.1). Ohne <c>client_max_body_size 0</c> bräche eine lange
    /// Suche an der Größe ab, ohne lange Timeouts an der Stille zwischen zwei tiefen Iterationen.
    /// </summary>
    [Fact]
    public void ExternalEngineLocation_StreamsTheProviderUploadUnbuffered()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var loc = Regex.Match(nginx, @"location \^~ /api/external-engine/ \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "location ^~ /api/external-engine/ fehlt in nginx.conf");
        var body = loc.Groups["body"].Value;
        foreach (var directive in new[]
        {
            "proxy_http_version 1.1;", "proxy_request_buffering off;", "proxy_buffering off;", "proxy_cache off;",
            "client_max_body_size 0;", "proxy_read_timeout 3600s;", "proxy_send_timeout 3600s;",
            "proxy_pass http://$rookhub_broker_api$request_uri;",
        })
            Assert.Contains(directive, body);

        // Die Location steht VOR der generischen /api/ (Lesbarkeit; für nginx entscheidet der längere Präfix).
        Assert.True(nginx.IndexOf("location ^~ /api/external-engine/ {", StringComparison.Ordinal)
                    < nginx.IndexOf("location ^~ /api/ {", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalEngineRegistration_HasAnExactLocation_SoNginxDoesNotRedirectIt()
    {
        // Gefunden mit dem echten Provider im E2E-Stack: die Registrierung ist GET/POST /api/external-engine OHNE
        // Schrägstrich. Eine Präfix-location „/api/external-engine/“ mit proxy_pass beantwortet genau diese URI
        // nginx-intern mit 301 auf „…/“ (an den Container-Port 8080) — der Provider registrierte nie eine Engine.
        // Die exakte location nimmt ihr die URI weg und muss an die API weiterreichen, nicht selbst antworten.
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var loc = Regex.Match(nginx, @"location = /api/external-engine \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "location = /api/external-engine fehlt in nginx.conf (sonst 301 auf die Registrierung)");
        var body = loc.Groups["body"].Value;
        Assert.Contains("proxy_pass http://$rookhub_api$request_uri;", body);
        Assert.DoesNotContain("return ", body);
    }

    /// <summary>
    /// Der Long-Poll des Providers ist anonym und vom Rate-Limiter ausgenommen; unter der Präfix-Regel oben galt für ihn
    /// „client_max_body_size 0" (A4-005). Die exakte Location deckelt seinen Rumpf klein — aber nicht unter dem, was die
    /// API annimmt ([RequestSizeLimit] an Acquire), sonst bekäme ein gültiger Poll schon an nginx 413.
    /// </summary>
    [Fact]
    public void ExternalEnginePoll_HasAnExactLocation_WithASmallBodyLimit()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var loc = Regex.Match(nginx, @"location = /api/external-engine/work \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "location = /api/external-engine/work fehlt in nginx.conf");
        var body = loc.Groups["body"].Value;
        Assert.Contains("proxy_pass http://$rookhub_broker_api$request_uri;", body);
        Assert.DoesNotContain("return ", body);
        var size = Regex.Match(body, @"client_max_body_size (?<n>\d+)k;");
        Assert.True(size.Success, "client_max_body_size (in k) fehlt in der Poll-Location");
        var nginxBytes = long.Parse(size.Groups["n"].Value) * 1024;
        Assert.InRange(nginxBytes, RookHub.Api.Controllers.ExternalEngineController.MaxAcquireBodyBytes, 64 * 1024);
    }

    [Fact]
    public void ExtensionChessableLocation_AllowsTheApiRequestSizeLimits()
    {
        // Gemeldet 2026-09-14: „Mitschnitt importieren" bekam 413, obwohl die API bis 64 MB annimmt — die
        // generische /api/-Location des Frontend-nginx deckelte den Rumpf auf 15 MB, und die Anfrage kam nie bei
        // der API an. Das Limit der Extension-Location muss über JEDEM [RequestSizeLimit] liegen, das der
        // ExtensionController für seine chessable/-Routen setzt.
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var loc = Regex.Match(nginx, @"location \^~ /api/extension/chessable/ \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "location ^~ /api/extension/chessable/ fehlt in nginx.conf");
        var size = Regex.Match(loc.Groups["body"].Value, @"client_max_body_size (?<n>\d+)(?<unit>[kKmMgG]?);");
        Assert.True(size.Success, "client_max_body_size fehlt in der Extension-Location");
        var unit = size.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "k" => 1024L,
            "m" => 1024L * 1024,
            "g" => 1024L * 1024 * 1024,
            _ => 1L,
        };
        var nginxBytes = long.Parse(size.Groups["n"].Value) * unit;

        var limits = typeof(RookHub.Api.Controllers.ExtensionController).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false)
                .Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()
                .Any(a => a.Template?.StartsWith("chessable/", StringComparison.Ordinal) == true))
            .SelectMany(m => m.GetCustomAttributesData()
                .Where(a => a.AttributeType == typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute)))
            .Select(a => Convert.ToInt64(a.ConstructorArguments[0].Value))
            .ToList();
        Assert.NotEmpty(limits);
        Assert.True(nginxBytes >= limits.Max(), $"nginx erlaubt {nginxBytes} Bytes, die API bis {limits.Max()}");
    }

    /// <summary>Mehrseitige Formulare (0.600.0): bis zu drei Fotos in einer Anfrage. Die generische /api/-Location
    /// deckelt auf 15 MB — die Formular-Location muss mindestens so viel durchlassen, wie der Upload annimmt.</summary>
    [Fact]
    public void ScoresheetLocation_AllowsTheUploadRequestLimit()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var loc = Regex.Match(nginx, @"location \^~ /api/scoresheets \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "location ^~ /api/scoresheets fehlt in nginx.conf");
        var size = Regex.Match(loc.Groups["body"].Value, @"client_max_body_size (?<n>\d+)M;");
        Assert.True(size.Success, "client_max_body_size (in M) fehlt in der Formular-Location");
        Assert.Contains("proxy_pass http://$rookhub_sheet_api$request_uri;", loc.Groups["body"].Value);
        var nginxBytes = long.Parse(size.Groups["n"].Value) * 1024 * 1024;

        var upload = typeof(RookHub.Api.Controllers.ScoresheetsController).GetMethod("Upload")!;
        var limit = upload.GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute));
        var apiBytes = Convert.ToInt64(limit.ConstructorArguments[0].Value);
        Assert.True(nginxBytes >= apiBytes, $"nginx erlaubt {nginxBytes} Bytes, die API bis {apiBytes}");
        Assert.True(apiBytes >= 3L * 15 * 1024 * 1024, "drei Fotos zu je 15 MB sollen durchgehen");
    }

    /// <summary>
    /// A6-020: der LeagueHub-Upload eines Formular-Fotos (angemeldet und über den Teilen-Link) nimmt in der API bis zu
    /// 30 MB je Foto, die generische /api/-Location deckelte aber bei 15 MB — ein großes Handyfoto bekam eine HTML-413
    /// von nginx und die Seite nur „Hochladen hat nicht geklappt". Die verschachtelte Location muss mindestens so viel
    /// durchlassen, wie beide Upload-Actions annehmen, und darf nur genau diese beiden Pfade treffen.
    /// </summary>
    [Fact]
    public void LeagueScanUploadLocation_AllowsTheApiRequestSizeLimit_OnlyOnTheUploadPaths()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var api = Regex.Match(nginx, @"location \^~ /api/ \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(api.Success, "location ^~ /api/ fehlt in nginx.conf");
        // Verschachtelt in /api/: auf oberster Ebene griffe eine Regex-Location wegen ^~ nie.
        var loc = Regex.Match(api.Groups["body"].Value, @"location ~ (?<re>\S+) \{(?<body>.*?)\n        \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "verschachtelte Location für den LeagueHub-Formular-Upload fehlt in location ^~ /api/");
        var path = new Regex(loc.Groups["re"].Value);
        foreach (var upload in new[] { "/api/league/club/scans", "/api/league/s/AbC-12_xyz/club/scans" })
            Assert.True(path.IsMatch(upload), $"Upload-Pfad nicht getroffen: {upload}");
        foreach (var other in new[] { "/api/league/club/scans/5", "/api/league/club/scans/lookup", "/api/league/s/x/club/scans/lookup",
                     "/api/league/s/x/club/games/preview", "/api/league/club/games/chessbase", "/api/scoresheets" })
            Assert.False(path.IsMatch(other), $"Location träfe auch {other}");

        var body = loc.Groups["body"].Value;
        Assert.Contains("proxy_pass http://$rookhub_api$request_uri;", body);
        Assert.Contains("set $rookhub_api api:8080;", body);
        var size = Regex.Match(body, @"client_max_body_size (?<n>\d+)M;");
        Assert.True(size.Success, "client_max_body_size (in M) fehlt in der Upload-Location");
        var nginxBytes = long.Parse(size.Groups["n"].Value) * 1024 * 1024;

        foreach (var controller in new[] { typeof(RookHub.Api.Controllers.LeagueClubController), typeof(RookHub.Api.Controllers.LeagueShareClubController) })
        {
            var upload = controller.GetMethod("Upload")!;
            var post = upload.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false)
                .Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Single();
            Assert.Equal("scans", post.Template);
            var limit = upload.GetCustomAttributesData()
                .Single(a => a.AttributeType == typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute));
            var apiBytes = Convert.ToInt64(limit.ConstructorArguments[0].Value);
            Assert.True(nginxBytes >= apiBytes, $"nginx erlaubt {nginxBytes} Bytes, {controller.Name}.Upload bis {apiBytes}");
        }
        Assert.True(nginxBytes >= RookHub.Api.Services.ScoresheetScanService.MaxUploadBytes);
    }

    /// <summary>
    /// F8-002: die Kachel-Weiterleitung an OSM war anonym, ungedrosselt und auf allen vier Seiten offen, und sie nahm
    /// jedes z (zwei Stellen) und x/y (sieben Stellen) an. Jeder Cache-Fehlgriff geht unter unserem User-Agent zu OSM —
    /// ein Massenabruf liesse OSM uns sperren, und die Turnierkarte bliebe fuer alle schwarz.
    /// </summary>
    [Fact]
    public void TileProxy_IsThrottled_BoundedToTheMapsZoom_AndOnlyOnTheTournamentSite()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var loc = Regex.Match(nginx, @"location ~ ""(?<re>\^/tiles/[^""]+)"" \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "Kachel-Location in nginx.conf nicht gefunden");
        var body = loc.Groups["body"].Value;

        // Nur, was die Karte (maxZoom 18) je anfragt; x/y < 2^18 hat hoechstens sechs Stellen.
        var path = new Regex(loc.Groups["re"].Value);
        foreach (var ok in new[] { "/tiles/0/0/0.png", "/tiles/5/17/11.png", "/tiles/10/555/360.png", "/tiles/18/262143/262143.png" })
            Assert.True(path.IsMatch(ok), $"Kachel der Karte abgewiesen: {ok}");
        foreach (var junk in new[] { "/tiles/19/1/1.png", "/tiles/20/1/1.png", "/tiles/99/1/1.png", "/tiles/1/9999999/9999999.png", "/tiles/5/17/11.jpg" })
            Assert.False(path.IsMatch(junk), $"Unsinnige Kachel wuerde an OSM weitergereicht: {junk}");
        Assert.Contains("proxy_pass https://$osm_host/$tile_z/$tile_x/$tile_y.png;", body);

        // Gedrosselt je Betrachter (X-Real-IP vom vorgelagerten Proxy, sonst die Verbindung) und insgesamt.
        Assert.Matches(@"limit_req zone=osm_tiles_client burst=\d+ nodelay;", body);
        Assert.Matches(@"limit_req zone=osm_tiles_all burst=\d+ nodelay;", body);
        Assert.Contains("limit_req_status 429;", body);
        Assert.Matches(@"limit_req_zone \$rookhub_tile_client zone=osm_tiles_client:\d+m rate=\d+r/s;", nginx);
        Assert.Matches(@"limit_req_zone \$server_name zone=osm_tiles_all:\d+m rate=\d+r/s;", nginx);
        var client = Regex.Match(nginx, @"map \$http_x_real_ip \$rookhub_tile_client \{(?<body>[^}]*)\}");
        Assert.True(client.Success, "map fuer den Drossel-Schluessel fehlt");
        Assert.Contains("default $http_x_real_ip;", client.Groups["body"].Value);
        Assert.Contains("\"\"      $binary_remote_addr;", client.Groups["body"].Value);

        // Nur die Turnierseite braucht Kacheln: die drei anderen Seiten (am Host erkannt) antworten 404, unbekannte
        // Hosts (Container-IP, localhost) bleiben offen.
        Assert.Contains("if ($rookhub_tiles_off) { return 404; }", body);
        var off = Regex.Match(nginx, @"map \$host \$rookhub_tiles_off \{(?<body>[^}]*)\}");
        Assert.True(off.Success, "map $host $rookhub_tiles_off fehlt");
        Assert.Contains("default 0;", off.Groups["body"].Value);
        var switches = Regex.Matches(off.Groups["body"].Value, @"~\*(?<re>\S+)\s+1;")
            .Select(m => new Regex(m.Groups["re"].Value, RegexOptions.IgnoreCase)).ToList();
        Assert.NotEmpty(switches);
        bool Off(string host) => switches.Any(r => r.IsMatch(host));
        foreach (var other in new[] { "rookhub.oberschmid.homes", "rookhub-dev.oberschmid.homes", "kidhub.oberschmid.homes",
                     "kidhub-dev.oberschmid.homes", "leaguehub.oberschmid.homes", "leaguehub-dev.oberschmid.homes" })
            Assert.True(Off(other), $"Kacheln auf {other} noch offen");
        foreach (var tournament in new[] { "tournament.oberschmid.homes", "turnier.oberschmid.homes", "turnier-dev.oberschmid.homes",
                     "localhost", "172.18.0.5" })
            Assert.False(Off(tournament), $"Kacheln auf {tournament} gesperrt");
    }

    /// <summary>
    /// N6-005: die Kachel-Weiterleitung sprach TLS ohne Zertifikatspruefung (nginx-Vorgabe aus), reichte den
    /// Inhaltstyp des Fremdservers durch und lieferte die Antwort 30 Tage unter unserer Herkunft aus — ohne CSP, weil
    /// das add_header der Location die serverweite verdraengt. Wer den Weg zu OSM beeinflusst, haette dort ein
    /// text/html-Dokument mit Skript ablegen koennen.
    /// </summary>
    [Fact]
    public void TileProxy_VerifiesUpstreamTls_AndServesTilesAsSandboxedImages()
    {
        var nginx = ReadRepoFile("src/frontend/nginx.conf");
        var loc = Regex.Match(nginx, @"location ~ ""\^/tiles/[^""]+"" \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(loc.Success, "Kachel-Location in nginx.conf nicht gefunden");
        var body = loc.Groups["body"].Value;

        Assert.Contains("proxy_ssl_server_name on;", body);
        Assert.Contains("proxy_ssl_verify on;", body);
        Assert.Contains("proxy_ssl_trusted_certificate /etc/ssl/certs/ca-certificates.crt;", body);
        // Kette heute: Blatt, Zwischen-CA, Wurzel — die nginx-Vorgabe 1 liesse sie scheitern.
        var depth = Regex.Match(body, @"proxy_ssl_verify_depth (?<n>\d+);");
        Assert.True(depth.Success, "proxy_ssl_verify_depth fehlt");
        Assert.InRange(int.Parse(depth.Groups["n"].Value), 2, 5);

        Assert.Contains("proxy_hide_header Content-Type;", body);
        Assert.Contains("add_header Content-Type \"image/png\";", body);
        // Ohne always: auf nginx-eigenen 404/429 stuende Content-Type sonst doppelt.
        Assert.DoesNotContain("add_header Content-Type \"image/png\" always;", body);
        var csp = Regex.Match(body, "add_header Content-Security-Policy \"(?<v>[^\"]+)\" always;");
        Assert.True(csp.Success, "CSP fehlt in der Kachel-Location (das add_header dort verdraengt die serverweite)");
        Assert.Contains("default-src 'none'", csp.Groups["v"].Value);
        Assert.Contains("sandbox", csp.Groups["v"].Value);
        Assert.Contains("add_header X-Content-Type-Options \"nosniff\" always;", body);
    }

    [Fact]
    public void RateLimitScale_IsRaisedOnlyInTheE2eStack()
    {
        // Der Faktor lockert JEDEN Deckel des Rate-Limiters. Im E2E-Stack noetig (eine Adresse
        // faehrt die ganze Suite), in jeder anderen Umgebung ein offenes Scheunentor.
        Assert.Contains("RateLimiting__PermitScale", ReadRepoFile("compose.e2e.yml"));
        foreach (var file in new[] { "compose.yml.example", "compose.vpn.example", "compose.vpn.yml", "compose.dev.yml", "compose.dev.vpn.yml" })
            Assert.DoesNotContain("PermitScale", ReadRepoFile(file));
    }

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
    /// Die DSGVO-Löschfrist der Logs hängt am Index-Template des Sinks und geht bei jeder
    /// Template-Neuschreibung verloren — das Retention-Skript muss also REGELMÄSSIG laufen
    /// (Codereview 2026-09-29, I1-010). Es verlangte das selbst, verwies dafür aber auf eine Vorlage
    /// in docs/backup.md, die es nie gab; weder Repo noch Host planten einen Lauf ein.
    /// </summary>
    [Fact]
    public void LogRetention_ShipsAMonthlyTimerTemplate()
    {
        var service = ReadRepoFile("scripts/systemd/rookhub-log-retention.service.example");
        var timer = ReadRepoFile("scripts/systemd/rookhub-log-retention.timer.example");

        Assert.Matches(@"(?m)^ExecStart=\S*python3\s+\S*scripts/es_log_retention\.py", service);
        Assert.Matches(@"(?m)^Type=oneshot\s*$", service);
        // Monatlich: am Ersten (oder das systemd-Kürzel) — und ein verpasster Lauf wird nachgeholt.
        Assert.Matches(@"(?m)^OnCalendar=(monthly|\*-\*-01( .*)?)\s*$", timer);
        Assert.Matches(@"(?m)^Persistent=true\s*$", timer);
        // Skript und Doku zeigen auf die Vorlage, statt ins Leere.
        Assert.Contains("rookhub-log-retention", ReadRepoFile("scripts/es_log_retention.py"));
        Assert.Contains("rookhub-log-retention.timer.example", ReadRepoFile("docs/log-retention.md"));
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
