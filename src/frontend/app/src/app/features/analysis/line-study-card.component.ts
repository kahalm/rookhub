import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { AnalysisNode, numberedSan } from './analysis-tree';
import { LineComparison, LineVerdict } from './line-study.util';
import { MOVE_CLASS_COLORS } from '../games/game-review.util';
import { MOVE_CLASS_SYMBOLS } from '../games/move-badge.util';
import { contrastText } from '../../shared/contrast-text.util';

/**
 * Karte „Ohne Engine" am Analysebrett (0.855.0): solange die Engine aus ist, der Fortschritt der Hintergrundrechnung und
 * „Engine einschalten"; danach der Vergleich — wie viele eigene Züge der beste waren und wo die Linie abwich (Klick springt hin).
 */
@Component({
  selector: 'app-line-study-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <mat-card class="line-study">
      <mat-card-content>
        <div class="head">
          <mat-icon aria-hidden="true">{{ revealed() ? 'insights' : 'visibility_off' }}</mat-icon>
          <strong>{{ (revealed() ? 'analysis.lineStudy.titleRevealed' : 'analysis.lineStudy.title') | translate }}</strong>
        </div>
        <p class="progress">{{ 'analysis.lineStudy.progress' | translate:{ done: analyzed(), total: total() } }}</p>
        @if (!revealed()) {
          <p class="hint">{{ 'analysis.lineStudy.hint' | translate }}</p>
          <button mat-flat-button color="primary" type="button" class="reveal" (click)="reveal.emit()">
            <mat-icon>visibility</mat-icon> {{ 'analysis.lineStudy.reveal' | translate }}
          </button>
        } @else if (comparison(); as c) {
          @if (c.rated === 0) {
            <p class="hint">{{ 'analysis.lineStudy.noOwnMoves' | translate }}</p>
          } @else {
            <p class="summary">{{ 'analysis.lineStudy.summary' | translate:{ rated: c.rated, best: c.best } }}
              @if (c.inaccuracies) { <span class="cnt" [style.color]="color('inaccuracy')">?! {{ c.inaccuracies }}</span> }
              @if (c.mistakes) { <span class="cnt" [style.color]="color('mistake')">? {{ c.mistakes }}</span> }
              @if (c.blunders) { <span class="cnt" [style.color]="color('blunder')">?? {{ c.blunders }}</span> }
            </p>
            <ul class="verdicts">
              @for (v of c.verdicts; track v.node) {
                <li>
                  <button type="button" class="verdict" (click)="jump.emit(v.node)">
                    <span class="mark" [style.background]="color(v.cls)" [style.color]="text(v.cls)">{{ symbol(v) }}</span>
                    <span class="move">{{ label(v.node) }}</span>
                    <span class="eval">{{ v.evalText }}</span>
                    @if (v.bestSan) { <span class="better">{{ 'analysis.lineStudy.better' | translate:{ san: v.bestSan } }}</span> }
                  </button>
                </li>
              }
            </ul>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .head { display: flex; align-items: center; gap: 8px; }
    .progress, .hint, .summary { margin: 6px 0; }
    .hint { color: var(--mat-sys-on-surface-variant); }
    .reveal { margin-top: 4px; }
    .cnt { margin-left: 8px; font-weight: 700; }
    .verdicts { list-style: none; margin: 8px 0 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
    .verdict { font: inherit; color: inherit; background: none; border: 0; padding: 4px 6px; border-radius: 6px; cursor: pointer;
      display: flex; flex-wrap: wrap; align-items: center; gap: 8px; width: 100%; text-align: left; }
    .verdict:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .mark { font-weight: 700; font-size: 12px; border-radius: 8px; padding: 0 6px; line-height: 18px; }
    .move { font-weight: 600; }
    .eval { font-variant-numeric: tabular-nums; color: var(--mat-sys-on-surface-variant); }
    .better { color: var(--mat-sys-on-surface-variant); }
  `],
})
export class LineStudyCardComponent {
  readonly revealed = input(false);
  readonly analyzed = input(0);
  readonly total = input(0);
  readonly comparison = input<LineComparison | null>(null);
  readonly reveal = output<void>();
  readonly jump = output<AnalysisNode>();

  color(cls: string): string { return MOVE_CLASS_COLORS[cls as keyof typeof MOVE_CLASS_COLORS]; }
  text(cls: string): string { return contrastText(this.color(cls)); }
  symbol(v: LineVerdict): string { return MOVE_CLASS_SYMBOLS[v.cls]; }
  label(node: AnalysisNode): string { return numberedSan(node); }
}
