using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Welche Partien als naechstes gerechnet werden. Die Note allein entscheidet das nicht: an der
/// Spitze stehen tausende Partien mit 100, und die Reihenfolge dahinter bestimmt, ob zwanzig
/// verschiedene Partien in der Warteschlange landen oder zwanzig desselben Kommentators.
/// </summary>
public class LibraryPicksTests
{
    private static LibraryGame Game(int id, int score, int commentedPlies, string annotator,
        int commentChars = 1000) => new()
    {
        Id = id, Score = score, CommentedPlies = commentedPlies, CommentChars = commentChars,
        Annotator = annotator, Pgn = "1. e4 e5",
    };

    [Fact]
    public void Best_sortiertNachNoteDannKommentiertenHalbzuegen()
    {
        var picked = LibraryPicks.Best(
        [
            Game(1, 90, 40, "A"), Game(2, 100, 10, "B"), Game(3, 100, 30, "C"),
        ], count: 3);

        // 100/30 vor 100/10 vor 90/40 — die Note schlaegt die Dichte, die Dichte den Gleichstand.
        Assert.Equal([3, 2, 1], picked.Select(g => g.Id).ToList());
    }

    /// <summary>Gleiche Note und gleiche Dichte: dann entscheidet die Textmenge, zuletzt die Zeile.</summary>
    [Fact]
    public void Best_beiGleichstandEntscheidetDieTextmenge()
    {
        var picked = LibraryPicks.Best(
        [
            Game(7, 100, 20, "A", commentChars: 500), Game(8, 100, 20, "B", commentChars: 9000),
        ], count: 2);

        Assert.Equal([8, 7], picked.Select(g => g.Id).ToList());
    }

    /// <summary>Der Kern: sechs Partien desselben Grossmeisters ueber sich selbst sind eine
    /// Auswahl ueber einen Menschen, nicht ueber den Bestand.</summary>
    [Fact]
    public void Best_deckeltProKommentator()
    {
        var picked = LibraryPicks.Best(
        [
            Game(1, 100, 50, "Giri,Anish"), Game(2, 100, 49, "Giri,Anish"),
            Game(3, 100, 48, "Giri,Anish"), Game(4, 100, 20, "Stohl,Igor"),
        ], count: 3, maxPerAnnotator: 2);

        Assert.Equal([1, 2, 4], picked.Select(g => g.Id).ToList());
    }

    /// <summary>Der Deckel ist ein VORZUG, kein Ausschluss: reicht der Rest nicht, wird mit den
    /// zurueckgestellten Partien aufgefuellt — sonst laege weniger in der Warteschlange als
    /// verlangt, ohne dass jemand erfaehrt warum.</summary>
    [Fact]
    public void Best_fuelltMitZurueckgestelltenAuf()
    {
        var picked = LibraryPicks.Best(
        [
            Game(1, 100, 50, "Giri,Anish"), Game(2, 100, 49, "Giri,Anish"),
            Game(3, 100, 48, "Giri,Anish"),
        ], count: 3, maxPerAnnotator: 1);

        Assert.Equal([1, 2, 3], picked.Select(g => g.Id).ToList());
    }

    /// <summary>Ohne Kommentator-Angabe gibt es nichts zu haeufen — solche Zeilen laufen am
    /// Deckel vorbei, statt sich gegenseitig zu verdraengen.</summary>
    [Fact]
    public void Best_ohneKommentatorGiltDerDeckelNicht()
    {
        var picked = LibraryPicks.Best(
        [
            Game(1, 100, 50, ""), Game(2, 100, 49, ""), Game(3, 100, 48, ""),
        ], count: 3, maxPerAnnotator: 1);

        Assert.Equal([1, 2, 3], picked.Select(g => g.Id).ToList());
    }

    [Fact]
    public void Best_ohneAnforderungLiefertNichts()
        => Assert.Empty(LibraryPicks.Best([Game(1, 100, 50, "A")], count: 0));
}
