import { Observable, Subscription, switchMap } from 'rxjs';
import type { ExplorerAnalysisRequest, ExplorerAnalysisResult, ExplorerSettings } from './repertoire-explorer.service';
import type { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import type { TrainColor } from '../../shared/repertoire/repertoire-color.util';
import { normalizeFen } from './position-filter.util';

/** Der Teil von `RepertoireExplorerService`, den der Lader braucht. */
export interface FrequencyExplorer {
  effectiveSettings(): Observable<ExplorerSettings>;
  run(repertoireId: number, req: ExplorerAnalysisRequest): Observable<ExplorerAnalysisResult>;
}

/**
 * „Häufigste zuerst" im Repertoire-Trainer (Zerlegung Z21): holt die Linien-Häufigkeiten Runde um Runde über den
 * Explorer (wie der Lochfinder) und sortiert danach. `changed` meldet Fortschritt, `done` das Ende — beendet,
 * übersprungen oder gescheitert; danach baut der Trainer die Warteschlange. Nach {@link dispose} startet kein Lauf
 * mehr (Codereview F3-025: kein Explorer-Lauf ohne Abnehmer).
 */
export class TrainerFrequencyLoader {
  /** Endstellung einer Linie (erste drei FEN-Felder) → wie oft man sie erreicht; null = nicht geladen. */
  lineFreq: Map<string, number> | null = null;
  /** Laufender Abruf (Fortschritt für die LOADING-Ansicht). */
  progress: ExplorerAnalysisResult | null = null;
  running = false;
  /** Hinweis, wenn die Häufigkeiten fehlen oder unvollständig sind (i18n-Key). */
  notice: string | null = null;
  private sub: Subscription | null = null;
  private disposed = false;

  constructor(
    private readonly explorer: () => FrequencyExplorer,
    private readonly changed: () => void,
    private readonly done: () => void,
  ) {}

  /** Häufigkeiten für alle Kapitel holen (Farbe je Kapitel). */
  load(repertoireId: number, chapterColors: Record<string, TrainColor>): void {
    if (this.disposed) return;
    this.sub?.unsubscribe();
    this.running = true;
    this.progress = null;
    this.notice = null;
    const explorer = this.explorer();
    this.sub = explorer.effectiveSettings().pipe(
      switchMap(s => explorer.run(repertoireId, {
        color: null,
        chapterColors,
        source: s.source,
        database: s.database,
        ratings: s.ratings,
        speeds: s.speeds,
        thresholdPercent: s.thresholdPercent,
        includeHoles: false,
        includeLineFrequencies: true,
      })),
    ).subscribe({
      next: r => { this.progress = r; this.changed(); },
      error: () => { this.notice = 'repertoireTrainer.freqFailed'; this.finish(); },
      complete: () => this.finish(),
    });
  }

  /** Abruf beendet ODER übersprungen: mit dem, was da ist, weiter. */
  finish(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    this.running = false;
    const r = this.progress;
    this.lineFreq = new Map(Object.entries(r?.lineFrequencies ?? {}));
    if (!this.notice && r) {
      if (r.tokenMissing || r.tokenInvalid) this.notice = 'repertoireTrainer.freqToken';
      else if (r.fetchFailed) this.notice = 'repertoireTrainer.freqFailed';
      else if (!r.complete) this.notice = 'repertoireTrainer.freqIncomplete';
    }
    this.done();
  }

  /** Wie oft man diese Linie erreicht (0…1), -1 = unbekannt. */
  freqOf(line: ParsedGame): number {
    const end = line.fens[line.fens.length - 1];
    if (!end || !this.lineFreq) return -1;
    return this.lineFreq.get(normalizeFen(end)) ?? -1;
  }

  /** Bei `on` absteigend nach Häufigkeit; unbekannte hinten, sonst Reihenfolge wie gehabt (stabile Sortierung). */
  order(lines: ParsedGame[], on: boolean): ParsedGame[] {
    if (!on || !this.lineFreq) return lines;
    return [...lines].sort((a, b) => this.freqOf(b) - this.freqOf(a));
  }

  /** Laufenden Abruf beenden; danach startet {@link load} nichts mehr. */
  dispose(): void {
    this.disposed = true;
    this.sub?.unsubscribe();
    this.sub = null;
  }
}
