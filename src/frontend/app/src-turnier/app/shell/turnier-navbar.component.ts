import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, Router } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { LocaleService, AppLang } from '@rh/core/locale.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ThemeService } from '@rh/core/theme.service';

/**
 * Kopfzeile der Turnierseite. Bewusst schmal: zwei Wege (Liste, Kalender), Sprache, Konto — und
 * der Sprung zurueck nach RookHub, der die Anmeldung mitnimmt.
 *
 * <p>Am Handy (bis 768px, der Bruch der ganzen App) traegt die Zeile nur Marke, ☰ und
 * Anmelden bzw. Konto. Vorher standen die drei Textlinks plus RookHub, Design, Sprache und
 * Anmelden in EINER Zeile ohne Umbruch — die Toolbar war rund 670px breit, die GANZE Seite
 * scrollte seitwaerts, und Anmelden wie Sprachwechsel lagen ausserhalb des Schirms. Alles, was
 * nicht in die Zeile passt, liegt jetzt im ☰-Menue (dasselbe Muster wie RookHubs Navbar).</p>
 */
@Component({
  selector: 'trn-navbar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [CommonModule, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule,
    MatIconModule, MatMenuModule, MatTooltipModule, TranslatePipe],
  template: `
    <mat-toolbar color="primary" class="tb">
      <a class="brand" routerLink="/tournaments">{{ 'turnier.brand' | translate }}</a>

      <nav class="links">
        <a mat-button routerLink="/tournaments" routerLinkActive="on"
           [routerLinkActiveOptions]="{ exact: true }">{{ 'nav.tournaments' | translate }}</a>
        <a mat-button routerLink="/tournaments/calendar" routerLinkActive="on">{{ 'nav.tournamentCalendar' | translate }}</a>
        <a mat-button routerLink="/tournaments/history" routerLinkActive="on">{{ 'nav.tournamentHistory' | translate }}</a>
      </nav>

      <span class="spacer"></span>

      <!-- Nur bis 768px sichtbar: die drei Wege, der RookHub-Sprung, Design und Sprache aus der
           Zeile — die passte am Handy nicht in den Schirm. Anmelden/Konto bleibt draussen: die
           eine Aktion je Seite gehoert nicht in ein Menue. -->
      <button mat-icon-button class="narrow" [matMenuTriggerFor]="navMenu" [attr.aria-label]="'nav.menu' | translate">
        <mat-icon>menu</mat-icon>
      </button>
      <mat-menu #navMenu="matMenu">
        <a mat-menu-item routerLink="/tournaments">{{ 'nav.tournaments' | translate }}</a>
        <a mat-menu-item routerLink="/tournaments/calendar">{{ 'nav.tournamentCalendar' | translate }}</a>
        <a mat-menu-item routerLink="/tournaments/history">{{ 'nav.tournamentHistory' | translate }}</a>
        @if (partnerUrl) {
          <button mat-menu-item (click)="toRookHub()">
            <mat-icon>open_in_new</mat-icon> {{ 'turnier.toRookHub' | translate }}
          </button>
        }
        <button mat-menu-item (click)="theme.toggle()">
          <mat-icon>{{ themeIcon }}</mat-icon> {{ themeLabel }}
        </button>
        <!-- Dasselbe Sprachmenue wie der Globus in der Zeile — kein zweiter Bestand. -->
        <button mat-menu-item [matMenuTriggerFor]="langMenu">
          <mat-icon>language</mat-icon> {{ 'nav.language' | translate }}
        </button>
      </mat-menu>

      @if (partnerUrl) {
        <button mat-button class="wide" (click)="toRookHub()" [attr.title]="'turnier.toRookHub' | translate">
          <mat-icon>open_in_new</mat-icon>
          <span>{{ 'turnier.toRookHub' | translate }}</span>
        </button>
      }

      <button mat-icon-button class="wide" (click)="theme.toggle()"
              [matTooltip]="themeLabel" [attr.aria-label]="themeLabel">
        <mat-icon>{{ themeIcon }}</mat-icon>
      </button>
      <button mat-icon-button class="wide" [matMenuTriggerFor]="langMenu" [attr.aria-label]="'nav.language' | translate">
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
          <a mat-menu-item routerLink="/profile">
            <mat-icon>badge</mat-icon> {{ 'nav.profile' | translate }}
          </a>
          <!-- Nur fuer Admins, und nur EIN Punkt: als ein Nutzer einsteigen. Das uebrige
               Admin-Panel bleibt in RookHub. -->
          @if (auth.isAdmin) {
            <a mat-menu-item routerLink="/admin">
              <mat-icon>login</mat-icon> {{ 'turnierAdmin.title' | translate }}
            </a>
          }
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
    /* Das ☰ gibt es nur am Handy; am Schreibtisch stehen alle Wege in der Zeile. */
    .narrow { display: none; }
    /* EINE Bruchstelle (768px wie ueberall in der App, vorher 700px hier): Textlinks, RookHub,
       Design und Sprache verschwinden aus der Zeile und liegen im ☰-Menue. Ohne das war die
       Toolbar am Handy rund 670px breit und die ganze Seite scrollte seitwaerts. inline-block
       ist der Vorgabewert des Material-Icon-Knopfs — nur den Vorgabe-„none" von oben aufheben. */
    @media (max-width: 768px) {
      .links, .wide { display: none; }
      .narrow { display: inline-block; }
    }
  `],
})
export class TurnierNavbarComponent {
  auth = inject(AuthService);
  private locale = inject(LocaleService);
  private translate = inject(TranslateService);
  private handoff = inject(HandoffService);
  readonly theme = inject(ThemeService);

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

  /** Beschriftung wie in RookHub — dieselben Schluessel, damit beide Seiten gleich sprechen. */
  get themeLabel(): string {
    const key = { system: 'nav.themeSystem', light: 'nav.themeLight', dark: 'nav.themeDark' }[this.theme.preference];
    return this.translate.instant(key);
  }

  /** Sonne = hell, Mond = dunkel, Automatik = Systemvorgabe. */
  get themeIcon(): string {
    return this.theme.preference === 'light' ? 'light_mode'
      : this.theme.preference === 'dark' ? 'dark_mode'
      : 'brightness_auto';
  }

}
