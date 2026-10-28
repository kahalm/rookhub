/**
 * Einordnung jedes Browser-Speicher-Schlüssels der Oberflächen (RookHub, KidHub, LeagueHub, ClubHub, Turnierseite) —
 * Codereview F1-010: Was gehört dem angemeldeten Konto und muss beim Abmelden vom Gerät, was ist eine Vorliebe des
 * GERÄTS und bleibt? Vorher eine Handliste in `OfflineService`, an der neue Schlüssel vorbeiliefen (Dashboard-Snapshot,
 * Discord-Vormerkung, Punktepartie-Id, KidHub-Endlos-Stand F7-013).
 *
 * `local-storage-keys.spec.ts` sucht in den Quelltexten aller Projekte jedes Literal mit einem Speicher-Präfix
 * (`rookhub_`, `rh.`, `rh-kids-`, `lh-`, `leaguehub`, `public_fav_`) und wird rot, wenn es hier keine Einordnung hat.
 * Neuer Schlüssel ⇒ hier eine Zeile, `user` wird beim Abmelden geräumt (`OfflineService.clearOnLogout`).
 *
 * Reine Daten, ohne Angular.
 */
import { ANON_PUZZLE_SESSION_KEY } from './anon-session';

/** Wohin ein Schlüssel gehört. */
export type StorageKeyClass =
  /** Gehört dem Konto (oder verrät, was es getan hat) — beim Abmelden vom Gerät räumen. */
  | 'user'
  /** Vorliebe des Geräts (Sprache, Brett, Design, Ansicht …) — bleibt beim Abmelden. */
  | 'device'
  /** Die Sitzung selbst (`SessionStore`/`AuthService` räumen sie gezielt). */
  | 'auth'
  /** Offline-Schreib-Warteschlange: konto-gestempelt, geht nur unter demselben Konto hinaus — bleibt bewusst. */
  | 'queue'
  /** Liegt im sessionStorage (nur dieser Tab, endet mit ihm) — nicht im localStorage. */
  | 'session'
  /** Kein Speicherschlüssel (CSS-Klasse, Dateiname, Oberflächen-Kennung) — trägt nur das Präfix. */
  | 'none';

export interface StorageKeyEntry {
  /** Schlüssel oder — mit `prefix` — Anfang einer Schlüsselfamilie (`rookhub_book_offline_<datei>`). */
  key: string;
  prefix?: boolean;
  cls: StorageKeyClass;
  /** Warum so eingeordnet (kurz). */
  why: string;
}

const u = (key: string, why: string, prefix = false): StorageKeyEntry => ({ key, prefix, cls: 'user', why });
const d = (key: string, why: string, prefix = false): StorageKeyEntry => ({ key, prefix, cls: 'device', why });

export const STORAGE_KEYS: readonly StorageKeyEntry[] = [
  // --- Konto/Sitzung -------------------------------------------------------------------------------------------------
  { key: 'rookhub_user', cls: 'auth', why: 'Sitzung (Token, Rechte) — AuthService.logout' },
  { key: 'rookhub_admin_user', cls: 'auth', why: 'Admin-Sicherung beim Einsteigen — SessionStore.clearLocalTraces' },
  { key: 'rookhub_session_end_pending', cls: 'auth', why: 'Merker „geteilte Anmeldung noch beenden“ (F1-004) — muss das Abmelden überleben' },
  { key: 'rookhub_offline_queue', cls: 'queue', why: 'gemerkte Lösungen, konto-gestempelt' },

  // --- Offline-Caches (OfflineService.cacheKeys) ----------------------------------------------------------------------
  u('rookhub_puzzle_offline_pool', 'Offline-Puzzles des Kontos'),
  u('rookhub_book_offline_', 'heruntergeladene Kurse', true),
  u('rookhub_book_idmap', 'Index der heruntergeladenen Kurse'),
  u('rookhub_book_lang_', 'Sprache der Kurskopien', true),
  u('rookhub_book_complete_', 'Vollständig-Marker der Kurskopien', true),
  u('rookhub_daily_offline', 'Tagespuzzle-Cache'),
  u('rookhub_repertoire_offline_', 'heruntergeladene Repertoires', true),
  u('rookhub_courses_cache', 'letzte Kursliste'),

  // --- Nutzer-Spuren (früher OfflineService.LocalTracePrefixes) ---------------------------------------------------
  u('rookhub_endless_', 'Endlos: Konfiguration, Historie, Highscore, laufender Lauf (würde ins nächste Konto übertragen)', true),
  u('rookhub_calc_local_', 'Analysebäume/Bewertungen der Kalkulation', true),
  u('rookhub_calc_timer_', 'Kapitel-Uhr der Kalkulation je Kurs', true),
  u('rookhub_calc_note_off_', 'weggeklickter Kalkulations-Hinweis je Kurs', true),
  u('rookhub_course_local_solved_', 'lokal gelöste Kurs-Linien', true),
  u('rookhub_solve_modes', 'Spielweise je Bereich', true),
  u('rookhub_course_lang', 'Sprachwahl je Kurs (verrät die Kurse)', true),
  u('rookhub_menu_keys', 'Menü-Sichtbarkeit des Kontos'),
  u(ANON_PUZZLE_SESSION_KEY, 'anonyme Puzzle-Sitzung (Besitzer anonymer Stände)'),
  u('rookhub_guess_session', 'anonyme Punktepartie-Sitzung'),
  u('rookhub_dashboard_cache_', 'Dashboard-Snapshot je Konto: Turniere samt Ort/Termin, Kurse, Elo', true),
  u('rookhub_discord_link', 'Discord-Vormerkung (Altbestand im localStorage)'),
  u('rh.turnier.', 'Turnierseite: Kalender-Filter samt Ort/Koordinaten, Verlaufs-Reiter', true),

  // --- neu eingeordnet (F1-010 / F7-013, 2026-10-26) ----------------------------------------------------------------
  u('rookhub_daily_elapsed', 'laufende Lösezeit des Tagespuzzles (fremde Wertung)'),
  u('rookhub_solve_elapsed', 'laufende Lösezeit je Puzzle'),
  u('rookhub_rep_train_chaptercolor_', 'Kapitelfarben je Repertoire-Id (verrät die Repertoires)', true),
  u('rh.ebooks.calc.', 'Kalkulations-Stand je E-Book-Diagramm', true),
  u('lh-club', 'LeagueHub: gewählter Verein des Kontos'),
  u('lh-training-lines', 'gewähltes Trainings-Repertoire des Kontos'),
  u('lh-claim-keys', 'LeagueHub: Schlüssel anonymer Uploads zum Übernehmen ins Konto'),
  u('lh-anon-scans', 'LeagueHub: Schlüssel anonymer Formular-Uploads je Teilen-Link'),
  u('lh-anon-drafts', 'LeagueHub: Schlüssel anonymer Partielisten je Teilen-Link'),
  u('rh-kids-progress-v1', 'KidHub: Stufen/Kurse (KidsProgressSync räumt ihn mit)'),
  u('rh-kids-endless-v1', 'KidHub: Endlos-Läufe und Rekord (F7-013)'),
  u('rh-kids-stars-v1', 'KidHub: geschaffte Stufen der Sternenjagd'),

  // --- Gerätevorlieben ----------------------------------------------------------------------------------------------
  d('rookhub_lang', 'Sprache'),
  d('rookhub_theme', 'Design'),
  d('rookhub_theme_mode', 'Hell/Dunkel'),
  d('rookhub_app_theme', 'Farbschema'),
  d('rookhub_board_theme', 'Brett'),
  d('rookhub_piece_set', 'Figurensatz'),
  d('rookhub_ui_scale', 'Größe der Oberfläche'),
  d('rookhub_eink', 'E-Ink-Modus'),
  d('rookhub_visualization', 'Visualisierung'),
  d('rookhub_viz_arrow', 'Pfeile'),
  d('rookhub_en_passant_forced', 'En-passant-Einstellung'),
  d('rookhub_offpath_warn_moves', 'Warnung abseits der Linie'),
  d('rookhub_puzzle_config', 'Puzzle-Einstellungen'),
  d('rookhub_book_puzzle_config', 'Kurs-Einstellungen'),
  d('rookhub_puzzle_settings_open', 'Einstellungen auf-/zugeklappt'),
  d('rookhub_offline_settings', 'Wie viel offline vorladen'),
  d('rookhub_dashboard_layout_v2', 'Kachel-Anordnung des Dashboards'),
  d('rookhub_notifications_hidden_categories', 'ausgeblendete Benachrichtigungs-Arten'),
  d('rookhub_analysis_', 'Analyse: Engine, Tiefe, Linien, Vergleich, Maia, Explorer offen', true),
  d('rookhub_explorer_settings', 'Eröffnungs-Explorer'),
  d('rookhub_game_', 'Partie-Rückblick: Pfeil, Symbole, Linien, Engine-Ansicht', true),
  d('rookhub_guess_accept', 'Punktepartie: angenommene Züge'),
  d('rookhub_guess_comment_lang', 'Punktepartie: Kommentarsprache'),
  d('rookhub_guess_list_filter', 'Punktepartien: Filter'),
  d('rookhub_position_reps_', 'Stellungssuche: Modus und Ähnlichkeit', true),
  d('rookhub_rep_train_freq_order', 'Repertoire-Training: Reihenfolge'),
  d('rookhub_scoresheet_', 'Formular-Upload: Sprache und Seite', true),
  d('rookhub_apk_seen_version', 'zuletzt gesehene App-Version'),
  d('rookhub_sw_recovery_reload', 'Service-Worker-Wiederanlauf (technisch)'),
  d('rh.ebooks.', 'E-Book-Leser: Breite, Schrift, Layout, Lösestil, Weiter', true),
  d('rh.repertoire.treeArrows', 'Baum-Pfeile'),
  d('rh-kids-stars-free', 'KidHub: Figur und Zahl im freien Spiel'),
  d('lh-auto-my-games', 'LeagueHub: eigene Partien automatisch'),
  d('lh-overview-newest-first', 'LeagueHub: Vereinsübersicht „Neueste zuerst“'),
  d('lh-overview-collapsed', 'LeagueHub: Vereinsübersicht zugeklappte Ligen'),
  d('lh-all-leagues', 'LeagueHub: alle Ligen zeigen'),
  d('lh-scan-engine', 'Formular-Werkbank: Engine'),
  d('lh-tree-filter', 'Spielerkarte: Baum-Filter'),
  d('leaguehub', 'LeagueHub: gewählte Liga/Mannschaft (auch Oberflächen-Kennung)'),
  d('public_fav_', 'Turnierseite ohne Anmeldung: Favoriten/Filter je Turnier (nur abgemeldet benutzt)', true),

  // --- sessionStorage -----------------------------------------------------------------------------------------------
  { key: 'rookhub_last_solved_', prefix: true, cls: 'session', why: 'zuletzt gelöstes Puzzle je Modus' },
  { key: 'rookhub_weekly_mode_', prefix: true, cls: 'session', why: 'Wochenpost: Modus je Post' },
  { key: 'leaguehub-round', cls: 'session', why: 'LeagueHub: gewählte Runde' },

  // --- keine Speicherschlüssel --------------------------------------------------------------------------------------
  { key: 'leaguehub-dev', cls: 'none', why: 'Host-Kennung (partner-site)' },
  { key: 'leaguehub-stapel-', prefix: true, cls: 'none', why: 'Dateiname des Stapel-Downloads' },
  { key: 'lh-menu', cls: 'none', why: 'CSS-Klasse' },
  { key: 'lh-pick', cls: 'none', why: 'CSS-Klasse' },
];

/** Einordnung eines Schlüssels: der genaueste (längste) passende Eintrag; `null` = nicht eingeordnet. */
export function classifyStorageKey(key: string): StorageKeyEntry | null {
  let best: StorageKeyEntry | null = null;
  for (const e of STORAGE_KEYS) {
    const hit = e.prefix ? key.startsWith(e.key) : key === e.key;
    if (hit && (!best || e.key.length > best.key.length)) best = e;
  }
  return best;
}

/** Präfixe/Schlüssel, die beim Abmelden aus dem localStorage verschwinden (`OfflineService.clearOnLogout`). */
export const LOGOUT_CLEARED_KEYS: readonly string[] = STORAGE_KEYS.filter(e => e.cls === 'user').map(e => e.key);

/** Wird `key` beim Abmelden geräumt? Nur, wenn sein genauester Eintrag `user` ist — ein Gerätewert unter einem
 *  Nutzer-Präfix bliebe so erhalten (die Spec verbietet solche Überlappungen ohnehin). */
export function clearedOnLogout(key: string): boolean {
  return classifyStorageKey(key)?.cls === 'user';
}
