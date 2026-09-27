import { Injectable, computed, signal } from '@angular/core';
import { EndlessRun } from './kids-endless';

interface StoredEndless {
  runs: EndlessRun[];
  /** Meiste geschaffte Aufgaben in einem Lauf. */
  best: number;
}

const STORAGE_KEY = 'rh-kids-endless-v1';
/** So viele Laeufe bleiben liegen — die Kurve braucht die letzten zehn. */
export const ENDLESS_KEEP_RUNS = 20;

/**
 * Die Laeufe des Endlos-Modus auf diesem Geraet (localStorage) — Grundlage der adaptiven Kurve und des Rekords.
 * Bewusst (noch) nicht im Konto: der Abgleich (`KidsProgressSync`) kennt nur Stufen und Kurse.
 */
@Injectable({ providedIn: 'root' })
export class KidsEndlessStore {
  private readonly state = signal<StoredEndless>(this.load());

  readonly best = computed(() => this.state().best);
  readonly runs = computed(() => this.state().runs);

  /** Einen beendeten Lauf ablegen; `true`, wenn er ein neuer Rekord ist. */
  record(run: EndlessRun): boolean {
    const cur = this.state();
    const isBest = run.solved > cur.best;
    this.state.set({
      runs: [...cur.runs, run].slice(-ENDLESS_KEEP_RUNS),
      best: Math.max(cur.best, run.solved),
    });
    this.save();
    return isBest;
  }

  private load(): StoredEndless {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as Partial<StoredEndless>;
        return { runs: Array.isArray(parsed.runs) ? parsed.runs : [], best: Number(parsed.best) || 0 };
      }
    } catch { /* kein Speicher oder kaputter Eintrag */ }
    return { runs: [], best: 0 };
  }

  private save(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.state()));
    } catch { /* Speicher voll/gesperrt: im Arbeitsspeicher weiter */ }
  }
}
