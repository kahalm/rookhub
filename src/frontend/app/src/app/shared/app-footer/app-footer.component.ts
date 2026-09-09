import { Component, HostListener, Input, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DomSanitizer } from '@angular/platform-browser';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { A11yModule } from '@angular/cdk/a11y';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { environment } from '../../../environments/environment';
import { ChangelogEntry } from '../../../environments/changelog';
import { DISCORD_INVITE_URL, DISCORD_SVG, KOFI_URL } from '../../core/community';
import { partnerSiteUrl } from '../../core/partner-site';

/**
 * Die Fusszeile — und das Changelog-Overlay, das ihr Versionslink oeffnet.
 *
 * <p>Beide Oberflaechen benutzen DIESE Komponente (RookHub und die Turnierseite ueber `@rh/*`).
 * Vorher lag sie im Template von RookHubs `AppComponent`, die Turnierseite hatte gar keine —
 * eine Kopie waere die naechste Stelle gewesen, die auseinanderlaeuft.</p>
 *
 * <p>Der Hilfe-Link ist der einzige Teil, der sich unterscheiden MUSS: die Hilfeseite gibt es nur
 * in RookHub. Gefragt wird die Routentabelle, nicht der Hostname — das stimmt auch auf einem
 * Entwicklungsserver, wo die Adresse nichts verraet. Fehlt die Route und gibt es keine
 * Schwesterseite (localhost, IP), entfaellt der Link ganz, statt ins Leere zu zeigen.</p>
 */
@Component({
  selector: 'app-footer',
  standalone: true,
  imports: [RouterLink, MatIconModule, A11yModule, TranslatePipe],
  template: `
    <footer class="app-footer" [class.hide-on-mobile]="hideOnMobile">
      <span class="version-link" role="button" tabindex="0"
            [attr.aria-label]="'app.changelogTitle' | translate"
            (click)="toggleChangelog()"
            (keydown.enter)="toggleChangelog()"
            (keydown.space)="$event.preventDefault(); toggleChangelog()">v{{ version }}@if (!production) { <span class="dev-badge">dev</span>}</span>
      @if (helpRoute) {
        <span class="footer-sep">·</span>
        <a class="feedback-link" routerLink="/help">{{ 'nav.help' | translate }}</a>
      } @else if (helpHref) {
        <span class="footer-sep">·</span>
        <a class="feedback-link" [href]="helpHref">{{ 'nav.help' | translate }}</a>
      }
      <span class="footer-sep">·</span>
      <a class="feedback-link" href="https://github.com/kahalm/rookhub/issues" target="_blank" rel="noopener noreferrer">{{ 'app.feedback' | translate }}</a>
      <span class="footer-sep">·</span>
      <a class="discord-link" [href]="discordUrl" target="_blank" rel="noopener noreferrer"
         [attr.aria-label]="'nav.discord' | translate">
        <mat-icon svgIcon="discord" aria-hidden="true"></mat-icon><span>{{ 'nav.discord' | translate }}</span>
      </a>
      <span class="footer-sep">·</span>
      <a class="kofi-link" [href]="kofiUrl" target="_blank" rel="noopener noreferrer"
         [attr.aria-label]="'nav.support' | translate">
        <mat-icon aria-hidden="true">local_cafe</mat-icon><span>{{ 'nav.support' | translate }}</span>
      </a>
    </footer>

    @if (showChangelog) {
      <div class="changelog-overlay" (click)="showChangelog = false">
        <div class="changelog-content" (click)="$event.stopPropagation()"
             role="dialog" aria-modal="true" [attr.aria-label]="'app.changelogTitle' | translate" cdkTrapFocus>
          <div class="changelog-header">
            <h3>{{ 'app.changelogTitle' | translate }}</h3>
            <button (click)="showChangelog = false" [attr.aria-label]="'common.close' | translate" cdkFocusInitial>&times;</button>
          </div>
          @for (entry of changelog; track entry.version) {
            <div class="changelog-entry">
              <strong>v{{ entry.version }}</strong> <span class="changelog-date">{{ entry.date }}</span>
              <ul>
                @for (change of entry.changes; track change.en) {
                  <li>{{ changeText(change) }}</li>
                }
              </ul>
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    .app-footer { text-align: center; padding: 8px; color: color-mix(in srgb, currentColor 47%, transparent); font-size: 0.75rem; }
    /* Am Handy ausblenden ist RookHubs Vorgabe (das Menue traegt Hilfe/Changelog/Discord
       selbst). Die Turnierseite hat keinen solchen Ersatz und schaltet es per Input ab —
       die Klasse traegt die Absicht der Huelle, nicht ein Selektor auf ein Wurzel-Tag. */
    @media (max-width: 768px) { .app-footer.hide-on-mobile { display: none; } }
    @media (max-width: 768px) {
      /* Wo die Fusszeile am Handy stehen bleibt (Turnierseite), sind ihre Links der einzige Weg
         zu Version, Hilfe und Rueckmeldung — als 14 px hoher Fliesstext waren sie kaum zu
         treffen. Padding statt groesserer Schrift: die Zeile bleibt eine Fusszeile. Auf RookHub
         ist sie hier ohnehin ausgeblendet, die Regel wirkt dort nicht. */
      .app-footer a, .app-footer .version-link { display: inline-block; padding: 10px 4px; }
    }
    .version-link { cursor: pointer; }
    .version-link:hover { color: color-mix(in srgb, currentColor 65%, transparent); text-decoration: underline; }
    .footer-sep { margin: 0 6px; color: color-mix(in srgb, currentColor 40%, transparent); }
    .discord-link {
      display: inline-flex; align-items: center; gap: 4px; vertical-align: middle;
      color: #5865F2; font-weight: 600; text-decoration: none;
    }
    .discord-link:hover { color: #4752c4; text-decoration: underline; }
    .discord-link mat-icon {
      font-size: 1.05rem; width: 1.05rem; height: 1.05rem; line-height: 1.05rem;
    }
    .discord-link mat-icon svg { display: block; width: 100%; height: 100%; }
    .kofi-link {
      display: inline-flex; align-items: center; gap: 4px; vertical-align: middle;
      color: #ff5e5b; font-weight: 600; text-decoration: none;
    }
    .kofi-link:hover { color: #e04b48; text-decoration: underline; }
    .kofi-link mat-icon {
      font-size: 1.05rem; width: 1.05rem; height: 1.05rem; line-height: 1.05rem;
    }
    .feedback-link { color: inherit; text-decoration: none; }
    .feedback-link:hover { color: color-mix(in srgb, currentColor 65%, transparent); text-decoration: underline; }
    .dev-badge { color: #ff9800; font-weight: bold; margin-left: 4px; }
    /* Das Overlay liegt in DIESER Ansicht — die gleichnamigen Regeln in RookHubs AppComponent
       sind dort gekapselt und erreichen es nicht: der Changelog stand ungestylt im Seitenfluss
       unter der Fusszeile (kein Abdunkeln, kein Kasten, kein Scrollen). AppComponent behaelt
       ihre Kopie fuer das Quickstart-Overlay. */
    .changelog-overlay {
      position: fixed; inset: 0; background: rgba(0,0,0,0.5);
      display: flex; align-items: center; justify-content: center; z-index: 1000;
    }
    .changelog-content {
      background: #1e1e1e; color: #ccc; border-radius: 8px; padding: 24px;
      max-width: 500px; width: min(90%, calc(100vw - 32px)); max-height: 80vh; overflow-y: auto;
    }
    .changelog-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .changelog-header h3 { margin: 0; color: #fff; }
    .changelog-header button {
      background: none; border: none; color: color-mix(in srgb, currentColor 47%, transparent); font-size: 1.5rem; cursor: pointer;
    }
    .changelog-header button:hover { color: inherit; }
    .changelog-entry { margin-bottom: 12px; }
    .changelog-date { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.85rem; margin-left: 8px; }
    .changelog-entry ul { margin: 4px 0 0 20px; padding: 0; }
    .changelog-entry li { font-size: 0.85rem; margin-bottom: 2px; }
  `],
})
export class AppFooterComponent {
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);

  readonly version = environment.version;
  readonly production = environment.production;
  readonly discordUrl = DISCORD_INVITE_URL;
  readonly kofiUrl = KOFI_URL;

  /**
   * Bis 768px ausblenden? Vorgabe ja (RookHub: das Menue fuehrt zu Hilfe, Changelog und
   * Discord). Die Turnierseite setzt false — dort waere die Fusszeile am Handy sonst der
   * einzige, aber unsichtbare Weg zu Version, Hilfe und Rueckmeldung gewesen.
   */
  @Input() hideOnMobile = true;

  /** Hat DIESE App eine eigene Hilfeseite? (RookHub ja, die Turnierseite nein.) */
  readonly helpRoute = this.router.config.some(r => r.path === 'help');
  /** Sonst die Hilfe der Schwesterseite — oder gar kein Link. */
  readonly helpHref = this.helpRoute ? null : partnerSiteUrl() && `${partnerSiteUrl()}/help`;

  /**
   * Changelog-Eintraege — LEER bis zum ersten Oeffnen: das Array (~0,9 MB Prosa,
   * changelog-data.ts) kommt per dynamic import() und darf nicht im Initial-Bundle liegen.
   */
  changelog: ChangelogEntry[] = [];
  showChangelog = false;

  /** Laufender Nachlade-Vorgang — als Feld, damit Tests den async-Ablauf awaiten koennen. */
  changelogLoad?: Promise<void>;

  /** Overlay oeffnen (Navbar-Menue) — laedt die Eintraege beim ersten Oeffnen nach. */
  openChangelog(): void {
    this.showChangelog = true;
    this.changelogLoad = this.loadChangelog();
  }

  /** Overlay per Versionslink auf-/zuklappen. */
  toggleChangelog(): void {
    this.showChangelog = !this.showChangelog;
    if (this.showChangelog) this.changelogLoad = this.loadChangelog();
  }

  /** Escape schliesst das Overlay — dieselbe Tastatur-Bedienbarkeit wie vorher in der App-Hülle. */
  @HostListener('document:keydown.escape')
  onEscape(): void { this.showChangelog = false; }

  changeText(change: { en: string; de: string }): string {
    return this.translate.currentLang() === 'de' ? change.de : change.en;
  }

  /**
   * Lazy laden: erst beim Oeffnen, genau einmal. Ein Fehlschlag (offline ohne SW-Cache, Chunk
   * nach einem Deploy weg) bleibt still — das Overlay zeigt dann nur den Kopf, und der naechste
   * Versuch laedt erneut (kein „geladen"-Merker im Fehlerfall).
   */
  constructor(iconRegistry: MatIconRegistry, sanitizer: DomSanitizer) {
    // Das Discord-Logo hier SELBST anmelden, damit die Fusszeile in jeder App fuer sich
    // funktioniert — die Turnierseite hat keine Navbar, die es sonst nebenbei anmeldet.
    // Doppelte Anmeldung mit demselben Literal ist harmlos (RookHubs AppComponent macht es
    // zusaetzlich, damit die Navbar es unabhaengig von der Reihenfolge schon hat).
    iconRegistry.addSvgIconLiteral('discord', sanitizer.bypassSecurityTrustHtml(DISCORD_SVG));
  }

  private async loadChangelog(): Promise<void> {
    if (this.changelog.length > 0) return;
    try {
      const m = await import('../../../environments/changelog-data');
      this.changelog = m.CHANGELOG;
    } catch {
      /* still: das Overlay bleibt bis zum naechsten Versuch leer */
    }
  }
}
