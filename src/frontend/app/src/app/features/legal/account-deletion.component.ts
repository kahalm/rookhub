import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { HandoffService } from '../../core/handoff.service';
import { ACCOUNT_DELETE_QUERY, ACCOUNT_DELETE_ROUTE, LEGAL_SITE, legalBack } from './legal-site';

/**
 * Öffentlich (ohne Login) erreichbare Info-Seite zur Konto-Löschung — erfüllt die
 * Google-Play-Anforderung einer öffentlich zugänglichen URL zur Löschanforderung.
 * Route: /account-deletion
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-account-deletion',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, RouterModule, TranslatePipe],
  template: `
    <div class="legal-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ 'legal.accountDeletion.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <p>{{ (home ? 'legal.accountDeletion.introPartner' : 'legal.accountDeletion.intro') | translate }}</p>

          <h4>{{ 'legal.accountDeletion.inAppTitle' | translate }}</h4>
          @if (!home) {
            <!-- Der eine Knopf zur Karte im Profil; abgemeldet fuehrt der authGuard ueber die Anmeldung dorthin
                 (data-login-required: die Routen-Specs lassen diesen Link deshalb eine Anmeldung verlangen). -->
            <p>{{ 'legal.accountDeletion.inApp' | translate }}</p>
            <p class="action">
              <a mat-flat-button color="warn" class="delete-now" data-login-required
                 [routerLink]="deleteRoute" [queryParams]="deleteQuery">{{ 'legal.accountDeletion.deleteNow' | translate }}</a>
            </p>
          } @else {
            <!-- KidHub, LeagueHub, ClubHub, Turnierseite: dasselbe Konto, geloescht wird es in RookHub. -->
            <p>{{ 'legal.accountDeletion.inPartner' | translate }}</p>
            @if (homeDeleteUrl) {
              <p class="action">
                <a mat-flat-button color="warn" class="delete-now" [href]="homeDeleteUrl"
                   (click)="openOnHome($event)">{{ 'legal.accountDeletion.deleteOnRookHub' | translate }}</a>
              </p>
            }
          }

          <h4>{{ 'legal.accountDeletion.removedTitle' | translate }}</h4>
          <ul>
            <li>{{ 'legal.accountDeletion.removed1' | translate }}</li>
            <li>{{ 'legal.accountDeletion.removed2' | translate }}</li>
          </ul>

          <h4>{{ 'legal.accountDeletion.keptTitle' | translate }}</h4>
          <p>{{ 'legal.accountDeletion.kept' | translate }}</p>

          <h4>{{ 'legal.accountDeletion.contactTitle' | translate }}</h4>
          <p>
            {{ 'legal.accountDeletion.contact' | translate }}:
            <a [href]="'mailto:' + site.contactEmail">{{ site.contactEmail }}</a>
          </p>

          <p class="back"><a [routerLink]="back.link">{{ back.label | translate }}</a></p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .legal-container { padding: 2rem; display: flex; justify-content: center; }
    mat-card { max-width: 720px; width: 100%; }
    h4 { margin: 1.25rem 0 0.25rem; color: #90caf9; }
    a { color: #90caf9; }
    .back { margin-top: 1.5rem; }
    .action { margin: 0.75rem 0 0.25rem; }
  `]
})
export class AccountDeletionComponent {
  /** Kontakt je Oberflaeche (KidHub: eigene Adresse, siehe LEGAL_SITE). */
  readonly site = inject(LEGAL_SITE);
  /** Ruecklink je Oberflaeche (KidHub: zur Startseite statt zur Anmeldemaske). */
  readonly back = legalBack(this.site, 'legal.accountDeletion.back');

  private readonly handoff = inject(HandoffService);
  /** Wird das Konto woanders gefuehrt (KidHub, LeagueHub, ClubHub, Turnierseite → RookHub)? Sonst ist hier RookHub. */
  readonly home = this.site.accountHome ?? null;
  /** Ziel in RookHub: das Profil mit aufgeklappter Karte „Konto loeschen" (Codereview UX-023). */
  readonly deleteRoute = ACCOUNT_DELETE_ROUTE;
  readonly deleteQuery = ACCOUNT_DELETE_QUERY;
  /** `profile?section=delete` — ohne fuehrenden Schraegstrich, wie ihn der Sprung erwartet. */
  private readonly deletePath = `${ACCOUNT_DELETE_ROUTE.slice(1)}?${new URLSearchParams(ACCOUNT_DELETE_QUERY)}`;
  /** Dasselbe Ziel als Adresse auf RookHub — `null` ohne bekannte Adresse (localhost, IP): dann nur der Text. */
  readonly homeDeleteUrl = this.home && this.handoff.accountHomeUrl ? `${this.handoff.accountHomeUrl}/${this.deletePath}` : null;

  /** Angemeldet nimmt der Sprung die Anmeldung mit (Einmal-Code), sonst meldet man sich drueben an. Strg/Mittelklick
   *  (neuer Tab) bleibt beim schlichten Link. */
  openOnHome(e: MouseEvent): void {
    if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    void this.handoff.jumpToAccountHome(this.deletePath);
  }
}
