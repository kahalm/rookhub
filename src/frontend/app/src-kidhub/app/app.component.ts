import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, untracked } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AppLang, LocaleService } from '@rh/core/locale.service';
import { AppUpdateService } from '@rh/core/app-update.service';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { catchError, filter, map, of, timeout } from 'rxjs';
import { environment } from '../../src/environments/environment';
import { KidsApiService } from './core/kids-api.service';

/** Die vollstaendig uebersetzten Sprachen — nur die bietet die Kinderseite an. */
export const KIDS_LANGUAGES: { code: AppLang; label: string }[] = [
  { code: 'de', label: 'Deutsch' },
  { code: 'en', label: 'English' },
  { code: 'hr', label: 'Hrvatski' },
  { code: 'hu', label: 'Magyar' },
];

/** Laenger wartet die Seite nicht auf den Sprach-Hinweis — danach Deutsch. */
export const HINT_TIMEOUT_MS = 4000;

function isKidsLanguage(code: string): code is AppLang {
  return KIDS_LANGUAGES.some(l => l.code === code);
}

/** Pfad ohne Abfrage und Anker — `/?quickstart=1` (nach dem Registrieren) ist auch die Startseite. */
export function isHomeUrl(url: string): boolean {
  const path = url.split(/[?#]/)[0];
  return path === '/' || path === '';
}

/**
 * Huelle der Kinderseite: eine schlichte Kopfzeile (Logo = zurueck zum Start; auf der Startseite
 * rechts Anmelden/Registrieren bzw. der Name und Abmelden), der Inhalt, unten
 * Sprache, Version und die Pflichtseiten. Die Farben der ganzen Seite stehen hier als Variablen
 * (`--kid-*`) — hell und freundlich, bewusst unabhaengig vom Design-Modus von RookHub.
 */
@Component({
  selector: 'kid-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, TranslatePipe],
  template: `
    <header class="top">
      <a class="logo" routerLink="/" [attr.aria-label]="'kids.home.title' | translate">
        <img class="mark" src="icons/icon-192.png" alt="" width="40" height="40">
        <span>{{ 'kids.home.title' | translate }}</span>
      </a>
      <!-- Nur auf der Startseite: mitten in einer Stufe lenkt ein Konto-Knopf ab. Spielen geht
           ohne Konto; angemeldet ist es dasselbe Konto wie in RookHub. -->
      @if (isHome()) {
        <nav class="account">
          @if (user(); as u) {
            <span class="who">👋 {{ u.username }}</span>
            <button type="button" class="acct" (click)="logout()">{{ 'nav.logout' | translate }}</button>
          } @else {
            <a class="acct" routerLink="/login" [queryParams]="{ returnUrl: '/' }">{{ 'nav.login' | translate }}</a>
            <a class="acct primary" routerLink="/register" [queryParams]="{ returnUrl: '/' }">{{ 'nav.register' | translate }}</a>
          }
        </nav>
      }
    </header>
    <main><router-outlet /></main>
    <footer class="foot">
      <label class="lang">
        <span class="sr-only">{{ 'kids.language' | translate }}</span>
        <!-- [selected] je Option statt [value] am select: die Optionen entstehen erst NACH der
             Bindung, der Wert ging verloren und die Liste zeigte immer den ersten Eintrag („Deutsch"),
             waehrend die Seite Englisch sprach — ein Klick auf Deutsch aenderte dann nichts. -->
        <select (change)="setLang($any($event.target).value)">
          @for (l of languages; track l.code) {
            <option [value]="l.code" [selected]="l.code === lang()">{{ l.label }}</option>
          }
        </select>
      </label>
      <span>v{{ version }}</span>
      <a routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</a>
      <a routerLink="/privacy">{{ 'legal.privacy.title' | translate }}</a>
      <!-- Pflichtangabe der Lizenz (CC BY 4.0) der Laenderliste, mit der die Startsprache bestimmt wird. -->
      <a href="https://db-ip.com" target="_blank" rel="noopener">IP Geolocation by DB-IP</a>
    </footer>
  `,
  styles: [`
    :host {
      --kid-bg: #eaf6ff;
      --kid-card: #ffffff;
      --kid-shadow: rgba(30, 70, 120, .22);
      --kid-title: #1d4f91;
      --kid-green: #2fb35f;
      --kid-yellow: #ffcc33;
      --kid-sky: #cfe9ff;
      --kid-peach: #ffe1cc;
      --kid-good-bg: #d6f5de;
      --kid-good-fg: #11632f;
      --kid-bad-bg: #ffe0e0;
      --kid-bad-fg: #9b1c1c;
      --kid-info-bg: #fff3c4;
      display: flex; flex-direction: column; min-height: 100vh; min-height: 100svh;
      background: radial-gradient(circle at 10% 0%, #fff7d6 0, transparent 40%), var(--kid-bg);
      color: #1f2d3d; font-family: Roboto, "Helvetica Neue", sans-serif;
    }
    .top { padding: 10px 16px; display: flex; align-items: center; justify-content: space-between;
           flex-wrap: wrap; gap: 8px 12px; }
    .account { display: flex; align-items: center; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-left: auto; }
    .who { font-weight: 700; color: var(--kid-title); }
    .acct { font: inherit; font-weight: 800; font-size: .95rem; text-decoration: none; cursor: pointer;
            padding: 7px 14px; border-radius: 999px; border: 2px solid var(--kid-title);
            background: var(--kid-card); color: var(--kid-title); box-shadow: 0 3px 0 var(--kid-shadow); }
    .acct.primary { background: var(--kid-green); border-color: var(--kid-green); color: #fff; }
    .acct:active { transform: translateY(2px); box-shadow: 0 1px 0 var(--kid-shadow); }
    .logo { display: inline-flex; align-items: center; gap: 8px; text-decoration: none; color: var(--kid-title);
            font-size: 1.35rem; font-weight: 900; }
    .mark { width: 40px; height: 40px; }
    main { flex: 1; }
    .foot { display: flex; flex-wrap: wrap; gap: 14px; justify-content: center; align-items: center;
            padding: 14px; font-size: .85rem; opacity: .75; }
    .foot a { color: inherit; }
    select { font: inherit; border-radius: 10px; padding: 2px 6px; border: 1px solid var(--kid-shadow); background: #fff; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
  `],
})
export class KidHubAppComponent implements OnInit {
  private readonly locale = inject(LocaleService);
  /** Hinweis auf eine neue Fassung (Service Worker) — derselbe Dienst wie in RookHub. */
  private readonly appUpdate = inject(AppUpdateService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);
  private readonly handoff = inject(HandoffService);
  private readonly router = inject(Router);

  readonly languages = KIDS_LANGUAGES;
  /** Angemeldet? Als Signal — die Anmeldung kommt asynchron (geteiltes Cookie, Maske). */
  readonly user = toSignal(this.auth.currentUser$, { initialValue: this.auth.currentUser });
  readonly isHome = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(e => isHomeUrl(e.urlAfterRedirects)),
    ),
    { initialValue: isHomeUrl(this.router.url) },
  );
  readonly version = environment.version;
  /** Die TATSAECHLICH aktive Sprache — nicht eine eigene Kopie, die mit ihr auseinanderlaufen kann. */
  readonly lang = computed(() => this.translate.currentLang());

  private readonly api = inject(KidsApiService);
  /** Die Antwort des Servers wird je Seitenaufruf nur EINMAL geholt. */
  private hint: AppLang | null | undefined;
  private hintPending = false;

  constructor() {
    // Nur de/en/hr/hu haben die Kindertexte vollstaendig; jede andere Sprache (Browser, Wahl auf
    // RookHub) stuende hier halb in Englisch. Dann zuerst das Land der IP fragen (AT → de, HU → hu,
    // HR → hr, jedes andere bekannte Land → en), erst ohne Land (LAN, Ausfall) Deutsch — ANGEZEIGT,
    // ohne die Wahl zu ueberschreiben.
    effect(() => {
      const current = this.translate.currentLang();
      if (current && !isKidsLanguage(current)) untracked(() => this.fallBack());
    });
  }

  private fallBack(): void {
    if (this.hint !== undefined) {
      this.locale.applyUnsaved(this.hint ?? 'de');
      return;
    }
    if (this.hintPending) return;
    this.hintPending = true;
    this.api.languageHint().pipe(
      timeout(HINT_TIMEOUT_MS),
      catchError(() => of({ country: null, language: null })),
    ).subscribe(result => {
      this.hintPending = false;
      this.hint = result.language && isKidsLanguage(result.language) ? result.language : null;
      // Hat inzwischen jemand selbst gewaehlt, bleibt es bei dieser Wahl.
      if (!isKidsLanguage(this.translate.currentLang() ?? '')) this.locale.applyUnsaved(this.hint ?? 'de');
    });
  }

  ngOnInit(): void {
    this.locale.init();
    this.appUpdate.start(this.destroyRef);
    // Schon in RookHub oder auf der Turnierseite angemeldet? Das geteilte Cookie gegen eine eigene
    // Anmeldung tauschen (204 = keine, der Normalfall bleibt still).
    void this.handoff.consumeIncoming();
  }

  /** Abmelden fuehrt zurueck auf die Startseite, nicht auf die Anmeldemaske wie in RookHub. */
  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/');
  }

  /** Speichert die Wahl — geraetelokal und im geteilten Cookie, also auch fuer RookHub und die Turnierseite. */
  setLang(code: AppLang): void {
    this.locale.use(code);
  }
}
