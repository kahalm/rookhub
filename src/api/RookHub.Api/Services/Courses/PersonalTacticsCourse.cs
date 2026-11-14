using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.DTOs.Puzzles;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Kurs „Persönliche Taktiken“ (Wunsch 2026-11-14): „Taktiken trainieren“ unter den Empfohlenen Übungen einer Partie
/// (<see cref="GameExercises"/>) legt die Aufgaben dieser Partie als KAPITEL in einen persönlichen Kurs — einer je Konto, je
/// Partie ein Kapitel — und der normale Kurs-Löser spielt sie sequenziell (Fortschritt und Statistik wie bei jedem Kurs).
///
/// <para><b>Ein Kurs je Konto</b>: das Buch heißt intern <see cref="FileOf"/> (<c>personal-tactics-u{userId}.pgn</c>). Erkannt wird
/// es NUR am Dateinamen, nie am Anzeigenamen — Umbenennen verdoppelt nichts, und der eindeutige Index auf
/// <c>Books.FileName</c> lässt auch bei gleichzeitigen Klicks nur eines entstehen.</para>
///
/// <para><b>Ein Kapitel je Partie</b>: die Linien einer Partie tragen die <c>LineId</c>
/// <c>{Datei}:{club|own}{PartieId}:{nn}</c>. Gibt es solche Linien schon, wird ihr Kapitel geöffnet (auch wenn es umbenannt
/// wurde) und nichts neu berechnet — das Kapitel hält fest, was beim ersten Klick empfohlen war. Die laufende Nummer macht die
/// <c>LineId</c> je Partie vorhersagbar: zwei gleichzeitige Anfragen stoßen am eindeutigen Index zusammen, die zweite liest das
/// Kapitel der ersten.</para>
///
/// <para><b>Linien</b>: Lichess-Aufgabe → <c>Fen</c> vor dem Gegnerzug, <c>Moves</c> = Gegnerzug + Lösung, <c>StartPly = 0</c>
/// (klassische Form, dieselbe wie die Taktik-Ernte); lösbares Buchdiagramm → Diagrammstellung und Buchlinie, die Löserseite
/// bestimmt <see cref="BookSolverLine"/> (0.973.1): gewinnt die Seite am Zug, <c>StartPly = -1</c> (Löser am Zug); macht ihr
/// Gegner den Mattzug, <c>StartPly = 0</c> (erster Zug = Gegnerzug) — der Löser spielt nie die Seite, die mattgesetzt wird.
/// „Beispiel im Buch“ ohne bestätigte Lösung oder ohne bestimmbare Löserseite hat nichts zu lösen und fehlt. Den Bestand von vor
/// 0.973.1 berichtigt <see cref="PersonalTacticsLineRepair"/> beim Start. Reihenfolge wie im Abschnitt.
/// Themen stehen in <c>Tags</c>, die Herkunft im <c>Title</c> („19. · Lichess AbCdE“), <c>Source</c> = <see cref="LineSource"/>,
/// und <c>SourceGame</c> führt aus dem Löser zurück zur Partie (nur, wenn der Nutzer sie auf ihrem eigenen Weg sehen darf —
/// nicht über einen Teilen-Link).</para>
///
/// <para>Kein Quell-PGN: wie ein handgepflegter Kurs steht <c>ImportVersion</c> auf der aktuellen Pipeline-Version und wird bei
/// jedem Aufruf nachgezogen — ein „Aktualisieren“-Lauf fasst das Buch nie an (ohne Quelle <see cref="StaleAction.Manual"/>).</para>
/// </summary>
public sealed class PersonalTacticsCourse(AppDbContext db, GameExercises exercises)
{
    /// <summary>Kennzeichen des Kurses: der interne Dateiname. Bewusst NICHT <c>tactics-…</c> — das ist die Taktik-Ernte.</summary>
    public const string FilePrefix = "personal-tactics-u";
    public static string FileOf(int userId) => $"{FilePrefix}{userId.ToString(CultureInfo.InvariantCulture)}.pgn";
    /// <summary><c>BookPuzzle.Source</c> der Linien.</summary>
    public const string LineSource = "game-exercise";

    public enum GameKind { Club, Own }

    /// <summary>Die Partie des Klicks. <paramref name="Linkable"/>: der Nutzer erreicht sie unter <c>/club-games/{id}</c> bzw.
    /// <c>/games/{id}</c> (sonst — Teilen-Link — bekommt die Linie keinen Rücksprung).</summary>
    public sealed record Game(GameKind Kind, int Id, bool Linkable)
    {
        internal string Key => (Kind == GameKind.Club ? "club" : "own") + Id.ToString(CultureInfo.InvariantCulture);
        internal string SourceKind => Kind == GameKind.Club ? "club" : "own";
    }

    /// <summary>Eigene Partie des Kontos; <c>null</c> = unbekannt oder fremd.</summary>
    public async Task<Game?> OwnGameAsync(int userId, int savedGameId, CancellationToken ct = default) =>
        await db.SavedGames.AsNoTracking().AnyAsync(g => g.Id == savedGameId && g.UserId == userId, ct)
            ? new Game(GameKind.Own, savedGameId, true) : null;

    /// <summary>Eigene Partie über ihren Teilen-Link <c>/g/{token}</c>; <c>null</c> = unbekannter Link.</summary>
    public async Task<Game?> SharedOwnGameAsync(int userId, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = await db.SavedGames.AsNoTracking().Where(x => x.ShareToken == token).Select(x => new { x.Id, x.UserId }).FirstOrDefaultAsync(ct);
        return g is null ? null : new Game(GameKind.Own, g.Id, g.UserId == userId);
    }

    /// <summary>Vereinspartie über ihren eigenen Link <c>/cg/{token}</c>; <c>null</c> = ungültig oder widerrufen.</summary>
    public async Task<Game?> ClubLinkGameAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var id = await db.LeagueClubGames.AsNoTracking().Where(g => g.ShareToken == token).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
        return id is int i ? new Game(GameKind.Club, i, false) : null;
    }

    /// <summary>Der Verein einer Vereinspartie (Vorschlag für die Vereinswahl, wie <c>GET …/club/games/{id}</c>).</summary>
    public Task<int?> ClubOfGameAsync(int clubGameId, CancellationToken ct = default) =>
        db.LeagueClubGames.AsNoTracking().Where(g => g.Id == clubGameId).Select(g => (int?)g.ClubId).FirstOrDefaultAsync(ct);

    /// <summary>Vereinspartie DIESES Vereins (Zugehörigkeit des Kontos prüft der Aufrufer); <c>null</c> = unbekannt oder fremd.</summary>
    public async Task<Game?> ClubGameAsync(int clubId, int clubGameId, bool linkable, CancellationToken ct = default) =>
        await db.LeagueClubGames.AsNoTracking().AnyAsync(g => g.Id == clubGameId && g.ClubId == clubId, ct)
            ? new Game(GameKind.Club, clubGameId, linkable) : null;

    /// <summary>
    /// Kapitel der Partie im Kurs des Kontos öffnen bzw. anlegen. Den Zugriff auf die Partie hat der Aufrufer geprüft.
    /// </summary>
    /// <param name="viewer">Wessen Bücher als Buchbeispiele zählen — derselbe wie beim Anzeigen des Abschnitts.</param>
    /// <param name="lang">UI-Sprache beim ERSTEN Anlegen des Kurses: sein Name (<c>de…</c> → „Persönliche Taktiken“, sonst „Personal tactics“).</param>
    /// <exception cref="DomainValidationException">Die Partie hat keine lösbare empfohlene Aufgabe.</exception>
    public async Task<PersonalTacticsChapterDto> OpenAsync(int userId, Game game, EbookViewer viewer, string? lang, CancellationToken ct = default)
    {
        var file = FileOf(userId);
        var prefix = $"{file}:{game.Key}:";

        var book = await db.Books.FirstOrDefaultAsync(b => b.FileName == file, ct);
        if (book is not null)
        {
            if (book.ImportVersion < ImportPipeline.CurrentVersion)
            {
                book.ImportVersion = ImportPipeline.CurrentVersion;   // kein Quell-PGN, nichts aufzubereiten
                await db.SaveChangesAsync(ct);
            }
            if (await ExistingAsync(book.Id, prefix, created: false, ct) is { } existing) return existing;
        }

        var lines = await LinesAsync(game, viewer, ct);
        if (lines.Count == 0) throw new DomainValidationException("This game has no solvable recommended exercises.");

        book ??= await EnsureBookAsync(userId, file, lang, ct);

        var chapter = await ChapterNameAsync(book.Id, game, ct);
        var round = await NextRoundAsync(book.Id, ct);
        var n = 0;
        foreach (var l in lines)
        {
            n++;
            db.BookPuzzles.Add(new BookPuzzle
            {
                LineId = $"{prefix}{n:D2}", BookFileName = file, BookId = book.Id,
                Round = (round++).ToString(CultureInfo.InvariantCulture),
                Fen = l.Fen, Moves = l.Moves, StartPly = l.StartPly,
                Title = PgnParser.Truncate(l.Title, 300), Chapter = chapter, Tags = l.Tags, BookRating = l.Rating,
                Source = LineSource,
                SourceGame = game.Linkable ? $"{game.SourceKind}:{game.Id.ToString(CultureInfo.InvariantCulture)}:{l.Ply.ToString(CultureInfo.InvariantCulture)}" : null,
            });
        }
        book.UpdatedAt = DateTime.UtcNow;
        book.ImportVersion = ImportPipeline.CurrentVersion;

        // Zweiter Klick im selben Augenblick: der eindeutige Index auf LineId lässt nur EIN Kapitel zu — dann das der anderen Anfrage.
        var saved = await db.SaveIgnoringUniqueRaceAsync(ct: ct);
        return await ExistingAsync(book.Id, prefix, created: saved, ct)
            ?? throw new InvalidOperationException("Personal tactics chapter could not be stored.");
    }

    /// <summary>Das schon angelegte Kapitel der Partie; <c>null</c> = es gibt keine Linie dieser Partie (mehr).</summary>
    private async Task<PersonalTacticsChapterDto?> ExistingAsync(int bookId, string prefix, bool created, CancellationToken ct)
    {
        var mine = await db.BookPuzzles.AsNoTracking().Where(p => p.BookId == bookId && p.LineId.StartsWith(prefix))
            .InReadingOrder().Select(p => new { p.Chapter, p.IsInfoOnly }).ToListAsync(ct);
        if (mine.Count == 0) return null;
        // Das Kapitel der ersten abfragbaren Linie — der Nutzer darf umbenannt haben; der Index ist der des Lösers (ChapterOrder).
        var first = mine.FirstOrDefault(p => !p.IsInfoOnly);
        var name = ChapterOrder.NormalizeChapter((first ?? mine[0]).Chapter);
        var index = first is null ? -1 : (await ChapterOrder.GetOrderedChapterNamesAsync(db, bookId)).IndexOf(name);
        return new PersonalTacticsChapterDto
        {
            BookId = bookId, ChapterIndex = index >= 0 ? index : null, Chapter = name, Created = created, Lines = mine.Count,
        };
    }

    private sealed record Line(int Ply, string Fen, string Moves, int StartPly, string Title, string? Tags, int? Rating);

    /// <summary>Die lösbaren Aufgaben der Partie in der Reihenfolge des Abschnitts.</summary>
    private async Task<List<Line>> LinesAsync(Game game, EbookViewer viewer, CancellationToken ct)
    {
        var dto = game.Kind == GameKind.Club
            ? await exercises.ForClubGameAsync(game.Id, ct, viewer)
            : await exercises.ForSavedGameAsync(game.Id, ct, viewer);
        var lichessIds = dto.Groups.SelectMany(g => g.Exercises).Where(e => e.Book is null).Select(e => e.Id).Distinct().ToList();
        var names = await db.Puzzles.AsNoTracking().Where(p => lichessIds.Contains(p.Id))
            .Select(p => new { p.Id, p.LichessId }).ToDictionaryAsync(p => p.Id, p => p.LichessId, ct);

        var result = new List<Line>();
        foreach (var g in dto.Groups)
        {
            var move = MoveLabel(g.Ply);
            var theme = g.Theme ?? (g.MateIn is int m ? $"mateIn{Math.Clamp(m, 1, GameExercises.MaxMateIn)}" : null);
            foreach (var e in g.Exercises)
            {
                var tags = Tags(theme, e.Themes);
                if (e.Book is { } b)
                {
                    // Nur mit bestätigter Buchlösung und bestimmter Löserseite (BookSolverLine); die Stellung ist das Diagramm.
                    if (!b.Solvable || e.Moves.Count < (b.OpponentFirst ? 2 : 1)) continue;
                    var where = b.PdfPage ? $", S. {b.Page.ToString(CultureInfo.InvariantCulture)}" : "";
                    result.Add(new Line(g.Ply, e.Fen, string.Join(' ', e.Moves), b.OpponentFirst ? 0 : -1, $"{move} · {b.Title}{where}", tags, null));
                }
                else
                {
                    // Lichess-Form: erst der Gegnerzug, dann die Lösung — ohne Löserzug gibt es nichts zu lösen.
                    if (e.Moves.Count < 2) continue;
                    var id = names.TryGetValue(e.Id, out var lichess) && !string.IsNullOrEmpty(lichess) ? lichess : $"#{e.Id.ToString(CultureInfo.InvariantCulture)}";
                    result.Add(new Line(g.Ply, e.Fen, string.Join(' ', e.Moves), 0, $"{move} · Lichess {id}", tags, e.Rating > 0 ? e.Rating : null));
                }
            }
        }
        return result;
    }

    /// <summary>Zugnummer der Stelle wie im Abschnitt: „19.“ (Weiß am Zug) bzw. „19…“.</summary>
    internal static string MoveLabel(int ply) => $"{(ply / 2 + 1).ToString(CultureInfo.InvariantCulture)}{(ply % 2 == 0 ? "." : "…")}";

    /// <summary>Thema der Stelle zuerst, dann die Themen der Aufgabe — kommagetrennt, höchstens 200 Zeichen (ganze Einträge).</summary>
    internal static string? Tags(string? theme, IEnumerable<string> themes)
    {
        var all = new[] { theme }.Concat(themes).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).Distinct(StringComparer.Ordinal);
        var joined = "";
        foreach (var t in all)
        {
            var next = joined.Length == 0 ? t : $"{joined},{t}";
            if (next.Length > 200) break;
            joined = next;
        }
        return joined.Length == 0 ? null : joined;
    }

    private async Task<Book> EnsureBookAsync(int userId, string file, string? lang, CancellationToken ct)
    {
        var german = lang is not null && lang.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var book = new Book
        {
            FileName = file, OwnerUserId = userId,
            DisplayName = german ? "Persönliche Taktiken" : "Personal tactics",
            Description = german
                ? "Empfohlene Übungen aus deinen Partien — je Partie ein Kapitel."
                : "Recommended exercises from your games — one chapter per game.",
            Kind = BookKind.Puzzle, Themes = "tactics", Tags = "personal,tactics",
            ImportVersion = ImportPipeline.CurrentVersion, CreatedAt = now, UpdatedAt = now,
            Source = new BookSource(),   // Pflicht-Navigation (Tabellensplitting): kein Quell-PGN
        };
        db.Books.Add(book);
        // Gleichzeitiger erster Klick: der eindeutige Index auf FileName lässt nur EIN Buch zu — dann das der anderen Anfrage.
        if (await db.SaveIgnoringUniqueRaceAsync(ct: ct)) return book;
        return await db.Books.FirstAsync(b => b.FileName == file, ct);
    }

    /// <summary>„Weiß – Schwarz (Jahr)“ wie die Titel der Taktik-Ernte — bei Vereinspartien die GESPEICHERTEN Namen (eine
    /// anonymisierte Seite heißt dort schon wie ihr Verein), nie die internen Klarnamen. Im Kurs eindeutig: ein schon
    /// vergebener Name bekommt „· 2“, „· 3“ …</summary>
    private async Task<string> ChapterNameAsync(int bookId, Game game, CancellationToken ct)
    {
        string? white, black; int? year;
        if (game.Kind == GameKind.Club)
        {
            var g = await db.LeagueClubGames.AsNoTracking().Where(x => x.Id == game.Id).Select(x => new { x.White, x.Black, x.Year }).FirstOrDefaultAsync(ct);
            (white, black, year) = (g?.White, g?.Black, g?.Year);
        }
        else
        {
            var g = await db.SavedGames.AsNoTracking().Where(x => x.Id == game.Id).Select(x => new { x.White, x.Black, x.PlayedAt }).FirstOrDefaultAsync(ct);
            (white, black, year) = (g?.White, g?.Black, g?.PlayedAt?.Year);
        }
        var name = GameLabel(white, black, year);
        var taken = (await db.BookPuzzles.AsNoTracking().Where(p => p.BookId == bookId).Select(p => p.Chapter).Distinct().ToListAsync(ct))
            .Select(ChapterOrder.NormalizeChapter).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var i = 2; ; i++)
            if ($"{name} · {i.ToString(CultureInfo.InvariantCulture)}" is var next && !taken.Contains(next)) return next;
    }

    internal static string GameLabel(string? white, string? black, int? year)
    {
        static string Side(string? s) => string.IsNullOrWhiteSpace(s) ? "?" : s.Trim();
        var tail = year is int y ? $" ({y.ToString(CultureInfo.InvariantCulture)})" : "";
        var names = $"{Side(white)} – {Side(black)}";
        // Platz für das Jahr und einen Zähler lassen (Chapter: 200 Zeichen).
        if (names.Length > 180) names = names[..180];
        return names + tail;
    }

    /// <summary>Nächste Rundennummer: hinter der größten vorhandenen — neue Kapitel stehen damit am Ende (Lesereihenfolge
    /// zählt Länge vor Text, also numerisch).</summary>
    private async Task<int> NextRoundAsync(int bookId, CancellationToken ct)
    {
        var rounds = await db.BookPuzzles.AsNoTracking().Where(p => p.BookId == bookId).Select(p => p.Round).ToListAsync(ct);
        var max = 0;
        foreach (var r in rounds)
            if (int.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > max) max = v;
        // Fremde, nicht numerische Runden (von Hand eingefügte Linien) zählen als belegte Plätze.
        return Math.Max(max, rounds.Count) + 1;
    }
}
