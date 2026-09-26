import { BOOK_OFFLINE_PREFIX, BOOK_ID_MAP_KEY, BOOK_LANG_PREFIX, DAILY_CACHE_KEY, COURSES_CACHE_KEY } from '../../core/offline.service';
import { BoundedMapStore, hasKey, keysWithPrefix, localStore, readJson, readRaw, removeKey, writeJson, writeRaw }
  from '../../core/local-json-store';
import { BookPuzzleDto } from './puzzle.service';
import { OfflineLanguageMeta, languagesFromLines, normLang } from '../courses/course-language.util';

/**
 * Offline-Cache ganzer Bücher (alle Puzzles eines Buchs) im localStorage, gekeyt per
 * Buch-Dateiname (stabil über Kurs-Liste UND Standalone-Buch-Puzzle hinweg).
 *
 * Die localStorage-Mechanik (lesen/schreiben/löschen/Präfix-Suche, alles fehlertolerant) liegt in
 * `core/local-json-store.ts`; hier steht nur noch das Fachliche.
 */
function bookKey(fileName: string): string {
  return BOOK_OFFLINE_PREFIX + encodeURIComponent(fileName);
}

function langKey(fileName: string): string {
  return BOOK_LANG_PREFIX + encodeURIComponent(fileName);
}

/** bookId→fileName-Index laden/speichern (der Kursmodus kennt nur die bookId). */
function loadIdMap(): Record<string, string> {
  return readJson<Record<string, string>>(localStore(), BOOK_ID_MAP_KEY) ?? {};
}
function saveIdMap(m: Record<string, string>): void {
  writeJson(localStore(), BOOK_ID_MAP_KEY, m);
}

/**
 * Buch offline speichern. Liefert `false`, wenn NICHTS geschrieben wurde (Quota voll /
 * Privatmodus) — gleiche Linie wie `writeCalcLocal*`: der Aufrufer darf dann keinen Erfolg
 * melden, sonst glaubt der User an eine Offline-Kopie, die nicht existiert (und das
 * ☁-Häkchen spraenge nach dem Reload zurueck, weil `cachedBookFileNames()` sie nicht findet).
 */
export function saveBookOffline(fileName: string, puzzles: BookPuzzleDto[], bookId?: number,
                                lang?: string | null): boolean {
  if (!fileName) return false;
  // Quota → gar nicht erst in den Index aufnehmen.
  if (!writeJson(localStore(), bookKey(fileName), puzzles ?? [])) return false;
  if (bookId != null) {
    const m = loadIdMap();
    m[String(bookId)] = fileName;
    saveIdMap(m);
  }
  // Sprache der Kopie (Kurs-Übersetzung): mit welchem `?lang=` geholt, und welche Sprachen die Linien
  // nannten. Scheitert NUR dieser Vermerk, liegt die Kopie trotzdem — sie gilt dann wie eine alte
  // Kopie als Original, und schlimmstenfalls erscheint der Hinweis „neu herunterladen" zu oft.
  const meta: OfflineLanguageMeta = { lang: normLang(lang), langs: languagesFromLines(puzzles) };
  writeJson(localStore(), langKey(fileName), meta);
  return true;
}

/**
 * Was die Offline-Kopie über ihre Sprache weiß; `null`, wenn es keine Kopie gibt. Eine Kopie von vor
 * 0.549.0 (ohne Vermerk) wurde ohne `?lang=` geholt und ist damit das Original.
 */
export function getBookOfflineLanguage(fileName: string | null | undefined): OfflineLanguageMeta | null {
  if (!fileName || !hasBookOffline(fileName)) return null;
  const meta = readJson<Partial<OfflineLanguageMeta>>(localStore(), langKey(fileName));
  return {
    lang: normLang(meta?.lang ?? null),
    langs: Array.isArray(meta?.langs) ? meta!.langs.filter((l): l is string => typeof l === 'string') : [],
  };
}

/** Wie {@link getBookOfflineLanguage}, über die (Kurs-)bookId aufgelöst. */
export function getBookOfflineLanguageByBookId(bookId: number): OfflineLanguageMeta | null {
  const fileName = loadIdMap()[String(bookId)];
  return fileName ? getBookOfflineLanguage(fileName) : null;
}

/** Offline gespeichertes Buch über die (Kurs-)bookId auflösen. Null, wenn nicht gespeichert. */
export function getBookOfflineByBookId(bookId: number): BookPuzzleDto[] | null {
  const fileName = loadIdMap()[String(bookId)];
  return fileName ? getBookOffline(fileName) : null;
}

export function getBookOffline(fileName: string): BookPuzzleDto[] | null {
  if (!fileName) return null;
  return readJson<BookPuzzleDto[]>(localStore(), bookKey(fileName));
}

export function hasBookOffline(fileName: string): boolean {
  return hasKey(localStore(), bookKey(fileName));
}

export function removeBookOffline(fileName: string): void {
  removeKey(localStore(), bookKey(fileName));
  removeKey(localStore(), langKey(fileName));
  const m = loadIdMap();
  let changed = false;
  for (const k of Object.keys(m)) if (m[k] === fileName) { delete m[k]; changed = true; }
  if (changed) saveIdMap(m);
}

/**
 * Lokaler Kurs-Fortschritt (gelöste/durchgeklickte Puzzle-Ids) je Buch — für anonyme (nicht
 * eingeloggte) Nutzer, die einen öffentlichen Kurs durchspielen: der Fortschritt bleibt rein
 * clientseitig und übersteht einen Reload. Eingeloggte Nutzer nutzen stattdessen den
 * serverseitigen Fortschritt.
 */
const COURSE_LOCAL_SOLVED_PREFIX = 'rookhub_course_local_solved_';

/** Marker: der lokale Cache dieses Buchs ist VOLLSTÄNDIG (die Seiten-Kette lief bis zum Ende).
 *  Ohne ihn wäre ein Torso — erste Seite geladen, dann Netz weg — beim nächsten Besuch nicht von
 *  einem kompletten Kurs zu unterscheiden: der anonyme Modus zählte die 300 gecachten Linien als
 *  Gesamtzahl und meldete nach 300 Aufgaben „Kurs abgeschlossen", während der Rest für diesen
 *  Browser dauerhaft unerreichbar blieb. */
const BOOK_COMPLETE_PREFIX = 'rookhub_book_complete_';

export function markBookCacheComplete(bookId: number, complete: boolean): void {
  // Quota/Privatmodus → dann gilt der Cache als unvollständig (siehe unten).
  if (complete) writeRaw(localStore(), BOOK_COMPLETE_PREFIX + bookId, '1');
  else removeKey(localStore(), BOOK_COMPLETE_PREFIX + bookId);
}

/** Gilt der lokale Cache als vollständig? Bei gesperrtem Speicher bewusst `false`: dann wird die
 *  Seiten-Kette erneut fortgesetzt, was höchstens Netz kostet — im Gegensatz zu still fehlenden
 *  Linien. */
export function isBookCacheComplete(bookId: number): boolean {
  return readRaw(localStore(), BOOK_COMPLETE_PREFIX + bookId) === '1';
}

export function loadCourseLocalSolved(bookId: number): number[] {
  const arr = readJson<unknown>(localStore(), COURSE_LOCAL_SOLVED_PREFIX + bookId);
  return Array.isArray(arr) ? arr.filter((x): x is number => typeof x === 'number') : [];
}

export function saveCourseLocalSolved(bookId: number, ids: Iterable<number>): void {
  // Quota/Privatmodus → Fortschritt eben nicht persistiert.
  writeJson(localStore(), COURSE_LOCAL_SOLVED_PREFIX + bookId, [...ids]);
}

export function clearCourseLocalSolved(bookId: number): void {
  removeKey(localStore(), COURSE_LOCAL_SOLVED_PREFIX + bookId);
}

/** Wie viele Tagespuzzles offline vorgehalten werden (jüngste gewinnen). */
const DAILY_CACHE_MAX = 14;

/** Die Datums-Schlüssel sind `yyyyMMdd` — lexikografisch sortiert heißt chronologisch, die
 *  Vorgabe-Verdrängung des {@link BoundedMapStore} passt also genau. */
const dailyCache = new BoundedMapStore<BookPuzzleDto>(DAILY_CACHE_KEY, DAILY_CACHE_MAX);

/** Tagespuzzle eines UTC-Datums offline vorhalten (online-Abruf cacht automatisch). */
export function saveDailyOffline(date: string, puzzle: BookPuzzleDto): void {
  if (!date || !puzzle) return;
  dailyCache.set(date, puzzle);
}

/** Offline gecachtes Tagespuzzle eines Datums (oder null). */
export function getDailyOffline(date: string): BookPuzzleDto | null {
  if (!date) return null;
  return dailyCache.get(date) ?? null;
}

/**
 * Letzte erfolgreich geladene Kursliste cachen — Offline-Fallback der /courses-Seite.
 * Bewusst untypisiert (Snapshot der Server-Antwort); der Aufrufer kennt das CourseListItem-Shape.
 */
export function saveCourseListCache<T>(list: T[]): void {
  writeJson(localStore(), COURSES_CACHE_KEY, list ?? []);
}

export function loadCourseListCache<T>(): T[] {
  const arr = readJson<unknown>(localStore(), COURSES_CACHE_KEY);
  return Array.isArray(arr) ? (arr as T[]) : [];
}

/** Sucht ein Puzzle nach Id über ALLE offline gespeicherten Bücher (für Offline-Direktaufruf).
 *  Ein kaputter Eintrag wird übersprungen, nicht zum Abbruch der Suche (wie in
 *  `cachedRepertoires`). */
export function findCachedBookPuzzle(id: number): BookPuzzleDto | null {
  for (const k of keysWithPrefix(localStore(), BOOK_OFFLINE_PREFIX)) {
    const arr = readJson<BookPuzzleDto[]>(localStore(), k);
    const hit = Array.isArray(arr) ? arr.find(p => p.id === id) : undefined;
    if (hit) return hit;
  }
  return null;
}

/** Dateinamen aller offline gespeicherten Bücher (für „bereits gespeichert"-Anzeige). */
export function cachedBookFileNames(): string[] {
  const out: string[] = [];
  for (const k of keysWithPrefix(localStore(), BOOK_OFFLINE_PREFIX)) {
    try { out.push(decodeURIComponent(k.slice(BOOK_OFFLINE_PREFIX.length))); }
    catch { /* kaputte Prozent-Kodierung → überspringen */ }
  }
  return out;
}
