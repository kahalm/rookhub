namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Welche Speicher-Schlüssel der Oberflächen als Anzeige-Vorliebe am Konto mitwandern (<c>UiPrefs</c> im Profil,
/// 1.018.0). Handgespiegelt von <c>SYNCED_UI_PREF_KEYS</c> in <c>src/frontend/app/src/app/core/ui-prefs-sync.ts</c>;
/// beide Seiten halten die Liste mit LITERALEN Werten in einem Test fest (<c>UiPrefKeysTests</c> ↔
/// <c>ui-prefs-sync.spec.ts</c>) — ein neuer Schlüssel muss auf beiden Seiten dazukommen, sonst lehnt der Server
/// das Hochladen mit 400 ab.
///
/// <para>Bewusst NICHT dabei: echte Gerätesachen (Sprache, Design, Größe, E-Ink, Offline-Umfang, Engine-Quelle des
/// Rechners) und alles, was ein Konto-Inhalt ist (Kalkulationsbäume, Kurs-Fortschritt) — der gehört an seine Entität.</para>
/// </summary>
public static class UiPrefKeys
{
    public const int MaxValueLength = 2000;

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        // Puzzle-/Brett-Einstellungen (PreferencesService)
        "rookhub_puzzle_config", "rookhub_visualization", "rookhub_viz_arrow", "rookhub_offpath_warn_moves",
        "rookhub_en_passant_forced",
        // RookHub-Seiten
        "rookhub_dashboard_layout_v2", "rookhub_notifications_hidden_categories", "rookhub_guess_accept",
        "rookhub_guess_comment_lang", "rookhub_explorer_settings", "rookhub_rep_train_show_comments",
        "rookhub_rep_train_review_engine", "rookhub_deviation_threshold", "rookhub_game_arrow", "rookhub_game_badges",
        "rookhub_game_engine_view", "rookhub_game_lines", "rookhub_analysis_compare", "rookhub_analysis_depth",
        "rookhub_analysis_lines", "rookhub_analysis_maia_elo", "rookhub_analysis_maia_evalbar",
        "rookhub_analysis_maia_warn", "rh.ebooks.solveStyle", "rh.ebooks.autoNext", "rh.repertoire.treeArrows",
        // Turnierseite
        "rookhub_live_eval", "rh.turnier.historyView",
        // LeagueHub / Spielerkarte
        "lh-club", "lh-training-lines", "lh-tree-filter", "lh-auto-my-games", "lh-all-leagues", "lh-overview-newest-first",
    };
}
