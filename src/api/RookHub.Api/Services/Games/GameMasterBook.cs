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

    /// <summary>Die bestbewerteten Meisterpartien, die <paramref name="fen"/> erreicht haben (bis zu
    /// <see cref="LichessExplorerClient.MaxMasterGames"/>, mit den Elo-Werten beider Spieler) — zum Nachzählen, wie viele
    /// davon ZWEI Meister spielten. <c>null</c> = nicht angekommen.</summary>
    Task<IReadOnlyList<ExplorerGame>?> MasterGamesAsync(string fen, CancellationToken ct);
}

/// <summary>Die Quelle im Betrieb: <see cref="RepertoireExplorerService.BatchStatsAsync"/> — nur der lokale Explorer, mit dessen
/// Arbeits- und Dauerspeicher (die Eröffnungsstellungen sind fast immer schon da). Ohne lokalen Explorer kommt nichts.</summary>
public sealed class ExplorerMasterBookSource(RepertoireExplorerService explorer, LocalExplorerClient local) : IMasterBookSource
{
    public async Task<IReadOnlyList<ExplorerGame>?> MasterGamesAsync(string fen, CancellationToken ct)
        => local.IsConfigured ? await local.FetchGamesAsync(fen, GameMasterBook.Masters, ct, LichessExplorerClient.MaxMasterGames) : null;

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
/// <para><b>„Meisterpartie“ = BEIDE Spieler ab <see cref="MinMasterElo"/></b> (Regelstand 2, 0.981.0, Entscheidung des Nutzers
/// 2026-11-15: „ja nur partien ab 2200 zählen“; der Bot ist schon so). Die lokale Meisterdatenbank (Lumbra) nimmt Partien
/// nach dem Elo-SCHNITT ab 2200 auf — ein Spieler darf darunter liegen, und das kommt oft vor. Die Zugstatistik lässt sich
/// nicht nach Elo filtern; für Züge mit weniger als <see cref="RecountBelow"/> Partien wird deshalb über die Partienliste der
/// Stellung NACH dem Zug nachgezählt (die bis zu 15 bestbewerteten, fehlender Elo-Wert = darunter). Ab 30 Partien wird nicht
/// nachgezählt (Annahme wie im Bot: genügend dabei). Gibt es mehr Partien als sichtbar, ist die Zählung eine Untergrenze —
/// ein Irrtum geht nur in die vorsichtige Richtung (kein Buchzug). Anlass: Analyse 28449, 7…Le7 — 2 Partien in der
/// Datenbank, keine mit beiden ab 2200; die Zusammenfassung sah die Meisterpraxis dort verlassen, die Partieseite führte
/// 7…Le7 und 8.Sd2 als Buch. Fällt das Nachzählen aus, gibt es keine Aussage (<c>null</c>, später erneut).</para>
///
/// <para>Abweichung vom Bot an EINER Stelle: sieht er nur einen Teil der Partien (er holt fünf) und ist keine mit zwei
/// Meistern dabei, sagt er nichts mehr; hier zählt das als „keine Meisterpartie“ — sonst bliebe die Analyse für immer
/// „noch nicht geprüft“ und stünde bei jedem Erntetakt wieder vorn. Mit 15 statt 5 sichtbaren Partien ist der Fall selten
/// (16–29 Partien, keine der 15 bestbewerteten mit zwei Meistern), und die Richtung ist dieselbe: kein Buchzug.</para>
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
    /// <summary>Regelstand der Zählung, abgelegt in <see cref="GameAnalysis.MasterBookVersion"/>: 1 = 0.978.0 (alle Partien der
    /// Meisterdatenbank), 2 = 0.981.0 (nur Partien mit beiden Spielern ab <see cref="MinMasterElo"/>). Hochzählen lässt den
    /// Erntelauf den Bestand neu bestimmen; bis dahin gelten die alten Buchzüge weiter.</summary>
    public const int Version = 2;
    /// <summary>Ab dieser Elo-Zahl BEIDER Spieler zählt eine Partie als Meisterpartie — wie <c>MIN_EXAMPLE_ELO</c> im Bot.</summary>
    public const int MinMasterElo = 2200;
    /// <summary>Unter so vielen Partien mit dem Zug wird nachgezählt — wie <c>RECOUNT_BELOW</c> im Bot.</summary>
    public const int RecountBelow = 30;
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
            // Meisterpartien je Halbzug: mit dem Partiezug (pm, nur solche mit zwei Meistern) und in der Stellung (tm). Hat die
            // Partie die Meisterdatenbank verlassen, zählt deren Antwort für die Folgestellungen nicht mehr.
            var all = positions.Where(p => p.Ply >= 0).GroupBy(p => p.Ply).ToDictionary(g => g.Key, g => g.First());
            var pm = new long[n];
            var tm = new long[n];
            var started = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0, alive = 1; i < n && alive == 1; i++)
            {
                if (!masters.TryGetValue(asked[i].Key, out var m)) return null;   // nicht angekommen → später erneut
                tm[i] = m.Total;
                pm[i] = Played(m, moves[i]);
                if (pm[i] > 0 && pm[i] < RecountBelow)
                {
                    if (started.Elapsed > left) return null;   // Zeit um → später erneut
                    // Stellung NACH dem Zug: die Partien dort sind die Partien mit diesem Zug (Zugumstellungen eingeschlossen).
                    var after = all.TryGetValue(i + 1, out var next) && !string.IsNullOrEmpty(next.Fen)
                        ? next.Fen
                        : TacticsBoard.TryParse(moves[i].Fen)?.Apply(moves[i].GameMoveUci)?.ToFen();
                    if (after is null) pm[i] = 0;   // nicht nachzählbar (Stellung unlesbar): vorsichtig, kein Rückhalt
                    else
                    {
                        if (await source.MasterGamesAsync(after, ct) is not { } games) return null;   // Nachzählen fiel aus
                        pm[i] = Math.Min(pm[i], games.Count(BothMasters));
                    }
                }
                if (tm[i] == 0 || pm[i] == 0) alive = 0;
            }
            // Lichess nur, wo die Meisterpraxis den Zug nicht schon als Theorie ausweist (spart die halben Abfragen).
            var needLichess = Enumerable.Range(0, n).Where(i => pm[i] < MastersMin).Select(i => asked[i]).ToList();
            var lichess = needLichess.Count > 0
                ? await source.StatsAsync(needLichess, Lichess, left, ct)
                : new Dictionary<string, ExplorerPositionStats>();

            var book = new List<int>();
            for (var i = 0; i < n; i++)
            {
                long pl = 0, tl = 0;
                if (pm[i] < MastersMin)
                {
                    if (!lichess.TryGetValue(asked[i].Key, out var l)) return null;
                    tl = l.Total;
                    pl = Played(l, moves[i]);
                }
                var backing = Classify(pm[i], pl, tl);
                if (backing == Backing.Theory) book.Add(i);
                if (backing == Backing.None) break;   // ohne Vorgänger: die Theorie ist verlassen
            }
            return Format(book);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Beide Spieler ab <see cref="MinMasterElo"/>; ein fehlender Elo-Wert gilt als darunter — wie <c>is_master</c> im Bot.</summary>
    public static bool BothMasters(ExplorerGame game) => game.WhiteRating >= MinMasterElo && game.BlackRating >= MinMasterElo;

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
