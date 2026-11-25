/**
 * Anzeige-Vorlieben, die am Konto mitwandern (1.018.0): eine Whitelist von localStorage-Schlüsseln der Oberflächen,
 * deren ROHER Wert im Profil liegt (`uiPrefs` an `GET/PUT /api/profile`, Server `UiPrefKeys` + `ProfileChoiceMaps`).
 * Vorher galten sie nur am Gerät: neue Geräte fingen bei null an, und die konto-gebundenen (`lh-club`,
 * `lh-training-lines`, `rh.turnier.historyView`) gingen bei jedem Abmelden, Sitzungsablauf und Hub-Wechsel verloren.
 *
 * <p><b>Ohne Eingriff in die Schreiber:</b> Die Seiten schreiben ihren Schlüssel wie bisher selbst. Hier liegt ein
 * Abbild des zuletzt abgeglichenen Stands (`SNAPSHOT_KEY`, je Schlüssel Wert + Zeitstempel); ein Wert, der davon
 * abweicht, gilt als JETZT geändert. Abgeglichen wird beim Laden des Profils (Anmelden, Kontowechsel), wenn der Tab in
 * den Hintergrund geht und beim Abmelden (vor dem Räumen, mit dem Token der endenden Sitzung).</p>
 *
 * <p>Regeln wie bei der Spielweise (`stamped-choices.ts`): Profil gewinnt, bei Konflikt der jüngere; was nur das Gerät
 * hat, wird beim ersten Mal hochgeladen (mit Zeitstempel 1 — ein vorhandener Profilwert schlägt es). Gäste: nichts.</p>
 *
 * Reine Funktionen auf dem Speicher, ohne Angular — benutzt vom `PreferencesService`.
 */
import { localStore, readJson, readRaw, writeJson, writeRaw } from './local-json-store';
import { mergeStamped } from './stamped-choices';

/** Handgespiegelt von `UiPrefKeys.Allowed` (Server) — beide Seiten mit LITERALEN Werten im Test. */
export const SYNCED_UI_PREF_KEYS: readonly string[] = [
  // Puzzle-/Brett-Einstellungen (PreferencesService)
  'rookhub_puzzle_config', 'rookhub_visualization', 'rookhub_viz_arrow', 'rookhub_offpath_warn_moves',
  'rookhub_en_passant_forced',
  // RookHub-Seiten
  'rookhub_dashboard_layout_v2', 'rookhub_notifications_hidden_categories', 'rookhub_guess_accept',
  'rookhub_guess_comment_lang', 'rookhub_explorer_settings', 'rookhub_rep_train_show_comments',
  'rookhub_rep_train_review_engine', 'rookhub_deviation_threshold', 'rookhub_game_arrow', 'rookhub_game_badges',
  'rookhub_game_engine_view', 'rookhub_game_lines', 'rookhub_analysis_compare', 'rookhub_analysis_depth',
  'rookhub_analysis_lines', 'rookhub_analysis_maia_elo', 'rookhub_analysis_maia_evalbar',
  'rookhub_analysis_maia_warn', 'rh.ebooks.solveStyle', 'rh.ebooks.autoNext', 'rh.repertoire.treeArrows',
  // Turnierseite
  'rookhub_live_eval', 'rh.turnier.historyView',
  // LeagueHub / Spielerkarte
  'lh-club', 'lh-training-lines', 'lh-tree-filter', 'lh-auto-my-games', 'lh-all-leagues', 'lh-overview-newest-first',
];

/** Wie der Server (`UiPrefKeys.MaxValueLength`): längere Werte bleiben am Gerät. */
export const UI_PREF_MAX_VALUE = 2000;

/** Abbild des zuletzt abgeglichenen Stands. Trägt Werte konto-gebundener Schlüssel → wird beim Abmelden geräumt. */
export const SNAPSHOT_KEY = 'rookhub_ui_prefs_sync';

export interface UiPrefEntry { v: string; at: number; }
type Snapshot = Record<string, UiPrefEntry>;

const ALLOWED = new Set(SYNCED_UI_PREF_KEYS);

function readSnapshot(): Snapshot {
  const s = readJson<Snapshot>(localStore(), SNAPSHOT_KEY);
  return s && typeof s === 'object' && !Array.isArray(s) ? s : {};
}

function valid(key: string, e: UiPrefEntry): boolean {
  return ALLOWED.has(key) && typeof e.v === 'string' && e.v.length <= UI_PREF_MAX_VALUE;
}

/** Der aktuelle Gerätestand mit Zeitstempeln: unverändert seit dem Abbild → dessen `at`, geändert → `now`, nie
 *  abgeglichen → 1 (ein Profilwert schlägt ihn, ein leeres Profil bekommt ihn). */
function localState(snap: Snapshot, now: number): Snapshot {
  const out: Snapshot = {};
  for (const key of SYNCED_UI_PREF_KEYS) {
    const v = readRaw(localStore(), key);
    if (v == null || v.length > UI_PREF_MAX_VALUE) continue;
    const s = snap[key];
    out[key] = { v, at: !s ? 1 : s.v === v ? s.at : now };
  }
  return out;
}

/**
 * Lokale Änderungen seit dem letzten Abgleich (für den PUT). Leer = nichts zu tun. Ein gelöschter Wert wird nicht
 * hochgeladen (das Profil kennt kein Löschen). Das Abbild zieht erst {@link commitUiPrefChanges} nach — nach dem
 * erfolgreichen PUT, sonst ginge eine offline gemachte Änderung nie hinaus.
 */
export function collectUiPrefChanges(now = Date.now()): Record<string, UiPrefEntry> {
  const snap = readSnapshot();
  const changes: Record<string, UiPrefEntry> = {};
  for (const [k, e] of Object.entries(localState(snap, now))) if (snap[k]?.v !== e.v) changes[k] = e;
  return changes;
}

/** Hochgeladene Einträge ins Abbild übernehmen. */
export function commitUiPrefChanges(changes: Record<string, UiPrefEntry>): void {
  if (Object.keys(changes).length) writeJson(localStore(), SNAPSHOT_KEY, { ...readSnapshot(), ...changes });
}

/**
 * Profil ⇄ Gerät: gewinnt das Profil, wird der Wert in den localStorage geschrieben (die Seite liest ihn beim nächsten
 * Öffnen). Liefert, was hochzuladen ist, und ob sich am Gerät etwas geändert hat.
 */
export function mergeUiPrefsFromProfile(server: Record<string, UiPrefEntry> | null | undefined, now = Date.now()):
  { upload: Record<string, UiPrefEntry>; changed: boolean } {
  const snap = readSnapshot();
  const { merged, upload, changed } = mergeStamped(localState(snap, now), server, valid);
  const applied: string[] = [];
  if (changed) {
    for (const [k, e] of Object.entries(merged)) {
      if (readRaw(localStore(), k) !== e.v && writeRaw(localStore(), k, e.v)) applied.push(k);
    }
  }
  writeJson(localStore(), SNAPSHOT_KEY, merged);
  if (applied.length && typeof window !== 'undefined') {
    // Seiten, die ihren Wert schon gelesen haben (gewählter Verein), ziehen nach.
    window.dispatchEvent(new CustomEvent<string[]>(UI_PREFS_APPLIED_EVENT, { detail: applied }));
  }
  return { upload, changed };
}

/** Fenster-Ereignis nach dem Einarbeiten: `detail` = die Schlüssel, deren Wert aus dem Profil kam. */
export const UI_PREFS_APPLIED_EVENT = 'rh-ui-prefs-applied';

/** Für Specs: alle synchronisierten Schlüssel samt Abbild vom Gerät — sonst lädt ein Profil-Abruf Reste anderer Specs
 *  hoch (zusätzlicher PUT, je nach Reihenfolge der Specs). */
export function clearSyncedUiPrefsForTest(): void {
  for (const k of [...SYNCED_UI_PREF_KEYS, SNAPSHOT_KEY]) {
    try { localStorage.removeItem(k); } catch { /* ohne Speicher */ }
  }
}
