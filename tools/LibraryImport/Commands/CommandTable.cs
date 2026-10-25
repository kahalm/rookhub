using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary>
/// Befehlsname (erstes Argument, Gross-/Kleinschreibung zaehlt) → Befehl. Neuer Befehl = eine Klasse unter
/// <c>Commands/</c> mit <c>RunAsync(ToolContext)</c> und eine Zeile hier; die Uebersicht steht im Kopf von Program.cs.
/// </summary>
public static class CommandTable
{
    public static IReadOnlyDictionary<string, Func<ToolContext, Task<int>>> All { get; } =
        new Dictionary<string, Func<ToolContext, Task<int>>>(StringComparer.Ordinal)
        {
            ["import"] = ImportCommand.RunAsync,
            ["dedupe"] = DedupeCommand.RunAsync,
            ["rehash-results"] = RehashResultsCommand.RunAsync,
            ["openings"] = OpeningsCommand.RunAsync,
            ["languages"] = LanguagesCommand.RunAsync,
            ["score"] = ScoreCommand.RunAsync,
            ["queue"] = QueueCommand.RunAsync,
            ["comments"] = CommentsCommand.RunAsync,
            ["analysis-openings"] = AnalysisOpeningsCommand.RunAsync,
            ["tree"] = TreeCommand.RunAsync,
            ["translate"] = TranslateCommand.RunAsync,
            ["embed"] = EmbedCommand.RunAsync,
            ["resplit"] = ResplitCommand.RunAsync,
            ["stats"] = StatsCommand.RunAsync,
            ["migrate"] = MigrateCommand.RunAsync,
            ["league-import"] = LeagueImportCommand.RunAsync,
            ["league-train"] = LeagueTrainCommand.RunAsync,
            ["pat-cap"] = PatCapCommand.RunAsync,
        };

    /// <summary>Ohne Argument: <c>stats</c>.</summary>
    public const string Default = "stats";
}
