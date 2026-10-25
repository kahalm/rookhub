import { ChangeDetectionStrategy, Component, inject, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CALC_EVALS, CALC_GLYPHS, CalcEval, CalcGlyph, evalNameKey, glyphNameKey } from './calc-tree.util';

/**
 * Editor-Leiste des Kalkulations-Modus (Zerlegung Z20): Zug-Symbole, Stellungseinschätzung und Kommentar zum
 * ausgewählten Zug. Werkzeug erscheint am Werkstück — die Komponente blendet die Karte nur mit Auswahl ein. Reine
 * Darstellung; der Kommentar-Entwurf gehört der Seite (`comment`, zweiseitig).
 */
@Component({
  selector: 'app-calc-annotate-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule, TranslatePipe],
  templateUrl: './calc-annotate-card.component.html',
  styleUrls: ['./calc-annotate-card.component.scss'],
})
export class CalcAnnotateCardComponent {
  private readonly translate = inject(TranslateService);

  /** Der ausgewählte Zug (SAN). */
  readonly san = input('');
  readonly glyph = input<CalcGlyph | null>(null);
  readonly evaluation = input<CalcEval | null>(null);
  readonly comment = model('');

  readonly glyphPicked = output<CalcGlyph>();
  readonly evalPicked = output<CalcEval>();
  readonly cleared = output<void>();
  readonly commentSaved = output<void>();

  readonly glyphs = CALC_GLYPHS;
  readonly evals = CALC_EVALS;

  /**
   * Erklärung eines Symbols fürs Mouseover: erst was es bedeutet („Weiß gewinnt"), dann der
   * Bedienhinweis. Der Übersetzungs-Schlüssel kommt aus `calc-tree.util` — dieselbe Quelle wie
   * die Symbolliste, damit kein Symbol ohne Erklärung bleibt.
   */
  glyphTooltip(glyph: CalcGlyph): string {
    return this.symbolTooltip(glyphNameKey(glyph));
  }

  evalTooltip(evaluation: CalcEval): string {
    return this.symbolTooltip(evalNameKey(evaluation));
  }

  private symbolTooltip(nameKey: string): string {
    return `${this.translate.instant(nameKey)} (${this.translate.instant('calc.symbolToggleHint')})`;
  }
}
