import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, input, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { SecondsTicker, clockDays, formatLongClock, serverTime } from '../scoresheet-timing';

/** Ab hier gilt das Warten als ungewöhnlich (der Leser schaut jede Minute) — dann sagt eine Zeile, dass er wohl nicht läuft. */
export const LONG_WAIT_SECONDS = 15 * 60;

/**
 * Rückmeldung, wo ein hochgeladenes Partieformular steht (0.817.0, Wunsch 2026-10-21: „erst ‚waiting for agent‘, und wenn's
 * beim Agenten ist hinter der Zeile ein Häkchen und nun ‚reading‘ mit Zeit"). Zwei Schritte, beide mit Uhr:
 *
 * 1. **Wartet auf den Leser** — zählt ab dem Hochladen; der Leser auf dem Server (Watcher) schaut jede Minute nach.
 * 2. Hat er sie abgeholt (`startedAt`, `POST /api/admin/scoresheets/{id}/start`), bekommt Zeile 1 ein ✓ samt der
 *    Wartezeit, und **Wird gelesen** zählt ab dem Abholen.
 *
 * Geteilt von RookHub (Formular hochladen) und LeagueHub (Formular prüfen). Die Uhr läuft nur, solange gewartet/gelesen wird.
 */
@Component({
  selector: 'app-sheet-reading-status',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, TranslatePipe],
  template: `
    <!-- d6-l-wartet-leser-gedankenstrich: die Zeile ist Fließtext (kein Flex) — so bricht sie wortweise um, und der Strich des
         Nachsatzes hängt per U+00A0 vorn an der Uhr und hinten an seinem ersten Wort (DESIGN 5.13) — nie steht eine Zeile mit
         „–“ am Anfang. Die übrigen Leerzeichen zwischen den Teilen stehen deshalb fest im Markup ({{ ' ' }}). Ab einem Tag zählt die Uhr in
         Tagen und Stunden („seit 12 Tagen 10 Std.“ statt „298:59:11“). -->
    <ng-template #clock let-s>{{ days(s) ? (dayKey(s) | translate: days(s)) : longClock(s) }}</ng-template>
    <div class="rs" role="status">
      <p class="rs-line" [class.done]="started()">
        <span class="rs-label">{{ 'sheetEdit.status.waiting' | translate }}</span>{{ ' ' }}
        @if (started()) {
          <span class="rs-check" [attr.aria-label]="'sheetEdit.status.picked' | translate">✓</span>{{ ' ' }}
          <span class="rs-time"><ng-container *ngTemplateOutlet="clock; context: { $implicit: waitSeconds() }" /></span>
        } @else {
          <b class="rs-clock"><ng-container *ngTemplateOutlet="clock; context: { $implicit: waitSeconds() }" /></b>
          <span class="rs-why">{{ 'sheetEdit.status.waitingWhy' | translate }}</span>
        }
      </p>
      @if (started()) {
        <p class="rs-line">
          <span class="rs-label">{{ 'sheetEdit.status.reading' | translate }}</span>{{ ' ' }}
          <b class="rs-clock"><ng-container *ngTemplateOutlet="clock; context: { $implicit: readSeconds() }" /></b>
          <span class="rs-why">{{ 'sheetEdit.status.readingWhy' | translate }}</span>
        </p>
      } @else if (longWait()) {
        <p class="rs-line rs-long">{{ 'sheetEdit.status.longWait' | translate }}</p>
      }
      <ng-content />
    </div>
  `,
  styles: [`
    :host { display: block; }
    .rs { display: grid; gap: 2px; }
    .rs-line { margin: 0; }
    .rs-label { font-weight: 600; }
    .rs-line.done .rs-label { font-weight: 400; opacity: .75; }
    .rs-check { color: var(--ok, var(--rh-success)); font-weight: 700; }
    .rs-time { font-size: .9em; opacity: .75; font-variant-numeric: tabular-nums; }
    .rs-clock { font-variant-numeric: tabular-nums; }
    /* Der Nachsatz: Strich hängt am Folgewort (a1-l-formular-wartezeit, d6-l-wartet-leser-gedankenstrich) */
    .rs-why { font-size: .9em; opacity: .75; }
    .rs-why::before { content: "\\00a0–\\00a0"; }
    .rs-long { font-size: .9em; color: var(--warn, var(--rh-warn)); }
  `],
})
export class SheetReadingStatusComponent implements OnDestroy {
  /** `pending` | `running` (andere Zustände zeigt die Seite selbst). */
  readonly status = input.required<string>();
  readonly createdAt = input<string | null | undefined>(null);
  readonly startedAt = input<string | null | undefined>(null);

  private readonly ticker = new SecondsTicker();
  /** Abgeholt: der Server meldet „läuft" (auch ohne Startzeit, ältere Antworten) oder eine Startzeit. */
  readonly started = computed(() => this.status() === 'running' || !!this.startedAt());
  private readonly createdMs = computed(() => serverTime(this.createdAt()));
  private readonly startedMs = computed(() => serverTime(this.startedAt()));
  readonly waitSeconds = computed(() => {
    const from = this.createdMs();
    if (!Number.isFinite(from)) return 0;
    const to = this.started() && Number.isFinite(this.startedMs()) ? this.startedMs() : this.ticker.now();
    return Math.max(0, Math.floor((to - from) / 1000));
  });
  readonly readSeconds = computed(() => {
    const from = Number.isFinite(this.startedMs()) ? this.startedMs() : this.createdMs();
    return Number.isFinite(from) ? Math.max(0, Math.floor((this.ticker.now() - from) / 1000)) : 0;
  });
  protected readonly days = clockDays;
  /** Schlüssel der Tagesform: „seit 1 Tag", „seit 13 Tagen", jeweils mit oder ohne „10 Std.". */
  protected dayKey(seconds: number): string {
    const d = clockDays(seconds);
    const one = d?.days === 1;
    if (d?.hours) return one ? 'sheetEdit.status.sinceDaysHoursOne' : 'sheetEdit.status.sinceDaysHours';
    return one ? 'sheetEdit.status.sinceDaysOne' : 'sheetEdit.status.sinceDays';
  }
  protected readonly longClock = formatLongClock;
  readonly longWait = computed(() => !this.started() && this.waitSeconds() >= LONG_WAIT_SECONDS);

  constructor() {
    effect(() => {
      const open = this.status() === 'pending' || this.status() === 'running';
      untracked(() => this.ticker.run(open));
    });
  }

  ngOnDestroy(): void { this.ticker.stop(); }
}
