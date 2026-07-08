import { LineStateDto } from './repertoire-training.service';

/**
 * Reine SR-Pool-Orchestrierung für den Repertoire-Trainer: Fälligkeit, Queue-Bau-Bausteine und
 * das relative Fälligkeits-Label. Bewusst zustandslos (arbeitet über plain `LineStateDto`), damit
 * die Fälligkeits-/Reihenfolge-Logik unabhängig von Timern, HTTP und Signals testbar bleibt.
 * Die Zuordnung Linie → Zustand (via lineKey) bleibt in der Komponente.
 */

/** Fällig = im Pool, nicht pausiert und DueAt ≤ jetzt. Noch nicht gelernte Linien (kein Zustand)
 * sind NICHT im Pool und damit nicht fällig. */
export function isStateDue(st: LineStateDto | undefined, now: number): boolean {
  return !!st && st.inPool && !st.paused && new Date(st.dueAt).getTime() <= now;
}

/** Learn-Kandidat = noch NICHT im Pool und nicht pausiert. */
export function isStateLearnable(st: LineStateDto | undefined): boolean {
  return (!st || !st.inPool) && !st?.paused;
}

/** Früheste künftige Fälligkeit unter den (Pool-)Zuständen als ISO-String; null = nichts im Pool.
 * Pausierte und nicht-im-Pool-Zustände zählen nicht. */
export function earliestDueIso(states: Iterable<LineStateDto | undefined>): string | null {
  let min: number | null = null;
  for (const st of states) {
    if (!st || !st.inPool || st.paused) continue;
    const t = new Date(st.dueAt).getTime();
    if (min === null || t < min) min = t;
  }
  return min === null ? null : new Date(min).toISOString();
}

/** Kompakte Restzeit bis zu einem ISO-Zeitpunkt, z. B. „4 h", „3 d", „2 w", „< 1 h". */
export function relDueLabel(iso: string | null, now: number = Date.now()): string {
  if (!iso) return '';
  const ms = new Date(iso).getTime() - now;
  const h = ms / 3_600_000;
  if (h < 1) return '< 1 h';
  if (h < 48) return `${Math.round(h)} h`;
  const d = h / 24;
  if (d < 14) return `${Math.round(d)} d`;
  const w = d / 7;
  if (w < 9) return `${Math.round(w)} w`;
  return `${Math.round(d / 30)} mo`;
}

/** Fisher–Yates in-place-Kopie. Reihenfolge der fälligen Trainings-Linien wird pro Session gemischt. */
export function shuffle<T>(arr: readonly T[]): T[] {
  const out = arr.slice();
  for (let i = out.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [out[i], out[j]] = [out[j], out[i]];
  }
  return out;
}
