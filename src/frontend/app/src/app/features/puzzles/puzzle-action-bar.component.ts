import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { PuzzleTagsComponent } from './puzzle-tags.component';

/**
 * DIE eine Aktionszeile unter der Status-Card — für alle drei Puzzle-Modi (Standard/Endless/
 * Buch-Kurs-Daily). Ersetzt die früheren Einzel-Karten/-Zeilen (Rating-Card, „Letztes
 * ansehen/lieben", Endlos-Knopf, Bottom-Actions): sichtbar bleiben Rating + Tags-Toggle,
 * kontextuelle Knöpfe des Modus (ng-content, z. B. „An Freund schicken" nach dem Lösen) und
 * Teilen; alles Seltene (Letztes ansehen/lieben, Endlos-Modus, Einstellungen) liegt im ⋮-Menü.
 *
 * UI-Welle 2 (TODO.md „Überladung"): pro Screen eine primäre Aktion, Rest gestuft.
 */
@Component({
  selector: 'app-puzzle-action-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule,
    TranslatePipe, PuzzleTagsComponent,
  ],
  template: `
    <div class="pab-row">
      @if (rating !== null) {
        <span class="pab-rating" [matTooltip]="ratingLabelKey | translate">
          <mat-icon inline>star</mat-icon>{{ rating }}
        </span>
      }
      @if (levelText) { <span class="pab-level">{{ levelText }}</span> }
      <app-puzzle-tags [tags]="tags" />
      <span class="pab-spacer"></span>
      <ng-content />
      <button mat-icon-button class="pab-share" (click)="shareClicked.emit()"
              [matTooltip]="shareLabelKey | translate"
              [attr.aria-label]="shareLabelKey | translate">
        <mat-icon>share</mat-icon>
      </button>
      <button mat-icon-button [matMenuTriggerFor]="moreMenu"
              [matTooltip]="'puzzles.bar.more' | translate"
              [attr.aria-label]="'puzzles.bar.more' | translate">
        <mat-icon>more_vert</mat-icon>
      </button>
      <mat-menu #moreMenu="matMenu">
        @if (hasLast) {
          <button mat-menu-item (click)="reviewLastClicked.emit()">
            <mat-icon>history</mat-icon><span>{{ reviewLastKey | translate }}</span>
          </button>
        }
        @if (hasLast && canLoveLast) {
          <button mat-menu-item class="pab-love" [class.pab-loved]="lastLoved"
                  (click)="loveLastClicked.emit()">
            <mat-icon>{{ lastLoved ? 'favorite' : 'favorite_border' }}</mat-icon>
            <span>{{ 'favorites.loveLast' | translate }}</span>
          </button>
        }
        @if (showEndless) {
          <button mat-menu-item (click)="endlessClicked.emit()">
            <mat-icon>all_inclusive</mat-icon><span>{{ 'puzzles.actions.endlessMode' | translate }}</span>
          </button>
        }
        <button mat-menu-item (click)="settingsClicked.emit()">
          <mat-icon>settings</mat-icon><span>{{ 'puzzles.settings.title' | translate }}</span>
        </button>
      </mat-menu>
    </div>
  `,
  styles: [`
    .pab-row {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 0.25rem 0.5rem;
      padding: 0 0.25rem;
    }
    .pab-rating {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      font-weight: 600;
      white-space: nowrap;
    }
    .pab-level {
      font-size: 0.85em;
      color: color-mix(in srgb, currentColor 60%, transparent);
      white-space: nowrap;
    }
    .pab-spacer { flex: 1; }
    .pab-loved mat-icon { color: #e91e63; }
  `],
})
export class PuzzleActionBarComponent {
  /** Puzzle-Rating als Zahl; null = keine Rating-Pille (Endless zeigt es schon in den Quick-Stats). */
  @Input() rating: number | null = null;
  /** Tooltip-Key der Rating-Pille. */
  @Input() ratingLabelKey = 'puzzles.info.rating';
  /** Optionaler Zusatztext neben dem Rating (z. B. Level-Fenster). */
  @Input() levelText = '';
  /** Space-separierter Themen-String — rendert den vorhandenen Tags-Toggle (leer = nichts). */
  @Input() tags = '';
  /** Gibt es ein „letztes gelöstes Puzzle" (⋮: ansehen/lieben)? */
  @Input() hasLast = false;
  /** Darf der User das letzte Puzzle lieben (eingeloggt, Modus erlaubt Favoriten)? */
  @Input() canLoveLast = false;
  @Input() lastLoved = false;
  /** ⋮-Eintrag „Endlos-Modus" (nur Standard-Puzzle). */
  @Input() showEndless = false;
  @Input() shareLabelKey = 'puzzles.actions.share';
  @Input() reviewLastKey = 'puzzles.actions.reviewLast';

  @Output() shareClicked = new EventEmitter<void>();
  @Output() settingsClicked = new EventEmitter<void>();
  @Output() reviewLastClicked = new EventEmitter<void>();
  @Output() loveLastClicked = new EventEmitter<void>();
  @Output() endlessClicked = new EventEmitter<void>();
}
