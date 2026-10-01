import { Injectable, computed, signal } from '@angular/core';

/** Stand einer Stufe. */
export interface LevelProgress {
  /** Beste Sternzahl eines abgeschlossenen Durchgangs (0 = noch nie geschafft). */
  stars: number;
  /** Laufender Durchgang: naechste Aufgabe und bisherige Fehler (Tipps zaehlen mit). */
  runIndex: number;
  runMistakes: number;
  /** Letzte Aenderung des Durchgangs (ms) — beim Abgleich mit dem Konto gewinnt der juengere. */
  runAt?: number;
}

/** Stand eines Kinderkurses. */
export interface CourseProgress {
  /** Geloeste Linien (Ids). */
  solved: number[];
  /** Wann jede Linie geloest wurde (ms); fehlt bei Linien von vor dem Konto-Abgleich. */
  solvedAt?: Record<string, number>;
  /** Zuletzt „von vorn" (ms). */
  resetAt?: number;
}

interface StoredProgress {
  levels: Record<string, LevelProgress>;
  courses: Record<string, CourseProgress>;
  /** Konto, dessen Stand hier liegt — `null`/fehlt: ohne Konto gespielt. */
  owner?: number | null;
}

/** Der Fortschritt in der Form des Servers (`KidsProgressDto`, `/api/kids/progress`). */
export interface KidsProgressDto {
  levels: { level: number; stars: number; runIndex: number; runMistakes: number; runAt: number }[];
  courses: { bookId: number; resetAt: number; solved: { id: number; at: number }[] }[];
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
 * Zwei Staende zusammenfuehren — Sterne nach Hoehe, der laufende Durchgang der juengere (Gleichstand:
 * der erste), Kurs-Linien vereinigt, aber nur nach dem juengsten „Von vorn".
 *
 * SPIEGEL von `KidsProgressMerge.Merge` (Server); dieselben LITERALEN Faelle in
 * `kids-progress.store.spec.ts` und `KidsProgressTests` — eine Seite aendern heisst beide aendern.
 */
export function mergeProgress(a: KidsProgressDto, b: KidsProgressDto): KidsProgressDto {
  const levels = new Map<number, KidsProgressDto['levels'][number]>();
  for (const l of [...a.levels, ...b.levels]) {
    const cur = levels.get(l.level);
    if (!cur) { levels.set(l.level, { ...l }); continue; }
    const run = l.runAt > cur.runAt ? l : cur;
    levels.set(l.level, {
      level: l.level, stars: Math.max(cur.stars, l.stars),
      runIndex: run.runIndex, runMistakes: run.runMistakes, runAt: run.runAt,
    });
  }

  const courses = new Map<number, { resetAt: number; lines: Map<number, number> }>();
  for (const c of [...a.courses, ...b.courses]) {
    const cur = courses.get(c.bookId) ?? { resetAt: 0, lines: new Map<number, number>() };
    for (const s of c.solved) cur.lines.set(s.id, Math.max(cur.lines.get(s.id) ?? s.at, s.at));
    cur.resetAt = Math.max(cur.resetAt, c.resetAt);
    courses.set(c.bookId, cur);
  }

  return {
    levels: [...levels.values()].sort((x, y) => x.level - y.level),
    courses: [...courses.entries()]
      .sort(([x], [y]) => x - y)
      .map(([bookId, c]) => ({
        bookId,
        resetAt: c.resetAt,
        solved: [...c.lines.entries()]
          .filter(([, at]) => at > c.resetAt)
          .sort(([x], [y]) => x - y)
          .map(([id, at]) => ({ id, at })),
      }))
      .filter(c => c.resetAt > 0 || c.solved.length > 0),
  };
}

/**
 * Der Fortschritt der Kinderseite im Browser (localStorage). Ohne Konto ist das alles; angemeldet
 * gleicht `KidsProgressSync` ihn mit dem Konto ab (`toDto` hinauf, `adopt` herunter). Faellt der
 * Speicher aus (privates Fenster, gesperrt), laeuft alles im Arbeitsspeicher weiter.
 */
@Injectable({ providedIn: 'root' })
export class KidsProgressStore {
  private readonly state = signal<StoredProgress>(this.load());
  private readonly changes = signal(0);

  /** Zaehlt jede Aenderung durch das Kind (nicht die Uebernahme aus dem Konto) — Ausloeser fuer den Abgleich. */
  readonly revision = this.changes.asReadonly();

  /**
   * Zahl der geschafften Stufen unter `levels` (die Leiter, wie der Server sie gerade liefert). Nur ueber die
   * aktuelle Leiter: der Stand behaelt auch Stufen, die ein Neuaufbau mit weniger Stufen nicht mehr kennt — ueber
   * alle gezaehlt stand auf der Startseite „38 von 36 Stufen geschafft" (Codereview 2026-09-29, F7-014).
   */
  completedOf(levels: readonly number[]): number {
    const stored = this.state().levels;
    return [...new Set(levels)].filter(l => (stored[l]?.stars ?? 0) > 0).length;
  }

  /** Gesammelte Sterne ueber alle Stufen. */
  readonly totalStars = computed(() =>
    Object.values(this.state().levels).reduce((sum, l) => sum + l.stars, 0));

  /** Konto, dem der Stand gehoert (`null` = ohne Konto gespielt). */
  owner(): number | null {
    return this.state().owner ?? null;
  }

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
    this.patchLevel(level, { ...cur, runIndex: cur.runIndex + 1, runMistakes: cur.runMistakes + mistakes, runAt: Date.now() });
  }

  /** Durchgang fertig: Sterne vergeben (die besten bleiben), Durchgang zuruecksetzen. */
  completeRun(level: number): number {
    const cur = this.level(level);
    const stars = starsFor(cur.runMistakes);
    this.patchLevel(level, { stars: Math.max(cur.stars, stars), runIndex: 0, runMistakes: 0, runAt: Date.now() });
    return stars;
  }

  /** Durchgang von vorn (die Sterne bleiben). */
  restartRun(level: number): void {
    const cur = this.level(level);
    this.patchLevel(level, { ...cur, runIndex: 0, runMistakes: 0, runAt: Date.now() });
  }

  course(bookId: number): CourseProgress {
    return this.state().courses[bookId] ?? { solved: [] };
  }

  recordCourseSolved(bookId: number, lineId: number): void {
    const cur = this.course(bookId);
    if (cur.solved.includes(lineId)) return;
    this.patchCourse(bookId, {
      ...cur, solved: [...cur.solved, lineId], solvedAt: { ...cur.solvedAt, [lineId]: Date.now() },
    });
  }

  resetCourse(bookId: number): void {
    this.patchCourse(bookId, { solved: [], solvedAt: {}, resetAt: Date.now() });
  }

  /** Der Stand in der Form des Servers. Linien ohne Zeit (von vor dem Abgleich) gelten als „irgendwann
   *  frueher" = 1 ms — mit 0 fielen sie durch „nach dem letzten Von vorn" (wie am Server). */
  toDto(): KidsProgressDto {
    const s = this.state();
    return {
      levels: Object.entries(s.levels).map(([level, l]) => ({
        level: Number(level), stars: l.stars, runIndex: l.runIndex, runMistakes: l.runMistakes, runAt: l.runAt ?? 0,
      })),
      courses: Object.entries(s.courses).map(([bookId, c]) => ({
        bookId: Number(bookId),
        resetAt: c.resetAt ?? 0,
        solved: c.solved.map(id => ({ id, at: Math.max(1, c.solvedAt?.[id] ?? 1) })),
      })),
    };
  }

  /**
   * Stand aus dem Konto uebernehmen — ZUSAMMENGEFUEHRT mit dem hiesigen, nicht ersetzt: waehrend die
   * Anfrage unterwegs war, kann das Kind weitergespielt haben. Zaehlt nicht als Aenderung.
   */
  adopt(dto: KidsProgressDto, owner: number): void {
    const merged = mergeProgress(this.toDto(), dto);
    const levels: Record<string, LevelProgress> = {};
    for (const l of merged.levels) {
      levels[l.level] = { stars: l.stars, runIndex: l.runIndex, runMistakes: l.runMistakes, runAt: l.runAt };
    }
    const courses: Record<string, CourseProgress> = {};
    for (const c of merged.courses) {
      courses[c.bookId] = {
        solved: c.solved.map(s => s.id),
        solvedAt: Object.fromEntries(c.solved.map(s => [s.id, s.at])),
        ...(c.resetAt > 0 ? { resetAt: c.resetAt } : {}),
      };
    }
    this.state.set({ levels, courses, owner });
    this.save();
  }

  /** Abgemeldet: der Stand gehoert dem Konto und bleibt dort — hier faengt das naechste Kind frisch an. */
  clear(): void {
    this.state.set({ levels: {}, courses: {}, owner: null });
    this.save();
  }

  private patchLevel(level: number, value: LevelProgress): void {
    this.state.update(s => ({ ...s, levels: { ...s.levels, [level]: value } }));
    this.changed();
  }

  private patchCourse(bookId: number, value: CourseProgress): void {
    this.state.update(s => ({ ...s, courses: { ...s.courses, [bookId]: value } }));
    this.changed();
  }

  private changed(): void {
    this.save();
    this.changes.update(n => n + 1);
  }

  private load(): StoredProgress {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as Partial<StoredProgress>;
        return { levels: parsed.levels ?? {}, courses: parsed.courses ?? {}, owner: parsed.owner ?? null };
      }
    } catch { /* kein Speicher oder kaputter Eintrag: frisch anfangen */ }
    return { levels: {}, courses: {}, owner: null };
  }

  private save(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.state()));
    } catch { /* Speicher voll/gesperrt: im Arbeitsspeicher weiter */ }
  }
}
