import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';

/** Woher die markierte Stellung stammt — alles optional (Analysebrett: nichts davon). */
export interface MarkOrigin {
  /** `analysis` = Partie-Analyse, `board` = Analysebrett, `mistake` = Fehler-Training. */
  context: 'analysis' | 'board' | 'mistake';
  savedGameId?: number | null;
  clubGameId?: number | null;
  shareToken?: string | null;
  /** Halbzüge bis zur Stellung (0 = Grundstellung). */
  ply?: number | null;
  /** Der bessere Zug (UCI), wenn bekannt — im Fehler-Training die Lösung. */
  bestUci?: string | null;
}

export interface MarkedPosition {
  id: number;
  fen: string;
  positionKey: string;
  context: MarkOrigin['context'];
  savedGameId?: number | null;
  clubGameId?: number | null;
  shareToken?: string | null;
  ply?: number | null;
  bestUci?: string | null;
  createdAt: string;
}

/** Stellungsschlüssel wie der Server: Brett, Seite am Zug, Rochade, en passant — ohne Zugzähler. */
export function positionKey(fen: string): string {
  return (fen ?? '').trim().split(/\s+/).slice(0, 4).join(' ');
}

/**
 * „+"-Markierungen besonders guter Stellungen (0.749.0). Hält die Schlüssel der eigenen Markierungen als Signal — einmal
 * geladen, danach optimistisch nachgeführt —, damit jeder „+"-Knopf (Partie-Analyse, Analysebrett, Fehler-Training)
 * sofort weiß, ob die Stellung auf dem Brett schon markiert ist.
 */
@Injectable({ providedIn: 'root' })
export class MarkedPositionsService {
  private readonly http = inject(HttpClient);
  readonly keys = signal<ReadonlySet<string>>(new Set());
  private loaded = false;

  /** Einmal je Sitzung die eigenen Markierungen holen (still im Fehlerfall — der Knopf zeigt dann „nicht markiert"). */
  ensureLoaded(): void {
    if (this.loaded) return;
    this.loaded = true;
    this.list().subscribe({ error: () => { this.loaded = false; } });
  }

  list(): Observable<MarkedPosition[]> {
    return this.http.get<MarkedPosition[]>('/api/positions/marked').pipe(
      tap(rows => this.keys.set(new Set(rows.map(r => r.positionKey)))),
    );
  }

  isMarked(fen: string): boolean { return this.keys().has(positionKey(fen)); }

  /** Markieren bzw. zurücknehmen; bei einem Fehler wird der Knopf zurückgedreht. Liefert den neuen Zustand. */
  toggle(fen: string, origin: MarkOrigin): Observable<boolean> {
    const key = positionKey(fen);
    const marked = this.keys().has(key);
    this.setKey(key, !marked);
    const req: Observable<unknown> = marked
      ? this.http.delete('/api/positions/marked', { params: { fen } })
      : this.http.post('/api/positions/marked', { fen, ...origin });
    return req.pipe(
      map(() => !marked),
      tap({ error: () => this.setKey(key, marked) }),
    );
  }

  private setKey(key: string, on: boolean): void {
    this.keys.update(s => {
      const next = new Set(s);
      if (on) next.add(key); else next.delete(key);
      return next;
    });
  }
}
