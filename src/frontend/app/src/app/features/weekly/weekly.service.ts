import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BookPuzzleDto } from '../puzzles/puzzle.service';

export interface WeeklyPost {
  id: number;
  title: string;
  fileName: string;
  fileSize: number;
  scheduledAt: string;
  createdAt: string;
  updatedAt: string;
}

export interface WeeklyPostDetail extends WeeklyPost {
  pgnContent: string;
}

/** Wochenpost als Puzzle-Sequenz zum Durchspielen. */
export interface WeeklyPlay {
  id: number;
  title: string;
  puzzles: BookPuzzleDto[];
}

/** Per-User-Fortschritt eines Wochenposts. */
export interface WeeklyProgress {
  weeklyPostId: number;
  total: number;
  playedCount: number;
  solvedCount: number;
  completed: boolean;
  /** Gesamtzeit über alle gespielten Puzzles dieses Wochenposts in Sekunden. */
  totalSeconds: number;
  /** Indizes der bereits gespielten Puzzles (für „zum ersten neuen Puzzle springen"); leer in der Übersicht. */
  playedIndices?: number[];
}

// --- Termin-Helfer (reine Funktionen, testbar ohne Komponente) ---

/** Datums-Teil "YYYY-MM-DD" aus einem ISO-String "YYYY-MM-DDTHH:mm:ss". */
export function weeklyDatePart(iso: string): string {
  return iso.split('T')[0] || '';
}

/** Uhrzeit "HH:mm" aus einem ISO-String; Default 19:00, wenn nicht vorhanden. */
export function weeklyTimePart(iso: string): string {
  return (iso.split('T')[1] || '19:00').slice(0, 5);
}

function ymd(d: Date): string {
  const p = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/**
 * Vorgeschlagener Termin für den nächsten Wochenpost:
 * letzter Termin + 7 Tage, gleiche Uhrzeit; ohne vorherigen Eintrag heute um 19:00.
 */
export function nextWeeklySlot(latestScheduledAt: string | null): { date: string; time: string } {
  if (latestScheduledAt) {
    const d = new Date(weeklyDatePart(latestScheduledAt) + 'T00:00:00');
    d.setDate(d.getDate() + 7);
    return { date: ymd(d), time: weeklyTimePart(latestScheduledAt) };
  }
  return { date: ymd(new Date()), time: '19:00' };
}

@Injectable({ providedIn: 'root' })
export class WeeklyService {
  constructor(private http: HttpClient) {}

  getAll(): Observable<WeeklyPost[]> {
    return this.http.get<WeeklyPost[]>('/api/weekly-posts');
  }

  getById(id: number): Observable<WeeklyPostDetail> {
    return this.http.get<WeeklyPostDetail>(`/api/weekly-posts/${id}`);
  }

  /** Puzzles des Wochenposts zum sequenziellen Durchspielen. */
  getPlay(id: number): Observable<WeeklyPlay> {
    return this.http.get<WeeklyPlay>(`/api/weekly-posts/${id}/puzzles`);
  }

  /** Fortschritt des eingeloggten Users für diesen Wochenpost. */
  getProgress(id: number): Observable<WeeklyProgress> {
    return this.http.get<WeeklyProgress>(`/api/weekly-posts/${id}/progress`);
  }

  /** Fortschritt des eingeloggten Users über alle Wochenposts (nur Posts mit Versuchen) — für die Übersicht. */
  getAllProgress(): Observable<WeeklyProgress[]> {
    return this.http.get<WeeklyProgress[]>('/api/weekly-posts/progress');
  }

  /** Zeichnet ein gespieltes Puzzle (gelöst oder nicht) des Wochenposts auf. */
  recordAttempt(id: number, puzzleIndex: number, solved: boolean, timeSeconds: number): Observable<WeeklyProgress> {
    return this.http.post<WeeklyProgress>(`/api/weekly-posts/${id}/attempt`, { puzzleIndex, solved, timeSeconds });
  }

  /** scheduledAt als lokaler Wall-Clock-String "YYYY-MM-DDTHH:mm:ss" (ohne Zeitzone). */
  create(file: File, scheduledAt: string, title?: string): Observable<WeeklyPost> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('scheduledAt', scheduledAt);
    if (title) form.append('title', title);
    return this.http.post<WeeklyPost>('/api/admin/weekly-posts', form);
  }

  update(id: number, dto: { title?: string; scheduledAt?: string }): Observable<WeeklyPost> {
    return this.http.put<WeeklyPost>(`/api/admin/weekly-posts/${id}`, dto);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/weekly-posts/${id}`);
  }
}
