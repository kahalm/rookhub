import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { AdminService, DailyPuzzleInfo } from '../../../core/admin.service';

/**
 * Admin-Tab „Tagespuzzle": zeigt das Tagespuzzle eines UTC-Datums und erlaubt das Neu-Generieren.
 * Aus <c>AdminComponent</c> ausgegliedert; self-contained (nur AdminService).
 */
@Component({
  selector: 'app-admin-daily-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, TranslateModule, LoadingSpinnerComponent,
  ],
  templateUrl: './admin-daily-puzzle.component.html',
  styleUrl: './admin-daily-puzzle.component.scss',
})
export class AdminDailyPuzzleComponent implements OnInit {
  readonly today = new Date().toISOString().slice(0, 10);
  dailyDate = new Date().toISOString().slice(0, 10);
  dailyPuzzle: DailyPuzzleInfo | null = null;
  dailyLoading = false;
  dailyRegenerating = false;

  constructor(
    private adminService: AdminService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.loadDailyPuzzle();
  }

  /** yyyy-MM-dd → yyyyMMdd für die API-Route. */
  private compactDate(d: string): string {
    return (d || '').replace(/-/g, '');
  }

  loadDailyPuzzle(): void {
    const date = this.compactDate(this.dailyDate);
    if (date.length !== 8) return;
    this.dailyLoading = true;
    this.dailyPuzzle = null;
    this.adminService.getDailyPuzzle(date).subscribe({
      next: p => { this.dailyPuzzle = p; this.dailyLoading = false; },
      error: err => {
        this.dailyLoading = false;
        // 404 = noch kein Tagespuzzle für dieses Datum (z. B. leerer Pool) — kein Fehler-Toast nötig.
        if (err.status !== 404) {
          this.snackbar.info(err.error?.message || this.translate.instant('admin.daily.errors.load'));
        }
      }
    });
  }

  regenerateDailyPuzzle(): void {
    const date = this.compactDate(this.dailyDate);
    if (date.length !== 8) return;
    if (!confirm(this.translate.instant('admin.daily.regenerateConfirm'))) return;

    this.dailyRegenerating = true;
    this.adminService.regenerateDailyPuzzle(date).subscribe({
      next: p => {
        this.dailyPuzzle = p;
        this.dailyRegenerating = false;
        this.snackbar.info(this.translate.instant('admin.daily.regenerated'));
      },
      error: err => {
        this.dailyRegenerating = false;
        this.snackbar.info(err.error?.message || this.translate.instant('admin.daily.errors.regenerate'));
      }
    });
  }
}
