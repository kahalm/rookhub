import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { LEGAL_SITE } from '@rh/features/legal/legal-site';

/**
 * Sperrkarte der geschützten LeagueHub-Seiten (Codereview UX-033): sagt, mit welchem Konto man angemeldet ist und was
 * fehlt — und nennt den nächsten Schritt: Freischaltung beim Betreiber anfragen (Kontaktadresse aus LEGAL_SITE),
 * mit einem anderen Konto anmelden (`/login?switch=1`, ohne vorher abzumelden — siehe guestGuard), optional zurück zu
 * einer Seite, die das Konto schon sehen darf, und zu RookHub (dasselbe Konto). Früher stand dort nur ein Satz.
 */
@Component({
  selector: 'lh-access-gate',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <section class="gate">
      <h2>Nicht freigeschaltet</h2>
      <p>Angemeldet als <b>{{ username }}</b>. {{ text }}</p>
      <p>Freischalten kann dich ein Admin von LeagueHub. Ist das das falsche Konto, melde dich mit dem richtigen an.</p>
      <div class="actions">
        <a class="btn-sec" [href]="requestHref">Freischaltung anfragen</a>
        <a routerLink="/login" [queryParams]="{ switch: 1, returnUrl: returnUrl }">Mit anderem Konto anmelden</a>
        @if (back) { <a [routerLink]="back.link">{{ back.label }}</a> }
        @if (rookHub) { <a [href]="rookHub">Zu RookHub</a> }
      </div>
    </section>
  `,
})
export class AccessGateComponent {
  /** Wer die Seite sehen bzw. die Aktion ausführen darf — oder, bei Lesern ohne Beitragsrecht, was noch fehlt. */
  @Input({ required: true }) text = '';
  /** Wofür die Freischaltung gebraucht wird (Betreff/Text der Anfrage). */
  @Input() purpose = 'LeagueHub';
  /** Eine Seite, die das Konto schon sehen darf (z. B. die Vereinspartien für Leser). */
  @Input() back: { link: string; label: string } | null = null;

  readonly username = inject(AuthService).currentUser?.username ?? '';
  /** Nach dem Kontowechsel zurück hierher. */
  readonly returnUrl = inject(Router).url;
  readonly rookHub = rookHubUrlForLeagueHub();
  private readonly contact = inject(LEGAL_SITE).contactEmail;

  get requestHref(): string {
    const subject = `LeagueHub-Freischaltung: ${this.username}`;
    const body = `Hallo,\n\nbitte schalte mein RookHub-Konto „${this.username}“ für ${this.purpose} frei.\n\nVerein / Name: `;
    return `mailto:${this.contact}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
  }
}
