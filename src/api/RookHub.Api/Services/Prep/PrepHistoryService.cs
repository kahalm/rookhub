using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// „Frühere Vorbereitungen" der Suchseite (<c>GET /api/prep/history</c>, Wunsch 2026-11-19: „zeig dort liste der alten
/// vorbereitungen an und mit klick hol ich den spieler gleich wieder"). Eine Vorbereitung ist ein eigenes
/// Trainings-Repertoire, wie <see cref="TrainingLinesService.CreateRepertoireAsync"/> es anlegt — es gibt dafür keine
/// eigene Tabelle und kein „zuletzt geöffnet". Erkannt an <c>OpponentJson</c> (seit 0.894.0) ODER am Namen
/// „Prep: &lt;Gegner&gt; &lt;Jahr&gt;" (<see cref="TrainingLinesService.RepertoirePrefix"/>, ältere). Gelöschte Repertoires
/// gibt es nicht als Zeile (<c>RepertoireService.DeleteAsync</c> entfernt sie), sie fallen also von selbst weg.
///
/// <para>Der Gegner wird zum Spieler des Bestands (<c>PrepPlayers</c>) aufgelöst, damit die Zeile direkt auf
/// <c>/prep/&lt;id&gt;</c> führt:</para>
/// <list type="bullet">
/// <item><c>prep:&lt;id&gt;</c> → der Spieler mit dieser Id;</item>
/// <item><c>league:&lt;FIDE&gt;</c> → der Spieler mit dieser FIDE-ID (Index auf <c>FideId</c>, wie die Suche nach einer Zahl);
/// steht er nicht im Bestand, bleibt die Zeile unaufgelöst — auch wenn jemand im Bestand so heißt (das wäre mit einer
/// anderen FIDE-ID ein anderer);</item>
/// <item>ohne <c>OpponentJson</c> → der Name aus dem Repertoire-Namen, aufgelöst NUR, wenn im Bestand GENAU EIN Spieler
/// exakt diesen Namensschlüssel trägt (<see cref="PrepPgn.NameKey(string?)"/>: klein, ohne Akzente und Titel wie „Mag.";
/// dazu die Megabase-Schreibweise mit ae/oe/ue). Zwei Namensvettern — auch der FIDE-Spieler und sein Zwilling ohne
/// FIDE-ID — heißt: nicht aufgelöst, die Oberfläche sucht dann nach <see cref="Item.Query"/>.</item>
/// </list>
///
/// <para>Je Gegner EIN Eintrag, der jüngste: gleicher aufgelöster Spieler, sonst gleicher Gegner-Schlüssel; ein älterer
/// Eintrag ohne Schlüssel fällt auch weg, wenn ein jüngerer denselben Namen trägt („Prep: Hess, Max 2025" hinter
/// „Prep: Hess, Max 2026").</para>
///
/// <para>Vier Abfragen, keine je Zeile: die Repertoires des Nutzers, die Namensschlüssel gezählt (nur Index
/// <c>NameKey, Games</c>), die Spieler nach Id und die nach FIDE-ID.</para>
/// </summary>
public sealed class PrepHistoryService(AppDbContext db)
{
    /// <summary>So viele Einträge höchstens.</summary>
    public const int MaxItems = 50;
    /// <summary>So viele eigene Trainings-Repertoires werden dafür höchstens angesehen (die zuletzt geänderten).</summary>
    public const int MaxScan = 300;

    /// <summary>
    /// Ein Eintrag. <see cref="PlayerId"/> <c>null</c> = nicht aufgelöst. <see cref="Name"/> = voller Name (der des Bestands,
    /// sonst der aus dem Repertoire-Namen). <see cref="Games"/> bis <see cref="MaxElo"/> wie ein Suchtreffer, nur aufgelöst.
    /// <see cref="PreparedAt"/> = <c>Repertoire.UpdatedAt</c> — Anlegen und „Ersetzen" setzen es (Dateien neu), aber auch jede
    /// andere Änderung am Repertoire (Umbenennen, Chessable-Aktualisieren). <see cref="Color"/> = MEINE Farbe (<c>w</c>/<c>b</c>),
    /// wie <c>OpponentJson</c> sie führt; ohne <c>OpponentJson</c> <c>null</c>. <see cref="Query"/> = Suchtext für
    /// <c>/api/prep/players</c>: der volle Name ohne Titel.
    /// </summary>
    public sealed record Item(int? PlayerId, string Name, string? Fide, int? Games, short? FirstYear, short? LastYear, short? MaxElo,
        int RepertoireId, string RepertoireName, DateTime PreparedAt, string? Color, string Query);

    private sealed record Player(int Id, string Name, string NameKey, string? FideId, int Games, short? FirstYear, short? LastYear, short? MaxElo);

    /// <summary>Ein Trainings-Repertoire, zerlegt: Gegner-Schlüssel (oder keiner), was daraus nachzuschlagen ist, der Name.</summary>
    private sealed record Entry(int RepertoireId, string RepertoireName, DateTime PreparedAt, string? Key, int? PrepId, string? Fide,
        string? Written, string? ShortName, string? Color, string[] NameKeys);

    public async Task<List<Item>> ListAsync(int userId, CancellationToken ct)
    {
        var like = PrepPlayerSearch.Escape(TrainingLinesService.RepertoirePrefix) + "%";
        var rows = await db.Repertoires.AsNoTracking()
            .Where(r => r.UserId == userId && (r.OpponentJson != null || EF.Functions.Like(r.Name, like, "\\")))
            .OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Id).Take(MaxScan)
            .Select(r => new { r.Id, r.Name, r.OpponentJson, r.UpdatedAt }).ToListAsync(ct);

        var entries = new List<Entry>(rows.Count);
        foreach (var r in rows)
        {
            var opponent = RepertoireOpponentDto.Parse(r.OpponentJson);
            // LIKE vergleicht je nach Kollation ohne Groß/klein — hier zählt genau die Schreibweise, die das Anlegen vergibt.
            var written = TrainingLinesService.OpponentOfRepertoireName(r.Name);
            if (opponent is null && written is null) continue;   // kaputtes OpponentJson an einem anders benannten Repertoire
            int? prepId = null;
            string? fide = null;
            if (opponent is not null)
            {
                var value = opponent.Opponent[(opponent.Opponent.IndexOf(':') + 1)..];
                if (opponent.Opponent.StartsWith("prep:", StringComparison.Ordinal))
                    prepId = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
                else
                    fide = FideOf(value);
            }
            entries.Add(new Entry(r.Id, r.Name, r.UpdatedAt, opponent?.Opponent, prepId, fide, written,
                string.IsNullOrWhiteSpace(opponent?.Name) ? null : opponent.Name.Trim(), opponent?.Color, NameKeys(written)));
        }
        if (entries.Count == 0) return [];

        // Ohne Schlüssel: der Name gilt nur, wenn GENAU EIN Spieler des Bestands so heißt (beide Schreibweisen zusammen).
        var byName = await UniqueByNameAsync(entries.Where(e => e.Key is null).SelectMany(e => e.NameKeys), ct);
        int? NameMatch(Entry e)
        {
            if (e.Key is not null) return null;
            var hits = e.NameKeys.Where(byName.ContainsKey).Select(k => byName[k]).ToList();
            return hits.Count == 1 && hits[0].Count == 1 ? hits[0].Id : null;
        }

        var ids = entries.Select(e => e.PrepId ?? NameMatch(e)).OfType<int>().Distinct().ToList();
        var byId = new Dictionary<int, Player>();
        if (ids.Count > 0)
            byId = (await Select(db.PrepPlayers.AsNoTracking().Where(p => ids.Contains(p.Id))).ToListAsync(ct)).ToDictionary(p => p.Id);
        var fides = entries.Select(e => e.Fide).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var byFide = new Dictionary<string, Player>(StringComparer.Ordinal);
        if (fides.Count > 0)
        {
            // FideId ist nicht eindeutig erzwungen (die Identität ist KeyHash) — bei mehreren der mit den meisten Partien, wie die Suche.
            var found = await Select(db.PrepPlayers.AsNoTracking().Where(p => p.FideId != null && fides.Contains(p.FideId))).ToListAsync(ct);
            foreach (var g in found.GroupBy(p => p.FideId!, StringComparer.Ordinal))
                byFide[g.Key] = g.OrderByDescending(p => p.Games).ThenBy(p => p.Id).First();
        }

        var items = new List<Item>();
        var seenPlayers = new HashSet<int>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            var player = e.PrepId is { } pid ? byId.GetValueOrDefault(pid)
                : e.Fide is { } f ? byFide.GetValueOrDefault(f)
                : NameMatch(e) is { } nid ? byId.GetValueOrDefault(nid) : null;
            var duplicate = player is not null ? !seenPlayers.Add(player.Id)
                : e.Key is not null ? seenKeys.Contains(e.Key)
                : e.NameKeys.Any(seenNames.Contains);
            if (e.Key is not null) seenKeys.Add(e.Key);
            foreach (var k in e.NameKeys) seenNames.Add(k);
            if (player is not null) seenNames.Add(player.NameKey);
            if (duplicate) continue;

            var name = player?.Name ?? e.Written ?? e.ShortName ?? e.RepertoireName;
            items.Add(new Item(player?.Id, name, player?.FideId ?? e.Fide, player?.Games, player?.FirstYear, player?.LastYear,
                player?.MaxElo, e.RepertoireId, e.RepertoireName, e.PreparedAt, e.Color, SearchText(name)));
            if (items.Count == MaxItems) break;
        }
        return items;
    }

    private static IQueryable<Player> Select(IQueryable<Models.PrepPlayer> players) =>
        players.Select(p => new Player(p.Id, p.Name, p.NameKey, p.FideId, p.Games, p.FirstYear, p.LastYear, p.MaxElo));

    /// <summary>Je Namensschlüssel: wie viele Spieler ihn tragen und (bei genau einem) wer — gezählt in der Datenbank, über den
    /// Index (NameKey, Games), ohne eine Zeile zu lesen.</summary>
    private async Task<Dictionary<string, (int Count, int Id)>> UniqueByNameAsync(IEnumerable<string> nameKeys, CancellationToken ct)
    {
        var keys = nameKeys.Distinct(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, (int Count, int Id)>(StringComparer.Ordinal);
        if (keys.Count == 0) return result;
        var counted = await db.PrepPlayers.AsNoTracking().Where(p => keys.Contains(p.NameKey))
            .GroupBy(p => p.NameKey).Select(g => new { Key = g.Key, Count = g.Count(), Id = g.Min(p => p.Id) }).ToListAsync(ct);
        // Die Datenbank vergleicht und gruppiert nach ihrer Kollation (ohne Groß/klein, je nach Kollation auch „ø“ = „o“): ein
        // Treffer zählt nur, wenn sein Schlüssel Zeichen für Zeichen der gefragte ist — sonst bleibt der Name unaufgelöst.
        var asked = keys.ToHashSet(StringComparer.Ordinal);
        foreach (var c in counted)
            if (asked.Contains(c.Key)) result[c.Key] = (c.Count, c.Id);
        return result;
    }

    /// <summary>Die Schlüssel, unter denen der Bestand diesen Namen führen kann: ohne Umlaut-Umschrift (Lumbra, „hocher") und
    /// mit (Megabase, „hoecher"). Die Rückschreibweise der Suche (ae → a) zählt hier NICHT — sie machte „Michael" zu „Michal".</summary>
    internal static string[] NameKeys(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        return new[] { PrepPgn.NameKey(name), PrepPgn.NameKeyTransliterated(name) }
            .Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Suchtext, mit dem <see cref="PrepPlayerSearch"/> den Namen findet: ohne Titel vorn und hinten („Moritz, Christian
    /// Mag." → „Moritz, Christian") — die Suche nähme „Mag" sonst als weiteren Vornamen und fände niemanden. Dieselben Regeln wie
    /// der Namensschlüssel (<see cref="LeagueNames.StripTitles"/>, <see cref="LeagueNames.IsSuffix"/>), nur ohne Kleinschreibung.</summary>
    internal static string SearchText(string name)
    {
        var tokens = LeagueNames.StripTitles(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokens.Count > 2 && LeagueNames.IsSuffix(tokens[^1])) tokens.RemoveAt(tokens.Count - 1);
        return string.Join(' ', tokens).TrimEnd(',', ' ');
    }

    /// <summary>FIDE-ID aus <c>league:&lt;…&gt;</c>, wie der Bestand sie schreibt (nur Ziffern, ohne führende Nullen); der Schlüssel
    /// eines Ligaspielers OHNE FIDE-ID („n-…") ist keine.</summary>
    private static string? FideOf(string value)
    {
        if (value.Length is 0 or > 12 || !value.All(char.IsAsciiDigit)) return null;
        var f = value.TrimStart('0');
        return f.Length == 0 ? null : f;
    }
}
