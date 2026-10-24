using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>translate</c> — fehlende Sprachen uebersetzen lassen (eingereihte Partien, Rohbestand mit <c>--library</c>, Kurs mit <c>--course</c>).</summary>
internal static class TranslateCommand
{
    // ===== Uebersetzen ==========================================================

    // Die Anmerkungen eingereihter Partien in eine weitere Sprache uebersetzen lassen.
    //
    //   translate --to de [--limit n] [--force] [--game <analyse-id>] [--library n] [--shard i/n]
    //   translate --to de --course <bookId> [--parallel p]      (Kurs-Linien, siehe TranslateCourseAsync unten)
    //
    // Braucht ANTHROPIC__TEXTAPIKEY (bzw. Anthropic:TextApiKey) in der Umgebung — NICHT den Konto-Schluessel
    //   Anthropic:ApiKey, der gehoert allein dem Formular-Einlesen — ohne Schluessel passiert
    // nichts. Uebersetzt wird immer aus der QUELLE, nie aus einer Uebersetzung, und die Quelle wird
    // nie ueberschrieben: es entsteht ein eigener Satz mit Herkunft „Machine".
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var target = ctx.Options.String("--to");
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("Aufruf: translate --to <sprache> [--library n [--parallel p]] [--limit n] [--force] [--include-und] [--game <analyse-id>] [--shard i/n]");
            Console.Error.WriteLine("        translate --to <sprache> --course <bookId> [--parallel p]");
            return 1;
        }
        target = target.Trim().ToLowerInvariant();
        var limit = ctx.Options.Int("--limit") ?? int.MaxValue;
        var force = ctx.Options.Has("--force");
        // Standardmaessig ohne die Partien, deren Quellsprache nicht bestimmbar war — siehe
        // CommentTranslationService.LibraryCandidatesAsync. --include-und nimmt sie mit.
        var includeUnd = ctx.Options.Has("--include-und");
        var one = ctx.Options.Int("--game");

        // Aufteilung fuer parallele Laeufe: „--shard 0/4" nimmt jede vierte Partie. Ohne die Aufteilung
        // greifen zwei Laeufe dieselben Partien: einer von beiden bezahlt dann ein Ergebnis, das der
        // eindeutige Index wegwirft. Innerhalb EINES Laufs geht es einfacher mit --parallel.
        if (!ctx.Options.TryShard(out var shardIndex, out var shardCount, out var shard))
        {
            Console.Error.WriteLine($"--shard erwartet „i/n\" mit 0 <= i < n (ist „{shard}\").");
            return 1;
        }

        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        // Warnungen auf die Konsole: ein gescheiterter Aufruf (Modell weg, abgeschnitten, zu kurz) gibt
        // sonst nur eine 0 zurueck — genau wie „nichts zu tun", und ein Tage-Lauf liefe blind.
        using var loggers = ToolLoggers.WarningsToConsole();
        // Dieselbe Regel wie die API: eigene Hardware (TextLlm__BaseUrl) vor Claude (Anthropic__TextApiKey).
        var client = TextJsonClients.Create(config, loggers);
        if (!client.IsConfigured)
        {
            Console.Error.WriteLine("Weder TextLlm__BaseUrl (eigene Hardware) noch Anthropic__TextApiKey gesetzt — ohne wird nicht uebersetzt.");
            return 1;
        }

        if (ctx.Options.Int("--course") is int course)
            return await TranslateCourseAsync(ctx, client, loggers, config, target, course, ctx.Options.Int("--parallel"));

        if (ctx.Options.Int("--library") is int top)
            return await TranslateLibraryAsync(ctx, client, loggers, target, Math.Min(top, limit), force,
                Math.Clamp(ctx.Options.Int("--parallel") ?? 1, 1, 64), shardIndex, shardCount, includeUnd);

        await using var db = ctx.Db.New();
        var service = new CommentTranslationService(db, client, loggers.CreateLogger<CommentTranslationService>());
        var ids = one is int only ? [only] : await db.GameAnalyses.AsNoTracking()
            .OrderBy(g => g.Id).Select(g => g.Id).ToListAsync();

        int done = 0, lines = 0;
        var started = DateTime.UtcNow;
        foreach (var id in ids)
        {
            if (done >= limit) break;
            if (shardCount > 1 && id % shardCount != shardIndex) continue;
            var written = await service.TranslateAsync(id, target, force);
            if (written == 0) continue;
            done++;
            lines += written;
            Console.WriteLine($"  #{id,-6} {written,3} Anmerkungen nach {target}");
        }
        Console.WriteLine($"Uebersetzt: {done:N0} Partien · {lines:N0} Zeilen · Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
        return 0;
    }

    // Den ROHBESTAND uebersetzen, die besten Partien zuerst (CommentTranslationService.LibraryCandidatesAsync).
    //
    //   translate --to de --library 200000 --parallel 8
    //
    // Die Quell-Saetze legt der Lauf selbst an (was `comments --library` sonst vorab tut) — so beginnt die
    // Uebersetzung sofort bei den besten Partien, statt erst den ganzen Bestand zu zerlegen. Eine Partie,
    // deren Quelle schon die Zielsprache ist, kostet keinen Modell-Aufruf. Wiederholbar: was die Zielsprache
    // hat, faellt aus der Auswahl; ein Abbruch verliert hoechstens die Partien, die gerade in Arbeit waren.
    //
    // --parallel p: so viele Partien gleichzeitig, je eine eigene Datenbank-Verbindung. Auf dem Spark
    // (vLLM) zahlt sich das aus — er buendelt gleichzeitige Anfragen, eine einzelne laeuft mit einem
    // Bruchteil seines Durchsatzes. Ueber ~16 hinaus wartet nur die Schlange des Servers.
    private static async Task<int> TranslateLibraryAsync(ToolContext ctx, IClaudeJsonClient client, ILoggerFactory loggers, string target, int top,
        bool force, int parallel, int shardIndex, int shardCount, bool includeUnd)
    {
        List<int> ids;
        await using (var db = ctx.Db.New())
            ids = await CommentTranslationService.LibraryCandidatesAsync(db, target, top, includeUnd);
        if (shardCount > 1) ids = ids.Where(id => id % shardCount == shardIndex).ToList();
        Console.WriteLine($"{ids.Count:N0} Partien ohne „{target}\" · {parallel} parallel · Modell {client.TranslationModel}"
                          + (includeUnd ? " · mit unbestimmter Quellsprache" : " · ohne unbestimmte Quellsprache"));

        var queue = new System.Collections.Concurrent.ConcurrentQueue<int>(ids);
        int seen = 0, done = 0, lines = 0, empty = 0, errors = 0, pauseNoted = 0;
        var started = DateTime.UtcNow;
        var translationLogger = loggers.CreateLogger<CommentTranslationService>();
        // Laenger als OpenAiJsonClient.UnreachableFor: nach der Pause darf wieder probiert werden.
        var unreachablePause = TimeSpan.FromSeconds(75);

        async Task Worker()
        {
            await using var db = ctx.Db.New();
            var sets = new CommentSetService(db, NullLogger<CommentSetService>.Instance);
            var service = new CommentTranslationService(db, client, translationLogger);
            while (queue.TryDequeue(out var id))
            {
                try
                {
                    await sets.EnsureSourceForLibraryAsync(id);
                    var written = await service.TranslateLibraryGameAsync(id, target, force);
                    if (written > 0)
                    {
                        Interlocked.Increment(ref done);
                        Interlocked.Add(ref lines, written);
                        Console.WriteLine($"  #{id,-6} {written,3} Anmerkungen nach {target}");
                    }
                    else if (client.IsUnreachable)
                    {
                        // Das Modell antwortet nicht: die Partie kommt zurueck in die Reihe und der Lauf wartet, statt die
                        // Kandidatenliste in Sekunden „ohne Ergebnis" durchzureichen.
                        queue.Enqueue(id);
                        if (Interlocked.Exchange(ref pauseNoted, 1) == 0)
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss} WRN Modell nicht erreichbar — Partien zurueck in die Reihe, {unreachablePause.TotalSeconds:0} s Pause.");
                        await Task.Delay(unreachablePause);
                        Interlocked.Exchange(ref pauseNoted, 0);
                        continue;
                    }
                    else Interlocked.Increment(ref empty);
                }
                catch (Exception ex)
                {
                    // Eine Partie darf den Lauf nicht beenden (Datenbank kurz weg, eine kaputte Quelle).
                    Interlocked.Increment(ref errors);
                    Console.Error.WriteLine($"  #{id,-6} FEHLER {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    db.ChangeTracker.Clear();   // sonst waechst der Kontext ueber zehntausende Partien
                }

                var n = Interlocked.Increment(ref seen);
                if (n % 100 == 0)
                {
                    var elapsed = DateTime.UtcNow - started;
                    var rest = TimeSpan.FromSeconds(elapsed.TotalSeconds / n * (ids.Count - n));
                    Console.WriteLine($"--- {n:N0}/{ids.Count:N0} · uebersetzt {done:N0} · ohne Ergebnis {empty:N0} · Fehler {errors:N0} · {elapsed:d\\.hh\\:mm} · Rest ~{rest:d\\.hh\\:mm}");
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, parallel).Select(_ => Task.Run(Worker)));
        Console.WriteLine($"Uebersetzt: {done:N0} Partien · {lines:N0} Zeilen · ohne Ergebnis {empty:N0} · Fehler {errors:N0} · Dauer {DateTime.UtcNow - started:d\\.hh\\:mm\\:ss}");
        return 0;
    }

    // Einen KURS uebersetzen (Zug-Kommentare, Einleitung, Linien-Titel, Kapitelnamen) — direkt, ohne Auftrag:
    // fuer Messungen und Nachhilfe von Hand.
    //
    //   translate --to de --course 123 [--parallel 4]
    //
    // Ruft CourseTranslationService.TranslateCourseAsync: Quellsprache bestimmen (einmal, steht danach am
    // Buch), nur fehlende/veraltete Texte ans Modell, Kapitelnamen in EINER Fuhre. Wiederholbar — was fertig
    // ist, kostet keinen Aufruf. Strg+C bricht ab; die laufenden Linien werden verworfen, das Fertige bleibt.
    // Der Dienst oeffnet je Linie einen eigenen Scope (eigener DbContext) — deshalb hier ein kleiner
    // Dienst-Container statt ctx.Db.New().
    private static async Task<int> TranslateCourseAsync(ToolContext ctx, IClaudeJsonClient client, ILoggerFactory loggers, IConfiguration config,
        string target, int bookId, int? parallel)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(client);
        services.AddSingleton(loggers);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddDbContext<AppDbContext>(o => o.UseMySql(ctx.Db.ConnectionString, new MariaDbServerVersion(new Version(11, 0, 0))));
        services.AddScoped<CourseTranslationService>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<CourseTranslationService>();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var started = DateTime.UtcNow;
        Console.WriteLine($"Kurs {bookId} nach „{target}\" · Modell {client.TranslationModel}");
        try
        {
            var run = await service.TranslateCourseAsync(bookId, target,
                (p, _) =>
                {
                    Console.WriteLine($"--- {p.LinesDone + p.LinesFailed:N0}/{p.LinesTotal:N0} · fertig {p.LinesDone:N0} · gescheitert {p.LinesFailed:N0} · {DateTime.UtcNow - started:hh\\:mm\\:ss}");
                    return Task.CompletedTask;
                },
                parallel, cts.Token);
            Console.WriteLine($"Ergebnis: {run.Status} · Quelle {run.SourceLanguage ?? "?"} · Linien {run.LinesDone:N0}/{run.LinesTotal:N0}"
                              + $" · gescheitert {run.LinesFailed:N0} · Kapitel {run.ChaptersTotal:N0} (ohne Uebersetzung {run.ChaptersMissing:N0})"
                              + $" · Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
            return run.Status is CourseTranslationRunStatus.Done or CourseTranslationRunStatus.NothingToDo
                or CourseTranslationRunStatus.SameLanguage ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Abgebrochen — die laufenden Linien sind verworfen, das Fertige bleibt.");
            return 130;
        }
    }
}
