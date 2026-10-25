import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';

/**
 * Hinweiszeile unter dem Brett des Buch-Lösers (Zerlegung Z18): Visualisierungs-Countdown, Zugrecht bzw. die
 * Vorspielzüge, Auge (Figuren zeigen), Tipp-Knopf und „Tipps melden“. Der Container rendert sie zweimal (unter dem
 * Brett und im App-Vollbild in der Seitenspalte); die Grundregel `.board-hint` steht global in styles.scss.
 */
@Component({
  selector: 'app-book-board-hint',
  imports: [MatIconModule, TranslatePipe, IconLabelDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './book-board-hint.component.html',
  styleUrl: './book-board-hint.component.scss',
})
export class BookBoardHintComponent {
  readonly countdownSeconds = input(0);
  /** Vorspielzüge der Visualisierung als HTML; leer = Zugrecht zeigen. */
  readonly moveHtml = input('');
  readonly turnColor = input<'white' | 'black'>('white');
  /** Auge anbieten (Figuren ausgeblendet bzw. Stufe 1) und ob es gerade gedrückt ist. */
  readonly showEye = input(false);
  readonly eyeShowing = input(false);
  /** Tipp-Knopf: es gibt (weitere) Tipps; `hintLevel` = schon aufgedeckt, `totalHints` = Stufen. */
  readonly showTip = input(false);
  readonly hintLevel = input(0);
  readonly totalHints = input(0);
  /** „Tipps melden“: sichtbar, gemeldet, Speichern läuft. */
  readonly canFlag = input(false);
  readonly flagged = input(false);
  readonly flagSaving = input(false);

  readonly eyeClicked = output<void>();
  readonly tipClicked = output<void>();
  readonly flagClicked = output<void>();
}
