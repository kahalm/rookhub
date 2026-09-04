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

    [Fact]
    public void DockerWorkflow_KeepsTheTestGate()
    {
        // Fällt diese eine Zeile weg, pushen die Image-Jobs ohne einen einzigen Test — und
        // Watchtower zieht die Images in derselben Nacht.
        var text = ReadRepoFile(".github/workflows/docker.yml");
        Assert.Contains("needs: tests", text);
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

    [Fact]
    public void OperationsScripts_AreShipped()
    {
        // Backup + Log-Retention gab es lange gar nicht — hier festnageln, damit sie
        // nicht wieder still verschwinden.
        Assert.Contains("mariadb-dump", ReadRepoFile("scripts/backup-db.sh"));
        Assert.Contains("_ilm/policy", ReadRepoFile("scripts/es_log_retention.py"));
        Assert.Contains("Restore", ReadRepoFile("docs/backup.md"));
    }
}
