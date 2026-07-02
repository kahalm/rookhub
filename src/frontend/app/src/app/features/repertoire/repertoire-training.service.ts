import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

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
}

@Injectable({ providedIn: 'root' })
export class RepertoireTrainingService {
  constructor(private http: HttpClient) {}

  /** Kombiniertes Repertoire-PGN (alle Dateien). */
  getPgn(repertoireId: number): Observable<string> {
    return this.http.get(`/api/repertoires/${repertoireId}/pgn`, { responseType: 'text' });
  }

  /** SR-Zustände aller (schon gelernten/pausierten) Linien. */
  getLineStates(repertoireId: number): Observable<LineStateDto[]> {
    return this.http.get<LineStateDto[]>(`/api/repertoires/${repertoireId}/training/lines`);
  }

  /** Ergebnis einer geübten Linie melden (richtig → +1 Stufe, falsch → Stufe 1). */
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
