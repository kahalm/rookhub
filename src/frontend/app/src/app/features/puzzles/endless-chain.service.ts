import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PuzzleService, PuzzleDto } from './puzzle.service';
import { buildChainWindows, RatingWindow, ENDLESS_CHAIN_BLOCK } from './endless-prefetch.util';

/**
 * Parameter für die Erzeugung eines Gauntlet-Ketten-Blocks — rein datengetrieben (aus config/Range/
 * Fasttrack), KEIN Komponenten-State. `count` default = {@link ENDLESS_CHAIN_BLOCK}.
 */
export interface ChainBlockRequest {
  /** Start-Elo des Laufs (config.startElo). */
  startElo: number;
  /** T1-Mittelwert der Fasttrack-Kurve. */
  avgFirst: number;
  /** T2-Mittelwert der Fasttrack-Kurve. */
  avgSecond: number;
  /** Obergrenze aus der Puzzle-DB (puzzleRange.max) — Fenster werden darauf geklemmt. */
  ratingMax: number;
  /** Absoluter Startindex des Blocks in der Kette (0 = Run-Start, sonst Verlängerung). */
  startIndex: number;
  /** Erster Lauf des Users → bewusst steile Erst-Lauf-Kurve. */
  firstRun: boolean;
  /** Anzahl Puzzles im Block. */
  count?: number;
}

/**
 * Kapselt die zustandsarme Ketten-Erzeugungs-/Prefetch-Orchestrierung des Endless-Modus:
 * die Abbildung (Kurven-Parameter → Rating-Fenster → serverseitiger Batch-Abruf). Die reine
 * Kurven-/Fenster-Mathematik liegt weiterhin in `endless-prefetch.util` (hier nur delegiert);
 * Komponenten-State (laufende Run-Arrays, seed, runGeneration, Timer), Persistenz und die
 * Themen-Auflösung (worst-themes HTTP + Cache) bleiben bewusst in der Komponente.
 */
@Injectable({ providedIn: 'root' })
export class EndlessChainService {
  constructor(private puzzles: PuzzleService) {}

  /** Rating-Fenster eines Ketten-Blocks entlang der Kurve (delegiert an die pure Util). */
  chainWindows(req: ChainBlockRequest): RatingWindow[] {
    return buildChainWindows(
      req.startElo, req.avgFirst, req.avgSecond, req.ratingMax,
      req.count ?? ENDLESS_CHAIN_BLOCK, req.startIndex, req.firstRun,
    );
  }

  /** Holt (online) einen Ketten-Block für bereits gebaute Fenster + Themen-Filter (getRandomBatch). */
  fetchBatch(windows: RatingWindow[], themes?: string, themesAny?: string): Observable<PuzzleDto[]> {
    return this.puzzles.getRandomBatch(windows, themes, false, themesAny);
  }

  /**
   * Erzeugt die Fenster aus den Kurven-Parametern und holt den Block in einem Schritt.
   * Reine Orchestrierung Parameter→Fenster→HTTP; kein State/keine Persistenz/kein Timing.
   */
  fetchBlock(req: ChainBlockRequest, themes?: string, themesAny?: string): Observable<PuzzleDto[]> {
    return this.fetchBatch(this.chainWindows(req), themes, themesAny);
  }
}
