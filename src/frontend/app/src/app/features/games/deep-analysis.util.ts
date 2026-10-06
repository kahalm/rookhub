import type { AnalysisJob, AnalysisJobLive } from '../analysis/analysis-jobs.service';

/**
 * „Tiefe Analyse" (0.686.0, gewünscht 2026-10-06): Stockfish bis Tiefe 40, Lc0 bis 500 000 Knoten — und gezeigt wird
 * das neue Ergebnis erst, sobald es WEITER ist als das, was die Partie-Analyse schon hinterlegt hat; bis dahin stehen die
 * hinterlegten Linien da. Hier die reinen Regeln dazu.
 */

/** Eine Linie zum Anzeigen — hinterlegt (Partie-Analyse) oder aus dem laufenden Auftrag. */
export interface DeepLine { evalText: string; san: string; positive: boolean; }

/** Was die Partie-Analyse für die Stellung auf dem Brett schon hat. */
export interface DeepStored {
  sf: { depth: number; lines: DeepLine[] } | null;
  lc0: { nodes: number; lines: DeepLine[] } | null;
}

export type DeepKind = 'sf' | 'lc0';

/** Wie weit ein Auftrag ist: jetzt / Ziel als Tiefe (Stockfish) bzw. Knoten (Lc0), dazu Prozent (0..100). */
export interface DeepProgress { now: number; target: number; percent: number; }

/** Knoten der gespeicherten Ergebniszeile (Broker-Format `{"nodes": …}`), 0 ohne. */
export function resultNodes(job: AnalysisJob | null | undefined): number {
  if (!job?.resultJson) return 0;
  try { return Number((JSON.parse(job.resultJson) as { nodes?: number }).nodes) || 0; } catch { return 0; }
}

/**
 * Fortschritt: Stockfish nach Tiefe (die laufende, sonst die erreichte), Lc0 nach Knoten. Fertig = 100 % — auch ein
 * Lc0-Auftrag, der per Smart Pruning vor dem Ziel aufgehört hat (der Zug steht dann fest).
 */
export function deepProgress(kind: DeepKind, job: AnalysisJob | null, live: AnalysisJobLive | null, target: number): DeepProgress {
  if (!job) return { now: 0, target, percent: 0 };
  const now = kind === 'sf'
    ? Math.max(live?.depth ?? 0, job.reachedDepth ?? 0)
    : Math.max(live?.nodes ?? 0, resultNodes(job));
  const percent = job.status === 'done' ? 100 : Math.min(100, Math.max(0, target > 0 ? now / target * 100 : 0));
  return { now, target, percent };
}

/** Ist das neue Ergebnis schon weiter als das hinterlegte? Ohne Hinterlegtes: sobald es überhaupt eins gibt. */
export function deepAhead(kind: DeepKind, job: AnalysisJob | null, stored: DeepStored): boolean {
  if (!job?.resultJson) return false;
  if (kind === 'sf') return job.reachedDepth > (stored.sf?.depth ?? 0);
  return resultNodes(job) > (stored.lc0?.nodes ?? 0);
}

/** Lc0 hat vor dem Knotenziel von selbst aufgehört (Smart Pruning)? */
export function prunedEarly(job: AnalysisJob | null, target: number): boolean {
  return job?.status === 'done' && resultNodes(job) > 0 && resultNodes(job) < target;
}
