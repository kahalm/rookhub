import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { buildStagedHints, classifyMoveFromFen } from '../puzzles/puzzle-hints.util';
import { EvalScore, MOVE_CLASS_COLORS, MoveClass, formatEval } from './game-review.util';
import { Mistake } from './mistakes.util';
import { MistakesSession } from './mistakes-session';

/**
 * „Eigene Fehler nachspielen" — die Leiste UNTER dem Brett der Partie-Seite: Aufgabe, Rückmeldung,
 * Knöpfe. Gespielt wird auf dem Brett der Seite selbst (gleiche Größe, gleiches Thema); den Zustand hält
 * {@link MistakesSession}, die Seite bindet ihr Brett daran. Bis 0.518.0 war der Trainer ein Dialog mit
 * eigenem, kleinem Brett — gewünscht 2026-09-24: „verwende das gleiche Brett wie die Analyse".
 */
@Component({
  selector: 'app-mistakes-trainer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="top">
      <span class="title">{{ 'games.mistakes.title' | translate }}</span>
      @if (session.bothSides) {
        <span class="sides">
          @for (s of SIDES; track s) {
            <button mat-button [class.active]="session.side() === s" (click)="session.chooseSide(s)">
              {{ ('games.review.' + s) | translate }} ({{ (s === 'white' ? session.bySide.white : session.bySide.black).length }})
            </button>
          }
        </span>
      }
      <button mat-icon-button class="close" (click)="closed.emit()"
              [matTooltip]="'common.close' | translate" [attr.aria-label]="'common.close' | translate">
        <mat-icon>close</mat-icon>
      </button>
    </div>

    @if (session.list().length === 0) {
      <p class="empty">{{ 'games.mistakes.none' | translate }}</p>
    } @else if (session.phase() === 'done') {
      <p class="summary">{{ 'games.mistakes.summary' | translate: { solved: session.solved(), total: session.list().length } }}</p>
      <div class="actions">
        <button mat-stroked-button (click)="session.restart()"><mat-icon>replay</mat-icon> {{ 'games.mistakes.again' | translate }}</button>
        <span class="spacer"></span>
        <button mat-flat-button color="primary" (click)="closed.emit()">{{ 'common.close' | translate }}</button>
      </div>
    } @else if (session.current(); as m) {
      <div class="head">
        <span class="progress">{{ 'games.mistakes.progress' | translate: { current: session.index() + 1, total: session.list().length } }}</span>
        <span class="badge" [style.background]="color(m.cls)">{{ ('games.review.class.' + m.cls) | translate }}</span>
        <span class="evals">{{ fmt(m) }}</span>
      </div>

      <p class="prompt" [class.right]="session.phase() === 'right'" [class.wrong]="session.phase() === 'wrong'">
        @switch (session.phase()) {
          @case ('ask') { {{ 'games.mistakes.prompt' | translate: { san: m.playedSan } }} }
          @case ('checking') { {{ 'games.mistakes.checking' | translate: { san: session.tried() } }} }
          @case ('wrong') { {{ 'games.mistakes.wrong' | translate: { san: session.tried() } }} }
          @case ('right') {
            @if (session.foundBest()) { {{ 'games.mistakes.right' | translate: { san: m.bestSan } }} }
            @else { {{ 'games.mistakes.rightEquivalent' | translate: { san: session.foundSan(), best: m.bestSan } }} }
          }
          @case ('shown') { {{ 'games.mistakes.shown' | translate: { san: m.bestSan } }} }
        }
      </p>
      @if (session.phase() === 'right' && session.foundByEngine()) {
        <p class="cost">{{ 'games.mistakes.checkedByEngine' | translate }}</p>
      }
      <!-- Tipps wie beim Puzzle (seit 0.526.2): Zugart → Figur → Zug, Stufe für Stufe. -->
      @if (session.hintLevel() > 0) {
        <ol class="hint-list">
          @for (h of hints(m).slice(0, session.hintLevel()); track $index) { <li>{{ h }}</li> }
        </ol>
      }
      @if (session.phase() === 'wrong' && session.checkFailed()) {
        <p class="cost">{{ 'games.mistakes.checkFailed' | translate }}</p>
      }
      @if (session.phase() === 'wrong') {
        @let te = session.triedEval();
        @if (te === undefined) {
          <p class="cost tried-eval">{{ 'games.mistakes.triedEvalPending' | translate: { san: session.tried() } }}</p>
        } @else if (te) {
          <p class="cost tried-eval">{{ 'games.mistakes.triedEval' | translate: { san: session.tried(), eval: fmtScore(te), best: fmtScore(m.evalBefore) } }}</p>
        }
      }
      @if (session.phase() === 'right' || session.phase() === 'shown') {
        @if (others(m); as rest) {
          <p class="cost">{{ 'games.mistakes.alsoGood' | translate: { moves: rest } }}</p>
        }
        <p class="cost">{{ 'games.mistakes.cost' | translate: { san: m.playedSan, percent: lost(m) } }}</p>
      }

      <div class="actions">
        @if (session.phase() === 'wrong') {
          <button mat-stroked-button (click)="session.retry()"><mat-icon>refresh</mat-icon> {{ 'games.mistakes.retry' | translate }}</button>
        }
        <!-- Nach dem Urteil (daneben, gezeigt oder gefunden): frei weiterrechnen mit der Live-Engine, bis zur nächsten
             Aufgabe (seit 0.526.2, gewünscht 2026-09-24). -->
        @if (session.phase() === 'wrong' || session.phase() === 'shown' || session.phase() === 'right') {
          <button mat-stroked-button class="analyze" [class.on]="analyzing" (click)="analyze.emit()">
            <mat-icon>memory</mat-icon> {{ 'games.mistakes.analyze' | translate }}
          </button>
        }
        @if ((session.phase() === 'ask' || session.phase() === 'wrong') && session.hintLevel() < hints(m).length) {
          <button mat-stroked-button class="hint" (click)="session.showHint(hints(m).length)">
            <mat-icon>lightbulb</mat-icon>
            {{ (session.hintLevel() === 0 ? 'puzzles.hints.show' : 'puzzles.hints.next') | translate }}
            ({{ session.hintLevel() }}/{{ hints(m).length }})
          </button>
        }
        @if (session.phase() === 'ask' || session.phase() === 'wrong' || session.phase() === 'checking') {
          <button mat-button (click)="session.showSolution()">{{ 'games.mistakes.show' | translate }}</button>
          <span class="spacer"></span>
          <button mat-button (click)="session.next()">{{ 'games.mistakes.skip' | translate }}</button>
        } @else {
          <span class="spacer"></span>
          <button mat-flat-button color="primary" (click)="session.next()">
            {{ (session.last() ? 'games.mistakes.finish' : 'games.mistakes.next') | translate }}
          </button>
        }
      </div>
    }
  `,
  styles: [`
    :host {
      display: block; width: 100%; box-sizing: border-box; padding: 6px 12px 10px;
      border: 1px solid color-mix(in srgb, currentColor 14%, transparent); border-radius: 6px;
    }
    .top { display: flex; align-items: center; gap: 4px 10px; flex-wrap: wrap; }
    .title { font-weight: 600; }
    .sides { display: flex; gap: 2px; }
    .sides button { min-width: 0; padding: 0 8px; font-size: 0.8rem; opacity: 0.6; }
    .sides button.active { opacity: 1; font-weight: 600; }
    .close { margin-left: auto; }
    .head { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; font-size: 0.85rem; }
    .badge { padding: 1px 8px; border-radius: 10px; color: #fff; font-weight: 600; text-shadow: 0 1px 1px rgba(0, 0, 0, 0.45); }
    .evals { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 70%, transparent); }
    .prompt { margin: 6px 0 0; }
    .prompt.right { color: #2e7d32; font-weight: 500; }
    .prompt.wrong { color: #c62828; }
    .cost { margin: 2px 0 0; font-size: 0.85rem; color: color-mix(in srgb, currentColor 70%, transparent); }
    .empty, .summary { margin: 8px 0; }
    .actions { display: flex; align-items: center; gap: 8px; margin-top: 8px; flex-wrap: wrap; }
    .spacer { flex: 1 1 auto; }
    .analyze.on { color: #42a5f5; border-color: #42a5f5; }
    .hint-list { margin: 6px 0 0; padding-left: 20px; font-size: 0.85rem; }
    .hint-list li { margin: 2px 0; }
  `],
})
export class MistakesTrainerComponent {
  readonly SIDES: readonly ('white' | 'black')[] = ['white', 'black'];
  private readonly translate = inject(TranslateService);
  /** Die drei Tipp-Stufen je Aufgabe — einmal gerechnet (die Vorlage fragt bei jeder Prüfung). */
  private readonly hintCache = new Map<string, string[]>();

  @Input({ required: true }) session!: MistakesSession;
  /** Training beenden — die Seite zeigt wieder die Partie. */
  @Output() closed = new EventEmitter<void>();
  /** Läuft gerade die Analyse dieser Aufgabe (Knopf hervorgehoben)? */
  @Input() analyzing = false;
  /** „Analysieren": die Seite macht das Brett frei und schaltet die Live-Engine zu — bis zur nächsten Aufgabe. */
  @Output() analyze = new EventEmitter<void>();

  color(c: MoveClass): string { return MOVE_CLASS_COLORS[c]; }
  fmt(m: Mistake): string { return `${formatEval(m.evalBefore)} → ${formatEval(m.evalAfter)}`; }
  fmtScore(score: EvalScore): string { return formatEval(score); }

  /** Tipps zur Aufgabe, dieselben wie beim Puzzle: zum Bestzug der Analyse aus der Stellung VOR dem Fehler. */
  hints(m: Mistake): string[] {
    const key = `${m.ply}:${this.translate.currentLang() ?? ''}`;
    let h = this.hintCache.get(key);
    if (!h) {
      h = buildStagedHints(classifyMoveFromFen(m.fenBefore, m.bestUci), (k, p) => this.translate.instant(k, p));
      this.hintCache.set(key, h);
    }
    return h;
  }
  lost(m: Mistake): string { return m.lostPercent.toFixed(1); }

  /** Die übrigen gleichwertigen Züge, außer dem gefundenen und dem schon genannten Bestzug. */
  others(m: Mistake): string {
    const skip = new Set([m.bestSan, this.session.foundSan()]);
    return (m.acceptSan ?? []).filter(s => !skip.has(s)).join(', ');
  }
}
