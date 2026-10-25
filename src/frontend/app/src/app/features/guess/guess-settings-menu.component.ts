import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe } from '@ngx-translate/core';
import { GuessAccept } from './guess.service';

/**
 * Zahnrad + Menü der Punktepartie: was als „erledigt" gilt (Zerlegung Z24, aus
 * `GuessBoardComponent`). Die Häkchen ändern das übergebene `accept`-Objekt der Seite direkt
 * (wie vorher per `ngModel`); `changed` sagt der Seite, dass sie es speichern soll.
 */
@Component({
  selector: 'app-guess-settings-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <button mat-icon-button [matMenuTriggerFor]="settings"
            [attr.title]="'guess.settings.title' | translate">
      <mat-icon>settings</mat-icon>
    </button>
    <mat-menu #settings="matMenu" class="guess-settings">
      <div class="set-box" (click)="$event.stopPropagation()">
        <p class="set-head">{{ 'guess.settings.title' | translate }}</p>
        <mat-checkbox [(ngModel)]="accept().better" (change)="changed.emit()">
          {{ 'guess.settings.acceptBetter' | translate }}
        </mat-checkbox>
        <mat-checkbox [(ngModel)]="accept().similar" (change)="changed.emit()">
          {{ 'guess.settings.acceptSimilar' | translate }}
        </mat-checkbox>
        <p class="set-hint muted small">{{ 'guess.settings.hint' | translate }}</p>
      </div>
    </mat-menu>
  `,
  styles: [`
    /* Kein eigener Kasten: der Knopf bleibt das Flex-Kind der Kopfzeile wie vor der Zerlegung. */
    :host { display: contents; }
    .set-box { padding: 10px 14px; display: flex; flex-direction: column; gap: 8px; max-width: 300px; }
    .set-head { margin: 0; font-weight: 600; }
    .set-hint { margin: 2px 0 0; line-height: 1.4; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class GuessSettingsMenuComponent {
  readonly accept = input.required<GuessAccept>();
  readonly changed = output<void>();
}
