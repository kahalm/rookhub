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
 * Die Laeufe des Endlos-Modus (localStorage) — Grundlage der adaptiven Kurve und des Rekords. Seit 1.020.0 auch im
 * Konto: `KidsProgressSync` schickt sie mit und übernimmt die vereinigte Antwort ({@link adopt}); beim Abmelden leert er
 * das Gerät (F7-013), nach dem Anmelden kommen sie aus dem Konto zurück.
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

  /** Für den Abgleich mit dem Konto. */
  toDto(): StoredEndless { return { runs: [...this.state().runs], best: this.state().best }; }

  /** Vereinigten Stand aus dem Konto übernehmen (`null` = Server kennt ihn nicht → nichts ändern). */
  adopt(d: StoredEndless | null | undefined): void {
    if (!d || !Array.isArray(d.runs)) return;
    // Vereinigen statt ersetzen: ein Lauf, der während der Anfrage dazukam, steht noch nicht in der Antwort.
    const byAt = new Map<number, EndlessRun>();
    for (const r of [...d.runs, ...this.state().runs]) if (!byAt.has(r.at)) byAt.set(r.at, r);
    const runs = [...byAt.values()].sort((a, b) => a.at - b.at).slice(-ENDLESS_KEEP_RUNS);
    const next = { runs, best: Math.max(Number(d.best) || 0, this.state().best, ...runs.map(r => r.solved)) };
    if (JSON.stringify(next) === JSON.stringify(this.state())) return;
    this.state.set(next);
    this.save();
  }

  /** Abmelden (F7-013): Laeufe und Rekord gehoerten dem Kind, das angemeldet war — das naechste am geteilten Tablet
   *  beginnt mit der flachen Anfangskurve und ohne fremden Rekord. */
  clear(): void {
    this.state.set({ runs: [], best: 0 });
    this.save();
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
