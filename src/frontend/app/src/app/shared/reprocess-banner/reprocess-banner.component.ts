import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';

interface ReprocessStatus {
  currentVersion: number; total: number; stale: number;
  reprocessableLocally: number; refetchable: number; needsReimport: number;
}
interface ReprocessResult { reprocessed: number; updatedLines: number; enqueued: number; skipped: number; }

/**
 * Wiederverwendbares Banner „N … können aktualisiert werden", wenn die Import-/Aufbereitungs-Pipeline
 * weiterentwickelt wurde (z. B. um nachträglich Zug-Kommentare zu extrahieren). Zeigt sich nur, wenn
 * der Server veraltete Datensätze meldet. Ein Klick stößt das Neu-Aufbereiten an; nach Abschluss wird
 * `done` emittiert, damit die Eltern-Liste neu laden kann. Arbeitet generisch gegen
 * `/api/{section}/reprocess[/status]` (section = "courses" | "repertoires").
 */
@Component({
  selector: 'app-reprocess-banner',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslateModule],
  template: `
    @if (status && status.stale > 0) {
      <div class="reprocess-banner">
        <mat-icon class="rb-icon">auto_fix_high</mat-icon>
        <span class="rb-text">{{ ('reprocess.available.' + section) | translate: { count: status.stale } }}</span>
        <button mat-stroked-button (click)="run()" [disabled]="working">
          @if (working) {
            <mat-spinner diameter="16" class="rb-spin"></mat-spinner> {{ 'reprocess.working' | translate }}
          } @else {
            <mat-icon>refresh</mat-icon> {{ 'reprocess.update' | translate }}
          }
        </button>
      </div>
    }
  `,
  styles: [`
    .reprocess-banner {
      display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
      margin: 0 0 1.25rem; padding: 10px 14px; border-radius: 8px; font-size: 0.9rem;
      background: color-mix(in srgb, #ffb300 16%, transparent);
      border: 1px solid color-mix(in srgb, #ffb300 35%, transparent);
    }
    .rb-icon { flex: 0 0 auto; color: #ef6c00; }
    .rb-text { flex: 1 1 220px; }
    .rb-spin { display: inline-block; vertical-align: middle; margin-right: 6px; }
  `]
})
export class ReprocessBannerComponent implements OnInit {
  /** API-/i18n-Sektion: "courses" oder "repertoires". */
  @Input({ required: true }) section!: 'courses' | 'repertoires';
  /** Wird nach erfolgreichem Reprocess emittiert (Eltern lädt die Liste neu). */
  @Output() done = new EventEmitter<void>();

  status: ReprocessStatus | null = null;
  working = false;

  constructor(private http: HttpClient, private snackbar: SnackbarService, private translate: TranslateService) {}

  ngOnInit(): void { this.refresh(); }

  private refresh(): void {
    this.http.get<ReprocessStatus>(`/api/${this.section}/reprocess/status`).subscribe({
      next: s => this.status = s,
      error: () => this.status = null,   // still: kein Banner bei Fehler
    });
  }

  run(): void {
    if (this.working) return;
    this.working = true;
    this.http.post<ReprocessResult>(`/api/${this.section}/reprocess`, {}).subscribe({
      next: res => {
        this.working = false;
        const parts: string[] = [];
        if (res.reprocessed > 0) parts.push(this.translate.instant('reprocess.doneLocal', { count: res.reprocessed }));
        if (res.enqueued > 0) parts.push(this.translate.instant('reprocess.doneQueued', { count: res.enqueued }));
        this.snackbar.info(parts.length ? parts.join(' ') : this.translate.instant('reprocess.doneEmpty'));
        this.refresh();
        this.done.emit();
      },
      error: () => { this.working = false; this.snackbar.info(this.translate.instant('reprocess.failed')); },
    });
  }
}
