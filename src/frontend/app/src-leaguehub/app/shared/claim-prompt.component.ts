import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ClubApiService } from '../core/club-api.service';
import { claimKeys, clearClaimKeys } from '../core/claim-keys';

/**
 * Rückfrage nach dem Anmelden (0.656.0): liegen in diesem Browser Schlüssel anonym hochgeladener Partien, fragt LeagueHub,
 * ob sie dem Konto zugeordnet werden sollen. Erst bei JA wird gespeichert, wer sie hochgeladen hat — bei anonymisierten Partien
 * ist das genau die Zustimmung, die das Häkchen beim Hochladen verspricht. NEIN lässt sie ohne Hochladenden; beide
 * Antworten verbrauchen die Schlüssel.
 */
@Component({
  selector: 'lh-claim-prompt',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    @if (offer(); as o) {
      <section class="claim" role="region" aria-label="Partien zuordnen">
        <p>In diesem Browser hast du ohne Anmeldung <b>{{ o.games }} {{ o.games === 1 ? 'Partie' : 'Partien' }}</b> hochgeladen@if (o.anonymized) {
          (davon {{ o.anonymized }} anonymisiert, mit dem Vereinsnamen statt des Spielers)}. Sollen sie deinem Konto zugeordnet werden?
          Dann findest du sie unter „Meine Partien“ und kannst sie jederzeit bearbeiten.@if (o.anonymized) {
          Bei den anonymisierten Partien wird damit gespeichert, dass sie von dir sind — angezeigt wird weiter der Vereinsname.}</p>
        <div class="actions">
          <button type="button" class="btn-pri" [disabled]="busy()" (click)="answer(true)">Ja, zuordnen</button>
          <button type="button" class="btn-sec" [disabled]="busy()" (click)="answer(false)">Nein</button>
        </div>
        @if (error()) { <p class="small err" role="status">{{ error() }}</p> }
      </section>
    } @else if (done(); as d) {
      <p class="claim-done" role="status">{{ d }}</p>
    }
  `,
})
export class ClaimPromptComponent {
  /** Angemeldeter Nutzer (Id) — wechselt er, wird neu gefragt. */
  readonly userId = input<number | null>(null);
  private readonly api = inject(ClubApiService).client(null);

  readonly offer = signal<{ games: number; anonymized: number } | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  constructor() {
    effect(() => {
      const uid = this.userId();
      untracked(() => {
        this.offer.set(null);
        if (uid != null) void this.check();
      });
    });
  }

  private async check(): Promise<void> {
    const keys = claimKeys();
    if (!keys.length) return;
    try {
      const p = await this.api.claimPreview(keys);
      if (p.games > 0) this.offer.set(p);
      else clearClaimKeys();   // nichts mehr offen (schon zugeordnet oder gelöscht)
    } catch { /* still: beim nächsten Laden wieder */ }
  }

  async answer(yes: boolean): Promise<void> {
    const keys = claimKeys();
    this.busy.set(true);
    this.error.set(null);
    try {
      if (yes) {
        const r = await this.api.claim(keys);
        this.done.set(`${r.claimed} ${r.claimed === 1 ? 'Partie ist' : 'Partien sind'} jetzt deinem Konto zugeordnet.`);
      } else {
        await this.api.forgetClaims(keys);
      }
      clearClaimKeys();
      this.offer.set(null);
    } catch {
      this.error.set('Hat nicht geklappt — versuch es bitte gleich noch einmal.');
    } finally {
      this.busy.set(false);
    }
  }
}
