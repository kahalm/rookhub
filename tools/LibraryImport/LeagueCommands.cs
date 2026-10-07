using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services.League;

namespace RookHub.Tools.LibraryImport;

/// <summary>
/// LeagueHub-Wartung ohne API-Instanz (2026-10-07, „eigenes Prognose-Modell für Bayern"):
/// <list type="bullet">
/// <item><c>migrate</c> — Schema per EF-Migrationen anlegen (für eine WEGWERF-MariaDB zum Trainieren).</item>
/// <item><c>league-import --source ligamanager --url &lt;Liga-URL&gt; [--boards n] [--profiles]</c> /
///   <c>league-import --source zugspitze --liga &lt;id&gt; --season &lt;JJJJ/JJ&gt; [--boards n]</c> /
///   <c>league-import --batch &lt;datei&gt;</c> (je Zeile „ligamanager &lt;url&gt;" oder „zugspitze &lt;liga&gt; &lt;saison&gt;", # = Kommentar)
///   — ruft <see cref="LigamanagerSource"/>/<see cref="ZugspitzeSource"/> direkt; Ansichten werden NICHT neu gerechnet,
///   Spielerkarten nur mit <c>--profiles</c>.</item>
/// <item><c>league-train --region bayern [--holdout S[,S2…]] [--out datei] [--features a,b,…]</c> — siehe <see cref="LeagueTraining.TrainAsync"/>.</item>
/// </list>
/// </summary>
internal static class LeagueCommands
{
    private const string UserAgent = "RookHub-LeagueHub/1.0 (+https://rookhub.oberschmid.homes)";

    public static async Task<int> MigrateAsync(Func<AppDbContext> newDb)
    {
        await using var db = newDb();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Console.WriteLine($"{pending.Count} Migrationen offen …");
        await db.Database.MigrateAsync();
        Console.WriteLine("Schema aktuell.");
        return 0;
    }

    private static ServiceProvider Http()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(LigamanagerSource.ClientName, c =>
        {
            c.BaseAddress = new Uri(LigamanagerSource.SiteUrl + "/");
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        services.AddHttpClient(ZugspitzeSource.ClientName, c =>
        {
            c.BaseAddress = new Uri(ZugspitzeSource.SiteUrl + "/");
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        return services.BuildServiceProvider();
    }

    public static async Task<int> ImportAsync(string[] args, Func<AppDbContext> newDb)
    {
        var jobs = new List<string[]>();
        if (Arg(args, "--batch") is { } file)
            jobs.AddRange(File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        else if (Arg(args, "--source") is "ligamanager" && Arg(args, "--url") is { } url) jobs.Add(["ligamanager", url]);
        else if (Arg(args, "--source") is "zugspitze" && Arg(args, "--liga") is { } liga) jobs.Add(["zugspitze", liga, Arg(args, "--season") ?? ""]);
        else
        {
            Console.Error.WriteLine("league-import --source ligamanager --url <url> | --source zugspitze --liga <id> --season <JJJJ/JJ> | --batch <datei>");
            return 1;
        }
        var boards = int.TryParse(Arg(args, "--boards"), out var b) ? b : (int?)null;
        var profiles = args.Contains("--profiles");
        using var http = Http();
        var factory = http.GetRequiredService<IHttpClientFactory>();
        var failed = 0;
        foreach (var job in jobs)
        {
            await using var db = newDb();
            db.ChangeTracker.AutoDetectChangesEnabled = true;   // die Leser schreiben über den Change-Tracker
            var league = new LeagueService(db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
            try
            {
                if (job[0] == "ligamanager")
                {
                    var lref = LigamanagerSource.LeagueRef.Parse(job.ElementAtOrDefault(1))
                               ?? throw new ArgumentException($"keine Ligamanager-Adresse: {job.ElementAtOrDefault(1)}");
                    var src = new LigamanagerSource(db, factory, league, NullLogger<LigamanagerSource>.Instance);
                    var r = await src.ImportAsync(lref, false, default, boards, rebuildViews: false, importProfiles: profiles);
                    Console.WriteLine($"ligamanager {lref.Path}: {r.Name} {r.Season} Stufe {r.Level} — {r.Counts.Rounds} Runden, "
                        + $"{r.Counts.BoardGamesPlayed}/{r.Counts.BoardGames} Bretter, {r.Counts.Players} Spieler ({r.Counts.PlayersWithFide} FIDE), "
                        + $"+{r.FideFilled} FIDE ergänzt, {r.ProfileGames} PGN");
                }
                else if (job[0] == "zugspitze")
                {
                    var lref = ZugspitzeSource.LeagueRef.Of(int.TryParse(job.ElementAtOrDefault(1), out var id) ? id : null, job.ElementAtOrDefault(2))
                               ?? throw new ArgumentException($"keine Zugspitze-Liga: {string.Join(' ', job)}");
                    var src = new ZugspitzeSource(db, factory, league, NullLogger<ZugspitzeSource>.Instance);
                    var r = await src.ImportAsync(lref, false, default, boards, rebuildViews: false);
                    Console.WriteLine($"zugspitze {lref.Path}: {r.Name} {r.Season} Stufe {r.Level} — {r.Counts.RoundsPlayed}/{r.Counts.Rounds} Runden, "
                        + $"{r.Counts.BoardGamesPlayed} Bretter, {r.Counts.Players} Spieler, +{r.FideFilled} FIDE ergänzt");
                }
                else throw new ArgumentException($"unbekannte Quelle {job[0]}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                Console.Error.WriteLine($"FEHLER {string.Join(' ', job)}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"{jobs.Count - failed} von {jobs.Count} eingespielt.");
        return failed == 0 ? 0 : 2;
    }

    public static async Task<int> TrainAsync(string[] args, Func<AppDbContext> newDb)
    {
        var region = Arg(args, "--region") ?? LeagueRegions.Bayern;
        if (!LeagueRegions.Valid(region)) { Console.Error.WriteLine($"Unbekannte Region {region}"); return 1; }
        var features = Arg(args, "--features")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                       ?? LeagueTraining.DefaultFeatures(region).ToList();
        var holdout = Arg(args, "--holdout")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var tirol = LeagueModel.FromEmbedded();
        await using var db = newDb();
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
        if (Arg(args, "--out") is { } outFile)
        {
            await File.WriteAllTextAsync(outFile, rep.Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"geschrieben: {outFile}");
        }
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
