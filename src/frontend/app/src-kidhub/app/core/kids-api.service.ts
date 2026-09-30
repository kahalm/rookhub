import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { MonoTypeOperatorFunction, Observable, retry, shareReplay, tap, throwError, timer } from 'rxjs';
import { KidsProgressDto } from './kids-progress.store';

/** Thema einer Stufe — die Schluessel kommen so vom Server (`KidsCurriculum.Themes`). */
export type KidsTheme = 'mate1' | 'promote' | 'capture' | 'fork' | 'skewer' | 'mate2' | 'pin' | 'discovered';

export interface KidsLevel {
  level: number;
  theme: KidsTheme;
  puzzleCount: number;
}

export interface KidsPuzzle {
  id: number;
  fen: string;
  /** Lichess-Form: moves[0] stellt die Aufgabe, danach ist das Kind am Zug. */
  moves: string;
}

export interface KidsLevelDetail {
  level: number;
  theme: KidsTheme;
  puzzles: KidsPuzzle[];
}

export interface KidsCourse {
  bookId: number;
  title: string;
  description: string | null;
  puzzleCount: number;
}

/** Die Felder einer Kurs-Linie, die die Kinderseite braucht (Teilmenge von `BookPuzzleDto`). */
export interface KidsCourseLine {
  id: number;
  bookTitle: string | null;
  fen: string;
  moves: string;
  startPly: number;
  title: string | null;
  titleLabel?: string | null;
  chapter: string | null;
  chapterLabel?: string | null;
  comment: string | null;
  moveComments: Record<number, string> | null;
  altMoves: string | null;
}

/** Ein Puzzle des Endlos-Modus (Lichess-Form wie `KidsPuzzle`) samt Rating. */
export interface KidsEndlessPuzzle extends KidsPuzzle {
  rating: number;
}

/** Sprache aus dem Land der Besucher-IP (Server schlaegt lokal nach, speichert nichts). */
export interface KidsLanguageHint {
  country: string | null;
  language: string | null;
}

/**
 * Alle Drosseln der API sind Minutenfenster (`FixedWindow`, 1 min) — laenger als bis zum naechsten Fenster
 * haelt ein 429 nie. So lange wartet die Kinderseite, wenn die Antwort keine Wartezeit nennt (die
 * Rate-Limiter der API schicken heute kein `Retry-After`), und laenger auch dann nicht.
 */
export const RATE_LIMIT_WINDOW_MS = 60_000;

/** So lange behaelt die Seite Stufen und Kursliste im Speicher — so lange, wie der Server sie dem Browser
 *  erlaubt (`KidsController.SharedMaxAgeSeconds`, `Cache-Control: max-age=300`). */
export const CATALOG_TTL_MS = 5 * 60_000;

/** Wartezeit aus dem `Retry-After` eines 429 (Sekunden ODER HTTP-Datum), gedeckelt auf 1 s bis ein
 *  Minutenfenster; ohne (brauchbaren) Header ein ganzes Fenster. */
export function retryAfterMs(err: HttpErrorResponse): number {
  const raw = err.headers?.get('Retry-After')?.trim();
  if (!raw) return RATE_LIMIT_WINDOW_MS;
  const secs = Number(raw);
  const ms = Number.isFinite(secs) ? secs * 1000 : Date.parse(raw) - Date.now();
  return Number.isFinite(ms) ? Math.min(Math.max(ms, 1000), RATE_LIMIT_WINDOW_MS) : RATE_LIMIT_WINDOW_MS;
}

/**
 * Ein 429 (Drossel) EINMAL nach der Wartezeit nachholen, statt dem Kind das Fehlerbild zu zeigen — eine
 * Schulklasse hinter einer NAT-Adresse teilt sich den Topf, und die Sperre endet mit dem Minutenfenster.
 * Bewusst nur hier und nicht im geteilten `retryInterceptor` (der wiederholt 429 absichtlich nicht) und
 * nur einmal: kommt danach wieder 429, bleibt es beim Fehlerbild.
 */
export function retryOnceAfter429<T>(): MonoTypeOperatorFunction<T> {
  return retry({
    count: 1,
    delay: (err: unknown) => err instanceof HttpErrorResponse && err.status === 429
      ? timer(retryAfterMs(err))
      : throwError(() => err),
  });
}

/** Endpunkte der Kinderseite (`/api/kids/*`) — alle ohne Anmeldung, ausser dem Fortschritt im Konto. */
@Injectable({ providedIn: 'root' })
export class KidsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/kids';
  /** Stufen-Leiter und Kursliste (je Sprache) — fuer alle gleich; Startseite, Stufenkarte und JEDER
   *  Stufenstart fragten sie sonst neu ab. Nur Erfolge bleiben liegen, eine leere Leiter nicht. */
  private readonly catalog = new Map<string, { until: number; data$: Observable<unknown> }>();

  levels(): Observable<KidsLevel[]> {
    // Leer = Leiter noch nicht (oder gerade neu) aufgebaut — der Server laesst sie auch nicht zwischenspeichern.
    return this.shared('levels', this.http.get<KidsLevel[]>(`${this.base}/levels`), levels => levels.length > 0);
  }

  level(level: number): Observable<KidsLevelDetail> {
    return this.http.get<KidsLevelDetail>(`${this.base}/levels/${level}`).pipe(retryOnceAfter429());
  }

  languageHint(): Observable<KidsLanguageHint> {
    return this.http.get<KidsLanguageHint>(`${this.base}/language-hint`);
  }

  /** Kinderkurse mit ihren Titeln in `lang` (Kindertitel je Sprache, sonst der Buchname). */
  courses(lang?: string): Observable<KidsCourse[]> {
    const params = lang ? new HttpParams().set('lang', lang) : undefined;
    return this.shared(`courses:${lang ?? ''}`, this.http.get<KidsCourse[]>(`${this.base}/courses`, { params }));
  }

  coursePuzzles(bookId: number, lang?: string): Observable<KidsCourseLine[]> {
    const params = lang ? new HttpParams().set('lang', lang) : undefined;
    return this.http.get<KidsCourseLine[]>(`${this.base}/courses/${bookId}/puzzles`, { params }).pipe(retryOnceAfter429());
  }

  /** Endlos-Modus: je Rating-Fenster ein kindgerechtes Puzzle (in Fensterreihenfolge; Fenster ohne Treffer fehlen). */
  endlessBatch(windows: { minRating: number; maxRating: number }[], exclude: number[]): Observable<KidsEndlessPuzzle[]> {
    return this.http.post<KidsEndlessPuzzle[]>(`${this.base}/endless/batch`, { windows, exclude });
  }

  /** Nur angemeldet: den hiesigen Stand mit dem Konto zusammenfuehren, Antwort = gemeinsamer Stand. */
  syncProgress(progress: KidsProgressDto): Observable<KidsProgressDto> {
    return this.http.put<KidsProgressDto>(`${this.base}/progress`, progress);
  }

  /** Eine Antwort fuer alle Aufrufer bis `CATALOG_TTL_MS`; ein Fehler (auch nach dem 429-Nachholen) oder
   *  ein Wert, den `keep` verwirft, raeumt den Eintrag gleich wieder ab — der naechste Aufruf fragt neu. */
  private shared<T>(key: string, request: Observable<T>, keep: (value: T) => boolean = () => true): Observable<T> {
    const hit = this.catalog.get(key);
    if (hit && hit.until > Date.now()) return hit.data$ as Observable<T>;
    const drop = () => { if (this.catalog.get(key)?.data$ === data$) this.catalog.delete(key); };
    const data$: Observable<T> = request.pipe(
      retryOnceAfter429(),
      tap({ next: value => { if (!keep(value)) drop(); }, error: drop }),
      shareReplay(1),
    );
    this.catalog.set(key, { until: Date.now() + CATALOG_TTL_MS, data$ });
    return data$;
  }
}
