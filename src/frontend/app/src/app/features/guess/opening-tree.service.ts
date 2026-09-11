import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { GameAnalysis } from '../analysis/game-analysis.service';
import { LibraryGamePage } from './library.service';

/** Ein Zug, der aus der angesehenen Stellung heraus gespielt wird, und wie oft. */
export interface OpeningMove {
  san: string;
  games: number;
}

export interface OpeningTree {
  /** Die Halbzüge bis hierhin, normalisiert („e4 e5 Nf3"). */
  line: string;
  onlyPlayable: boolean;
  /** Wie viele Partien diese Stellung überhaupt erreichen. */
  total: number;
  moves: OpeningMove[];
}

/**
 * Der Eröffnungsbaum der Punktepartie. Zwei Quellen, dieselbe Form: die schon gerechneten
 * Partien (sofort spielbar) oder der ganze Rohbestand (von dort wird angefordert).
 */
@Injectable({ providedIn: 'root' })
export class OpeningTreeService {
  private http = inject(HttpClient);

  branch(line: string, onlyPlayable: boolean): Observable<OpeningTree> {
    return this.http.get<OpeningTree>('/api/guess-tree', { params: { line, onlyPlayable } });
  }

  /** Die spielbaren Partien zu dieser Stellung. */
  playable(line: string): Observable<GameAnalysis[]> {
    return this.http.get<GameAnalysis[]>('/api/game-analyses/public', { params: { line } });
  }

  /** Der Rohbestand zu dieser Stellung — die Zeilen tragen „anfordern" bzw. „spielen". */
  library(line: string, page: number, pageSize: number): Observable<LibraryGamePage> {
    return this.http.get<LibraryGamePage>('/api/library-games',
      { params: { line, page, pageSize } });
  }
}
