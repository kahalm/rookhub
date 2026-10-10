import { ChangeDetectionStrategy, Component, ElementRef, OnInit, computed, effect, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatMenuModule } from '@angular/material/menu';
import { filter, map } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { authLinkQuery } from '@rh/core/return-url.util';
import { LocaleService } from '@rh/core/locale.service';
import { ThemeService } from '@rh/core/theme.service';
import { environment } from '../../src/environments/environment';
import { ClaimPromptComponent } from './shared/claim-prompt.component';
import { ClubContextService } from './core/club-context.service';

/**
 * Hülle von LeagueHub: Wortmarke, rechts Anmelden bzw. Name + Abmelden, unten Version und Datenschutz.
 * Beim Start übernimmt `consumeIncoming()` eine Anmeldung aus RookHub (Sprung-Code oder geteiltes Cookie);
 * wer keine hat, meldet sich hier über RookHubs Maske an — dasselbe Konto.
 */
@Component({
  selector: 'lh-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ClaimPromptComponent, MatMenuModule],
  template: `
    <header class="top">
      <div class="wrap top-row">
        <a class="brand" routerLink="/">LeagueHub</a>
        <nav class="account">
          @if (user() && clubs.clubs().length > 1) {
            <!-- mehrere Vereine (0.698.0): Umschalter, gemerkt im Gerät; ein Verein → unsichtbar -->
            <label class="club-switch desk">Verein
              <select (change)="switchClub(+$any($event.target).value)" aria-label="Verein wählen">
                @for (c of clubs.clubs(); track c.id) {
                  <option [value]="c.id" [selected]="c.id === clubs.current()?.id">{{ c.name }}</option>
                }
              </select>
            </label>
          }
          @if (user(); as u) {
            <span class="who desk">{{ u.username }}</span>
            <button type="button" class="btn-sec desk" (click)="logout()">Abmelden</button>
            <!-- UI-Sweep 2026-10-10 (l-nav-mobile): am Handy EIN Konto-Knopf, dahinter Name, Verein wechseln, Abmelden -->
            <button type="button" class="btn-sec acct-btn mob" [matMenuTriggerFor]="acctMenu"
                    [attr.aria-label]="'Konto ' + u.username" title="Konto">👤 ▾</button>
            <mat-menu #acctMenu="matMenu" xPosition="before">
              <button mat-menu-item type="button" class="acct-who" disabled>{{ u.username }}</button>
              @if (clubs.clubs().length > 1) {
                @for (c of clubs.clubs(); track c.id) {
                  <button mat-menu-item type="button" class="acct-club" [attr.aria-current]="c.id === clubs.current()?.id ? 'true' : null"
                          (click)="switchClub(c.id)">{{ c.id === clubs.current()?.id ? '✓ ' : '' }}{{ c.name }}</button>
                }
              }
              <button mat-menu-item type="button" class="acct-logout" (click)="logout()">Abmelden</button>
            </mat-menu>
          } @else if (!onLogin()) {
            <!-- UI-Sweep 2026-10-10 (x-login-headbtn): auf /login selbst kein zweiter „Anmelden“-Knopf -->
            <a class="btn-sec" routerLink="/login" [queryParams]="authQuery()">Anmelden</a>
          }
        </nav>
      </div>
      <p class="wrap lede" [class.work]="work()">Wer sitzt euch gegenüber? Aufstellungs-Prognosen für {{ region() }}.</p>
      @if (user()) {
        <nav class="wrap tabs" aria-label="Bereiche">
          @if (nav().view) {
            <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">Prognosen</a>
            <a routerLink="/verein" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">Vereinspartien</a>
          }
          @if (nav().contribute) { <a routerLink="/verein/neu" routerLinkActive="on">Partien hinzufügen</a> }
          @if (nav().manage) {
            <a routerLink="/konten" routerLinkActive="on">Konto-Vorschläge</a>
            <a routerLink="/uebertragungen" routerLinkActive="on">Übertragungen</a>
            @if (nav().admin) { <a routerLink="/vereine" routerLinkActive="on">Vereine</a> }
          }
        </nav>
      }
    </header>
    <!-- Die Formular-Korrektur braucht Foto, Brett und Zugliste nebeneinander — dort ist die Seite breiter. -->
    <!-- nicht beim Einstieg als ein Nutzer: die Schlüssel dieses Browsers gehören dem Admin, nicht dem Nutzer -->
    <lh-claim-prompt class="wrap" [userId]="user()?.impersonating ? null : user()?.userId ?? null" />
    <main class="wrap" [class.wide]="wide()" [class.mid]="mid()"><router-outlet /></main>
    <footer class="wrap foot">
      <span>v{{ version }}</span>
      <a routerLink="/impressum">Impressum</a>
      <a routerLink="/privacy">Datenschutz</a>
    </footer>
  `,
})
export class LeagueHubAppComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly handoff = inject(HandoffService);
  private readonly router = inject(Router);
  private readonly locale = inject(LocaleService);
  /** Nur injizieren genügt: hell/dunkel wie in RookHub (geteilter Design-Modus, setzt html.dark-theme). */
  private readonly theme = inject(ThemeService);
  readonly user = toSignal(this.auth.currentUser$, { initialValue: this.auth.currentUser });
  /** Der Verein des Kontos (0.698.0) — Umschalter im Kopf, wenn es mehrere sind. */
  readonly clubs = inject(ClubContextService);
  /** Wofür die Prognosen sind — nach der Region des Vereins (ohne Verein die Tiroler wie bisher). */
  readonly region = computed(() => this.clubs.club()?.region === 'bayern'
    ? 'die bayerischen Mannschaftsligen' : 'die Tiroler Mannschaftsmeisterschaft');
  readonly version = environment.version;
  /** Reiter nur für freigeschaltete Konten; neu gerechnet, wenn sich die Anmeldung ändert. */
  readonly nav = computed(() => {
    this.user();
    const manage = this.auth.has('league.manage');
    // „Vereine" (0.700.0): der Server verlangt Admin UND league.manage
    return { view: this.auth.has('league.view'), contribute: this.auth.has('league.contribute'), manage, admin: manage && !!this.auth.isAdmin };
  });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url });
  readonly wide = computed(() => this.url().startsWith('/verein/formular/'));
  /** Vereinspartien (UI-Sweep 2026-10-10, l-club-table): die Tabelle bekommt ~1240 px statt 780 — Namen und Eröffnung
   *  brachen sonst auf zwei, drei Zeilen um. */
  readonly mid = computed(() => /^\/verein(?:[?#]|$)/.test(this.url()));
  /** Auf der Anmeldemaske selbst gibt es oben keinen „Anmelden“-Knopf (UI-Sweep 2026-10-10, x-login-headbtn). */
  readonly onLogin = computed(() => this.url().split(/[?#]/)[0] === '/login');
  /** „Formular prüfen" (angemeldet und über den Teilen-Link): am Handy fällt dort der Werbesatz weg (UX-036) — er kostete
   *  drei Zeilen über Prüfteil und Brett, und vom Brett war im ersten Bildschirm nur die oberste Reihe zu sehen. */
  readonly work = computed(() => /^\/(verein|s\/[^/?#]+)\/formular\//.test(this.url()));
  /** „Anmelden“ fuehrt hierher zurueck (UX-020) — bisher fest „/“, auch von /verein/neu, /s/<token> oder der
   *  Registrierung mit eigenem Ziel. Rueckfall „/“: LeagueHub hat kein /dashboard. */
  readonly authQuery = computed(() => authLinkQuery(this.url(), '/'));

  constructor() {
    // Angemeldet: die Vereine des Kontos holen (Umschalter, Texte); die Aufrufe selbst warten ohnehin darauf.
    effect(() => {
      if (this.user()) this.clubs.ensure().subscribe();
    });
    // Am Handy scrollen die Reiter waagrecht (l-nav-mobile): der aktive soll sichtbar sein, nicht am Rand angeschnitten.
    effect(() => {
      this.url();
      setTimeout(() => this.revealActiveTab());
    });
  }

  /** Bringt den aktiven Reiter in den sichtbaren Teil der Reiterzeile — nur deren `scrollLeft`, nie die Seite. */
  revealActiveTab(): void {
    const bar = this.host.nativeElement.querySelector('nav.tabs') as HTMLElement | null;
    const on = bar?.querySelector('a.on') as HTMLElement | null;
    if (!bar || !on || bar.scrollWidth <= bar.clientWidth) return;
    const left = on.getBoundingClientRect().left - bar.getBoundingClientRect().left + bar.scrollLeft;
    const right = left + on.offsetWidth, pad = 28;
    if (left < bar.scrollLeft) bar.scrollLeft = Math.max(0, left - pad);
    else if (right > bar.scrollLeft + bar.clientWidth) bar.scrollLeft = right - bar.clientWidth + pad;
  }

  /** Verein wechseln: gemerkt, dann die Seite neu — nichts vom alten Verein (Listen, Entwürfe, Formulare) bleibt stehen. */
  switchClub(id: number): void {
    if (id === this.clubs.current()?.id) return;
    this.clubs.select(id);
    this.reload();
  }

  /** Eigene Methode, damit die Specs sie ersetzen können. */
  reload(): void {
    window.location.reload();
  }

  ngOnInit(): void {
    // Die Seite ist deutsch (Tiroler Ligen); die geteilten Masken (Anmelden, Datenschutz) zeigen es ebenso —
    // ANGEZEIGT, ohne die Sprachwahl aus RookHub zu überschreiben.
    this.locale.init();
    this.locale.applyUnsaved('de');
    void this.handoff.consumeIncoming();
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
