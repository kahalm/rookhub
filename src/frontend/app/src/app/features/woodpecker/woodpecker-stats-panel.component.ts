import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Subject, catchError, map, of, switchMap, timer } from 'rxjs';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { PctPipe } from '../../shared/format/pct';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { WoodpeckerPlan, WoodpeckerService, WoodpeckerStats, WoodpeckerStatsCycle } from './woodpecker.service';

/** Wie lange nach einer gemeldeten Aufgabe gewartet wird, bevor `/stats` neu geholt wird (mehrere schnelle Meldungen = ein Abruf). */
export const WOODPECKER_STATS_DEBOUNCE_MS = 800;
/** Spiegel von `WoodpeckerStatsCalculator.MinForecastSample`. */
export const WOODPECKER_MIN_FORECAST_SAMPLE = 10;
/** Höchstens so viele Tageszeilen (die jüngsten zuerst). */
export const WOODPECKER_STATS_MAX_DAYS = 14;

/** Antwort oder Fehler eines Abrufs. */
type Result = { stats: WoodpeckerStats; error: null } | { stats: null; error: unknown };

/**
 * Ausführliche Woodpecker-Statistik im Löser (0.957.0, Wunsch 10.11.: „momentan 1 h für 50 Puzzle, die gleichen Puzzle
 * haben letztes Mal 2 h gebraucht“ + Tipp-Auswertung). Steht nur im AUFGEKLAPPTEN Statistik-Block der Kontext-Karte
 * (DESIGN 5.12); wird erst dann erzeugt und lädt `GET /api/woodpecker/{id}/stats` — beim Erzeugen und nach jeder
 * gemeldeten Aufgabe (neuer Planstand), entprellt. Gerechnet ist alles am Server (`WoodpeckerStatsCalculator`), hier
 * nur Darstellung: Abschnitte Dieselben Aufgaben · Wechsel · Hochrechnung · Tipps · Verlauf · (Zyklus 1: je Tag) ·
 * Sorgenkinder.
 */
@Component({
  selector: 'app-woodpecker-stats-panel',
  imports: [DatePipe, DecimalPipe, PctPipe, RouterLink, TranslatePipe, LoadErrorComponent, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './woodpecker-stats-panel.component.html',
  styleUrl: './woodpecker-stats-panel.component.scss',
})
export class WoodpeckerStatsPanelComponent {
  private readonly service = inject(WoodpeckerService);

  readonly plan = input<WoodpeckerPlan | null>(null);

  readonly stats = signal<WoodpeckerStats | null>(null);
  readonly error = signal<unknown>(null);
  readonly loading = signal(false);
  readonly clock = formatSecondsClock;
  /** Spiegel von `WoodpeckerStatsCalculator.MinForecastSample` (Server): erst ab so vielen gemeinsamen Aufgaben wird hochgerechnet. */
  readonly minSample = WOODPECKER_MIN_FORECAST_SAMPLE;

  /** Planstand, der neu laden lässt: Plan, Zyklus und Zahl der bearbeiteten Aufgaben. */
  private readonly key = computed(() => {
    const p = this.plan();
    if (!p) return null;
    const c = p.cycles[p.cycles.length - 1];
    return `${p.id}:${p.currentCycle}:${c?.attempted ?? 0}`;
  });
  private readonly reload$ = new Subject<{ planId: number; immediate: boolean }>();
  private lastKey: string | null = null;
  private lastPlanId: number | null = null;

  constructor() {
    this.reload$.pipe(
      // Entprellen: ein neuer Stand vor Ablauf ersetzt den wartenden (switchMap); `of(r).pipe(debounceTime)` gäbe sofort
      // aus, weil `of` gleich abschließt.
      switchMap(r => (r.immediate ? of(r) : timer(WOODPECKER_STATS_DEBOUNCE_MS).pipe(map(() => r)))),
      switchMap(r => {
        this.loading.set(true);
        return this.service.stats(r.planId).pipe(
          map((stats): Result => ({ stats, error: null })),
          catchError(error => of<Result>({ stats: null, error })),   // innen: der Strom lebt nach einem Fehler weiter
        );
      }),
      takeUntilDestroyed(inject(DestroyRef)),
    ).subscribe(r => {
      this.loading.set(false);
      this.error.set(r.error);
      if (r.stats) this.stats.set(r.stats);
    });

    effect(() => {
      const k = this.key();
      const p = this.plan();
      if (!k || !p || k === this.lastKey) return;
      const first = this.lastPlanId !== p.id;   // erster Abruf bzw. anderer Plan: sofort, sonst entprellt
      this.lastKey = k;
      this.lastPlanId = p.id;
      this.reload$.next({ planId: p.id, immediate: first });
    });
  }

  retry(): void {
    const p = this.plan();
    if (p) this.reload$.next({ planId: p.id, immediate: true });
  }

  /** Vorzyklus-Zeile mit dem Vergleich auf denselben Aufgaben (Zyklus 2+). */
  readonly prevRow = computed<WoodpeckerStatsCycle | null>(() => {
    const s = this.stats();
    return s && s.currentCycle > 1 ? s.cycles.find(c => c.number === s.currentCycle - 1) ?? null : null;
  });

  readonly current = computed<WoodpeckerStatsCycle | null>(() => {
    const s = this.stats();
    return s ? s.cycles.find(c => c.number === s.currentCycle) ?? null : null;
  });

  /** Ältere Zyklen als der Vorzyklus mit gemeinsamen Aufgaben (je eine kurze Zeile). */
  readonly olderOverlaps = computed(() => {
    const s = this.stats();
    if (!s) return [];
    return s.cycles.filter(c => c.number < s.currentCycle - 1 && (c.sameAsNow?.count ?? 0) > 0).map(c => c.sameAsNow!);
  });

  /** Tage der Laufzeit: jüngste zuerst, als Datum für die DatePipe (lokale Mitternacht aus `yyyy-MM-dd`). */
  readonly days = computed(() => {
    const s = this.stats();
    if (!s) return [];
    return [...s.days].reverse().slice(0, WOODPECKER_STATS_MAX_DAYS).map(d => {
      const [y, m, day] = d.date.split('-').map(Number);
      return { ...d, at: new Date(y, m - 1, day), ratePct: d.attempted ? (100 * d.solved) / d.attempted : null };
    });
  });

  /** Tipp-Daten des Stichtags als Datum. */
  readonly hintsSince = computed(() => {
    const s = this.stats();
    return s ? new Date(s.hintsTrackedSince) : null;
  });

  /** Hat der Zyklus für ALLE Versuche Tipp-Daten? Sonst ist „ohne Tipp“ nicht belastbar und steht als „–“. */
  hintsComplete(c: WoodpeckerStatsCycle): boolean { return c.attempted > 0 && c.hintsTrackedCount >= c.attempted; }

  /** Anteil in Prozentpunkten (für `| pct`); `null` ohne Grundmenge. */
  pctOf(part: number, whole: number): number | null { return whole > 0 ? (100 * part) / whole : null; }
}
