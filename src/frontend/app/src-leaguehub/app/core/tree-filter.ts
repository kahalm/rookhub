import { TreeFilter, TreeSource } from './league.models';

/** Gemerkte Auswahl des Eröffnungsbaums (je Gerät, für alle Spieler). */
export const TREE_FILTER_KEY = 'lh-tree-filter';

/** Tempo-Klassen der Online-Partien — SPIEGEL von `LeagueOnlineSync.Speeds` (Kürzel + Anzeige). */
export const TREE_SPEEDS: { key: string; label: string }[] = [
  { key: 'bullet', label: 'Bullet' },
  { key: 'blitz', label: 'Blitz' },
  { key: 'rapid', label: 'Schnell' },
  { key: 'classical', label: 'Klassisch' },
  { key: 'correspondence', label: 'Fernschach' },
];

/** Auswahl „nur die letzten x Jahre". */
export const TREE_YEARS = [1, 2, 3, 5, 10];

/** Vorgabe: wie vor 0.605.0 nur die Brettpartien — Online-Partien erst, wenn man sie dazunimmt. */
export const DEFAULT_TREE_FILTER: TreeFilter = { source: 'board', speeds: [], years: null, onlySure: false };

const SOURCES: TreeSource[] = ['board', 'both', 'online'];

/** Gespeicherte (oder fremde) Angabe → gültiger Filter; Unbekanntes fällt auf die Vorgabe zurück. */
export function normalizeTreeFilter(raw: unknown): TreeFilter {
  const o = (raw && typeof raw === 'object' ? raw : {}) as Partial<Record<keyof TreeFilter, unknown>>;
  const source = SOURCES.includes(o.source as TreeSource) ? o.source as TreeSource : DEFAULT_TREE_FILTER.source;
  const speeds = Array.isArray(o.speeds)
    ? TREE_SPEEDS.map(s => s.key).filter(k => (o.speeds as unknown[]).includes(k))
    : [];
  const years = typeof o.years === 'number' && TREE_YEARS.includes(o.years) ? o.years : null;
  return { source, speeds, years, onlySure: o.onlySure === true };
}

/**
 * Was wirklich gefragt wird: ohne Online-Partien gibt es nur das Brett, ohne Brettpartien (nur Online-Konten) zeigt „nur
 * Brett" nichts — dann gleich online.
 */
export function effectiveTreeFilter(f: TreeFilter, boardGames: number, onlineGames: number): TreeFilter {
  if (onlineGames <= 0) return { ...f, source: 'board' };
  if (boardGames <= 0 && f.source === 'board') return { ...f, source: 'online' };
  return f;
}

/** Ein Tempo an- oder abwählen. Leer heißt „alle"; wer alle einzeln wählt, landet wieder bei „alle". */
export function toggleSpeed(speeds: readonly string[], key: string): string[] {
  const next = speeds.includes(key) ? speeds.filter(s => s !== key) : [...speeds, key];
  const ordered = TREE_SPEEDS.map(s => s.key).filter(k => next.includes(k));
  return ordered.length === TREE_SPEEDS.length ? [] : ordered;
}
