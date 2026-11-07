import { Observable } from 'rxjs';
import type { LineReviewRequest, LineStateDto, SrLevel } from './repertoire-training.service';
import { DEFAULT_SR_LEVELS, applyPause, applyPromote, applySrReview } from './repertoire-sr.util';
import { getRepertoireOffline, updateRepertoireOfflineStates } from './repertoire-offline.util';

/** Die Server-Aufrufe, die der Abgleich braucht (`RepertoireTrainingService`). */
export interface SrSyncTraining {
  promote(repertoireId: number, lineKeys: string[]): Observable<unknown>;
  reviewLine(repertoireId: number, req: LineReviewRequest): Observable<LineStateDto>;
  setPaused(repertoireId: number, lineKeys: string[], paused: boolean): Observable<unknown>;
}

/** Offline-Queue (`OfflineQueueService.enqueue`). */
export interface SrSyncQueue {
  enqueue(method: 'POST' | 'PUT', url: string, body: unknown): unknown;
}

export interface SrSyncDeps {
  repertoireId: () => number;
  training: () => SrSyncTraining;
  queue: () => SrSyncQueue;
  /** `navigator.onLine === false` — Vorgabe: der Browser. */
  isOffline?: () => boolean;
  now?: () => number;
}

const browserOffline = () => typeof navigator !== 'undefined' && !navigator.onLine;

/**
 * Spaced-Repetition-Abgleich des Repertoire-Trainers (Zerlegung Z21): hält die SR-Zustände je Linie, meldet
 * „gelernt" (Pool-Aufnahme) und das Linien-Urteil an den Server und rechnet offline bzw. bei einem Fehler lokal
 * (`repertoire-sr.util`, Spiegel des Backends) — Zustände werden in die Offline-Kopie gespiegelt, der Request
 * wird über die Offline-Queue nachgereicht. Ohne Angular.
 */
export class TrainerSrSync {
  private states = new Map<string, LineStateDto>();   // key = lineKey
  /** Effektive SR-Intervalle aus der Offline-Kopie (nur fürs lokale Fälligkeits-Rechnen). */
  private srLevels: SrLevel[] | null = null;

  constructor(private readonly deps: SrSyncDeps) {}

  private get repId(): number { return this.deps.repertoireId(); }
  private get now(): number { return (this.deps.now ?? Date.now)(); }
  private get offline(): boolean { return (this.deps.isOffline ?? browserOffline)(); }

  /** Zustände (neu) setzen — frisch vom Server oder aus der Offline-Kopie. */
  setStates(states: LineStateDto[]): void { this.states = new Map(states.map(s => [s.lineKey, s])); }

  setSrLevels(levels: SrLevel[] | null): void { this.srLevels = levels; }

  stateOf(lineKey: string): LineStateDto | undefined { return this.states.get(lineKey); }

  clear(): void { this.states.clear(); }

  /** Linie oft genug gelernt → in den Pool (sofort fällig). Lokal IMMER nachziehen; offline bzw. bei einem Fehler
   *  geht der Request in die Offline-Queue. */
  promoteLearned(key: string): void {
    this.states.set(key, applyPromote(this.states.get(key), key, this.now));
    this.persist();
    const url = `/api/repertoires/${this.repId}/training/promote`;
    const body = { lineKeys: [key] };
    if (this.offline) {
      this.deps.queue().enqueue('POST', url, body);
    } else {
      this.deps.training().promote(this.repId, [key])
        .subscribe({ next: () => { /* Zustand steht schon lokal */ }, error: () => this.deps.queue().enqueue('POST', url, body) });
    }
  }

  /** Linie pausieren bzw. fortsetzen (Trainer-Knopf „Linie pausieren", 0.889.0). Wie {@link promoteLearned}: lokal IMMER
   *  nachziehen (Kopie spiegeln); offline bzw. bei einem Fehler geht der Request in die Offline-Queue. */
  setPaused(key: string, paused: boolean): void {
    this.states.set(key, applyPause(this.states.get(key), key, paused, this.now));
    this.persist();
    const url = `/api/repertoires/${this.repId}/training/pause`;
    const body = { lineKeys: [key], paused };
    if (this.offline) {
      this.deps.queue().enqueue('POST', url, body);
    } else {
      this.deps.training().setPaused(this.repId, [key], paused)
        .subscribe({ next: () => { /* Zustand steht schon lokal */ }, error: () => this.deps.queue().enqueue('POST', url, body) });
    }
  }

  /** Urteil über eine Linie melden; `onDue` bekommt die neue Fälligkeit (Server-Antwort oder lokal gerechnet). */
  review(req: LineReviewRequest, onDue: (dueAt: string) => void): void {
    if (this.offline) { this.applyLocal(req, onDue); return; }
    this.deps.training().reviewLine(this.repId, req).subscribe({
      next: st => {
        // Die Pause bestimmt der Trainer selbst (ein parallel laufendes „Pausieren" darf die Antwort nicht zurückdrehen).
        const prev = this.states.get(st.lineKey);
        if (prev && prev.paused !== st.paused) st = { ...st, paused: prev.paused };
        this.states.set(st.lineKey, st);
        this.persist();
        onDue(st.dueAt);
      },
      error: () => this.applyLocal(req, onDue),
    });
  }

  /** Offline-/Fehlerpfad der Bewertung: Stufen-Übergang lokal, Kopie nachziehen, Request vormerken. */
  private applyLocal(req: LineReviewRequest, onDue: (dueAt: string) => void): void {
    const prev = this.states.get(req.lineKey);
    // Die Bewertung lässt die Pause stehen (wie der Server).
    const st = { ...applySrReview(prev, req.lineKey, req.correct, this.effectiveSrLevels(), this.now, req.hinted), paused: !!prev?.paused };
    this.states.set(st.lineKey, st);
    this.persist();
    this.deps.queue().enqueue('POST', `/api/repertoires/${this.repId}/training/line-review`, req);
    onDue(st.dueAt);
  }

  /** Intervalle fürs lokale Fälligkeits-Rechnen: Offline-Kopie → Client-Defaults. */
  private effectiveSrLevels(): SrLevel[] {
    if (this.srLevels?.length) return this.srLevels;
    const cached = getRepertoireOffline(this.repId);
    return cached?.config?.length ? cached.config : DEFAULT_SR_LEVELS;
  }

  /** SR-Zustände in die Offline-Kopie spiegeln (No-op, wenn nicht heruntergeladen). */
  private persist(): void {
    updateRepertoireOfflineStates(this.repId, [...this.states.values()]);
  }
}
