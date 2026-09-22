import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output, computed, signal, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { TrainColor, chapterColorsOf } from './repertoire-color.util';
import {
  EXPLORER_RATINGS, EXPLORER_SPEEDS, ExplorerAnalysisResult, ExplorerSettings, MAX_THRESHOLD_PERCENT,
  MIN_THRESHOLD_PERCENT, RepertoireExplorerService, RepertoireHole, clampThreshold, formatPath, holeMoveLabel,
  formatPercent, positionAfterHole, readExplorerSettings, saveExplorerSettings,
} from './repertoire-explorer.service';

/** Was das Brett zeigen soll, wenn ein Loch angewählt ist: die Stellung NACH dem fehlenden Zug. */
export interface HoleBoardView {
  fen: string;
  lastMove: [string, string];
}

/**
 * Lochfinder: welche häufigen Gegnerzüge beantwortet das Repertoire nicht? Die Auswahl (Farbe,
 * Datenbank, Elo, Bedenkzeit, Schwelle) steht oben, darunter die Löcher nach Häufigkeit. Ein Klick
 * zeigt die Stellung nach dem fehlenden Zug auf dem Brett der Detailseite.
 *
 * <p>Zustand in Signalen: die Antworten kommen über HttpClient (fetch), und eine Feldzuweisung
 * im Abonnenten löst dort keine Aenderungserkennung aus (siehe Turnierkalender).</p>
 */
@Component({
  selector: 'app-repertoire-holes',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatIconModule,
    MatProgressBarModule, MatTooltipModule, TranslatePipe, HelpHintComponent,
  ],
  template: `
    <div class="holes">
      <div class="settings">
        <div class="row">
          @if (colorsPresent().length > 1) {
            <mat-button-toggle-group [value]="color()" (change)="setColor($event.value)" hideSingleSelectionIndicator="true"
                                     [attr.aria-label]="'repertoire.holes.color' | translate">
              <mat-button-toggle value="w">{{ 'repertoire.holes.white' | translate }}</mat-button-toggle>
              <mat-button-toggle value="b">{{ 'repertoire.holes.black' | translate }}</mat-button-toggle>
            </mat-button-toggle-group>
          } @else {
            <span class="side-label">{{ (color() === 'w' ? 'repertoire.holes.asWhite' : 'repertoire.holes.asBlack') | translate }}</span>
          }
          <mat-button-toggle-group [value]="settings().database" (change)="setDatabase($event.value)" hideSingleSelectionIndicator="true"
                                   [attr.aria-label]="'repertoire.holes.database' | translate">
            <mat-button-toggle value="lichess">Lichess</mat-button-toggle>
            <mat-button-toggle value="masters">{{ 'repertoire.holes.masters' | translate }}</mat-button-toggle>
          </mat-button-toggle-group>
          <app-help-hint [text]="'repertoire.holes.help' | translate" />
        </div>

        @if (settings().database === 'lichess') {
          <div class="row chips" [attr.aria-label]="'repertoire.holes.ratings' | translate">
            <span class="row-label">{{ 'repertoire.holes.ratings' | translate }}</span>
            @for (r of ratings; track r) {
              <button type="button" class="chip" [class.on]="settings().ratings.includes(r)" (click)="toggleRating(r)"
                      [attr.aria-pressed]="settings().ratings.includes(r)">{{ ratingLabel(r) }}</button>
            }
          </div>
          <div class="row chips" [attr.aria-label]="'repertoire.holes.speeds' | translate">
            <span class="row-label">{{ 'repertoire.holes.speeds' | translate }}</span>
            @for (s of speeds; track s) {
              <button type="button" class="chip" [class.on]="settings().speeds.includes(s)" (click)="toggleSpeed(s)"
                      [attr.aria-pressed]="settings().speeds.includes(s)">{{ 'repertoire.holes.speed.' + s | translate }}</button>
            }
          </div>
        }

        <div class="row">
          <label class="threshold">
            {{ 'repertoire.holes.threshold' | translate }}
            <input type="number" [min]="minThreshold" [max]="maxThreshold" step="0.1"
                   [ngModel]="settings().thresholdPercent" (ngModelChange)="setThreshold($event)"
                   (blur)="normalizeThreshold()" /> %
          </label>
          <app-help-hint [text]="'repertoire.holes.thresholdHelp' | translate" />
          <span class="spacer"></span>
          @if (running()) {
            <button mat-stroked-button type="button" (click)="cancel()">
              <mat-icon>stop</mat-icon> {{ 'repertoire.holes.cancel' | translate }}
            </button>
          } @else {
            <button mat-flat-button color="primary" type="button" (click)="start()" [disabled]="!canStart()">
              <mat-icon>travel_explore</mat-icon> {{ 'repertoire.holes.search' | translate }}
            </button>
          }
        </div>
      </div>

      @if (result(); as r) {
        @if (running() || !r.complete) {
          <div class="progress">
            <mat-progress-bar mode="determinate" [value]="progressPercent()" />
            <span>{{ 'repertoire.holes.progress' | translate: { done: r.positionsAnalyzed, open: r.positionsPending } }}</span>
          </div>
        }
        @if (r.rateLimited && running()) {
          <p class="note">{{ 'repertoire.holes.rateLimited' | translate: { seconds: r.retryAfterSeconds ?? 60 } }}</p>
        }
        @if (r.tokenMissing) {
          <p class="note warn">{{ 'repertoire.holes.tokenMissing' | translate }}
            <a routerLink="/profile">{{ 'repertoire.holes.toProfile' | translate }}</a></p>
        }
        @if (r.tokenInvalid) {
          <p class="note warn">{{ 'repertoire.holes.tokenInvalid' | translate }}
            <a routerLink="/profile">{{ 'repertoire.holes.toProfile' | translate }}</a></p>
        }
        @if (r.fetchFailed) {
          <p class="note warn">{{ 'repertoire.holes.fetchFailed' | translate }}</p>
        }
        @if (!running() && !r.complete && !r.tokenMissing && !r.tokenInvalid && !r.fetchFailed) {
          <p class="note">{{ 'repertoire.holes.incomplete' | translate }}</p>
        }

        <div class="list-head">
          @if (r.holes.length) {
            {{ 'repertoire.holes.count' | translate: { count: r.holes.length } }}
          } @else if (r.complete) {
            {{ 'repertoire.holes.none' | translate }}
          }
        </div>
        <div class="list">
          @for (h of r.holes; track keyOf(h)) {
            <button type="button" class="hole" [class.sel]="selectedKey() === keyOf(h)" (click)="select(h)">
              <div class="hole-main">
                <span class="move">{{ label(h) }}</span>
                <span class="share" [matTooltip]="'repertoire.holes.shareTip' | translate">{{ pct(h.share) }}</span>
                <span class="freq" [matTooltip]="'repertoire.holes.frequencyTip' | translate">
                  {{ 'repertoire.holes.frequency' | translate: { value: pct(h.frequency) } }}
                </span>
              </div>
              <div class="hole-path">{{ path(h) || ('repertoire.holes.startPosition' | translate) }}</div>
              <div class="hole-meta">
                @if (h.opening) { <span>{{ h.eco }} {{ h.opening }}</span> · }
                <span>{{ 'repertoire.holes.games' | translate: { count: h.games.toLocaleString() } }}</span>
              </div>
            </button>
          }
        </div>
      } @else if (!running()) {
        <p class="intro">{{ 'repertoire.holes.intro' | translate }}</p>
      }
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .holes { display: flex; flex-direction: column; height: 100%; min-height: 0; }
    .settings { padding: 8px; display: flex; flex-direction: column; gap: 6px;
      border-bottom: 1px solid color-mix(in srgb, currentColor 15%, transparent); }
    .row { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
    .row-label { font-size: 12px; min-width: 64px; color: color-mix(in srgb, currentColor 65%, transparent); }
    .side-label { font-size: 13px; font-weight: 500; }
    .spacer { flex: 1; }
    .chip { font: inherit; font-size: 12px; padding: 2px 8px; border-radius: 12px; cursor: pointer;
      border: 1px solid color-mix(in srgb, currentColor 30%, transparent); background: transparent; color: inherit; }
    .chip.on { background: var(--mat-sys-primary, #3f51b5); color: var(--mat-sys-on-primary, #fff); border-color: transparent; }
    .threshold { font-size: 13px; display: flex; align-items: center; gap: 4px; }
    .threshold input { width: 56px; font: inherit; padding: 2px 4px; }
    .progress { padding: 6px 8px; font-size: 12px; display: flex; flex-direction: column; gap: 4px; }
    .note { margin: 4px 8px; font-size: 12px; color: color-mix(in srgb, currentColor 70%, transparent); }
    .note.warn { color: var(--mat-sys-error, #b00020); }
    .intro { margin: 12px 8px; font-size: 13px; color: color-mix(in srgb, currentColor 70%, transparent); }
    .list-head { padding: 4px 8px; font-size: 12px; font-weight: 500; }
    .list { flex: 1; overflow-y: auto; min-height: 0; }
    .hole { display: block; width: 100%; text-align: left; font: inherit; color: inherit; cursor: pointer;
      background: transparent; border: 0; border-bottom: 1px solid color-mix(in srgb, currentColor 10%, transparent);
      padding: 6px 8px; }
    .hole:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .hole.sel { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 14%, transparent); }
    .hole-main { display: flex; align-items: baseline; gap: 8px; }
    .move { font-weight: 600; font-family: 'Roboto Mono', monospace; }
    .share { font-size: 13px; }
    .freq { font-size: 12px; margin-left: auto; color: color-mix(in srgb, currentColor 65%, transparent); }
    .hole-path { font-family: 'Roboto Mono', monospace; font-size: 12px; white-space: nowrap; overflow: hidden;
      text-overflow: ellipsis; color: color-mix(in srgb, currentColor 75%, transparent); }
    .hole-meta { font-size: 11px; color: color-mix(in srgb, currentColor 60%, transparent); }
  `],
})
export class RepertoireHolesComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) repertoireId!: number;
  /** Linien des Repertoires ohne Info-Linien — daraus die Farbe je Kapitel (wie im Trainer). */
  @Input() games: ParsedGame[] = [];
  @Output() holeSelected = new EventEmitter<HoleBoardView | null>();

  readonly ratings = EXPLORER_RATINGS;
  readonly speeds = EXPLORER_SPEEDS;
  readonly minThreshold = MIN_THRESHOLD_PERCENT;
  readonly maxThreshold = MAX_THRESHOLD_PERCENT;

  readonly settings = signal<ExplorerSettings>(readExplorerSettings());
  readonly color = signal<TrainColor>('b');
  readonly colorsPresent = signal<TrainColor[]>([]);
  readonly running = signal(false);
  readonly result = signal<ExplorerAnalysisResult | null>(null);
  /** Angewähltes Loch als Schlüssel — jede Runde liefert neue Objekte für dieselben Löcher. */
  readonly selectedKey = signal<string | null>(null);

  readonly canStart = computed(() => {
    const s = this.settings();
    return this.colorsPresent().length > 0
      && (s.database === 'masters' || (s.ratings.length > 0 && s.speeds.length > 0));
  });

  readonly progressPercent = computed(() => {
    const r = this.result();
    if (!r) return 0;
    const all = r.positionsAnalyzed + r.positionsPending;
    return all === 0 ? 100 : Math.round(100 * r.positionsAnalyzed / all);
  });

  private chapterColors = new Map<string, TrainColor>();
  private sub: Subscription | null = null;

  constructor(private explorer: RepertoireExplorerService) {}

  ngOnChanges(): void {
    this.chapterColors = chapterColorsOf(this.repertoireId, this.games);
    const count = { w: 0, b: 0 };
    for (const g of this.games) count[this.chapterColors.get((g.headers['Black'] || '').trim()) ?? 'w']++;
    const present = (['w', 'b'] as TrainColor[]).filter(c => count[c] > 0);
    this.colorsPresent.set(present);
    if (present.length === 1) this.color.set(present[0]);
    else if (present.length === 2 && !this.result()) this.color.set(count.b > count.w ? 'b' : 'w');
  }

  ngOnDestroy(): void { this.cancel(); }

  setColor(c: TrainColor): void {
    if (c === this.color()) return;
    this.color.set(c);
    this.clearResult();
  }

  setDatabase(db: 'lichess' | 'masters'): void { this.update({ database: db }); }

  toggleRating(r: number): void {
    const cur = this.settings().ratings;
    this.update({ ratings: cur.includes(r) ? cur.filter(x => x !== r) : [...cur, r].sort((a, b) => a - b) });
  }

  toggleSpeed(s: string): void {
    const cur = this.settings().speeds;
    this.update({ speeds: cur.includes(s) ? cur.filter(x => x !== s) : EXPLORER_SPEEDS.filter(x => x === s || cur.includes(x)) });
  }

  /** Während des Tippens nichts klemmen (sonst springt „0," sofort auf 0,1) — erst beim Verlassen. */
  setThreshold(v: number | string | null): void {
    const n = Number(v);
    if (Number.isFinite(n) && n > 0) this.settings.update(s => ({ ...s, thresholdPercent: n }));
  }

  normalizeThreshold(): void { this.update({ thresholdPercent: clampThreshold(this.settings().thresholdPercent) }); }

  start(): void {
    this.cancel();
    this.normalizeThreshold();
    const s = this.settings();
    this.result.set(null);
    this.select(null);
    this.running.set(true);
    this.sub = this.explorer.run(this.repertoireId, {
      color: this.color(),
      chapterColors: Object.fromEntries(this.chapterColors),
      database: s.database,
      ratings: s.ratings,
      speeds: s.speeds,
      thresholdPercent: s.thresholdPercent,
      includeHoles: true,
      includeLineFrequencies: false,
    }).subscribe({
      next: r => this.result.set(r),
      error: () => {
        this.running.set(false);
        this.result.update(r => ({ ...(r ?? EMPTY_RESULT), fetchFailed: true }));
      },
      complete: () => this.running.set(false),
    });
  }

  cancel(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    this.running.set(false);
  }

  select(h: RepertoireHole | null): void {
    this.selectedKey.set(h ? this.keyOf(h) : null);
    this.holeSelected.emit(h ? positionAfterHole(h) : null);
  }

  keyOf(h: RepertoireHole): string { return h.fen + '|' + h.uci; }

  label(h: RepertoireHole): string { return holeMoveLabel(h); }

  path(h: RepertoireHole): string { return formatPath(h.startFen, h.path); }

  ratingLabel(r: number): string {
    if (r === 0) return '<1000';
    return r === 2500 ? '2500+' : String(r);
  }

  pct(x: number): string { return formatPercent(x); }

  private update(patch: Partial<ExplorerSettings>): void {
    const next = { ...this.settings(), ...patch };
    this.settings.set(next);
    saveExplorerSettings(next);
  }

  private clearResult(): void {
    this.cancel();
    this.result.set(null);
    this.select(null);
  }
}

const EMPTY_RESULT: ExplorerAnalysisResult = {
  complete: false, positionsAnalyzed: 0, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
  tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [], lineFrequencies: null,
};
