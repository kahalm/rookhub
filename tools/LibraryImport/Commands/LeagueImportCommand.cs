using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.League;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary>
/// <c>league-import --source ligamanager --url &lt;Liga-URL&gt; [--boards n] [--profiles]</c> /
/// <c>league-import --source zugspitze --liga &lt;id&gt; --season &lt;JJJJ/JJ&gt; [--boards n]</c> /
/// <c>league-import --batch &lt;datei&gt;</c> (je Zeile „ligamanager &lt;url&gt;" oder „zugspitze &lt;liga&gt; &lt;saison&gt;", # = Kommentar)
/// — LeagueHub-Wartung ohne API-Instanz (2026-10-07, „eigenes Prognose-Modell für Bayern"): ruft
/// <see cref="LigamanagerSource"/>/<see cref="ZugspitzeSource"/> direkt; Ansichten werden NICHT neu gerechnet,
/// Spielerkarten nur mit <c>--profiles</c>.
/// </summary>
internal static class LeagueImportCommand
{
    private const string UserAgent = "RookHub-LeagueHub/1.0 (+https://rookhub.oberschmid.homes)";

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

    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var jobs = new List<string[]>();
        if (ctx.Options.String("--batch") is { } file)
            jobs.AddRange(File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        else if (ctx.Options.String("--source") is "ligamanager" && ctx.Options.String("--url") is { } url) jobs.Add(["ligamanager", url]);
        else if (ctx.Options.String("--source") is "zugspitze" && ctx.Options.String("--liga") is { } liga) jobs.Add(["zugspitze", liga, ctx.Options.String("--season") ?? ""]);
        else
        {
            Console.Error.WriteLine("league-import --source ligamanager --url <url> | --source zugspitze --liga <id> --season <JJJJ/JJ> | --batch <datei>");
            return 1;
        }
        var boards = int.TryParse(ctx.Options.String("--boards"), out var b) ? b : (int?)null;
        var profiles = ctx.Options.Has("--profiles");
        using var http = Http();
        var factory = http.GetRequiredService<IHttpClientFactory>();
        var failed = 0;
        foreach (var job in jobs)
        {
            await using var db = ctx.Db.New();
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
}
