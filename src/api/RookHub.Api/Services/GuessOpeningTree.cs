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
/// <para><b>Gezaehlt wird mit einer Praefix-Suche</b> (<c>LIKE 'e4 e5 Nf3%'</c>) auf dem Index —
/// der Platzhalter steht hinten, also trifft sie ihn. Die Fortsetzung selbst schneidet die
/// Datenbank aus der Zeile (<c>SUBSTRING_INDEX</c>): bei 130 000 Partien waere es nicht tragbar,
/// die Zeilen zum Zaehlen erst alle zu holen.</para>
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

        var lines = await LinesAsync(prefix, onlyPlayable, ct);
        dto.Total = lines.Count;

        // Der naechste Halbzug ist das erste Wort NACH dem Praefix.
        var ab = prefix.Length == 0 ? 0 : prefix.Length + 1;
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

        dto.Moves = zaehlung
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxMoves)
            .Select(kv => new OpeningMoveDto { San = kv.Key, Games = kv.Value })
            .ToList();
        return dto;
    }

    /// <summary>
    /// Die Eroeffnungszeilen, die mit <paramref name="prefix"/> beginnen.
    ///
    /// <para>Bewusst die ZEILEN und nicht eine fertige Zaehlung aus der Datenbank: der Rohbestand
    /// ist zwar gross, aber ab dem zweiten Halbzug ist die Treffermenge klein, und eine Abfrage,
    /// die auf MariaDBs <c>SUBSTRING_INDEX</c> baut, liefe im Test gegen die InMemory-Datenbank
    /// gar nicht. Der teure Fall — die Grundstellung ueber 130 000 Partien — wird durch
    /// <see cref="RootLimit"/> gedeckelt: dort zaehlt ohnehin nur, welche ersten Zuege es gibt.</para>
    /// </summary>
    private async Task<List<string>> LinesAsync(string prefix, bool onlyPlayable, CancellationToken ct)
    {
        var muster = prefix.Length == 0 ? "%" : prefix + " %";

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
        return await roh.Select(g => g.OpeningLine!).Take(RootLimit).ToListAsync(ct);
    }

    /// <summary>Deckel fuer den Rohbestand. In der Grundstellung stehen dort 130 000 Zeilen, und
    /// fuer die Frage „welche ersten Zuege gibt es" genuegt ein Ausschnitt.</summary>
    public const int RootLimit = 20000;

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
