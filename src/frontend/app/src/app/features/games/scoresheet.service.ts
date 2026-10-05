import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { SavedGameDetail } from './games.service';
import { downloadBlob } from '../../shared/download.util';

/** Wie weit ist eine Formular-Einlesung? (`GET /api/scoresheets/{id}`) */
export interface ScoresheetScan {
  id: number;
  status: 'pending' | 'running' | 'done' | 'failed';
  /** Grund bei `failed`: `unreadable`, `noMoves`, `refused`, `notConfigured`, `failed`. */
  error?: string | null;
  savedGameId?: number | null;
  /** Über wie viele Fotos das Formular geht (0.600.0). */
  pageCount?: number;
  notationLanguage: string;
  /** „Ich spielte": white/black/auto. */
  ownerSide?: string;
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
  /** Ab wann wieder eingelesen werden kann (ISO, UTC), wenn die Tageszahl erreicht ist; sonst `null`. */
  nextAllowedAt?: string | null;
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
  /** Befund der Engine-Prüfung: `replaced` | `suggested` | fehlt (0.646.0). */
  check?: string | null;
}

export interface ScoresheetEditState {
  scanId: number;
  notationLanguage: string;
  written: string[];
  /** Je Formular-Eintrag (Index wie `written`) der Kasten auf dem Foto, [x0, y0, x1, y1] in 0..1000; `null` = unbekannt. */
  boxes?: (number[] | null)[];
  /** Über wie viele Fotos das Formular geht (0.600.0); fehlt = 1. */
  pageCount?: number;
  /** Je Formular-Eintrag seine Seite (ab 1) — die Kästen stehen in 0..1000 DIESER Seite. */
  pages?: number[];
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
  /** Meine Seite: `white`/`black`; leer = zurücknehmen; weglassen = unverändert. */
  ownerSide?: string | null;
  /** Klassifizierer 1/2 der Partienliste: leer = zurücknehmen (Online-Partien: wieder abgeleitet), weglassen = unverändert. */
  classifier1?: string | null;
  classifier2?: string | null;
  /** Eigene Tags: die vollständige neue Liste; weglassen = unverändert. */
  tags?: string[];
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

  /** Ein Foto oder mehrere (Seiten in Reihenfolge, 0.600.0) — je Seite ein Teil `file`. */
  upload(files: File | File[], language: string, side: 'white' | 'black' | 'auto' = 'auto'): Observable<ScoresheetScan> {
    const form = new FormData();
    for (const file of Array.isArray(files) ? files : [files]) form.append('file', file, file.name);
    form.append('language', language);
    form.append('side', side);
    return this.http.post<ScoresheetScan>('/api/scoresheets', form);
  }

  scan(id: number): Observable<ScoresheetScan> {
    return this.http.get<ScoresheetScan>(`/api/scoresheets/${id}`);
  }

  /** Das Foto als Blob — über den HttpClient, weil ein `<img src>` das Anmelde-Token nicht mitschickt. */
  photo(gameId: number, page = 1): Observable<Blob> {
    return this.http.get(photoUrl(gameId, page), { responseType: 'blob' });
  }

  /** Eine Seite samt der Zahl aller Seiten (Header `X-Page-Count`) — damit blättert der Foto-Dialog. */
  photoPage(gameId: number, page = 1): Observable<{ blob: Blob; pageCount: number }> {
    return this.http.get(photoUrl(gameId, page), { responseType: 'blob', observe: 'response' }).pipe(
      map(r => ({ blob: r.body as Blob, pageCount: Math.max(1, Number(r.headers.get('X-Page-Count')) || 1) })),
    );
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

function photoUrl(gameId: number, page: number): string {
  return page > 1 ? `/api/games/${gameId}/photo?page=${page}` : `/api/games/${gameId}/photo`;
}

/** Dateiname für den Download des Formular-Fotos einer Partie (Endung nach dem Bildtyp; ab Seite 2 mit Nummer). */
export function photoFileName(gameId: number, blob: Blob, page = 1): string {
  const ext = blob.type === 'image/png' ? 'png' : blob.type === 'image/webp' ? 'webp' : 'jpg';
  return page > 1 ? `scoresheet-${gameId}-${page}.${ext}` : `scoresheet-${gameId}.${ext}`;
}

/** Foto anzeigen (neuer Tab) bzw. herunterladen — geteilt von Partienliste und Partieseite. */
export function openPhotoBlob(blob: Blob, download: string | null): void {
  if (download) {
    downloadBlob(blob, download);
    return;
  }
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener');
  // Der neue Tab braucht die Adresse noch einen Moment; danach wird sie freigegeben.
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
