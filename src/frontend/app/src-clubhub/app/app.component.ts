import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { LocaleService } from '@rh/core/locale.service';
import { ThemeService } from '@rh/core/theme.service';
import { environment } from '../../src/environments/environment';
import { hasClubAccess } from './core/club-access';

/** Der Reiter „Kartei" gilt für die Liste UND für jedes Karteiblatt (`/kind/…`). */
export function isCardIndexUrl(url: string): boolean {
  const path = url.split(/[?#]/)[0];
  return path === '/' || path === '' || path.startsWith('/kind/');
}

/**
 * Hülle von ClubHub: Wortmarke, rechts Anmelden bzw. Name + Abmelden, darunter die Karteireiter (nur für freigeschaltete
 * Konten), unten Version und Datenschutz. Beim Start übernimmt `consumeIncoming()` eine Anmeldung aus RookHub
 * (Sprung-Code oder geteiltes Cookie); wer keine hat, meldet sich hier über RookHubs Maske an — dasselbe Konto.
 */
@Component({
  selector: 'ch-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="top" [class.no-tabs]="!access()">
      <div class="wrap top-row">
        <a class="brand" routerLink="/">ClubHub</a>
        <nav class="account" aria-label="Konto">
          @if (user(); as u) {
            <span class="who">{{ u.username }}</span>
            <button type="button" class="btn" (click)="logout()">Abmelden</button>
          } @else {
            <a class="btn" routerLink="/login" [queryParams]="{ returnUrl: '/' }">Anmelden</a>
          }
        </nav>
      </div>
      <p class="wrap lede">Die Kartei der Kinder und Jugendlichen im Verein: Kontakte, Gruppen, Anwesenheit, Lernstand.</p>
      @if (access()) {
        <nav class="wrap tabs" aria-label="Bereiche">
          <a routerLink="/" [class.on]="onCardIndex()" [attr.aria-current]="onCardIndex() ? 'page' : null">Kartei</a>
          <a routerLink="/gruppen" routerLinkActive="on" ariaCurrentWhenActive="page">Gruppen</a>
        </nav>
      }
    </header>
    <main class="wrap"><router-outlet /></main>
    <footer class="wrap foot">
      <span>v{{ version }}</span>
      <a routerLink="/verknuepfen">Konto verknüpfen</a>
      <a routerLink="/impressum">Impressum</a>
      <a routerLink="/privacy">Datenschutz</a>
    </footer>
  `,
})
export class ClubHubAppComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly handoff = inject(HandoffService);
  private readonly router = inject(Router);
  private readonly locale = inject(LocaleService);
  /** Nur injizieren genügt: hell/dunkel wie in RookHub (geteilter Design-Modus, setzt html.dark-theme). */
  private readonly theme = inject(ThemeService);
  readonly user = toSignal(this.auth.currentUser$, { initialValue: this.auth.currentUser });
  readonly version = environment.version;
  /** Reiter nur für freigeschaltete Konten; neu gerechnet, wenn sich die Anmeldung ändert. */
  readonly access = computed(() => {
    this.user();
    return hasClubAccess(this.auth);
  });

  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url });
  readonly onCardIndex = computed(() => isCardIndexUrl(this.url()));

  ngOnInit(): void {
    // Die Seite ist deutsch; die geteilten Masken (Anmelden, Datenschutz) zeigen es ebenso — ANGEZEIGT, ohne die
    // Sprachwahl aus RookHub zu überschreiben.
    this.locale.init();
    this.locale.applyUnsaved('de');
    void this.handoff.consumeIncoming();
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
