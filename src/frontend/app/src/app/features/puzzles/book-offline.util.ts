import { BOOK_OFFLINE_PREFIX } from '../../core/offline.service';
import { BookPuzzleDto } from './puzzle.service';

/**
 * Offline-Cache ganzer Bücher (alle Puzzles eines Buchs) im localStorage, gekeyt per
 * Buch-Dateiname (stabil über Kurs-Liste UND Standalone-Buch-Puzzle hinweg).
 */
function bookKey(fileName: string): string {
  return BOOK_OFFLINE_PREFIX + encodeURIComponent(fileName);
}

export function saveBookOffline(fileName: string, puzzles: BookPuzzleDto[]): void {
  if (!fileName) return;
  try { localStorage.setItem(bookKey(fileName), JSON.stringify(puzzles ?? [])); } catch { /* ignore (Quota) */ }
}

export function getBookOffline(fileName: string): BookPuzzleDto[] | null {
  if (!fileName) return null;
  try {
    const raw = localStorage.getItem(bookKey(fileName));
    return raw ? (JSON.parse(raw) as BookPuzzleDto[]) : null;
  } catch { return null; }
}

export function hasBookOffline(fileName: string): boolean {
  try { return localStorage.getItem(bookKey(fileName)) != null; } catch { return false; }
}

export function removeBookOffline(fileName: string): void {
  try { localStorage.removeItem(bookKey(fileName)); } catch { /* ignore */ }
}

/** Sucht ein Puzzle nach Id über ALLE offline gespeicherten Bücher (für Offline-Direktaufruf). */
export function findCachedBookPuzzle(id: number): BookPuzzleDto | null {
  try {
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (!k || !k.startsWith(BOOK_OFFLINE_PREFIX)) continue;
      const arr = JSON.parse(localStorage.getItem(k) || '[]') as BookPuzzleDto[];
      const hit = arr.find(p => p.id === id);
      if (hit) return hit;
    }
  } catch { /* ignore */ }
  return null;
}

/** Dateinamen aller offline gespeicherten Bücher (für „bereits gespeichert"-Anzeige). */
export function cachedBookFileNames(): string[] {
  const out: string[] = [];
  try {
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith(BOOK_OFFLINE_PREFIX)) {
        try { out.push(decodeURIComponent(k.slice(BOOK_OFFLINE_PREFIX.length))); } catch { /* ignore */ }
      }
    }
  } catch { /* ignore */ }
  return out;
}
