import { Component, AfterViewInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { REPCHECK_CHROME_URL, REPCHECK_FIREFOX_URL } from '../../core/community';

/**
 * Ausführliche Hilfe-/Anleitungsseite. Route: /help (offen, kein Login nötig).
 *
 * Die Seitenstruktur (Reihenfolge + Icon je Abschnitt) lebt hier als `SECTIONS`,
 * die Texte ausschließlich in i18n unter `help.s.<id>.t` (Titel) und
 * `help.s.<id>.p` (Array von Absätzen). So bleibt die Seite voll lokalisierbar;
 * fehlende Sprachen fallen wie im Rest der App automatisch auf `en` zurück.
 */
interface HelpSection { id: string; icon: string; }

/**
 * Welcher Hilfe-Abschnitt erklärt welchen Menüeintrag — je Schlüssel aus `MenuRegistry.Items`
 * (Backend, `Services/MenuRegistry.cs`). Ohne diese Zuordnung erschienen neue Menüpunkte
 * (Punktepartie, Partien, Rekonstruieren, Aufgabenblätter …) ohne ein Wort auf /help
 * (Codereview UX-011). `MenuRegistryTests.EveryMenuItem_HasHelpSection` verlangt für JEDEN
 * Registry-Schlüssel einen Eintrag hier; das Spec prüft, dass jedes Ziel ein Abschnitt ist.
 */
export const MENU_HELP: Readonly<Record<string, string>> = {
  'dashboard': 'welcome',
  'repertoires': 'repertoires',
  'tournaments': 'tournaments',
  'tournament-calendar': 'tournaments',
  'friends': 'friends',
  'puzzles': 'puzzles',
  'favorites': 'puzzles',
  'worksheets': 'worksheets',
  'training-goals': 'trainingGoals',
  'analysis': 'analysis',
  'guess': 'guess',
  'games': 'games',
  'reconstruct': 'reconstruct',
  'scoresheet': 'games',
  'remembered': 'chessable',
  'weekly': 'weekly',
  'courses': 'courses',
  'catalog': 'courses',
  'leaderboards': 'leaderboards',
  'stats': 'stats',
  'chessable': 'chessable',
  'install': 'offline',
  'help': 'welcome',
};

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-help',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, RouterModule, TranslatePipe],
  template: `
    <div class="help-container">
      <header class="help-header">
        <h1>{{ 'help.title' | translate }}</h1>
        <p class="subtitle">{{ 'help.subtitle' | translate }}</p>
      </header>

      <nav class="help-toc" [attr.aria-label]="'help.tocTitle' | translate">
        <h2>{{ 'help.tocTitle' | translate }}</h2>
        <ul>
          @for (s of sections; track s.id) {
            <li>
              <a (click)="scrollTo(s.id)">
                <span class="toc-icon">{{ s.icon }}</span>
                <span>{{ 'help.s.' + s.id + '.t' | translate }}</span>
              </a>
            </li>
          }
        </ul>
      </nav>

      @for (s of sections; track s.id) {
        <mat-card [id]="s.id" class="help-section">
          <mat-card-header>
            <mat-card-title>
              <span class="sec-icon">{{ s.icon }}</span>{{ 'help.s.' + s.id + '.t' | translate }}
            </mat-card-title>
          </mat-card-header>
          <mat-card-content>
            @for (p of asParagraphs('help.s.' + s.id + '.p' | translate: textParams); track $index) {
              <p [innerHTML]="linkify(p)"></p>
            }
            <a class="back-top" (click)="scrollTop()">
              <mat-icon>arrow_upward</mat-icon>{{ 'help.backToTop' | translate }}
            </a>
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .help-container { max-width: 860px; margin: 0 auto; padding: 1.5rem 1rem 3rem; }
    .help-header { text-align: center; margin-bottom: 1.5rem; }
    .help-header h1 { margin: 0 0 0.25rem; }
    .subtitle { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0; }

    .help-toc { margin-bottom: 1.5rem; }
    .help-toc h2 { font-size: 1rem; text-transform: uppercase; letter-spacing: 0.05em;
                   color: color-mix(in srgb, currentColor 55%, transparent); margin: 0 0 0.5rem; }
    .help-toc ul { list-style: none; padding: 0; margin: 0;
                   display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 4px; }
    .help-toc a { display: flex; align-items: center; gap: 8px; padding: 6px 8px;
                  border-radius: 6px; cursor: pointer; color: inherit; text-decoration: none; }
    .help-toc a:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .toc-icon { width: 1.4em; text-align: center; }

    .help-section { margin-bottom: 1rem; scroll-margin-top: 80px; }
    .sec-icon { margin-right: 0.5rem; }
    /* Lange Adressen (Linktext) dürfen umbrechen — sonst machte EINE URL die ganze Seite am Handy
       breiter als den Bildschirm (Codereview UX-013: 222 px seitlicher Überlauf). */
    mat-card-content p { line-height: 1.55; margin: 0 0 0.75rem; overflow-wrap: anywhere; }

    .back-top { display: inline-flex; align-items: center; gap: 4px; cursor: pointer;
                font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent);
                margin-top: 0.25rem; }
    .back-top:hover { color: inherit; }
    .back-top mat-icon { font-size: 1rem; width: 1rem; height: 1rem; }
  `]
})
export class HelpComponent implements AfterViewInit {
  constructor(private route: ActivatedRoute) {}

  /** Deep-Link wie /help#extension: nach dem Render zum Abschnitt scrollen. */
  ngAfterViewInit(): void {
    const fragment = this.route.snapshot.fragment;
    if (fragment) {
      setTimeout(() => this.scrollTo(fragment), 0);
    }
  }

  /** Reihenfolge = die Gruppen des ☰-Menüs (Training, Analyse & Sammlung, Community, Konto). */
  readonly sections: HelpSection[] = [
    { id: 'welcome', icon: '\u{1F44B}' },
    { id: 'account', icon: '\u{1F511}' },
    // Training
    { id: 'puzzles', icon: '\u{265F}' },
    { id: 'endless', icon: '\u{267E}' },
    { id: 'daily', icon: '\u{1F4C5}' },
    { id: 'courses', icon: '\u{1F4DA}' },
    { id: 'weekly', icon: '\u{1F4F0}' },
    { id: 'trainingGoals', icon: '\u{1F3AF}' },
    { id: 'repertoires', icon: '\u{1F5C2}' },
    { id: 'guess', icon: '\u{1F914}' },
    { id: 'worksheets', icon: '\u{1F5A8}' },
    // Analyse & Sammlung
    { id: 'analysis', icon: '\u{1F52C}' },
    { id: 'games', icon: '\u{1F4DD}' },
    { id: 'reconstruct', icon: '\u{1F9E0}' },
    // Community
    { id: 'friends', icon: '\u{1F91D}' },
    { id: 'tournaments', icon: '\u{1F3C6}' },
    { id: 'leaderboards', icon: '\u{1F947}' },
    { id: 'discord', icon: '\u{1F4AC}' },
    // Konto
    { id: 'profile', icon: '\u{1F464}' },
    { id: 'stats', icon: '\u{1F4C8}' },
    { id: 'chessable', icon: '\u{1F4D6}' },
    { id: 'offline', icon: '\u{1F4F2}' },
    { id: 'settings', icon: '\u{1F3A8}' },
    { id: 'tokens', icon: '\u{1F50C}' },
    { id: 'extension', icon: '\u{1F9E9}' },
    { id: 'privacy', icon: '\u{1F512}' },
    { id: 'feedback', icon: '\u{1F41E}' },
  ];

  /**
   * Platzhalter in den Absätzen: die Store-Adressen der Erweiterung stehen EINMAL im Code statt als
   * Literal in vier Sprachdateien (dort stand Firefox mit festem Sprachkürzel, Codereview F5-015).
   */
  readonly textParams = { chromeUrl: REPCHECK_CHROME_URL, firefoxUrl: REPCHECK_FIREFOX_URL };

  /** Der `translate`-Pipe liefert für ein JSON-Array das Array zurück; defensiv normalisieren. */
  asParagraphs(value: unknown): string[] {
    if (Array.isArray(value)) return value as string[];
    return value ? [String(value)] : [];
  }

  /** `[Linktext](https://…)` ODER eine nackte http(s)-Adresse (abschließende Satzzeichen bleiben draußen). */
  private static readonly LINK_RE = /\[([^\]\n]+)\]\((https?:\/\/[^\s<)]+)\)|(https?:\/\/[^\s<]+[^\s<.,;:!?)\]])/g;

  /**
   * Wandelt Links in einem Absatz in klickbare Anker: `[Linktext](https://…)` mit sprechendem Text
   * (lange Store-Adressen sind als Linktext unlesbar, UX-013) und nackte http(s)-URLs wie bisher.
   * Der Text wird zuerst HTML-escaped (kein Markup aus i18n durchlassen), dann werden die Links
   * durch Anker ersetzt; das Ergebnis bindet das Template per [innerHTML] (Angular sanitisiert,
   * behält aber a[href][target]). Daher kein DomSanitizer/bypass nötig.
   */
  linkify(text: string): string {
    const escaped = (text ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
    return escaped.replace(
      HelpComponent.LINK_RE,
      (_match, label: string | undefined, labelUrl: string | undefined, bareUrl: string | undefined) => {
        const url = labelUrl ?? bareUrl ?? '';
        return `<a href="${url}" target="_blank" rel="noopener noreferrer">${label ?? url}</a>`;
      },
    );
  }

  scrollTo(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  scrollTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
