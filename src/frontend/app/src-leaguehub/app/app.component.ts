import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { LocaleService } from '@rh/core/locale.service';
import { ThemeService } from '@rh/core/theme.service';
import { environment } from '../../src/environments/environment';

/**
 * Hülle von LeagueHub: Wortmarke, rechts Anmelden bzw. Name + Abmelden, unten Version und Datenschutz.
 * Beim Start übernimmt `consumeIncoming()` eine Anmeldung aus RookHub (Sprung-Code oder geteiltes Cookie);
 * wer keine hat, meldet sich hier über RookHubs Maske an — dasselbe Konto.
 */
@Component({
  selector: 'lh-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="top">
      <div class="wrap top-row">
        <a class="brand" routerLink="/">LeagueHub</a>
        <nav class="account">
          @if (user(); as u) {
            <span class="who">{{ u.username }}</span>
            <button type="button" class="btn-sec" (click)="logout()">Abmelden</button>
          } @else {
            <a class="btn-sec" routerLink="/login" [queryParams]="{ returnUrl: '/' }">Anmelden</a>
          }
        </nav>
      </div>
      <p class="wrap lede">Wer sitzt euch gegenüber? Aufstellungs-Prognosen für die Tiroler Mannschaftsmeisterschaft.</p>
      @if (nav().view) {
        <nav class="wrap tabs" aria-label="Bereiche">
          <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">Prognosen</a>
          <a routerLink="/verein" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">Vereinspartien</a>
          @if (nav().contribute) { <a routerLink="/verein/neu" routerLinkActive="on">Partien hinzufügen</a> }
        </nav>
      }
    </header>
    <!-- Die Formular-Korrektur braucht Foto, Brett und Zugliste nebeneinander — dort ist die Seite breiter. -->
    <main class="wrap" [class.wide]="wide()"><router-outlet /></main>
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
  readonly version = environment.version;
  /** Reiter nur für freigeschaltete Konten; neu gerechnet, wenn sich die Anmeldung ändert. */
  readonly nav = computed(() => {
    this.user();
    return { view: this.auth.has('league.view'), contribute: this.auth.has('league.contribute') };
  });
  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url });
  readonly wide = computed(() => this.url().startsWith('/verein/formular/'));

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
