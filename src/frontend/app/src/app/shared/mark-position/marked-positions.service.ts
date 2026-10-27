import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, of, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';

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
  /** 0.848.0: in einer Nebenvariante markiert — Halbzug der Partie, an dem sie abzweigt (dann ohne `ply`)… */
  variationFromPly?: number | null;
  /** …und ihre Züge bis zur Stellung (UCI, Leerzeichen). */
  variationUci?: string | null;
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
  /** 0.862.0: Schlagworte (feste Codes aus `mark-tags.ts` oder eigene Wörter) und Kommentar. */
  tags?: string[];
  comment?: string | null;
}

/** Schlagworte + Kommentar einer markierten Stellung (0.862.0). */
export interface MarkNotes {
  tags: string[];
  comment: string | null;
}

/** Stellungsschlüssel wie der Server: Brett, Seite am Zug, Rochade, en passant — ohne Zugzähler. */
export function positionKey(fen: string): string {
  return (fen ?? '').trim().split(/\s+/).slice(0, 4).join(' ');
}

/**
 * „+"-Markierungen besonders guter Stellungen (0.749.0). Hält die Schlüssel der eigenen Markierungen als Signal — einmal
 * geladen, danach optimistisch nachgeführt —, damit jeder „+"-Knopf (Partie-Analyse, Analysebrett, Fehler-Training)
 * sofort weiß, ob die Stellung auf dem Brett schon markiert ist.
 * Seit 0.847.0 auch ohne Anmeldung: der Server ordnet sie dann der anonymen Kennung (`X-Visitor-Id`) zu. Die Schlüssel
 * gelten je Aufrufer — wechselt das Konto (Anmelden/Abmelden), lädt der nächste Knopf neu.
 */
@Injectable({ providedIn: 'root' })
export class MarkedPositionsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  readonly keys = signal<ReadonlySet<string>>(new Set());
  /** Schlagworte + Kommentar je Stellungsschlüssel (0.862.0) — nur Stellungen, die welche haben. */
  readonly notes = signal<ReadonlyMap<string, MarkNotes>>(new Map());
  /** Für wen die Schlüssel geladen sind: Nutzer-Id oder `anon`; `null` = noch nicht. */
  private loadedFor: string | null = null;

  private who(): string { return String(this.auth.currentUser?.userId ?? 'anon'); }

  /** Einmal je Aufrufer die eigenen Markierungen holen (still im Fehlerfall — der Knopf zeigt dann „nicht markiert"). */
  ensureLoaded(): void {
    const who = this.who();
    if (this.loadedFor === who) return;
    this.loadedFor = who;
    this.keys.set(new Set());
    this.notes.set(new Map());
    this.list().subscribe({ error: () => { this.loadedFor = null; } });
  }

  /** Beim Anmelden: die ohne Anmeldung markierten Stellungen dieser Kennung ins Konto holen, danach neu laden. */
  claimSession(sessionId: string): Observable<{ claimed: number }> {
    return this.http.post<{ claimed: number }>('/api/positions/marked/claim-session', { sessionId }).pipe(
      tap(() => { this.loadedFor = null; this.ensureLoaded(); }),
    );
  }

  list(): Observable<MarkedPosition[]> {
    return this.http.get<MarkedPosition[]>('/api/positions/marked').pipe(
      tap(rows => {
        this.keys.set(new Set(rows.map(r => r.positionKey)));
        this.notes.set(new Map(rows.filter(r => r.tags?.length || r.comment)
          .map(r => [r.positionKey, { tags: r.tags ?? [], comment: r.comment ?? null }])));
      }),
    );
  }

  /** Schlagworte + Kommentar einer Stellung (leer, wenn keine). */
  notesOf(fen: string): MarkNotes {
    return this.notes().get(positionKey(fen)) ?? { tags: [], comment: null };
  }

  /** Schlagworte + Kommentar einer markierten Stellung setzen (ersetzt beide). */
  setNotes(fen: string, notes: MarkNotes): Observable<MarkedPosition> {
    return this.http.put<MarkedPosition>('/api/positions/marked/notes', { fen, ...notes }).pipe(
      tap(row => this.notes.update(m => {
        const next = new Map(m);
        next.set(positionKey(fen), { tags: row.tags ?? [], comment: row.comment ?? null });
        return next;
      })),
    );
  }

  /** Markieren (idempotent) — wie `toggle`, aber nie zurücknehmen. */
  mark(fen: string, origin: MarkOrigin): Observable<boolean> {
    return this.isMarked(fen) ? of(true) : this.toggle(fen, origin);
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
    if (!on) this.notes.update(m => { const next = new Map(m); next.delete(key); return next; });
    this.keys.update(s => {
      const next = new Set(s);
      if (on) next.add(key); else next.delete(key);
      return next;
    });
  }
}
