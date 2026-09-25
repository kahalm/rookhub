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
        // Die engine-provider-Tests liefen zuvor in KEINEM Workflow — ein Bump von PROVIDER_SHA oder
        // ein Umbau von entrypoint.sh war damit ungeprüft. provider.test.py hält den Vertrag mit dem
        // Broker fest (bestmove am Suchende, Lebenszeichen): genau daran hing der Pin bis 0.478.10.
        Assert.Contains("test/entrypoint.test.sh", text);
        Assert.Contains("test/supervisor.test.sh", text);
        Assert.Contains("test/provider.test.py", text);
        Assert.DoesNotContain("heartbeat.test.py", text);
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
        })
            Assert.False(Blocked(legit), $"App-Pfad wuerde faelschlich 404: {legit}");

        // Die Punkt-Regel traefe auch /.well-known/assetlinks.json — die Datei steht deshalb als
        // exakte Location in der Konfiguration, und exakte Treffer gewinnen vor jeder Regex.
        Assert.Contains("location = /.well-known/assetlinks.json {", nginx);

        // Bei Regex-Locations gewinnt der ERSTE Treffer: die Regeln stehen vor der OG-Weiche. Die
        // /api/-Locations tragen ^~, sonst fingen die Regeln /api/.env ab, bevor die API es loggt.
        var firstRule = nginx.IndexOf("return 404;", StringComparison.Ordinal);
        Assert.True(firstRule > 0 && firstRule < nginx.IndexOf("location ~ ^/(g|t|puzzles)", StringComparison.Ordinal),
            "Scanner-Regeln muessen vor der OG-Location stehen");
        Assert.True(firstRule < nginx.IndexOf("location / {", StringComparison.Ordinal));
        foreach (var api in new[] { "/api/", "/api/engine/", "/api/extension/chessable/" })
        {
            Assert.Contains($"location ^~ {api} {{", nginx);
            Assert.DoesNotContain($"location {api} {{", nginx);
        }
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
