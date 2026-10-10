import { Injectable, signal } from '@angular/core';

/**
 * Ob — und wo — die Fußzeile mit den Rechtslinks gerade steht.
 *
 * <p>`none`: keine Fußzeile (KidHub, LeagueHub, ClubHub). `wide`: nur breiter als 768px (RookHub, am Handy ist sie
 * aus). `always`: immer (Turnierseite). Die Anmeldemaske zeigt ihre eigene Zeile „Datenschutz · Impressum" nur dort,
 * wo die Fußzeile sie nicht schon zeigt (UI-Review login-legal) — und erfährt es hier, statt die App zu raten.</p>
 */
export type FooterPresence = 'none' | 'wide' | 'always';

@Injectable({ providedIn: 'root' })
export class FooterPresenceService {
  readonly presence = signal<FooterPresence>('none');
}
