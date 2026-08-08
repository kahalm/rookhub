using System.Buffers.Binary;
using System.Numerics;

namespace RookHub.Api.Services;

/// <summary>Figurentyp-Index in <see cref="PositionFeatures"/> (Reihenfolge = Materialwert-Tabelle).</summary>
public enum SimPieceType { Pawn = 0, Knight = 1, Bishop = 2, Rook = 3, Queen = 4, King = 5 }

/// <summary>
/// Gewichtung der vier Ähnlichkeits-Komponenten (Summe = 1).
/// </summary>
public readonly record struct SimilarityWeights(double Pawns, double Material, double Pieces, double King)
{
    /// <summary>Bauernstruktur dominiert — „welche Linien haben dasselbe Gerüst?"</summary>
    public static readonly SimilarityWeights Struktur = new(0.75, 0.10, 0.10, 0.05);
    /// <summary>Standard.</summary>
    public static readonly SimilarityWeights Ausgewogen = new(0.50, 0.20, 0.20, 0.10);
    /// <summary>Figurenaufstellung dominiert — „welche Linien sehen auf dem Brett so aus?"</summary>
    public static readonly SimilarityWeights Stellungsbild = new(0.30, 0.15, 0.45, 0.10);

    /// <summary>Voreinstellung nach Name; unbekannt/leer → <see cref="Ausgewogen"/>.</summary>
    public static SimilarityWeights FromPreset(string? preset) => (preset ?? "").Trim().ToLowerInvariant() switch
    {
        "struktur" => Struktur,
        "stellungsbild" => Stellungsbild,
        _ => Ausgewogen,
    };

    /// <summary>Kanonischer Name der Voreinstellung (für die Antwort).</summary>
    public static string NormalizePreset(string? preset) => (preset ?? "").Trim().ToLowerInvariant() switch
    {
        "struktur" => "struktur",
        "stellungsbild" => "stellungsbild",
        _ => "ausgewogen",
    };
}

/// <summary>Gesamtscore und die vier Einzelwerte, je 0…100 (100 = identisch).
/// <paramref name="Pieces"/> und <paramref name="King"/> sind <c>null</c>, wenn die Komponente NICHT
/// ANWENDBAR ist (keine Nicht-Bauern-Figur auf dem Brett bzw. mindestens eine Stellung ohne König —
/// reine Bauernendspiele, illegale Chessable-Diagramme). Sie fließen dann NICHT mit Bestnote ein,
/// sondern ihr Gewicht wird auf die übrigen Komponenten umgelegt; zusätzlich deckelt
/// <see cref="PositionSimilarity.Compare"/> den Gesamtscore je nicht anwendbarer Komponente um einen
/// Punkt, damit ein unvollständig gemessener Vergleich nie mit einem echten Volltreffer gleichzieht.</summary>
public readonly record struct SimilarityBreakdown(double Score, double Pawns, double Material, double? Pieces, double? King);

/// <summary>
/// Eine Stellung, verdichtet auf Bitmasken + Zähler. Bewusst OHNE Schachbibliothek aus dem
/// FEN-String geparst: Buch-/Kurs-FENs (Chessable-Diagramme) sind teils illegal — z. B. ohne
/// König — und würden in einer Bibliothek werfen. Hier ist alles optional; fehlt etwas, fällt
/// nur die betroffene Komponente aus.
/// </summary>
public sealed class PositionFeatures
{
    /// <summary>Feldindex = Reihe*8 + Linie (a1 = 0, h8 = 63). Index in dieses Array = Farbe*6 + Typ.</summary>
    private readonly ulong[] _masks;

    /// <summary>Das Stellungsfeld der FEN, unverändert — für „ist das dieselbe Stellung?".</summary>
    public string Placement { get; }

    /// <summary><c>'w'</c> oder <c>'b'</c> (fehlt das Feld: <c>'w'</c>).</summary>
    public char SideToMove { get; }

    /// <summary>Gesamtmaterial BEIDER Seiten in Bauerneinheiten (D 9, T 5, L/S 3, B 1; König 0).</summary>
    public int Material { get; }

    /// <summary>Material EINER Farbe (0 = weiß, 1 = schwarz) in Bauerneinheiten.</summary>
    public int MaterialOf(int color) => _materialByColor[color];

    private readonly int[] _materialByColor;

    /// <summary><c>true</c>, wenn überhaupt keine Figur auf dem Brett steht.</summary>
    public bool IsEmpty { get; }

    private PositionFeatures(ulong[] masks, string placement, char sideToMove)
    {
        _masks = masks;
        Placement = placement;
        SideToMove = sideToMove;
        var byColor = new int[2];
        bool any = false;
        for (int c = 0; c < 2; c++)
            for (int t = 0; t < 6; t++)
            {
                int n = BitOperations.PopCount(masks[c * 6 + t]);
                if (n > 0) any = true;
                byColor[c] += n * PositionSimilarity.Value((SimPieceType)t);
            }
        _materialByColor = byColor;
        Material = byColor[0] + byColor[1];
        IsEmpty = !any;
    }

    /// <summary>Maske eines Typs einer Farbe (<paramref name="color"/> 0 = weiß, 1 = schwarz).</summary>
    public ulong Mask(int color, SimPieceType type) => _masks[color * 6 + (int)type];

    /// <summary>Anzahl Figuren eines Typs einer Farbe.</summary>
    public int Count(int color, SimPieceType type) => BitOperations.PopCount(Mask(color, type));

    /// <summary>Figurenart auf einem Feld (0…63), unabhängig von der Farbe; <c>null</c> = leer.</summary>
    public SimPieceType? TypeAt(int square)
    {
        if (square is < 0 or > 63) return null;
        ulong bit = 1UL << square;
        for (int i = 0; i < _masks.Length; i++)
            if ((_masks[i] & bit) != 0) return (SimPieceType)(i % 6);
        return null;
    }

    /// <summary>Königsfeld (0…63) oder <c>-1</c>, wenn kein (oder mehr als ein) König da ist.</summary>
    public int KingSquare(int color)
    {
        var m = Mask(color, SimPieceType.King);
        return m == 0 ? -1 : BitOperations.TrailingZeroCount(m);
    }

    internal ulong[] RawMasks() => _masks;

    internal static PositionFeatures Create(ulong[] masks, string placement, char sideToMove)
        => new(masks, placement, sideToMove);
}

/// <summary>
/// Ähnlichkeit zweier Stellungen — reine Rechenlogik, kein DbContext, kein I/O, keine Schachregeln.
///
/// Vier Komponenten, jede als Distanz 0…1, Gesamtscore = 100 * (1 - Σ Gewicht·Distanz):
/// <list type="number">
///   <item>Bauerngerüst: Hamming der Bauernmasken, normiert auf die VEREINIGUNG der besetzten
///     Bauernfelder (nicht auf 16) — zwei abweichende Bauern von vier sind im Endspiel ein anderes
///     Bild als zwei von sechzehn.</item>
///   <item>Material: erst eine harte Schranke JE FARBE (&gt; 3 Bauerneinheiten Unterschied bei einer
///     der beiden Farben ⇒ gar kein Vergleich), dann die je Farbe normierte Zähler-Differenz.</item>
///   <item>Figurenplatzierung: EINS-ZU-EINS-Zuordnung der Nicht-Bauern-Figuren gleicher Farbe und
///     Art (gierig, kürzeste Abstände zuerst), Kosten je Paar aus dem Königsabstand (Chebyshev) —
///     NICHT Hamming, damit Sf3 gegen Sg5 als „nah" durchgeht statt als „völlig anders", aber mit
///     steilem Abfall, damit ab drei Feldern Abstand nichts mehr geschenkt wird. Steht auf beiden
///     Brettern gar keine Nicht-Bauern-Figur (reines Bauernendspiel), ist sie nicht anwendbar —
///     dann wandert ihr Gewicht auf die übrigen Komponenten.</item>
///   <item>König: Rochadeseite (Anteil 0,7) + Königsabstand (Anteil 0,3). Ohne König auf dem Brett
///     nicht anwendbar — dann wandert ihr Gewicht auf die anderen drei Komponenten.</item>
/// </list>
/// </summary>
public static class PositionSimilarity
{
    /// <summary>Materialwert in Bauerneinheiten.</summary>
    public static int Value(SimPieceType t) => t switch
    {
        SimPieceType.Pawn => 1,
        SimPieceType.Knight => 3,
        SimPieceType.Bishop => 3,
        SimPieceType.Rook => 5,
        SimPieceType.Queen => 9,
        _ => 0,   // König zählt nicht ins Material
    };

    /// <summary>Harte Materialschranke in Bauerneinheiten: mehr Unterschied ⇒ gar kein Treffer.</summary>
    public const int MaterialGateThreshold = 3;

    /// <summary>Maximaler Chebyshev-Abstand auf dem Brett.</summary>
    private const double MaxPieceDistance = 7.0;

    // ─── Extraktion ───────────────────────────────────────────────────────

    /// <summary>
    /// Parst NUR das Stellungsfeld (und die Seite am Zug) einer FEN. Tolerant: unbekannte Zeichen,
    /// zu kurze/zu lange Reihen und fehlende Könige sind erlaubt — es wird nie geworfen.
    /// </summary>
    public static PositionFeatures Extract(string? fen)
    {
        var masks = new ulong[12];
        var text = (fen ?? string.Empty).Trim();
        var placementEnd = text.IndexOf(' ');
        var placement = placementEnd < 0 ? text : text[..placementEnd];
        char side = 'w';
        if (placementEnd >= 0)
        {
            var rest = text[(placementEnd + 1)..].TrimStart();
            if (rest.Length > 0 && (rest[0] == 'b' || rest[0] == 'B')) side = 'b';
        }

        int rank = 7, file = 0;
        foreach (var ch in placement)
        {
            if (ch == '/') { rank--; file = 0; continue; }
            if (ch >= '1' && ch <= '8') { file += ch - '0'; continue; }
            if (rank is >= 0 and <= 7 && file is >= 0 and <= 7)
            {
                var type = TypeOf(ch);
                if (type != null)
                {
                    int color = char.IsUpper(ch) ? 0 : 1;
                    masks[color * 6 + (int)type.Value] |= 1UL << (rank * 8 + file);
                }
            }
            file++;
        }
        return PositionFeatures.Create(masks, placement, side);
    }

    private static SimPieceType? TypeOf(char ch) => char.ToLowerInvariant(ch) switch
    {
        'p' => SimPieceType.Pawn,
        'n' => SimPieceType.Knight,
        'b' => SimPieceType.Bishop,
        'r' => SimPieceType.Rook,
        'q' => SimPieceType.Queen,
        'k' => SimPieceType.King,
        _ => null,
    };

    /// <summary>
    /// Farbtausch: Brett an der Mittellinie spiegeln (Reihe r → 9-r) UND die Farben tauschen.
    /// Damit findet dieselbe Metrik auch „dieselbe Struktur mit vertauschten Farben".
    /// Das Stellungsfeld (<see cref="PositionFeatures.Placement"/>) wird mitgespiegelt, damit die
    /// Identitätsprüfung auch gegen das Spiegelbild funktioniert.
    /// </summary>
    public static PositionFeatures Mirror(PositionFeatures f)
    {
        var src = f.RawMasks();
        var masks = new ulong[12];
        for (int t = 0; t < 6; t++)
        {
            masks[0 * 6 + t] = FlipRanks(src[1 * 6 + t]);   // schwarz → weiß
            masks[1 * 6 + t] = FlipRanks(src[0 * 6 + t]);   // weiß → schwarz
        }
        return PositionFeatures.Create(masks, MirrorPlacement(f.Placement), f.SideToMove == 'w' ? 'b' : 'w');
    }

    /// <summary>Reihen spiegeln: Feldindex = Reihe*8 + Linie ⇒ ein Byte je Reihe ⇒ Byte-Reihenfolge drehen.</summary>
    private static ulong FlipRanks(ulong bits) => BinaryPrimitives.ReverseEndianness(bits);

    private static string MirrorPlacement(string placement)
    {
        var ranks = placement.Split('/');
        Array.Reverse(ranks);
        for (int i = 0; i < ranks.Length; i++)
        {
            var chars = ranks[i].ToCharArray();
            for (int j = 0; j < chars.Length; j++)
            {
                var c = chars[j];
                if (char.IsLetter(c)) chars[j] = char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
            }
            ranks[i] = new string(chars);
        }
        return string.Join('/', ranks);
    }

    // ─── Vergleich ────────────────────────────────────────────────────────

    /// <summary>
    /// Harte Materialschranke: <c>false</c> ⇒ die Stellungen werden gar nicht verglichen
    /// (spart den Großteil der Rechenzeit über ein ganzes Repertoire).
    ///
    /// Geprüft wird JE FARBE, nicht die Gesamtsumme: „ich habe die Dame" und „der Gegner hat die
    /// Dame" haben dieselbe Summe, sind aber gegensätzliche Stellungstypen. Über die Summe kam so
    /// ein Paar mit 92 durch (belegt an
    /// <c>r3k2r/…/R2QK2R</c> gegen <c>r2qk2r/…/R3K2R</c>) — mit der Prüfung je Farbe fällt es raus.
    /// </summary>
    public static bool PassesMaterialGate(PositionFeatures a, PositionFeatures b)
        => Math.Abs(a.MaterialOf(0) - b.MaterialOf(0)) <= MaterialGateThreshold
        && Math.Abs(a.MaterialOf(1) - b.MaterialOf(1)) <= MaterialGateThreshold;

    /// <summary>
    /// Abzug von der Obergrenze je NICHT ANWENDBARER Komponente (in Punkten).
    ///
    /// 100 heißt „in allen vier Komponenten gemessen und überall identisch". Konnte eine Komponente
    /// gar nicht gemessen werden, weiß der Vergleich strikt weniger — er darf dann nicht mit einem
    /// vollständigen Volltreffer gleichziehen. Belegt: <c>8/8/3n4/2p5/2P5/3N4/8/8</c> (königsloses
    /// Diagramm) gegen <c>8/4k3/3n4/2p5/2P5/3N4/4K3/8</c> kam auf glatte 100,0.
    ///
    /// Bewusst nur EIN Punkt je Komponente statt eines Abzugs in Höhe des ausgefallenen Gewichts:
    /// Letzteres würde königslose Chessable-Diagramme pauschal um 5…10 Punkte bestrafen — also genau
    /// den Fehler in die andere Richtung machen, den die Umlegung des Gewichts beheben sollte. Der
    /// Deckel greift ausschließlich an der Spitze und ändert an der Rangfolge darunter nichts.
    /// </summary>
    public const double NotApplicablePenalty = 1.0;

    /// <summary>
    /// Gesamtscore + die vier Einzelwerte (je 0…100; <c>null</c> = nicht anwendbar).
    ///
    /// Fehlt in einer der Stellungen der König (illegale Diagramm-FEN) oder steht auf keinem der
    /// beiden Bretter eine Nicht-Bauern-Figur (reines Bauernendspiel), ist die betroffene Komponente
    /// NICHT ANWENDBAR: sie wird nicht mit 100 gutgeschrieben, sondern ihr Gewicht wird auf die
    /// übrigen umgelegt. Sonst bekämen genau die Stellungen, über die am wenigsten bekannt ist,
    /// Gratispunkte. Zusätzlich greift der Deckel aus <see cref="NotApplicablePenalty"/>.
    /// </summary>
    public static SimilarityBreakdown Compare(PositionFeatures a, PositionFeatures b, SimilarityWeights w)
    {
        double dPawns = PawnDistance(a, b);
        double dMaterial = MaterialDistance(a, b);
        double? dPieces = PieceDistance(a, b);
        double? dKing = KingDistance(a, b);

        double wPieces = dPieces.HasValue ? w.Pieces : 0.0;
        double wKing = dKing.HasValue ? w.King : 0.0;
        double sumWeights = w.Pawns + w.Material + wPieces + wKing;
        double total = sumWeights <= 0 ? 0.0
            : (w.Pawns * dPawns + w.Material * dMaterial + wPieces * (dPieces ?? 0.0) + wKing * (dKing ?? 0.0)) / sumWeights;

        int notApplicable = (dPieces.HasValue ? 0 : 1) + (dKing.HasValue ? 0 : 1);

        return new SimilarityBreakdown(
            Score: Math.Min(Pct(total), 100.0 - notApplicable * NotApplicablePenalty),
            Pawns: Pct(dPawns),
            Material: Pct(dMaterial),
            Pieces: dPieces.HasValue ? Pct(dPieces.Value) : null,
            King: dKing.HasValue ? Pct(dKing.Value) : null);
    }

    private static double Pct(double distance) => Math.Clamp(100.0 * (1.0 - distance), 0.0, 100.0);

    /// <summary>Bauerngerüst: Hamming, normiert auf die Vereinigung der Bauernfelder beider Stellungen.
    /// Zwei bauernlose Stellungen gelten als identisch (Distanz 0).</summary>
    internal static double PawnDistance(PositionFeatures a, PositionFeatures b)
    {
        ulong wa = a.Mask(0, SimPieceType.Pawn), wb = b.Mask(0, SimPieceType.Pawn);
        ulong ba = a.Mask(1, SimPieceType.Pawn), bb = b.Mask(1, SimPieceType.Pawn);
        int diff = BitOperations.PopCount(wa ^ wb) + BitOperations.PopCount(ba ^ bb);
        int union = BitOperations.PopCount(wa | wb) + BitOperations.PopCount(ba | bb);
        return union == 0 ? 0.0 : (double)diff / union;
    }

    /// <summary>
    /// Material: gewichtete Zähler-Differenz je Typ, JE FARBE auf das Material dieser Farbe normiert
    /// und dann über die Farben gemittelt.
    ///
    /// Nicht über beide Farben zusammen normieren: sonst verdünnt das viele Material der einen Seite
    /// den Fehlbetrag der anderen — „Schwarz fehlt die Dame" ist ein Viertel von Schwarz, nicht ein
    /// Achtel des Bretts.
    /// </summary>
    internal static double MaterialDistance(PositionFeatures a, PositionFeatures b)
    {
        double sum = 0;
        int counted = 0;
        for (int c = 0; c < 2; c++)
        {
            int total = a.MaterialOf(c) + b.MaterialOf(c);
            if (total == 0) continue;                 // diese Farbe hat nirgends Material → nichts zu vergleichen
            int diff = 0;
            for (int t = 0; t < 6; t++)
            {
                var type = (SimPieceType)t;
                diff += Math.Abs(a.Count(c, type) - b.Count(c, type)) * Value(type);
            }
            sum += Math.Clamp((double)diff / total, 0.0, 1.0);
            counted++;
        }
        return counted == 0 ? 0.0 : sum / counted;
    }

    /// <summary>
    /// Ab diesem Chebyshev-Abstand gilt eine Figur als „woanders" (volle Strafe). Absichtlich klein:
    /// mit dem früheren linearen Abfall über 7 Felder waren zwei völlig verschiedene Eröffnungen bei
    /// 90…97 — die Komponente hat nichts unterschieden. Drei Felder sind mehr als ein Springerzug.
    /// </summary>
    private const double PieceDistanceSaturation = 3.0;

    /// <summary>Strafe 0…1 für einen Feldabstand — steiler Abfall statt <c>d/7</c>.</summary>
    private static double PiecePenalty(double chebyshev) => Math.Min(1.0, chebyshev / PieceDistanceSaturation);

    /// <summary>
    /// Figurenplatzierung (ohne Bauern und Könige): je Farbe und Typ werden die Figuren EINS ZU EINS
    /// zugeordnet (gierig, kürzeste Abstände zuerst); jede Figur der Gegenstellung darf also nur
    /// EINMAL vergeben werden. Ohne das durfte derselbe Springer beide gegnerischen Springer
    /// „erklären", und der Wert lag praktisch immer bei 95…99.
    ///
    /// Kosten je Paar = <see cref="PiecePenalty"/> des Chebyshev-Abstands; Figuren ohne Partner
    /// (Anzahl-Unterschied oder Typ fehlt drüben ganz) kosten die volle Strafe 1. Gewichtet mit dem
    /// Figurenwert, normiert auf die Summe der Gewichte.
    ///
    /// Steht auf KEINEM der beiden Bretter eine Nicht-Bauern-Figur (reines Bauernendspiel), gibt es
    /// nichts zu vergleichen: dann ist die Komponente NICHT ANWENDBAR (<c>null</c>) — dieselbe Falle
    /// wie beim König. Vorher wurde die Distanz auf 0 gesetzt und die Komponente mit 100 verbucht,
    /// also volle Punkte für einen Vergleich, der gar nicht stattgefunden hat (auch bei völlig
    /// disjunkten Bauernendspielen).
    /// </summary>
    internal static double? PieceDistance(PositionFeatures a, PositionFeatures b)
    {
        double weighted = 0, weights = 0;
        for (int c = 0; c < 2; c++)
            foreach (var type in new[] { SimPieceType.Knight, SimPieceType.Bishop, SimPieceType.Rook, SimPieceType.Queen })
                MatchType(a.Mask(c, type), b.Mask(c, type), Value(type), ref weighted, ref weights);
        return weights == 0 ? null : Math.Clamp(weighted / weights, 0.0, 1.0);
    }

    private static void MatchType(ulong ma, ulong mb, double value, ref double weighted, ref double weights)
    {
        var sa = Squares(ma);
        var sb = Squares(mb);
        int slots = Math.Max(sa.Count, sb.Count);
        if (slots == 0) return;
        weights += slots * value;

        // Gieriges 1:1-Matching: alle Paare nach Abstand, kürzeste zuerst, jede Figur nur einmal.
        // Bei höchstens ~10 Figuren je Typ/Farbe ist das billiger als eine Ungarische Methode und
        // liefert für Schachstellungen dasselbe Bild.
        var pairs = new List<(int Distance, int A, int B)>(sa.Count * sb.Count);
        for (int i = 0; i < sa.Count; i++)
            for (int j = 0; j < sb.Count; j++)
                pairs.Add((Chebyshev(sa[i], sb[j]), i, j));
        pairs.Sort((x, y) => x.Distance.CompareTo(y.Distance));

        var usedA = new bool[sa.Count];
        var usedB = new bool[sb.Count];
        int matched = 0;
        foreach (var (distance, i, j) in pairs)
        {
            if (usedA[i] || usedB[j]) continue;
            usedA[i] = usedB[j] = true;
            weighted += value * PiecePenalty(distance);
            if (++matched == slots) break;
        }
        weighted += value * (slots - matched);        // Figuren ohne Partner: volle Strafe
    }

    private static List<int> Squares(ulong mask)
    {
        var list = new List<int>(BitOperations.PopCount(mask));
        while (mask != 0)
        {
            list.Add(BitOperations.TrailingZeroCount(mask));
            mask &= mask - 1;
        }
        return list;
    }

    // ─── Felder ───────────────────────────────────────────────────────────

    /// <summary>„e4" → Feldindex 28 (Reihe*8 + Linie, a1 = 0); <c>null</c> bei Unsinn.</summary>
    public static int? SquareFromAlgebraic(string? text)
    {
        var s = (text ?? "").Trim().ToLowerInvariant();
        if (s.Length != 2) return null;
        int file = s[0] - 'a', rank = s[1] - '1';
        if (file is < 0 or > 7 || rank is < 0 or > 7) return null;
        return rank * 8 + file;
    }

    /// <summary>Feldindex 28 → „e4".</summary>
    public static string AlgebraicFromSquare(int square)
        => square is < 0 or > 63 ? "" : $"{(char)('a' + (square & 7))}{(char)('1' + (square >> 3))}";

    /// <summary>Feld an der Mittellinie spiegeln (a1 ↔ a8) — das Gegenstück zu <see cref="Mirror"/>
    /// für einzelne Felder, damit ein Anfragezug auch im farbvertauschten Vergleich passt.</summary>
    public static int MirrorSquare(int square) => square ^ 56;

    /// <summary>Königsabstand zweier Felder: max(|Δ Linie|, |Δ Reihe|).</summary>
    internal static int Chebyshev(int squareA, int squareB)
        => Math.Max(Math.Abs((squareA & 7) - (squareB & 7)), Math.Abs((squareA >> 3) - (squareB >> 3)));

    /// <summary>
    /// König: je Farbe 0,7 · (Rochadeseite verschieden?) + 0,3 · (Königsabstand / 7), über die
    /// vergleichbaren Farben gemittelt. Fehlt ein König (illegale Diagramm-FEN), fällt die Farbe
    /// raus; ist KEINE Farbe vergleichbar, ist die Komponente nicht anwendbar (<c>null</c>) — sie
    /// darf dann weder strafen noch (wie früher) 100 Punkte verschenken, ihr Gewicht wandert in
    /// <see cref="Compare"/> auf die übrigen Komponenten.
    /// </summary>
    internal static double? KingDistance(PositionFeatures a, PositionFeatures b)
    {
        double sum = 0;
        int counted = 0;
        for (int c = 0; c < 2; c++)
        {
            int ka = a.KingSquare(c), kb = b.KingSquare(c);
            if (ka < 0 || kb < 0) continue;
            double sideDiff = CastleSide(ka) == CastleSide(kb) ? 0.0 : 1.0;
            sum += 0.7 * sideDiff + 0.3 * (Chebyshev(ka, kb) / MaxPieceDistance);
            counted++;
        }
        return counted == 0 ? null : sum / counted;
    }

    /// <summary>Rochadeseite: 1 = kurz (g/h), -1 = lang (a/b/c), 0 = Zentrum (d/e/f).</summary>
    private static int CastleSide(int square)
    {
        int file = square & 7;
        if (file >= 6) return 1;
        if (file <= 2) return -1;
        return 0;
    }
}
