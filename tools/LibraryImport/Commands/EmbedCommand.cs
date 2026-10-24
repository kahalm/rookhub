using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>embed</c> — Kommentare des Bestands fuer „Frag die Kommentare" einbetten.</summary>
internal static class EmbedCommand
{
    // ===== Einbetten („Frag die Kommentare", 0.536.0) ===========================

    // Die Kommentare der noch nicht eingebetteten Partien in Stücke zerlegen und über das Embedding-Modell (Embedding__BaseUrl,
    // OpenAI-kompatibel — ein Pooling-Modell auf dem DGX Spark) in CommentEmbeddings ablegen. Wiederholbar: was schon da ist,
    // bleibt, der nächste Lauf macht weiter. Portionsweise, damit ein Abbruch nichts Großes verwirft.
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var limit = ctx.Options.Int("--limit") ?? int.MaxValue;
        var batch = ctx.Options.Int("--batch") ?? 32;
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var embedder = new OpenAiTextEmbedder(http, config, NullLogger.Instance);
        if (!embedder.IsConfigured)
        {
            Console.Error.WriteLine("Embedding__BaseUrl fehlt (OpenAI-kompatibler Embedding-Server, z. B. vLLM auf dem Spark).");
            return 1;
        }
        var started = DateTime.UtcNow;
        // LibraryGameService braucht nur die Suche (MarkKnownAsync) — beim Einbetten wird sie nicht angefasst.
        CommentSearchService Service(AppDbContext db)
            => new(db, embedder, new LibraryGameService(db, null!), NullLogger<CommentSearchService>.Instance);

        // Die Auswahl EINMAL je Lauf: sie ist teuer (Prod 37 s bei 10 000 eingebetteten Partien) und riss mit dem
        // 30-s-Standard den Lauf vom 2026-09-26 ab — vorher lief sie für jede Portion von 500 Partien neu.
        List<int> pending;
        await using (var db = ctx.Db.New(TimeSpan.FromMinutes(10)))
            pending = await Service(db).PendingGameIdsAsync(limit, CancellationToken.None);
        Console.WriteLine($"{pending.Count:N0} Partien ohne Einbettung");

        int games = 0, chunks = 0, failed = 0;
        foreach (var slice in pending.Chunk(500))
        {
            await using var db = ctx.Db.New();
            var step = await Service(db).EmbedGamesAsync(slice, batch, CancellationToken.None);
            games += step.Games;
            chunks += step.Chunks;
            failed += step.Failed;
            var rate = games / Math.Max(1, (DateTime.UtcNow - started).TotalSeconds);
            Console.WriteLine($"{games} Partien, {chunks} Stücke eingebettet ({rate:0.0} Partien/s), {failed} gescheitert");
            if (step.Games == 0 && step.Failed > 0) break; // nur Fehlschläge — das Modell antwortet nicht, nicht endlos weiterversuchen
        }
        Console.WriteLine($"Fertig: {games} Partien, {chunks} Stücke, {failed} gescheitert, {(DateTime.UtcNow - started).TotalMinutes:0.0} min");
        return failed > 0 && games == 0 ? 2 : 0;
    }
}
