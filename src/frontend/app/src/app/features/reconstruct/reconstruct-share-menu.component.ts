import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';

/**
 * Das ⋮ im Kopf der Rekonstruktion: Anfang kopieren, ganze Partie teilen, Teilen beenden
 * (Zerlegung Z25, aus `ReconstructDetailComponent`). Geteilt wird in der Seite — hier nur das Menü.
 */
@Component({
  selector: 'app-reconstruct-share-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslatePipe, IconLabelDirective],
  template: `
    <!-- Kopieren/Teilen im ⋮ (d2-rk-recon-detail-actions, DESIGN 5.5): als Text-Knöpfe in der Chip-Zeile standen sie am
         Handy je in einer eigenen Zeile vor dem ersten Teil. -->
    <button mat-icon-button class="head-menu" [matMenuTriggerFor]="headMenu"
            [appIconLabel]="'common.moreActions' | translate">
      <mat-icon>more_vert</mat-icon>
    </button>
    <mat-menu #headMenu="matMenu">
      @if (hasPrefix()) {
        <button mat-menu-item (click)="copyPrefix.emit()">
          <mat-icon>content_copy</mat-icon><span>{{ 'reconstruct.copyPrefix' | translate }}</span>
        </button>
      }
      <!-- Die ganze Partie weitergeben: ein Link auf DIESE Rekonstruktion, Lücken inbegriffen. -->
      <button mat-menu-item class="share-item" [disabled]="busy()" (click)="share.emit()"
              [matTooltip]="shared() ? '' : ('reconstruct.share.hint' | translate)" matTooltipPosition="left">
        <mat-icon>share</mat-icon>
        <span>{{ (shared() ? 'reconstruct.share.copyAgain' : 'reconstruct.share.action') | translate }}</span>
      </button>
      @if (shared()) {
        <button mat-menu-item class="stop-share-item" [disabled]="busy()" (click)="stopSharing.emit()">
          <mat-icon>link_off</mat-icon><span>{{ 'reconstruct.share.stop' | translate }}</span>
        </button>
      }
    </mat-menu>
  `,
  styles: [`
    /* Kein eigener Kasten: der Knopf bleibt das Flex-Kind der Kopfzeile wie vor der Zerlegung. */
    :host { display: contents; }
  `],
})
export class ReconstructShareMenuComponent {
  /** Gibt es einen verankerten Anfang (SAN) zum Kopieren? */
  readonly hasPrefix = input(false);
  /** Ist der öffentliche Link schon eingeschaltet? */
  readonly shared = input(false);
  readonly busy = input(false);

  readonly copyPrefix = output<void>();
  readonly share = output<void>();
  readonly stopSharing = output<void>();
}
