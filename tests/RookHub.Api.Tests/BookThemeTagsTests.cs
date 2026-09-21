using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Zentrale Buch-Themen-Semantik (ersetzt die zwei gedrifteten Parser-Kopien in
/// CourseService und TrainingGoalService).</summary>
public class BookThemeTagsTests
{
    [Fact]
    public void ParseKeys_DedupesKeepsOrder_FiltersInvalid()
    {
        Assert.Equal(new[] { "endgame", "tactics" },
            BookThemeTags.ParseKeys("endgame, tactics, ENDGAME, unknown"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense, alsobad")]
    public void ParseKeys_EmptyOrInvalid_DefaultsToTactics(string? csv)
    {
        Assert.Equal(new[] { "tactics" }, BookThemeTags.ParseKeys(csv));
    }

    [Fact]
    public void IsValidKey_MatchesWhitelist()
    {
        Assert.True(BookThemeTags.IsValidKey("opening"));
        Assert.False(BookThemeTags.IsValidKey("blitz"));
    }

    // ===== Spiegel-Pin: Buch-Themen ===========================================================
    // Dieselben fuenf Keys stehen ein zweites Mal im Frontend
    // (features/courses/course-themes-dialog.component.ts, ALL_THEMES) — der Dialog baut daraus
    // seine Checkboxen. Laufen die beiden auseinander, bietet der Dialog ein Thema an, das der
    // Server beim Speichern still wegwirft, oder er laesst ein gueltiges gar nicht erst waehlen.
    // Muster wie RepertoireTrainingServiceTests.DefaultLevels_MatchTheFrontendMirror: BEIDE Seiten
    // pruefen LITERALE Werte, nicht die jeweils andere Implementierung.
    // Wer hier etwas aendert, aendert course-themes-dialog.component.spec.ts mit.
    [Fact]
    public void ValidKeys_MatchTheFrontendMirror()
    {
        // Reihenfolge hier = ChessableTheme-Enum; im Dialog ist sie die ANZEIGE-Reihenfolge und
        // darf abweichen — die MENGE darf es nicht.
        Assert.Equal(new[] { "opening", "middlegame", "endgame", "tactics", "other" }, BookThemeTags.ValidKeys);
        Assert.Equal(
            new[] { "endgame", "middlegame", "opening", "other", "tactics" },
            BookThemeTags.ValidKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    // Die Keys SIND die Enum-Namen kleingeschrieben — daran haengt das Zeit-Routing im
    // Trainingsziele-Tracker (ChessableTheme -> Kategorie).
    [Fact]
    public void ValidKeys_AreTheChessableThemeNamesLowercased()
    {
        Assert.Equal(
            Enum.GetNames<Models.ChessableTheme>().Select(n => n.ToLowerInvariant()).ToArray(),
            BookThemeTags.ValidKeys);
    }
}
