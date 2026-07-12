import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TrackerDay, SourceBreakdown, ThemeBreakdown, SOURCE_KEYS, THEME_KEYS } from './training-goals.service';
import {
  BreakRow, BreakdownPeriod, BREAKDOWN_PERIODS,
  parseYmd, periodBounds, shiftAnchor, sumBreakdown, breakdownRows,
} from './breakdown.util';
import { formatDuration } from './duration.util';

/**
 * Karte-Fragment „umschaltbare Perioden-Aufschlüsselung" (Tag/Woche/Monat/Jahr/Gesamt + Durchschalten).
 * Aus <c>TrainingGoalsComponent</c> ausgegliedert; bekommt die vollständige Tagesreihe als <c>series</c>-Input
 * und rechnet die Aufschlüsselung + Navigation komplett selbst (rein lesend, kein Output). Wird unter dem
 * Tracker-Heatmap gerendert.
 */
@Component({
  selector: 'app-period-breakdown-card',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatButtonToggleModule, TranslatePipe],
  templateUrl: './period-breakdown-card.component.html',
  styles: [`
    :host { display: block; }
    .period-breakdown { margin-top: 16px; }
    .pb-controls { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px 16px; }
    .pb-periods { font-size: .8rem; }
    .pb-nav { display: flex; align-items: center; gap: 4px; }
    .pb-label { font-size: .9rem; font-weight: 600; min-width: 120px; text-align: center; font-variant-numeric: tabular-nums; }
    .pb-empty { margin-top: 14px; }
    .muted { color: color-mix(in srgb, currentColor 55%, transparent); }
    .small { font-size: .85rem; }
    .breakdowns { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px 28px; }
    .breakdowns.period { margin-top: 16px; }
    .bd-title { font-size: .8rem; font-weight: 600; color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 6px; text-transform: uppercase; letter-spacing: .03em; }
    .bd-row { display: flex; align-items: center; gap: 8px; font-size: .85rem; margin-bottom: 5px; }
    .bd-label { flex: 0 0 32%; }
    .bd-bar { flex: 1; height: 8px; border-radius: 4px; background: color-mix(in srgb, currentColor 10%, transparent); overflow: hidden; }
    .bd-fill { display: block; height: 100%; border-radius: 4px; }
    .bd-fill.src { background: #1976d2; }
    .bd-fill.thm { background: #6a1b9a; }
    .bd-val { flex: 0 0 auto; color: color-mix(in srgb, currentColor 65%, transparent); font-variant-numeric: tabular-nums; min-width: 56px; text-align: right; }
  `],
})
export class PeriodBreakdownCardComponent implements OnChanges {
  /** Vollständige Tagesreihe (ganze Historie) — Grundlage der Periodenberechnung. */
  @Input() series: TrackerDay[] = [];

  readonly periods = BREAKDOWN_PERIODS;
  period: BreakdownPeriod = 'all';
  /** Ein Datum innerhalb der aktuell betrachteten Periode (yyyy-MM-dd). */
  anchor = this.todayDate;
  periodSourceRows: BreakRow[] = [];
  periodThemeRows: BreakRow[] = [];
  periodLabel = '';
  canPrev = false;
  canNext = false;

  constructor(private translate: TranslateService) {}

  ngOnChanges(): void { this.recomputePeriod(); }

  durValue(seconds: number): string { return formatDuration(seconds, this.translate.currentLang()).value; }
  durUnit(seconds: number): string { return formatDuration(seconds, this.translate.currentLang()).unitKey; }

  /** Periode wechseln → Anker auf heute zurücksetzen (man startet bei der jüngsten Periode). */
  setPeriod(period: BreakdownPeriod): void {
    this.period = period;
    this.anchor = this.todayDate;
    this.recomputePeriod();
  }

  /** Eine Periode vor/zurück blättern (−1 = zurück, +1 = vor). */
  navPeriod(dir: number): void {
    this.anchor = shiftAnchor(this.period, this.anchor, dir);
    this.recomputePeriod();
  }

  /** Aufschlüsselung + Navigations-Status + Label für die aktuelle Periode neu berechnen. */
  private recomputePeriod(): void {
    const today = this.todayDate;
    const firstDate = this.series.length ? this.series[0].date : today;
    const { start, end } = periodBounds(this.period, this.anchor, firstDate, today);
    const { bySource, byTheme } = sumBreakdown(this.series, start, end);
    this.periodSourceRows = this.sourceRows(bySource);
    this.periodThemeRows = this.themeRows(byTheme);
    this.canPrev = this.period !== 'all' && start > firstDate;
    this.canNext = this.period !== 'all' && end < today;
    this.periodLabel = this.formatPeriodLabel(start, end);
  }

  private sourceRows(b: SourceBreakdown): BreakRow[] {
    return breakdownRows(b as unknown as Record<string, number>, SOURCE_KEYS);
  }
  private themeRows(b: ThemeBreakdown): BreakRow[] {
    return breakdownRows(b as unknown as Record<string, number>, THEME_KEYS);
  }

  /** Lesbares Label der aktuellen Periode in der aktiven UI-Sprache. */
  private formatPeriodLabel(start: string, end: string): string {
    if (this.period === 'all') return this.translate.instant('trainingGoals.period.all');
    const lang = this.translate.currentLang() || this.translate.getFallbackLang() || 'en';
    const fmt = (s: string, opts: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(lang, opts).format(parseYmd(s));
    if (this.period === 'day') return fmt(start, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' });
    if (this.period === 'week') {
      return `${fmt(start, { day: 'numeric', month: 'short' })} – ${fmt(end, { day: 'numeric', month: 'short', year: 'numeric' })}`;
    }
    if (this.period === 'month') return fmt(start, { year: 'numeric', month: 'long' });
    return fmt(start, { year: 'numeric' }); // year
  }

  /** Lokales Datum als yyyy-MM-dd. */
  get todayDate(): string {
    const d = new Date();
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
  }
}
