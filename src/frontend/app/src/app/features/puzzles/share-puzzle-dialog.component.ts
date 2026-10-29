import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { QrCodeComponent } from '../../shared/qr-code/qr-code.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { PuzzleChallengeSource } from '../../core/challenge.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-share-puzzle-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, TranslatePipe, QrCodeComponent, ChallengeFriendsComponent, IconLabelDirective],
  template: `
    <!-- d7-rk-dialog-titel-uneinheitlich: Standard-Dialograster (Titel/Inhalt/Knopfzeile) wie alle anderen Dialoge. -->
    <h2 mat-dialog-title>{{ 'puzzles.share.title' | translate }}</h2>
    <mat-dialog-content>
      <div class="which-label" *ngIf="data.previousUrl">
        {{ (showingPrevious ? 'puzzles.share.previous' : 'puzzles.share.current') | translate }}
      </div>
      <div class="qr-container">
        <app-qr-code [data]="activeUrl" [width]="220" />
      </div>
      <div class="link-row">
        <!-- d8-rk-share-url-feld-abgeschnitten: der Dialog fokussiert „Kopieren“ statt des Felds — ein fokussiertes Feld
             scrollte ans Textende, „https://ro…“ war weg. Ein Klick ins Feld markiert die ganze Adresse. -->
        <input class="link-input" [value]="activeUrl" readonly #linkInput (focus)="linkInput.select()" />
        <button mat-icon-button cdkFocusInitial (click)="copyLink()" [appIconLabel]="'common.copy' | translate">
          <mat-icon>content_copy</mat-icon>
        </button>
      </div>
      @if (data.canChallenge && activePuzzleId) {
        <div class="challenge-row">
          <app-challenge-friends [puzzleId]="activePuzzleId" [source]="data.source ?? 'standard'" />
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions>
      <button mat-stroked-button *ngIf="data.previousUrl" (click)="toggle()">
        {{ (showingPrevious ? 'puzzles.share.current' : 'puzzles.share.previous') | translate }}
      </button>
      <span class="spacer"></span>
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .which-label { text-align: center; margin-bottom: 0.5rem; font-size: 0.9rem; opacity: 0.7; }
    .qr-container { display: flex; justify-content: center; margin-bottom: 1rem; }
    .link-row { display: flex; align-items: center; gap: 0.5rem; }
    .challenge-row { display: flex; justify-content: center; margin-top: 0.85rem; }
    .link-input {
      flex: 1; min-width: 0; padding: 0.5rem; border: 1px solid color-mix(in srgb, currentColor 20%, transparent); border-radius: 4px;
      font-size: 0.85rem; background: var(--mat-sys-surface-container); color: inherit; text-overflow: ellipsis;
    }
    .spacer { flex: 1; }
  `]
})
export class SharePuzzleDialogComponent {
  showingPrevious = false;

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: {
      url: string;
      previousUrl?: string;
      /** Puzzle-IDs + Quelle für „An Freund schicken" direkt aus dem Dialog (optional). */
      puzzleId?: number;
      previousPuzzleId?: number;
      source?: PuzzleChallengeSource;
      /** Nur eingeloggt anzeigen (Senden braucht Auth). */
      canChallenge?: boolean;
    },
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  get activeUrl(): string {
    return this.showingPrevious && this.data.previousUrl ? this.data.previousUrl : this.data.url;
  }

  /** Puzzle-ID passend zur aktuell angezeigten Seite (aktuell vs. vorher). */
  get activePuzzleId(): number | undefined {
    return this.showingPrevious && this.data.previousPuzzleId ? this.data.previousPuzzleId : this.data.puzzleId;
  }

  toggle(): void {
    this.showingPrevious = !this.showingPrevious;
  }

  copyLink(): void {
    if (navigator.clipboard?.writeText) {
      navigator.clipboard.writeText(this.activeUrl).then(() => {
        this.snackbar.copy(this.translate.instant('puzzles.share.copied'));
      }).catch(() => this.fallbackCopy());
    } else {
      this.fallbackCopy();
    }
  }

  private fallbackCopy(): void {
    const textarea = document.createElement('textarea');
    textarea.value = this.activeUrl;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    try {
      document.execCommand('copy');
      this.snackbar.copy(this.translate.instant('puzzles.share.copied'));
    } catch {
      this.snackbar.copy(this.translate.instant('puzzles.share.copyFailed'));
    }
    document.body.removeChild(textarea);
  }
}
