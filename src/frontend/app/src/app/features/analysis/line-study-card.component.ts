import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { AnalysisNode, numberedSan } from './analysis-tree';
import { LineComparison, LineVerdict } from './line-study.util';
import { MOVE_CLASS_COLORS } from '../games/game-review.util';
import { MOVE_CLASS_SYMBOLS } from '../games/move-badge.util';
import { contrastText } from '../../shared/contrast-text.util';

/**
 * Leiste „Ohne Engine" am Analysebrett (0.855.0, seit 0.855.1 eine Zeile): solange die Engine aus ist, der Fortschritt der
 * Hintergrundrechnung und „Engine einschalten"; danach die Zähler des Vergleichs, aufklappbar zur Liste — wo die Linie vom besten
 * Zug abwich (Klick springt hin).
 */
@Component({
  selector: 'app-line-study-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="line-study" [class.open]="open()">
      <div class="bar">
        <mat-icon class="icon" aria-hidden="true">{{ revealed() ? 'insights' : 'visibility_off' }}</mat-icon>
        <span class="label" [matTooltip]="revealed() ? '' : ('analysis.lineStudy.hint' | translate)">
          {{ (revealed() ? 'analysis.lineStudy.titleRevealed' : 'analysis.lineStudy.title') | translate }}
        </span>
        <span class="progress">{{ 'analysis.lineStudy.progressShort' | translate:{ done: analyzed(), total: total() } }}</span>
        @if (comparison(); as c) {
          @if (revealed() && c.rated > 0) {
            <button type="button" class="toggle" [attr.aria-expanded]="open()" (click)="open.set(!open())">
              <span class="cnt best">{{ 'analysis.lineStudy.bestShort' | translate:{ best: c.best, rated: c.rated } }}</span>
              @if (c.inaccuracies) { <span class="cnt" [style.color]="color('inaccuracy')">?! {{ c.inaccuracies }}</span> }
              @if (c.mistakes) { <span class="cnt" [style.color]="color('mistake')">? {{ c.mistakes }}</span> }
              @if (c.blunders) { <span class="cnt" [style.color]="color('blunder')">?? {{ c.blunders }}</span> }
              <mat-icon class="chev">{{ open() ? 'expand_less' : 'expand_more' }}</mat-icon>
            </button>
          }
        }
        @if (!revealed()) {
          <button mat-stroked-button type="button" class="reveal" (click)="reveal.emit()">
            <mat-icon>visibility</mat-icon> {{ 'analysis.lineStudy.reveal' | translate }}
          </button>
        }
      </div>
      @if (revealed() && open()) {
        @if (comparison(); as c) {
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
    </div>
  `,
  styles: [`
    /* Schmale Leiste (0.855.1, Wunsch 2026-10-27: „der große Block rechts oben stört") — eine Zeile, Erklärung im Tooltip. */
    .line-study { border: 1px solid color-mix(in srgb, currentColor 18%, transparent); border-radius: 10px; padding: 4px 8px; }
    .bar { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; min-height: 36px; }
    .icon { font-size: 20px; width: 20px; height: 20px; opacity: 0.8; }
    .label { font-weight: 600; }
    .progress { color: var(--mat-sys-on-surface-variant); font-size: 13px; font-variant-numeric: tabular-nums; }
    .reveal { margin-left: auto; height: 32px; }
    .toggle { font: inherit; color: inherit; background: none; border: 0; cursor: pointer; margin-left: auto;
      display: inline-flex; align-items: center; gap: 8px; padding: 2px 4px; border-radius: 6px; }
    .toggle:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .cnt { font-weight: 700; font-size: 13px; }
    .cnt.best { font-weight: 500; }
    .chev { font-size: 20px; width: 20px; height: 20px; }
    .verdicts { list-style: none; margin: 4px 0 2px; padding: 0; display: flex; flex-direction: column; gap: 2px;
      max-height: 220px; overflow-y: auto; }
    .verdict { font: inherit; color: inherit; background: none; border: 0; padding: 3px 6px; border-radius: 6px; cursor: pointer;
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
  /** Vergleichsliste aufgeklappt (zu, bis man auf die Zähler tippt). */
  readonly open = signal(false);

  color(cls: string): string { return MOVE_CLASS_COLORS[cls as keyof typeof MOVE_CLASS_COLORS]; }
  text(cls: string): string { return contrastText(this.color(cls)); }
  symbol(v: LineVerdict): string { return MOVE_CLASS_SYMBOLS[v.cls]; }
  label(node: AnalysisNode): string { return numberedSan(node); }
}
