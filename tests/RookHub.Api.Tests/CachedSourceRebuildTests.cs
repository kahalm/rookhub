using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// <see cref="CachedSourceRebuild"/>: erneuert den ZUGTEXT gespeicherter Chessable-Linien aus dem geteilten
/// piratechess-Linien-Cache, ohne die Header anzufassen. Die Header tragen die Identität der Linie im Kurs
/// (<c>Round</c> → LineId → Fortschritt); die Cache-Antwort zählt dagegen ab <c>001.001</c> und heißt
/// <c>[Event "x"]</c>, weil sie aus einem Fake-Kapitel stammt.
/// </summary>
public class CachedSourceRebuildTests
{
    private const string Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";
    private const string OtherFen = "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR b KQkq - 0 1";

    // piratechess trennt Header und Züge seit dem oid-Header durch eine Zeile aus 24 Leerzeichen (Einrückung
    // seines Raw-String-Templates) — genau so steht es in den gespeicherten Kursen.
    private static readonly string Pad = new(' ', 24);

    /// <summary>Gespeicherter Block im piratechess-Format (≥ v1.0.39): Kapitel-Header, oid, Leerzeichen-Zeile.</summary>
    private static string StoredBlock(string round, string white, string? oid, string moves,
        string fen = Fen, string extraHeaders = "") =>
        $"\n[Event \"Chapter 1\"]\n[Round \"{round}\"]\n[White \"{white}\"]\n[Black \"Kapitel\"]\n[FEN \"{fen}\"]\n[Result \"*\"]\n"
        + (oid != null ? $"[ChessableOid \"{oid}\"]\n" : "") + extraHeaders + Pad + "\n" + moves + "\n\n\n";

    /// <summary>Antwort des Linien-Caches je oid (<c>GetCachedLinePgnsAsync</c>): Fake-Kapitel „x", Zählung ab
    /// 001.001, getrimmt; der Farb-Header (piratechess ≥ v1.0.46) steht wie im Original eingerückt.</summary>
    private static string CacheBlock(string oid, string moves, string? color = null, string fen = Fen) =>
        ($"[Event \"x\"]\n[Round \"001.001\"]\n[White \"x\"]\n[Black \"x\"]\n[FEN \"{fen}\"]\n[Result \"*\"]\n[ChessableOid \"{oid}\"]\n"
         + (color != null ? $"{Pad}[ChessableColor \"{color}\"]\n" : "") + Pad + "\n" + moves).Trim();

    private static Dictionary<string, string> Fresh(params (string Oid, string Pgn)[] lines) =>
        lines.ToDictionary(l => l.Oid, l => l.Pgn, StringComparer.Ordinal);

    [Fact]
    public void Rebuild_ErsetztDenZugtextUeberDieOid_HeaderBleibenZeichengenau()
    {
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old A.} e5 2. Nf3 *")
                   + StoredBlock("002.003", "Line B", "102", "1. e4 e5 {Old B.}\n2. Nc3 *");
        var fresh = Fresh(
            ("101", CacheBlock("101", "1. e4 {New A.} e5 2. Nf3 *")),
            ("102", CacheBlock("102", "1. e4 e5 {New B.} 2. Nc3 *")));

        var r = CachedSourceRebuild.Rebuild(stored, fresh);

        // Golden: alles außer dem Zugtext Zeichen für Zeichen wie vorher — Event, Round (→ LineId), White,
        // Black, FEN, oid, die Leerzeichen-Zeile und die Leerzeilen zwischen den Blöcken.
        var expected = StoredBlock("002.002", "Line A", "101", "1. e4 {New A.} e5 2. Nf3 *")
                     + StoredBlock("002.003", "Line B", "102", "1. e4 e5 {New B.} 2. Nc3 *");
        Assert.Equal(expected, r.Pgn);
        Assert.Equal(2, r.Total);
        Assert.Equal(2, r.Replaced);
        Assert.Equal(0, r.Missing);
        Assert.Equal(0, r.ModeMismatch);
        Assert.Equal(0, r.Conflicts);
    }

    [Fact]
    public void Rebuild_OidNichtImCache_BlockBleibt_ZaehltAlsFehlend()
    {
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old A.} e5 *")
                   + StoredBlock("002.003", "Line B", "102", "1. d4 {Old B.} d5 *");

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1. e4 {New A.} e5 *"))));

        Assert.Equal(StoredBlock("002.002", "Line A", "101", "1. e4 {New A.} e5 *")
                   + StoredBlock("002.003", "Line B", "102", "1. d4 {Old B.} d5 *"), r.Pgn);
        Assert.Equal(1, r.Replaced);
        Assert.Equal(1, r.Missing);
        Assert.Equal(2, r.Total);
    }

    [Fact]
    public void Rebuild_NichtsAusDemCache_GibtDenTextUnveraendertZurueck()
    {
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old A.} e5 *");

        var r = CachedSourceRebuild.Rebuild(stored, Fresh());

        Assert.Same(stored, r.Pgn);   // dieselbe Instanz: kein Kopieren, und der Import sieht „unverändert"
        Assert.Equal(0, r.Replaced);
        Assert.Equal(1, r.Missing);
    }

    [Fact]
    public void Rebuild_ErgaenztDenFarbHeaderAusDemCache_VorhandeneHeaderGewinnen()
    {
        var ohneFarbe = StoredBlock("002.002", "Line A", "101", "1. e4 {Old.} e5 *");
        var mitFarbe = StoredBlock("002.003", "Line B", "102", "1. d4 {Old.} d5 *",
            extraHeaders: "[ChessableColor \"black\"]\n");
        var fresh = Fresh(
            ("101", CacheBlock("101", "1. e4 {New.} e5 *", color: "white")),
            ("102", CacheBlock("102", "1. d4 {New.} d5 *", color: "white")));

        var r = CachedSourceRebuild.Rebuild(ohneFarbe + mitFarbe, fresh);

        // Fehlte der Header, kommt er (ohne die Einrückung aus dem piratechess-Template) hinter den letzten
        // Header; war er da, bleibt der gespeicherte Wert — auch wenn der Cache etwas anderes sagt.
        Assert.Equal(
            StoredBlock("002.002", "Line A", "101", "1. e4 {New.} e5 *", extraHeaders: "[ChessableColor \"white\"]\n")
            + StoredBlock("002.003", "Line B", "102", "1. d4 {New.} d5 *", extraHeaders: "[ChessableColor \"black\"]\n"),
            r.Pgn);
        Assert.Equal(2, r.Replaced);
    }

    [Fact]
    public void Rebuild_UebernimmtKeineHeaderDesFakeKapitels()
    {
        // Ein gespeicherter Block ohne Round/Black (Altbestand, von Hand gebaut): die Werte der Cache-Antwort
        // („001.001", „x") beschreiben das Fake-Kapitel der Abfrage, nicht die Linie — eine übernommene Round
        // verschöbe die LineId. Echte Linien-Header (hier [Result]) kommen dagegen dazu.
        var stored = $"[Event \"Intro\"]\n[White \"Line C\"]\n[FEN \"{Fen}\"]\n[ChessableOid \"103\"]\n\n1. c4 {{Old.}} *\n";

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("103", CacheBlock("103", "1. c4 {New.} *"))));

        Assert.Equal($"[Event \"Intro\"]\n[White \"Line C\"]\n[FEN \"{Fen}\"]\n[ChessableOid \"103\"]\n[Result \"*\"]\n\n1. c4 {{New.}} *\n", r.Pgn);
        Assert.DoesNotContain("[Round", r.Pgn);
        Assert.DoesNotContain("[Black", r.Pgn);
        Assert.DoesNotContain("[Event \"x\"]", r.Pgn);
    }

    [Theory]
    [InlineData("1. e4 {[%tqu \"En\",\"find the move\"] Old.} e5 *", "1. e4 {New.} e5 *")]
    [InlineData("1. e4 {Old.} e5 *", "1. e4 {[%tqu \"En\",\"find the move\"] New.} e5 *")]
    public void Rebuild_TrainingsmarkerNurAufEinerSeite_BlockBleibt_ZaehltAlsModusKonflikt(string alt, string neu)
    {
        // Der Marker legt den Trainingsstart fest. Steht er nur auf einer Seite, passt der Modus der Abfrage
        // nicht zu dieser Linie — lieber den Bestand lassen, als den Trainingsstart still zu verschieben.
        var stored = StoredBlock("002.002", "Line A", "101", alt);

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", neu))));

        Assert.Equal(stored, r.Pgn);
        Assert.Equal(1, r.ModeMismatch);
        Assert.Equal(0, r.Replaced);
    }

    [Fact]
    public void Rebuild_MarkerAufBeidenSeiten_WirdErsetzt()
    {
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {[%tqu \"En\",\"a\"] Old.} e5 *");
        var neu = "1. e4 e5 {[%tqu \"En\",\"b\"] New.} *";

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", neu))));

        Assert.Equal(StoredBlock("002.002", "Line A", "101", neu), r.Pgn);
        Assert.Equal(1, r.Replaced);
    }

    [Fact]
    public void Rebuild_PartienOhneOid_BleibenUnveraendert()
    {
        // Kapitel-Einleitungen/Info-Seiten tragen keine oid — für sie gibt es nichts im Cache. Auch Text vor
        // dem ersten [Event bleibt stehen.
        var intro = StoredBlock("002.001", "Introduction", null, "{Welcome to the chapter.} *");
        var line = StoredBlock("002.002", "Line A", "101", "1. e4 {Old.} e5 *");
        var stored = "% Kommentar vor der ersten Partie\n" + intro + line;

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1. e4 {New.} e5 *"))));

        Assert.Equal("% Kommentar vor der ersten Partie\n" + intro
                     + StoredBlock("002.002", "Line A", "101", "1. e4 {New.} e5 *"), r.Pgn);
        Assert.Equal(1, r.Total);   // gezählt werden nur Linien mit oid
        Assert.Equal(1, r.Replaced);
    }

    [Fact]
    public void Rebuild_GemeldeterFall_MehrdeutigerSpringerzugInDerVariante_WirdKommentar()
    {
        // Meldung 2026-09-23, „Lifetime Repertoires: King's Indian Defense - Part 2": beide Springer (c3, c5)
        // können nach e4, der alte Text führte das als Variante, die kein PGN-Leser spielen kann. piratechess
        // v1.0.47 schreibt die Seitenlinie als Kommentar — genau das soll der gespeicherte Kurs bekommen.
        var alt = "16. Nd2 (16.Ne4 {is a better try, but after} 16...Nxe4 17.Nxe4 {Black is fine.}) 16... Rb8 *";
        var neu = "16. Nd2 {16.Ne4 is a better try, but after 16...Nxe4 17.Nxe4 Black is fine.} 16... Rb8 *";
        var stored = StoredBlock("003.017", "Main line 16.Nd2", "4711", alt);

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("4711", CacheBlock("4711", neu, color: "black"))));

        Assert.Equal(1, r.Replaced);
        Assert.Contains("{16.Ne4 is a better try", r.Pgn);
        Assert.DoesNotContain("(16.Ne4", r.Pgn);
        Assert.Contains("[Round \"003.017\"]", r.Pgn);
        Assert.Contains("[White \"Main line 16.Nd2\"]", r.Pgn);
        Assert.Contains("[ChessableColor \"black\"]", r.Pgn);
    }

    [Fact]
    public void Rebuild_AndereStartstellung_BlockBleibt_ZaehltAlsKonflikt()
    {
        // Gleiche oid, aber eine andere Stellung: der neue Zugtext passte nicht zur behaltenen FEN.
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old.} e5 *");

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1... d5 {New.} *", fen: OtherFen))));

        Assert.Equal(stored, r.Pgn);
        Assert.Equal(1, r.Conflicts);
        Assert.Equal(0, r.Replaced);
    }

    [Fact]
    public void Rebuild_FenMitAnderenZugzaehlern_GiltAlsDieselbeStellung()
    {
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old.} e5 *");
        var sameBoard = Fen.Replace(" 0 2", " 0 7");

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1. e4 {New.} e5 *", fen: sameBoard))));

        Assert.Equal(1, r.Replaced);
        Assert.Contains($"[FEN \"{Fen}\"]", r.Pgn);   // die gespeicherte FEN bleibt
    }

    [Fact]
    public void Rebuild_OidAnMehrerenPartien_BleibenAlle_ZaehlenAlsKonflikt()
    {
        // Altlast des positionsbasierten Parsers (bis RookHub 0.476): eine oid an verschiedenen Linien.
        // Welche die richtige ist, entscheidet hier niemand — beide behielten sonst denselben Zugtext.
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {A.} e5 *")
                   + StoredBlock("002.003", "Line B", "101", "1. d4 {B.} d5 *");

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1. e4 {New.} e5 *"))));

        Assert.Equal(stored, r.Pgn);
        Assert.Equal(2, r.Conflicts);
        Assert.Equal(2, r.Total);
    }

    [Fact]
    public void Rebuild_EventImKommentar_SchneidetKeinePartieEntzwei()
    {
        // Ein „[Event " mitten im Zugtext ist Kommentarinhalt, kein Partie-Anfang. Würde dort geschnitten,
        // ersetzte der Lauf nur die erste Hälfte des alten Zugtexts und hängte die zweite wieder an.
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {compare [Event \"Other\"] here} e5 *");

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1. e4 {New.} e5 *"))));

        Assert.Equal(StoredBlock("002.002", "Line A", "101", "1. e4 {New.} e5 *"), r.Pgn);
    }

    [Fact]
    public void Rebuild_IstIdempotent()
    {
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old.} e5 *")
                   + StoredBlock("002.003", "Line B", "102", "1. d4 {Old.} d5 *");
        var fresh = Fresh(("101", CacheBlock("101", "1. e4 {New.} e5 *", color: "white")),
                          ("102", CacheBlock("102", "1. d4 {New.} d5 *")));

        var once = CachedSourceRebuild.Rebuild(stored, fresh).Pgn;
        var twice = CachedSourceRebuild.Rebuild(once, fresh).Pgn;

        Assert.Equal(once, twice);
    }

    // ── Repertoire-Altlasten (RepertoirePgnCleanup): kommen in Kursen nie vor, in Repertoire-Dateien schon ──

    private const string Hidden = "[RookHubHidden \"Kopie von Partie 1\"]\n";

    [Fact]
    public void Rebuild_AusgeblendeterBlock_BleibtUnveraendert_ZaehltAlsAusgeblendet()
    {
        // Eine ausgeblendete Partie sieht niemand; ein frischer Zugtext änderte daran nichts, und die
        // Ausblend-Begründung bezieht sich auf den ALTEN Text. Also nie ersetzen — auch wenn die oid im Cache liegt.
        var hidden = StoredBlock("002.003", "Line B", "102", "1. d4 {Old B.} d5 *", extraHeaders: Hidden);
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old A.} e5 *") + hidden;
        var fresh = Fresh(
            ("101", CacheBlock("101", "1. e4 {New A.} e5 *")),
            ("102", CacheBlock("102", "1. d4 {New B.} d5 *")));

        var r = CachedSourceRebuild.Rebuild(stored, fresh);

        Assert.Equal(StoredBlock("002.002", "Line A", "101", "1. e4 {New A.} e5 *") + hidden, r.Pgn);
        Assert.Equal(2, r.Total);
        Assert.Equal(1, r.Replaced);
        Assert.Equal(1, r.Hidden);
        // „Jede Partie mit oid landet in genau einem Zähler" gilt weiter.
        Assert.Equal(r.Total, r.Replaced + r.Missing + r.ModeMismatch + r.Conflicts + r.Hidden);
    }

    [Fact]
    public void Rebuild_AusgeblendeteKopieMitDerselbenOid_MachtDieSichtbareNichtZumKonflikt()
    {
        // Wie AmbiguousOids in der Bereinigung: eine oid an einer ausgeblendeten Kopie ist keine zweite Linie.
        // Zählte sie mit, bliebe die sichtbare Partie als „Konflikt" für immer auf dem alten Text.
        var hidden = StoredBlock("002.003", "Line A", "101", "1. e4 {Copy.} e5 *", extraHeaders: Hidden);
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old.} e5 *") + hidden;

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1. e4 {New.} e5 *"))));

        Assert.Equal(StoredBlock("002.002", "Line A", "101", "1. e4 {New.} e5 *") + hidden, r.Pgn);
        Assert.Equal(1, r.Replaced);
        Assert.Equal(1, r.Hidden);
        Assert.Equal(0, r.Conflicts);
    }

    [Fact]
    public void Rebuild_BlockMitEntfernterOid_BleibtUnveraendert_UndBekommtSieNichtZurueck()
    {
        // Die Bereinigung hat dieser Partie die oid genommen ([RookHubRemovedOid]) — sichtbar mit eigenem Inhalt
        // oder als ausgeblendete Kopie. Ohne [ChessableOid] bekommt sie keinen frischen Block, also auch keine
        // ergänzten Header: die entfernte oid darf nicht über „fehlende Header aus dem Cache" zurückkommen.
        var removed = StoredBlock("002.003", "Line B", null, "1. d4 {Unique.} d5 *",
            extraHeaders: "[RookHubRemovedOid \"101\"]\n");
        var removedAndHidden = StoredBlock("002.004", "Line A", null, "1. e4 {Old.} e5 *",
            extraHeaders: "[RookHubRemovedOid \"101\"]\n" + Hidden);
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 {Old.} e5 *") + removed + removedAndHidden;

        var r = CachedSourceRebuild.Rebuild(stored, Fresh(("101", CacheBlock("101", "1. e4 {New.} e5 *", color: "white"))));

        Assert.Equal(StoredBlock("002.002", "Line A", "101", "1. e4 {New.} e5 *", extraHeaders: "[ChessableColor \"white\"]\n")
                     + removed + removedAndHidden, r.Pgn);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(r.Pgn, @"\[ChessableOid ""101""\]"));
        Assert.Equal(1, r.Total);   // nur die Partie, die die oid noch trägt
        Assert.Equal(1, r.Replaced);
        Assert.Equal(0, r.Hidden);  // ohne oid nicht gezählt — auch die ausgeblendete
    }

    [Fact]
    public void OidsOf_UeberspringtAusgeblendetePartien()
    {
        // Ihr Zugtext wird nie ersetzt — sie im Cache nachzufragen kostete piratechess je oid ~455 KB Rohdaten.
        var stored = StoredBlock("002.002", "Line A", "101", "1. e4 e5 *")
                   + StoredBlock("002.003", "Line B", "102", "1. d4 d5 *", extraHeaders: Hidden);

        Assert.Equal(new[] { "101" }, CachedSourceRebuild.OidsOf(stored));
    }

    [Fact]
    public void OidsOf_NurAusDenHeadern_JedeEinmal_InReihenfolge()
    {
        var stored = StoredBlock("002.002", "Line A", "102", "1. e4 {siehe [ChessableOid \"999\"]} e5 *")
                   + StoredBlock("002.003", "Intro", null, "{Text.} *")
                   + StoredBlock("002.004", "Line B", "101", "1. d4 d5 *")
                   + StoredBlock("002.005", "Line C", "102", "1. c4 *");

        Assert.Equal(new[] { "102", "101" }, CachedSourceRebuild.OidsOf(stored));
        Assert.Empty(CachedSourceRebuild.OidsOf(null));
    }

    [Theory]
    [InlineData("1. e4 {[%tqu \"En\",\"x\"]} e5 *", "FirstKeyMove")]
    [InlineData("1. e4 {[%TQU \"En\",\"x\"]} e5 *", "FirstKeyMove")]
    [InlineData("1. e4 {[%alt d2d4]} e5 *", "None")]
    [InlineData("", "None")]
    public void ModeFor_FolgtDenTrainingsmarkernDesGespeichertenKurses(string moves, string mode)
    {
        Assert.Equal(mode, CachedSourceRebuild.ModeFor(StoredBlock("002.002", "Line A", "101", moves)));
    }
}
