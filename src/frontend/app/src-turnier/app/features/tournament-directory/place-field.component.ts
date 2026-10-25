import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { IconLabelDirective } from '@rh/shared/icon-label/icon-label.directive';
import { GeoPlaceSuggestion } from './tournament-directory.model';

/**
 * Das Ortsfeld der Filterleiste: der Mittelpunkt der Umkreissuche. Er kommt entweder aus einem
 * gespeicherten Suchprofil, aus der Ortsliste oder vom Browser — das Feld zeigt in JEDEM Fall, was
 * gerade gilt, sonst behauptet die Leiste etwas anderes als die Liste darunter.
 *
 * <p>Reine Darstellung (Zerlegung Z28): Suche (`PlaceSearch`), Ortung und was ein Ort mit dem
 * Filter macht, liegen in `TournamentDirectoryComponent`. Die Breite steuert der Elternteil ueber
 * die Klasse `place-field` am Host.</p>
 */
@Component({
  selector: 'app-place-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, MatAutocompleteModule, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressSpinnerModule, IconLabelDirective, TranslatePipe,
  ],
  template: `
    <!-- subscriptSizing=dynamic: das Feld zeigt weder Hint noch Fehler, der reservierte Streifen
         darunter kostete auf dem Handy ~22 px je Feldzeile. -->
    <mat-form-field appearance="outline" class="field" subscriptSizing="dynamic">
      <mat-icon matPrefix>place</mat-icon>
      <mat-label>{{ 'tournamentDirectory.place.label' | translate }}</mat-label>
      <input matInput [ngModel]="label()" (ngModelChange)="labelChange.emit($event)"
             [matAutocomplete]="placeAuto" [placeholder]="'tournamentDirectory.place.hint' | translate" />
      <mat-autocomplete #placeAuto (optionSelected)="chosen.emit($event.option.value)">
        @for (place of suggestions(); track place.label) {
          <mat-option [value]="place">{{ place.label }}</mat-option>
        }
        <!-- Leer und kaputt sehen sonst gleich aus: nach der Sanduhr stand einfach nichts da. -->
        @if (noMatch()) {
          <mat-option disabled class="place-note">{{ 'tournamentDirectory.place.noMatch' | translate }}</mat-option>
        } @else if (searchFailed()) {
          <mat-option disabled class="place-note">{{ 'tournamentDirectory.place.searchFailed' | translate }}</mat-option>
        }
      </mat-autocomplete>

      @if (searching()) {
        <mat-spinner matSuffix diameter="18" />
      } @else if (label()) {
        <button matSuffix mat-icon-button (click)="cleared.emit()"
                [attr.aria-label]="'common.clear' | translate">
          <mat-icon>close</mat-icon>
        </button>
      }
      @if (geolocationSupported()) {
        <button matSuffix mat-icon-button (click)="locate.emit()" [disabled]="locating()"
                [appIconLabel]="'tournamentDirectory.place.useCurrent' | translate">
          <mat-icon>{{ locating() ? 'hourglass_top' : 'my_location' }}</mat-icon>
        </button>
      }
    </mat-form-field>
  `,
  styles: [`
    :host { display: block; }
    /* Block statt inline-flex: sonst stuende unter dem Feld der Unterlaengen-Spalt einer Textzeile. */
    .field { display: flex; width: 100%; }
  `],
})
export class PlaceFieldComponent {
  /** Was im Feld steht — Beschriftung eines Lexikon-Treffers, Profilname oder Koordinaten. */
  readonly label = input('');
  readonly suggestions = input<GeoPlaceSuggestion[]>([]);
  readonly searching = input(false);
  readonly noMatch = input(false);
  readonly searchFailed = input(false);
  readonly locating = input(false);
  readonly geolocationSupported = input(false);

  /** Jede Eingabe im Feld. */
  readonly labelChange = output<string>();
  /** Ein Vorschlag wurde gewaehlt. */
  readonly chosen = output<GeoPlaceSuggestion>();
  /** „Leeren" (x). */
  readonly cleared = output<void>();
  /** „Meinen Standort verwenden". */
  readonly locate = output<void>();
}
