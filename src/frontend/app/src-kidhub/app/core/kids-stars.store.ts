import { Injectable, computed, signal } from '@angular/core';
import { STAR_STAGES } from './kids-stars';

const STORAGE_KEY = 'rh-kids-stars-v1';

/**
 * Geschaffte Stufen der Sternenjagd (localStorage). Seit 1.020.0 auch im Konto (`KidsProgressSync`, {@link adopt}).
 */
@Injectable({ providedIn: 'root' })
export class KidsStarsStore {
  private readonly state = signal<ReadonlySet<number>>(this.load('done'));
  /** Per Link (`/stars/:stage/unlock`) freigeschaltete Stufen — offen, ohne dass die davor geschafft sind. */
  private readonly unlocked = signal<ReadonlySet<number>>(this.load('unlocked'));

  readonly done = computed(() => this.state().size);
  readonly total = STAR_STAGES.length;
  /** Die erste noch nicht geschaffte Stufe — `null`, wenn alle geschafft sind. */
  readonly current = computed(() => STAR_STAGES.find(s => !this.state().has(s.stage))?.stage ?? null);

  isDone(stage: number): boolean {
    return this.state().has(stage);
  }

  /** Offen ist Stufe 1, jede, deren Vorgaengerin geschafft ist, und jede per Link freigeschaltete. */
  isOpen(stage: number): boolean {
    return stage === 1 || this.state().has(stage - 1) || this.unlocked().has(stage);
  }

  complete(stage: number): void {
    if (this.state().has(stage)) return;
    this.state.set(new Set([...this.state(), stage]));
    this.save();
  }

  /** Schaltet eine Stufe frei (verschickter Link); unbekannte Stufen werden ignoriert. */
  unlock(stage: number): boolean {
    if (!STAR_STAGES.some(s => s.stage === stage)) return false;
    if (!this.isOpen(stage)) {
      this.unlocked.set(new Set([...this.unlocked(), stage]));
      this.save();
    }
    return true;
  }

  /** Änderungszähler für den Abgleich. */
  readonly revision = computed(() => this.state().size + this.unlocked().size * 1000);

  toDto(): { done: number[]; unlocked: number[] } {
    const sorted = (set: ReadonlySet<number>) => [...set].sort((a, b) => a - b);
    return { done: sorted(this.state()), unlocked: sorted(this.unlocked()) };
  }

  /** Vereinigten Stand aus dem Konto übernehmen (`null` = nichts ändern). */
  adopt(d: { done?: number[]; unlocked?: number[] } | null | undefined): void {
    if (!d) return;
    const ok = (l: unknown) => (Array.isArray(l) ? l : []).filter((n): n is number => Number.isInteger(n) && n >= 1);
    const done = new Set([...this.state(), ...ok(d.done)]);
    const unlocked = new Set([...this.unlocked(), ...ok(d.unlocked)]);
    if (done.size === this.state().size && unlocked.size === this.unlocked().size) return;
    this.state.set(done);
    this.unlocked.set(unlocked);
    this.save();
  }

  /** Abmelden (F7-013): geschaffte und per Link freigeschaltete Stufen gehoerten dem angemeldeten Kind. */
  clear(): void {
    this.state.set(new Set());
    this.unlocked.set(new Set());
    this.save();
  }

  private save(): void {
    const sorted = (set: ReadonlySet<number>) => [...set].sort((a, b) => a - b);
    const data: { done: number[]; unlocked?: number[] } = { done: sorted(this.state()) };
    if (this.unlocked().size) data.unlocked = sorted(this.unlocked());
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
    } catch { /* Speicher voll/gesperrt: im Arbeitsspeicher weiter */ }
  }

  private load(field: 'done' | 'unlocked'): ReadonlySet<number> {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      const list = raw ? (JSON.parse(raw) as Record<string, unknown>)[field] : null;
      if (Array.isArray(list)) return new Set(list.filter((n): n is number => Number.isInteger(n) && n >= 1));
    } catch { /* kein Speicher oder kaputter Eintrag */ }
    return new Set();
  }
}
