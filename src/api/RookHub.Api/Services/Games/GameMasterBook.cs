using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Games;

/// <summary>Woher <see cref="GameMasterBook"/> die Zughäufigkeiten nimmt — im Betrieb der LOKALE Explorer
/// (<see cref="ExplorerMasterBookSource"/>), in Tests eine feste Tabelle. Was nicht ankommt, fehlt im Ergebnis; nie eine
/// Ausnahme.</summary>
public interface IMasterBookSource
{
    Task<IReadOnlyDictionary<string, ExplorerPositionStats>> StatsAsync(IReadOnlyList<(string Key, string Fen)> positions,
        ExplorerQuery query, TimeSpan budget, CancellationToken ct);
}

/// <summary>Die Quelle im Betrieb: <see cref="RepertoireExplorerService.BatchStatsAsync"/> — nur der lokale Explorer, mit dessen
/// Arbeits- und Dauerspeicher (die Eröffnungsstellungen sind fast immer schon da). Ohne lokalen Explorer kommt nichts.</summary>
public sealed class ExplorerMasterBookSource(RepertoireExplorerService explorer) : IMasterBookSource
{
    public async Task<IReadOnlyDictionary<string, ExplorerPositionStats>> StatsAsync(IReadOnlyList<(string Key, string Fen)> positions,
        ExplorerQuery query, TimeSpan budget, CancellationToken ct)
        => (await explorer.BatchStatsAsync(positions, query, ct, budget, parallelism: GameMasterBook.Parallelism)).Stats;
}

/// <summary>
/// „Meister-Buchzüge" einer Partie (Wunsch 2026-11-15 an Partie 234: 1.e4 e6 2.d4 d5 3.Ld3 stand als „Fehler 1 von 14,
/// Ungenauigkeit +0.79 → −0.09" da — „ist Ld3 kein Theoriezug?"): die Halbzüge der Eröffnung, die in der Meisterpraxis
/// etabliert sind. An so einem Zug fällt die UNGENAUIGKEIT weg (<see cref="GameMistakes.Classify(double,double,bool,int?,int?,bool,bool)"/>);
/// Fehler und grobe Fehler bleiben — Theorie entschuldigt keinen Einsteller.
///
/// <para><b>Dieselben Schwellen und dieselbe Abbruchregel wie der Zusammenfassungs-Bot</b>
/// (<c>formulare-bot/lib/opening_book.py</c>, 14.11.2026, Partie 245), damit Zusammenfassung und Partieseite nicht
/// auseinanderlaufen: je Stellung die Meisterpartien; nur wenn der Zug dort weniger als <see cref="MastersMin"/>-mal vorkommt,
/// Lichess ab 2200 dazu. Theorie = ≥ 2 Meisterpartien, oder 1 Meisterpartie und ≥ <see cref="LichessMin"/> Lichess-Partien.
/// Schluss beim ersten Zug „ohne Vorgänger" (keine Meisterpartie und weniger als 5 bei Lichess), spätestens nach
/// <see cref="MaxPly"/> Halbzügen (Zug 15). Abweichung vom Bot: es gilt ab dem ERSTEN Zug (dort erst ab Zug 5 — 3.Ld3!).</para>
///
/// <para>Gerechnet am Server, einmal je Analyse, abgelegt in <see cref="GameAnalysis.MasterBookPlies"/> — für alle Betrachter
/// (auch anonym über Teilen-Links), ohne dass ein Seitenaufruf den Explorer fragt. Ist der Explorer nicht erreichbar oder
/// reicht die Zeit nicht, bleibt die Spalte <c>null</c> („noch nicht geprüft") und <see cref="MoveClassHarvestWorker"/>
/// versucht es später wieder; die Analyse selbst scheitert daran nie.</para>
/// </summary>
public class GameMasterBook(IMasterBookSource source)
{
    /// <summary>Höchstens so viele Halbzüge ab der Grundstellung (Zug 15) — wie <c>MAX_PLY</c> im Bot.</summary>
    public const int MaxPly = 30;
    /// <summary>Ab so vielen Meisterpartien ist ein Zug Theorie.</summary>
    public const int MastersMin = 2;
    /// <summary>Mit genau einer Meisterpartie braucht es so viele Lichess-Partien (ab 2200); so viele allein machen ihn „selten".</summary>
    public const int LichessMin = 5;
    /// <summary>So viele Explorer-Abfragen gleichzeitig je Partie — klein, der Erntelauf soll den Explorer nicht belegen.</summary>
    public const int Parallelism = 4;
    /// <summary>Zeit für die Abfragen EINER Partie (je Datenbank).</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(8);

    private const string StartPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w ";

    internal static readonly ExplorerQuery Masters = ExplorerQuery.Create(ExplorerQuery.Masters, null, null);
    /// <summary>Lichess ab 2200, Blitz/Schnell/Klassisch — wie <c>LICHESS</c> im Bot.</summary>
    internal static readonly ExplorerQuery Lichess = ExplorerQuery.Create(ExplorerQuery.Lichess, [2200, 2500], ["blitz", "rapid", "classical"]);

    /// <summary>Einstufung eines Partiezugs — Spiegel von <c>classify</c> im Bot.</summary>
    public enum Backing { None, Rare, Common, Theory }

    /// <param name="masters">Meisterpartien mit diesem Zug.</param>
    /// <param name="lichess">Lichess-Partien (ab 2200) mit diesem Zug.</param>
    /// <param name="lichessTotal">Lichess-Partien in der Stellung.</param>
    public static Backing Classify(long masters, long lichess, long lichessTotal)
    {
        if (masters >= MastersMin || (masters >= 1 && lichess >= LichessMin)) return Backing.Theory;
        // Zehntel als ganze Zahlen verglichen (lichess / total ≥ 0,10).
        if (lichess >= 20 && lichessTotal > 0 && lichess * 10 >= lichessTotal) return Backing.Common;
        if (masters >= 1 || lichess >= LichessMin) return Backing.Rare;
        return Backing.None;
    }

    /// <summary>Die abgelegte Form: Halbzüge (0-basiert) aufsteigend, durch Komma getrennt; leer = geprüft, keiner.</summary>
    public static string Format(IEnumerable<int> plies) => string.Join(',', plies.Distinct().OrderBy(p => p));

    /// <summary>Liest <see cref="GameAnalysis.MasterBookPlies"/>; <c>null</c>/leer/unlesbar = keine Buchzüge.</summary>
    public static IReadOnlySet<int> Parse(string? stored)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrEmpty(stored)) return set;
        foreach (var part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part, out var ply) && ply >= 0) set.Add(ply);
        return set;
    }

    /// <summary>
    /// Die Meister-Buchzüge der Partie in der abgelegten Form. Leer: geprüft, keiner (auch: Partie nicht ab der Grundstellung).
    /// <c>null</c>: jetzt nicht feststellbar (Explorer nicht eingerichtet/erreichbar, Zeit um) — nichts ablegen, später erneut.
    /// Wirft nie.
    /// </summary>
    public async Task<string?> ComputeAsync(IEnumerable<GameAnalysisPosition> positions, int plyCount, CancellationToken ct = default,
        TimeSpan? budget = null)
    {
        try
        {
            var moves = positions.Where(p => p.Ply >= 0 && p.Ply < Math.Min(plyCount, MaxPly) && !string.IsNullOrEmpty(p.GameMoveUci))
                .OrderBy(p => p.Ply).ToList();
            // Lückenlos ab Halbzug 0 — und nur Partien ab der Grundstellung haben Eröffnungstheorie.
            var n = 0;
            while (n < moves.Count && moves[n].Ply == n) n++;
            if (n == 0 || !moves[0].Fen.StartsWith(StartPlacement, StringComparison.Ordinal)) return "";
            var asked = moves.Take(n).Select(p => (Key: RepertoireReach.Key(p.Fen), p.Fen)).ToList();
            var left = budget ?? DefaultBudget;

            var masters = await source.StatsAsync(asked, Masters, left, ct);
            // Lichess nur, wo die Meisterpraxis den Zug nicht schon als Theorie ausweist (spart die halben Abfragen). Hat die
            // Partie die Meisterdatenbank verlassen, zählt deren Antwort für die Folgestellungen nicht mehr.
            var needLichess = new List<(string Key, string Fen)>();
            for (int i = 0, alive = 1; i < n; i++)
            {
                long pm = 0, tm = 0;
                if (alive == 1)
                {
                    if (!masters.TryGetValue(asked[i].Key, out var m)) return null;   // nicht angekommen → später erneut
                    (pm, tm) = (Played(m, moves[i]), m.Total);
                }
                if (pm < MastersMin) needLichess.Add(asked[i]);
                if (tm == 0 || pm == 0) alive = 0;
            }
            var lichess = needLichess.Count > 0
                ? await source.StatsAsync(needLichess, Lichess, left, ct)
                : new Dictionary<string, ExplorerPositionStats>();

            var book = new List<int>();
            var mastersAlive = true;
            for (var i = 0; i < n; i++)
            {
                long pm = 0, tm = 0;
                if (mastersAlive)
                {
                    if (!masters.TryGetValue(asked[i].Key, out var m)) return null;
                    tm = m.Total;
                    pm = Played(m, moves[i]);
                }
                long pl = 0, tl = 0;
                if (pm < MastersMin)
                {
                    if (!lichess.TryGetValue(asked[i].Key, out var l)) return null;
                    tl = l.Total;
                    pl = Played(l, moves[i]);
                }
                var backing = Classify(pm, pl, tl);
                if (backing == Backing.Theory) book.Add(i);
                // Die Folgestellung kennt die Meisterdatenbank nicht mehr.
                if (tm == 0 || pm == 0) mastersAlive = false;
                if (backing == Backing.None) break;   // ohne Vorgänger: die Theorie ist verlassen
            }
            return Format(book);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Wie oft der Partiezug in der Stellung gespielt wurde. Verglichen als UCI; die Rochade schreibt der Explorer
    /// teils als König-schlägt-Turm (<c>e1h1</c>), deshalb ersatzweise über die SAN ohne Schach-/Mattzeichen.</summary>
    internal static long Played(ExplorerPositionStats stats, GameAnalysisPosition move)
    {
        var uci = move.GameMoveUci.ToLowerInvariant();
        var san = Bare(move.GameMoveSan);
        foreach (var m in stats.Moves)
            if (string.Equals(m.Uci, uci, StringComparison.OrdinalIgnoreCase) || (san.Length > 0 && Bare(m.San) == san)) return m.Games;
        return 0;
    }

    private static string Bare(string? san) => (san ?? "").TrimEnd('+', '#', '!', '?');
}
