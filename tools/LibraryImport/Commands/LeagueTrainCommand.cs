using System.Text.Json;
using RookHub.Api.Services.League;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary>
/// <c>league-train --region bayern [--holdout S[,S2…]] [--out datei] [--features a,b,…]</c> — Prognose-Modell einer
/// Region trainieren + Backtest, siehe <see cref="LeagueTraining.TrainAsync"/>.
/// </summary>
internal static class LeagueTrainCommand
{
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var region = ctx.Options.String("--region") ?? LeagueRegions.Bayern;
        if (!LeagueRegions.Valid(region)) { Console.Error.WriteLine($"Unbekannte Region {region}"); return 1; }
        var features = ctx.Options.String("--features")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                       ?? LeagueTraining.DefaultFeatures(region).ToList();
        var holdout = ctx.Options.String("--holdout")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var tirol = LeagueModel.FromEmbedded();
        await using var db = ctx.Db.New(TimeSpan.FromMinutes(10));
        var rep = await LeagueTraining.TrainAsync(db, region, features, holdout, region == LeagueRegions.Tirol ? null : tirol, default);

        Console.WriteLine($"Datenlage Region {region}:");
        Console.WriteLine($"  {"Saison",-8}{"Ligen",6}{"Kämpfe",8}{"Zeilen",8}{"Gemeldete",10}{"mit FIDE",10}");
        foreach (var s in rep.Seasons)
            Console.WriteLine($"  {s.Season,-8}{s.Leagues,6}{s.Groups,8}{s.Rows,8}{s.Players,10}{s.PlayersWithFide,10}"
                + $" ({(s.Players == 0 ? 0 : 100.0 * s.PlayersWithFide / s.Players):0} %)");
        Console.WriteLine();
        Console.Write(LeagueTraining.Report($"Backtest Holdout {string.Join(", ", rep.Holdout)} — Modell {region} (trainiert je Saison nur auf früheren):", rep.Backtest));
        if (rep.Reference is { } reference)
            Console.Write(LeagueTraining.Report("Backtest dieselben Daten — Tiroler Modell (heute im Produkt):", reference));
        Console.WriteLine();
        Console.WriteLine($"Endgültig: {rep.Rows} Zeilen, Saisonen {string.Join(" ", rep.TrainedOn)}");
        for (var i = 0; i < rep.Model.Features.Count; i++) Console.WriteLine($"  {rep.Model.Features[i],-14}{rep.Model.Weights[i],+8:+0.000;-0.000}");
        Console.WriteLine();
        Console.WriteLine("Hits je (Stufe, Lage) für LeagueViewBuilder:");
        foreach (var ((lvl, phase), t) in rep.Backtest.ByPhase)
            Console.WriteLine($"  [({lvl}, \"{phase}\")] = {t.HitRate:0.00},   // {t.Matches} Kämpfe");
        if (ctx.Options.String("--out") is { } outFile)
        {
            await File.WriteAllTextAsync(outFile, rep.Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"geschrieben: {outFile}");
        }
        return 0;
    }
}
