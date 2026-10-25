import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { SimilarPositionMatch, SimilarityPreset } from '../../core/repertoire.service';
import { SimilarPositionsComponent, SimilarRepertoireOption } from './similar-positions.component';

/**
 * Die Sicht „Ähnliche Stellungen" im Stellungs-Panel (`app-position-repertoires`, Zerlegung Z32): Hinweis bei
 * abgelaufenem Zeitbudget und darunter Filter + Treffer (`app-similar-positions`). Reine Darstellung — den
 * Zustand hält `SimilarPositionsSearch` im Panel (er überlebt Zuklappen und Sichtwechsel), die Anfrage führt das
 * Panel für alle Sichten gemeinsam; jede Bedienung geht als Ausgabe zurück.
 */
@Component({
  selector: 'app-similar-positions-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, SimilarPositionsComponent],
  template: `
    @if (truncated()) {
      <div class="pr-muted pr-note">{{ 'positionInReps.truncated' | translate }}</div>
    }
    <app-similar-positions
      [matches]="matches()"
      [options]="options()"
      [selected]="selected()"
      [preset]="preset()"
      [includeMirrored]="includeMirrored()"
      [sameSideToMove]="sameSideToMove()"
      [moveText]="moveText()"
      [moveSan]="moveSan()"
      [moveInvalid]="moveInvalid()"
      [onlyWithMove]="onlyWithMove()"
      (presetChange)="presetChange.emit($event)"
      (mirroredChange)="mirroredChange.emit($event)"
      (sameSideToMoveChange)="sameSideToMoveChange.emit($event)"
      (moveTextChange)="moveTextChange.emit($event)"
      (onlyWithMoveChange)="onlyWithMoveChange.emit($event)"
      (selectionChange)="selectionChange.emit($event)"
      (open)="openMatch.emit($event)" />
  `,
  styles: [`
    :host { display: block; }
    .pr-muted { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; display: flex; align-items: center; gap: 8px; }
    .pr-note { font-size: .8rem; margin-bottom: 6px; }
  `],
})
export class SimilarPositionsPanelComponent {
  readonly matches = input<SimilarPositionMatch[]>([]);
  readonly options = input<SimilarRepertoireOption[]>([]);
  readonly selected = input<ReadonlySet<number>>(new Set<number>());
  readonly preset = input<SimilarityPreset>('ausgewogen');
  readonly includeMirrored = input(true);
  readonly sameSideToMove = input(false);
  readonly moveText = input('');
  readonly moveSan = input('');
  readonly moveInvalid = input(false);
  readonly onlyWithMove = input(false);
  /** Der Server hat am Zeitbudget aufgehört — das Ergebnis ist unvollständig. */
  readonly truncated = input(false);

  readonly presetChange = output<SimilarityPreset>();
  readonly mirroredChange = output<boolean>();
  readonly sameSideToMoveChange = output<boolean>();
  readonly moveTextChange = output<string>();
  readonly onlyWithMoveChange = output<boolean>();
  readonly selectionChange = output<number[]>();
  /** Ein Treffer soll geöffnet werden (wie „Ansehen" in der Listen-Sicht). */
  readonly openMatch = output<SimilarPositionMatch>();
}
