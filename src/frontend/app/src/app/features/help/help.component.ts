import { Component, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

/**
 * Ausführliche Hilfe-/Anleitungsseite. Route: /help (offen, kein Login nötig).
 *
 * Die Seitenstruktur (Reihenfolge + Icon je Abschnitt) lebt hier als `SECTIONS`,
 * die Texte ausschließlich in i18n unter `help.s.<id>.t` (Titel) und
 * `help.s.<id>.p` (Array von Absätzen). So bleibt die Seite voll lokalisierbar;
 * fehlende Sprachen fallen wie im Rest der App automatisch auf `en` zurück.
 */
interface HelpSection { id: string; icon: string; }

@Component({
  selector: 'app-help',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, RouterModule, TranslateModule],
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
            @for (p of asParagraphs('help.s.' + s.id + '.p' | translate); track $index) {
              <p>{{ p }}</p>
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
    mat-card-content p { line-height: 1.55; margin: 0 0 0.75rem; }

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

  readonly sections: HelpSection[] = [
    { id: 'welcome', icon: '\u{1F44B}' },
    { id: 'account', icon: '\u{1F511}' },
    { id: 'profile', icon: '\u{1F464}' },
    { id: 'discord', icon: '\u{1F4AC}' },
    { id: 'friends', icon: '\u{1F91D}' },
    { id: 'tournaments', icon: '\u{1F3C6}' },
    { id: 'puzzles', icon: '\u{265F}' },
    { id: 'endless', icon: '\u{267E}' },
    { id: 'daily', icon: '\u{1F4C5}' },
    { id: 'courses', icon: '\u{1F4DA}' },
    { id: 'weekly', icon: '\u{1F4F0}' },
    { id: 'trainingGoals', icon: '\u{1F3AF}' },
    { id: 'stats', icon: '\u{1F4C8}' },
    { id: 'analysis', icon: '\u{1F52C}' },
    { id: 'repertoires', icon: '\u{1F5C2}' },
    { id: 'offline', icon: '\u{1F4F2}' },
    { id: 'settings', icon: '\u{1F3A8}' },
    { id: 'tokens', icon: '\u{1F50C}' },
    { id: 'extension', icon: '\u{1F9E9}' },
    { id: 'privacy', icon: '\u{1F512}' },
    { id: 'feedback', icon: '\u{1F41E}' },
  ];

  /** Der `translate`-Pipe liefert für ein JSON-Array das Array zurück; defensiv normalisieren. */
  asParagraphs(value: unknown): string[] {
    if (Array.isArray(value)) return value as string[];
    return value ? [String(value)] : [];
  }

  scrollTo(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  scrollTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
