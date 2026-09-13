using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Altlasten-Bereinigung in Repertoire-PGNs: nie löschen, nur ausblenden; gewollte Wiederholungen bleiben.</summary>
public class RepertoirePgnCleanupTests
{
    private static string G(string white, string moves, string? oid = null, bool neu = false) =>
        $"[Event \"C\"]\n[Round \"002.002\"]\n[White \"{white}\"]\n[Black \"T\"]\n[Result \"*\"]\n"
        + (oid != null ? $"[ChessableOid \"{oid}\"]\n" : "")
        + (neu ? "                        \n" : "\n")
        + moves + "\n\n";

    private static int Count(string pgn, string needle)
        => System.Text.RegularExpressions.Regex.Matches(pgn, System.Text.RegularExpressions.Regex.Escape(needle)).Count;

    private static readonly IReadOnlyDictionary<string, string> NoTruth = new Dictionary<string, string>();

    // Nachbau von „My Variations" (Prod, 13.09.): drei Originale, dazu vier hinten angehängte Kopien des alten Fehlers.
    private static string MyVariations() =>
        G("2", "1. e4 e5 2. Nf3 *")                                   // 1 Original ohne oid
        + G("ad. French", "1. e4 e6 2. d3 *")                         // 2 Original ohne oid
        + G("Drexler", "1. e4 e6 2. Qe2 *", "900")                    // 3 Original mit eigener oid
        + G("2", "1. e4 e5 2. Nf3 *", "100", neu: true)               // 4 Kopie von 1, richtige oid
        + G("ad. French", "1. e4 e6 2. d3 *", "200", neu: true)       // 5 Kopie von 2, richtige oid
        + G("ad. French", "1. e4 e6 2. Qe2 *", "200", neu: true)      // 6 Drexler-Inhalt, FALSCHE oid 200
        + G("ad. French", "1. e4 e6 2. Qe2 *", "200", neu: true);     // 7 dito

    [Fact]
    public void MyVariations_HidesAllFourCopies_MovesOidsToOriginals_DeletesNothing()
    {
        var pgn = MyVariations();
        var result = RepertoirePgnCleanup.Repair(pgn, NoTruth);

        Assert.Equal(7, Count(result.Pgn, "[Event "));                        // nichts gelöscht
        Assert.Equal(4, Count(result.Pgn, "[RookHubHidden "));
        var sichtbar = RepertoirePgnCleanup.WithoutHidden(result.Pgn);
        Assert.Equal(3, Count(sichtbar, "[Event "));
        Assert.Contains("[White \"2\"]\n[Black \"T\"]\n[Result \"*\"]\n[ChessableOid \"100\"]\n\n1. e4 e5 2. Nf3 *", sichtbar);
        Assert.Contains("[White \"ad. French\"]\n[Black \"T\"]\n[Result \"*\"]\n[ChessableOid \"200\"]\n\n1. e4 e6 2. d3 *", sichtbar);
        Assert.Contains(G("Drexler", "1. e4 e6 2. Qe2 *", "900"), sichtbar);   // unberührt, Zeichen für Zeichen
        Assert.Equal(3, Count(sichtbar, "[ChessableOid "));                  // jede oid genau einmal
        Assert.Equal(2, Count(result.Pgn, "[RookHubRemovedOid \"200\"]"));  // die zwei falsch beschrifteten
    }

    [Fact]
    public void Repair_IsIdempotent()
    {
        var once = RepertoirePgnCleanup.Repair(MyVariations(), NoTruth).Pgn;
        var twice = RepertoirePgnCleanup.Repair(once, NoTruth);
        Assert.Empty(twice.Actions);
        Assert.Same(once, twice.Pgn);
    }

    [Fact]
    public void SameMovesUnderDifferentOids_AreIntendedRepeats_AndStay()
    {
        var pgn = G("Kapitel 3", "1. d4 d5 2. c4 *", "1", neu: true) + G("Quick Starter", "1. d4 d5 2. c4 *", "2", neu: true);
        var result = RepertoirePgnCleanup.Repair(pgn, NoTruth);
        Assert.Empty(result.Actions);
        Assert.Same(pgn, result.Pgn);
    }

    [Fact]
    public void DuplicatesWithoutAnyOid_AreUndecidable_AndStay()
    {
        var pgn = G("A", "1. c4 e5 *") + G("B", "1. c4 e5 *");
        Assert.Empty(RepertoirePgnCleanup.Repair(pgn, NoTruth).Actions);
    }

    [Fact]
    public void WrongOidOnUniqueContent_LosesTheOid_ButStaysVisible_EarliestWins()
    {
        // Nachbau „100 Tactical Patterns": Original + drei später angehängte Unikate unter derselben oid.
        var pgn = G("Introduction to Promotion", "{[%info] Intro} 1. -- *", "47")
            + G("Introduction to Promotion", "1. d4 d5 2. c4 *", "47", neu: true)
            + G("Introduction to Promotion", "1. e4 c5 2. Nf3 *", "47", neu: true);
        var result = RepertoirePgnCleanup.Repair(pgn, NoTruth);

        Assert.Equal(0, Count(result.Pgn, "[RookHubHidden "));
        Assert.Equal(1, Count(result.Pgn, "[ChessableOid \"47\"]"));
        Assert.Equal(2, Count(result.Pgn, "[RookHubRemovedOid \"47\"]"));
        Assert.StartsWith(G("Introduction to Promotion", "{[%info] Intro} 1. -- *", "47"), result.Pgn);
        Assert.All(result.Actions, a => Assert.Equal("oid entfernt", a.Action));
    }

    [Fact]
    public void TruthFromTheLineCache_OverridesTheEarliestFallback()
    {
        var pgn = G("X", "1. d4 d5 2. c4 *", "47") + G("X", "1. e4 c5 2. Nf3 *", "47", neu: true);
        var truth = new Dictionary<string, string> { ["47"] = G("X", "1. e4 c5 2. Nf3 *", "47", neu: true) };
        var result = RepertoirePgnCleanup.Repair(pgn, truth);

        Assert.Contains("[RookHubRemovedOid \"47\"]", result.Pgn[..result.Pgn.IndexOf("[Event ", 1, StringComparison.Ordinal)]);
        Assert.Contains("laut Linien-Cache", Assert.Single(result.Actions).Detail);
    }

    [Fact]
    public void AmbiguousOids_UsesLineIdentity_NotCommentDifferences()
    {
        var pgn = G("A", "1. e4 {alt} e5 *", "5") + G("A", "1. e4 {neuer Kommentar} e5 *", "5", neu: true);
        Assert.Empty(RepertoirePgnCleanup.AmbiguousOids(pgn));
        Assert.Equal(new[] { "5" }, RepertoirePgnCleanup.AmbiguousOids(G("A", "1. e4 e5 *", "5") + G("B", "1. d4 d5 *", "5")));
    }

    [Fact]
    public void WithoutHidden_ReturnsTheSameInstance_WhenNothingIsHidden()
    {
        var pgn = G("A", "1. e4 e5 *", "1");
        Assert.Same(pgn, RepertoirePgnCleanup.WithoutHidden(pgn));
        Assert.Equal(string.Empty, RepertoirePgnCleanup.WithoutHidden(null));
    }
}
