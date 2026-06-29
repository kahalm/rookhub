import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { QRCodeComponent } from 'angularx-qrcode';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { PuzzleChallengeSource } from '../../core/challenge.service';

@Component({
  selector: 'app-share-puzzle-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, MatCheckboxModule, TranslateModule, QRCodeComponent, ChallengeFriendsComponent],
  template: `
    <h2 class="dialog-title">{{ 'puzzles.share.title' | translate }}</h2>
    <div class="which-label" *ngIf="data.previousUrl">
      {{ (showingPrevious ? 'puzzles.share.previous' : 'puzzles.share.current') | translate }}
    </div>
    <div class="qr-container">
      <qrcode [qrdata]="activeUrl" [width]="220" errorCorrectionLevel="M"></qrcode>
    </div>
    <div class="link-row">
      <input class="link-input" [value]="activeUrl" readonly #linkInput />
      <button mat-icon-button (click)="copyLink()">
        <mat-icon>content_copy</mat-icon>
      </button>
    </div>
    @if (canTrack) {
      <div class="track-row">
        <mat-checkbox [checked]="trackSolves" (change)="setTrack($event.checked)">
          {{ 'puzzles.share.trackSolves' | translate }}
        </mat-checkbox>
        <div class="track-hint">{{ 'puzzles.share.trackSolvesHint' | translate }}</div>
      </div>
    }
    @if (data.canChallenge && activePuzzleId) {
      <div class="challenge-row">
        <app-challenge-friends [puzzleId]="activePuzzleId" [source]="data.source ?? 'standard'" />
      </div>
    }
    <div class="dialog-actions">
      <button mat-stroked-button *ngIf="data.previousUrl" (click)="toggle()">
        {{ (showingPrevious ? 'puzzles.share.current' : 'puzzles.share.previous') | translate }}
      </button>
      <span class="spacer"></span>
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </div>
  `,
  styles: [`
    :host { display: block; padding: 1.25rem; }
    .dialog-title { margin: 0 0 1rem; font-size: 1.2rem; }
    .which-label { text-align: center; margin-bottom: 0.5rem; font-size: 0.9rem; opacity: 0.7; }
    .qr-container { display: flex; justify-content: center; margin-bottom: 1rem; }
    .link-row { display: flex; align-items: center; gap: 0.5rem; }
    .track-row { margin-top: 0.75rem; }
    .track-hint { font-size: 0.75rem; opacity: 0.6; margin: 0.1rem 0 0 2rem; }
    .challenge-row { display: flex; justify-content: center; margin-top: 0.85rem; }
    .link-input {
      flex: 1; padding: 0.5rem; border: 1px solid color-mix(in srgb, currentColor 20%, transparent); border-radius: 4px;
      font-size: 0.85rem; background: var(--mat-sys-surface-container, #f5f5f5); color: inherit;
    }
    .dialog-actions { display: flex; align-items: center; margin-top: 1rem; }
    .dialog-actions .spacer { flex: 1; }
  `]
})
export class SharePuzzleDialogComponent {
  showingPrevious = false;
  /** „Track solves": geteilter Link zählt Erstversuche der Besucher (solved/failed) und zeigt sie an. */
  trackSolves = false;

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

  /** „Track solves" nur für geteilte Buch-Einzel-Puzzles (Link trägt bereits `?single=1`). */
  get canTrack(): boolean {
    return this.data.source === 'book' && /[?&]single=1\b/.test(this.data.url);
  }

  get activeUrl(): string {
    const base = this.showingPrevious && this.data.previousUrl ? this.data.previousUrl : this.data.url;
    return this.trackSolves && this.canTrack ? `${base}&track=1` : base;
  }

  setTrack(checked: boolean): void {
    this.trackSolves = checked;
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
