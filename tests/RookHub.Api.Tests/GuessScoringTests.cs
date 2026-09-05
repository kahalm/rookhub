using RookHub.Api.Models;
using RookHub.Api.Services;
using static RookHub.Api.Services.GuessScoring;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Punktetabelle der Punktepartie (Vorgabe des Betreibers, 2026-09-05) — Stellung für Stellung
/// durchgespielt. Reine Funktion, keine Datenbank: genau deshalb steht sie ganz am Anfang des
/// Features, damit sich die Schwellen an echten Läufen nachjustieren lassen, bevor UI daran hängt.
/// </summary>
public class GuessScoringTests
{
    /// <summary>Kandidatenliste aus (Zug, Centipawns) — Bewertung aus Sicht der Seite am Zug.</summary>
    private static List<Candidate> Cands(params (string Uci, int Cp)[] items) =>
        items.Select(i => new Candidate(i.Uci, Eval.FromCp(i.Cp))).ToList();

    [Fact]
    public void ExakterPartiezug_gibt5Punkte()
    {
        // Partiezug e2e4 (+30), daneben eine fast gleichwertige Alternative → kein „einziger Zug".
        var r = Evaluate(Cands(("e2e4", 30), ("d2d4", 20)), playedUci: "e2e4", gameUci: "e2e4")!.Value;
        Assert.Equal(GuessGrade.GameMove, r.Grade);
        Assert.Equal(5, r.Points);
    }

    [Fact]
    public void ExakterPartiezug_wennAlleAnderenDeutlichSchlechter_gibt8Punkte()
    {
        // „Der einzige Zug": jede Alternative ist mindestens eine Bauerneinheit schlechter.
        var r = Evaluate(Cands(("e2e4", 30), ("d2d4", -80), ("g1f3", -150)), "e2e4", "e2e4")!.Value;
        Assert.Equal(GuessGrade.OnlyMove, r.Grade);
        Assert.Equal(8, r.Points);
    }

    [Fact]
    public void AehnlichGuterAndererZug_gibt2Punkte()
    {
        // ±0.1 Bauerneinheiten gelten als gleich gut.
        var r = Evaluate(Cands(("e2e4", 30), ("d2d4", 22)), playedUci: "d2d4", gameUci: "e2e4")!.Value;
        Assert.Equal(GuessGrade.Similar, r.Grade);
        Assert.Equal(2, r.Points);
    }

    [Fact]
    public void BesserAlsDerPartiezug_gibt8Punkte_eindeutigBesser10()
    {
        // Knapp besser (0.15) → 8 …
        var knapp = Evaluate(Cands(("e2e4", 30), ("d2d4", 45)), "d2d4", "e2e4")!.Value;
        Assert.Equal(GuessGrade.Better, knapp.Grade);
        Assert.Equal(8, knapp.Points);

        // … ab 0.2 eindeutig besser → 10.
        var klar = Evaluate(Cands(("e2e4", 30), ("d2d4", 60)), "d2d4", "e2e4")!.Value;
        Assert.Equal(GuessGrade.ClearlyBetter, klar.Grade);
        Assert.Equal(10, klar.Points);
    }

    [Fact]
    public void DeutlichSchlechter_gibtMinus2_dazwischenNull()
    {
        // Mehr als eine Bauerneinheit schlechter → Abzug.
        var patzer = Evaluate(Cands(("e2e4", 30), ("b1a3", -120)), "b1a3", "e2e4")!.Value;
        Assert.Equal(GuessGrade.MuchWorse, patzer.Grade);
        Assert.Equal(-2, patzer.Points);

        // Die Lücke der Vorgabe: schlechter, aber kein Patzer → 0 Punkte, kein Abzug.
        var mittel = Evaluate(Cands(("e2e4", 30), ("g1f3", -20)), "g1f3", "e2e4")!.Value;
        Assert.Equal(GuessGrade.Worse, mittel.Grade);
        Assert.Equal(0, mittel.Points);
    }

    [Fact]
    public void ZugAusserhalbDerKandidatenliste_wirdHoechstensAlsAehnlichGewertet()
    {
        // Das Protokoll liefert höchstens 5 Linien. Steht der geratene Zug nicht darunter, kennen
        // wir seine Bewertung NICHT — gewertet wird die Obergrenze (schlechtester gelisteter Zug),
        // und „besser als der Partiezug" ist er sicher nicht.
        var r = Evaluate(Cands(("e2e4", 30), ("d2d4", 25), ("g1f3", 20)), "h2h3", "e2e4")!.Value;
        Assert.False(r.PlayedWasListed);
        Assert.True(r.Grade <= GuessGrade.Similar);
    }

    [Fact]
    public void OhnePartiezugInDerListe_istDieStellungNichtWertbar()
    {
        // Kein Bezugspunkt → null (Stellung überspringen statt mit 0 abstrafen).
        Assert.Null(Evaluate(Cands(("d2d4", 25)), "d2d4", "e2e4"));
        Assert.Null(Evaluate(new List<Candidate>(), "e2e4", "e2e4"));
    }

    [Fact]
    public void MattSchlaegtJedeMaterialbewertung_undKuerzeresMattIstBesser()
    {
        // Matt in 3 statt Partiezug mit +9.0 → eindeutig besser.
        var mattGefunden = Evaluate(
            new List<Candidate> { new("h5f7", Eval.FromMate(3)), new("d1d8", Eval.FromCp(900)) },
            playedUci: "h5f7", gameUci: "d1d8")!.Value;
        Assert.Equal(GuessGrade.ClearlyBetter, mattGefunden.Grade);

        // Umgekehrt: Matt verpasst und „nur" +9.0 gespielt → deutlich schlechter.
        var mattVerpasst = Evaluate(
            new List<Candidate> { new("h5f7", Eval.FromMate(3)), new("d1d8", Eval.FromCp(900)) },
            playedUci: "d1d8", gameUci: "h5f7")!.Value;
        Assert.Equal(GuessGrade.MuchWorse, mattVerpasst.Grade);

        // Kürzeres Matt ist besser als längeres.
        var kuerzer = Evaluate(
            new List<Candidate> { new("a1a8", Eval.FromMate(2)), new("b1b8", Eval.FromMate(6)) },
            playedUci: "a1a8", gameUci: "b1b8")!.Value;
        Assert.Equal(GuessGrade.ClearlyBetter, kuerzer.Grade);
    }

    [Fact]
    public void UmwandlungsfigurGehoertZumZug()
    {
        // e7e8q und e7e8n sind verschiedene Züge — sonst gäbe es Punkte für die falsche Figur.
        var r = Evaluate(
            new List<Candidate> { new("e7e8q", Eval.FromMate(1)), new("e7e8n", Eval.FromCp(-300)) },
            playedUci: "e7e8n", gameUci: "e7e8q")!.Value;
        Assert.Equal(GuessGrade.MuchWorse, r.Grade);
    }

    [Fact]
    public void PunkteTabelle_istDieEinzigeQuelleDerZahlen()
    {
        // Wenn diese Zuordnung sich ändert, ändert sie sich genau hier — gespeichert wird die Stufe.
        Assert.Equal(-2, GuessGrades.PointsFor(GuessGrade.MuchWorse));
        Assert.Equal(0, GuessGrades.PointsFor(GuessGrade.Worse));
        Assert.Equal(2, GuessGrades.PointsFor(GuessGrade.Similar));
        Assert.Equal(5, GuessGrades.PointsFor(GuessGrade.GameMove));
        Assert.Equal(8, GuessGrades.PointsFor(GuessGrade.OnlyMove));
        Assert.Equal(8, GuessGrades.PointsFor(GuessGrade.Better));
        Assert.Equal(10, GuessGrades.PointsFor(GuessGrade.ClearlyBetter));
        Assert.Equal(10, GuessGrades.MaxPointsPerMove);
    }
}
