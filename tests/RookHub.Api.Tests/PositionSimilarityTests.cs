using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Einheitstests der reinen Ähnlichkeits-Metrik (<see cref="PositionSimilarity"/>) — ohne DB, ohne
/// Repertoires. Die schachlichen Prüffälle stehen mit Klartext-Namen der Stellung dabei: wenn hier
/// später etwas nachjustiert wird, muss nachvollziehbar bleiben, WARUM ein Paar ähnlich sein soll.
/// </summary>
public class PositionSimilarityTests
{
    // ─── Referenzstellungen (echte Partiestellungen, per Zugfolge erreicht) ───

    /// <summary>Grundstellung.</summary>
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>Damengambit, Abtauschvariante — Karlsbader Struktur:
    /// 1.d4 d5 2.c4 e6 3.Nc3 Nf6 4.cxd5 exd5 5.Bg5 c6 6.e3 Be7 7.Bd3 0-0 8.Nf3 Nbd7 9.Qc2 Te8.
    /// Weiße Bauern a2 b2 d4 e3 f2 g2 h2, schwarze a7 b7 c6 d5 f7 g7 h7.</summary>
    private const string Carlsbad_QGD = "r1bqr1k1/pp1nbppp/2p2n2/3p2B1/3P4/2NBPN2/PPQ2PPP/R3K2R w KQ - 7 10";

    /// <summary>Nimzoindisch (Rubinstein) mit Übergang in DIESELBE Karlsbader Struktur:
    /// 1.d4 Nf6 2.c4 e6 3.Nc3 Bb4 4.e3 0-0 5.Bd3 d5 6.cxd5 exd5 7.Nge2 Te8 8.0-0 c6 9.Ng3 Nbd7.
    /// Anderer Eröffnungsweg, identisches Bauerngerüst, andere Figurenaufstellung.</summary>
    private const string Carlsbad_Nimzo = "r1bqr1k1/pp1n1ppp/2p2n2/3p4/1b1P4/2NBP1N1/PP3PPP/R1BQ1RK1 w - - 2 10";

    /// <summary>Königsindisch, Mar-del-Plata-Aufbau: 1.d4 Nf6 2.c4 g6 3.Nc3 Bg7 4.e4 d6 5.Nf3 0-0
    /// 6.Be2 e5 7.0-0 Nc6 8.d5 Ne7. Gleiches Material wie oben, aber ein völlig anderes Bauernbild
    /// (blockiertes Zentrum d5/e4 gegen d6/e5).</summary>
    private const string KingsIndian = "r1bq1rk1/ppp1npbp/3p1np1/3Pp3/2P1P3/2N2N2/PP2BPPP/R1BQ1RK1 w - - 1 9";

    /// <summary>Sizilianisch Najdorf: 1.e4 c5 2.Nf3 d6 3.d4 cxd4 4.Nxd4 Nf6 5.Nc3 a6 6.Be2 e5
    /// 7.Nb3 Be7 8.0-0 0-0.</summary>
    private const string Najdorf = "rnbq1rk1/1p2bppp/p2p1n2/4p3/4P3/1NN5/PPP1BPPP/R1BQ1RK1 w - - 4 9";

    /// <summary>Dieselbe Najdorf-Stellung mit vertauschten Farben (an der Mittellinie gespiegelt) —
    /// so sieht sie „von der Englisch-Seite" aus.</summary>
    private const string NajdorfMirrored = "r1bq1rk1/ppp1bppp/1nn5/4p3/4P3/P2P1N2/1P2BPPP/RNBQ1RK1 b - - 4 9";

    private static SimilarityBreakdown Cmp(string a, string b, SimilarityWeights? w = null)
        => PositionSimilarity.Compare(PositionSimilarity.Extract(a), PositionSimilarity.Extract(b),
            w ?? SimilarityWeights.Ausgewogen);

    // ─── Extract ──────────────────────────────────────────────────────────

    [Fact]
    public void Extract_StartPosition_CountsMaterialAndPieces()
    {
        var f = PositionSimilarity.Extract(Start);

        Assert.Equal(8, f.Count(0, SimPieceType.Pawn));
        Assert.Equal(8, f.Count(1, SimPieceType.Pawn));
        Assert.Equal(2, f.Count(0, SimPieceType.Rook));
        Assert.Equal(1, f.Count(1, SimPieceType.Queen));
        Assert.Equal('w', f.SideToMove);
        Assert.False(f.IsEmpty);
        // 2*(8*1 + 2*3 + 2*3 + 2*5 + 9) = 78 Bauerneinheiten (König zählt 0).
        Assert.Equal(78, f.Material);
        // e1 = Reihe 0, Linie 4 → Feldindex 4; e8 = 7*8+4 = 60.
        Assert.Equal(4, f.KingSquare(0));
        Assert.Equal(60, f.KingSquare(1));
    }

    [Fact]
    public void Extract_SideToMoveBlack_IsRead()
        => Assert.Equal('b', PositionSimilarity.Extract(NajdorfMirrored).SideToMove);

    [Fact]
    public void Extract_PlacementOnlyFen_Works()
    {
        var f = PositionSimilarity.Extract("8/8/4k3/8/8/8/4K3/8");
        Assert.Equal('w', f.SideToMove);          // fehlendes Feld → Weiß am Zug
        Assert.Equal(0, f.Material);
    }

    /// <summary>Chessable-Diagramme (Info-Varianten) tragen oft KÖNIGSLOSE, also illegale FENs.
    /// Eine Schachbibliothek würde hier werfen — die Extraktion darf das nicht (siehe v0.316.3).</summary>
    [Fact]
    public void Extract_IllegalFenWithoutKing_DoesNotThrow()
    {
        var f = PositionSimilarity.Extract("8/5p2/6p1/7p/7P/6P1/5P2/R7 w - - 0 1");

        Assert.Equal(-1, f.KingSquare(0));
        Assert.Equal(-1, f.KingSquare(1));
        Assert.Equal(1, f.Count(0, SimPieceType.Rook));
        Assert.Equal(11, f.Material);            // Turm 5 + 3 weiße + 3 schwarze Bauern
    }

    [Fact]
    public void Compare_IllegalFensWithoutKing_DoNotThrowAndKingIsNotApplicable()
    {
        // Zwei königslose Diagramme mit identischer Bauernkette.
        var a = "8/5p2/6p1/7p/7P/6P1/5P2/R7 w - - 0 1";
        var b = "8/5p2/6p1/7p/7P/6P1/5P2/7R w - - 0 1";   // Turm h1 statt a1

        var r = Cmp(a, b);

        Assert.Equal(100, r.Pawns, 3);
        Assert.Null(r.King);                     // ohne Könige: nicht anwendbar (früher: 100 geschenkt)
        Assert.NotNull(r.Pieces);                // Türme stehen ja — die Komponente ist anwendbar
        Assert.True(r.Score > 60, $"Score {r.Score}");
    }

    [Fact]
    public void Extract_GarbageFen_DoesNotThrow()
    {
        var f = PositionSimilarity.Extract("?!/?? -- 99");
        Assert.True(f.IsEmpty);
    }

    // ─── Mirror ───────────────────────────────────────────────────────────

    [Fact]
    public void Mirror_StartPosition_IsStartPositionWithOtherSideToMove()
    {
        var m = PositionSimilarity.Mirror(PositionSimilarity.Extract(Start));

        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", m.Placement);
        Assert.Equal('b', m.SideToMove);
    }

    [Fact]
    public void Mirror_Najdorf_YieldsTheKnownColourSwappedPosition()
    {
        var m = PositionSimilarity.Mirror(PositionSimilarity.Extract(Najdorf));
        Assert.Equal(NajdorfMirrored.Split(' ')[0], m.Placement);
    }

    [Fact]
    public void Mirror_Twice_IsIdentity()
    {
        var once = PositionSimilarity.Mirror(PositionSimilarity.Extract(Carlsbad_QGD));
        var twice = PositionSimilarity.Mirror(once);
        Assert.Equal(Carlsbad_QGD.Split(' ')[0], twice.Placement);
        Assert.Equal('w', twice.SideToMove);
    }

    /// <summary>Farbtausch: Stellung gegen ihr Spiegelbild ist über den gespiegelten Vergleich
    /// perfekt ähnlich — das ist der Wert, den die Suche mit <c>mirrored=true</c> meldet.</summary>
    [Fact]
    public void Compare_PositionAgainstItsColourSwappedMirror_IsPerfect()
    {
        var mirroredQuery = PositionSimilarity.Mirror(PositionSimilarity.Extract(NajdorfMirrored));
        var r = PositionSimilarity.Compare(mirroredQuery, PositionSimilarity.Extract(Najdorf), SimilarityWeights.Ausgewogen);
        Assert.Equal(100, r.Score, 3);
    }

    // ─── Materialschranke ─────────────────────────────────────────────────

    [Fact]
    public void MaterialGate_QueenDown_IsRejected()
    {
        // Najdorf-Stellung, aber Schwarz fehlt die Dame — ohne Schranke wäre das (alles andere ist
        // identisch) ein Top-Treffer; mit Schranke gar keiner.
        var withoutQueen = "rnb2rk1/1p2bppp/p2p1n2/4p3/4P3/1NN5/PPP1BPPP/R1BQ1RK1 w - - 4 9";
        var a = PositionSimilarity.Extract(Najdorf);
        var b = PositionSimilarity.Extract(withoutQueen);

        Assert.False(PositionSimilarity.PassesMaterialGate(a, b));
        Assert.True(PositionSimilarity.Compare(a, b, SimilarityWeights.Ausgewogen).Score > 90,
            "ohne Schranke wäre die Stellung sehr ähnlich — genau deshalb braucht es die Schranke");
    }

    [Fact]
    public void MaterialGate_MinorPieceDown_StillPasses()
    {
        // Najdorf ohne den schwarzen Springer b8: Unterschied 3 Bauerneinheiten = genau die Schranke.
        var withoutKnight = "r1bq1rk1/1p2bppp/p2p1n2/4p3/4P3/1NN5/PPP1BPPP/R1BQ1RK1 w - - 4 9";
        Assert.True(PositionSimilarity.PassesMaterialGate(
            PositionSimilarity.Extract(Najdorf), PositionSimilarity.Extract(withoutKnight)));
    }

    // ─── Bauerngerüst: Normierung auf die Vereinigung ─────────────────────

    /// <summary>Zwei abweichende Bauern von vier sind im Endspiel ein anderes Bild als zwei von
    /// sechzehn — deshalb wird auf die VEREINIGUNG normiert, nicht auf 16.</summary>
    [Fact]
    public void PawnDistance_IsNormalisedToTheUnion_NotTo16()
    {
        // Endspiel: je zwei Bauern, einer davon verschoben.
        var endgameA = "4k3/8/8/8/8/8/PP6/4K3 w - - 0 1";
        var endgameB = "4k3/8/8/8/8/P7/1P6/4K3 w - - 0 1";
        var sparse = Cmp(endgameA, endgameB).Pawns;

        // Eröffnung: alle 16 Bauern, EIN Bauer verschoben (a2→a3) — dieselbe absolute Abweichung.
        var openingA = Start;
        var openingB = "rnbqkbnr/pppppppp/8/8/8/P7/1PPPPPPP/RNBQKBNR b KQkq - 0 1";
        var dense = Cmp(openingA, openingB).Pawns;

        // 2 von (Vereinigung 3) abweichend → 33; 2 von (Vereinigung 17) → 88.
        Assert.Equal(100.0 * (1 - 2.0 / 3), sparse, 3);
        Assert.Equal(100.0 * (1 - 2.0 / 17), dense, 3);
        Assert.True(sparse < dense - 40);
    }

    [Fact]
    public void PawnDistance_BothSidesPawnless_CountsAsIdentical()
    {
        var a = "4k3/8/8/8/8/8/8/R3K3 w - - 0 1";
        var b = "4k3/8/8/8/8/8/8/4K2R w - - 0 1";
        Assert.Equal(100, Cmp(a, b).Pawns, 3);
    }

    // ─── Figurenplatzierung: Abstand statt Hamming ────────────────────────

    /// <summary>Sf3 gegen Sg5 ist Abstand 2, nicht „völlig anders" — Hamming könnte das nicht von
    /// Sf3 gegen Sa8 unterscheiden.</summary>
    [Fact]
    public void PieceDistance_UsesChebyshevDistance_NotHamming_AndFallsOffSteeply()
    {
        var knightF3 = "4k3/8/8/8/8/5N2/8/4K3 w - - 0 1";
        var knightG4 = "4k3/8/8/8/6N1/8/8/4K3 w - - 0 1";
        var knightG5 = "4k3/8/8/6N1/8/8/8/4K3 w - - 0 1";
        var knightA8 = "N3k3/8/8/8/8/8/8/4K3 w - - 0 1";

        var veryNear = Cmp(knightF3, knightG4).Pieces!.Value;
        var near = Cmp(knightF3, knightG5).Pieces!.Value;
        var far = Cmp(knightF3, knightA8).Pieces!.Value;

        Assert.Equal(100.0 * (1 - 1.0 / 3), veryNear, 3);   // f3→g4: max(1,1) = 1
        Assert.Equal(100.0 * (1 - 2.0 / 3), near, 3);       // f3→g5: max(|f-g|,|3-5|) = 2
        Assert.Equal(0.0, far, 3);                          // f3→a8: max(5,5) = 5 ⇒ volle Strafe
        Assert.True(veryNear > near && near > far);
    }

    /// <summary>Zwei Springer derselben Farbe dürfen NICHT beide demselben gegnerischen Springer
    /// zugeordnet werden — sonst sättigt die Komponente und unterscheidet nichts mehr (Befund B aus
    /// der Gegenlesung: reale Stellungen landeten fast immer bei 95–99).</summary>
    [Fact]
    public void PieceDistance_MatchesOneToOne_NotNearestNeighbour()
    {
        var knightsC3F3 = "4k3/8/8/8/8/2N2N2/8/4K3 w - - 0 1";
        var knightsA8F3 = "N3k3/8/8/8/8/5N2/8/4K3 w - - 0 1";

        var r = Cmp(knightsC3F3, knightsA8F3).Pieces!.Value;

        // 1:1 — f3↔f3 (Abstand 0) und c3↔a8 (Abstand 5 ⇒ volle Strafe 1), also 1 von 2 Plätzen.
        // Mit „nächster Nachbar“ hätten beide Springer f3 gewählt — der Wert wäre 100 statt 50.
        Assert.Equal(50.0, r, 3);
    }

    [Fact]
    public void PieceDistance_MissingTypeCountsAsMaximumDistance()
    {
        var queenAndRook = "4k3/8/8/8/8/8/8/R2QK3 w - - 0 1";
        var rookOnly = "4k3/8/8/8/8/8/8/R3K3 w - - 0 1";
        // Die Dame findet drüben keinen Partner → volle Strafe 1 (die Türme stehen gleich).
        // Gewichte: ein Damen-Platz (9) + ein Turm-Platz (5).
        var expected = 100.0 * (1 - 9 * 1.0 / (9 + 5));
        Assert.Equal(expected, Cmp(queenAndRook, rookOnly).Pieces!.Value, 3);
    }

    /// <summary>Dieselbe Falle wie beim König, nur eine Komponente weiter: steht auf keinem der
    /// beiden Bretter eine Nicht-Bauern-Figur (reines Bauernendspiel), gab es früher trotzdem die
    /// volle Note 100 — Gratispunkte für einen Vergleich, der gar nicht stattgefunden hat. Jetzt ist
    /// die Komponente nicht anwendbar und ihr Gewicht liegt auf den übrigen.</summary>
    [Fact]
    public void PieceDistance_NoPiecesAtAll_IsNotApplicable()
    {
        // Zwei Bauernendspiele mit VÖLLIG verschiedenen Bauern (Distanz 1,0) — genau der Fall, in
        // dem „keine Figuren da" früher 100 einbrachte.
        var a = "4k3/pp6/8/8/8/8/8/4K3 w - - 0 1";
        var b = "4k3/8/8/8/8/8/6PP/4K3 w - - 0 1";

        var r = Cmp(a, b);

        Assert.Null(r.Pieces);
        Assert.Equal(0, r.Pawns, 3);              // kein einziges gemeinsames Bauernfeld
        Assert.Equal(100, r.King!.Value, 3);      // beide Könige stehen gleich
        // Gewichte ohne Figuren: 0,50 Bauern / 0,20 Material / 0,10 König → Summe 0,80.
        // Material: Weiß 2 Bauern gegen 0 und Schwarz 0 gegen 2 ⇒ je Farbe volle Differenz.
        Assert.Equal(100.0 * (1 - (0.5 * 1.0 + 0.2 * 1.0) / 0.8), r.Score, 3);
    }

    /// <summary>Nicht anwendbar heißt auch: nie Bestnote. Ein königsloses Diagramm, das in allen
    /// MESSBAREN Komponenten identisch ist, kam auf glatte 100,0 und zog damit mit einem echten
    /// Volltreffer gleich. Der Deckel nimmt je nicht anwendbarer Komponente einen Punkt.</summary>
    [Fact]
    public void NotApplicableComponents_CapTheScoreBelowAPerfectMatch()
    {
        // Königsloses Diagramm gegen dieselbe Stellung MIT Königen: Bauern, Material und Figuren
        // stimmen exakt, nur die Königskomponente ist nicht anwendbar.
        var oneMissing = Cmp("8/8/3n4/2p5/2P5/3N4/8/8", "8/4k3/3n4/2p5/2P5/3N4/4K3/8");
        Assert.Null(oneMissing.King);
        Assert.Equal(100, oneMissing.Pieces!.Value, 3);
        Assert.Equal(99.0, oneMissing.Score, 3);

        // Bauernendspiel ohne Könige: König UND Figuren fallen aus → zwei Punkte Abzug.
        var twoMissing = Cmp("8/8/8/2p5/2P5/8/8/8", "8/8/8/2p5/2P5/8/8/8");
        Assert.Null(twoMissing.King);
        Assert.Null(twoMissing.Pieces);
        Assert.Equal(98.0, twoMissing.Score, 3);

        // Gegenprobe: vollständig vergleichbar und identisch ⇒ weiterhin glatte 100.
        var complete = Cmp("8/4k3/3n4/2p5/2P5/3N4/4K3/8", "8/4k3/3n4/2p5/2P5/3N4/4K3/8");
        Assert.Equal(100.0, complete.Score, 3);
        Assert.True(complete.Score > oneMissing.Score && oneMissing.Score > twoMissing.Score);
    }

    // ─── König ────────────────────────────────────────────────────────────

    [Fact]
    public void KingDistance_SameCastlingSideBeatsOppositeSide()
    {
        var bothShort = "6k1/8/8/8/8/8/8/6K1 w - - 0 1";
        var whiteLong = "6k1/8/8/8/8/8/8/2K5 w - - 0 1";

        Assert.Equal(100, Cmp(bothShort, bothShort).King!.Value, 3);
        // Weiß: andere Rochadeseite (0,7) + Abstand g1→c1 = 4 (0,3 * 4/7); Schwarz identisch.
        var expected = 100.0 * (1 - (0.7 + 0.3 * 4.0 / 7) / 2);
        Assert.Equal(expected, Cmp(bothShort, whiteLong).King!.Value, 3);
    }

    [Fact]
    public void KingDistance_OneSideMissingKing_UsesOnlyTheOtherColour()
    {
        var noWhiteKing = "6k1/8/8/8/8/8/8/8 w - - 0 1";
        var kings = "6k1/8/8/8/8/8/8/6K1 w - - 0 1";
        Assert.Equal(100, Cmp(noWhiteKing, kings).King!.Value, 3);   // nur Schwarz vergleichbar, dort identisch
    }

    // ─── Schachliche Gesamtfälle ──────────────────────────────────────────

    /// <summary>Gleiches Bauerngerüst aus verschiedenen Eröffnungen (Karlsbader Struktur aus dem
    /// Abtausch-Damengambit und aus Nimzoindisch) muss hoch punkten.</summary>
    [Fact]
    public void SameStructureFromDifferentOpenings_ScoresHigh()
    {
        var r = Cmp(Carlsbad_QGD, Carlsbad_Nimzo);

        Assert.Equal(100, r.Pawns, 3);        // identisches Gerüst
        Assert.Equal(100, r.Material, 3);     // vollzählig auf beiden Seiten
        Assert.True(r.Score >= 90, $"Score {r.Score} — gleiches Gerüst muss hoch punkten");
    }

    /// <summary>Gleiche Figuren, deutlich anderes Bauernbild (Karlsbader Struktur gegen die
    /// blockierte Königsinder-Kette) → deutlich niedriger.</summary>
    [Fact]
    public void SamePiecesButDifferentPawnPicture_ScoresClearlyLower()
    {
        var same = Cmp(Carlsbad_QGD, Carlsbad_Nimzo);
        var other = Cmp(Carlsbad_QGD, KingsIndian);

        Assert.True(other.Pawns < 60, $"Bauernwert {other.Pawns}");
        Assert.True(other.Score < same.Score - 20, $"{other.Score} vs {same.Score}");
    }

    // ─── Voreinstellungen ─────────────────────────────────────────────────

    [Fact]
    public void Presets_HaveTheAgreedWeights()
    {
        Assert.Equal(new SimilarityWeights(0.75, 0.10, 0.10, 0.05), SimilarityWeights.Struktur);
        Assert.Equal(new SimilarityWeights(0.50, 0.20, 0.20, 0.10), SimilarityWeights.Ausgewogen);
        Assert.Equal(new SimilarityWeights(0.30, 0.15, 0.45, 0.10), SimilarityWeights.Stellungsbild);
        foreach (var w in new[] { SimilarityWeights.Struktur, SimilarityWeights.Ausgewogen, SimilarityWeights.Stellungsbild })
            Assert.Equal(1.0, w.Pawns + w.Material + w.Pieces + w.King, 6);
    }

    [Fact]
    public void Presets_UnknownNameFallsBackToBalanced()
    {
        Assert.Equal(SimilarityWeights.Ausgewogen, SimilarityWeights.FromPreset(null));
        Assert.Equal(SimilarityWeights.Ausgewogen, SimilarityWeights.FromPreset("quatsch"));
        Assert.Equal(SimilarityWeights.Struktur, SimilarityWeights.FromPreset("Struktur"));
        Assert.Equal("stellungsbild", SimilarityWeights.NormalizePreset("Stellungsbild"));
        Assert.Equal("ausgewogen", SimilarityWeights.NormalizePreset("gibtsnicht"));
    }

    /// <summary>Dieselben zwei Kandidaten, andere Reihenfolge: „struktur" bevorzugt das gleiche
    /// Bauernbild (Nimzo-Karlsbad), „stellungsbild" die gleiche Figurenstellung (dieselbe Partie
    /// nach dem Abtausch im Zentrum — Figuren fast unverändert, Gerüst verändert).</summary>
    [Fact]
    public void Presets_RankTheSameTwoCandidatesDifferently()
    {
        // 9...c5 10.Bh4 cxd4 … Zentrum aufgelöst: Figuren stehen fast wie in Carlsbad_QGD,
        // aber der weiße d- und der schwarze c-Bauer sind weg.
        const string liquidated = "r1bqr1k1/pp2bppp/5n2/2np4/7B/2NBPN2/PPQ2PPP/R3K2R w KQ - 0 12";

        var structNimzo = Cmp(Carlsbad_QGD, Carlsbad_Nimzo, SimilarityWeights.Struktur).Score;
        var structLiq = Cmp(Carlsbad_QGD, liquidated, SimilarityWeights.Struktur).Score;
        var pictureNimzo = Cmp(Carlsbad_QGD, Carlsbad_Nimzo, SimilarityWeights.Stellungsbild).Score;
        var pictureLiq = Cmp(Carlsbad_QGD, liquidated, SimilarityWeights.Stellungsbild).Score;

        Assert.True(structNimzo > structLiq, $"struktur: {structNimzo} vs {structLiq}");
        Assert.True(pictureLiq > pictureNimzo, $"stellungsbild: {pictureLiq} vs {pictureNimzo}");
    }

    // ─── Referenzpaare der Kalibrierung (Befunde A–D der Gegenlesung, 2026-08-08) ──────────
    // Diese Paare begründen die Default-Mindestscores JE VOREINSTELLUNG
    // (RepertoireSimilarityService.DefaultMinScoreFor); die vollständige Messtabelle steht dort.

    /// <summary>Grünfeld-Inder nach 9.0-0 Sc6 (Zentrum c3/d4/e4 gegen …c5).</summary>
    private const string Gruenfeld = "r1bq1rk1/pp2ppbp/2n3p1/2p5/3PP3/2P2N2/P3BPPP/R1BQ1RK1 w - - 4 10";
    /// <summary>Holländisch Stonewall nach 8.Dc2 Sbd7 (Bauern c6/d5/e6/f5).</summary>
    private const string Stonewall = "r1bq1rk1/pp1n2pp/2pbpn2/3p1p2/2PP4/2N2NP1/PPQ1PPBP/R1B2RK1 w - - 4 9";
    /// <summary>Königsindischer Angriff nach 8...b5 (Fianchetto + d3/e4 gegen d5/e6/c5).</summary>
    private const string KIA = "r1bq1rk1/p3bppp/2n1pn2/1ppp4/4P3/3P1NP1/PPPN1PBP/R1BQR1K1 w - b6 0 9";
    /// <summary>Slawisch (abgetauscht) nach 8...0-0 — mit Karlsbad verwandte Struktur (c6/e6 gegen d4).</summary>
    private const string Slav = "rn1q1rk1/pp3ppp/2p1pn2/5b2/PbBP4/2N1PN2/1P3PPP/R1BQ1RK1 w - - 3 9";
    /// <summary>Italienisch (Giuoco Pianissimo) nach 7...a6.</summary>
    private const string Italian = "r1bq1rk1/1pp2ppp/p1np1n2/2b1p3/2B1P3/2PP1N2/PP3PPP/RNBQR1K1 w - - 0 8";
    /// <summary>Französisch Winawer nach 8...0-0 (geschlossene Kette e5/d4 gegen d5/e6).</summary>
    private const string FrenchWinawer = "rnbq1rk1/pp2nppp/4p3/2ppP3/3P2Q1/P1P5/2P2PPP/R1B1KBNR w KQ - 3 8";
    /// <summary>Katalanisch (offen) nach 9...Lb7: 1.d4 Sf6 2.c4 e6 3.g3 d5 4.Lg2 Le7 5.Sf3 0-0 6.0-0
    /// dxc4 7.Dc2 a6 8.Dxc4 b5 9.Dc2 Lb7 — dasselbe Aufbauschema wie Stonewall/KIA (d4, g3, Lg2,
    /// Sf3, Dc2, kurz rochiert) bei ganz anderem Bauernbild. Von der Gegenlesung als Fehltreffer
    /// der Voreinstellung „stellungsbild" benannt.</summary>
    private const string Catalan = "rn1q1rk1/1bp1bppp/p3pn2/1p6/3P4/5NP1/PPQ1PPBP/RNB2RK1 w - - 2 10";
    /// <summary>Sizilianisch Drachen nach 8...Sc6: 1.e4 c5 2.Sf3 d6 3.d4 cxd4 4.Sxd4 Sf6 5.Sc3 g6
    /// 6.Le2 Lg7 7.0-0 0-0 8.Sb3 Sc6 — dieselbe Familie wie <see cref="Najdorf"/> (identische weiße
    /// Aufstellung, gemeinsames Sizilianisch-Gerüst), anderer schwarzer Aufbau. Von der Gegenlesung
    /// als NIEDRIGSTES verwandtes Paar in „stellungsbild" benannt.</summary>
    private const string Dragon = "r1bq1rk1/pp2ppbp/2np1np1/8/4P3/1NN5/PPP1BPPP/R1BQ1RK1 w - - 4 9";
    /// <summary>Karlsbad aus dem Damengambit, Zentrum aufgelöst (…c5, dxc5, Sxc5) — dieselbe Partie
    /// wenige Züge später.</summary>
    private const string Carlsbad_Liquidated = "r1bqr1k1/pp2bppp/5n2/2np4/7B/2NBPN2/PPQ2PPP/R3K2R w KQ - 0 12";

    /// <summary>Befund A: DIESELBE Materialsumme, gegensätzliche Verteilung — hier hat Weiß die Dame,
    /// dort Schwarz. Über die Summe (Δ = 0) rutschte das Paar durch die Schranke und kam auf 92,3;
    /// je Farbe ist Δ = 9 Bauerneinheiten und es wird gar nicht erst verglichen.</summary>
    [Fact]
    public void MaterialGate_SameTotalButOppositeQueens_IsRejectedPerColour()
    {
        var a = PositionSimilarity.Extract(WhiteHasTheQueen);
        var b = PositionSimilarity.Extract(BlackHasTheQueen);

        Assert.Equal(a.Material, b.Material);                        // Summe identisch — die alte Schranke sah nichts
        Assert.Equal(9, Math.Abs(a.MaterialOf(0) - b.MaterialOf(0)));
        Assert.Equal(9, Math.Abs(a.MaterialOf(1) - b.MaterialOf(1)));
        Assert.False(PositionSimilarity.PassesMaterialGate(a, b));
        // Ohne Schranke wäre das Paar weiterhin verführerisch hoch — genau deshalb muss sie greifen.
        Assert.True(PositionSimilarity.Compare(a, b, SimilarityWeights.Ausgewogen).Score > 80);
    }

    /// <summary>Weiß hat die Dame, Schwarz nicht — Materialungleichgewicht von 9 Bauerneinheiten.</summary>
    private const string WhiteHasTheQueen = "r3k2r/pp3ppp/2n1pn2/3p4/3P4/2N1PN2/PP3PPP/R2QK2R w KQkq - 0 12";
    /// <summary>Exakt das Spiegelbild davon (Farbtausch an der Mittellinie): jetzt hat Schwarz die Dame.</summary>
    private const string BlackHasTheQueen = "r2qk2r/pp3ppp/2n1pn2/3p4/3P4/2N1PN2/PP3PPP/R3K2R w KQkq - 0 12";

    /// <summary>
    /// Die Kehrseite von Befund A: die farbweise Schranke gilt JE VERGLEICH. Der gespiegelte
    /// Vergleich läuft gegen die GESPIEGELTE Anfrage — also muss auch die Schranke gegen sie laufen.
    /// Prüft man sie (wie zuvor) gegen die ungespiegelte, fällt jede Stellung mit
    /// Materialungleichgewicht aus der Spiegelsuche: hier findet die Stellung nicht einmal ihr
    /// eigenes, perfektes Spiegelbild.
    /// </summary>
    [Fact]
    public void MaterialGate_MirroredComparison_UsesTheMirroredQuery()
    {
        var query = PositionSimilarity.Extract(WhiteHasTheQueen);
        var candidate = PositionSimilarity.Extract(BlackHasTheQueen);
        var mirroredQuery = PositionSimilarity.Mirror(query);

        // Der Kandidat IST das Spiegelbild der Anfrage.
        Assert.Equal(candidate.Placement, mirroredQuery.Placement);
        // Gegen die ungespiegelte Anfrage geprüft (der alte Fehler) fällt er raus …
        Assert.False(PositionSimilarity.PassesMaterialGate(query, candidate));
        // … gegen die gespiegelte, gegen die er auch verglichen wird, passt er exakt.
        Assert.True(PositionSimilarity.PassesMaterialGate(mirroredQuery, candidate));
        Assert.Equal(100, PositionSimilarity.Compare(mirroredQuery, candidate, SimilarityWeights.Ausgewogen).Score, 3);
    }

    /// <summary>Ein Referenzpaar der Kalibrierung. <paramref name="Related"/> = „ein Spieler würde
    /// sagen: dieselbe Stellungsfamilie" (gleiches Bauerngerüst, ggf. aus einer anderen Eröffnung,
    /// oder dieselbe Partie wenige Züge später).</summary>
    private sealed record RefPair(string Label, string A, string B, bool Related);

    /// <summary>Die Referenzpaare, aus denen die Default-Schwellen abgeleitet sind. Sie werden für
    /// ALLE drei Voreinstellungen mit denselben Etiketten gemessen — die Schwelle muss je
    /// Voreinstellung passen, nicht die Zuordnung.</summary>
    private static readonly RefPair[] ReferencePairs =
    {
        new("Karlsbad ~ Königsindisch Mar del Plata", Carlsbad_QGD, KingsIndian, false),
        new("Grünfeld ~ Stonewall", Gruenfeld, Stonewall, false),
        new("Grünfeld ~ Königsindischer Angriff", Gruenfeld, KIA, false),
        new("Mar del Plata ~ Grünfeld", KingsIndian, Gruenfeld, false),
        new("Najdorf ~ Karlsbad", Najdorf, Carlsbad_QGD, false),
        new("Italienisch ~ Stonewall", Italian, Stonewall, false),
        new("Französisch Winawer ~ Königsindischer Angriff", FrenchWinawer, KIA, false),
        new("Französisch Winawer ~ Karlsbad", FrenchWinawer, Carlsbad_QGD, false),
        new("Slawisch ~ Königsindisch Mar del Plata", Slav, KingsIndian, false),
        new("Stonewall ~ Königsindischer Angriff", Stonewall, KIA, false),
        new("Stonewall ~ Katalanisch", Stonewall, Catalan, false),
        new("Italienisch ~ Königsindischer Angriff", Italian, KIA, false),
        new("Najdorf ~ Stonewall", Najdorf, Stonewall, false),
        new("Katalanisch ~ Königsindisch Mar del Plata", Catalan, KingsIndian, false),
        new("Katalanisch ~ Slawisch", Catalan, Slav, false),
        new("Karlsbad aus Damengambit ~ aus Nimzoindisch", Carlsbad_QGD, Carlsbad_Nimzo, true),
        new("Karlsbad ~ dieselbe Partie nach Auflösung des Zentrums", Carlsbad_QGD, Carlsbad_Liquidated, true),
        new("Karlsbad aus Nimzoindisch ~ nach Auflösung des Zentrums", Carlsbad_Nimzo, Carlsbad_Liquidated, true),
        new("Slawisch ~ Karlsbad (gemeinsames c6/e6-Gerüst)", Slav, Carlsbad_QGD, true),
        new("Najdorf ~ Drache", Najdorf, Dragon, true),
    };

    /// <summary>
    /// Die zwei DOKUMENTIERTEN Fehlklassifikationen der Voreinstellung „stellungsbild" (Schlüssel
    /// „Voreinstellung|Label"). Dort überlappen die Gruppen, es KANN also keine trennende Schwelle
    /// geben — ein verwandtes Paar liegt sogar exakt auf einem unverwandten (beide 80,5).
    /// Ursache ist die Gewichtung selbst: bei 45 % Figurenplatzierung schlägt „anderes Bauernbild,
    /// gleiches Aufbauschema" das „gleiche Bauerngerüst, andere Figuren" — genau danach ist
    /// „stellungsbild" gefragt. Die Liste ist bewusst explizit: taucht ein WEITERES Paar auf der
    /// falschen Seite auf, fällt der Test.
    /// </summary>
    private static readonly HashSet<string> DocumentedMisses = new()
    {
        "stellungsbild|Stonewall ~ Katalanisch",                       // unverwandt, kommt mit 80,5 durch
        "stellungsbild|Slawisch ~ Karlsbad (gemeinsames c6/e6-Gerüst)", // verwandt, fällt mit 74,8 raus
    };

    /// <summary>Befund C der Gegenlesung: die Schwelle 70 trug in „stellungsbild" nicht (dort lagen
    /// unverwandte Paare bei 71…81, das niedrigste verwandte bei 74,8). Sie gehört JE VOREINSTELLUNG
    /// gesetzt — dieser Test misst dieselben Referenzpaare in allen dreien gegen die dortige
    /// Default-Schwelle. Mit einer globalen 70 fällt er in „stellungsbild".</summary>
    [Theory]
    [InlineData("struktur")]
    [InlineData("ausgewogen")]
    [InlineData("stellungsbild")]
    public void DefaultThresholdPerPreset_SeparatesTheReferencePairs(string preset)
    {
        var weights = SimilarityWeights.FromPreset(preset);
        int threshold = RepertoireSimilarityService.DefaultMinScoreFor(preset);

        foreach (var pair in ReferencePairs)
        {
            var fa = PositionSimilarity.Extract(pair.A);
            var fb = PositionSimilarity.Extract(pair.B);
            if (!PositionSimilarity.PassesMaterialGate(fa, fb))
            {
                Assert.False(pair.Related, $"{pair.Label}: verwandtes Paar darf nicht an der Materialschranke scheitern");
                continue;                                  // unverwandt + gar kein Vergleich = erst recht in Ordnung
            }
            var r = PositionSimilarity.Compare(fa, fb, weights);
            bool expectedAbove = DocumentedMisses.Contains(preset + "|" + pair.Label) ? !pair.Related : pair.Related;
            Assert.True(r.Score >= threshold == expectedAbove,
                $"{preset} (Schwelle {threshold}): {(pair.Related ? "V" : "U")} {pair.Label} = {r.Score:F1} " +
                $"(B {r.Pawns:F1} / M {r.Material:F1} / F {r.Pieces?.ToString("F1") ?? "n/a"} / K {r.King?.ToString("F1") ?? "n/a"})");
        }
    }

    /// <summary>Die tragende Behauptung hinter den Schwellen 67 und 75: in „struktur" und
    /// „ausgewogen" gibt es eine ECHTE Lücke zwischen dem höchsten unverwandten und dem niedrigsten
    /// verwandten Paar, und die Schwelle liegt darin. (In „stellungsbild" gibt es sie nicht — siehe
    /// <see cref="DocumentedMisses"/>; deshalb steht diese Voreinstellung hier nicht.)</summary>
    [Theory]
    [InlineData("struktur", 61.4, 72.9)]
    [InlineData("ausgewogen", 72.8, 77.4)]
    public void DefaultThreshold_LiesInTheMeasuredGap(string preset, double highestUnrelated, double lowestRelated)
    {
        var weights = SimilarityWeights.FromPreset(preset);
        double measuredUnrelated = 0, measuredRelated = 100;
        foreach (var pair in ReferencePairs)
        {
            var fa = PositionSimilarity.Extract(pair.A);
            var fb = PositionSimilarity.Extract(pair.B);
            if (!PositionSimilarity.PassesMaterialGate(fa, fb)) continue;
            var score = PositionSimilarity.Compare(fa, fb, weights).Score;
            if (pair.Related) measuredRelated = Math.Min(measuredRelated, score);
            else measuredUnrelated = Math.Max(measuredUnrelated, score);
        }

        // Die dokumentierten Messwerte müssen reproduzierbar bleiben (±0,1) — sonst stimmt die
        // Herleitung im Doc-Kommentar nicht mehr mit der Metrik überein.
        Assert.Equal(highestUnrelated, measuredUnrelated, 1);
        Assert.Equal(lowestRelated, measuredRelated, 1);

        int threshold = RepertoireSimilarityService.DefaultMinScoreFor(preset);
        Assert.True(measuredUnrelated < threshold && threshold <= measuredRelated,
            $"{preset}: Schwelle {threshold} liegt nicht in der Lücke {measuredUnrelated:F1}…{measuredRelated:F1}");
    }

    /// <summary>Die Schwellen sind je Voreinstellung verschieden — eine globale Zahl kann es nicht
    /// geben, weil die drei Gewichtungen verschiedene Wertebereiche erzeugen.</summary>
    [Fact]
    public void DefaultMinScore_DiffersPerPreset()
    {
        Assert.Equal(67, RepertoireSimilarityService.DefaultMinScoreFor("struktur"));
        Assert.Equal(75, RepertoireSimilarityService.DefaultMinScoreFor("ausgewogen"));
        Assert.Equal(79, RepertoireSimilarityService.DefaultMinScoreFor("stellungsbild"));
        Assert.Equal(75, RepertoireSimilarityService.DefaultMinScoreFor(null));       // unbekannt → ausgewogen
        Assert.Equal(75, RepertoireSimilarityService.DefaultMinScoreFor("quatsch"));
    }

    /// <summary>Befund D: königslose Chessable-Diagramme bekamen die Königskomponente mit 100
    /// geschenkt (bis zu 10 Gratispunkte für genau die Stellungen, über die am wenigsten bekannt
    /// ist). Jetzt ist sie nicht anwendbar und ihr Gewicht liegt auf den übrigen drei.</summary>
    [Fact]
    public void KinglessDiagrams_DoNotGetTheKingComponentForFree()
    {
        // Zwei königslose Diagramme, Turm identisch, Bauern komplett verschoben (Distanz 1,0).
        var a = "8/5p2/6p1/7p/7P/6P1/5P2/R7 w - - 0 1";
        var b = "8/4p3/5p2/6p1/6P1/5P2/4P3/R7 w - - 0 1";

        var r = Cmp(a, b);

        Assert.Null(r.King);
        Assert.Equal(0, r.Pawns, 3);
        Assert.Equal(100, r.Pieces!.Value, 3);
        // Gewichte ohne König: 0,50/0,20/0,20 → Summe 0,90. Früher: 0,5·1 + 0,1·0 ⇒ 50 Punkte,
        // von denen 10 allein daher kamen, dass gar kein König auf dem Brett stand.
        Assert.Equal(100.0 * (1 - 0.5 / 0.9), r.Score, 3);
    }
}
