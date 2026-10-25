import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Ergebnisfeld des Kalkulations-Modus (Zerlegung Z20): Festlegung, Rechenzeit an der Stellung und der Knopf
 * „Ergebnis" (die fünf Stufen stehen im Dialog — der Modus soll ein Brett bleiben, kein Formular). Reine Darstellung;
 * Texte und Zahlen rechnet die `CalculationComponent`.
 */
@Component({
  selector: 'app-calc-result-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule, TranslatePipe],
  templateUrl: './calc-result-card.component.html',
  styleUrls: ['./calc-result-card.component.scss'],
})
export class CalcResultCardComponent {
  /** Festgelegter erster Zug (SAN) oder null. */
  readonly chosenSan = input<string | null>(null);
  /** Rechenzeit an der Stellung inkl. laufender Sitzung („m:ss"). */
  readonly positionTime = input('');
  /** Ist die Stellung bewertet? */
  readonly graded = input(false);
  /** Beschriftung des Knopfes: „Ergebnis" bzw. die gewählte Stufe. */
  readonly gradeLabel = input('');
  /** Punkte der Stellung mit Maximum („2 / 4"). */
  readonly score = input('');

  readonly openGrade = output<void>();
  readonly analyze = output<void>();
}
