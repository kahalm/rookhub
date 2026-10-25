import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { MIXED, PIN_COLOUR_SCHEMES, PinColourBy, legendOf } from './pin-palette';

/**
 * Das Kartenzubehoer der Turnierkarte: Auswahl „Einfaerben nach" und die aufklappbare Legende.
 * Reine Darstellung — die Wahl geht als `colourPicked` an die Karte, die die Punkte umfaerbt und
 * sie nach draussen meldet. Die Karte haelt den umgebenden Kasten (`.map-chrome`), weil dort
 * Leaflets `disableClickPropagation` sitzt.
 */
@Component({
  selector: 'app-map-legend',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    <label class="colour-by">
      <span>{{ 'tournamentDirectory.map.colourBy' | translate }}</span>
      <select [value]="colourBy()" (change)="pick($event)">
        @for (scheme of schemes; track scheme) {
          <option [value]="scheme">
            {{ 'tournamentDirectory.map.colourScheme.' + scheme | translate }}
          </option>
        }
      </select>
    </label>

    <button type="button" class="legend-toggle" (click)="toggleLegend()"
            [attr.aria-expanded]="legendOpen()">
      <span class="chevron" [class.open]="legendOpen()">&#9656;</span>
      {{ 'tournamentDirectory.map.legendTitle' | translate }}
    </button>

    @if (legendOpen()) {
    <ul class="legend">
      @for (category of legend(); track category.key) {
        <li>
          <i [style.background]="category.fill" [style.border-color]="category.stroke"></i>
          {{ category.label | translate }}
        </li>
      }
      @if (mixed()) {
        <li>
          <i [style.background]="mixedCategory.fill" [style.border-color]="mixedCategory.stroke"></i>
          {{ mixedCategory.label | translate }}
        </li>
      }
      <li>
        <i class="marked"></i> {{ 'tournamentDirectory.map.legend.bookmarked' | translate }}
      </li>
      <li>
        <i class="vague"></i> {{ 'tournamentDirectory.map.legend.approximate' | translate }}
      </li>
    </ul>
    }
  `,
  styleUrls: ['./map-legend.component.scss'],
})
export class MapLegendComponent {
  /** Das gewaehlte Merkmal; die Legende zeigt seine Klassen in dieser Reihenfolge. */
  readonly colourBy = input<PinColourBy>('speed');
  /**
   * Steht auf der Karte ein gebuendelter Punkt mit UNTERSCHIEDLICHEN Klassen? Nur dann gehoert
   * „gemischt" in die Legende — sonst erklaerte sie eine Farbe, die nirgends vorkommt.
   */
  readonly mixed = input(false);
  /** Der Betrachter hat ein anderes Merkmal gewaehlt. */
  readonly colourPicked = output<PinColourBy>();

  readonly schemes = PIN_COLOUR_SCHEMES;
  readonly mixedCategory = MIXED;
  readonly legend = computed(() => legendOf(this.colourBy()));

  /**
   * Ist die Legende aufgeklappt? Zugeklappt als Vorgabe: sie erklaert eine Bildsprache, die man
   * EINMAL nachliest — dauerhaft aufgeklappt verdeckt sie auf einem Handy ein Viertel der Karte,
   * also genau das, wofuer man sie aufgeschlagen hat.
   */
  readonly legendOpen = signal(false);

  toggleLegend(): void {
    this.legendOpen.update(open => !open);
  }

  pick(event: Event): void {
    this.colourPicked.emit((event.target as HTMLSelectElement).value as PinColourBy);
  }
}
