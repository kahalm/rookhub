import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { hasKey, keysWithPrefix, localStore, readJson, removeKey, writeJson } from '../../core/local-json-store';
import { Repertoire } from '../../core/models';
import { LineStateDto, SrLevel } from './repertoire-training.service';

/**
 * Offline-Cache heruntergeladener Repertoires im localStorage, gekeyt per Repertoire-Id.
 * Ein Eintrag hält alles, was der Trainer offline braucht: kombiniertes PGN, SR-Linien-Zustände
 * und die effektiven Intervalle (für die lokale Fälligkeits-Berechnung); dazu die Listen-Metadaten
 * für den Offline-Fallback der /repertoires-Seite.
 *
 * Die localStorage-Mechanik liegt in `core/local-json-store.ts` — hier steht nur das Fachliche
 * (Form-Prüfung beim Lesen, Sortierung der Liste, Auffrischen einer bestehenden Kopie).
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
  return writeJson(localStore(), repKey(entry.meta.id), entry);
}

export function getRepertoireOffline(id: number): OfflineRepertoire | null {
  const entry = readJson<OfflineRepertoire>(localStore(), repKey(id));
  return entry?.meta && typeof entry.pgn === 'string' ? entry : null;
}

export function hasRepertoireOffline(id: number): boolean {
  return hasKey(localStore(), repKey(id));
}

export function removeRepertoireOffline(id: number): void {
  removeKey(localStore(), repKey(id));
}

/** Metadaten aller heruntergeladenen Repertoires (für den Offline-Fallback der Liste). */
export function cachedRepertoires(): Repertoire[] {
  const out: Repertoire[] = [];
  for (const k of keysWithPrefix(localStore(), REPERTOIRE_OFFLINE_PREFIX)) {
    // Korrupter Eintrag → null → überspringen.
    const entry = readJson<OfflineRepertoire>(localStore(), k);
    if (entry?.meta?.id) out.push(entry.meta);
  }
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
