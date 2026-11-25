import { HttpClient } from '@angular/common/http';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { keysWithPrefix, localStore } from '../../core/local-json-store';
import { CALC_LOCAL_PREFIX, clearCalcLocal, readCalcLocal } from './calc/calc-local.util';
import { clearCourseLocalSolved, loadCourseLocalSolved } from '../puzzles/book-offline.util';

const COURSE_SOLVED_PREFIX = 'rookhub_course_local_solved_';
const CHUNK = 500;

/**
 * Was ein Besucher OHNE Anmeldung in Kursen erarbeitet hat, beim Anmelden bzw. Registrieren ins Konto übernehmen
 * (1.020.3) — wie die anonymen Puzzle-/Endlos-Sitzungen (`LoginFollowUpService`). Vorher blieb es als Konto-Spur am
 * Gerät und verschwand beim nächsten Abmelden:
 * - gelöste Kurs-Linien (`rookhub_course_local_solved_<bookId>`) → `POST /api/courses/{bookId}/claim-local-solved`
 *   (nur die Markierung „gelöst“, kein Versuch — anonyme Lösungen zählen nicht in Bestenlisten);
 * - Kalkulations-Stellungen (`rookhub_calc_local_<bookId>`) → `POST /api/calculations/claim-local` (der Konto-Stand
 *   gewinnt, wo es schon einen gibt).
 * Erst nach Erfolg wird der Gerätestand geräumt; scheitert es (offline), versucht es das nächste Anmelden wieder.
 * → Zahl der übernommenen Einträge (Fehler zählen 0).
 */
export function claimGuestCourseWork(http: HttpClient): Observable<number> {
  const store = localStore();
  const jobs: Observable<number>[] = [];

  for (const key of keysWithPrefix(store, COURSE_SOLVED_PREFIX)) {
    const bookId = Number(key.slice(COURSE_SOLVED_PREFIX.length));
    const ids = Number.isInteger(bookId) && bookId > 0 ? loadCourseLocalSolved(bookId) : [];
    if (!ids.length) continue;
    jobs.push(http.post<{ claimed: number }>(`/api/courses/${bookId}/claim-local-solved`, { ids }).pipe(
      map(r => { clearCourseLocalSolved(bookId); return r?.claimed ?? 0; }),
      catchError(() => of(0)),
    ));
  }

  for (const key of keysWithPrefix(store, CALC_LOCAL_PREFIX)) {
    const bookId = Number(key.slice(CALC_LOCAL_PREFIX.length));
    if (!Number.isInteger(bookId) || bookId <= 0) continue;
    const entries = Object.entries(readCalcLocal(bookId))
      .map(([id, e]) => ({
        bookPuzzleId: Number(id), treeJson: e.tree, chosenSan: e.chosenSan, chosenUci: e.chosenUci,
        secondsSpent: e.secondsSpent || 0, grade: e.grade,
      }))
      .filter(e => Number.isInteger(e.bookPuzzleId) && e.bookPuzzleId > 0);
    if (!entries.length) continue;
    const chunks: Observable<number>[] = [];
    for (let i = 0; i < entries.length; i += CHUNK) {
      chunks.push(http.post<{ claimed: number }>('/api/calculations/claim-local', entries.slice(i, i + CHUNK)).pipe(
        map(r => r?.claimed ?? 0)));
    }
    jobs.push(forkJoin(chunks).pipe(
      map(ns => { clearCalcLocal(bookId); return ns.reduce((a, b) => a + b, 0); }),
      catchError(() => of(0)),
    ));
  }

  return jobs.length ? forkJoin(jobs).pipe(map(ns => ns.reduce((a, b) => a + b, 0))) : of(0);
}
