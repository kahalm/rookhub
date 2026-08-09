import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import {
  CalcEval, CalcGlyph, CalcLine, CalcNode, CalcTree, evalNameKey, glyphNameKey, lines, plyPrefix,
} from './calc-tree.util';

/**
 * Die „Visualisierung" des Kalkulations-Modus: die eingeklickten Linien als Notation.
 * Das Brett bleibt eingefroren — HIER sieht der Nutzer, was er gerechnet hat.
 *
 * Reine Darstellung + Auswahl: Züge sind anklickbar (setzt den Cursor, um dort abzuzweigen oder
 * ein Symbol zu setzen), je Linie gibt es Kommentar und Löschen, oben den (+)-Knopf für eine neue
 * Linie ab der Ausgangsstellung. Der Baum wird von der Eltern-Komponente in place verändert;
 * `revision` (Zähler) ist der Anstoß zum Neuzeichnen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-calc-lines',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="calc-lines">
      <div class="cl-head">
        <span class="cl-title">{{ 'calc.lines' | translate }}</span>
        <span class="cl-count" *ngIf="allLines.length">{{ allLines.length }}</span>
        <span class="cl-spacer"></span>
        <button mat-icon-button class="cl-add" [matTooltip]="'calc.newLine' | translate"
                [attr.aria-label]="'calc.newLine' | translate" (click)="newLine.emit()">
          <mat-icon>add_circle_outline</mat-icon>
        </button>
        <!-- Werkzeuge des Elternteils sitzen in DIESER Kopfzeile statt als eigene Knopfreihe
             darunter — sonst steht neben der Liste eine zweite Leiste mit denselben Aufgaben. -->
        <ng-content select="[headTools]"></ng-content>
      </div>

      @if (allLines.length === 0) {
        <p class="cl-empty">{{ 'calc.noLines' | translate }}</p>
      } @else {
        <ol class="cl-list">
          @for (line of allLines; track line.leafId) {
            <li class="cl-line" [class.cl-line--active]="isActiveLine(line)"
                [class.cl-line--chosen]="isChosenLine(line)">
              <div class="cl-moves">
                <!-- Stern am ERSTEN Zug = „meine Wahl". Nur an der ersten Linie, die mit diesem
                     Zug beginnt (sharedPrefix = 0) — sonst stünde derselbe Stern je Abzweigung
                     noch einmal da. -->
                @if (line.sharedPrefix === 0 && line.moves.length) {
                  <button type="button" class="cl-star" [class.cl-star--on]="isChosenLine(line)"
                          [matTooltip]="'calc.review.chooseHint' | translate"
                          [attr.aria-pressed]="isChosenLine(line)"
                          [attr.aria-label]="'calc.review.choose' | translate"
                          (click)="chooseMove.emit(line.moves[0].id)">
                    <mat-icon>{{ isChosenLine(line) ? 'star' : 'star_border' }}</mat-icon>
                  </button>
                }
                @for (move of line.moves; track move.id) {
                  <button type="button" class="cl-move"
                          [class.cl-move--shared]="$index < line.sharedPrefix"
                          [class.cl-move--cursor]="move.id === cursorId"
                          (click)="selectNode.emit(move.id)">
                    <span class="cl-num" *ngIf="prefixFor($index)">{{ prefixFor($index) }}</span>{{ move.san
                    }}<span class="cl-glyph" *ngIf="move.glyph"
                            [attr.title]="glyphName(move.glyph!)">{{ move.glyph }}</span
                    ><span class="cl-eval" *ngIf="move.evaluation"
                           [attr.title]="evalName(move.evaluation!)">{{ move.evaluation }}</span>
                  </button>
                  @if (move.comment && move.id !== line.leafId) {
                    <span class="cl-inline-comment">{{ braced(move.comment) }}</span>
                  }
                }
              </div>

              <div class="cl-actions">
                <button mat-icon-button class="cl-icon" [matTooltip]="'calc.comment' | translate"
                        (click)="toggleComment(line.leafId)">
                  <mat-icon>{{ leafComment(line) ? 'chat' : 'chat_bubble_outline' }}</mat-icon>
                </button>
                <button mat-icon-button class="cl-icon" [matTooltip]="'calc.deleteLine' | translate"
                        (click)="deleteLine.emit(line.leafId)">
                  <mat-icon>delete_outline</mat-icon>
                </button>
              </div>

              @if (editingLeafId === line.leafId) {
                <div class="cl-comment-edit">
                  <input type="text" [(ngModel)]="draftComment" maxlength="500"
                         [placeholder]="'calc.commentPlaceholder' | translate"
                         (keydown.enter)="commitComment(line.leafId)"
                         (keydown.escape)="cancelComment()" />
                  <button mat-button (click)="commitComment(line.leafId)">{{ 'common.save' | translate }}</button>
                  <button mat-button (click)="cancelComment()">{{ 'common.cancel' | translate }}</button>
                </div>
              } @else if (leafComment(line)) {
                <div class="cl-comment">{{ leafComment(line) }}</div>
              }
            </li>
          }
        </ol>
      }
    </div>
  `,
  styles: [`
    .calc-lines { display: flex; flex-direction: column; gap: 0.35rem; }
    .cl-head { display: flex; align-items: center; gap: 0.4rem; }
    .cl-title { font-weight: 600; }
    .cl-count {
      font-size: 0.72rem; font-weight: 700; padding: 1px 7px; border-radius: 10px;
      background: color-mix(in srgb, currentColor 12%, transparent);
    }
    .cl-spacer { flex: 1; }
    /* Alle vier Werkzeuge der Kopfzeile sollen als GESCHWISTER lesbar sein: gleiche Größe,
       gleiche Strichstärke. Das gefüllte, größere Plus fiel aus der Reihe. */
    .cl-add mat-icon { font-size: 24px; width: 24px; height: 24px; }
    .cl-empty { margin: 0.2rem 0; font-style: italic; color: color-mix(in srgb, currentColor 45%, transparent); }
    .cl-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.2rem; }
    .cl-line {
      /* Die beiden Knöpfe stehen MITTIG zur Zeile — mit align-items: start hingen sie sichtbar
         tiefer als die Züge, neben denen sie stehen (Knopfhöhe > Textzeile). Achtung: in diesem
         styles-Literal darf KEIN Backtick vorkommen, er würde es beenden. */
      /* minmax(0, max-content): die Zuege nehmen ihre natuerliche Breite, die Knoepfe stehen
         DIREKT daneben. Mit 1fr klebten sie am rechten Rand — in einer breiten Spalte lagen
         mehrere hundert Pixel Leere zwischen einer Linie und ihren eigenen Knoepfen. */
      display: grid; grid-template-columns: minmax(0, max-content) auto; align-items: center;
      gap: 0.2rem;
      padding: 0.25rem 0.3rem; border-radius: 6px;
      border-left: 3px solid transparent;
    }
    .cl-line--active {
      border-left-color: #1976d2;
      background: color-mix(in srgb, currentColor 6%, transparent);
    }
    /* Festlegung: die Linie unter dem gewählten ersten Zug bleibt sichtbar hervorgehoben,
       auch wenn der Cursor gerade woanders steht. */
    .cl-line--chosen {
      border-left-color: #f9a825;
      background: color-mix(in srgb, #f9a825 10%, transparent);
    }
    .cl-line--chosen.cl-line--active { border-left-color: #f57f17; }
    .cl-line--chosen .cl-move { font-weight: 600; }
    .cl-star {
      border: none; background: none; padding: 0 2px; cursor: pointer;
      color: color-mix(in srgb, currentColor 45%, transparent); line-height: 1;
    }
    .cl-star:hover { color: #f9a825; }
    .cl-star--on { color: #f9a825; }
    .cl-star mat-icon { font-size: 17px; width: 17px; height: 17px; vertical-align: middle; }
    .cl-moves { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.15rem; }
    .cl-move {
      border: none; background: none; color: inherit; cursor: pointer;
      font-size: 1em; font-family: inherit; padding: 1px 3px; border-radius: 4px;
    }
    .cl-move:hover { background: color-mix(in srgb, currentColor 12%, transparent); }
    .cl-move--shared { color: color-mix(in srgb, currentColor 40%, transparent); }
    .cl-move--cursor {
      background: #1976d2; color: #fff; font-weight: 600;
    }
    .cl-num { margin-right: 2px; opacity: 0.65; font-variant-numeric: tabular-nums; }
    .cl-glyph { font-weight: 700; }
    .cl-eval { margin-left: 2px; }
    .cl-inline-comment { font-size: 0.85em; font-style: italic; color: color-mix(in srgb, currentColor 55%, transparent); }
    /* Die beiden Knöpfe gehören zur ERSTEN Zugzeile, nicht zur ganzen (ggf. mehrzeiligen) Linie:
       oben ausrichten und so hoch wie eine Textzeile halten, sonst hängen sie sichtbar tiefer als
       die Züge, neben denen sie stehen. */
    .cl-actions { display: flex; align-items: center; }
    /* HIER lag der Versatz: ein mat-icon-button bringt 12px Innenpolster mit. Schrumpft man ihn
       auf 24px, bleibt kein Inhaltsplatz — das Symbol wurde von der Polsterkante aus gesetzt und
       sass damit sichtbar zu tief und zu weit rechts. Ohne Polster, dafuer selbst zentriert. */
    .cl-icon {
      width: 24px; height: 24px; padding: 0; line-height: 1;
      display: inline-flex; align-items: center; justify-content: center;
      opacity: 0.55;
    }
    .cl-line:hover .cl-icon, .cl-icon:focus-visible { opacity: 1; }
    .cl-icon mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .cl-comment { grid-column: 1 / -1; font-size: 0.85em; font-style: italic; opacity: 0.8; padding-left: 3px; }
    .cl-comment-edit { grid-column: 1 / -1; display: flex; gap: 0.25rem; align-items: center; flex-wrap: wrap; }
    .cl-comment-edit input {
      flex: 1 1 12rem; min-width: 8rem; padding: 4px 6px; border-radius: 4px;
      border: 1px solid color-mix(in srgb, currentColor 30%, transparent);
      background: transparent; color: inherit; font: inherit;
    }
  `],
})
export class CalcLinesComponent {
  @Input() tree!: CalcTree;
  @Input() cursorId = 0;
  @Input() startFen = '';
  /** Zähler: ändert sich, sobald der Baum verändert wurde → Neuzeichnen. */
  @Input() revision = 0;
  /** UCI des Zuges, auf den sich der Nutzer festgelegt hat (immer ein ERSTER Zug); null = keiner. */
  @Input() chosenUci: string | null = null;

  @Output() selectNode = new EventEmitter<number>();
  @Output() newLine = new EventEmitter<void>();
  @Output() deleteLine = new EventEmitter<number>();
  @Output() commentChanged = new EventEmitter<{ nodeId: number; text: string }>();
  /** Stern am ersten Zug geklickt (Knoten-Id) — die Eltern-Komponente schaltet die Festlegung um. */
  @Output() chooseMove = new EventEmitter<number>();

  editingLeafId: number | null = null;
  draftComment = '';

  constructor(private translate: TranslateService) {}

  /** Bedeutung eines Symbols in der Notation (Mouseover) — „+−" = „Weiß gewinnt". */
  glyphName(glyph: CalcGlyph): string {
    return this.translate.instant(glyphNameKey(glyph));
  }

  evalName(evaluation: CalcEval): string {
    return this.translate.instant(evalNameKey(evaluation));
  }

  get allLines(): CalcLine[] {
    return this.tree ? lines(this.tree) : [];
  }

  /** Zugnummer vor dem Halbzug an Position `index` der Linie (Linienanfang trägt immer eine). */
  prefixFor(index: number): string {
    return plyPrefix(this.startFen, index, index === 0);
  }

  /** Liegt der Cursor auf dieser Linie? (Dann ist sie die „aktive".) */
  isActiveLine(line: CalcLine): boolean {
    return line.moves.some(m => m.id === this.cursorId);
  }

  /**
   * Beginnt diese Linie mit dem festgelegten Zug? Verglichen wird über UCI, nicht über die
   * Knoten-Id: die Festlegung überlebt so das Löschen und erneute Eingeben desselben Zuges.
   * Alle Linien unter der Wahl (auch die Abzweigungen) gelten als hervorgehoben.
   */
  isChosenLine(line: CalcLine): boolean {
    return !!this.chosenUci && line.moves[0]?.uci === this.chosenUci;
  }

  leafComment(line: CalcLine): string | undefined {
    return this.leafOf(line)?.comment;
  }

  /** Kommentar in geschweifte Klammern setzen (PGN-Schreibweise) — als Methode, weil geschweifte
   *  Klammern direkt neben einer Interpolation den Angular-Template-Parser stören. */
  braced(text: string): string {
    return `{${text}}`;
  }

  toggleComment(leafId: number): void {
    if (this.editingLeafId === leafId) { this.cancelComment(); return; }
    this.editingLeafId = leafId;
    this.draftComment = this.allLines.find(l => l.leafId === leafId)?.moves.at(-1)?.comment ?? '';
  }

  commitComment(leafId: number): void {
    this.commentChanged.emit({ nodeId: leafId, text: this.draftComment });
    this.cancelComment();
  }

  cancelComment(): void {
    this.editingLeafId = null;
    this.draftComment = '';
  }

  private leafOf(line: CalcLine): CalcNode | undefined {
    return line.moves.at(-1);
  }
}
