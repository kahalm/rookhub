import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessMaxim, ChessMaximSource, ChessMaximsService, readerJump } from './chess-maxims.service';

/**
 * Der Merksatz unter einer Erklärung (0.917.0, Wunsch 2026-11-09: „mach dahinter immer ein (i), mit dem du auf die
 * Bücher verweist, die das Prinzip erklären, am besten mit direktem Link zur passenden Stelle"). Das (i) klappt die
 * Buchstellen auf: Autor, Titel, Stelle, kurzes Zitat; der Link öffnet das Buch im Leser — bei PDFs direkt auf der Seite.
 */
@Component({
  selector: 'app-maxim-note',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatIconModule, TranslatePipe],
  template: `
    <!-- Nur Sätze der kuratierten Liste — ein früher frei erfundener Merksatz (vor 0.917.0) bleibt unsichtbar. -->
    @if (maxim(); as m) {
    <p class="maxim">
      <span class="label">{{ 'maxims.label' | translate }}</span> {{ m.text }}
        <button type="button" class="info" [class.open]="open()" (click)="open.set(!open())"
                [attr.aria-expanded]="open()" [attr.aria-label]="'maxims.sources' | translate">
          <mat-icon>info_outline</mat-icon>
        </button>
    </p>
    }
    @if (open() && maxim(); as m) {
      <ul class="sources">
        @for (s of m.sources; track $index) {
          <li>
            @if (s.accessible) {
              <a [routerLink]="['/ebooks', s.ebookId]" [queryParams]="jump(s)" target="_blank" rel="noopener">
                {{ s.author }} — <em>{{ s.title }}</em></a>
            } @else { {{ s.author }} — <em>{{ s.title }}</em> }@if (s.year) { ({{ s.year }})}
            @if (s.page) { <span class="where">· {{ 'maxims.page' | translate: { page: s.page } }}</span> }
            @else if (where(s); as w) { <span class="where">· {{ w }}</span> }
            @if (s.quote) { <q>{{ s.quote }}</q> }
          </li>
        }
        <li class="all"><a [routerLink]="['/maxims']" [queryParams]="{ m: m.id }">{{ 'maxims.all' | translate }}</a></li>
      </ul>
    }
  `,
  styles: [`
    :host { display: block; }
    .maxim { margin: 6px 0 2px; font-size: 0.88rem; line-height: 1.45; font-style: italic; }
    .label { font-style: normal; font-weight: 600; }
    .info { all: unset; cursor: pointer; display: inline-flex; vertical-align: middle; margin-left: 2px; border-radius: 50%;
      color: color-mix(in srgb, currentColor 65%, transparent); }
    .info mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .info:hover, .info:focus-visible, .info.open { color: var(--rh-accent, currentColor); }
    .sources { margin: 2px 0 4px; padding-left: 18px; font-size: 0.8rem; line-height: 1.4; }
    .sources li { margin: 3px 0; }
    .sources li.all { list-style: none; margin-left: -18px; }
    .sources a { color: inherit; }
    .where { color: color-mix(in srgb, currentColor 65%, transparent); }
    q { display: block; color: color-mix(in srgb, currentColor 70%, transparent); }
  `],
})
export class MaximNoteComponent {
  /** Der Wortlaut des Merksatzes (aus der Erklärung). */
  readonly text = input.required<string>();
  readonly open = signal(false);
  private readonly all = toSignal(inject(ChessMaximsService).all$, { initialValue: [] as ChessMaxim[] });
  /** Der passende Eintrag der Liste — nur dann gibt es das (i). */
  readonly maxim = computed(() => ChessMaximsService.byText(this.all(), this.text()));

  readonly jump = readerJump;

  /** Stellenangabe eines EPUBs (Kapitel, ~Anteil) ohne den Hinweis auf fehlende Seitenzahlen. */
  where(s: ChessMaximSource): string {
    return (s.location ?? '').replace(/;?\s*E-Book ohne Seitenzahlen, bei ca\.\s*(\d+)\s*%/, (_m, p) => ` ~${p} %`).trim();
  }
}
