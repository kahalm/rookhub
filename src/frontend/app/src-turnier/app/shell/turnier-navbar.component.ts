import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, Router } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { LocaleService, AppLang } from '@rh/core/locale.service';
import { HandoffService } from '@rh/core/handoff.service';

/**
 * Kopfzeile der Turnierseite. Bewusst schmal: zwei Wege (Liste, Kalender), Sprache, Konto — und
 * der Sprung zurueck nach RookHub, der die Anmeldung mitnimmt.
 */
@Component({
  selector: 'trn-navbar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [CommonModule, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule,
    MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <mat-toolbar color="primary" class="tb">
      <a class="brand" routerLink="/tournaments">{{ 'turnier.brand' | translate }}</a>

      <nav class="links">
        <a mat-button routerLink="/tournaments" routerLinkActive="on"
           [routerLinkActiveOptions]="{ exact: true }">{{ 'nav.tournaments' | translate }}</a>
        <a mat-button routerLink="/tournaments/calendar" routerLinkActive="on">{{ 'nav.tournamentCalendar' | translate }}</a>
      </nav>

      <span class="spacer"></span>

      @if (partnerUrl) {
        <button mat-button (click)="toRookHub()" [attr.title]="'turnier.toRookHub' | translate">
          <mat-icon>open_in_new</mat-icon>
          <span class="wide">{{ 'turnier.toRookHub' | translate }}</span>
        </button>
      }

      <button mat-icon-button [matMenuTriggerFor]="langMenu" [attr.aria-label]="'nav.language' | translate">
        <mat-icon>language</mat-icon>
      </button>
      <mat-menu #langMenu="matMenu">
        @for (l of languages; track l) {
          <button mat-menu-item (click)="setLang(l)">{{ l.toUpperCase() }}</button>
        }
      </mat-menu>

      @if (auth.isLoggedIn) {
        <button mat-icon-button [matMenuTriggerFor]="userMenu" [attr.aria-label]="'nav.account' | translate">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <div class="who" mat-menu-item disabled>{{ auth.currentUser?.username }}</div>
          <button mat-menu-item (click)="logout()">
            <mat-icon>logout</mat-icon> {{ 'nav.logout' | translate }}
          </button>
        </mat-menu>
      } @else {
        <a mat-button routerLink="/login">{{ 'nav.login' | translate }}</a>
      }
    </mat-toolbar>
  `,
  styles: [`
    .tb { gap: 4px; }
    .brand { font-weight: 600; text-decoration: none; color: inherit; margin-right: 8px; }
    .links { display: flex; gap: 2px; }
    .spacer { flex: 1 1 auto; }
    .who { opacity: .7; font-size: .85rem; }
    /* Auf schmalen Geräten nur das Symbol — der Text sprengt sonst die Zeile. */
    @media (max-width: 700px) { .wide { display: none; } }
  `],
})
export class TurnierNavbarComponent {
  auth = inject(AuthService);
  private locale = inject(LocaleService);
  private translate = inject(TranslateService);
  private handoff = inject(HandoffService);
  private router = inject(Router);

  readonly languages: AppLang[] = ['en', 'de', 'hr'];
  get partnerUrl(): string | null { return this.handoff.partnerUrl; }

  setLang(lang: AppLang): void { this.locale.use(lang); }

  /** Zurueck nach RookHub — angemeldet, wenn es geht (Einmal-Code, siehe HandoffService). */
  toRookHub(): void { void this.handoff.jump('dashboard'); }

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
