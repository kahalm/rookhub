import { Injectable, computed, signal } from '@angular/core';

/** Stand einer Stufe auf diesem Geraet. */
export interface LevelProgress {
  /** Beste Sternzahl eines abgeschlossenen Durchgangs (0 = noch nie geschafft). */
  stars: number;
  /** Laufender Durchgang: naechste Aufgabe und bisherige Fehler (Tipps zaehlen mit). */
  runIndex: number;
  runMistakes: number;
}

/** Stand eines Kinderkurses: geloeste Linien (Ids). */
export interface CourseProgress {
  solved: number[];
}

interface StoredProgress {
  levels: Record<string, LevelProgress>;
  courses: Record<string, CourseProgress>;
}

const STORAGE_KEY = 'rh-kids-progress-v1';

/**
 * Sterne fuer einen Durchgang: 0–1 Fehler = 3, 2–4 = 2, sonst 1. Ein geschaffter Durchgang bringt
 * immer mindestens einen Stern — Kinder sollen fuers Durchhalten belohnt werden, nicht bestraft.
 */
export function starsFor(mistakes: number): number {
  if (mistakes <= 1) return 3;
  if (mistakes <= 4) return 2;
  return 1;
}

/**
 * Der Fortschritt der Kinderseite — nur auf diesem Geraet (localStorage), ohne Konto. Faellt der
 * Speicher aus (privates Fenster, gesperrt), laeuft alles im Arbeitsspeicher weiter; der Stand ist
 * dann nach dem Neuladen weg, aber nichts bricht.
 */
@Injectable({ providedIn: 'root' })
export class KidsProgressStore {
  private readonly state = signal<StoredProgress>(this.load());

  /** Zahl der geschafften Stufen. */
  readonly completedLevels = computed(() =>
    Object.values(this.state().levels).filter(l => l.stars > 0).length);

  /** Gesammelte Sterne ueber alle Stufen. */
  readonly totalStars = computed(() =>
    Object.values(this.state().levels).reduce((sum, l) => sum + l.stars, 0));

  level(level: number): LevelProgress {
    return this.state().levels[level] ?? { stars: 0, runIndex: 0, runMistakes: 0 };
  }

  /** Stufe 1 ist immer offen, jede weitere, sobald die davor geschafft ist. */
  isUnlocked(level: number): boolean {
    return level <= 1 || this.level(level - 1).stars > 0;
  }

  /** Die Stufe, bei der das Kind weitermachen sollte: die erste noch nicht geschaffte. */
  currentLevel(levels: number[]): number | null {
    const sorted = [...levels].sort((a, b) => a - b);
    return sorted.find(l => this.level(l).stars === 0) ?? sorted[sorted.length - 1] ?? null;
  }

  /** Eine Aufgabe des laufenden Durchgangs ist geloest. */
  recordSolved(level: number, mistakes: number): void {
    const cur = this.level(level);
    this.patchLevel(level, { ...cur, runIndex: cur.runIndex + 1, runMistakes: cur.runMistakes + mistakes });
  }

  /** Durchgang fertig: Sterne vergeben (die besten bleiben), Durchgang zuruecksetzen. */
  completeRun(level: number): number {
    const cur = this.level(level);
    const stars = starsFor(cur.runMistakes);
    this.patchLevel(level, { stars: Math.max(cur.stars, stars), runIndex: 0, runMistakes: 0 });
    return stars;
  }

  /** Durchgang von vorn (die Sterne bleiben). */
  restartRun(level: number): void {
    const cur = this.level(level);
    this.patchLevel(level, { ...cur, runIndex: 0, runMistakes: 0 });
  }

  course(bookId: number): CourseProgress {
    return this.state().courses[bookId] ?? { solved: [] };
  }

  recordCourseSolved(bookId: number, lineId: number): void {
    const cur = this.course(bookId);
    if (cur.solved.includes(lineId)) return;
    this.patchCourse(bookId, { solved: [...cur.solved, lineId] });
  }

  resetCourse(bookId: number): void {
    this.patchCourse(bookId, { solved: [] });
  }

  private patchLevel(level: number, value: LevelProgress): void {
    this.state.update(s => ({ ...s, levels: { ...s.levels, [level]: value } }));
    this.save();
  }

  private patchCourse(bookId: number, value: CourseProgress): void {
    this.state.update(s => ({ ...s, courses: { ...s.courses, [bookId]: value } }));
    this.save();
  }

  private load(): StoredProgress {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as Partial<StoredProgress>;
        return { levels: parsed.levels ?? {}, courses: parsed.courses ?? {} };
      }
    } catch { /* kein Speicher oder kaputter Eintrag: frisch anfangen */ }
    return { levels: {}, courses: {} };
  }

  private save(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.state()));
    } catch { /* Speicher voll/gesperrt: im Arbeitsspeicher weiter */ }
  }
}
