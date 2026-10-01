using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Vertrag zwischen Crawler und RookHub (Codereview I2-006). Die Crawler-Antworten laufen als
/// <see cref="JsonElement"/> ungetypt durch die API bis in die Turnierseite; gelesen wird per
/// Feldnamen-String (<c>TryGetProperty("knownRounds")</c>) bzw. ueber handgeschriebene
/// TypeScript-Interfaces. Ein reines C#-Refactoring im Crawler (Property umbenannt) liess bisher
/// alle drei Beteiligten gruen: der Crawler testet seine Properties, RookHub fuetterte
/// selbstgebautes JSON.
///
/// <para><b>Die Kette.</b> <c>Fixtures/CrawlerContract/*.json</c> ist die GOLDENE Fassung der
/// Antworten — je Datei die Felder des Crawler-DTOs in dessen Reihenfolge, mit nicht-leeren
/// Werten. Dagegen pruefen drei Tests: (1) jedes Feld, das RookHubs Server liest, steht mit dem
/// erwarteten Typ darin; (2) jedes Feld der Frontend-Interfaces in <c>core/models.ts</c> steht
/// darin; (3) liegt der Crawler-Quelltext daneben (Stack-Kopie, oder <c>CRAWLER_REPO</c> im
/// CI-Job <c>test-crawler-contract</c>, dort der zuletzt getaggte Crawler), traegt sein DTO jedes
/// Feld der goldenen Datei. Ein umbenanntes oder entferntes Crawler-Feld faellt damit in (3) auf,
/// und nach dem Nachziehen der goldenen Datei in (1)/(2) an genau der Stelle, die es liest. Ein
/// NEUES Crawler-Feld bricht nichts und darf in der goldenen Datei fehlen, bis es jemand liest.</para>
///
/// <para><b>SPIEGEL</b>: chessresults_crawler <c>src/ChessResultsCrawler/DTOs/TournamentDtos.cs</c>,
/// <c>DTOs/PlayerSearchDtos.cs</c> und <c>Services/RoundDetectionService.cs</c>
/// (<c>RoundCheckResult</c>). Wer dort ein Antwort-Feld aendert, zieht die goldene Datei hier nach
/// — und damit jeden Leser.</para>
/// </summary>
public class CrawlerContractTests
{
    // ----- Goldene Antworten ------------------------------------------------

    private static JsonNode Golden(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CrawlerContract", file);
        Assert.True(File.Exists(path), $"Goldene Datei fehlt: {file}");
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    /// <summary>Das Objekt einer Antwort — bei einer Liste ihr erstes Element; <c>child</c> = ein
    /// verschachteltes Feld (Liste: dessen erstes Element).</summary>
    private static JsonObject Sample(string file, string? child = null)
    {
        JsonNode node = Golden(file);
        if (node is JsonArray top) node = top[0]!;
        if (child is not null)
        {
            node = node[child]!;
            if (node is JsonArray inner) node = inner[0]!;
        }
        return node.AsObject();
    }

    private static string KindOf(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => "null",
    };

    // ----- (1) Was RookHubs Server liest ------------------------------------

    /// <summary>
    /// Jedes Feld, das der Server aus einer Crawler-Antwort per Namen liest — mit dem Typ, den er
    /// erwartet (die Leser pruefen den <c>ValueKind</c> und fallen sonst still auf „nichts" zurueck).
    /// Ein neuer Lesezugriff gehoert hier hinein, sonst schlaegt
    /// <see cref="ServerParsers_ReadOnlyFieldsOfTheContract"/> an.
    /// </summary>
    public static TheoryData<string, string, string> FieldsTheServerReads => new()
    {
        // GET /api/tournaments/{id}: TournamentMonitorController, CrawlQueueClient,
        // AutoSubscriptionService (Termin), BotStatsService (Ort)
        { "tournament.json", "id", "number" },
        { "tournament.json", "chessResultsId", "string" },
        { "tournament.json", "knownRounds", "number" },
        { "tournament.json", "date", "string" },
        { "tournament.json", "location", "string" },
        // GET /api/tournaments/{id}/rounds/check: RoundMonitorService, TournamentMonitorController
        { "rounds-check.json", "knownRounds", "number" },
        { "rounds-check.json", "availableRounds", "number" },
        { "rounds-check.json", "hasNewRound", "bool" },
        { "rounds-check.json", "newRoundNumbers", "array" },
        // GET /api/tournaments/{id}/players: AutoSubscriptionService (Auto-Favoriten)
        { "players.json", "snr", "number" },
        { "players.json", "name", "string" },
        { "players.json", "fideId", "string" },
        // GET /api/tournament-search/player-history: AutoSubscriptionService
        { "player-history.json", "tournamentId", "string" },
        { "player-history.json", "tournamentName", "string" },
        { "player-history.json", "endDate", "string" },
        // GET /api/tournaments/{id}/players/{snr}/results: BotStatsService
        { "player-results.json", "result", "string" },
        { "player-results.json", "roundNumber", "number" },
        { "player-results.json", "points", "string" },
        // GET /api/players/search: PlayerSearchService
        { "player-search.json", "name", "string" },
        { "player-search.json", "fideId", "string" },
        { "player-search.json", "chessResultsId", "string" },
        { "player-search.json", "elo", "number" },
        { "player-search.json", "country", "string" },
        { "player-search.json", "title", "string" },
    };

    [Theory]
    [MemberData(nameof(FieldsTheServerReads))]
    public void Golden_CarriesEveryFieldTheServerReads(string file, string field, string kind)
    {
        var sample = Sample(file);
        Assert.True(sample.ContainsKey(field), $"{file}: Feld '{field}' fehlt — der Leser bekaeme still nichts.");
        Assert.Equal(kind, KindOf(sample[field]));
    }

    /// <summary>Die Dateien, die Crawler-JSON per Feldnamen lesen.</summary>
    private static readonly string[] ParserFiles =
    [
        "src/api/RookHub.Api/Services/PlayerSearchService.cs",
        "src/api/RookHub.Api/Services/AutoSubscriptionService.cs",
        "src/api/RookHub.Api/Services/BotStatsService.cs",
        "src/api/RookHub.Api/Services/RoundMonitorService.cs",
        "src/api/RookHub.Api/Services/CrawlQueueClient.cs",
        "src/api/RookHub.Api/Controllers/TournamentMonitorController.cs",
    ];

    /// <summary>
    /// Felder, die in diesen Dateien gelesen werden, aber NICHT vom Crawler kommen: die
    /// FIDE-Spielersuche in <c>PlayerSearchService</c> (eigener Client, eigenes Format).
    /// </summary>
    private static readonly HashSet<string> NotFromTheCrawler = ["fideid", "rating"];

    /// <summary>
    /// Jeder Feldnamen-Zugriff der Leser-Dateien steht im Vertrag — ein neuer Zugriff ohne Eintrag
    /// waere wieder einer, den niemand gegen die Crawler-Antwort prueft.
    /// </summary>
    [Fact]
    public void ServerParsers_ReadOnlyFieldsOfTheContract()
    {
        var contract = FieldsTheServerReads.Select(row => (string)row[1]).ToHashSet(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var file in ParserFiles)
        {
            var source = ReadRepoFile(file);
            foreach (Match m in Regex.Matches(source, @"(?:TryGetProperty|GetProperty)\(""([^""]+)"""))
            {
                var name = m.Groups[1].Value;
                if (!contract.Contains(name) && !NotFromTheCrawler.Contains(name))
                    unknown.Add($"{file}: {name}");
            }
        }
        Assert.True(unknown.Count == 0,
            "Feldnamen ohne Vertragseintrag (FieldsTheServerReads ergaenzen): " + string.Join(", ", unknown));
    }

    // ----- (2) Was die Turnierseite liest -----------------------------------

    /// <summary>Frontend-Interface in <c>core/models.ts</c> → goldene Antwort, die es beschreibt.</summary>
    public static TheoryData<string, string, string?> FrontendModels => new()
    {
        { "Tournament", "tournament.json", null },
        { "TournamentGroup", "tournament.json", "groups" },
        { "TournamentPlayer", "players.json", null },
        { "TournamentTeam", "team.json", null },
        { "TournamentPairing", "pairings.json", null },
        { "TeamPairingResponse", "team-pairings.json", null },
    };

    /// <summary>
    /// Jedes Feld der handgeschriebenen Interfaces kommt wirklich vom Crawler — sonst zeigt die
    /// Turnierseite leere Spalten (Szenario des Befunds: <c>teamName</c> umbenannt, Mannschafts- und
    /// Vereinsspalte leer, Favoriten-Logik greift nicht). Zusaetzliche Crawler-Felder ohne
    /// Interface-Eintrag sind erlaubt.
    /// </summary>
    [Theory]
    [MemberData(nameof(FrontendModels))]
    public void FrontendModel_ReadsOnlyFieldsTheCrawlerSends(string iface, string file, string? child)
    {
        var fields = InterfaceFields(ReadRepoFile("src/frontend/app/src/app/core/models.ts"), iface);
        Assert.NotEmpty(fields);
        var golden = Sample(file, child).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var missing = fields.Where(f => !golden.Contains(f)).ToList();
        Assert.True(missing.Count == 0,
            $"{iface} liest Felder, die {file} nicht hat: {string.Join(", ", missing)}");
    }

    /// <summary>Die Feldnamen eines <c>export interface</c> (ohne Kommentarzeilen).</summary>
    private static List<string> InterfaceFields(string ts, string iface)
    {
        var start = Regex.Match(ts, $@"export interface {iface}\s*\{{");
        Assert.True(start.Success, $"Interface {iface} nicht gefunden");
        var end = ts.IndexOf("\n}", start.Index, StringComparison.Ordinal);
        var body = ts[(start.Index + start.Length)..end];
        return Regex.Matches(body, @"(?m)^\s*(\w+)\??\s*:")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    // ----- Getypte Leser ---------------------------------------------------

    /// <summary>
    /// Der Turnierverlauf liest die Trefferliste GETYPT (<see cref="DirectoryCrawlerClient.JsonOptions"/>):
    /// ein Name, den der Record anders schreibt als der Crawler, bliebe still <c>null</c>. Die
    /// goldene Datei traegt nur gefuellte Werte — jede Property muss also ankommen.
    /// </summary>
    [Fact]
    public void HistoryRecord_ReadsEveryFieldOfTheGoldenPlayerHistory()
    {
        var rows = JsonSerializer.Deserialize<List<TournamentHistoryService.CrawlerPlayerTournament>>(
            Golden("player-history.json").ToJsonString(), DirectoryCrawlerClient.JsonOptions)!;

        var row = Assert.Single(rows);
        var empty = typeof(TournamentHistoryService.CrawlerPlayerTournament)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract" && p.GetValue(row) is null)
            .Select(p => p.Name)
            .ToList();
        Assert.True(empty.Count == 0, "Nicht gelesen: " + string.Join(", ", empty));
    }

    // ----- (3) Goldene Datei gegen den Crawler-Quelltext --------------------

    /// <summary>Goldene Antwort → Crawler-DTO (Quelldatei relativ zum Crawler-Repo, Klasse).</summary>
    public static TheoryData<string, string?, string, string> CrawlerDtos => new()
    {
        { "tournament.json", null, "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "TournamentResponse" },
        { "tournament.json", "groups", "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "TournamentGroupResponse" },
        { "players.json", null, "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "PlayerResponse" },
        { "team.json", null, "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "TeamResponse" },
        { "team.json", "players", "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "PlayerResponse" },
        { "pairings.json", null, "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "PairingResponse" },
        { "team-pairings.json", null, "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "TeamPairingResponse" },
        { "player-results.json", null, "src/ChessResultsCrawler/DTOs/TournamentDtos.cs", "PlayerResultResponse" },
        { "rounds-check.json", null, "src/ChessResultsCrawler/Services/RoundDetectionService.cs", "RoundCheckResult" },
        { "player-search.json", null, "src/ChessResultsCrawler/DTOs/PlayerSearchDtos.cs", "PlayerSearchResponse" },
        { "player-history.json", null, "src/ChessResultsCrawler/DTOs/PlayerSearchDtos.cs", "PlayerTournamentResponse" },
    };

    /// <summary>
    /// Jedes Feld der goldenen Datei traegt das Crawler-DTO (camelCase, wie ASP.NET es schreibt).
    /// Umbenannt oder entfernt = rot; ein zusaetzliches Crawler-Feld bricht keinen Leser und ist
    /// erlaubt — sonst hielte jedes neue Feld im Crawler das RookHub-Gate an (Codereview I2-010).
    /// Laeuft nur mit Crawler-Quelltext daneben — in der Stack-Kopie liegt er neben rookhub, in der
    /// CI setzt der Vertrags-Job <c>CRAWLER_REPO</c> (dann ohne Ueberspringen: fehlt die Quelle
    /// dort, ist das rot).
    /// </summary>
    [CrawlerSourceTheory]
    [MemberData(nameof(CrawlerDtos))]
    public void Golden_FieldsExistInTheCrawlerDto(string file, string? child, string source, string dto)
    {
        var path = Path.Combine(CrawlerSourceTheoryAttribute.CrawlerRepo()!, source);
        Assert.True(File.Exists(path), $"Crawler-Quelle fehlt: {source}");
        var crawler = DtoProperties(File.ReadAllText(path), dto)
            .Select(JsonNamingPolicy.CamelCase.ConvertName)
            .ToHashSet(StringComparer.Ordinal);
        var missing = Sample(file, child).Select(p => p.Key).Where(k => !crawler.Contains(k)).ToList();

        Assert.True(missing.Count == 0,
            $"{dto} im Crawler hat diese Felder der goldenen Datei {file} nicht (mehr): {string.Join(", ", missing)}");
    }

    /// <summary>Die Auto-Properties (<c>public T Name { get; set; }</c>) einer Klasse, in Reihenfolge.</summary>
    internal static List<string> DtoProperties(string source, string className)
    {
        var start = Regex.Match(source, $@"\bclass\s+{className}\b[^{{]*\{{");
        Assert.True(start.Success, $"Klasse {className} nicht gefunden");
        var depth = 1;
        var i = start.Index + start.Length;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        var body = source[(start.Index + start.Length)..i];
        return Regex.Matches(body, @"(?m)^\s*public\s+(?!static\b)[\w<>\[\]?,. ]+?\s+(\w+)\s*\{\s*get;")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    [Fact]
    public void DtoProperties_ReadsAutoPropertiesInOrder_AndSkipsMethods()
    {
        const string source = """
            public class FooResponse
            {
                public int Id { get; set; }
                public string? TeamName { get; set; }
                public List<BarResponse> Items { get; set; } = [];

                public static FooResponse FromEntity(Foo f) => new() { Id = f.Id };
            }
            public class BarResponse { public int Other { get; set; } }
            """;

        Assert.Equal(["Id", "TeamName", "Items"], DtoProperties(source, "FooResponse"));
    }

    // ----- Hilfen -----------------------------------------------------------

    internal static string RepoRoot()
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
}

/// <summary>
/// Laeuft nur, wenn der Crawler-Quelltext erreichbar ist: <c>CRAWLER_REPO</c> (CI) oder das
/// Nachbarverzeichnis <c>../chessresults_crawler</c> (Stack-Kopie). Sonst uebersprungen — aber
/// NICHT, wenn <c>CRAWLER_REPO</c> gesetzt ist: dann soll ein falscher Pfad rot werden, statt den
/// Vertrags-Job still gruen zu lassen.
/// </summary>
public sealed class CrawlerSourceTheoryAttribute : TheoryAttribute
{
    public CrawlerSourceTheoryAttribute()
    {
        if (CrawlerRepo() is null)
            Skip = "Crawler-Quelltext nicht gefunden (CRAWLER_REPO oder ../chessresults_crawler) — Vertragsabgleich nur mit Crawler daneben.";
    }

    internal static string? CrawlerRepo()
    {
        var env = Environment.GetEnvironmentVariable("CRAWLER_REPO");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        if (dir?.Parent is null) return null;

        var sibling = Path.Combine(dir.Parent.FullName, "chessresults_crawler");
        return File.Exists(Path.Combine(sibling, "src", "ChessResultsCrawler", "DTOs", "TournamentDtos.cs"))
            ? sibling : null;
    }
}
