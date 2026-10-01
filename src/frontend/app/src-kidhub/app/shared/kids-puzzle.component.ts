import {
  ChangeDetectionStrategy, Component, DestroyRef, HostListener, computed, effect, inject, input, output, signal, untracked,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { PuzzleBoardComponent } from '@rh/features/puzzles/puzzle-board.component';
import { KidsMove, KidsSolver, KidsTask } from '../core/kids-solver';
import { isAdvanceKey } from '../core/kids-keys';
import { KID_STACKED } from './kids-layout';

/** Was die Eule gerade sagt. */
export type KidsPuzzleStatus = 'watch' | 'yourTurn' | 'good' | 'wrong' | 'alternative' | 'solved';

/** Was der NAECHSTE Tipp kostet — der Modus rechnet es aus, der Knopf zeigt es vor dem Druck: `free` (Endlos: der
 *  erste je Aufgabe), `heart` (Endlos: ab dem zweiten), `star` (Stufe: dieser Tipp senkt die Sterne), `null` = nichts. */
export type KidsHintCost = 'free' | 'heart' | 'star' | null;

/** Pausen, damit das Kind sieht, was passiert (Stellungszug, Antwort des Gegners). */
export const SETUP_DELAY_MS = 700;
export const REPLY_DELAY_MS = 550;
/** So lange bleibt ein falscher Zug stehen, bevor er zurueckgenommen wird — das Kind soll ihn sehen. */
export const WRONG_HOLD_MS = 2000;

const SOLVED_KEYS = ['kids.feedback.solved1', 'kids.feedback.solved2', 'kids.feedback.solved3', 'kids.feedback.solved4'];

/**
 * Eine Aufgabe auf der Kinderseite: grosses Brett, eine Eule, die sagt, was los ist, ein Tipp-Knopf.
 * Falsche Zuege kosten nichts ausser einem Fehlerpunkt (fuer die Sterne) — der Zug bleibt
 * `WRONG_HOLD_MS` rot markiert stehen, dann springt das Brett zurueck und das Kind probiert weiter.
 * Am PC loest die Leertaste „Weiter" aus.
 *
 * <p>Aufbau: am PC und am Handy quer Brett links (so hoch, wie das Fenster erlaubt), rechts die Aufgabe
 * (`[kidTask]`, vom Aufrufer), die Eule und der Knopf; am Handy und Tablet hochkant alles untereinander, die
 * Aufgabe ueber dem Brett (`KID_STACKED`).</p>
 * Aller Anzeige-Zustand steckt in Signalen: die Antworten des Gegners
 * kommen per Timer, und Angular 22 zeichnet nach einem Timer eine unmarkierte Ansicht nicht neu.
 */
@Component({
  selector: 'kid-puzzle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PuzzleBoardComponent, TranslatePipe],
  template: `
    <div class="puzzle">
      <div class="task-slot"><ng-content select="[kidTask]" /></div>
      <div class="board">
        <app-puzzle-board
          [fen]="fen()"
          [orientation]="orientation()"
          [turnColor]="turnColor()"
          [dests]="dests()"
          [lastMove]="lastMove()"
          [check]="check()"
          [viewOnly]="!interactive()"
          [reviewShapes]="shapes()"
          [allowFullscreen]="false"
          [autoQueen]="true"
          boardTheme="blue"
          (moveMade)="onMove($event)" />
      </div>

      <div class="side">
        <div class="bubble" [class]="'bubble status-' + bubble()" role="status" aria-live="polite">
          <span class="owl" aria-hidden="true">🦉</span>
          <p>
            @switch (bubble()) {
              @case ('yourTurn') {
                {{ (orientation() === 'white' ? 'kids.feedback.yourTurnWhite' : 'kids.feedback.yourTurnBlack') | translate }}
              }
              @case ('good') { {{ 'kids.feedback.good' | translate }} }
              @case ('wrong') { {{ 'kids.feedback.wrong' | translate }} }
              @case ('alternative') { {{ 'kids.feedback.alternative' | translate }} }
              @case ('solved') { {{ solvedKey() | translate }} }
            }
          </p>
        </div>

        @if (status() === 'solved' && finalComment(); as text) {
          <p class="comment">{{ text }}</p>
        }

        <div class="actions">
          @if (status() === 'solved') {
            <button type="button" class="big next" (click)="next.emit()">
              {{ 'common.next' | translate }} ▶ <kbd class="key">{{ 'kids.spaceKey' | translate }}</kbd>
            </button>
          } @else {
            <!-- Der Preis steht VOR dem Druck am Knopf: ein zweiter Tipp im Endlos-Modus kostete still ein Herz, in den
                 Stufen kostete er unangekuendigt Sterne (Codereview 2026-09-29, UX-061). -->
            <button type="button" class="big hint" (click)="showHint()" [disabled]="!interactive()"
                    [attr.aria-label]="hintCost() ? ('kids.hint' | translate) + ' – ' + (('kids.hintCost.' + hintCost()) | translate) : null">
              💡 {{ 'kids.hint' | translate }}
              @switch (hintCost()) {
                @case ('free') { <span class="cost">{{ 'kids.hintCost.free' | translate }}</span> }
                @case ('heart') { <span class="cost">−❤️</span> }
                @case ('star') { <span class="cost">−⭐</span> }
              }
            </button>
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    /* PC: Brett links ueber beide Zeilen, rechts oben die Aufgabe, darunter Eule und Knopf. Das Brett
       ist so gross, wie die Fensterhoehe erlaubt (--kid-board, gesetzt in der App-Huelle) — vorher
       70vh, und unter Kopfzeile, Punkten und Aufgabe lief die unterste Reihe aus dem Bild. */
    .puzzle {
      display: grid; justify-content: center; align-items: start; column-gap: 28px; row-gap: 14px;
      grid-template-columns: var(--kid-board, 640px) minmax(280px, 400px);
      grid-template-rows: auto 1fr;
      grid-template-areas: "board task" "board side";
    }
    .task-slot { grid-area: task; }
    .task-slot:empty { display: none; }
    .board {
      grid-area: board; width: var(--kid-board, 640px);
      border-radius: 14px; overflow: hidden; box-shadow: 0 6px 0 var(--kid-shadow);
    }
    /* Brett-Koordinaten fuer Kinder groesser und deckend (UX-060): chessground setzt 9 px bei 0,8 Deckkraft — auf
       dem bis zu 820 px grossen Brett kaum zu finden, wenn der Trainer „schau auf g8" sagt. Die Farben je Feld
       stehen in styles.scss. */
    .board ::ng-deep .cg-wrap coords { font-size: 11px; opacity: 1; }
    .side { grid-area: side; display: flex; flex-direction: column; gap: 14px; }
    .bubble {
      display: flex; gap: 12px; align-items: center; padding: 14px 16px; border-radius: 22px;
      background: var(--kid-card); box-shadow: 0 4px 0 var(--kid-shadow); font-size: 1.25rem; font-weight: 700;
      transition: background-color .2s;
    }
    .bubble p { margin: 0; }
    .owl { font-size: 2.4rem; line-height: 1; }
    .status-good, .status-solved { background: var(--kid-good-bg); color: var(--kid-good-fg); }
    .status-wrong { background: var(--kid-bad-bg); color: var(--kid-bad-fg); animation: wiggle .35s; }
    .status-alternative { background: var(--kid-info-bg); }
    .status-solved .owl { animation: hop .6s; }
    .comment { margin: 0; padding: 12px 16px; border-radius: 16px; background: var(--kid-card); font-size: 1.05rem; line-height: 1.4; }
    .actions { display: flex; gap: 12px; }
    .big {
      flex: 1; font: inherit; font-size: 1.3rem; font-weight: 800; padding: 14px 18px; border: 0;
      border-radius: 18px; cursor: pointer; box-shadow: 0 5px 0 var(--kid-shadow); color: #fff;
    }
    .big:active { transform: translateY(3px); box-shadow: 0 2px 0 var(--kid-shadow); }
    .big:disabled { opacity: .5; cursor: default; }
    .hint { background: var(--kid-yellow); color: #3d2c00; }
    .cost {
      display: inline-block; margin-left: 8px; padding: 1px 10px; border-radius: 999px; vertical-align: middle;
      font-size: .8em; background: rgba(255, 255, 255, .6); white-space: nowrap;
    }
    .next { background: var(--kid-green-strong); }
    /* Nur mit Maus/Tastatur: „Leertaste" am Weiter-Knopf. Am Tablet gibt es keine. */
    .key { display: none; }
    @media (hover: hover) and (pointer: fine) {
      .key {
        display: inline-block; margin-left: 10px; padding: 1px 8px; border-radius: 6px; vertical-align: middle;
        font: inherit; font-size: .75rem; font-weight: 700; background: rgba(0, 0, 0, .2);
        border: 1px solid rgba(255, 255, 255, .6);
      }
    }
    @keyframes wiggle { 25% { transform: translateX(-6px); } 75% { transform: translateX(6px); } }
    @keyframes hop { 40% { transform: translateY(-10px) rotate(-8deg); } }
    /* Untereinander: die Breite setzt die App-Huelle (--kid-board), die Kopfzeile der Seite geht mit. */
    @media ${KID_STACKED} {
      .puzzle {
        grid-template-columns: var(--kid-board, 640px);
        grid-template-rows: auto;
        grid-template-areas: "task" "board" "side";
        row-gap: 12px;
      }
      .board { width: 100%; min-width: 0; }
      .bubble { font-size: 1.1rem; }
    }
  `],
})
export class KidsPuzzleComponent {
  readonly task = input.required<KidsTask>();
  /** Kurs-Kommentare je Halbzug; der nach dem letzten Zug erscheint, wenn die Aufgabe geloest ist. */
  readonly moveComments = input<Record<number, string> | null>(null);
  /** Preis des naechsten Tipps (`KidsHintCost`), vom Modus gesetzt — ohne (Kurse) steht nur „Tipp" am Knopf. */
  readonly hintCost = input<KidsHintCost>(null);

  /** Die Aufgabe ist geloest — mit der Zahl der Fehler (Tipps zaehlen mit). */
  readonly solved = output<{ mistakes: number }>();
  /** Das Kind will weiter. */
  readonly next = output<void>();
  /** Ein falscher Zug — sofort, nicht erst beim Loesen; der Endlos-Modus zieht dafuer ein Herz ab. */
  readonly mistake = output<void>();
  /** Ein Tipp — mit der Zahl der Tipps in DIESER Aufgabe (1, 2, …); ob er etwas kostet, entscheidet der Modus. */
  readonly hinted = output<number>();

  readonly fen = signal('8/8/8/8/8/8/8/8 w - - 0 1');
  readonly orientation = signal<'white' | 'black'>('white');
  readonly turnColor = signal<'white' | 'black'>('white');
  readonly dests = signal<Map<Key, Key[]>>(new Map());
  readonly lastMove = signal<[Key, Key] | undefined>(undefined);
  readonly check = signal(false);
  readonly shapes = signal<DrawShape[]>([]);
  readonly status = signal<KidsPuzzleStatus>('watch');
  readonly solvedKey = signal(SOLVED_KEYS[0]);
  readonly finalComment = signal<string | null>(null);
  /** Ein falscher Zug steht gerade noch da (`WRONG_HOLD_MS`) — das Brett ist so lange gesperrt. */
  readonly holding = signal(false);
  /** Was die Eule sagt. Waehrend des Stellungszugs schon „Du bist am Zug": ein eigener Satz dafuer („Pass auf, was dein
   *  Gegner zieht") stand nur den Bruchteil einer Sekunde da — zu kurz zum Lesen (Wunsch 2026-09-27). */
  readonly bubble = computed(() => this.status() === 'watch' ? 'yourTurn' : this.status());
  readonly interactive = computed(() => !this.holding() && (this.status() === 'yourTurn' || this.status() === 'good'
    || this.status() === 'wrong' || this.status() === 'alternative'));

  private solver: KidsSolver | null = null;
  private mistakes = 0;
  private hintLevel = 0;
  private hintsUsed = 0;
  private timers: ReturnType<typeof setTimeout>[] = [];

  constructor() {
    effect(() => {
      const task = this.task();
      untracked(() => this.start(task));
    });
    inject(DestroyRef).onDestroy(() => this.clearTimers());
  }

  private start(task: KidsTask): void {
    this.clearTimers();
    this.solver = new KidsSolver(task);
    this.mistakes = 0;
    this.hintLevel = 0;
    this.hintsUsed = 0;
    this.holding.set(false);
    this.finalComment.set(null);
    this.shapes.set([]);
    this.lastMove.set(undefined);
    this.orientation.set(this.solver.solverColor);
    this.status.set(this.solver.hasPendingSetup() ? 'watch' : 'yourTurn');
    this.sync();
    if (this.solver.hasPendingSetup()) {
      this.later(SETUP_DELAY_MS, () => {
        const move = this.solver?.playSetup();
        if (move) this.lastMove.set(toKeys(move));
        this.status.set('yourTurn');
        this.sync();
      });
    }
  }

  onMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    const solver = this.solver;
    if (!solver || !this.interactive()) return;
    this.shapes.set([]);
    const result = solver.tryMove(event.orig, event.dest, event.promotion);
    switch (result) {
      case 'wrong':
        this.mistakes++;
        this.mistake.emit();
        this.status.set('wrong');
        this.holdThenTakeBack(event, 'red');
        return;
      case 'alternative':
        this.status.set('alternative');
        this.holdThenTakeBack(event, 'yellow');
        return;
      case 'illegal':
        this.sync();
        return;
      case 'solved':
        this.lastMove.set([event.orig, event.dest]);
        this.finish();
        return;
      case 'continue':
        this.lastMove.set([event.orig, event.dest]);
        this.hintLevel = 0;
        this.status.set('good');
        this.sync();
        this.later(REPLY_DELAY_MS, () => {
          const reply = this.solver?.playReply();
          if (reply) this.lastMove.set(toKeys(reply));
          if (this.solver?.isFinished()) {
            this.finish();
          } else {
            this.sync();
          }
        });
        return;
    }
  }

  /** Leertaste/Enter = „Weiter", sobald die Aufgabe geloest ist (Regeln in `isAdvanceKey`). */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    if (this.status() !== 'solved' || !isAdvanceKey(event)) return;
    event.preventDefault();      // sonst scrollt die Leertaste die Seite
    this.next.emit();
  }

  /**
   * Der Zug bleibt stehen (Zielfeld markiert), dann nimmt das Brett ihn zurueck. Die Stellung danach
   * wird AUSDRUECKLICH gesetzt: das Brett uebernimmt bei jeder geaenderten Eingabe die `fen` — schon
   * das Sperren haette den Zug sonst sofort zurueckspringen lassen.
   */
  private holdThenTakeBack(event: { orig: Key; dest: Key; promotion?: string }, brush: 'red' | 'yellow'): void {
    const shown = this.solver?.fenAfter(event.orig, event.dest, event.promotion);
    if (!shown) {
      this.sync();
      return;
    }
    const before = this.lastMove();
    this.holding.set(true);
    this.fen.set(shown);
    this.lastMove.set([event.orig, event.dest]);
    this.check.set(false);
    this.dests.set(new Map());
    this.shapes.set([{ orig: event.dest, brush }]);
    this.later(WRONG_HOLD_MS, () => {
      this.holding.set(false);
      this.lastMove.set(before);
      this.shapes.set([]);
      this.sync();
    });
  }

  /** Erster Druck: die Figur leuchtet. Zweiter Druck: der Pfeil zeigt den Zug. Fuer die Sterne zaehlt jeder Tipp als
   *  Fehler; im Endlos-Modus ist der erste je Aufgabe frei (`ENDLESS_FREE_HINTS`). Was er kostet, zeigt `hintCost`. */
  showHint(): void {
    const hint = this.solver?.hint();
    if (!hint) return;
    this.mistakes++;
    this.hinted.emit(++this.hintsUsed);
    this.hintLevel = Math.min(this.hintLevel + 1, 2);
    this.shapes.set(this.hintLevel === 1
      ? [{ orig: hint.from as Key, brush: 'yellow' }]
      : [{ orig: hint.from as Key, dest: hint.to as Key, brush: 'green' }]);
  }

  private finish(): void {
    const solver = this.solver;
    this.solvedKey.set(SOLVED_KEYS[Math.floor(Math.random() * SOLVED_KEYS.length)]);
    const comments = this.moveComments();
    this.finalComment.set(solver && comments ? (comments[solver.lastPly()] ?? null) : null);
    this.status.set('solved');
    this.sync();
    this.solved.emit({ mistakes: this.mistakes });
  }

  /** Brett auf den Stand des Solvers bringen. Ein NEUES dests-Objekt auch bei gleicher Stellung:
   *  erst dann setzt das Brett einen falschen Zug optisch zurueck (es reagiert auf Eingabe-Aenderungen). */
  private sync(): void {
    const solver = this.solver;
    if (!solver) return;
    this.fen.set(solver.fen());
    this.turnColor.set(solver.turnColor());
    this.check.set(solver.inCheck());
    this.dests.set(solver.dests() as Map<Key, Key[]>);
  }

  private later(ms: number, fn: () => void): void {
    this.timers.push(setTimeout(fn, ms));
  }

  private clearTimers(): void {
    this.timers.forEach(t => clearTimeout(t));
    this.timers = [];
  }
}

function toKeys(move: KidsMove): [Key, Key] {
  return [move.from as Key, move.to as Key];
}
