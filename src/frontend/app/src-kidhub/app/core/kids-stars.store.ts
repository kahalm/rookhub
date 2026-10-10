import { Injectable, computed, signal } from '@angular/core';
import { STAR_STAGES } from './kids-stars';

const STORAGE_KEY = 'rh-kids-stars-v1';

/**
 * Geschaffte Stufen der Sternenjagd auf diesem Geraet (localStorage). Wie der Endlos-Modus bewusst (noch) nicht im
 * Konto: der Abgleich (`KidsProgressSync`) kennt nur die Puzzle-Stufen und Kurse.
 */
@Injectable({ providedIn: 'root' })
export class KidsStarsStore {
  private readonly state = signal<ReadonlySet<number>>(this.load());

  readonly done = computed(() => this.state().size);
  readonly total = STAR_STAGES.length;
  /** Die erste noch nicht geschaffte Stufe — `null`, wenn alle geschafft sind. */
  readonly current = computed(() => STAR_STAGES.find(s => !this.state().has(s.stage))?.stage ?? null);

  isDone(stage: number): boolean {
    return this.state().has(stage);
  }

  /** Offen ist Stufe 1 und jede, deren Vorgaengerin geschafft ist. */
  isOpen(stage: number): boolean {
    return stage === 1 || this.state().has(stage - 1);
  }

  complete(stage: number): void {
    if (this.state().has(stage)) return;
    this.state.set(new Set([...this.state(), stage]));
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ done: [...this.state()].sort((a, b) => a - b) }));
    } catch { /* Speicher voll/gesperrt: im Arbeitsspeicher weiter */ }
  }

  private load(): ReadonlySet<number> {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      const done = raw ? (JSON.parse(raw) as { done?: unknown }).done : null;
      if (Array.isArray(done)) return new Set(done.filter((n): n is number => Number.isInteger(n) && n >= 1));
    } catch { /* kein Speicher oder kaputter Eintrag */ }
    return new Set();
  }
}
