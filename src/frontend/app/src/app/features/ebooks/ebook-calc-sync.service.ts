import { DestroyRef, Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { localStore, readJson, readRaw, writeJson, writeRaw } from '../../core/local-json-store';
import { registerLogoutFlush } from '../../core/logout-flush';

/** Lokaler Baum je Diagramm (Zwischenspeicher; Konto-Spur, beim Abmelden geräumt). */
export const EBOOK_CALC_PREFIX = 'rh.ebooks.calc.';
/** Je Diagramm: Gerätezeitpunkt, Buch und ob der Stand noch nicht beim Server ist. */
export const EBOOK_CALC_META_KEY = 'rh.ebooks.calc.meta';
/** Hochladen gesammelt nach dieser Pause (jeder Zug speichert). */
export const EBOOK_CALC_DEBOUNCE_MS = 1500;

interface Meta { at: number; ebookId: number; dirty?: boolean; }
interface ServerCalc { tree: string; at: number; }

/**
 * Eigene Kalkulation am E-Book-Diagramm im Konto (1.019.0). Vorher nur im localStorage — der ist eine Konto-Spur und wird
 * beim Abmelden, beim Ablauf und beim Hub-Wechsel geräumt: die Analyse war weg.
 *
 * - Angemeldet: der Server gewinnt beim Öffnen, AUSSER das Gerät hat einen jüngeren, noch nicht hochgeladenen Stand —
 *   dann geht der hoch. Jede Änderung geht lokal sofort und zum Server gesammelt ({@link EBOOK_CALC_DEBOUNCE_MS}).
 * - Ungesicherte Stände gehen vor dem Abmelden mit dem Token der endenden Sitzung hoch (`core/logout-flush.ts`) und
 *   beim nächsten Start dieses Dienstes, falls das Netz fehlte.
 * - Gäste: nur lokal, wie bisher.
 */
@Injectable({ providedIn: 'root' })
export class EbookCalcSyncService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  constructor() {
    const unregister = registerLogoutFlush(token => this.flushDirty(token));
    inject(DestroyRef).onDestroy(() => { unregister(); for (const t of this.timers.values()) clearTimeout(t); });
    if (this.auth.isLoggedIn) this.flushDirty();
  }

  /** Der lokale Stand (roh, wie gespeichert). */
  local(diagramId: number): string | null {
    return readRaw(localStore(), EBOOK_CALC_PREFIX + diagramId);
  }

  /**
   * Den Server fragen (nur angemeldet): liefert den Baum, der jetzt gelten soll und NICHT schon lokal steht — oder
   * `null` (Gast, Fehler, kein Server-Stand, lokal gleich/jünger). Ein jüngerer ungesicherter Gerätestand wird dabei
   * hochgeladen.
   */
  fetch(ebookId: number, diagramId: number): Observable<string | null> {
    if (!this.auth.isLoggedIn || !ebookId) return of(null);
    return this.http.get<ServerCalc | null>(`/api/ebooks/${ebookId}/diagrams/${diagramId}/calc`).pipe(
      map(server => {
        const meta = this.meta()[diagramId];
        const local = this.local(diagramId);
        if (meta?.dirty && local && (!server || meta.at > server.at)) { this.upload(ebookId, diagramId); return null; }
        if (!server?.tree || server.tree === local) {
          if (server && meta) this.setMeta(diagramId, { at: server.at, ebookId, dirty: false });
          return null;
        }
        writeRaw(localStore(), EBOOK_CALC_PREFIX + diagramId, server.tree);
        this.setMeta(diagramId, { at: server.at, ebookId, dirty: false });
        return server.tree;
      }),
      catchError(() => of(null)),
    );
  }

  /** Neuer Stand: lokal sofort (`false` = Speicher voll), angemeldet gesammelt zum Server. */
  save(ebookId: number, diagramId: number, tree: string): boolean {
    const ok = writeRaw(localStore(), EBOOK_CALC_PREFIX + diagramId, tree);
    if (!this.auth.isLoggedIn || !ebookId) return ok;
    this.setMeta(diagramId, { at: Date.now(), ebookId, dirty: true });
    clearTimeout(this.timers.get(diagramId));
    this.timers.set(diagramId, setTimeout(() => { this.timers.delete(diagramId); this.upload(ebookId, diagramId); }, EBOOK_CALC_DEBOUNCE_MS));
    return ok;
  }

  private upload(ebookId: number, diagramId: number): void {
    const tree = this.local(diagramId);
    const meta = this.meta()[diagramId];
    if (!tree || !meta) return;
    this.http.put<ServerCalc>(`/api/ebooks/${ebookId}/diagrams/${diagramId}/calc`, { tree, at: meta.at }).pipe(
      catchError(() => of(null)),
    ).subscribe(r => {
      // Inzwischen weiter geändert? Dann bleibt der Stand ungesichert (der nächste Upload kommt).
      if (r && this.meta()[diagramId]?.at === meta.at) this.setMeta(diagramId, { ...meta, dirty: false });
    });
  }

  /** Alle ungesicherten Stände auf einmal (vor dem Abmelden mit `token`, sonst mit der laufenden Sitzung). */
  flushDirty(token?: string): void {
    const all = this.meta();
    const items = Object.entries(all)
      .filter(([, m]) => m.dirty)
      .map(([id, m]) => ({ diagramId: Number(id), tree: this.local(Number(id)), at: m.at }))
      .filter((i): i is { diagramId: number; tree: string; at: number } => !!i.tree)
      .slice(0, 50);
    if (!items.length) return;
    for (const t of this.timers.values()) clearTimeout(t);
    this.timers.clear();
    const options = token ? { headers: new HttpHeaders({ Authorization: `Bearer ${token}` }) } : {};
    this.http.post<{ saved: number }>('/api/ebooks/calc/bulk', items, options).pipe(catchError(() => of(null))).subscribe(r => {
      if (!r || token) return;   // beim Abmelden räumt gleich darauf das Gerät — nichts mehr nachzuführen
      const now = this.meta();
      for (const i of items) if (now[i.diagramId]?.at === i.at) now[i.diagramId] = { ...now[i.diagramId], dirty: false };
      writeJson(localStore(), EBOOK_CALC_META_KEY, now);
    });
  }

  private meta(): Record<number, Meta> {
    const m = readJson<Record<number, Meta>>(localStore(), EBOOK_CALC_META_KEY);
    return m && typeof m === 'object' && !Array.isArray(m) ? m : {};
  }

  private setMeta(diagramId: number, m: Meta): void {
    writeJson(localStore(), EBOOK_CALC_META_KEY, { ...this.meta(), [diagramId]: m });
  }
}
