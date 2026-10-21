import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, input, untracked } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { SecondsTicker, formatLongClock, serverTime } from '../scoresheet-timing';

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
  imports: [TranslatePipe],
  template: `
    <div class="rs" role="status">
      <p class="rs-line" [class.done]="started()">
        <span class="rs-label">{{ 'sheetEdit.status.waiting' | translate }}</span>
        @if (started()) {
          <span class="rs-check" [attr.aria-label]="'sheetEdit.status.picked' | translate">✓</span>
          <span class="rs-time">{{ waitClock() }}</span>
        } @else {
          <b class="rs-clock">{{ waitClock() }}</b>
          <span class="rs-why">{{ 'sheetEdit.status.waitingWhy' | translate }}</span>
        }
      </p>
      @if (started()) {
        <p class="rs-line">
          <span class="rs-label">{{ 'sheetEdit.status.reading' | translate }}</span>
          <b class="rs-clock">{{ readClock() }}</b>
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
    .rs-line { margin: 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: 2px 8px; }
    .rs-label { font-weight: 600; }
    .rs-line.done .rs-label { font-weight: 400; opacity: .75; }
    .rs-check { color: var(--ok, var(--rh-success)); font-weight: 700; }
    .rs-time { font-size: .9em; opacity: .75; font-variant-numeric: tabular-nums; }
    .rs-clock { font-variant-numeric: tabular-nums; }
    /* Der Nachsatz: Strich hängt am Folgewort (a1-l-formular-wartezeit) */
    .rs-why { font-size: .9em; opacity: .75; }
    .rs-why::before { content: "–\\00a0"; }
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
  private readonly waitSeconds = computed(() => {
    const from = this.createdMs();
    if (!Number.isFinite(from)) return 0;
    const to = this.started() && Number.isFinite(this.startedMs()) ? this.startedMs() : this.ticker.now();
    return Math.max(0, Math.floor((to - from) / 1000));
  });
  readonly waitClock = computed(() => formatLongClock(this.waitSeconds()));
  readonly readClock = computed(() => {
    const from = Number.isFinite(this.startedMs()) ? this.startedMs() : this.createdMs();
    return formatLongClock(Number.isFinite(from) ? Math.max(0, Math.floor((this.ticker.now() - from) / 1000)) : 0);
  });
  readonly longWait = computed(() => !this.started() && this.waitSeconds() >= LONG_WAIT_SECONDS);

  constructor() {
    effect(() => {
      const open = this.status() === 'pending' || this.status() === 'running';
      untracked(() => this.ticker.run(open));
    });
  }

  ngOnDestroy(): void { this.ticker.stop(); }
}
