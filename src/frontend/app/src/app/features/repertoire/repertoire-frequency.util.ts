import { ExplorerDatabase, ExplorerSettings, ExplorerSource } from './repertoire-explorer.service';

/**
 * Häufigkeiten der Repertoire-Stellungen aus der LETZTEN Lochsuche, je Repertoire auf dem Gerät
 * gemerkt — der Repertoire-Baum zeigt sie hinter jedem Zug. Gespeichert wird, womit gerechnet
 * wurde (Quelle, Datenbank, Elo, Tempo) und wann: eine Zahl ohne diese Angabe ist nicht lesbar.
 */
export interface StoredFrequencies {
  savedAt: string;
  source: ExplorerSource;
  database: ExplorerDatabase;
  ratings: number[];
  speeds: string[];
  /** War die Suche vollständig? Sonst fehlen tiefe Stellungen. */
  complete: boolean;
  /** Stellung (erste drei FEN-Felder, wie `normalizeFen`) → Häufigkeit 0…1. */
  positions: Record<string, number>;
}

/** Deckel je Repertoire: die häufigsten Stellungen — ein großer Kurs hätte sonst Megabytes im Speicher. */
export const MAX_STORED_POSITIONS = 4000;

const KEY = (repertoireId: number) => `rookhub_rep_freq_${repertoireId}`;

/** Merken (auf die häufigsten {@link MAX_STORED_POSITIONS} gekürzt). `false`, wenn der Speicher nicht mitspielt. */
export function saveRepertoireFrequencies(repertoireId: number, f: StoredFrequencies): boolean {
  const top = Object.entries(f.positions)
    .sort((a, b) => b[1] - a[1])
    .slice(0, MAX_STORED_POSITIONS)
    .map(([k, v]) => [k, Number(v.toPrecision(4))] as const);
  try {
    localStorage.setItem(KEY(repertoireId), JSON.stringify({ ...f, positions: Object.fromEntries(top) }));
    return true;
  } catch {
    return false;
  }
}

export function readRepertoireFrequencies(repertoireId: number): StoredFrequencies | null {
  try {
    const raw = localStorage.getItem(KEY(repertoireId));
    if (!raw) return null;
    const f = JSON.parse(raw) as StoredFrequencies;
    return f && typeof f.positions === 'object' && typeof f.savedAt === 'string' ? f : null;
  } catch {
    return null;
  }
}

/** Stammen die gemerkten Zahlen aus derselben Auswahl? Nur dann darf man sie ergänzen. */
export function sameSelection(f: StoredFrequencies, s: ExplorerSettings): boolean {
  if (f.source !== s.source || f.database !== s.database) return false;
  return f.database === 'masters'
    || (f.ratings.join(',') === s.ratings.join(',') && f.speeds.join(',') === s.speeds.join(','));
}

/**
 * Neue Zahlen dazunehmen (Baum beim Durchklicken). Aus derselben Auswahl wird ergänzt, aus einer
 * anderen beginnt ein neuer Satz — Prozente aus Meister- und Lichess-Partien nebeneinander wären
 * nicht vergleichbar.
 */
export function mergeFrequencies(
  f: StoredFrequencies | null, s: ExplorerSettings, positions: Record<string, number>, now = new Date(),
): StoredFrequencies {
  const base: StoredFrequencies = f && sameSelection(f, s)
    ? f
    : { savedAt: now.toISOString(), source: s.source, database: s.database, ratings: s.ratings, speeds: s.speeds,
        complete: true, positions: {} };
  return { ...base, savedAt: now.toISOString(), positions: { ...base.positions, ...positions } };
}
