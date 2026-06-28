import { Injectable } from '@angular/core';

/** localStorage-Keys der Offline-Caches. */
export const ENDLESS_POOL_KEY = 'rookhub_endless_offline_pool';
export const PUZZLE_POOL_KEY = 'rookhub_puzzle_offline_pool';
export const BOOK_OFFLINE_PREFIX = 'rookhub_book_offline_';
/** bookId→fileName-Index, damit der Kursmodus (kennt nur die bookId) das offline gespeicherte
 *  Buch (per fileName gekeyt) auflösen kann. Bewusst ANDERER Präfix als BOOK_OFFLINE_PREFIX,
 *  sonst würde er als „gecachtes Buch" mitgezählt/durchsucht. */
export const BOOK_ID_MAP_KEY = 'rookhub_book_idmap';
/** Tagespuzzle-Cache (Datum→Puzzle); auto-befüllt beim Online-Abruf eines Tagespuzzles. */
export const DAILY_CACHE_KEY = 'rookhub_daily_offline';
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
    try {
      const raw = localStorage.getItem(SETTINGS_KEY);
      if (raw) {
        const s = JSON.parse(raw);
        return {
          puzzleCount: this.clampInt(s.puzzleCount, DEFAULTS.puzzleCount),
          endlessRuns: this.clampInt(s.endlessRuns, DEFAULTS.endlessRuns),
        };
      }
    } catch { /* ignore */ }
    return { ...DEFAULTS };
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
    try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(this.settings)); } catch { /* ignore */ }
  }

  /** Alle localStorage-Keys, die zu Offline-Caches gehören. */
  private cacheKeys(): string[] {
    const keys: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (!k) continue;
      if (k === ENDLESS_POOL_KEY || k === PUZZLE_POOL_KEY || k === BOOK_ID_MAP_KEY || k === DAILY_CACHE_KEY || k.startsWith(BOOK_OFFLINE_PREFIX)) keys.push(k);
    }
    return keys;
  }

  /** Gesamtgröße der Offline-Caches in Bytes (UTF-16-Annäherung: 2 Byte/Zeichen). */
  cacheSizeBytes(): number {
    let chars = 0;
    for (const k of this.cacheKeys()) {
      const v = localStorage.getItem(k);
      if (v) chars += v.length + k.length;
    }
    return chars * 2;
  }

  /** Anzahl gecachter Bücher. */
  cachedBookCount(): number {
    return this.cacheKeys().filter(k => k.startsWith(BOOK_OFFLINE_PREFIX)).length;
  }

  /** Leert alle Offline-Caches (Einstellungen bleiben erhalten). */
  clearAll(): void {
    for (const k of this.cacheKeys()) {
      try { localStorage.removeItem(k); } catch { /* ignore */ }
    }
  }

  /** Menschlich lesbare Größe. */
  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
