import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { LocaleService } from '@rh/core/locale.service';
import { ThemeService } from '@rh/core/theme.service';
import { FooterPresenceService } from '@rh/shared/app-footer/footer-presence';
import { environment } from '../../src/environments/environment';
import { hasClubAccess } from './core/club-access';
import { MemberPhotoStore } from './core/member-photo.store';
import { LinkStateStore } from './core/link-state.store';

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
          } @else if (!onLogin()) {
            <!-- UI-Sweep 2026-10-10 (x-login-headbtn): auf /login selbst kein zweiter „Anmelden“-Knopf -->
            <a class="btn" routerLink="/login" [queryParams]="{ returnUrl: '/' }">Anmelden</a>
          }
        </nav>
      </div>
      <!-- Die Werbezeile nur ohne Club-Zugang (d2-c-kartei-lead, d2-c-attendance-lead): wer arbeitet, kennt die Kartei —
           die Zeile schob auf jeder Seite die Hauptsache (Liste, Abhakliste) nach unten. -->
      @if (!access()) {
        <p class="wrap lede">Die Kartei der Kinder und Jugendlichen im Verein: Kontakte, Gruppen, Anwesenheit, Lernstand.</p>
      }
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
      <!-- UI-Sweep 2026-10-11 (r2-c-footer-link): nur für Konten ohne Club-Recht; verknüpft heißt er „Verknüpfung (Lena)" -->
      <!-- z3-c-fuss-umbruch: Links als Gruppe — reicht die Zeile nicht, rückt die Gruppe geschlossen in Zeile 2. -->
      <span class="foot-links">
        @if (!access()) { <a routerLink="/verknuepfen">{{ linkText() }}</a> }
        <a routerLink="/impressum">Impressum</a>
        <a routerLink="/privacy">Datenschutz</a>
      </span>
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
  private readonly photos = inject(MemberPhotoStore);
  private readonly linkStore = inject(LinkStateStore);
  readonly user = toSignal(this.auth.currentUser$, { initialValue: this.auth.currentUser });
  readonly version = environment.version;
  /** Reiter nur für freigeschaltete Konten; neu gerechnet, wenn sich die Anmeldung ändert. */
  readonly access = computed(() => {
    this.user();
    return hasClubAccess(this.auth);
  });

  /** UI-Sweep 2026-10-11 (r2-c-footer-link): Text des Fußzeilenlinks je Verknüpfungsstand. */
  readonly linkText = computed(() => {
    const s = this.linkStore.state();
    if (!s?.linked) return 'Konto verknüpfen';
    return s.firstName ? `Verknüpfung (${s.firstName})` : 'Verknüpfung';
  });
  private linkUser: number | null = null;

  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url });
  readonly onCardIndex = computed(() => isCardIndexUrl(this.url()));
  /** Auf der Anmeldemaske selbst gibt es oben keinen „Anmelden“-Knopf (UI-Sweep 2026-10-10, x-login-headbtn). */
  readonly onLogin = computed(() => this.url().split(/[?#]/)[0] === '/login');

  constructor() {
    // Die Fusszeile zeigt Impressum und Datenschutz immer — die Anmeldemaske laesst ihre eigene Zeile dann weg
    // (UI-Sweep 2026-10-10, x-login-legal).
    inject(FooterPresenceService).presence.set('always');
    // UI-Sweep 2026-10-11 (r2-c-footer-link): Verknüpfungsstand nur für angemeldete Konten ohne Club-Recht; ein anderes
    // Konto (Abmelden, Wechsel) beginnt mit leerem Stand.
    effect(() => {
      const u = this.user();
      const id = u?.userId ?? null;
      if (id !== this.linkUser) {
        this.linkUser = id;
        this.linkStore.clear();
      }
      if (u && !this.access()) void this.linkStore.ensure();
    });
  }

  ngOnInit(): void {
    // Die Seite ist deutsch; die geteilten Masken (Anmelden, Datenschutz) zeigen es ebenso — ANGEZEIGT, ohne die
    // Sprachwahl aus RookHub zu überschreiben.
    this.locale.init();
    this.locale.applyUnsaved('de');
    void this.handoff.consumeIncoming();
  }

  logout(): void {
    this.photos.clear();                           // Bilder der Kinder bleiben nicht im Speicher der Seite liegen
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
