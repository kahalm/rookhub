using Chess;

namespace RookHub.Api.Services.League;

/// <summary>
/// Stellungs-Fingerabdruck (0.612.0, Idee des Nutzers 2026-09-30: „wenn viele Partien viele Züge von ihm gleich haben, ist es
/// vermutlich derselbe"): spielt ein Online-Konto dieselben Stellungen wie ein Spieler am Brett? Stellung = Brett + Seite am Zug,
/// genommen NACH jedem EIGENEN Zug der ersten <see cref="MaxPly"/> Halbzüge (Zugumstellungen zählen), getrennt nach Farbe. Je
/// Online-Partie die tiefste Stellung, die auch in seinen Brettpartien mit derselben Farbe vorkommt; der Wert eines Spielers ist
/// das Mittel darüber (Partien ohne gemeinsame Stellung zählen 0).
/// <para>Für sich sagt die Zahl wenig (Hauptvarianten teilen viele) — sie taugt zum VERGLEICH unter wenigen Kandidaten, z. B. den
/// Spielern des Vereins, für den ein Konto gespielt hat. Geprüft an der Meldeliste der Online-TMM 2021 (47 Konten mit bekanntem
/// Spieler): der beste Vereinsspieler war es in 22 Fällen; mit mindestens <see cref="LeagueTeamScout.ClubMargin"/>-fachem Abstand
/// zum Zweiten in 11 von 13, ab 1,5 in 7 von 8, ab 2 in 4 von 4.</para>
/// </summary>
public static class LeagueFingerprint
{
    public const int MaxPly = 20;
    /// <summary>Bullet nur, wenn es weniger andere Partien gibt — dort spielt man andere Eröffnungen.</summary>
    public const int MinNonBullet = 50;

    /// <summary>Die Stellungen nach den eigenen Zügen (Halbzug → Schlüssel). Ein Zug, der nicht geht, beendet die Partie.</summary>
    public static Dictionary<int, string> OwnPositions(IReadOnlyList<string> sans, bool white)
    {
        var res = new Dictionary<int, string>();
        var board = new ChessBoard();
        for (var i = 0; i < Math.Min(sans.Count, MaxPly); i++)
        {
            try
            {
                if (!board.Move(sans[i])) break;
            }
            catch
            {
                break;
            }
            var ply = i + 1;
            if ((ply % 2 == 1) == white) res[ply] = Key(board.ToFen());
        }
        return res;
    }

    private static string Key(string fen)
    {
        var p = fen.Split(' ');
        return p.Length > 1 ? p[0] + " " + p[1] : fen;
    }

    /// <summary>Alle Stellungen eines Spielers aus seinen Partien, je Farbe.</summary>
    public sealed class Repertoire
    {
        public HashSet<string> White { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Black { get; } = new(StringComparer.Ordinal);
        public int Games { get; private set; }

        public void Add(IReadOnlyList<string> sans, bool white)
        {
            var set = white ? White : Black;
            foreach (var k in OwnPositions(sans, white).Values) set.Add(k);
            Games++;
        }
    }

    /// <summary>Mittlere tiefste gemeinsame Stellung der Online-Partien mit dem Repertoire (0 = keine gemeinsame).</summary>
    public static double Depth(IReadOnlyList<(IReadOnlyList<string> Sans, bool White)> games, Repertoire r)
    {
        if (games.Count == 0) return 0;
        double sum = 0;
        foreach (var (sans, white) in games)
        {
            var set = white ? r.White : r.Black;
            var deepest = 0;
            foreach (var (ply, key) in OwnPositions(sans, white))
                if (ply > deepest && set.Contains(key)) deepest = ply;
            sum += deepest;
        }
        return sum / games.Count;
    }

    /// <summary>
    /// Wie viele Online-Partien folgen den Brettpartien mindestens <paramref name="ownMoves"/> EIGENE Züge weit (Weiß bis Halbzug
    /// 2n−1, Schwarz bis 2n)? → (Partien, davon so weit, mittlere tiefste gemeinsame Stellung). Für die Konto-Prüfung (i), 0.619.0:
    /// „% Übereinstimmung Repertoire". Nach einem eigenen Zug ist fast jede Partie im Repertoire (1.e4), nach dreien nicht mehr.
    /// </summary>
    public static (int Games, int Reached, double Depth) Coverage(IReadOnlyList<(IReadOnlyList<string> Sans, bool White)> games,
        Repertoire r, int ownMoves = 3)
    {
        if (games.Count == 0) return (0, 0, 0);
        var reached = 0;
        double sum = 0;
        foreach (var (sans, white) in games)
        {
            var set = white ? r.White : r.Black;
            var deepest = 0;
            foreach (var (ply, key) in OwnPositions(sans, white))
                if (ply > deepest && set.Contains(key)) deepest = ply;
            sum += deepest;
            if (deepest >= (white ? 2 * ownMoves - 1 : 2 * ownMoves)) reached++;
        }
        return (games.Count, reached, sum / games.Count);
    }

    /// <summary>Ohne Bullet, wenn genug andere da sind.</summary>
    public static List<T> Usable<T>(IReadOnlyList<T> games, Func<T, string?> speed)
    {
        var other = games.Where(g => speed(g) is not ("bullet" or "ultraBullet")).ToList();
        return other.Count >= MinNonBullet ? other : games.ToList();
    }

    /// <summary>
    /// Wer unter den Kandidaten passt am besten? → (bester, Wert, Abstand zum Zweiten); <c>null</c> ohne gemeinsame Stellung. Der
    /// Abstand ist das Verhältnis der Werte (ein Zweiter mit 0 zählt als 0,1, damit ein einziger Treffer nicht unendlich wird).
    /// </summary>
    public static (string Fide, double Depth, double Ratio, string? Second)? Best(IReadOnlyDictionary<string, double> depths)
    {
        var ranked = depths.Where(d => d.Value > 0).OrderByDescending(d => d.Value).ThenBy(d => d.Key, StringComparer.Ordinal).ToList();
        if (ranked.Count == 0) return null;
        var second = ranked.Count > 1 ? ranked[1] : default;
        return (ranked[0].Key, ranked[0].Value, ranked[0].Value / Math.Max(second.Value, 0.1), ranked.Count > 1 ? second.Key : null);
    }
}
