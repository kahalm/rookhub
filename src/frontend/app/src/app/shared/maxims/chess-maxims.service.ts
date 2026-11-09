import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, of, shareReplay } from 'rxjs';

/** Eine Buchstelle zu einem Merksatz (0.917.0): PDF-Seite (`page`) bzw. Anteil im EPUB (`percent`), Zitat. */
export interface ChessMaximSource {
  ebookId: number; author?: string | null; title?: string | null; year?: string | null;
  location?: string | null; page?: number | null; percent?: number | null; quote?: string | null;
  /** Darf der Aufrufer das Buch öffnen? Nur dann gibt es einen Link (Regel der Bibliothek, am Server geprüft). */
  accessible?: boolean;
}

/** Ein Merksatz der kuratierten Liste (`GET /api/maxims`). */
export interface ChessMaxim {
  id: string; text: string; category: string; when: string; sources: ChessMaximSource[];
  /** Englische Fassung (Anzeige in jeder Sprache außer Deutsch); `text` bleibt der Schlüssel der gespeicherten Erklärungen. */
  textEn?: string | null; categoryEn?: string | null; whenEn?: string | null;
}

/** Der Merksatz in der Oberflächensprache: Deutsch nur bei `de`, sonst Englisch als Rückfall für alle weiteren Sprachen. */
export function localizeMaxim(m: ChessMaxim, lang: string | null | undefined): ChessMaxim {
  if (/^de\b/i.test(lang ?? '')) return m;
  return { ...m, text: m.textEn || m.text, category: m.categoryEn || m.category, when: m.whenEn || m.when };
}

/** Sprung in den Leser: PDF-Seite (`fmt=pdf` — sonst öffnet das bevorzugte EPUB ohne Seiten) bzw. Anteil im EPUB. */
export function readerJump(s: ChessMaximSource): Record<string, string | number> {
  if (s.page) return { page: s.page, fmt: 'pdf' };
  if (s.percent) return { pct: s.percent };
  return {};
}

/** Präfix der Merksatz-Zeile am Ende einer Erklärung (wie `ChessMaxims.LinePrefix` am Server). */
export const MAXIM_PREFIX = 'Merksatz:';

/** Trennt die Merksatz-Zeile vom Rest der Erklärung. */
export function splitMaxim(text: string): { body: string; maxim: string | null } {
  const m = /\n*[ \t]*Merksatz:[ \t]*(.+?)\s*$/.exec(text);
  return m ? { body: text.slice(0, m.index).trimEnd(), maxim: m[1].trim() } : { body: text, maxim: null };
}

/** Die kuratierten Merksätze — einmal je Sitzung geladen; ohne Netz einfach keine Buchstellen (still). */
@Injectable({ providedIn: 'root' })
export class ChessMaximsService {
  private readonly http = inject(HttpClient);
  readonly all$: Observable<ChessMaxim[]> = this.http.get<ChessMaxim[]>('/api/maxims').pipe(
    catchError(() => of([] as ChessMaxim[])), shareReplay(1));

  /** Der Merksatz mit genau diesem Wortlaut, deutsch oder englisch (die Erklärung trägt den festen Text, nicht die Kennung). */
  static byText(all: readonly ChessMaxim[], text: string): ChessMaxim | null {
    const norm = (s: string) => s.trim().replace(/[.„“"]+$/g, '').replace(/^[„"]/, '');
    return all.find(m => m.id === text.trim() || norm(m.text) === norm(text) || (!!m.textEn && norm(m.textEn) === norm(text))) ?? null;
  }
}
