import { Injectable } from '@angular/core';

/**
 * Persistiert die per-Gerät einstellbare Dashboard-Kachel-Anordnung (Reihenfolge + ausgeblendete
 * Kacheln) in localStorage — analog zu Theme (`rookhub_app_theme`) und Menü-Cache. Bewusst KEIN
 * Server-State: die Anordnung ist eine reine Anzeigepräferenz und darf je Gerät abweichen.
 *
 * Gespeichert wird `{ order, hidden }`:
 *  - `order`: die zuletzt gewählte Reihenfolge der Kachel-IDs (kann veraltete/künftige IDs enthalten;
 *    die Komponente gleicht gegen die aktuell bekannten Kacheln ab und hängt neue hinten an).
 *  - `hidden`: explizit ausgeblendete Kachel-IDs (Default: alle sichtbar).
 */
export interface DashboardLayout {
  order: string[];
  hidden: string[];
}

// v2: kuratierter Standard eingeführt (Puzzles/Weekly/Repertoires/Kurse/Trainingsziele/Bestenlisten
// sichtbar, Rest aus). Kein weiterer Bump, wenn eine neue Kachel default-versteckt eingeführt wird —
// die Komponente hängt neue IDs hinten an und lässt gespeicherte User-Layouts intakt.
const LAYOUT_KEY = 'rookhub_dashboard_layout_v2';

@Injectable({ providedIn: 'root' })
export class DashboardLayoutService {
  /** Geladene Anordnung; leere Defaults, wenn nie gespeichert / kaputt. */
  load(): DashboardLayout {
    try {
      const raw = localStorage.getItem(LAYOUT_KEY);
      if (!raw) return { order: [], hidden: [] };
      const parsed = JSON.parse(raw);
      const order = Array.isArray(parsed?.order) ? parsed.order.filter((x: unknown) => typeof x === 'string') : [];
      const hidden = Array.isArray(parsed?.hidden) ? parsed.hidden.filter((x: unknown) => typeof x === 'string') : [];
      return { order, hidden };
    } catch {
      return { order: [], hidden: [] };
    }
  }

  save(layout: DashboardLayout): void {
    try {
      localStorage.setItem(LAYOUT_KEY, JSON.stringify({ order: layout.order, hidden: layout.hidden }));
    } catch {
      /* Quota / Privatmodus — Anordnung bleibt dann nur für diese Sitzung bestehen. */
    }
  }

  /** Auf Werkseinstellung zurücksetzen (alle Kacheln sichtbar, Standardreihenfolge). */
  reset(): void {
    try { localStorage.removeItem(LAYOUT_KEY); } catch { /* ignore */ }
  }
}
