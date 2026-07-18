import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { Repertoire } from '../../core/models';
import { LineStateDto, SrLevel } from './repertoire-training.service';

/**
 * Offline-Cache heruntergeladener Repertoires im localStorage, gekeyt per Repertoire-Id.
 * Ein Eintrag hält alles, was der Trainer offline braucht: kombiniertes PGN, SR-Linien-Zustände
 * und die effektiven Intervalle (für die lokale Fälligkeits-Berechnung); dazu die Listen-Metadaten
 * für den Offline-Fallback der /repertoires-Seite.
 */
export interface OfflineRepertoire {
  meta: Repertoire;
  pgn: string;
  states: LineStateDto[];
  /** Effektive SR-Intervalle zum Download-Zeitpunkt; null → Client-Defaults. */
  config: SrLevel[] | null;
  savedAt: string;
}

function repKey(id: number): string {
  return REPERTOIRE_OFFLINE_PREFIX + id;
}

/** Speichert/überschreibt die Offline-Kopie. false bei Quota-Fehler (nichts gespeichert). */
export function saveRepertoireOffline(entry: OfflineRepertoire): boolean {
  if (!entry?.meta?.id) return false;
  try { localStorage.setItem(repKey(entry.meta.id), JSON.stringify(entry)); return true; }
  catch { return false; }
}

export function getRepertoireOffline(id: number): OfflineRepertoire | null {
  try {
    const raw = localStorage.getItem(repKey(id));
    const entry = raw ? (JSON.parse(raw) as OfflineRepertoire) : null;
    return entry?.meta && typeof entry.pgn === 'string' ? entry : null;
  } catch { return null; }
}

export function hasRepertoireOffline(id: number): boolean {
  try { return localStorage.getItem(repKey(id)) != null; } catch { return false; }
}

export function removeRepertoireOffline(id: number): void {
  try { localStorage.removeItem(repKey(id)); } catch { /* ignore */ }
}

/** Metadaten aller heruntergeladenen Repertoires (für den Offline-Fallback der Liste). */
export function cachedRepertoires(): Repertoire[] {
  const out: Repertoire[] = [];
  try {
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (!k || !k.startsWith(REPERTOIRE_OFFLINE_PREFIX)) continue;
      try {
        const entry = JSON.parse(localStorage.getItem(k) || 'null') as OfflineRepertoire | null;
        if (entry?.meta?.id) out.push(entry.meta);
      } catch { /* korrupter Eintrag → überspringen */ }
    }
  } catch { /* ignore */ }
  return out.sort((a, b) => (a.name || '').localeCompare(b.name || ''));
}

/** PGN + SR-Zustände einer bestehenden Offline-Kopie auffrischen (Meta/Config bleiben).
 * No-op, wenn das Repertoire nicht heruntergeladen ist. */
export function refreshRepertoireOffline(id: number, pgn: string, states: LineStateDto[]): void {
  const entry = getRepertoireOffline(id);
  if (!entry) return;
  saveRepertoireOffline({ ...entry, pgn, states, savedAt: new Date().toISOString() });
}

/** Nur die SR-Zustände einer bestehenden Offline-Kopie ersetzen (nach lokaler Bewertung).
 * No-op, wenn das Repertoire nicht heruntergeladen ist. */
export function updateRepertoireOfflineStates(id: number, states: LineStateDto[]): void {
  const entry = getRepertoireOffline(id);
  if (!entry) return;
  saveRepertoireOffline({ ...entry, states, savedAt: entry.savedAt });
}
