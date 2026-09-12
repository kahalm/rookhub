using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Der EROEFFNUNGSBAUM ueber die spielbaren Partien: „welche Zuege werden in dieser Stellung
/// gespielt, und von wie vielen Partien?"
///
/// <para>Warum es diesen Weg braucht: die Punktepartie-Liste laesst sich nach Namen durchsuchen,
/// aber nicht nach STELLUNG — und genau danach sucht man, wenn man seine Eroeffnung ueben will.
/// „Zeig mir alle Partien mit Sizilianisch Najdorf" ist keine Textsuche.</para>
///
/// <para><b>Zwei Quellen, dieselbe Form.</b> „Nur gerechnete" fragt die freigegebenen
/// <see cref="Models.GameAnalysis"/>n — das sind die, die man sofort spielen kann. „Alle" fragt
/// den Rohbestand (<see cref="Models.LibraryGame"/>, 130 000 Partien); von dort laesst sich eine
/// Partie anfordern. Beide fuehren dieselbe normalisierte Zeile (<c>OpeningLine</c>), deshalb ist
/// es EIN Baum mit zwei Zaehlungen und nicht zwei Baeume.</para>
///
/// <para><b>Gezaehlt wird in der Datenbank, nicht im Speicher</b> — mit einer Praefix-Suche
/// (<c>LIKE 'e4 e5 Nf3%'</c>) auf dem Index und <c>GROUP BY</c> ueber die naechste Zugsilbe, die
/// <c>SUBSTRING_INDEX</c> aus der Zeile schneidet. Die erste Fassung holte stattdessen bis zu
/// 20 000 Zeilen und zaehlte sie clientseitig; dieser Deckel galt auf JEDER Ebene und nicht nur in
/// der Grundstellung, und er war keine Stichprobe, sondern die ersten 20 000 Zeilen nach Id, also
/// nach Importreihenfolge. Am echten Bestand (2026-09-12, 130 572 Partien) log damit alles bis zur
/// Tiefe, ab der die Treffermenge unter den Deckel faellt: die Grundstellung meldete 20 000 statt
/// 130 572 Partien, und die 9761 Partien, die dort bei <c>e4</c> standen, wurden nach dem Klick auf
/// <c>e4</c> wieder zu 20 000. Eine Zahl, die sich unter der Hand aendert, ist schlimmer als keine.</para>
///
/// <para>Bezahlt wird das mit einem INDEX, der die Abfrage abdeckt (<c>Status, OpeningLine</c> bzw.
/// <c>IsPublic, OpeningLine</c>). Ohne ihn waehlt MariaDB bei einem Praefix, der die halbe Tabelle
/// trifft, den vollen Tabellenscan — und der laeuft ueber die LONGTEXT-Spalte mit den PGNs.
/// Gemessen auf Dev: Grundstellung 15,8 s ohne, 0,13 s mit; nach <c>1.e4</c> 22,4 s gegen 0,25 s.</para>
/// </summary>
public class GuessOpeningTree
{
    /// <summary>So tief reicht die gespeicherte Zeile — dahinter endet der Baum.</summary>
    public const int MaxDepth = LibraryGameReader.OpeningPlies;

    /// <summary>So viele Fortsetzungen werden hoechstens genannt. Mehr als das ist keine Auswahl
    /// mehr, sondern eine Liste zum Scrollen.</summary>
    public const int MaxMoves = 40;

    private readonly AppDbContext _db;

    public GuessOpeningTree(AppDbContext db) => _db = db;

    /// <summary>
    /// Die Fortsetzungen nach <paramref name="line"/>.
    /// </summary>
    /// <param name="line">Die bisherigen Halbzuege, durch Leerzeichen getrennt („e4 e5"); leer =
    /// Grundstellung.</param>
    /// <param name="onlyPlayable">Nur was schon gerechnet und freigegeben ist.</param>
    public async Task<OpeningTreeDto> BranchAsync(string? line, bool onlyPlayable,
        CancellationToken ct = default)
    {
        var prefix = Normalize(line);
        var dto = new OpeningTreeDto { Line = prefix, OnlyPlayable = onlyPlayable };
        if (prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MaxDepth)
            return dto;   // tiefer reicht die gespeicherte Zeile nicht

        dto.Total = await TotalAsync(prefix, onlyPlayable, ct);
        if (dto.Total == 0) return dto;

        // Der naechste Halbzug ist das erste Wort NACH dem Praefix. In SQL ist das ein
        // SUBSTRING_INDEX ab Position `ab + 1` (dort zaehlt ab 1), clientseitig ein Span.
        var ab = prefix.Length == 0 ? 0 : prefix.Length + 1;

        var zaehlung = _db.Database.IsRelational()
            ? await CountBySqlAsync(prefix, onlyPlayable, ab, ct)
            : CountInMemory(await LinesAsync(prefix, onlyPlayable, ct), ab);

        dto.Moves = zaehlung
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxMoves)
            .Select(kv => new OpeningMoveDto { San = kv.Key, Games = kv.Value })
            .ToList();
        return dto;
    }

    /// <summary>Wie viele Partien erreichen diese Stellung? Eine gezaehlte Zeile, kein Ausschnitt —
    /// der Index traegt sie (gemessen 0,14 s ueber 130 572 Partien).</summary>
    private async Task<int> TotalAsync(string prefix, bool onlyPlayable, CancellationToken ct)
    {
        // Die Partie, die GENAU hier endet, erreicht die Stellung auch. Ohne den ersten Vergleich
        // meldete die Wurzel bei `e4` 60 498 Partien und die Ebene danach 60 497 — wieder eine Zahl,
        // die sich beim Klicken aendert. Die ZAEHLUNG der Fortsetzungen unten laesst sie dagegen zu
        // Recht weg: eine Partie ohne naechsten Zug traegt zu keinem Ast bei.
        var mitTrenner = prefix + " %";
        if (onlyPlayable)
        {
            var q = _db.GameAnalyses.AsNoTracking().Where(g => g.IsPublic && g.OpeningLine != null);
            if (prefix.Length > 0)
                q = q.Where(g => g.OpeningLine == prefix || EF.Functions.Like(g.OpeningLine!, mitTrenner));
            return await q.CountAsync(ct);
        }
        var r = _db.LibraryGames.AsNoTracking()
            .Where(g => g.OpeningLine != null && g.Status == Models.LibraryGameStatus.New);
        if (prefix.Length > 0)
            r = r.Where(g => g.OpeningLine == prefix || EF.Functions.Like(g.OpeningLine!, mitTrenner));
        return await r.CountAsync(ct);
    }

    /// <summary>Eine Zeile je Fortsetzung, gezaehlt von der Datenbank.
    /// <para>Bewusst rohes SQL und nicht LINQ: die Zerlegung der Zeile haengt an
    /// <c>SUBSTRING_INDEX</c>, das kein Anbieter einheitlich uebersetzt — und die InMemory-Datenbank
    /// der Tests kennt es gar nicht (siehe CLAUDE.md zur InMemory-Luecke). Der Praefix geht als
    /// PARAMETER hinein, die einzige Zahl im Text (<paramref name="ab"/>) ist eine gerechnete
    /// Laenge.</para></summary>
    private async Task<Dictionary<string, int>> CountBySqlAsync(string prefix, bool onlyPlayable,
        int ab, CancellationToken ct)
    {
        var tabelle = onlyPlayable
            ? "FROM `GameAnalyses` WHERE `IsPublic` = 1 AND `OpeningLine` IS NOT NULL"
            : "FROM `LibraryGames` WHERE `Status` = 0 AND `OpeningLine` IS NOT NULL";
        var filter = prefix.Length == 0 ? " AND `OpeningLine` <> ''" : " AND `OpeningLine` LIKE {0}";
        var sql = $"SELECT SUBSTRING_INDEX(SUBSTRING(`OpeningLine`, {ab + 1}), ' ', 1) AS `San`, "
                + $"COUNT(*) AS `Games` {tabelle}{filter} GROUP BY `San` "
                + $"ORDER BY `Games` DESC, `San` ASC LIMIT {MaxMoves}";

        var rows = prefix.Length == 0
            ? _db.Database.SqlQueryRaw<OpeningTally>(sql)
            : _db.Database.SqlQueryRaw<OpeningTally>(sql, prefix + " %");
        return (await rows.ToListAsync(ct))
            .Where(r => !string.IsNullOrEmpty(r.San))
            .ToDictionary(r => r.San, r => r.Games, StringComparer.Ordinal);
    }

    /// <summary>Dasselbe ohne Datenbank-Funktionen — der Weg der Tests (InMemory).</summary>
    private static Dictionary<string, int> CountInMemory(List<string> lines, int ab)
    {
        var zaehlung = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (l.Length <= ab) continue;
            var rest = l.AsSpan(ab);
            var ende = rest.IndexOf(' ');
            var san = (ende < 0 ? rest : rest[..ende]).ToString();
            if (san.Length == 0) continue;
            zaehlung[san] = zaehlung.GetValueOrDefault(san) + 1;
        }
        return zaehlung;
    }

    /// <summary>Eine Zeile des <c>GROUP BY</c>. Die Namen muessen den Spalten-Aliassen im SQL
    /// entsprechen — <c>SqlQueryRaw</c> bildet ueber den Namen ab.</summary>
    private sealed record OpeningTally(string San, int Games);

    /// <summary>
    /// Die Eroeffnungszeilen, die mit <paramref name="prefix"/> beginnen — nur noch fuer den
    /// InMemory-Weg der Tests. Gegen MariaDB zaehlt <see cref="CountBySqlAsync"/>.
    /// </summary>
    private async Task<List<string>> LinesAsync(string prefix, bool onlyPlayable, CancellationToken ct)
    {
        var muster = prefix + " %";

        if (onlyPlayable)
        {
            var query = _db.GameAnalyses.AsNoTracking()
                .Where(g => g.IsPublic && g.OpeningLine != null);
            if (prefix.Length > 0) query = query.Where(g => EF.Functions.Like(g.OpeningLine!, muster));
            return await query.Select(g => g.OpeningLine!).ToListAsync(ct);
        }

        var roh = _db.LibraryGames.AsNoTracking()
            .Where(g => g.OpeningLine != null && g.Status == Models.LibraryGameStatus.New);
        if (prefix.Length > 0) roh = roh.Where(g => EF.Functions.Like(g.OpeningLine!, muster));
        return await roh.Select(g => g.OpeningLine!).ToListAsync(ct);
    }

    /// <summary>
    /// Die Zeile auf die gespeicherte Form bringen: einfache Leerzeichen, und ohne die Zeichen,
    /// die denselben Zug verschieden aussehen lassen („Nf3+" und „Nf3" sind ein Zug).
    /// </summary>
    public static string Normalize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;
        var teile = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxDepth)
            .Select(t => new string(t.Where(c => c is not ('+' or '#' or '!' or '?')).ToArray()))
            .Where(t => t.Length is > 0 and <= 10);
        return string.Join(' ', teile);
    }
}
