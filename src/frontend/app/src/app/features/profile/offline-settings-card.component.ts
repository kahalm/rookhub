import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { OfflineService } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';

/**
 * Karte „Offline-Einstellungen": Pool-Größen (Puzzles/Endlos-Läufe), Cache-Größe + Leeren,
 * ausstehende Offline-Lösungen. Aus <c>ProfileComponent</c> ausgegliedert; self-contained —
 * liest/schreibt direkt über den <see cref="OfflineService"/> (pro Gerät, kein API-Call).
 */
@Component({
  selector: 'app-offline-settings-card',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, TranslatePipe,
  ],
  template: `
    <div class="offline-section">
      <h4>{{ 'profile.offline.title' | translate }}</h4>
      <p class="offline-hint">{{ 'profile.offline.hint' | translate }}</p>
      <div class="offline-fields">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'profile.offline.puzzleCount' | translate }}</mat-label>
          <input matInput type="number" min="0" max="200" [(ngModel)]="offlinePuzzleCount" name="offPuzzles" (change)="saveOffline()">
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'profile.offline.endlessRuns' | translate }}</mat-label>
          <input matInput type="number" min="0" max="50" [(ngModel)]="offlineEndlessRuns" name="offRuns" (change)="saveOffline()">
        </mat-form-field>
      </div>
      <div class="offline-cache">
        <span class="offline-size">{{ 'profile.offline.cacheSize' | translate }}: <strong>{{ offlineSize }}</strong>{{ offlineBooks > 0 ? ' (' + ('profile.offline.books' | translate: { count: offlineBooks }) + ')' : '' }}</span>
        <button mat-stroked-button color="warn" type="button" (click)="clearOfflineCache()">
          <mat-icon>delete_sweep</mat-icon> {{ 'profile.offline.clear' | translate }}
        </button>
      </div>
      @if (offlinePending > 0) {
        <p class="offline-pending">
          <mat-icon>sync</mat-icon> {{ 'profile.offline.pending' | translate: { count: offlinePending } }}
        </p>
      }
    </div>
  `,
  styles: [`
    .offline-section h4 { margin: 0 0 0.25rem; color: #90caf9; }
    .offline-hint { color: #bdbdbd; font-size: 0.85rem; margin: 0 0 0.5rem; }
    .offline-fields { display: flex; gap: 0.75rem; flex-wrap: wrap; }
    .offline-fields mat-form-field { width: 200px; max-width: 100%; }
    .offline-cache { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .offline-size { color: #ccc; font-size: 0.9rem; }
    .offline-pending { display: flex; align-items: center; gap: 6px; color: #ffb74d; font-size: 0.85rem; margin: 6px 0 0; }
    .offline-pending mat-icon { font-size: 18px; width: 18px; height: 18px; }
  `]
})
export class OfflineSettingsCardComponent implements OnInit {
  offlinePuzzleCount = 10;
  offlineEndlessRuns = 2;
  offlineSize = '0 B';
  offlineBooks = 0;
  offlinePending = 0;

  constructor(
    private offline: OfflineService,
    private offlineQueue: OfflineQueueService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.offlinePuzzleCount = this.offline.puzzleCount;
    this.offlineEndlessRuns = this.offline.endlessRuns;
    this.refreshOfflineSize();
  }

  saveOffline(): void {
    this.offline.setPuzzleCount(this.offlinePuzzleCount);
    this.offline.setEndlessRuns(this.offlineEndlessRuns);
    this.offlinePuzzleCount = this.offline.puzzleCount;   // geklemmte Werte zurückspiegeln
    this.offlineEndlessRuns = this.offline.endlessRuns;
  }

  private refreshOfflineSize(): void {
    this.offlineSize = this.offline.formatSize(this.offline.cacheSizeBytes());
    this.offlineBooks = this.offline.cachedBookCount();
    this.offlinePending = this.offlineQueue.pendingCount();
  }

  clearOfflineCache(): void {
    this.offline.clearAll();
    this.refreshOfflineSize();
    this.snackbar.success(this.translate.instant('profile.offline.cleared'));
  }
}
