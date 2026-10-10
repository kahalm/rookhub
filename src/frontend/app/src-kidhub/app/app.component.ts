import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, OnInit, afterNextRender, computed, effect, inject, untracked,
} from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AppLang, LocaleService } from '@rh/core/locale.service';
import { AppUpdateService } from '@rh/core/app-update.service';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { FooterPresenceService } from '@rh/shared/app-footer/footer-presence';
import { catchError, filter, map, of, timeout } from 'rxjs';
import { environment } from '../../src/environments/environment';
import { KidsApiService } from './core/kids-api.service';
import { KidsProgressSync } from './core/kids-progress-sync.service';
import { KID_BACK, KID_PAGE_WIDTH, KID_SHORT, KID_STACKED, kidPageWidth } from './shared/kids-layout';
import { KID_PRIVACY_STYLES, foldPrivacy, isPrivacyUrl } from './shared/kids-privacy';

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
    <!-- Die Kopfzeile steht in derselben zentrierten Spalte wie der Inhalt der Seite (UI-Sweep 2026-10-10, k-logo). -->
    <header class="top" [style.--kid-page-width]="pageWidth()">
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
    <main>
      @if (isPrivacy()) {
        <!-- Datenschutz: der Weg zurueck zum Spiel steht oben, nicht erst nach 3 500 px (UI-Sweep 2026-10-10, k-privacy). -->
        <div class="legal-top"><a class="back" routerLink="/">← {{ 'kids.back' | translate }}</a></div>
      }
      <router-outlet />
    </main>
    <footer class="foot">
      <!-- Sprachwahl als runde Pille mit Globus und eigenem Pfeil (UI-Sweep 2026-10-10, k-lang-select); das native
           select bleibt darunter — Tastatur, Vorleser und die Auswahlliste des Handys funktionieren wie gehabt. -->
      <label class="lang">
        <span class="globe" aria-hidden="true">🌐</span>
        <span class="sr-only">{{ 'kids.language' | translate }}</span>
        <!-- [selected] je Option statt [value] am select: die Optionen entstehen erst NACH der
             Bindung, der Wert ging verloren und die Liste zeigte immer den ersten Eintrag („Deutsch"),
             waehrend die Seite Englisch sprach — ein Klick auf Deutsch aenderte dann nichts. -->
        <select (change)="setLang($any($event.target).value)">
          @for (l of languages; track l.code) {
            <option [value]="l.code" [selected]="l.code === lang()">{{ l.label }}</option>
          }
        </select>
        <span class="caret" aria-hidden="true">▾</span>
      </label>
      <span>v{{ version }}</span>
      <a routerLink="/privacy">{{ 'legal.privacy.title' | translate }}</a>
      <!-- Pflichtangabe der Lizenz (CC BY 4.0) der Laenderliste, mit der die Startsprache bestimmt wird. -->
      <a href="https://db-ip.com" target="_blank" rel="noopener">IP Geolocation by DB-IP</a>
    </footer>
  `,
  styles: [KID_BACK, KID_PRIVACY_STYLES, `
    :host {
      --kid-bg: #eaf6ff;
      --kid-card: #ffffff;
      --kid-shadow: rgba(30, 70, 120, .22);
      --kid-title: #1d4f91;
      /* Helles Gruen nur fuer Flaechen OHNE Schrift (geloeste Punkte) — weisse Schrift darauf hat 2,7:1.
         Knoepfe mit Schrift und Rahmen, die etwas anzeigen, nehmen das kraeftige (5,1:1 zu Weiss). */
      --kid-green: #2fb35f;
      --kid-green-strong: #1a7f3e;
      --kid-yellow: #ffcc33;
      --kid-sky: #cfe9ff;
      --kid-peach: #ffe1cc;
      --kid-good-bg: #d6f5de;
      --kid-good-fg: #11632f;
      --kid-bad-bg: #ffe0e0;
      --kid-bad-fg: #9b1c1c;
      --kid-info-bg: #fff3c4;
      /* Brettgroesse am PC: so hoch, wie das Fenster erlaubt (Kopfzeile + Titelzeile ≈ 170px), rechts
         Platz fuer die Spalte daneben. Hier statt im Brett, damit die Titelzeile genauso breit wird
         (--kid-row = Brett + Abstand 28px + Spalte 400px). Die Ansichten stehen in shared/kids-layout.ts. */
      --kid-vh: 1vh;
      --kid-board: max(min(calc(var(--kid-vh) * 100 - 170px), calc(100vw - 500px), 820px), 300px);
      --kid-row: calc(var(--kid-board) + 428px);
      display: flex; flex-direction: column; min-height: 100vh; min-height: 100svh;
      background: radial-gradient(circle at 10% 0%, #fff7d6 0, transparent 40%), var(--kid-bg);
      color: #1f2d3d; font-family: Roboto, "Helvetica Neue", sans-serif;
    }
    @supports (height: 100svh) {
      :host { --kid-vh: 1svh; }
    }
    /* Handy quer: das Brett nimmt die Hoehe ganz (Kopfzeile + EINE Titelzeile ≈ 110px — die Seiten brechen ihre
       Titelzeile dort nicht um —, dazu der Schatten unter dem Brett), daneben die Spalte mit ihrer Mindestbreite
       (280px + Abstand + Rand = 340px). Ohne Untergrenze — 300px liefen bei 390px Fensterhoehe unten aus dem Bild. */
    @media ${KID_SHORT} {
      :host { --kid-board: min(calc(var(--kid-vh) * 100 - 116px), calc(100vw - 340px)); }
    }
    /* Untereinander: das Brett so breit wie moeglich, die Titelzeile genauso breit wie das Brett. */
    @media ${KID_STACKED} {
      :host { --kid-board: min(92vw, 70vh, 640px); --kid-row: var(--kid-board); }
    }
    .top { box-sizing: border-box; width: 100%; max-width: var(--kid-page-width, 980px); margin: 0 auto;
           padding: 10px 16px; display: flex; align-items: center; justify-content: space-between;
           flex-wrap: wrap; gap: 8px 12px; }
    .account { display: flex; align-items: center; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-left: auto; }
    .who { font-weight: 700; color: var(--kid-title); }
    /* Tippziele mindestens 44 px hoch (Codereview 2026-09-29, UX-063: vorher 36 px Konto-Knoepfe, 24/16 px im Fuss). */
    .acct { font: inherit; font-weight: 800; font-size: .95rem; text-decoration: none; cursor: pointer;
            display: inline-flex; align-items: center; box-sizing: border-box; min-height: 44px;
            padding: 7px 14px; border-radius: 999px; border: 2px solid var(--kid-title);
            background: var(--kid-card); color: var(--kid-title); box-shadow: 0 3px 0 var(--kid-shadow); }
    .acct.primary { background: var(--kid-green-strong); border-color: var(--kid-green-strong); color: #fff; }
    .acct:active { transform: translateY(2px); box-shadow: 0 1px 0 var(--kid-shadow); }
    .logo { display: inline-flex; align-items: center; gap: 8px; text-decoration: none; color: var(--kid-title);
            font-size: 1.35rem; font-weight: 900; }
    .mark { width: 40px; height: 40px; }
    main { flex: 1; }
    .foot { display: flex; flex-wrap: wrap; gap: 14px; justify-content: center; align-items: center;
            padding: 14px; font-size: .85rem; opacity: .75; }
    .foot a { color: inherit; display: inline-flex; align-items: center; min-height: 44px; padding: 0 8px; }
    /* Sprachwahl als Pille (k-lang-select): das select ist durchsichtig und fuellt die Pille, der Pfeil liegt darueber
       und laesst Klicks durch. Fokus sichtbar an der ganzen Pille. */
    .lang { position: relative; display: inline-flex; align-items: center; gap: 6px; box-sizing: border-box; min-height: 44px;
            padding: 0 14px; border-radius: 999px; background: #fff; color: #23344a; border: 1px solid #c9d6e6;
            box-shadow: 0 2px 0 var(--kid-shadow); cursor: pointer; }
    .lang:focus-within { outline: 3px solid var(--kid-title); outline-offset: 2px; }
    .lang select { appearance: none; -webkit-appearance: none; font: inherit; font-weight: 700; color: inherit; cursor: pointer;
                   background: transparent; border: 0; outline: none; min-height: 44px; padding: 0 22px 0 0; margin: 0; }
    .caret { position: absolute; right: 14px; pointer-events: none; font-size: .9em; }
    .globe { font-size: 1.1em; line-height: 1; }
    /* Datenschutz: Ruecklink oben in derselben Spalte wie die Seite. */
    .legal-top { box-sizing: border-box; max-width: ${KID_PAGE_WIDTH.legal}px; margin: 0 auto; padding: 8px 16px 0; }
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
  /** Nur injizieren genuegt: der Dienst gleicht den Fortschritt ab, solange jemand angemeldet ist. */
  private readonly progressSync = inject(KidsProgressSync);

  readonly languages = KIDS_LANGUAGES;
  /** Angemeldet? Als Signal — die Anmeldung kommt asynchron (geteiltes Cookie, Maske). */
  readonly user = toSignal(this.auth.currentUser$, { initialValue: this.auth.currentUser });
  /** Die Adresse der angezeigten Seite (nach Umleitungen). */
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(e => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );
  readonly isHome = computed(() => isHomeUrl(this.url()));
  readonly isPrivacy = computed(() => isPrivacyUrl(this.url()));
  /** Breite der Kopfzeile = Breite der Inhaltsspalte dieser Seite (k-logo). */
  readonly pageWidth = computed(() => kidPageWidth(this.url()));
  readonly version = environment.version;
  /** Die TATSAECHLICH aktive Sprache — nicht eine eigene Kopie, die mit ihr auseinanderlaufen kann. */
  readonly lang = computed(() => this.translate.currentLang());

  private readonly api = inject(KidsApiService);
  /** Die Antwort des Servers wird je Seitenaufruf nur EINMAL geholt. */
  private hint: AppLang | null | undefined;
  private hintPending = false;

  constructor() {
    // Die Fusszeile zeigt „Datenschutz" immer — die Anmeldemaske laesst ihre eigene Zeile dann weg (x-login-legal).
    inject(FooterPresenceService).presence.set('always');
    // Datenschutz in der Kinder-Fassung (k-privacy): die geteilte Seite bleibt, wie sie ist; nach dem Zeichnen klappt
    // KidHub die Eltern-Abschnitte zu und hebt das Wichtigste fuer Kinder hervor (`foldPrivacy`).
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const injector = inject(Injector);
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed()).subscribe(e => {
      if (isPrivacyUrl((e as NavigationEnd).urlAfterRedirects)) afterNextRender(() => foldPrivacy(host), { injector });
    });
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
