import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SavedGameDetail } from './games.service';

/** Wie weit ist eine Formular-Einlesung? (`GET /api/scoresheets/{id}`) */
export interface ScoresheetScan {
  id: number;
  status: 'pending' | 'running' | 'done' | 'failed';
  /** Grund bei `failed`: `unreadable`, `noMoves`, `refused`, `notConfigured`, `failed`. */
  error?: string | null;
  savedGameId?: number | null;
  notationLanguage: string;
  fileName?: string | null;
  createdAt: string;
  finishedAt?: string | null;
  rounds: number;
  moveCount: number;
  uncertainCount: number;
  unresolvedCount: number;
  white?: string | null;
  black?: string | null;
}

export interface ScoresheetLanguage {
  code: string;
  name: string;
  /** Figurenbuchstaben König Dame Turm Läufer Springer in dieser Sprache. */
  pieces: string;
}

export interface ScoresheetStatus {
  available: boolean;
  dailyLimit: number;
  usedToday: number;
  /** Wie viel vom Kostenbudget verbraucht ist (das knappere von Tag und 30 Tagen), 0–100. */
  budgetUsedPercent?: number;
  /** Warum gerade nichts geht: `userDailyBudget`, `userMonthlyBudget`, `globalBudget`; `null` = es geht. */
  blocked?: string | null;
  /** Admin: keine Nutzerbudgets. */
  unlimited?: boolean;
  languages: ScoresheetLanguage[];
}

/** Eine mögliche Lesart an einer unsicheren Stelle — samt dem, was aus ihr folgt. */
export interface ScoresheetOption {
  san: string;
  uci: string;
  match: string;
  /** Wie viele der folgenden Formular-Einträge sich mit dieser Lesart glatt lesen lassen. */
  reach: number;
  /** Die ersten Folgezüge dieser Lesart. */
  preview: string[];
}

/** Ein Halbzug, wie der Server ihn aus dem Formular aufgelöst hat. */
export interface ScoresheetPly {
  /** Index des Formular-Eintrags; `null` = vom Nutzer eingefügt. */
  w?: number | null;
  written: string;
  san: string;
  uci: string;
  match: string;
  uncertain: boolean;
  confirmed?: boolean;
  options?: ScoresheetOption[] | null;
}

export interface ScoresheetEditState {
  scanId: number;
  notationLanguage: string;
  written: string[];
  plies: ScoresheetPly[];
  unresolved: string[];
  unresolvedFrom?: number | null;
}

export interface ScoresheetResolveResult {
  plies: ScoresheetPly[];
  unresolved: string[];
  unresolvedFrom?: number | null;
}

/** `PUT /api/games/{id}` — die korrigierte Partie. */
export interface GameUpdate {
  moves: { san: string; comment?: string | null }[];
  white?: string | null;
  black?: string | null;
  result?: string | null;
  event?: string | null;
  site?: string | null;
  round?: string | null;
  /** `yyyy-MM-dd` oder leer. */
  date?: string | null;
  scoresheetPlies?: ScoresheetPly[] | null;
}

/** „Partieformular einlesen" (0.529.0) und die Korrektur einer gespeicherten Partie. */
@Injectable({ providedIn: 'root' })
export class ScoresheetService {
  private http = inject(HttpClient);

  status(): Observable<ScoresheetStatus> {
    return this.http.get<ScoresheetStatus>('/api/scoresheets/status');
  }

  recent(take = 10): Observable<ScoresheetScan[]> {
    return this.http.get<ScoresheetScan[]>(`/api/scoresheets?take=${take}`);
  }

  upload(file: File, language: string): Observable<ScoresheetScan> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('language', language);
    return this.http.post<ScoresheetScan>('/api/scoresheets', form);
  }

  scan(id: number): Observable<ScoresheetScan> {
    return this.http.get<ScoresheetScan>(`/api/scoresheets/${id}`);
  }

  /** Das Foto als Blob — über den HttpClient, weil ein `<img src>` das Anmelde-Token nicht mitschickt. */
  photo(gameId: number): Observable<Blob> {
    return this.http.get(`/api/games/${gameId}/photo`, { responseType: 'blob' });
  }

  editState(gameId: number): Observable<ScoresheetEditState> {
    return this.http.get<ScoresheetEditState>(`/api/games/${gameId}/scoresheet`);
  }

  resolve(gameId: number, prefix: string[], writtenFrom: number): Observable<ScoresheetResolveResult> {
    return this.http.post<ScoresheetResolveResult>(`/api/games/${gameId}/scoresheet/resolve`, { prefix, writtenFrom });
  }

  update(gameId: number, body: GameUpdate): Observable<SavedGameDetail> {
    return this.http.put<SavedGameDetail>(`/api/games/${gameId}`, body);
  }
}

/** Dateiname für den Download des Formular-Fotos einer Partie (Endung nach dem Bildtyp). */
export function photoFileName(gameId: number, blob: Blob): string {
  const ext = blob.type === 'image/png' ? 'png' : blob.type === 'image/webp' ? 'webp' : 'jpg';
  return `scoresheet-${gameId}.${ext}`;
}

/** Foto anzeigen (neuer Tab) bzw. herunterladen — geteilt von Partienliste und Partieseite. */
export function openPhotoBlob(blob: Blob, download: string | null): void {
  const url = URL.createObjectURL(blob);
  if (download) {
    const a = document.createElement('a');
    a.href = url;
    a.download = download;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } else {
    window.open(url, '_blank', 'noopener');
  }
  // Der neue Tab braucht die Adresse noch einen Moment; danach wird sie freigegeben.
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
