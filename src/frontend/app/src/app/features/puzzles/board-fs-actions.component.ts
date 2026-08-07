import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import {
  PuzzleSolverMode, SOLVER_ACTION_KEYS, canMouseslip, canReset, isSolvingState,
} from './solver-actions.util';

/**
 * Kompakte Icon-Leiste für das BRETT-VOLLBILD: Tipp, Zurücksetzen, Mausrutscher, Aufgeben.
 * Im Vollbild rendert der Browser nur den Teilbaum der Vollbild-Hülle — die normale
 * Aktionszeile (`puzzle-your-turn`) und die Tipp-Zeile unter dem Brett sind dort also weg.
 * Diese Leiste wird deshalb per `<ng-content>` IN die Hülle projiziert und sitzt in den
 * sonst ungenutzten schwarzen Balken: quer daneben, hochkant darunter.
 *
 * Bewusst klein, quadratisch, halbtransparent und nur mit Icon — sie soll das Brett nicht
 * stören. Erklärt wird jeder Knopf per nativem `title`: CDK-Overlays (matTooltip) hängen am
 * `<body>` und sind im Vollbild unsichtbar.
 *
 * Sichtbarkeits-Regeln kommen aus `solver-actions.util`, damit sie mit der normalen
 * Aktionszeile identisch bleiben.
 */
@Component({
  selector: 'app-board-fs-actions',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, TranslatePipe],
  // Nur im Brett-Vollbild sichtbar (globale Regel in styles.scss).
  host: { 'data-fs-only': '' },
  template: `
    @if (visible) {
      @if (showHint) {
        <button type="button" class="bfa-btn" (click)="hintClicked.emit()"
                [attr.title]="hintTitle | translate" [attr.aria-label]="hintTitle | translate">
          <mat-icon>lightbulb</mat-icon>
          @if (hintLevel > 0 && totalHints > 0) {
            <span class="bfa-badge">{{ hintLevel }}/{{ totalHints }}</span>
          }
        </button>
      }
      @if (showReset) {
        <button type="button" class="bfa-btn" (click)="resetClicked.emit()"
                [attr.title]="keys.reset | translate" [attr.aria-label]="keys.reset | translate">
          <mat-icon>replay</mat-icon>
        </button>
      }
      @if (showMouseslipAction) {
        <button type="button" class="bfa-btn" (click)="mouseslipClicked.emit()"
                [attr.title]="keys.mouseslip | translate" [attr.aria-label]="keys.mouseslip | translate">
          <mat-icon>mouse</mat-icon>
        </button>
      }
      <button type="button" class="bfa-btn bfa-btn--warn" (click)="giveUpClicked.emit()"
              [attr.title]="keys.giveUp | translate" [attr.aria-label]="keys.giveUp | translate">
        <mat-icon>flag</mat-icon>
      </button>
    }
  `,
  styles: [`
    /* display kommt aus der globalen [data-fs-only]-Regel (none / im Vollbild flex). */
    :host {
      position: absolute;
      z-index: 70;
      gap: 10px;
      align-items: center;
      justify-content: center;
      pointer-events: none;        /* nur die Knöpfe fangen Klicks, nie der leere Balken */
    }
    /* Querformat: linker schwarzer Balken, Knöpfe untereinander mittig.
       Breite = Balkenbreite (min. Knopfbreite, damit sie bei fast quadratischen
       Bildschirmen nicht ins Nichts rutschen). */
    @media (min-aspect-ratio: 1/1) {
      :host {
        flex-direction: column;
        left: 0; top: 50%;
        transform: translateY(-50%);
        width: max(48px, calc((100vw - 100vh) / 2));
      }
    }
    /* Hochformat: unterer Balken, Knöpfe nebeneinander mittig. */
    @media (max-aspect-ratio: 1/1) {
      :host {
        flex-direction: row;
        bottom: 0; left: 50%;
        transform: translateX(-50%);
        height: max(48px, calc((100vh - 100vw) / 2));
      }
    }
    .bfa-btn {
      pointer-events: auto;
      position: relative;
      width: 38px; height: 38px;
      display: grid; place-items: center;
      padding: 0;
      border: 0;
      border-radius: 8px;
      cursor: pointer;
      background: rgba(0, 0, 0, 0.35);
      color: #fff;
      opacity: 0.7;
      transition: opacity 0.12s ease-in-out, background 0.12s ease-in-out;
    }
    .bfa-btn:hover, .bfa-btn:focus-visible { opacity: 1; background: rgba(0, 0, 0, 0.6); }
    .bfa-btn mat-icon { font-size: 22px; width: 22px; height: 22px; line-height: 22px; }
    .bfa-btn--warn { color: #ff8a80; }
    /* Tipp-Stand, sobald der erste Tipp gezogen wurde (vorher bleibt der Knopf reines Icon). */
    .bfa-badge {
      position: absolute;
      right: -3px; bottom: -3px;
      font-size: 9px; line-height: 1;
      padding: 1px 3px;
      border-radius: 6px;
      background: rgba(0, 0, 0, 0.75);
      color: #fff;
    }
  `],
})
export class BoardFsActionsComponent {
  /** Übersetzungs-Variante (Standard/Endless/Buch) — gleiche Keys wie die normale Aktionszeile. */
  @Input() mode: PuzzleSolverMode = 'standard';
  @Input() state = 'LOADING';
  /** Im Review/Nachschau-Modus gibt es nichts zu lösen → Leiste bleibt weg. */
  @Input() reviewMode = false;
  /** Tipps für dieses Puzzle vorhanden (Base-Solver `hasHints`). */
  @Input() hasHints = false;
  /** Noch ungezeigte Tipp-Stufen übrig (Base-Solver `canShowMoreHints`). */
  @Input() canShowMoreHints = false;
  @Input() hintLevel = 0;
  @Input() totalHints = 0;
  /** !mouseslipUsed && (!onSolutionPath || hasMadeFirstMove) — berechnet im Eltern (wie im Panel). */
  @Input() showMouseslip = false;
  /** Endless zeigt den Mausrutscher auch im THINKING-State. */
  @Input() showMouseslipInThinking = false;
  @Input() hasMadeFirstMove = false;

  @Output() hintClicked = new EventEmitter<void>();
  @Output() resetClicked = new EventEmitter<void>();
  @Output() mouseslipClicked = new EventEmitter<void>();
  @Output() giveUpClicked = new EventEmitter<void>();

  get visible(): boolean { return !this.reviewMode && isSolvingState(this.state); }
  get keys() { return SOLVER_ACTION_KEYS[this.mode]; }
  get showHint(): boolean { return this.hasHints && this.canShowMoreHints; }
  get hintTitle(): string { return this.hintLevel === 0 ? 'puzzles.hints.show' : 'puzzles.hints.next'; }
  get showReset(): boolean { return canReset(this.state, this.hasMadeFirstMove); }
  get showMouseslipAction(): boolean {
    return canMouseslip(this.state, this.showMouseslip, this.hasMadeFirstMove, this.showMouseslipInThinking);
  }
}
