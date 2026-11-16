import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, distinctUntilChanged, map, of, shareReplay, skip, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';

/**
 * Trainerstunden (Trainingsvideos, privat je Konto): Stundenliste, Stunde mit Segmenten, Suche nach Stellung/Text,
 * Trainerkommentare an einer RookHub-Partie, stundenübergreifender Fragen-Satz und der Lernstand je Segment.
 * Vertrag: `docs/api/trainingsvideos.md`; Oberfläche: `docs/frontend/trainerstunden.md`.
 */
export type TrainingGameKind = 'own' | 'lesson' | 'exercise';
export type TrainingStatus = 'solved' | 'wrong' | 'skipped';
export type TrainingTag = 'question' | 'key' | 'mistake' | 'plan' | 'opening' | 'endgame' | 'tactic';

export interface TrainingGame {
  key: string;
  kind: TrainingGameKind | string;
  white: string | null;
  black: string | null;
  event: string | null;
  year: number | null;
  eco: string | null;
  result: string | null;
  fromSec: number | null;
  toSec: number | null;
  rookhubGameId: number | null;
  matchConfidence: string | null;
  hasPgn: boolean;
}

export interface TrainingLessonListItem {
  id: number;
  /** "YYYY-MM-DD" (Tag der Stunde, ohne Uhrzeit). */
  date: string;
  summary: string | null;
  durationSec: number | null;
  hasVideo: boolean;
  segmentCount: number;
  games: TrainingGame[];
  /** Nur für Admins bei fremden Stunden, sonst null. */
  ownerName: string | null;
}

export interface TrainingQuestion {
  prompt: string;
  /** Gesuchter Zug (SAN, englisch). */
  answer: string | null;
  answerLine: string | null;
  answerSec: number | null;
  explanation: string | null;
}

export interface TrainingProgress {
  status: TrainingStatus;
  attempts: number;
  plays: number;
  updatedAt: string;
}

export interface TrainingSegment {
  id: number;
  startSec: number;
  endSec: number | null;
  gameKey: string | null;
  /** Volle FEN oder null. */
  fen: string | null;
  move: string | null;
  moveKind: 'played' | 'analysis' | null | string;
  comment: string | null;
  quote: string | null;
  /** Varianten als PGN-Zugtext. */
  variations: string | null;
  tags: string[];
  hasQuestion: boolean;
  quoteStartSec: number | null;
  quoteEndSec: number | null;
  question: TrainingQuestion | null;
  progress: TrainingProgress | null;
  /** Aufgabe im Nachspielen (Server): Stellung VOR dem gesuchten Zug — weicht von `fen` ab, wenn die Aufbereitung die
   *  Stellung danach lieferte; `null` = keine Aufgabe. Ältere Antworten ohne das Feld: `undefined`. */
  taskFen?: string | null;
  /** Der gesuchte Zug (SAN, legal aus `taskFen`). */
  taskMove?: string | null;
}

export interface TrainingLesson {
  id: number;
  date: string;
  summary: string | null;
  durationSec: number | null;
  hasVideo: boolean;
  ownerName: string | null;
  games: TrainingGame[];
  segments: TrainingSegment[];
  pgns: Record<string, string>;
  /** Voller ChessBase-Analysebaum (mit Varianten) je Partie. */
  analysisPgns: Record<string, string>;
}

export interface TrainingSearchHit {
  kind: 'segment' | 'note' | string;
  lessonId: number;
  date: string;
  segmentId: number | null;
  startSec: number | null;
  fen: string | null;
  move: string | null;
  text: string | null;
  gameLabel: string | null;
  category: string | null;
  /** Nur Stellungssuche: stimmt auch das Zugrecht? */
  sameSide: boolean | null;
  /** Nur Notizen: die Notiz (für den Filter im Notizbuch). */
  noteId?: number | null;
}

export interface TrainingSearchResult { hits: TrainingSearchHit[]; truncated: boolean; }

export interface TrainingGameComment {
  /** 0 = Anfangsstellung, n = nach dem n-ten Halbzug (= `PgnViewerService.currentMoveIndex + 1`). */
  fenIndex: number;
  matchedBy: 'move' | 'analysis' | 'position' | 'variation' | string;
  lessonId: number;
  date: string;
  segmentId: number;
  startSec: number;
  endSec: number | null;
  fen: string | null;
  move: string | null;
  moveKind: string | null;
  comment: string | null;
  variations: string | null;
  tags: string[];
  hasVideo: boolean;
}

export interface TrainingGameComments { items: TrainingGameComment[]; unmatched: number; }

export interface TrainingDrillItemDto {
  lessonId: number;
  date: string;
  hasVideo: boolean;
  gameLabel: string | null;
  /** Die RookHub-Partie zur Frage, falls zugeordnet. */
  rookhubGameId: number | null;
  segment: TrainingSegment;
}

/** Kategorien der Trainer-Notizen, in der Reihenfolge der Reiter des Notizbuchs (Server: `TrainingVideoImporter`). */
export const TRAINING_NOTE_CATEGORIES = ['schwaeche', 'eroeffnung', 'plan', 'rechnen', 'endspiel', 'zeit', 'psychologie', 'allgemein'] as const;
export type TrainingNoteCategory = typeof TRAINING_NOTE_CATEGORIES[number];

export interface TrainingNotebookNote {
  id: number;
  lessonId: number;
  date: string;
  /** Sekunde im Video, zu der die Aussage fällt. */
  startSec: number | null;
  category: TrainingNoteCategory | string;
  text: string;
  /** Rohzitat aus dem Transkript. */
  quote: string | null;
  gameKey: string | null;
  gameWhite: string | null;
  gameBlack: string | null;
  gameKind: TrainingGameKind | string | null;
  rookhubGameId: number | null;
  hasVideo: boolean;
  lessonDurationSec: number | null;
}

/** „Sagt er immer wieder“: `lessons` Stunden, `noteId` = stellvertretende Notiz, `noteIds` neueste zuerst. */
export interface TrainingNotebookTheme { lessons: number; noteId: number; category: string; noteIds: number[]; }

export interface TrainingNotebookRepertoireHit {
  repertoireId: number;
  repertoireName: string;
  chapter: string;
  lineName: string;
  gameIndex: number;
  ply: number;
  /** Züge des Repertoires aus dieser Stellung (SAN, Hauptzug zuerst; leer = Linie endet hier). */
  moves: string[];
  sameSide: boolean;
}

export interface TrainingNotebookOpening {
  noteId: number;
  /** Stellung des nächstgelegenen Segments derselben Partie (null = keine). */
  fen: string | null;
  segmentId: number | null;
  segmentMove: string | null;
  repertoire: TrainingNotebookRepertoireHit[];
}

export interface TrainingNotebook {
  notes: TrainingNotebookNote[];
  themes: TrainingNotebookTheme[];
  openings: TrainingNotebookOpening[];
  /** Das Konto hat eigene Repertoire-Linien (sonst kein Abgleich). */
  hasRepertoire: boolean;
  repertoireTruncated: boolean;
}

/** So lange muss der Video-Ausweis mindestens noch gelten (eine Stunde Video samt Spulen); er gilt 12 h. */
const TOKEN_MARGIN_MS = 3 * 60 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class TrainingVideosService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/training-videos';

  private token: { value: string; expiresAt: number } | null = null;
  private tokenRequest: Observable<string> | null = null;

  constructor() {
    // Der Ausweis gehört zu genau einem Konto: bei An-/Abmelden oder Kontowechsel vergessen (Muster EbookService).
    inject(AuthService).currentUser$?.pipe(map(u => u?.userId ?? null), distinctUntilChanged(), skip(1))
      .subscribe(() => this.forgetToken());
  }

  list(): Observable<TrainingLessonListItem[]> {
    return this.http.get<TrainingLessonListItem[]>(this.base);
  }

  get(id: number): Observable<TrainingLesson> {
    return this.http.get<TrainingLesson>(`${this.base}/${id}`);
  }

  searchPosition(fen: string): Observable<TrainingSearchResult> {
    return this.http.get<TrainingSearchResult>(`${this.base}/search/position`, { params: new HttpParams().set('fen', fen) });
  }

  searchText(q: string): Observable<TrainingSearchResult> {
    return this.http.get<TrainingSearchResult>(`${this.base}/search/text`, { params: new HttpParams().set('q', q) });
  }

  gameComments(gameId: number): Observable<TrainingGameComments> {
    return this.http.get<TrainingGameComments>(`${this.base}/games/${gameId}/comments`);
  }

  notebook(): Observable<TrainingNotebook> {
    return this.http.get<TrainingNotebook>(`${this.base}/notebook`);
  }

  questions(onlyWrong = false): Observable<TrainingDrillItemDto[]> {
    return this.http.get<TrainingDrillItemDto[]>(`${this.base}/questions`, { params: new HttpParams().set('onlyWrong', String(onlyWrong)) });
  }

  saveProgress(lessonId: number, segmentId: number, status: TrainingStatus, attempts: number): Observable<TrainingProgress> {
    return this.http.put<TrainingProgress>(`${this.base}/${lessonId}/segments/${segmentId}/progress`, { status, attempts });
  }

  /**
   * Ausweis für `<video src>` (`?t=`): einmal geholt, bis kurz vor Ablauf wiederverwendet; parallele Anfragen teilen sich
   * EINE Server-Anfrage (Muster `EbookService.accessToken`).
   */
  accessToken(): Observable<string> {
    if (this.token && this.token.expiresAt - Date.now() > TOKEN_MARGIN_MS) return of(this.token.value);
    if (!this.tokenRequest) {
      this.tokenRequest = this.http.get<{ token: string; expiresAt: string }>(`${this.base}/access-token`).pipe(
        map(r => {
          this.token = { value: r.token, expiresAt: Date.parse(r.expiresAt) };
          return r.token;
        }),
        tap({ finalize: () => { this.tokenRequest = null; } }),
        shareReplay(1),
      );
    }
    return this.tokenRequest;
  }

  /** Vergessen (z. B. nachdem das Video nicht lud) — der nächste Aufruf holt einen frischen. */
  forgetToken(): void {
    this.token = null;
    this.tokenRequest = null;
  }

  videoUrl(lessonId: number, token: string): string {
    return `${this.base}/${lessonId}/video?t=${encodeURIComponent(token)}`;
  }
}

/** „Weiß – Schwarz“ einer Partie der Stunde; fehlende Namen (auch PGN-„?“) als `unknown`. */
export function trainingGameLabel(g: Pick<TrainingGame, 'white' | 'black'> | null | undefined, unknown: string): string {
  const name = (s: string | null | undefined) => (s && s.trim() && s.trim() !== '?' ? s.trim() : unknown);
  return `${name(g?.white)} – ${name(g?.black)}`;
}
