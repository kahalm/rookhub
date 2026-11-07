import type { ChessableMisses } from './repertoire-trainer-misses.util';
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { RepertoireService } from '../../core/repertoire.service';

/** Ein Intervall-Eintrag der 9-Stufen-Leiter. */
export interface SrLevel {
  value: number;
  unit: 'h' | 'd' | 'w' | 'mo';
}

/** SR-Zustand EINER Repertoire-Linie (Server). Kein Eintrag = noch nicht gelernt (nicht im Pool). */
export interface LineStateDto {
  lineKey: string;
  level: number;
  reps: number;
  lapses: number;
  dueAt: string;            // ISO
  lastReviewedAt: string | null;
  inPool: boolean;
  paused: boolean;
}

/** Effektive SR-Konfiguration eines Repertoires + beide bearbeitbaren Ebenen. */
export interface SrConfigDto {
  effective: SrLevel[];
  user: SrLevel[];
  repertoire: SrLevel[] | null;
  source: 'repertoire' | 'user' | 'default';
}

export interface LineReviewRequest {
  lineKey: string;
  label: string;
  correct: boolean;
  /** Fehlerfrei, aber mit Tipp: die Karte rückt nicht vor (0.886.0). */
  hinted?: boolean;
}

/** Gegner eines „Prep: …"-Trainings-Repertoires (Server `RepertoireOpponentDto`): `opponent` wie `?opponent=` des Trainers,
 *  `name` für „in den Partien von …", `color` = MEINE Farbe, `params` = Filter (`TRAINER_PASS_THROUGH`). */
export interface RepertoireOpponent {
  opponent: string;
  name: string | null;
  color: 'w' | 'b' | null;
  params: Record<string, string>;
}

export interface RepertoireMeta {
  name: string | null;
  opponent: RepertoireOpponent | null;
}

@Injectable({ providedIn: 'root' })
export class RepertoireTrainingService {
  private readonly repertoires = inject(RepertoireService);

  constructor(private http: HttpClient) {}

  /** Kombiniertes Repertoire-PGN (alle Dateien) — derselbe Abruf wie `RepertoireService.getPgnText` (Codereview F3-024). */
  getPgn(repertoireId: number): Observable<string> {
    return this.repertoires.getPgnText(repertoireId);
  }

  /** Nur der Name des Repertoires (Kopf des Trainers, d3-rk-trainer-leer-zwei-aktionen). */
  getRepertoireName(repertoireId: number): Observable<string | null> {
    return this.getRepertoireMeta(repertoireId).pipe(map(r => r.name));
  }

  /** Name + gespeicherter Gegner eines „Prep: …"-Repertoires (0.894.0) aus `GET /api/repertoires/{id}` — EIN Abruf für den Trainer. */
  getRepertoireMeta(repertoireId: number): Observable<RepertoireMeta> {
    return this.http.get<{ name?: string | null; opponent?: RepertoireOpponent | null }>(`/api/repertoires/${repertoireId}`)
      .pipe(map(r => ({ name: r?.name?.trim() || null, opponent: r?.opponent ?? null })));
  }

  /** SR-Zustände aller (schon gelernten/pausierten) Linien. */
  /** Als Flashcard markierte Linien-Schlüssel des Users in diesem Repertoire. */
  getFlashcardMarks(repertoireId: number): Observable<{ lineKeys: string[] }> {
    return this.http.get<{ lineKeys: string[] }>(`/api/repertoires/${repertoireId}/flashcards`);
  }

  /** Setzt/entfernt die persistente Flashcard-Markierung einer Repertoire-Linie. */
  setFlashcardMark(repertoireId: number, lineKey: string, marked: boolean): Observable<{ marked: boolean }> {
    const url = `/api/repertoires/${repertoireId}/flashcards/${encodeURIComponent(lineKey)}`;
    return marked ? this.http.post<{ marked: boolean }>(url, {}) : this.http.delete<{ marked: boolean }>(url);
  }

  getLineStates(repertoireId: number): Observable<LineStateDto[]> {
    return this.http.get<LineStateDto[]>(`/api/repertoires/${repertoireId}/training/lines`);
  }

  /** Chessable-Fehler je Linie (oid → Zeile/Farbe/Anzahl/zuletzt) aus den Extension-Mitschnitten — EIN Aufruf je Repertoire (0.886.0). */
  getChessableMisses(repertoireId: number): Observable<ChessableMisses> {
    return this.http.get<ChessableMisses>(`/api/repertoires/${repertoireId}/training/chessable-misses`);
  }

  /** Ergebnis einer geübten Linie melden (richtig → +1 Stufe, falsch → Stufe 1, richtig mit Tipp → Stufe bleibt). */
  reviewLine(repertoireId: number, req: LineReviewRequest): Observable<LineStateDto> {
    return this.http.post<LineStateDto>(`/api/repertoires/${repertoireId}/training/line-review`, req);
  }

  /** Nimmt Linien in den Übungspool auf (Learn/manuell; Kapitel/Kurs = deren Schlüssel). */
  promote(repertoireId: number, lineKeys: string[]): Observable<{ affected: number }> {
    return this.http.post<{ affected: number }>(`/api/repertoires/${repertoireId}/training/promote`, { lineKeys });
  }

  /** Pausiert/aktiviert Linien (Kapitel = alle seine Schlüssel) — pausierte fallen aus dem Pool. */
  setPaused(repertoireId: number, lineKeys: string[], paused: boolean): Observable<{ affected: number }> {
    return this.http.post<{ affected: number }>(`/api/repertoires/${repertoireId}/training/pause`, { lineKeys, paused });
  }

  /** Macht Pool-Linien sofort fällig + hebt Pause auf (leere Liste = ganzer Kurs). */
  makeDue(repertoireId: number, lineKeys: string[]): Observable<{ affected: number }> {
    return this.http.post<{ affected: number }>(`/api/repertoires/${repertoireId}/training/make-due`, { lineKeys });
  }

  /** Effektive Intervall-Konfiguration dieses Repertoires (+ globale & Override-Ebene). */
  getConfig(repertoireId: number): Observable<SrConfigDto> {
    return this.http.get<SrConfigDto>(`/api/repertoires/${repertoireId}/training/config`);
  }

  /** Pro-Repertoire-Override setzen (levels=null → Override löschen = globale Defaults). */
  setRepertoireConfig(repertoireId: number, levels: SrLevel[] | null): Observable<void> {
    return this.http.put<void>(`/api/repertoires/${repertoireId}/training/config`, { levels });
  }

  /** Globale Nutzer-Intervalle der 9 Stufen. */
  getUserConfig(): Observable<SrLevel[]> {
    return this.http.get<SrLevel[]>(`/api/repertoires/training/sr-config`);
  }

  /** Globale Nutzer-Intervalle setzen (levels=null → auf Defaults zurücksetzen). */
  setUserConfig(levels: SrLevel[] | null): Observable<void> {
    return this.http.put<void>(`/api/repertoires/training/sr-config`, { levels });
  }

  /** Löscht alle Linien-SR-Zustände des Users für dieses Repertoire (frischer Trainings-Start). */
  reset(repertoireId: number): Observable<{ deleted: number }> {
    return this.http.delete<{ deleted: number }>(`/api/repertoires/${repertoireId}/training/reset`);
  }
}
