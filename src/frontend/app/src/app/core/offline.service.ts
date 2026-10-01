import { Injectable } from '@angular/core';
import { allKeys, localStore, readJson, readRaw, removeKey, sessionStore, writeJson } from './local-json-store';
import { ANON_PUZZLE_SESSION_KEY } from './anon-session';

/** localStorage-Keys der Offline-Caches. */
export const ENDLESS_POOL_KEY = 'rookhub_endless_offline_pool';
export const PUZZLE_POOL_KEY = 'rookhub_puzzle_offline_pool';
export const BOOK_OFFLINE_PREFIX = 'rookhub_book_offline_';
/** bookId→fileName-Index, damit der Kursmodus (kennt nur die bookId) das offline gespeicherte
 *  Buch (per fileName gekeyt) auflösen kann. Bewusst ANDERER Präfix als BOOK_OFFLINE_PREFIX,
 *  sonst würde er als „gecachtes Buch" mitgezählt/durchsucht. */
export const BOOK_ID_MAP_KEY = 'rookhub_book_idmap';
/** Sprache einer offline gespeicherten Kurskopie (Kurs-Übersetzung, 0.549.0) je Dateiname —
 *  eigener Präfix aus demselben Grund wie beim Index: sonst zählte er als gecachtes Buch. */
export const BOOK_LANG_PREFIX = 'rookhub_book_lang_';
/** Marker „der lokale Cache dieses Buchs ist VOLLSTÄNDIG" je bookId (siehe `markBookCacheComplete`).
 *  Gehört zu den Caches: bliebe er stehen, während die Kursinhalte geräumt werden, gälte ein späterer
 *  Torso als vollständig. */
export const BOOK_COMPLETE_PREFIX = 'rookhub_book_complete_';
/** Tagespuzzle-Cache (Datum→Puzzle); auto-befüllt beim Online-Abruf eines Tagespuzzles. */
export const DAILY_CACHE_KEY = 'rookhub_daily_offline';
/** Heruntergeladene Repertoires (PGN + SR-Zustände + Intervalle) je Repertoire-Id. */
export const REPERTOIRE_OFFLINE_PREFIX = 'rookhub_repertoire_offline_';
/** Letzte erfolgreich geladene Kursliste — Offline-Fallback der /courses-Seite. */
export const COURSES_CACHE_KEY = 'rookhub_courses_cache';
const SETTINGS_KEY = 'rookhub_offline_settings';

export interface OfflineSettings {
  puzzleCount: number;   // Standard-Puzzles offline (auf aktueller Schwierigkeit)
  endlessRuns: number;   // Anzahl vorab geladener Endless-Runs
}

const DEFAULTS: OfflineSettings = { puzzleCount: 30, endlessRuns: 2 };

/**
 * Geräte-lokale Offline-Einstellungen + Verwaltung aller Offline-Caches (Größe/Leeren).
 * Bewusst NICHT serverseitig synchronisiert — der Cache ist pro Gerät.
 */
@Injectable({ providedIn: 'root' })
export class OfflineService {
  private settings: OfflineSettings = this.load();

  private load(): OfflineSettings {
    const s = readJson<Partial<OfflineSettings>>(localStore(), SETTINGS_KEY);
    if (!s) return { ...DEFAULTS };
    return {
      puzzleCount: this.clampInt(s.puzzleCount, DEFAULTS.puzzleCount),
      endlessRuns: this.clampInt(s.endlessRuns, DEFAULTS.endlessRuns),
    };
  }

  private clampInt(v: any, fallback: number): number {
    const n = Math.round(Number(v));
    return Number.isFinite(n) ? Math.max(0, Math.min(200, n)) : fallback;
  }

  get puzzleCount(): number { return this.settings.puzzleCount; }
  get endlessRuns(): number { return this.settings.endlessRuns; }

  setPuzzleCount(n: number): void { this.settings.puzzleCount = this.clampInt(n, DEFAULTS.puzzleCount); this.persist(); }
  setEndlessRuns(n: number): void { this.settings.endlessRuns = this.clampInt(n, DEFAULTS.endlessRuns); this.persist(); }

  private persist(): void {
    // Quota/Privatmodus: dann bleibt es bei den Werten dieser Sitzung — mehr kostet es nicht.
    writeJson(localStore(), SETTINGS_KEY, this.settings);
  }

  /** Alle localStorage-Keys, die zu Offline-Caches gehören (Größenanzeige + „Cache leeren"). */
  private cacheKeys(): string[] {
    return allKeys(localStore()).filter(k =>
      k === ENDLESS_POOL_KEY || k === PUZZLE_POOL_KEY || k === BOOK_ID_MAP_KEY || k === DAILY_CACHE_KEY
      || k === COURSES_CACHE_KEY
      || k.startsWith(BOOK_OFFLINE_PREFIX) || k.startsWith(BOOK_LANG_PREFIX) || k.startsWith(REPERTOIRE_OFFLINE_PREFIX)
      || k.startsWith(BOOK_COMPLETE_PREFIX));
  }

  /** Geräte-lokale Nutzer-SPUREN, die beim Abmelden verschwinden müssen — mehr als die Caches oben.
   *
   *  Hintergrund: `logout()` verspricht, dass nichts für den NÄCHSTEN Nutzer desselben Geräts
   *  sichtbar bleibt. Die Endless-Schlüssel standen nicht auf der Liste, und der Endless-Modus
   *  ÜBERTRÄGT lokale Läufe beim ersten Öffnen ins Konto (Migration von localStorage zum Server):
   *  Nutzer B erbte damit auf einem geteilten Gerät die Laufhistorie und den Highscore von A — in
   *  seiner Statistik und in der Bestenliste. Ebenso die Kalkulations-Notizen und der lokale
   *  Kursfortschritt (Freitext bzw. fremde Leistung) und der Menü-Snapshot (fremde Sichtbarkeit).
   *
   *  Bewusst als PRÄFIX-Liste: eine Handliste einzelner Namen ist genau so gealtert. */
  private static readonly LocalTracePrefixes = [
    'rookhub_endless_',            // Konfiguration, Historie, Highscore, laufender Lauf, Ketten-Seed …
    'rookhub_calc_local_',         // Analysebäume/Bewertungen ohne Konto
    'rookhub_calc_timer_',         // Kapitel-Uhr der Kalkulation je Kurs (ohne Nutzerbezug — fremde Trainingszeit)
    'rookhub_calc_note_off_',      // weggeklickter Kalkulations-Hinweis je Kurs (verrät, welche Kurse offen waren)
    'rookhub_course_local_solved_',// lokal gelöste Kurs-Linien
    'rookhub_solve_modes',         // Spielweise je Bereich
    'rookhub_course_lang',         // Sprachwahl je Kurs (verrät, welche Kurse offen waren)
    'rookhub_menu_keys',           // Menü-Sichtbarkeit des vorigen Nutzers
    ANON_PUZZLE_SESSION_KEY,       // anonyme Puzzle-Sitzung
    'rookhub_guess_session',       // anonyme Partie-Raten-Sitzung (Gegenstück zur Puzzle-Sitzung)
    'rookhub_dashboard_cache_',    // Dashboard-Snapshot je Konto: Turniere samt Ort/Termin, Kurse, Elo
    'rookhub_discord_link',        // Discord-Vormerkung, Altbestand im localStorage (heute sessionStorage, s. u.)
    'rh.turnier.',                 // Turnierseite: Kalender-Filter samt Ort/Koordinaten, Verlaufs-Reiter
  ];

  /** Nutzer-Spuren im sessionStorage DIESES Tabs, die beim Abmelden ebenfalls verschwinden: die
   *  Discord-Vormerkung bleibt nach einem vorübergehenden Fehler beim Einlösen liegen — der NÄCHSTE, der
   *  sich im selben Tab anmeldet, würde sonst gefragt, ob er den Discord-Account des vorigen verknüpft. */
  private static readonly SessionTraceKeys = ['rookhub_discord_link'];

  /** Keys, die beim Abmelden gelöscht werden: Offline-Caches UND die lokalen Nutzer-Spuren. */
  private logoutKeys(): string[] {
    const keys = new Set(this.cacheKeys());
    for (const k of allKeys(localStore()))
      if (OfflineService.LocalTracePrefixes.some(p => k.startsWith(p))) keys.add(k);
    return [...keys];
  }

  /** Gesamtgröße der Offline-Caches in Bytes (UTF-16-Annäherung: 2 Byte/Zeichen). */
  cacheSizeBytes(): number {
    let chars = 0;
    for (const k of this.cacheKeys()) {
      const v = readRaw(localStore(), k);
      if (v) chars += v.length + k.length;
    }
    return chars * 2;
  }

  /** Anzahl gecachter Bücher. */
  cachedBookCount(): number {
    return this.cacheKeys().filter(k => k.startsWith(BOOK_OFFLINE_PREFIX)).length;
  }

  /** Anzahl heruntergeladener Repertoires. */
  cachedRepertoireCount(): number {
    return this.cacheKeys().filter(k => k.startsWith(REPERTOIRE_OFFLINE_PREFIX)).length;
  }

  /** Leert alle Offline-Caches (Einstellungen bleiben erhalten). */
  clearAll(): void {
    for (const k of this.cacheKeys()) removeKey(localStore(), k);
  }

  /** Beim ABMELDEN aufräumen: Caches PLUS die lokalen Nutzer-Spuren (siehe <c>logoutKeys</c>).
   *  Getrennt von <see cref="clearAll"/>, weil „Cache leeren" im Profil nur den Platz freigeben
   *  soll — nicht den laufenden Endless-Lauf oder die Kalkulations-Notizen des ANGEMELDETEN Nutzers. */
  clearOnLogout(): void {
    for (const k of this.logoutKeys()) removeKey(localStore(), k);
    for (const k of OfflineService.SessionTraceKeys) removeKey(sessionStore(), k);
  }

  /** Menschlich lesbare Größe. */
  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
