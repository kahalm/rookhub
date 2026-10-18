import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { CourseScopeStats } from './course.service';

/** Breite eines Leistenabschnitts in Prozent (0–100, eine Nachkommastelle); 0 bei leerem Kurs. */
export function splitPercent(count: number | undefined, total: number): number {
  if (!total || !count) return 0;
  return Math.round(1000 * Math.min(count, total) / total) / 10;
}

/**
 * Kursfortschritt dreigeteilt (Wunsch 2026-10-15) statt „x/y (3 %)": richtig ohne Hilfe (grün), richtig mit
 * Tipp/Neustart (gelb), aufgegeben (rot); der Rest der Leiste = noch offen. Darunter die drei Zahlen und „von N".
 * Host ist `display: contents`: Leiste (volle Zeile) und Zahlen fließen in die umgebende Flex-Zeile (`.cs-stats`).
 */
@Component({
  selector: 'app-course-progress-split',
  imports: [MatIconModule, MatTooltipModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let s = stats();
    <div class="split-bar" role="img" [attr.aria-label]="label()">
      <span class="seg clean" [style.width.%]="cleanPct()"></span>
      <span class="seg helped" [style.width.%]="helpedPct()"></span>
      <span class="seg gaveup" [style.width.%]="gaveUpPct()"></span>
    </div>
    <span class="lg clean" [matTooltip]="'book.course.split.clean' | translate">
      <mat-icon>check</mat-icon>{{ clean() }}
    </span>
    <span class="lg helped" [matTooltip]="'book.course.split.helped' | translate">
      <mat-icon>lightbulb</mat-icon>{{ s.helpedCount ?? 0 }}
    </span>
    <span class="lg gaveup" [matTooltip]="'book.course.split.gaveUp' | translate">
      <mat-icon>close</mat-icon>{{ s.gaveUpCount ?? 0 }}
    </span>
    <span class="lg total">{{ 'book.course.split.ofTotal' | translate:{ total: s.total } }}</span>
  `,
  styles: [`
    :host { display: contents; }
    .split-bar { flex: 0 0 100%; display: flex; height: 6px; margin-bottom: 2px; border-radius: 3px; overflow: hidden;
      background: color-mix(in srgb, currentColor 14%, transparent); }
    .seg { height: 100%; }
    .seg.clean { background: var(--rh-success); }
    .seg.helped { background: var(--rh-hint); }  /* d4-rk-progress-bulb-light-red */
    .seg.gaveup { background: var(--rh-error); }
    .lg { display: inline-flex; align-items: center; gap: 3px; white-space: nowrap; font-variant-numeric: tabular-nums; }
    .lg mat-icon { width: 16px; height: 16px; font-size: 16px; }
    .lg.clean mat-icon { color: var(--rh-success); }
    .lg.helped mat-icon { color: var(--rh-hint); }
    .lg.gaveup mat-icon { color: var(--rh-error); }
  `],
})
export class CourseProgressSplitComponent {
  readonly stats = input.required<CourseScopeStats>();
  readonly ariaPrefix = input<string>('');

  /** Ältere Antworten ohne Aufschlüsselung: alles Gelöste als „ohne Hilfe" zeigen. */
  readonly clean = computed(() => this.stats().cleanCount ?? this.stats().solvedCount);
  readonly cleanPct = computed(() => splitPercent(this.clean(), this.stats().total));
  readonly helpedPct = computed(() => splitPercent(this.stats().helpedCount, this.stats().total));
  readonly gaveUpPct = computed(() => splitPercent(this.stats().gaveUpCount, this.stats().total));
  readonly label = computed(() => {
    const s = this.stats();
    return `${this.ariaPrefix()} ${this.clean()} / ${s.helpedCount ?? 0} / ${s.gaveUpCount ?? 0} / ${s.total}`.trim();
  });
}
