using System.Text.RegularExpressions;

namespace RookHub.Api.Tests;

/// <summary>
/// Quelltext-Wache über die Ordnerstruktur (seit 0.752–0.753): Dienste, Controller, DTOs und Tests liegen
/// je Fachbereich in <c>&lt;Art&gt;/&lt;Bereich&gt;/</c>, Namespace = Ordner.
///
/// <para><b>Warum ein Test:</b> die Regel steht in der CLAUDE.md, aber eine Regel im Text bricht still — eine
/// neue Datei direkt in <c>Services/</c> oder ein Namespace, der nicht zum Ordner passt, fällt sonst erst
/// auf, wenn man die Datei sucht. Geprüft werden nur dauerhafte Regeln (Ort, Namespace, Abhängigkeits-
/// Richtung), keine Stilfragen. Bewusst als Scan ohne Zusatzpaket, wie <c>BookSourceIncludeGuardTests</c>.</para>
/// </summary>
public class ArchitectureGuardTests
{
    /// <summary>Gemeinsame Bausteine, die direkt in <c>Services/</c> liegen dürfen: ihr Namespace
    /// <c>RookHub.Api.Services</c> ist aus jedem Bereich ohne <c>using</c> sichtbar. Ein NEUER Dienst gehört in
    /// den Ordner seines Bereichs; nur ein echter Querschnitts-Baustein (PGN/FEN, Infrastruktur, LLM-Client)
    /// kommt hierher — dann mit Eintrag in diese Liste.</summary>
    private static readonly HashSet<string> GemeinsameBausteine =
    [
        "ApiErrorResponses.cs", "BackgroundPause.cs", "BackgroundTaskQueue.cs", "ClaudeJsonClient.cs", "DbIdempotency.cs", "FenFields.cs",
        "FenListParser.cs", "Figurines.cs", "GithubActionsService.cs", "HeartbeatService.cs", "IpBucket.cs", "IpCountryService.cs",
        "LimitedReadStream.cs", "LogThrottle.cs", "OpenAiJsonClient.cs", "Paging.cs", "PeriodicWorker.cs",
        "PermissiveSan.cs", "PgnMoveTree.cs", "PgnParser.cs", "PgnTokens.cs", "PgnWriter.cs", "PieceLetters.cs",
        "QuietHours.cs", "RateLimitPartitions.cs", "RetentionDelete.cs", "RetentionOptions.cs", "RateLimitRejectionHandler.cs", "RateLimitScale.cs",
        "SecretConfigCheck.cs", "StaleContentRule.cs", "WindowAndConcurrencyLimiter.cs",
    ];

    /// <summary>Direkt in <c>Controllers/</c> bzw. <c>DTOs/</c>: die Basisklasse und die Infrastruktur-Endpunkte,
    /// die keinem Fachbereich gehören.</summary>
    private static readonly HashSet<string> StammControllerUndDtos =
    [
        "Controllers/BackgroundJobsController.cs", "Controllers/BaseApiController.cs",
        "Controllers/BotHeartbeatController.cs", "Controllers/CiController.cs", "Controllers/ClientLogController.cs",
        "DTOs/BackgroundJobsDtos.cs", "DTOs/CiDtos.cs", "DTOs/ClientLogDto.cs",
    ];

    /// <summary>DTO-Dateien, die heute auf Dienste zugreifen — fast immer, um eine Grenze (MaxLength,
    /// Deckel) oder einen Helfer mit dem Dienst zu teilen. SPERRKLINKE: die Liste darf schrumpfen, aber nicht
    /// wachsen. Eine neue Grenze gehört in das DTO selbst (der Dienst liest sie von dort), nicht umgekehrt.</summary>
    private static readonly HashSet<string> DtosMitDienstZugriff =
    [
        "Chessable/ChessableDtos.cs", "Courses/CourseDtos.cs", "Engines/AnalysisJobDtos.cs",
        "Games/GameAnalysisDtos.cs", "Games/GameDtos.cs", "Games/GameReconstructionDtos.cs",
        "League/LeagueClubDtos.cs", "Puzzles/PuzzleDtos.cs", "Repertoires/RepertoireDtos.cs",
        "Repertoires/RepertoireExplorerDtos.cs", "Scoresheet/ScoresheetDtos.cs",
        "TournamentDirectory/TournamentDirectoryDtos.cs", "Tournaments/TournamentHistoryDtos.cs",
    ];

    private static readonly Regex NamespaceZeile = new(@"^namespace ([\w.]+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex NutztServices = new(@"^using RookHub\.Api\.Services\b|RookHub\.Api\.Services\.|\bServices\.[A-Z]",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex NutztControllers = new(@"^using RookHub\.Api\.Controllers\b|RookHub\.Api\.Controllers\.",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex UsingServicesOderControllers = new(@"^using RookHub\.Api\.(Services|Controllers)\b",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ApiRoot() => Path.Combine(RepoRoot(), "src", "api", "RookHub.Api");

    /// <summary>Alle .cs unter <paramref name="root"/> ohne bin/obj, als Pfad relativ zu <paramref name="root"/> mit „/".</summary>
    private static IEnumerable<string> Quellen(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(r => !r.StartsWith("bin/") && !r.StartsWith("obj/") && !r.Contains("/bin/") && !r.Contains("/obj/"));

    [Fact]
    public void ServicesStamm_NurGemeinsameBausteine()
    {
        var stamm = Directory.GetFiles(Path.Combine(ApiRoot(), "Services"), "*.cs").Select(Path.GetFileName).ToList();
        var fremd = stamm.Where(f => !GemeinsameBausteine.Contains(f!)).ToList();
        Assert.True(fremd.Count == 0,
            "Neue Dateien direkt in Services/ — gehören in Services/<Bereich>/ (oder als Querschnitts-Baustein in " +
            $"die Liste GemeinsameBausteine): {string.Join(", ", fremd)}");
        var weg = GemeinsameBausteine.Where(f => !stamm.Contains(f)).ToList();
        Assert.True(weg.Count == 0, $"Veraltete Einträge in GemeinsameBausteine (Datei gibt es nicht mehr): {string.Join(", ", weg)}");
    }

    [Theory]
    [InlineData("Controllers")]
    [InlineData("DTOs")]
    public void ControllerUndDtoStamm_NurInfrastruktur(string art)
    {
        var fremd = Directory.GetFiles(Path.Combine(ApiRoot(), art), "*.cs")
            .Select(f => $"{art}/{Path.GetFileName(f)}")
            .Where(r => !StammControllerUndDtos.Contains(r)).ToList();
        Assert.True(fremd.Count == 0, $"Neue Dateien direkt in {art}/ — gehören in {art}/<Bereich>/: {string.Join(", ", fremd)}");
    }

    [Theory]
    [InlineData("src/api/RookHub.Api/Services", "RookHub.Api.Services")]
    [InlineData("src/api/RookHub.Api/Controllers", "RookHub.Api.Controllers")]
    [InlineData("src/api/RookHub.Api/DTOs", "RookHub.Api.DTOs")]
    [InlineData("tests/RookHub.Api.Tests", "RookHub.Api.Tests")]
    public void Namespace_PasstZumOrdner(string ordner, string basis)
    {
        var root = Path.Combine(RepoRoot(), ordner);
        var falsch = new List<string>();
        foreach (var rel in Quellen(root))
        {
            var unter = Path.GetDirectoryName(rel)!.Replace('\\', '/');
            var erwartet = unter.Length == 0 ? basis : $"{basis}.{unter.Replace('/', '.')}";
            var m = NamespaceZeile.Match(File.ReadAllText(Path.Combine(root, rel)));
            if (!m.Success || m.Groups[1].Value != erwartet)
                falsch.Add($"{rel}: {(m.Success ? m.Groups[1].Value : "kein namespace")} statt {erwartet}");
        }
        Assert.True(falsch.Count == 0, "Namespace passt nicht zum Ordner:\n" + string.Join("\n", falsch));
    }

    [Fact]
    public void Dienste_HaengenNieVonControllernAb()
    {
        var root = Path.Combine(ApiRoot(), "Services");
        var treffer = Quellen(root).Where(r => NutztControllers.IsMatch(File.ReadAllText(Path.Combine(root, r)))).ToList();
        Assert.True(treffer.Count == 0,
            "Dienste dürfen keine Controller kennen (Richtung Controller → Dienst): " + string.Join(", ", treffer));
    }

    [Fact]
    public void Models_KennenWederDiensteNochController()
    {
        var root = Path.Combine(ApiRoot(), "Models");
        var treffer = Quellen(root).Where(r => UsingServicesOderControllers.IsMatch(File.ReadAllText(Path.Combine(root, r)))).ToList();
        Assert.True(treffer.Count == 0, "EF-Entitäten dürfen keine Dienste/Controller einbinden: " + string.Join(", ", treffer));
    }

    [Fact]
    public void DtoZugriffAufDienste_WaechstNicht()
    {
        var root = Path.Combine(ApiRoot(), "DTOs");
        var heute = Quellen(root).Where(r => NutztServices.IsMatch(File.ReadAllText(Path.Combine(root, r)))).ToHashSet();
        var neu = heute.Except(DtosMitDienstZugriff).ToList();
        Assert.True(neu.Count == 0,
            "Neue DTO-Datei greift auf Dienste zu — Grenzen/Konstanten ins DTO legen und den Dienst davon lesen lassen: " +
            string.Join(", ", neu));
        var erledigt = DtosMitDienstZugriff.Except(heute).ToList();
        Assert.True(erledigt.Count == 0,
            "Diese DTOs greifen nicht mehr auf Dienste zu — aus DtosMitDienstZugriff streichen (Sperrklinke): " +
            string.Join(", ", erledigt));
    }
}
