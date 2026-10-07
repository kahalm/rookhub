import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { LoadErrorComponent } from '../../../shared/load-error/load-error.component';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';

/** Ein LeagueHub-Stapel-Upload (`GET /api/admin/league-uploads`). */
export interface LeagueUploadBatch {
  id: number; createdAt: string; finishedAt: string | null; user: string | null; viaShare: boolean; files: number; bytes: number;
  comment: string | null;
  /** Der Verein, für den hochgeladen wurde (LeagueHub mit mehreren Vereinen, 0.698.0). */
  clubId?: number; club?: string | null;
}

/**
 * Admin-Tab „Uploads" (0.651.0): Formular-Bilder, die über den LeagueHub-Stapel-Upload nur abgelegt wurden — als ZIP
 * holen (über HttpClient, damit die Anmeldung mitgeht) und danach löschen. Dorthin führen die Admin-Nachricht und die Glocke.
 */
@Component({
  selector: 'app-admin-league-uploads',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButtonModule, MatIconModule, TranslatePipe, LoadErrorComponent],
  template: `
    <div class="uploads">
      <p class="intro">{{ 'admin.uploads.intro' | translate }}</p>
      @if (error()) {
        <app-load-error (retry)="load()" />
      } @else if (rows(); as list) {
        @if (!list.length) {
          <p class="empty">{{ 'admin.uploads.empty' | translate }}</p>
        } @else {
          <table class="tbl">
            <thead><tr>
              <th>#</th><th>{{ 'admin.uploads.when' | translate }}</th><th>{{ 'admin.uploads.who' | translate }}</th>
              <th class="num">{{ 'admin.uploads.files' | translate }}</th><th class="num">{{ 'admin.uploads.size' | translate }}</th>
              <th>{{ 'admin.uploads.comment' | translate }}</th><th></th>
            </tr></thead>
            <tbody>
              @for (b of list; track b.id) {
                <tr>
                  <td>{{ b.id }}</td>
                  <td>{{ b.createdAt | date: 'dd.MM.yyyy HH:mm' }}@if (!b.finishedAt) { <span class="muted"> · {{ 'admin.uploads.open' | translate }}</span> }</td>
                  <td>{{ b.viaShare ? ('admin.uploads.viaShare' | translate) : b.user }}@if (b.club) { <span class="muted club"> · {{ b.club }}</span> }</td>
                  <td class="num">{{ b.files }}</td>
                  <td class="num">{{ mb(b.bytes) }}</td>
                  <td class="comment">{{ b.comment }}</td>
                  <td class="acts">
                    <button mat-stroked-button type="button" [disabled]="busy() === b.id || !b.files" (click)="download(b)">
                      <mat-icon>download</mat-icon> {{ 'admin.uploads.download' | translate }}</button>
                    <button mat-button type="button" color="warn" [disabled]="busy() === b.id" (click)="remove(b)">
                      {{ 'common.delete' | translate }}</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      }
    </div>
  `,
  styles: [`
    .uploads { padding: 16px 0; }
    .intro, .empty { color: var(--mat-sys-on-surface-variant, #666); }
    .tbl { width: 100%; border-collapse: collapse; }
    .tbl th, .tbl td { text-align: left; padding: 6px 8px; border-bottom: 1px solid rgba(128,128,128,.25); vertical-align: middle; }
    .tbl .num { text-align: right; white-space: nowrap; }
    .comment { max-width: 32ch; overflow-wrap: anywhere; }
    .acts { white-space: nowrap; }
    .muted { opacity: .7; }
  `],
})
export class AdminLeagueUploadsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly translate = inject(TranslateService);
  private readonly snack = inject(SnackbarService);
  private readonly confirm = inject(ConfirmService);

  readonly rows = signal<LeagueUploadBatch[] | null>(null);
  readonly error = signal(false);
  readonly busy = signal<number | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(false);
    try {
      this.rows.set(await firstValueFrom(this.http.get<LeagueUploadBatch[]>('/api/admin/league-uploads')));
    } catch {
      this.error.set(true);
    }
  }

  mb(bytes: number): string {
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  async download(b: LeagueUploadBatch): Promise<void> {
    this.busy.set(b.id);
    try {
      const blob = await firstValueFrom(this.http.get(`/api/admin/league-uploads/${b.id}/zip`, { responseType: 'blob' }));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `leaguehub-stapel-${b.id}.zip`;
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 10_000);
    } catch {
      this.snack.warn(this.translate.instant('admin.uploads.downloadError'));
    } finally {
      this.busy.set(null);
    }
  }

  async remove(b: LeagueUploadBatch): Promise<void> {
    if (!await firstValueFrom(this.confirm.ask('admin.uploads.deleteConfirm', { id: b.id, count: b.files }))) return;
    this.busy.set(b.id);
    try {
      await firstValueFrom(this.http.delete(`/api/admin/league-uploads/${b.id}`));
      this.rows.update(r => (r ?? []).filter(x => x.id !== b.id));
      this.snack.success(this.translate.instant('admin.uploads.deleted'));
    } catch {
      this.snack.warn(this.translate.instant('admin.uploads.downloadError'));
    } finally {
      this.busy.set(null);
    }
  }
}
