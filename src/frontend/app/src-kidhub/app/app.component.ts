import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, untracked } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AppLang, LocaleService } from '@rh/core/locale.service';
import { AppUpdateService } from '@rh/core/app-update.service';
import { catchError, of, timeout } from 'rxjs';
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

/**
 * Huelle der Kinderseite: eine schlichte Kopfzeile (Logo = zurueck zum Start), der Inhalt, unten
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
    .top { padding: 10px 16px; }
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

  readonly languages = KIDS_LANGUAGES;
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
    // HR → hr, GB/US → en …), erst ohne Treffer Deutsch — ANGEZEIGT, ohne die Wahl zu ueberschreiben.
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
  }

  /** Speichert die Wahl — geraetelokal und im geteilten Cookie, also auch fuer RookHub und die Turnierseite. */
  setLang(code: AppLang): void {
    this.locale.use(code);
  }
}
