import { BOOK_OFFLINE_PREFIX, BOOK_ID_MAP_KEY, DAILY_CACHE_KEY } from '../../core/offline.service';
import { BookPuzzleDto } from './puzzle.service';

/**
 * Offline-Cache ganzer Bücher (alle Puzzles eines Buchs) im localStorage, gekeyt per
 * Buch-Dateiname (stabil über Kurs-Liste UND Standalone-Buch-Puzzle hinweg).
 */
function bookKey(fileName: string): string {
  return BOOK_OFFLINE_PREFIX + encodeURIComponent(fileName);
}

/** bookId→fileName-Index laden/speichern (der Kursmodus kennt nur die bookId). */
function loadIdMap(): Record<string, string> {
  try { return JSON.parse(localStorage.getItem(BOOK_ID_MAP_KEY) || '{}') || {}; } catch { return {}; }
}
function saveIdMap(m: Record<string, string>): void {
  try { localStorage.setItem(BOOK_ID_MAP_KEY, JSON.stringify(m)); } catch { /* ignore */ }
}

export function saveBookOffline(fileName: string, puzzles: BookPuzzleDto[], bookId?: number): void {
  if (!fileName) return;
  try { localStorage.setItem(bookKey(fileName), JSON.stringify(puzzles ?? [])); }
  catch { return; /* Quota → gar nicht erst in den Index aufnehmen */ }
  if (bookId != null) {
    const m = loadIdMap();
    m[String(bookId)] = fileName;
    saveIdMap(m);
  }
}

/** Offline gespeichertes Buch über die (Kurs-)bookId auflösen. Null, wenn nicht gespeichert. */
export function getBookOfflineByBookId(bookId: number): BookPuzzleDto[] | null {
  const fileName = loadIdMap()[String(bookId)];
  return fileName ? getBookOffline(fileName) : null;
}

export function getBookOffline(fileName: string): BookPuzzleDto[] | null {
  if (!fileName) return null;
  try {
    const raw = localStorage.getItem(bookKey(fileName));
    return raw ? (JSON.parse(raw) as BookPuzzleDto[]) : null;
  } catch { return null; }
}

export function hasBookOffline(fileName: string): boolean {
  try { return localStorage.getItem(bookKey(fileName)) != null; } catch { return false; }
}

export function removeBookOffline(fileName: string): void {
  try { localStorage.removeItem(bookKey(fileName)); } catch { /* ignore */ }
  try {
    const m = loadIdMap();
    let changed = false;
    for (const k of Object.keys(m)) if (m[k] === fileName) { delete m[k]; changed = true; }
    if (changed) saveIdMap(m);
  } catch { /* ignore */ }
}

/**
 * Lokaler Kurs-Fortschritt (gelöste/durchgeklickte Puzzle-Ids) je Buch — für anonyme (nicht
 * eingeloggte) Nutzer, die einen öffentlichen Kurs durchspielen: der Fortschritt bleibt rein
 * clientseitig und übersteht einen Reload. Eingeloggte Nutzer nutzen stattdessen den
 * serverseitigen Fortschritt.
 */
const COURSE_LOCAL_SOLVED_PREFIX = 'rookhub_course_local_solved_';

export function loadCourseLocalSolved(bookId: number): number[] {
  try {
    const raw = localStorage.getItem(COURSE_LOCAL_SOLVED_PREFIX + bookId);
    const arr = raw ? JSON.parse(raw) : [];
    return Array.isArray(arr) ? arr.filter((x): x is number => typeof x === 'number') : [];
  } catch { return []; }
}

export function saveCourseLocalSolved(bookId: number, ids: Iterable<number>): void {
  try { localStorage.setItem(COURSE_LOCAL_SOLVED_PREFIX + bookId, JSON.stringify([...ids])); }
  catch { /* Quota/Privatmodus → Fortschritt eben nicht persistiert */ }
}

export function clearCourseLocalSolved(bookId: number): void {
  try { localStorage.removeItem(COURSE_LOCAL_SOLVED_PREFIX + bookId); } catch { /* ignore */ }
}

/** Wie viele Tagespuzzles offline vorgehalten werden (jüngste gewinnen). */
const DAILY_CACHE_MAX = 14;

/** Tagespuzzle eines UTC-Datums offline vorhalten (online-Abruf cacht automatisch). */
export function saveDailyOffline(date: string, puzzle: BookPuzzleDto): void {
  if (!date || !puzzle) return;
  try {
    const map: Record<string, BookPuzzleDto> = JSON.parse(localStorage.getItem(DAILY_CACHE_KEY) || '{}') || {};
    map[date] = puzzle;
    // Auf die jüngsten DAILY_CACHE_MAX Datumsschlüssel begrenzen (lexikografisch = chronologisch bei yyyyMMdd).
    const keys = Object.keys(map).sort();
    while (keys.length > DAILY_CACHE_MAX) { delete map[keys.shift()!]; }
    localStorage.setItem(DAILY_CACHE_KEY, JSON.stringify(map));
  } catch { /* Quota/ignore */ }
}

/** Offline gecachtes Tagespuzzle eines Datums (oder null). */
export function getDailyOffline(date: string): BookPuzzleDto | null {
  if (!date) return null;
  try {
    const map = JSON.parse(localStorage.getItem(DAILY_CACHE_KEY) || '{}') || {};
    return map[date] ?? null;
  } catch { return null; }
}

/** Sucht ein Puzzle nach Id über ALLE offline gespeicherten Bücher (für Offline-Direktaufruf). */
export function findCachedBookPuzzle(id: number): BookPuzzleDto | null {
  try {
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (!k || !k.startsWith(BOOK_OFFLINE_PREFIX)) continue;
      const arr = JSON.parse(localStorage.getItem(k) || '[]') as BookPuzzleDto[];
      const hit = arr.find(p => p.id === id);
      if (hit) return hit;
    }
  } catch { /* ignore */ }
  return null;
}

/** Dateinamen aller offline gespeicherten Bücher (für „bereits gespeichert"-Anzeige). */
export function cachedBookFileNames(): string[] {
  const out: string[] = [];
  try {
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith(BOOK_OFFLINE_PREFIX)) {
        try { out.push(decodeURIComponent(k.slice(BOOK_OFFLINE_PREFIX.length))); } catch { /* ignore */ }
      }
    }
  } catch { /* ignore */ }
  return out;
}
