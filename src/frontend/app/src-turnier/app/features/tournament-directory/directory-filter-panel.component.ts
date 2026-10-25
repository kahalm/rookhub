import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import {
  DIRECTORY_AGE_GROUPS, DIRECTORY_GENDERS, DIRECTORY_KINDS, DIRECTORY_RADII, DIRECTORY_RANGE_PRESETS,
  DirectoryFilter, DirectoryRangePreset, TournamentAgeGroup, TournamentGender, TournamentKind,
  TournamentSpeed,
} from './tournament-directory.model';

/**
 * Das ausgeklappte Filterfeld des Turnierkalenders: Zeitraum/Umkreis (Handy bzw. eigener
 * Zeitraum), Textsuche, Wer (Art/Alter/Geschlecht), Schalter, Bedenkzeit und der Rest.
 *
 * <p>Reine Darstellung zum Filterzustand der Komponente (Zerlegung Z28): jeder Wert kommt einzeln
 * herein, jede Aenderung geht als Ereignis hinaus — die, die der Elternteil nachbearbeitet, je
 * eigen, die frueher direkt geschriebenen gesammelt als `patch`.</p>
 */
@Component({
  selector: 'app-directory-filter-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatSelectModule, MatSlideToggleModule, MatTooltipModule, HelpHintComponent, TranslatePipe,
  ],
  templateUrl: './directory-filter-panel.component.html',
  styleUrls: ['./directory-filter-panel.component.scss'],
})
export class DirectoryFilterPanelComponent {
  // Die Filterfelder einzeln: der Elternteil aendert sein Filterobjekt IN PLACE (gemerkter Zustand,
  // Suchprofil) — ein Objekt-Eingang saehe das unter OnPush nicht, ein Wert je Feld schon.
  readonly radiusKm = input<number | null>(null);
  readonly from = input<string | null>(null);
  readonly to = input<string | null>(null);
  readonly kinds = input<TournamentKind[]>([]);
  readonly ageGroups = input<TournamentAgeGroup[]>([]);
  readonly genders = input<TournamentGender[]>([]);
  readonly adultsOnly = input(false);
  readonly hideLeagues = input(false);
  readonly includeIgnored = input(false);
  readonly includeImplausible = input(false);
  readonly speed = input<TournamentSpeed | null>(null);
  readonly weekendOnly = input(false);
  readonly minPlayers = input<number | null>(null);
  readonly rangePreset = input.required<DirectoryRangePreset>();
  /** Der Text der Suche (`TournamentDirectoryComponent.searchText`). */
  readonly searchText = input('');
  /** Ausgeblendet hat nur ein Konto etwas. */
  readonly loggedIn = input(false);

  readonly rangePresetChange = output<DirectoryRangePreset>();
  /** 0 = „Überall" (siehe `onRadiusChange`). */
  readonly radiusChange = output<number | null>();
  readonly searchTextChange = output<string>();
  readonly kindsChange = output<TournamentKind[] | null>();
  readonly ageGroupsChange = output<TournamentAgeGroup[] | null>();
  readonly gendersChange = output<TournamentGender[] | null>();
  readonly adultsOnlyChange = output<boolean>();
  readonly includeIgnoredChange = output<boolean>();
  readonly includeImplausibleChange = output<boolean>();
  readonly speedChange = output<TournamentSpeed | null>();
  /**
   * Felder, die schon vor der Zerlegung per `[(ngModel)]` direkt in den Filter geschrieben wurden
   * (Datum, Ligen, Wochenende, Mindestzahl) — der Elternteil legt sie unveraendert hinein.
   */
  readonly patch = output<Partial<DirectoryFilter>>();
  /** Ein direkt geschriebenes Feld ist fertig (`change`) bzw. Enter in der Suche: neu laden. */
  readonly reload = output<void>();
  readonly reset = output<void>();

  readonly speedOptions: TournamentSpeed[] = ['Standard', 'Rapid', 'Blitz'];
  readonly rangePresets = DIRECTORY_RANGE_PRESETS;
  readonly radii = DIRECTORY_RADII;
  readonly ageGroupOptions = DIRECTORY_AGE_GROUPS;
  readonly genderOptions = DIRECTORY_GENDERS;
  readonly kindOptions = DIRECTORY_KINDS;
}
