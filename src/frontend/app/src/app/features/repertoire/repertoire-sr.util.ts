import { LineStateDto, SrLevel } from './repertoire-training.service';

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

/** Eingebaute Standard-Intervalle der 9 Stufen — MUSS `RepertoireTrainingService.DefaultLevels`
 * (Backend) spiegeln; Fallback für die Offline-Bewertung, wenn keine Config gecacht ist. */
export const DEFAULT_SR_LEVELS: SrLevel[] = [
  { value: 4, unit: 'h' }, { value: 10, unit: 'h' }, { value: 24, unit: 'h' },
  { value: 2.5, unit: 'd' }, { value: 1, unit: 'w' }, { value: 2.5, unit: 'w' },
  { value: 1.5, unit: 'mo' }, { value: 3, unit: 'mo' }, { value: 6, unit: 'mo' },
];

/** Stunden eines Intervall-Eintrags (Spiegel von Backend `HoursOf`; mo = 30 Tage). */
export function hoursOfLevel(l: SrLevel): number {
  switch (l.unit) {
    case 'h': return l.value;
    case 'd': return l.value * 24;
    case 'w': return l.value * 24 * 7;
    default: return l.value * 24 * 30;
  }
}

/**
 * Lokale (Offline-)SR-Bewertung einer Linie — Spiegel von Backend `ScheduleLevel`:
 * richtig → Stufe+1 (max 9, Reps+1), falsch → Stufe 1 (Lapses+1); DueAt = jetzt + Intervall der
 * neuen Stufe. Liefert den NEUEN Zustand (Eingabe bleibt unverändert); `prev` fehlt bei einer
 * noch nie bewerteten Linie (Server legt die Karte beim Replay ebenfalls neu an).
 */
export function applySrReview(prev: LineStateDto | undefined, lineKey: string, correct: boolean,
                              levels: readonly SrLevel[], now: number): LineStateDto {
  const eff = levels.length ? levels : DEFAULT_SR_LEVELS;
  const level = correct ? Math.min(Math.max(prev?.level ?? 0, 0) + 1, 9) : 1;
  const idx = Math.min(Math.max(level - 1, 0), eff.length - 1);
  return {
    lineKey,
    level,
    reps: (prev?.reps ?? 0) + (correct ? 1 : 0),
    lapses: (prev?.lapses ?? 0) + (correct ? 0 : 1),
    dueAt: new Date(now + hoursOfLevel(eff[idx]) * 3_600_000).toISOString(),
    lastReviewedAt: new Date(now).toISOString(),
    inPool: true,
    paused: false,
  };
}

/** Lokale (Offline-)Pool-Aufnahme einer Linie — Spiegel von Backend `PromoteAsync`:
 * inPool=true, Pause aufgehoben, sofort fällig; Stufe/Zähler bleiben (bzw. starten bei 0). */
export function applyPromote(prev: LineStateDto | undefined, lineKey: string, now: number): LineStateDto {
  return {
    lineKey,
    level: prev?.level ?? 0,
    reps: prev?.reps ?? 0,
    lapses: prev?.lapses ?? 0,
    dueAt: new Date(now).toISOString(),
    lastReviewedAt: prev?.lastReviewedAt ?? null,
    inPool: true,
    paused: false,
  };
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
