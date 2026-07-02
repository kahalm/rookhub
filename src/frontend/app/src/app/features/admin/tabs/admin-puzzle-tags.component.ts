import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { AdminService } from '../../../core/admin.service';

/**
 * Admin-Tab „Puzzles" (Standard): stößt den einmaligen PuzzleTags-Backfill als Hintergrund-Job an.
 * Aus <c>AdminComponent</c> ausgegliedert; self-contained.
 */
@Component({
  selector: 'app-admin-puzzle-tags',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, TranslateModule],
  templateUrl: './admin-puzzle-tags.component.html',
  styles: ['.tab-content { padding: 16px 0; }'],
})
export class AdminPuzzleTagsComponent {
  puzzleTagsBackfilling = false;

  constructor(
    private adminService: AdminService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  backfillPuzzleTags(): void {
    if (this.puzzleTagsBackfilling) return;
    if (!confirm(this.translate.instant('admin.puzzles.backfillConfirm'))) return;
    this.puzzleTagsBackfilling = true;
    this.adminService.backfillPuzzleTags().subscribe({
      next: () => {
        this.puzzleTagsBackfilling = false;
        this.snackbar.info(this.translate.instant('admin.puzzles.backfillStarted'));
      },
      error: err => {
        this.puzzleTagsBackfilling = false;
        this.snackbar.info(err.error?.message || this.translate.instant('admin.puzzles.backfillError'));
      }
    });
  }
}
