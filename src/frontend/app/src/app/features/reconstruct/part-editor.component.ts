import { ChangeDetectionStrategy, Component, ElementRef, inject, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';

/** Blätter-Ziel des Zug-Editors: Halbzüge vor/zurück oder an Anfang/Ende. */
export type PlyStep = number | 'start' | 'end';

/**
 * Der Zug-Editor EINES Teils der Rekonstruktion: Seite am Zug, Brett, Blätterleiste, Zugtext,
 * Notiz und Knöpfe (Zerlegung Z25, aus `ReconstructDetailComponent`). Reine Darstellung — Züge,
 * Blätter-Stand und Prüfung hält die Seite; hier wird angezeigt und gemeldet.
 */
@Component({
  selector: 'app-part-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule,
    TranslatePipe, ChessBoardComponent, IconLabelDirective],
  template: `
    <div class="side-row" [hidden]="generated()">
      <span class="muted">{{ 'reconstruct.sideToMove' | translate }}</span>
      <mat-button-toggle-group [value]="sideBlack() ? 'b' : 'w'" hideSingleSelectionIndicator="true"
                               (change)="sideChange.emit($event.value === 'b')">
        <mat-button-toggle value="w">{{ 'reconstruct.sideWhite' | translate }}</mat-button-toggle>
        <mat-button-toggle value="b">{{ 'reconstruct.sideBlack' | translate }}</mat-button-toggle>
      </mat-button-toggle-group>
    </div>

    <div class="board-wrap" tabindex="0">
      <app-chess-board [fen]="fen()" [playable]="playable()" (userMove)="boardMove.emit($event)"
                       [boardTheme]="boardTheme()" [pieceSet]="pieceSet()"></app-chess-board>
    </div>

    <!-- Blättern durch die eingetippten Züge (am PC auch mit den Pfeiltasten). -->
    <div class="ply-nav">
      <button mat-icon-button [disabled]="plyShown() === 0" (click)="goPly.emit('start')"
              [appIconLabel]="'reconstruct.plyFirst' | translate">
        <mat-icon>first_page</mat-icon>
      </button>
      <button mat-icon-button [disabled]="plyShown() === 0" (click)="goPly.emit(-1)"
              [appIconLabel]="'reconstruct.plyPrev' | translate">
        <mat-icon>chevron_left</mat-icon>
      </button>
      <span class="muted ply-count">{{ 'reconstruct.plyPosition' | translate:{ shown: plyShown(), total: plyTotal() } }}</span>
      <button mat-icon-button [disabled]="atEnd()" (click)="goPly.emit(1)"
              [appIconLabel]="'reconstruct.plyNext' | translate">
        <mat-icon>chevron_right</mat-icon>
      </button>
      <button mat-icon-button [disabled]="atEnd()" (click)="goPly.emit('end')"
              [appIconLabel]="'reconstruct.plyLast' | translate">
        <mat-icon>last_page</mat-icon>
      </button>
      @if (!atEnd()) {
        <button mat-stroked-button (click)="truncate.emit()">
          <mat-icon>content_cut</mat-icon> {{ 'reconstruct.continueHere' | translate }}
        </button>
      }
    </div>
    <p class="muted small arrow-hint">{{ 'reconstruct.arrowHint' | translate }}</p>
    <mat-form-field appearance="outline" class="wide">
      <mat-label>{{ 'reconstruct.movesLabel' | translate }}</mat-label>
      <textarea matInput rows="3" [ngModel]="moves()" (ngModelChange)="movesInput.emit($event)"
                [placeholder]="'reconstruct.movesPlaceholder' | translate"></textarea>
      <mat-hint>{{ 'reconstruct.movesHint' | translate }}</mat-hint>
    </mat-form-field>
    @if (badMove()) {
      <p class="bad-hint">{{ 'reconstruct.badMove' | translate:{ move: badMove() } }}</p>
      <p class="muted small">{{ 'reconstruct.boardLocked' | translate }}</p>
    }
    <mat-form-field appearance="outline" class="wide">
      <mat-label>{{ 'reconstruct.partNote' | translate }}</mat-label>
      <input matInput [ngModel]="note()" (ngModelChange)="note.set($event)" maxlength="500">
    </mat-form-field>
    <div class="editor-actions">
      <button mat-flat-button color="primary" [disabled]="busy() || generated()" (click)="save.emit()">
        {{ 'common.save' | translate }}
      </button>
      @if (isNew()) {
        <!-- „Hier komme ich mit Zügen nicht weiter": speichert das Getippte und macht mit
             der nächsten erinnerten Stellung weiter (leerer Editor wechselt nur die Art). -->
        <button mat-stroked-button [disabled]="busy()" (click)="toPosition.emit()">
          <mat-icon>grid_on</mat-icon> {{ 'reconstruct.enterPosition' | translate }}
        </button>
      }
      <button mat-button (click)="cancel.emit()">{{ 'common.cancel' | translate }}</button>
      <button mat-button [disabled]="!moves().trim()" (click)="undo.emit()">
        <mat-icon>undo</mat-icon> {{ 'reconstruct.undoMove' | translate }}
      </button>
    </div>
  `,
  styles: [`
    /* Kein eigener Kasten: die Teile stehen direkt im Editor-Abschnitt wie vor der Zerlegung. */
    :host { display: contents; }
    .muted { opacity: 0.75; }
    .wide { width: 100%; }

    /* Das Brett ist QUADRATISCH: jede Breite ist auch eine Höhe. Ohne die Schranke gegen die
       Fensterhöhe füllte es auf einem breiten Bildschirm die halbe Seite und schob Zugfeld und
       Speichern-Knopf unter den Rand — man musste an einem riesigen Brett vorbeiscrollen, um den
       eingetippten Zug zu sehen. 56vh lässt darunter Platz für Eingabe und Knöpfe. */
    app-chess-board {
      display: block;
      width: 100%;
      max-width: min(100%, 56vh);
      margin-inline: auto;
      margin-bottom: 0.5rem;
    }

    .editor-actions { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }

    .side-row { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.4rem; flex-wrap: wrap; }

    /* Fokussierbar, damit die Pfeiltasten hier ankommen und nicht im Zug-Textfeld landen. */
    .board-wrap {
      outline: none;

      &:focus-visible { outline: 2px solid color-mix(in srgb, currentColor 45%, transparent); outline-offset: 3px; border-radius: 6px; }
    }

    .arrow-hint { max-width: min(100%, 56vh); margin: -0.2rem auto 0.5rem; }

    /* Die Blätterleiste gehört unter das Brett, in Brettbreite — am Handy gibt es keine Pfeiltasten. */
    .ply-nav {
      display: flex;
      align-items: center;
      gap: 0.15rem;
      flex-wrap: wrap;
      max-width: min(100%, 56vh);
      margin: 0 auto 0.5rem;

      .ply-count { font-variant-numeric: tabular-nums; margin: 0 0.35rem; }
    }

    .bad-hint { color: var(--rh-error); margin: 0 0 0.5rem; }

    /* b1-rk-recon-breitstufe: Breitbild-Stufe (DESIGN 4.1) — Brett höchstens bis Fensterhöhe (Platz für Zugfeld +
       Knöpfe darunter). Unter 2400 px unverändert. */
    @media (min-width: 2400px) {
      app-chess-board,
      .arrow-hint,
      .ply-nav { max-width: min(100%, calc(100vh - 360px)); }
    }
  `],
})
export class PartEditorComponent {
  /** Stellung nach den gezeigten Zügen — das Brett zeigt sie und spielt darauf weiter. */
  readonly fen = input.required<string>();
  readonly playable = input(false);
  readonly boardTheme = input<string>('');
  readonly pieceSet = input<string>('');
  readonly plyShown = input(0);
  readonly plyTotal = input(0);
  readonly atEnd = input(true);
  /** Der eingetippte Zugtext (die Seite hält ihn; Änderungen kommen über `movesInput`). */
  readonly moves = input('');
  readonly badMove = input<string | null>(null);
  readonly note = model('');
  readonly sideBlack = input(false);
  /** Ein Vorschlag wird nicht gespeichert, und die Seite am Zug steht fest. */
  readonly generated = input(false);
  /** Ein neues, noch nicht gespeichertes Teil („Stellung eingeben" möglich). */
  readonly isNew = input(false);
  readonly busy = input(false);

  readonly boardMove = output<UserBoardMove>();
  readonly goPly = output<PlyStep>();
  readonly truncate = output<void>();
  readonly movesInput = output<string>();
  readonly sideChange = output<boolean>();
  readonly save = output<void>();
  readonly toPosition = output<void>();
  readonly cancel = output<void>();
  readonly undo = output<void>();

  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /**
   * Den Kasten um das Brett fokussieren, damit die Pfeiltasten sofort blättern. Über den Host statt
   * einer Abfrage: die Seite ruft das per `setTimeout`, und nach dem Verlassen darf es nicht werfen.
   */
  focusBoard(): void {
    this.host.nativeElement.querySelector<HTMLElement>('.board-wrap')?.focus({ preventScroll: true });
  }
}
