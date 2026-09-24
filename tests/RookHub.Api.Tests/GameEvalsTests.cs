using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Eine Zeile der Partie-Analyse → Bewertung aus WEISS-Sicht, wie die Kurve unter der gespeicherten
/// Partie sie braucht.
///
/// <para>Die Kandidatenlisten stehen aus Sicht der Seite AM ZUG (so braucht sie die Punktepartie).
/// Die Kurve dagegen zeichnet EINE Linie ueber die ganze Partie — bliebe das Vorzeichen, sprang sie
/// nach jedem Halbzug auf die andere Seite der Mittellinie. Deshalb hat jede Umrechnung hier einen
/// Fall mit Schwarz am Zug.</para>
/// </summary>
public class GameEvalsTests
{
    private const string WhiteToMove = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string BlackToMove = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    [Fact]
    public void WeissAmZug_bleibtWieEsIst()
    {
        var ply = GameEvals.PlyOf(0, WhiteToMove, "e2e4",
            """[{"uci":"e2e4","cp":30},{"uci":"d2d4","cp":25}]""", 20);

        Assert.NotNull(ply);
        Assert.Equal(0, ply!.Ply);
        Assert.Equal(30, ply.Cp);
        Assert.Null(ply.Mate);
        Assert.Equal(20, ply.Depth);
        Assert.Equal("e2e4", ply.BestUci);
        Assert.Equal("e2e4", ply.PlayedUci);
        Assert.Equal(30, ply.PlayedCp);
        Assert.Equal(25, ply.SecondCp);
    }

    /// <summary>Schwarz steht nach 1.e4 um 0,20 schlechter — aus SEINER Sicht −20. Aus Weiß-Sicht
    /// ist das +20, und der schwächere gespielte Zug (−35 für Schwarz) wird +35 für Weiß.</summary>
    [Fact]
    public void SchwarzAmZug_drehtJedesVorzeichen()
    {
        var ply = GameEvals.PlyOf(1, BlackToMove, "c7c5",
            """[{"uci":"e7e5","cp":-20},{"uci":"c7c5","cp":-35}]""", 18);

        Assert.NotNull(ply);
        Assert.Equal(20, ply!.Cp);
        Assert.Equal("e7e5", ply.BestUci);
        Assert.Equal("c7c5", ply.PlayedUci);
        Assert.Equal(35, ply.PlayedCp);
        Assert.Equal(35, ply.SecondCp);
    }

    /// <summary>„Eigene Fehler nachspielen" laesst jeden GLEICHWERTIGEN Zug gelten — dafuer braucht der
    /// Client alle Kandidaten, in derselben Reihenfolge und derselben Weiß-Sicht wie die Kurve.</summary>
    [Fact]
    public void Kandidaten_alleInReihenfolge_inWeissSicht()
    {
        var ply = GameEvals.PlyOf(1, BlackToMove, "c7c5",
            """[{"uci":"e7e5","cp":-20},{"uci":"c7c5","cp":-35},{"uci":"d7d5","mate":3}]""", 18);

        Assert.NotNull(ply);
        Assert.Equal(new[] { "e7e5", "c7c5", "d7d5" }, ply!.Candidates.Select(c => c.Uci));
        Assert.Equal(20, ply.Candidates[0].Cp);
        Assert.Equal(35, ply.Candidates[1].Cp);
        Assert.Null(ply.Candidates[2].Cp);
        Assert.Equal(-3, ply.Candidates[2].Mate);
    }

    /// <summary>Matt bleibt Matt — nur das Vorzeichen dreht: Schwarz setzt in 1 matt (aus seiner Sicht
    /// +1) heißt für Weiß „wird in 1 mattgesetzt" (−1). Keine Umrechnung in Centipawns.</summary>
    [Fact]
    public void SchwarzAmZug_MattBleibtMattMitGedrehtemVorzeichen()
    {
        var ply = GameEvals.PlyOf(3, "rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq g3 0 2", "g8f6",
            """[{"uci":"d8h4","mate":1},{"uci":"g8f6","cp":-50}]""", 22);

        Assert.NotNull(ply);
        Assert.Null(ply!.Cp);
        Assert.Equal(-1, ply.Mate);
        Assert.Equal(50, ply.PlayedCp);
        Assert.Null(ply.PlayedMate);
        Assert.Equal(50, ply.SecondCp);
    }

    [Fact]
    public void WeissAmZug_MattFuerWeiss_bleibtPositiv()
    {
        var ply = GameEvals.PlyOf(4, WhiteToMove, "d1h5",
            """[{"uci":"d1h5","mate":2}]""", 30);

        Assert.Equal(2, ply!.Mate);
        Assert.Equal(2, ply.PlayedMate);
        Assert.Null(ply.SecondCp);
        Assert.Null(ply.SecondMate);
    }

    /// <summary>Der gespielte Zug steht nicht unter den fünf besten — dann gibt es KEINE Bewertung
    /// für ihn, statt einer geratenen. Der Client nimmt dafür die nächste Stellung.</summary>
    [Fact]
    public void GespielterZugNichtGelistet_hatKeineBewertung()
    {
        var ply = GameEvals.PlyOf(0, WhiteToMove, "g2g4",
            """[{"uci":"e2e4","cp":30},{"uci":"d2d4","cp":25}]""", 20);

        Assert.Equal(30, ply!.Cp);
        Assert.Null(ply.PlayedCp);
        Assert.Null(ply.PlayedMate);
    }

    /// <summary>Aufgegeben (<c>[]</c>) oder unlesbar: die Zeile fehlt in der Kurve, statt mit 0,00 als
    /// „ausgeglichen" dazustehen.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("kein json")]
    [InlineData(null)]
    public void OhneKandidaten_keineZeile(string? json)
        => Assert.Null(GameEvals.PlyOf(0, WhiteToMove, "e2e4", json, 20));

    /// <summary>Für die Stellung NACH dem letzten Zug gibt es keine Zeile — ihre Bewertung ist die des
    /// gespielten Kandidaten der letzten Zeile, schon in Weiß-Sicht.</summary>
    [Fact]
    public void Final_istDerGespielteKandidatDerLetztenZeile()
    {
        var last = GameEvals.PlyOf(1, BlackToMove, "c7c5",
            """[{"uci":"e7e5","cp":-20},{"uci":"c7c5","cp":-35}]""", 18);

        var final = GameEvals.FinalOf(last, plyCount: 2);

        Assert.NotNull(final);
        Assert.Equal(35, final!.Cp);
        Assert.Null(final.Mate);
    }

    [Fact]
    public void Final_nurAusDerLETZTENZeile_undNurMitGespieltemKandidaten()
    {
        var notLast = GameEvals.PlyOf(0, WhiteToMove, "e2e4", """[{"uci":"e2e4","cp":30}]""", 20);
        Assert.Null(GameEvals.FinalOf(notLast, plyCount: 2));   // die Partie ist länger
        Assert.Null(GameEvals.FinalOf(null, plyCount: 1));      // letzte Zeile nicht gerechnet

        var unlisted = GameEvals.PlyOf(0, WhiteToMove, "g2g4", """[{"uci":"e2e4","cp":30}]""", 20);
        Assert.Null(GameEvals.FinalOf(unlisted, plyCount: 1));
    }

    [Fact]
    public void Final_MattBleibtMatt()
    {
        var last = GameEvals.PlyOf(3, "rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq g3 0 2", "d8h4",
            """[{"uci":"d8h4","mate":1}]""", 22);

        var final = GameEvals.FinalOf(last, plyCount: 4);

        Assert.Null(final!.Cp);
        Assert.Equal(-1, final.Mate);
    }

    // ===== Restdauer =========================================================

    private static readonly DateTime Now = new(2026, 9, 24, 9, 41, 52, DateTimeKind.Utc);

    /// <summary>Die Zahlen der Prod-Partie vom 2026-09-24, die den Wunsch ausgeloest hat: 18 von 47
    /// Stellungen gerechnet, die juengsten zwoelf ueber 4:31 min verteilt → gut 22 s je Stellung,
    /// 29 offen → 11 Minuten.</summary>
    [Fact]
    public void Eta_ausDenJuengstenZwoelf_bisJetzt()
    {
        var at = new[]
        {
            "09:36:01", "09:36:21", "09:36:21", "09:36:21", "09:36:41", "09:37:01",
            "09:37:21", "09:37:21", "09:38:21", "09:38:21", "09:38:41", "09:39:01",
            "09:39:21", "09:40:21", "09:40:42", "09:40:42", "09:40:42", "09:41:22",
        }.Select(t => DateTime.SpecifyKind(DateTime.Parse("2026-09-24T" + t), DateTimeKind.Utc));

        Assert.Equal(11, GameEvals.EtaMinutes(at, remaining: 29, Now));
    }

    [Fact]
    public void Eta_nurAeltereErgebnisseAlsDieJuengstenZwoelf_zaehlenNicht()
    {
        // Zwoelf im Minutentakt, davor eine Stunde Warteschlange — die darf das Tempo nicht verderben.
        var recent = Enumerable.Range(1, 12).Select(i => Now.AddMinutes(-i));
        var old = new[] { Now.AddHours(-1), Now.AddHours(-2) };

        Assert.Equal(10, GameEvals.EtaMinutes(recent.Concat(old), remaining: 10, Now));
    }

    [Fact]
    public void Eta_haengtDieEngine_waechstDieRestdauer()
    {
        var at = new[] { Now.AddMinutes(-2), Now.AddMinutes(-1) };

        var fresh = GameEvals.EtaMinutes(at, remaining: 10, Now);
        var stalled = GameEvals.EtaMinutes(at, remaining: 10, Now.AddMinutes(10));

        Assert.Equal(10, fresh);      // 2 min fuer 2 Stellungen
        Assert.Equal(60, stalled);    // 12 min fuer 2 Stellungen
    }

    [Fact]
    public void Eta_mehrereImSelbenTakt_mindestensEineMinuteSpanne()
    {
        // Die Pumpe holt drei Ergebnisse auf einen Schlag: ohne Untergrenze waeren das 0 s je Stellung.
        var at = new[] { Now, Now, Now };

        Assert.Equal(7, GameEvals.EtaMinutes(at, remaining: 20, Now));   // 60 s / 3 = 20 s → 400 s
    }

    [Fact]
    public void Eta_ohneTempoOderOhneRest_null()
    {
        Assert.Null(GameEvals.EtaMinutes(Array.Empty<DateTime>(), remaining: 20, Now));
        Assert.Null(GameEvals.EtaMinutes(new[] { Now.AddMinutes(-1) }, remaining: 20, Now));
        Assert.Null(GameEvals.EtaMinutes(new[] { Now.AddMinutes(-2), Now.AddMinutes(-1) }, remaining: 0, Now));
    }

    [Fact]
    public void Eta_nieUnterEinerMinute()
    {
        var at = Enumerable.Range(1, 12).Select(i => Now.AddSeconds(-10 * i));

        Assert.Equal(1, GameEvals.EtaMinutes(at, remaining: 1, Now));
    }

    [Fact]
    public void Kandidaten_tragenIhreVariante_inWeissSicht()
    {
        var ply = GameEvals.PlyOf(1, BlackToMove, "c7c5",
            """[{"uci":"e7e5","cp":-25,"pv":["e7e5","g1f3"]},{"uci":"c7c5","cp":-32}]""", 20);

        Assert.Equal(2, ply!.Candidates.Count);
        Assert.Equal(new[] { "e7e5", "g1f3" }, ply.Candidates[0].Pv);
        Assert.Equal(25, ply.Candidates[0].Cp);    // Schwarz am Zug → gedreht
        Assert.Null(ply.Candidates[1].Pv);         // Zeile ohne Variante (Altbestand)
    }
}
