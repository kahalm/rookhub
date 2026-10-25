import type { Book } from '../../../core/admin.service';

/** Drei-Zustands-Filter: leer = egal, sonst ja/nein. */
export type TriFilter = '' | 'yes' | 'no';

/** Per-Spalten-Filter über dem Bücher-Grid (UND-verknüpft, zusätzlich zur globalen Suche). */
export interface BookFilters {
  name: string;
  kind: '' | 'Puzzle' | 'Study';
  difficulty: string;
  eloMin: number | null;
  eloMax: number | null;
  puzzlesMin: number | null;
  puzzlesMax: number | null;
  daily: TriFilter;
  random: TriFilter;
  blind: TriFilter;
  public: TriFilter;
  group: '' | 'none' | number;
}

/** Ein frischer, leerer Filtersatz (jeder Aufruf ein neues Objekt — die Felder hängen per ngModel daran). */
export function emptyBookFilters(): BookFilters {
  return {
    name: '',
    kind: '',
    difficulty: '',
    eloMin: null,
    eloMax: null,
    puzzlesMin: null,
    puzzlesMax: null,
    daily: '',
    random: '',
    blind: '',
    public: '',
    group: '',
  };
}

/**
 * Clientseitiger Filter über die bereits geladenen Bücher: globale Suche über Name/Dateiname/Besitzer/Tags
 * (case-insensitive) UND alle gesetzten Spaltenfilter. Rein — ändert weder `books` noch `f`.
 */
export function filterBooks(books: readonly Book[], search: string, f: BookFilters): Book[] {
  const q = search.trim().toLowerCase();
  const name = f.name.trim().toLowerCase();
  const diff = f.difficulty.trim().toLowerCase();
  const tri = (v: TriFilter, actual: boolean) => v === '' || (v === 'yes') === actual;
  return books.filter(b => {
    if (q && !((b.displayName ?? '').toLowerCase().includes(q) ||
               (b.fileName ?? '').toLowerCase().includes(q) ||
               (b.ownerName ?? '').toLowerCase().includes(q) ||
               (b.tags ?? '').toLowerCase().includes(q))) return false;
    if (name && !((b.displayName ?? '').toLowerCase().includes(name) ||
                  (b.fileName ?? '').toLowerCase().includes(name))) return false;
    if (f.kind && b.kind !== f.kind) return false;
    if (diff && !(b.difficulty ?? '').toLowerCase().includes(diff)) return false;
    if (f.eloMin != null && (b.minElo == null || b.minElo < f.eloMin)) return false;
    if (f.eloMax != null && (b.maxElo == null || b.maxElo > f.eloMax)) return false;
    if (f.puzzlesMin != null && b.puzzleCount < f.puzzlesMin) return false;
    if (f.puzzlesMax != null && b.puzzleCount > f.puzzlesMax) return false;
    if (!tri(f.daily, b.forDaily)) return false;
    if (!tri(f.random, b.forRandom)) return false;
    if (!tri(f.blind, b.forBlind)) return false;
    if (!tri(f.public, b.isPublic)) return false;
    if (f.group === 'none' && (b.accessGroupIds?.length ?? 0) > 0) return false;
    if (typeof f.group === 'number' && !(b.accessGroupIds ?? []).includes(f.group)) return false;
    return true;
  });
}

/** Ist mindestens ein Spaltenfilter gesetzt (die globale Suche zählt nicht)? */
export function hasActiveBookFilters(f: BookFilters): boolean {
  return !!(f.name || f.kind || f.difficulty || f.eloMin != null || f.eloMax != null ||
    f.puzzlesMin != null || f.puzzlesMax != null || f.daily || f.random || f.blind ||
    f.public || f.group !== '');
}
