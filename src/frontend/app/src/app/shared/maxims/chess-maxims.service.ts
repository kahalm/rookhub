import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import {
  BehaviorSubject, Observable, catchError, combineLatest, defer, distinctUntilChanged, filter, map, of, shareReplay,
  startWith, switchMap, tap,
} from 'rxjs';
import { AuthService } from '../../core/auth.service';

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

/** Stand der Merksätze: `undefined` = lädt, `'error'` = Abruf gescheitert, sonst die Liste. */
export type ChessMaximsState = ChessMaxim[] | 'error' | undefined;

/**
 * Die kuratierten Merksätze. Die Antwort hängt am Aufrufer (`accessible` je Buchstelle nach den E-Book-Rechten), deshalb
 * wird je Konto geladen: An-/Abmelden oder ein Kontowechsel holt die Liste neu (Codereview F3-003). Ein gescheiterter
 * Abruf wird nicht behalten — das nächste Abonnieren (oder `retry()`) fragt erneut. Die (i)-Hinweise nehmen `all$`
 * (ohne Netz still keine Buchstellen), die Seite `/maxims` nimmt `state$` und zeigt den Fehler.
 */
@Injectable({ providedIn: 'root' })
export class ChessMaximsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly reload$ = new BehaviorSubject<void>(undefined);
  private failed = false;

  private readonly shared$: Observable<ChessMaximsState> = combineLatest([
    this.auth.currentUser$.pipe(map(u => u?.userId ?? null), distinctUntilChanged()),
    this.reload$,
  ]).pipe(
    switchMap(() => this.http.get<ChessMaxim[]>('/api/maxims').pipe(
      tap(() => this.failed = false),
      catchError(() => { this.failed = true; return of('error' as const); }),
      startWith(undefined))),
    shareReplay(1));

  /** Liste mit Lade- und Fehlerzustand; nach einem Fehler lädt jedes neue Abonnieren erneut. */
  readonly state$: Observable<ChessMaximsState> = defer(() => {
    if (this.failed) this.retry();
    return this.shared$;
  });

  /** Nur die Liste (Fehler = leer, Laden wird übersprungen) — für die (i)-Hinweise in Erklärungen. */
  readonly all$: Observable<ChessMaxim[]> = this.state$.pipe(
    filter((s): s is ChessMaxim[] | 'error' => s !== undefined),
    map(s => s === 'error' ? [] : s));

  /** Erneut laden (Knopf „Erneut versuchen“). */
  retry(): void {
    this.failed = false;
    this.reload$.next();
  }

  /** Der Merksatz mit genau diesem Wortlaut, deutsch oder englisch (die Erklärung trägt den festen Text, nicht die Kennung). */
  static byText(all: readonly ChessMaxim[], text: string): ChessMaxim | null {
    const norm = (s: string) => s.trim().replace(/[.„“"]+$/g, '').replace(/^[„"]/, '');
    return all.find(m => m.id === text.trim() || norm(m.text) === norm(text) || (!!m.textEn && norm(m.textEn) === norm(text))) ?? null;
  }
}
