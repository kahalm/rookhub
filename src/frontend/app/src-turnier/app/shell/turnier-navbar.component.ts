import { Component, ChangeDetectionStrategy, DestroyRef, ElementRef, NgZone, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, Router } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { IconLabelDirective } from '@rh/shared/icon-label/icon-label.directive';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { LocaleService } from '@rh/core/locale.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ThemeService } from '@rh/core/theme.service';
import { authLinkQuery } from '@rh/core/return-url.util';
import { trackNavEdge } from '@rh/shared/navbar/nav-edge';
import { LanguageMenu, languageMenu } from '@rh/core/language-menu';
import { PctPipe } from '@rh/shared/format/pct';

/**
 * Kopfzeile der Turnierseite. Bewusst schmal: drei Wege (Kalender, Gemerkt, Meine Turniere), Sprache, Konto — und
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
    MatIconModule, MatMenuModule, IconLabelDirective, TranslatePipe, PctPipe],
  template: `
    <mat-toolbar color="primary" class="tb">
      <!-- Die Marke fuehrt zur Startseite (/ = Kalender, siehe app.routes.ts), nicht auf die Merkliste (F6-019). -->
      <a class="brand" routerLink="/">{{ 'turnier.brand' | translate }}</a>

      <!-- Kalender zuerst (die Startseite); die Merkliste heisst „Gemerkt" wie der Knopf, der sie fuellt.
           Der Kalender ist seit 0.643.0 OHNE Anmeldung offen und steht deshalb auch fuer Gaeste da; „Gemerkt" und
           „Meine Turniere" gehoeren einem Konto und bleiben angemeldeten Nutzern vorbehalten (UX-038: ein Link, der
           nur auf die Anmeldemaske fuehrt, wirkt tot). -->
      <nav class="links">
        <!-- d4-t-navbar-aktiv-marker-fehlt: der aktive Weg trägt Farbe + Unterstrich und aria-current (E-Ink-Rahmen, DESIGN 3.6). -->
        <a mat-button routerLink="/tournaments/calendar" routerLinkActive="on" ariaCurrentWhenActive="page">{{ 'nav.tournamentCalendar' | translate }}</a>
        @if (auth.isLoggedIn) {
          <a mat-button routerLink="/tournaments" routerLinkActive="on" ariaCurrentWhenActive="page"
             [routerLinkActiveOptions]="{ exact: true }">{{ 'nav.tournamentBookmarks' | translate }}</a>
          <a mat-button routerLink="/tournaments/history" routerLinkActive="on" ariaCurrentWhenActive="page">{{ 'nav.tournamentHistory' | translate }}</a>
        }
      </nav>

      <span class="spacer"></span>

      <!-- Nur bis 768px sichtbar: die drei Wege, der RookHub-Sprung, Design und Sprache aus der
           Zeile — die passte am Handy nicht in den Schirm. Anmelden/Konto bleibt draussen: die
           eine Aktion je Seite gehoert nicht in ein Menue. -->
      <button mat-icon-button class="narrow" [matMenuTriggerFor]="navMenu" [attr.aria-label]="'nav.menu' | translate">
        <mat-icon>menu</mat-icon>
      </button>
      <mat-menu #navMenu="matMenu">
        <a mat-menu-item routerLink="/tournaments/calendar" routerLinkActive="on" ariaCurrentWhenActive="page"><mat-icon>event</mat-icon> {{ 'nav.tournamentCalendar' | translate }}</a>
        @if (auth.isLoggedIn) {
          <a mat-menu-item routerLink="/tournaments" routerLinkActive="on" ariaCurrentWhenActive="page"
             [routerLinkActiveOptions]="{ exact: true }"><mat-icon>bookmarks</mat-icon> {{ 'nav.tournamentBookmarks' | translate }}</a>
          <a mat-menu-item routerLink="/tournaments/history" routerLinkActive="on" ariaCurrentWhenActive="page"><mat-icon>emoji_events</mat-icon> {{ 'nav.tournamentHistory' | translate }}</a>
        }
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
        <!-- a1-t-navbar-tablet-en: zwischen 769 und 1023 px nur das Symbol (Name bleibt per title/aria-label) — englisch
             klebte „To RookHub" sonst wie ein vierter Menuepunkt an „My tournaments". -->
        <button mat-button class="wide to-rh" (click)="toRookHub()" [attr.title]="'turnier.toRookHub' | translate"
                [attr.aria-label]="'turnier.toRookHub' | translate">
          <mat-icon>open_in_new</mat-icon>
          <span class="to-rh-label">{{ 'turnier.toRookHub' | translate }}</span>
        </button>
      }

      <button mat-icon-button class="wide" (click)="theme.toggle()"
              [appIconLabel]="themeLabel">
        <mat-icon>{{ themeIcon }}</mat-icon>
      </button>
      <button mat-icon-button class="wide" [matMenuTriggerFor]="langMenu" [attr.aria-label]="'nav.language' | translate">
        <mat-icon>language</mat-icon>
      </button>
      <!-- Dieselbe Auswahl wie RookHubs Navbar (locale.languages: alle Sprachen mit Eigenbezeichnung, Haken an der
           aktuellen). Vorher fest EN/DE/HR ohne Markierung: wer ueber das geteilte Cookie oder den Browser z. B. auf
           Ungarisch kam, sah hier weder, was eingestellt ist, noch fuehrte nach einem Wechsel ein Weg zurueck. -->
      <mat-menu #langMenu="matMenu">
        @for (l of langGroups.full; track l.code) {
          <button mat-menu-item (click)="locale.use(l.code)">
            <mat-icon>{{ locale.current === l.code ? 'check' : 'translate' }}</mat-icon>
            <span>{{ l.label }}</span>
          </button>
        }
        <!-- F8-009: Teilübersetzungen wie in RookHubs Navbar — eigene Gruppe mit Anteil. -->
        <div class="lang-menu-label lang-partial">{{ 'nav.languagePartial' | translate }}</div>
        @for (l of langGroups.partial; track l.code) {
          <button mat-menu-item class="lang-partial-item" (click)="locale.use(l.code)">
            <mat-icon>{{ locale.current === l.code ? 'check' : 'translate' }}</mat-icon>
            <span>{{ l.label }} ({{ l.coverage | pct }})</span>
          </button>
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
      } @else if (!onAuthPage) {
        <!-- UI-Sweep 2026-10-11 (r2-t-login-headbtn, a1-t-reset-kopf-anmelden): auf /login, /register und den beiden
             Passwort-Masken kein zweiter „Anmelden“-Link im Kopf. -->
        <a mat-button routerLink="/login" [queryParams]="authQuery">{{ 'nav.login' | translate }}</a>
      }
    </mat-toolbar>
  `,
  styles: [`
    /* F8-009: Zwischentitel der Teilübersetzungen im Sprachmenü (wie RookHubs Navbar). */
    .lang-menu-label { padding: 12px 16px 4px; margin-top: 4px; font-size: 0.75rem; text-transform: uppercase;
      color: color-mix(in srgb, currentColor 47%, transparent); border-top: 1px solid color-mix(in srgb, currentColor 15%, transparent); }
    .tb { gap: 4px; }
    .brand { font-weight: 600; text-decoration: none; color: inherit; margin-right: 8px; }
    .links { display: flex; gap: 2px; }
    /* d4-t-navbar-aktiv-marker-fehlt: routerLinkActive setzte „on“, aber keine Regel wirkte — die aktuelle Seite war in
       der Kopfleiste nicht zu erkennen. Primärfarbe, halbfett, Unterstrich als Innenschatten (kein Layout-Sprung). */
    .links a.on {
      --mat-button-text-label-text-color: var(--mat-sys-primary);
      color: var(--mat-sys-primary); font-weight: 600; border-radius: 0;
      box-shadow: inset 0 -2px 0 var(--mat-sys-primary);
    }
    .mat-mdc-menu-item.on, .mat-mdc-menu-item.on .mat-icon { color: var(--mat-sys-primary); font-weight: 600; }
    /* Mindestabstand zwischen den Wegen und den Kopfknoepfen (a1-t-navbar-tablet-en). */
    .spacer { flex: 1 1 auto; min-width: 24px; }
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
    /* Tablet-Zwischenbreite (a1-t-navbar-tablet-en): „Zu RookHub" nur als Symbol, damit die Zeile Luft behaelt. */
    @media (min-width: 769px) and (max-width: 1023.98px) {
      .to-rh .to-rh-label { display: none; }
      .to-rh .mat-icon { margin: 0; }
    }
    /* UI-Sweep u1 (u1-rk-navbar-raender): Breitbild-Stufe ab 2400 px (DESIGN 4.1) — der Hintergrund bleibt randlos,
       Marke und Symbole rücken auf die Inhaltskante (statt 1 100 px vom Inhalt entfernt am Fensterrand).
       b1-rk-kopfleiste-ultrawide-flucht: Kante der JEWEILIGEN Seite (--nav-edge, gemessen von trackNavEdge);
       Rückfall --page-max-width-wide (1 880 px). */
    @media (min-width: 2400px) {
      .tb { padding-inline: max(16px, calc((100% - var(--nav-edge, var(--page-max-width-wide, 1880px))) / 2 + 16px)); }
    }
  `],
})
export class TurnierNavbarComponent {
  auth = inject(AuthService);
  readonly locale = inject(LocaleService);
  /** Sprachmenü: gepflegte Sprachen oben, Teilübersetzungen mit Prozent darunter (F8-009) — erst beim Zeichnen. */
  private langGroupsCache?: LanguageMenu;
  get langGroups(): LanguageMenu { return this.langGroupsCache ??= languageMenu(this.locale.languages); }
  private translate = inject(TranslateService);
  private handoff = inject(HandoffService);
  readonly theme = inject(ThemeService);

  private router = inject(Router);

  constructor() {
    // b1-rk-kopfleiste-ultrawide-flucht: ab 2400 px fluchtet der Kopfinhalt mit der Inhaltskante der Seite.
    trackNavEdge(inject<ElementRef<HTMLElement>>(ElementRef).nativeElement, this.router, inject(NgZone), inject(DestroyRef));
  }

  /** „Anmelden“ fuehrt hierher zurueck, z. B. auf das geteilte Turnier /t/42 (UX-020). Rueckfall „/“: die Turnierseite
   *  hat kein /dashboard, den Rueckfall der Maske. */
  get authQuery(): { returnUrl?: string } { return authLinkQuery(this.router.url, '/'); }

  /** Anmelde-, Registrierungs- oder Passwort-Maske offen? Dort fuehrt „Anmelden“ im Kopf nur auf die Seite, auf der man
   *  schon ist, bzw. doppelt „Zurueck zur Anmeldung“ in der Karte (UI-Sweep 2026-10-11, r2-t-login-headbtn — gleiche
   *  Loesung wie x-login-headbtn in LeagueHub/ClubHub; Passwort-Masken: a1-t-reset-kopf-anmelden). */
  get onAuthPage(): boolean {
    const path = this.router.url.split(/[?#]/)[0];
    return ['/login', '/register', '/forgot-password', '/reset-password'].includes(path);
  }

  get partnerUrl(): string | null { return this.handoff.partnerUrl; }

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
