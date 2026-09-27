import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
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

/** Sprache aus dem Land der Besucher-IP (Server schlaegt lokal nach, speichert nichts). */
export interface KidsLanguageHint {
  country: string | null;
  language: string | null;
}

/** Endpunkte der Kinderseite (`/api/kids/*`) — alle ohne Anmeldung, ausser dem Fortschritt im Konto. */
@Injectable({ providedIn: 'root' })
export class KidsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/kids';

  levels(): Observable<KidsLevel[]> {
    return this.http.get<KidsLevel[]>(`${this.base}/levels`);
  }

  level(level: number): Observable<KidsLevelDetail> {
    return this.http.get<KidsLevelDetail>(`${this.base}/levels/${level}`);
  }

  languageHint(): Observable<KidsLanguageHint> {
    return this.http.get<KidsLanguageHint>(`${this.base}/language-hint`);
  }

  /** Kinderkurse mit ihren Titeln in `lang` (Kindertitel je Sprache, sonst der Buchname). */
  courses(lang?: string): Observable<KidsCourse[]> {
    const params = lang ? new HttpParams().set('lang', lang) : undefined;
    return this.http.get<KidsCourse[]>(`${this.base}/courses`, { params });
  }

  coursePuzzles(bookId: number, lang?: string): Observable<KidsCourseLine[]> {
    const params = lang ? new HttpParams().set('lang', lang) : undefined;
    return this.http.get<KidsCourseLine[]>(`${this.base}/courses/${bookId}/puzzles`, { params });
  }

  /** Nur angemeldet: den hiesigen Stand mit dem Konto zusammenfuehren, Antwort = gemeinsamer Stand. */
  syncProgress(progress: KidsProgressDto): Observable<KidsProgressDto> {
    return this.http.put<KidsProgressDto>(`${this.base}/progress`, progress);
  }
}
