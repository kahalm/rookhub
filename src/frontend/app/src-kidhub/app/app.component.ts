import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppLang, LocaleService } from '@rh/core/locale.service';
import { AppUpdateService } from '@rh/core/app-update.service';
import { environment } from '../../src/environments/environment';

/** Die vollstaendig uebersetzten Sprachen — nur die bietet die Kinderseite an. */
export const KIDS_LANGUAGES: { code: AppLang; label: string }[] = [
  { code: 'de', label: 'Deutsch' },
  { code: 'en', label: 'English' },
  { code: 'hr', label: 'Hrvatski' },
  { code: 'hu', label: 'Magyar' },
];

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
        <span class="knight" aria-hidden="true">♞</span>
        <span>{{ 'kids.home.title' | translate }}</span>
      </a>
    </header>
    <main><router-outlet /></main>
    <footer class="foot">
      <label class="lang">
        <span class="sr-only">{{ 'kids.language' | translate }}</span>
        <select [value]="lang()" (change)="setLang($any($event.target).value)">
          @for (l of languages; track l.code) {
            <option [value]="l.code">{{ l.label }}</option>
          }
        </select>
      </label>
      <span>v{{ version }}</span>
      <a routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</a>
      <a routerLink="/privacy">{{ 'legal.privacy.title' | translate }}</a>
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
    .knight { font-size: 2rem; line-height: 1; }
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

  readonly languages = KIDS_LANGUAGES;
  readonly version = environment.version;
  readonly lang = signal<AppLang>('de');

  ngOnInit(): void {
    this.locale.init();
    this.lang.set(this.locale.current);
    this.appUpdate.start(this.destroyRef);
  }

  setLang(code: AppLang): void {
    this.locale.use(code);
    this.lang.set(code);
  }
}
