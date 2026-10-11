import { Injectable, inject, signal } from '@angular/core';
import { ClubApiService } from './club-api.service';
import { LinkState } from './club.models';

/**
 * UI-Sweep 2026-10-11 (r2-c-gate-deadend, r2-c-footer-link): der Verknüpfungsstand des angemeldeten Kontos EINMAL für
 * die ganze Seite — die Sperrkarte (`ch-gate`) und der Fußzeilenlink lesen ihn, „Konto verknüpfen" schreibt ihn nach
 * dem Einlösen/Trennen zurück. Nur für Konten ohne Club-Recht abgefragt; `clear()` beim Abmelden.
 */
@Injectable({ providedIn: 'root' })
export class LinkStateStore {
  private readonly api = inject(ClubApiService);
  readonly state = signal<LinkState | null>(null);
  private pending: Promise<void> | null = null;

  /** Holt den Stand, falls noch keiner da ist; ein Fehler gilt als „nicht verknüpft" (die Seite bleibt bedienbar). */
  ensure(): Promise<void> {
    if (this.state()) return Promise.resolve();
    this.pending ??= Promise.resolve()
      .then(() => this.api.linkState())
      .then(s => this.state.set(s), () => this.state.set({ linked: false }))
      .finally(() => (this.pending = null));
    return this.pending;
  }

  set(s: LinkState): void {
    this.state.set(s);
  }

  clear(): void {
    this.state.set(null);
  }
}
