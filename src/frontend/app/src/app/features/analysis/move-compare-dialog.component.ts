import { ChangeDetectionStrategy, Component, Inject, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { MoveComparisonService, MoveComparisonStatusInfo } from './move-comparison.service';

/** Ein Vorschlag der Engine (erster Zug einer Linie) mit ihrer Bewertung — vorausgewählt. */
export interface MoveCompareCandidate { uci: string; evalText?: string; }

export interface MoveCompareDialogData {
  fen: string;
  candidates?: MoveCompareCandidate[];
}

interface PickMove { uci: string; san: string; evalText?: string; }

const DEFAULT_MAX = 4;
/** Absagen des Servers, für die es einen eigenen Satz gibt (`moveCompare.error.*`); alles andere = allgemein. */
const KNOWN_REASONS = new Set(['too-many-open', 'no-engine', 'too-many-jobs', 'invalid-move', 'too-few-moves',
  'too-many-moves', 'game-over', 'invalid-fen']);

/**
 * „Züge vergleichen" (0.602.0): 2–4 Züge der Stellung wählen — die Vorschläge der Engine stehen oben und sind
 * vorausgewählt, darunter jeder legale Zug (auch der eigene, den die Engine nicht vorschlägt) — und den Vergleich
 * anlegen. Gerechnet wird im Hintergrund; das Ergebnis steht auf `/analysis/compare/:id`.
 */
@Component({
  selector: 'app-move-compare-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatIconModule,
    MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'moveCompare.dialog.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ 'moveCompare.dialog.hint' | translate:{ max: max() } }}</p>
      @if (status(); as s) {
        @if (!s.engineAvailable) {
          <p class="warn"><mat-icon>info_outline</mat-icon> {{ 'moveCompare.dialog.noEngine' | translate }}</p>
        } @else {
          @if (!s.ownEngine) { <p class="muted small">{{ 'moveCompare.dialog.houseEngine' | translate:{ depth: s.maxDepth } }}</p> }
          @if (!s.explanations) { <p class="muted small">{{ 'moveCompare.dialog.noExplanations' | translate }}</p> }
        }
      }

      @if (engineMoves().length > 0) {
        <div class="group-label">{{ 'moveCompare.dialog.engineMoves' | translate }}</div>
        <div class="moves">
          @for (m of engineMoves(); track m.uci) {
            <button type="button" class="mv" [class.on]="isSelected(m.uci)" [disabled]="!isSelected(m.uci) && full()"
                    (click)="toggle(m.uci)">{{ m.san }}@if (m.evalText) { <span class="ev">{{ m.evalText }}</span> }</button>
          }
        </div>
      }
      <div class="group-label">{{ 'moveCompare.dialog.allMoves' | translate }}</div>
      <div class="moves">
        @for (m of otherMoves(); track m.uci) {
          <button type="button" class="mv" [class.on]="isSelected(m.uci)" [disabled]="!isSelected(m.uci) && full()"
                  (click)="toggle(m.uci)">{{ m.san }}</button>
        }
      </div>

      <div class="foot">
        <span class="muted small">{{ 'moveCompare.dialog.selected' | translate:{ count: selected().length, max: max() } }}</span>
        <mat-form-field appearance="outline" class="depth" subscriptSizing="dynamic">
          <mat-label>{{ 'moveCompare.dialog.depth' | translate }}</mat-label>
          <mat-select [ngModel]="depth()" (ngModelChange)="depth.set($event)">
            @for (d of depthOptions(); track d) { <mat-option [value]="d">{{ d }}</mat-option> }
          </mat-select>
        </mat-form-field>
      </div>
      @if (error(); as e) { <p class="warn"><mat-icon>error_outline</mat-icon> {{ e }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!canSubmit()" (click)="submit()">
        @if (busy()) { <mat-spinner diameter="18" /> } @else { {{ 'moveCompare.dialog.submit' | translate }} }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { margin: 0 0 8px; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; margin: 2px 0; }
    .warn { display: flex; align-items: center; gap: 6px; color: #e65100; font-size: .85rem; }
    .warn mat-icon { font-size: 18px; width: 18px; height: 18px; flex: 0 0 auto; }
    .group-label { font-size: .78rem; font-weight: 600; margin: 12px 0 6px; color: color-mix(in srgb, currentColor 70%, transparent); }
    .moves { display: flex; flex-wrap: wrap; gap: 6px; }
    .mv { font: inherit; font-family: 'Roboto Mono', monospace; font-size: .85rem; padding: 3px 9px; border-radius: 999px;
      border: 1px solid color-mix(in srgb, currentColor 25%, transparent); background: transparent; color: inherit; cursor: pointer;
      display: inline-flex; gap: 6px; align-items: center; }
    .mv:hover:not(:disabled) { background: color-mix(in srgb, currentColor 8%, transparent); }
    .mv.on { background: var(--mat-sys-primary, #1976d2); color: var(--mat-sys-on-primary, #fff); border-color: transparent; }
    .mv:disabled { opacity: .4; cursor: default; }
    .ev { font-size: .72rem; opacity: .8; }
    .foot { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: 14px; flex-wrap: wrap; }
    .depth { width: 120px; }
  `],
})
export class MoveCompareDialogComponent implements OnInit {
  private readonly api = inject(MoveComparisonService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly ref = inject(MatDialogRef<MoveCompareDialogComponent>);

  readonly status = signal<MoveComparisonStatusInfo | null>(null);
  readonly selected = signal<string[]>([]);
  readonly depth = signal(22);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  private readonly legal: PickMove[];

  constructor(@Inject(MAT_DIALOG_DATA) public data: MoveCompareDialogData) {
    this.legal = legalMoves(data.fen);
    const known = new Set(this.legal.map(m => m.uci));
    const pre = (data.candidates ?? []).map(c => c.uci).filter(u => known.has(u));
    this.selected.set([...new Set(pre)].slice(0, 2));
  }

  readonly max = computed(() => this.status()?.maxCandidates ?? DEFAULT_MAX);
  readonly full = computed(() => this.selected().length >= this.max());

  /** Die Vorschläge der Engine in ihrer Reihenfolge, mit Bewertung. */
  readonly engineMoves = computed<PickMove[]>(() => {
    const byUci = new Map(this.legal.map(m => [m.uci, m]));
    const seen = new Set<string>();
    const out: PickMove[] = [];
    for (const c of this.data.candidates ?? []) {
      const m = byUci.get(c.uci);
      if (!m || seen.has(m.uci)) continue;
      seen.add(m.uci);
      out.push({ ...m, evalText: c.evalText });
    }
    return out;
  });

  readonly otherMoves = computed<PickMove[]>(() => {
    const shown = new Set(this.engineMoves().map(m => m.uci));
    return this.legal.filter(m => !shown.has(m.uci));
  });

  readonly depthOptions = computed(() => {
    const top = this.status()?.maxDepth ?? 30;
    const out: number[] = [];
    for (let d = 16; d <= top; d += 2) out.push(d);
    return out;
  });

  readonly canSubmit = computed(() => !this.busy() && this.selected().length >= 2 && this.status()?.engineAvailable !== false);

  ngOnInit(): void {
    this.api.status().subscribe({
      next: s => { this.status.set(s); this.depth.set(Math.min(s.defaultDepth, s.maxDepth)); },
      error: () => {},
    });
  }

  isSelected(uci: string): boolean { return this.selected().includes(uci); }

  toggle(uci: string): void {
    const cur = this.selected();
    if (cur.includes(uci)) this.selected.set(cur.filter(u => u !== uci));
    else if (!this.full()) this.selected.set([...cur, uci]);
  }

  submit(): void {
    if (!this.canSubmit()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.create({ fen: this.data.fen, moves: this.selected(), depth: this.depth(), lang: this.translate.currentLang() ?? undefined })
      .subscribe({
        next: c => { this.busy.set(false); this.ref.close(c); this.router.navigate(['/analysis/compare', c.id]); },
        error: (e: HttpErrorResponse) => {
          this.busy.set(false);
          const reason = (e.error as { reason?: string } | null)?.reason;
          this.error.set(this.translate.instant(`moveCompare.error.${reason && KNOWN_REASONS.has(reason) ? reason : 'generic'}`));
        },
      });
  }
}

/** Alle legalen Züge der Stellung als UCI + SAN (leer bei einer FEN, die chess.js nicht lädt). */
export function legalMoves(fen: string): PickMove[] {
  try {
    return new Chess(fen).moves({ verbose: true }).map(m => ({ uci: m.from + m.to + (m.promotion ?? ''), san: m.san }));
  } catch {
    return [];
  }
}
