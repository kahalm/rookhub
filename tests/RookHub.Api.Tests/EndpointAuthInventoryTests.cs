using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace RookHub.Api.Tests;

/// <summary>
/// Statisches Inventar der HTTP-Fläche (Reflection über alle Controller der API-Assembly).
/// Hintergrund: Controller-Tests instanziieren den Controller DIREKT — Attribute wie
/// <c>[Authorize]</c>/<c>[AllowAnonymous]</c> und die Routen-Templates werden dabei nie ausgewertet.
/// Eine fehlende Klassen-Annotation (z. B. WeeklyPostController ohne <c>[Authorize]</c>) oder ein
/// versehentliches <c>[AllowAnonymous]</c> geht heute grün durch die CI und fällt erst in Prod auf.
/// Ein echter Pipeline-Test scheidet aus (siehe Begründung in <see cref="PatScopeFenceTests"/>:
/// <c>Program.cs</c> migriert beim Start gegen eine echte MariaDB), deshalb diese Attribut-Ebene.
/// </summary>
public class EndpointAuthInventoryTests
{
    /// <summary>
    /// Whitelist der ANONYM erreichbaren Endpoints (kein Login nötig). Jeder Eintrag ist eine bewusste
    /// Entscheidung — wächst die Liste durch eine Änderung, MUSS das hier nachgezogen werden.
    /// Umgekehrt: verschwindet ein Eintrag, wurde etwas hinter Auth geschoben (auch bewusst zu prüfen).
    /// </summary>
    private static readonly string[] ExpectedAnonymous =
    [
        "GET /api/book-puzzles/books",                               // BookPuzzleController.GetBooks
        "GET /api/book-puzzles/by-line-id",                          // BookPuzzleController.GetByLineId
        "GET /api/book-puzzles/daily/hall-of-fame",                  // BookPuzzleController.GetDailyHallOfFame
        "GET /api/book-puzzles/daily/leaderboard",                   // BookPuzzleController.GetDailyLeaderboard
        "GET /api/book-puzzles/daily/{date}",                        // BookPuzzleController.GetDaily
        "GET /api/book-puzzles/random",                              // BookPuzzleController.GetRandom
        "GET /api/book-puzzles/{id:int}/next",                       // BookPuzzleController.GetNextInBook
        "GET /api/book-puzzles/{id:int}/random",                     // BookPuzzleController.GetRandomInBook
        "GET /api/book-puzzles/{id:int}/results",                    // BookPuzzleController.GetResults
        "GET /api/book-puzzles/{id:int}/track-counts",               // BookPuzzleController.TrackCounts
        "GET /api/book-puzzles/{id:int}",                            // BookPuzzleController.GetById
        "GET /api/bot/player-progress/{discordId}",                  // BotStatsController.GetPlayerProgress
        "GET /api/calculations/books/{bookId}/public",                // CalculationController.GetPublicBook
        "GET /api/courses/by-slug/{slug}",                           // CourseController.ResolvePublicSlug
        "GET /api/courses/by-slug/{slug}/{chapter}",                 // CourseController.ResolvePublicSlugChapter
        "GET /api/courses/{bookId}/public",                          // CourseController.GetPublicCourse
        "GET /api/endless/progress/anonymous",                       // EndlessController.GetAnonymousProgress
        "GET /api/games/shared/{token}",                             // GamesController.GetShared
        "GET /api/menu",                                             // MenuController.Get
        "GET /api/og/img/{kind}/{id}.png",                           // OgController.Image
        "GET /api/og/render",                                        // OgController.Render
        "GET /api/profile/{username}",                               // ProfileController.GetPublicProfile
        "GET /api/puzzles/random",                                   // PuzzleController.GetRandom
        "GET /api/puzzles/rating-range",                             // PuzzleController.GetRatingRange
        "GET /api/puzzles/stats/anonymous",                          // PuzzleController.GetAnonymousStats
        "GET /api/puzzles/themes",                                   // PuzzleController.GetThemes
        "GET /api/puzzles/{id:int}",                                 // PuzzleController.GetById
        "GET /api/repertoires/shared-line/{token}",                  // RepertoireController.GetSharedLine
        "GET /api/tournaments/{id}/pairings",                        // TournamentProxyController.GetPairings
        "GET /api/tournaments/{id}/players/{snr:int}/results",       // TournamentProxyController.GetPlayerResults
        "GET /api/tournaments/{id}/players",                         // TournamentProxyController.GetPlayers
        "GET /api/tournaments/{id}/teams/{snr}",                     // TournamentProxyController.GetTeamDetail
        "GET /api/tournaments/{id}/teams",                           // TournamentProxyController.GetTeams
        "GET /api/tournaments/{id}",                                 // TournamentProxyController.GetById
        "GET /api/weekly-posts/{id}/puzzles",                        // WeeklyPostController.GetPuzzles
        "GET /api/weekly-posts/{id}/results",                        // WeeklyPostController.GetResults
        "GET /api/weekly-posts/{id}",                                // WeeklyPostController.GetById
        "GET /api/weekly-posts",                                     // WeeklyPostController.GetAll
        "POST /api/auth/forgot-password",                            // AuthController.ForgotPassword
        "POST /api/auth/login",                                      // AuthController.Login
        "POST /api/auth/register",                                   // AuthController.Register
        "POST /api/auth/reset-password",                             // AuthController.ResetPassword
        "POST /api/book-puzzles/{id:int}/attempt/anonymous",         // BookPuzzleController.RecordAnonymousAttempt
        "POST /api/book-puzzles/{id:int}/track",                     // BookPuzzleController.Track
        "POST /api/ci/build-report",                                 // CiBuildReportController.Report
        "POST /api/ci/gh-webhook",                                   // CiBuildReportController.GithubWebhook
        "POST /api/client-log",                                      // ClientLogController.Post
        "POST /api/endless/sessions/anonymous",                      // EndlessController.RecordAnonymousSession
        "POST /api/endless/sessions/bulk/anonymous",                 // EndlessController.BulkImportAnonymousSessions
        "POST /api/extension/chessable/review-lines/anon",           // ExtensionController.ChessableReviewLinesAnon (uid-based, token-less)
        "POST /api/puzzles/random-batch",                            // PuzzleController.GetRandomBatch
        "POST /api/puzzles/{id}/attempt/anonymous",                  // PuzzleController.RecordAnonymousAttempt
        "PUT /api/endless/progress/anonymous",                       // EndlessController.SaveAnonymousProgress
    ];

    private record Endpoint(string Method, string Template, bool Anonymous);

    private static List<Endpoint> Inventory()
    {
        var result = new List<Endpoint>();
        var asm = typeof(RookHub.Api.Controllers.ProfileController).Assembly;
        foreach (var type in asm.GetTypes()
                     .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var prefix = (type.GetCustomAttribute<RouteAttribute>(inherit: true)?.Template ?? "")
                .Replace("[controller]", type.Name.EndsWith("Controller", StringComparison.Ordinal)
                    ? type.Name[..^"Controller".Length]
                    : type.Name, StringComparison.Ordinal);
            var classAnon = type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            var classAuth = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null))
            {
                var verbs = m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();
                if (verbs.Count == 0) continue;   // keine Action (Attribut-Routing ist Pflicht bei [ApiController])
                var anon = classAnon || m.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
                           || (!classAuth && !m.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any());
                foreach (var v in verbs)
                {
                    var t = v.Template ?? "";
                    var full = t.StartsWith('/') || t.StartsWith("~/", StringComparison.Ordinal)
                        ? t.TrimStart('~').TrimStart('/')
                        : string.Join('/', new[] { prefix, t }.Where(s => s.Length > 0));
                    foreach (var verb in v.HttpMethods)
                        result.Add(new Endpoint(verb, "/" + full, anon));
                }
            }
        }
        return result;
    }

    [Fact]
    public void AnonymousEndpoints_MatchWhitelist()
    {
        var actual = Inventory().Where(e => e.Anonymous)
            .Select(e => $"{e.Method} {e.Template}")
            .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var expected = ExpectedAnonymous.OrderBy(s => s, StringComparer.Ordinal).ToList();

        var added = actual.Except(expected).ToList();
        var removed = expected.Except(actual).ToList();
        Assert.True(added.Count == 0,
            "NEU anonym erreichbar (gewollt? sonst [Authorize] ergänzen; wenn gewollt: Whitelist erweitern):\n  "
            + string.Join("\n  ", added));
        Assert.True(removed.Count == 0,
            "Nicht mehr anonym erreichbar (Whitelist aufräumen):\n  " + string.Join("\n  ", removed));
    }

    [Fact]
    public void NoDuplicateRouteTemplates()
    {
        // Zwei Actions mit identischem Verb+Template werfen erst zur Laufzeit eine
        // AmbiguousMatchException — und zwar erst beim ersten Aufruf, nicht beim Start.
        var dupes = Inventory()
            .GroupBy(e => $"{e.Method} {e.Template}", StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(dupes.Count == 0, "Doppelte Routen: " + string.Join(", ", dupes));
    }
}
