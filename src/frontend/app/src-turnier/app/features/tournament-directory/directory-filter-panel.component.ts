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
 * <p>Darstellung zum Filterzustand der Komponente (Zerlegung Z28). Der FILTER bleibt dort ein
 * einfaches, veraenderliches Objekt (siehe `TournamentDirectoryComponent`): die Felder, die schon
 * vorher per `[(ngModel)]` direkt geschrieben wurden (Datum, Ligen, Wochenende, Mindestzahl), schreibt
 * dieses Feld weiter direkt hinein; alles, was der Elternteil nachbearbeitet, geht als Ereignis
 * hinaus. Deshalb Default-Aenderungserkennung wie der Elternteil: er aendert das Objekt auch in
 * place (gemerkter Zustand, Suchprofil), und eine OnPush-Ansicht zeigte dann einen alten Stand.</p>
 */
@Component({
  selector: 'app-directory-filter-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [
    FormsModule, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatSelectModule, MatSlideToggleModule, MatTooltipModule, HelpHintComponent, TranslatePipe,
  ],
  templateUrl: './directory-filter-panel.component.html',
  styleUrls: ['./directory-filter-panel.component.scss'],
})
export class DirectoryFilterPanelComponent {
  readonly filter = input.required<DirectoryFilter>();
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
  /** Ein direkt geschriebenes Feld hat sich geaendert (bzw. Enter in der Suche): neu laden. */
  readonly reload = output<void>();
  readonly reset = output<void>();

  readonly speeds: TournamentSpeed[] = ['Standard', 'Rapid', 'Blitz'];
  readonly rangePresets = DIRECTORY_RANGE_PRESETS;
  readonly radii = DIRECTORY_RADII;
  readonly ageGroups = DIRECTORY_AGE_GROUPS;
  readonly genders = DIRECTORY_GENDERS;
  readonly kinds = DIRECTORY_KINDS;
}
