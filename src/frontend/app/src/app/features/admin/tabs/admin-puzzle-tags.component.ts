import { Component, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { AdminService } from '../../../core/admin.service';
import { apiErrorText } from '../../../core/api-error';

/**
 * Admin-Tab „Puzzles" (Standard): stößt den einmaligen PuzzleTags-Backfill als Hintergrund-Job an.
 * Aus <c>AdminComponent</c> ausgegliedert; self-contained.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-admin-puzzle-tags',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, TranslatePipe],
  templateUrl: './admin-puzzle-tags.component.html',
  styles: ['.tab-content { padding: 16px 0; }'],
})
export class AdminPuzzleTagsComponent {
  puzzleTagsBackfilling = false;
  /** Admin-Seite OnPush + Angular 22: nach Dialog/HTTP selbst markieren, sonst friert der Knopf ein. */
  private readonly cdr = inject(ChangeDetectorRef);

  constructor(
    private adminService: AdminService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private confirm: ConfirmService,
  ) {}

  backfillPuzzleTags(): void {
    if (this.puzzleTagsBackfilling) return;
    this.confirm.ask('admin.puzzles.backfillConfirm').subscribe(ok => {
      if (!ok || this.puzzleTagsBackfilling) return;
      this.puzzleTagsBackfilling = true;
      this.cdr.markForCheck();
      this.adminService.backfillPuzzleTags().subscribe({
        next: () => {
          this.puzzleTagsBackfilling = false;
          this.cdr.markForCheck();
          this.snackbar.info(this.translate.instant('admin.puzzles.backfillStarted'));
        },
        error: err => {
          this.puzzleTagsBackfilling = false;
          this.cdr.markForCheck();
          this.snackbar.info(apiErrorText(err, this.translate, 'admin.puzzles.backfillError'));
        }
      });
    });
  }
}
