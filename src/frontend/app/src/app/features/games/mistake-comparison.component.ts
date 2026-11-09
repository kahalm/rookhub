import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { MoveComparison, MoveComparisonCandidate, numberedLine } from '../analysis/move-comparison.service';
import { localizeSanText } from '../../shared/chess/keyboard-move.util';
import { ExplanationPart, explanationParts } from './explanation-lines';
import { LiveMove } from './live-engine-session';
import { MaximNoteComponent } from '../../shared/maxims/maxim-note.component';
import { splitMaxim } from '../../shared/maxims/chess-maxims.service';

/** Zugvergleich zu einem Fehler einer Vereinspartie (0.908.0, `GET /api/league/club/games/{id}/comparisons`). */
export interface ClubMistakeComparison {
  ply: number;
  playedUci: string;
  analysisBestUci: string;
  comparison: MoveComparison;
}

/** Eine Zeile der Gegenüberstellung: Zug, Bewertung (Sicht der Seite am Zug), Linie der Engine danach. */
export interface ComparisonRow { kind: 'played' | 'best'; san: string; evalText: string | null; isBest: boolean; line: string; }

/** Läuft der Vergleich noch (wartet, rechnet, wird erklärt)? */
export function comparisonPending(c: MoveComparison): boolean {
  return c.status !== 'done' && c.status !== 'failed';
}

/** Die beiden Zeilen (Partiezug, bester Zug der Analyse) eines fertigen Vergleichs; `null` = nicht beide gerechnet. */
export function comparisonRows(entry: ClubMistakeComparison, lang: string): ComparisonRow[] | null {
  const c = entry.comparison;
  if (c.status === 'failed') return null;
  const played = c.candidates.find(x => x.uci === entry.playedUci);
  const best = c.candidates.find(x => x.uci !== entry.playedUci);
  if (!played || !best || played.state !== 'done' || best.state !== 'done') return null;
  const row = (kind: ComparisonRow['kind'], x: MoveComparisonCandidate): ComparisonRow => {
    const after = afterFen(c.fen, x.uci);
    const line = x.replies[0]?.line ?? [];
    return {
      kind, san: localizeSanText(numberedLine(c.fen, [x.san]), lang), evalText: x.evalText, isBest: x.isBest,
      line: after && line.length ? localizeSanText(numberedLine(after, line, 6), lang) : '',
    };
  };
  return [row('played', played), row('best', best)];
}

/** Die Begründung: beim schwächeren der beiden Züge (meist der Partiezug — die tiefere Rechnung kann anders urteilen). */
export function comparisonExplanation(c: MoveComparison): string | null {
  return c.candidates.find(x => !x.isBest && x.state === 'done')?.explanation ?? null;
}

function afterFen(fen: string, uci: string): string | null {
  try {
    const chess = new Chess(fen);
    chess.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci.length > 4 ? uci[4] : undefined });
    return chess.fen();
  } catch {
    return null;
  }
}

/**
 * Unter dem Urteil im Trainer „Fehler nachspielen" einer Vereinspartie: Partiezug gegen besten Zug, je mit Bewertung und
 * Engine-Linie, dazu die Begründung und der Weg zur vollen Vergleichsseite. Läuft der Vergleich noch: eine leise Zeile;
 * gibt es keinen (oder ist er gescheitert): nichts (DESIGN 8.x „Zugvergleich am Fehler").
 */
@Component({
  selector: 'app-mistake-comparison',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslatePipe, MaximNoteComponent],
  template: `
    @if (entry(); as e) {
      @if (pending()) {
        <p class="pending">{{ 'games.mistakes.compare.pending' | translate }}</p>
      } @else if (rows(); as rs) {
        <div class="cmp">
          <div class="head">{{ 'games.mistakes.compare.title' | translate: { depth: e.comparison.depth } }}</div>
          @for (r of rs; track r.kind) {
            <div class="row">
              <span class="label">{{ ('games.mistakes.compare.' + r.kind) | translate }}</span>
              <span class="san">{{ r.san }}</span>
              @if (r.isBest) { <span class="best">{{ 'moveCompare.page.best' | translate }}</span> }
              <span class="spacer"></span>
              @if (r.evalText) { <span class="ev" [class.neg]="!r.isBest">{{ r.evalText }}</span> }
            </div>
            @if (r.line) { <div class="line">{{ r.line }}</div> }
          }
          <!-- Züge der Erklärung klickbar (0.915.0): zeigt die Stellung auf dem Brett der Seite, ← → laufen weiter. -->
          @if (explanationParts(); as parts) {
            <p class="expl">@for (p of parts; track $index) {@if (p.line) {<button type="button" class="mv"
              (click)="showLine.emit({ fen: e.comparison.fen, line: p.line, at: p.at! })">{{ p.text }}</button>} @else {{{ p.text }}}}</p>
          }
          <!-- Merksatz aus der kuratierten Liste mit (i) → Buchstellen (0.917.0). -->
          @if (maxim(); as mx) { <app-maxim-note [text]="mx" /> }
          <!-- Die volle Vergleichsseite braucht ein Konto: über den Teilen-Link (beide Ids leer) kein Knopf. -->
          @if (clubGameId() || ownGameId()) {
          <a mat-button class="open" [routerLink]="['/analysis/compare', e.comparison.id]" [queryParams]="ownGameId() ? { ownGame: ownGameId() } : { clubGame: clubGameId() }">
            <mat-icon>compare_arrows</mat-icon> {{ 'games.mistakes.compare.open' | translate }}
          </a>
          }
        </div>
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .pending { margin: 4px 0 0; font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .cmp { margin-top: 8px; padding-top: 6px; border-top: 1px solid color-mix(in srgb, currentColor 14%, transparent); }
    .head { font-size: 0.8rem; font-weight: 600; margin-bottom: 2px; }
    .row { display: flex; align-items: center; gap: 8px; font-size: 0.88rem; }
    .label { min-width: 6.5em; color: color-mix(in srgb, currentColor 70%, transparent); }
    .san, .line, .ev { font-family: 'Roboto Mono', monospace; }
    .spacer { flex: 1 1 auto; }
    .ev { font-weight: 600; color: var(--rh-success); }
    .ev.neg { color: var(--rh-error); }
    .best { font-size: 0.7rem; font-weight: 700; padding: 1px 8px; border-radius: 999px;
      background: color-mix(in srgb, var(--rh-success) 18%, transparent); color: var(--rh-success); }
    .line { font-size: 0.78rem; padding: 0 0 4px 6.5em; overflow-wrap: anywhere;
      color: color-mix(in srgb, currentColor 65%, transparent); }
    .expl { margin: 6px 0 2px; font-size: 0.88rem; line-height: 1.45; }
    .mv { all: unset; cursor: pointer; font-weight: 500; color: var(--rh-accent, inherit); border-radius: 3px;
      text-decoration: underline dotted; text-underline-offset: 2px; }
    .mv:hover, .mv:focus-visible { background: color-mix(in srgb, currentColor 12%, transparent); outline: none; }
    .open { margin-left: -8px; }
    @media (max-width: 480px) { .line { padding-left: 0; } .label { min-width: 0; } }
  `],
})
export class MistakeComparisonComponent {
  private readonly translate = inject(TranslateService);
  /** Der Vergleich zu DIESEM Fehler; `null` = keiner — dann zeigt die Komponente nichts. */
  readonly entry = input<ClubMistakeComparison | null>(null);
  readonly clubGameId = input<number | null>(null);
  readonly ownGameId = input<number | null>(null);

  readonly pending = computed(() => { const e = this.entry(); return !!e && comparisonPending(e.comparison); });
  readonly rows = computed(() => { const e = this.entry(); return e ? comparisonRows(e, this.translate.currentLang() ?? 'en') : null; });
  readonly explanation = computed(() => { const e = this.entry(); return e ? comparisonExplanation(e.comparison) : null; });
  /** Die Erklärung als Text + klickbare Züge (Folgen, die auf dem Brett nicht aufgehen, bleiben Text). */
  readonly explanationParts = computed<ExplanationPart[] | null>(() => {
    const e = this.entry();
    const text = this.explanation();
    return e && text ? explanationParts(splitMaxim(text).body, e.comparison.fen) : null;
  });
  /** Der Merksatz am Ende der Erklärung (fester Wortlaut aus der Liste), sonst `null`. */
  readonly maxim = computed(() => { const t = this.explanation(); return t ? splitMaxim(t).maxim : null; });
  /** Klick auf einen Zug der Erklärung: die Folge ab der Aufgabe, man steht nach `at` Zügen. */
  readonly showLine = output<{ fen: string; line: LiveMove[]; at: number }>();
}
