using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Der Rohbestand als NACHSCHLAGEWERK: durchsuchen und einzelne Partien zum Rechnen anfordern.
///
/// <para>Bis hierher war <see cref="LibraryGame"/> ein Arbeitsvorrat, in den nur das
/// Wartungswerkzeug sah. Damit war die Auswahl eine Aufgabe fuer genau eine Person mit
/// Datenbankzugang — bei 130 000 Partien ist das der Flaschenhals. Wer die Partie sucht, die er
/// spielen will, findet sie hier selbst und stellt sie in die Warteschlange.</para>
///
/// <para><b>Gesucht wird am SERVER.</b> Der kuratierte Bestand wird ganz ausgeliefert und im
/// Browser gefiltert (ein paar Dutzend Zeilen); hier sind es 130 000 mit 338 MB Partietext. Die
/// Suche laeuft deshalb in SQL und gibt Seiten zurueck — und das PGN bleibt ganz draussen.</para>
/// </summary>
public class LibraryGameService
{
    private readonly AppDbContext _db;
    private readonly GameAnalysisService _analyses;

    public LibraryGameService(AppDbContext db, GameAnalysisService analyses)
    {
        _db = db;
        _analyses = analyses;
    }

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Suche im Rohbestand.
    ///
    /// <para>Sortiert wird nach der EIGNUNGSNOTE, nicht nach Datum: bei 130 000 Partien ist die
    /// erste Seite die einzige, die die meisten je ansehen, und dort sollen die durchgaengig
    /// erklaerten Meisterpartien stehen — nicht die zufaellig juengsten.</para>
    ///
    /// <para>Dubletten fallen heraus (dieselbe Partie von zwei Leuten kommentiert steht einmal in
    /// der Liste, mit der besser kommentierten Fassung).</para>
    /// </summary>
    /// <param name="query">Freitext ueber Spieler, Turnier und Kommentator.</param>
    /// <param name="language">ISO-Kuerzel; trifft auch die gemischten Angaben („en,de").</param>
    /// <param name="minCommentedPlies">Mindestzahl kommentierter Halbzuege.</param>
    /// <param name="line">Eroeffnungszeile als Filter („e4 e5 Nf3"); leer = alles. Der
    /// Stellungsfilter der Punktepartie-Seite reicht sie durch — eine Praefix-Suche auf dem
    /// Index von <see cref="LibraryGame.OpeningLine"/>.</param>
    public async Task<LibraryGamePageDto> SearchAsync(int userId, string? query, string? language,
        int? minCommentedPlies, int page, int pageSize, CancellationToken ct = default,
        string? line = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var rows = _db.LibraryGames.AsNoTracking()
            .Where(g => g.Status != LibraryGameStatus.Duplicate && g.Status != LibraryGameStatus.Rejected);

        var prefix = GuessOpeningTree.Normalize(line);
        if (prefix.Length > 0)
        {
            var muster = prefix + "%";
            rows = rows.Where(g => g.OpeningLine != null && EF.Functions.Like(g.OpeningLine, muster));
        }

        var term = BooleanTerm(query);
        if (term is not null)
        {
            // Ein einziges Feld fuer alle vier Spalten: wer „Capablanca" tippt, meint den Spieler,
            // wer „Aagaard" tippt, meint den Kommentator — und wer es weiss, tippt beides.
            //
            // VOLLTEXT statt Teilzeichenkette. Am echten Bestand gemessen (130 572 Zeilen): ein
            // LIKE ueber eine der vier Spalten 4,5 s, ueber eine schmale indizierte Spalte mit
            // Sortierung nach der Note 50 s, mit dem Volltext-Index 13 ms. Der Preis ist, dass
            // WORTANFAENGE gesucht werden: „Capa" findet „Capablanca", „blanca" nicht — fuer Namen
            // ist das die Suche, die Leute ohnehin tippen.
            //
            // Die InMemory-Datenbank der Tests kennt kein MATCH. Dort wird auf die einfache Suche
            // zurueckgefallen; das ist ausdruecklich eine Test-Kruecke, die echte Abfrage wird gegen
            // MariaDB geprueft (siehe CLAUDE.md zur InMemory-Luecke).
            rows = _db.Database.IsRelational()
                // Match liefert die RELEVANZ, nicht ein Ja/Nein — im Boolean-Modus ist alles ueber
                // null ein Treffer.
                ? rows.Where(g => EF.Functions.Match(g.SearchText!, term, MySqlMatchSearchMode.Boolean) > 0)
                : rows.Where(g => g.SearchText != null && g.SearchText.Contains(query!.Trim().ToLower()));
        }

        var lang = language?.Trim();
        if (!string.IsNullOrEmpty(lang))
            rows = rows.Where(g => g.Languages != null && g.Languages.Contains(lang));

        if (minCommentedPlies is int min && min > 0)
            rows = rows.Where(g => g.CommentedPlies >= min);

        var total = await rows.CountAsync(ct);
        // Sortiert wird NUR nach Note und Id — beides steht so im Index (InnoDB haengt den
        // Primaerschluessel an jeden Sekundaerindex), ein Rueckwaertslauf liefert die Reihenfolge
        // also fertig. Nimmt man die Kommentardichte als zweites Merkmal dazu, steht sie nicht im
        // Index, und MariaDB sortiert 130 000 Zeilen von Hand: gemessen 13,6 s gegen 3 ms. Die
        // Dichte steckt ohnehin in der Note.
        var items = await rows
            .OrderByDescending(g => g.Score)
            .ThenByDescending(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new LibraryGameDto
            {
                Id = g.Id, White = g.White, Black = g.Black, WhiteElo = g.WhiteElo, BlackElo = g.BlackElo,
                Result = g.Result, Event = g.Event, PlayedOn = g.PlayedOn, Eco = g.Eco,
                PlyCount = g.PlyCount, Annotator = g.Annotator, CommentedPlies = g.CommentedPlies,
                CommentChars = g.CommentChars, Languages = g.Languages, Score = g.Score,
                SourceTitle = g.SourceTitle,
            })
            .ToListAsync(ct);

        await MarkKnownAsync(userId, items, ct);
        return new LibraryGamePageDto { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// Traegt je Zeile nach, ob sie schon spielbar ist — im kuratierten Bestand oder als eigene
    /// Anforderung. In EINER Abfrage fuer die ganze Seite: je Zeile eine waere bei fuenfundzwanzig
    /// Treffern fuenfundzwanzig Umlaeufe.
    /// </summary>
    /// <remarks>Anonym wird <c>0</c> uebergeben: eine UserId 0 gibt es nicht, also bleibt
    /// <c>Requested</c> ueberall falsch und nur <c>InPool</c> traegt — genau richtig fuer einen
    /// Besucher ohne Konto.</remarks>
    private async Task MarkKnownAsync(int userId, List<LibraryGameDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return;

        var ids = items.Select(i => i.Id).ToList();
        var known = await _db.GameAnalyses.AsNoTracking()
            .Where(a => a.LibraryGameId != null && ids.Contains(a.LibraryGameId.Value)
                        && (a.IsPublic || a.UserId == userId))
            .Select(a => new { LibraryGameId = a.LibraryGameId!.Value, a.Id, a.IsPublic, a.UserId })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            var mine = known.FirstOrDefault(k => k.LibraryGameId == item.Id && k.UserId == userId);
            var pool = known.FirstOrDefault(k => k.LibraryGameId == item.Id && k.IsPublic);
            item.Requested = mine is not null;
            item.InPool = pool is not null;
            // Die eigene gewinnt: wer selbst angefordert hat, soll auf SEINE Partie geschickt werden.
            item.GameAnalysisId = mine?.Id ?? pool?.Id;
        }
    }

    /// <summary>
    /// Eine Partie des Bestands zum Rechnen anfordern.
    ///
    /// <para>Derselbe Weg wie ein eingeworfenes PGN — dieselbe feste Tiefe, dieselbe Engine-Wahl,
    /// derselbe Deckel. Der Unterschied ist nur, woher das PGN kommt.</para>
    ///
    /// <para><b>Nichts wird doppelt gerechnet</b>, was schon spielbar ist: liegt die Partie im
    /// kuratierten Bestand oder hat der Aufrufer sie selbst schon angefordert, kommt die vorhandene
    /// Analyse zurueck (<c>AlreadyPlayable</c>). Eine halbe Stunde Engine-Zeit fuer etwas, das
    /// daneben schon fertig liegt, waere die teuerste Art, nichts zu gewinnen.</para>
    /// </summary>
    public async Task<LibraryRequestResult> RequestAsync(int userId, int libraryGameId,
        CancellationToken ct = default)
    {
        var game = await _db.LibraryGames.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == libraryGameId, ct);
        if (game is null)
            return new LibraryRequestResult(null, LibraryRequestReason.NotFound, false);

        var existing = await _db.GameAnalyses.AsNoTracking()
            .Where(a => a.LibraryGameId == libraryGameId && (a.IsPublic || a.UserId == userId))
            .OrderByDescending(a => a.UserId == userId)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != 0)
        {
            var known = await _analyses.GetPlayableHeadAsync(userId, existing, ct);
            return new LibraryRequestResult(known, null, true);
        }

        // Die Herkunft wird beim ANLEGEN gesetzt, nicht hinterher: ein zweiter Schreibvorgang
        // koennte zwischen den beiden scheitern, und dann staende eine Analyse da, die niemand mehr
        // ihrer Bibliothekszeile zuordnen kann (der Abgleich „schon angefordert" liefe ins Leere).
        var result = await _analyses.CreateForGuessAsync(userId, new CreateGuessGameRequest
        {
            Pgn = game.Pgn,
            Title = TitleOf(game),
        }, ct, libraryGameId);

        return result.Analysis is null
            ? new LibraryRequestResult(null, result.Reason, false)
            : new LibraryRequestResult(result.Analysis, null, false);
    }

    /// <summary>Kuerzer als so viele Zeichen wird nicht gesucht — der Volltext-Index von MariaDB
    /// nimmt Woerter erst ab drei Buchstaben auf (<c>innodb_ft_min_token_size</c>), und eine Suche,
    /// die verlaesslich nichts findet, ist schlimmer als keine.</summary>
    public const int MinQueryLength = 3;

    /// <summary>
    /// Die Eingabe als Volltext-Ausdruck: jedes Wort ein Pflicht-Treffer, das letzte mit Stern
    /// (wer tippt, ist mitten im Wort).
    ///
    /// <para>Die Sonderzeichen der Boolean-Syntax (<c>+ - &gt; &lt; ( ) ~ * " @</c>) werden
    /// WEGGEWORFEN, nicht durchgereicht: sie sind Operatoren, und ein Nutzer, der einen Bindestrich
    /// in einen Doppelnamen tippt, bekaeme sonst eine Suche, die das Gegenteil meint.</para>
    ///
    /// <para><c>null</c>, wenn nichts Brauchbares uebrigbleibt — dann wird gar nicht gefiltert.</para>
    /// </summary>
    internal static string? BooleanTerm(string? query)
    {
        // Getrennt wird an ALLEM, was kein Buchstabe und keine Ziffer ist — auch mitten im Wort:
        // „Saint-John" sind zwei Woerter, und der Volltext-Index fuehrt sie auch als zwei.
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        foreach (var c in (query ?? string.Empty) + " ")
        {
            if (char.IsLetterOrDigit(c)) { word.Append(char.ToLowerInvariant(c)); continue; }
            if (word.Length >= MinQueryLength) words.Add(word.ToString());
            word.Clear();
            if (words.Count >= 6) break;
        }
        if (words.Count == 0) return null;

        var terms = words.Select((w, i) => i == words.Count - 1 ? $"+{w}*" : $"+{w}");
        return string.Join(' ', terms);
    }

    /// <summary>„Aljechin – Bogoljubow, Hastings 1922" — der Titel, unter dem die Partie danach in
    /// der Punktepartie-Liste steht. Ohne Namen bleibt das Turnier, ohne beides der Kommentator.</summary>
    public static string TitleOf(LibraryGame game)
    {
        var names = string.Join(" – ", new[] { game.White, game.Black }
            .Where(n => !string.IsNullOrWhiteSpace(n)));
        var where = string.Join(" ", new[] { game.Event, game.PlayedOn?.Year.ToString() }
            .Where(n => !string.IsNullOrWhiteSpace(n)));

        var title = names.Length > 0 && where.Length > 0 ? $"{names}, {where}"
            : names.Length > 0 ? names
            : where.Length > 0 ? where
            : game.Annotator ?? "Partie";
        return title.Length > 200 ? title[..200] : title;
    }
}
