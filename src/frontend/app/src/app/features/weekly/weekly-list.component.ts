import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { RouterModule } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { WeeklyService, WeeklyPost, WeeklyProgress, WeeklyPlayerResult, sortLeaderboard, nextWeeklySlot, weeklyDatePart, weeklyTimePart } from './weekly.service';
import { WeeklyBreakdownDialogComponent } from './weekly-breakdown-dialog.component';
import { WeeklyFromChapterDialogComponent } from './weekly-from-chapter-dialog.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

interface WeeklyPostRow extends WeeklyPost {
  editDate: string;   // YYYY-MM-DD (Admin-Edit)
  editTime: string;   // HH:mm
}

@Component({
  selector: 'app-weekly-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatDialogModule,
    TranslateModule, LoadingSpinnerComponent
  ],
  template: `
    <div class="weekly-container">
      <h1>{{ 'weekly.title' | translate }}</h1>
      <p class="intro">{{ 'weekly.intro' | translate }}</p>

      @if (auth.isAdmin) {
        <mat-card class="upload-card">
          <mat-card-header><mat-card-title>{{ 'weekly.upload.title' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            <div class="upload-row">
              <input #pgnInput type="file" accept=".pgn" hidden (change)="onFileSelected($event)">
              <button mat-stroked-button (click)="pgnInput.click()">
                <mat-icon>upload_file</mat-icon> {{ uploadFileName || ('weekly.upload.choosePgn' | translate) }}
              </button>
              <mat-form-field appearance="outline" class="f-date">
                <mat-label>{{ 'weekly.fields.date' | translate }}</mat-label>
                <input matInput type="date" [(ngModel)]="uploadDate">
              </mat-form-field>
              <mat-form-field appearance="outline" class="f-time">
                <mat-label>{{ 'weekly.fields.time' | translate }}</mat-label>
                <input matInput type="time" [(ngModel)]="uploadTime">
              </mat-form-field>
              <mat-form-field appearance="outline" class="f-title">
                <mat-label>{{ 'weekly.fields.titleOptional' | translate }}</mat-label>
                <input matInput [(ngModel)]="uploadTitle" [placeholder]="'weekly.upload.titlePlaceholder' | translate">
              </mat-form-field>
              <mat-form-field appearance="outline" class="f-desc">
                <mat-label>{{ 'weekly.fields.descriptionOptional' | translate }}</mat-label>
                <input matInput [(ngModel)]="uploadDescription" maxlength="500"
                       [placeholder]="'weekly.upload.descriptionPlaceholder' | translate">
              </mat-form-field>
              <button mat-raised-button color="primary"
                      [disabled]="!uploadFile || !uploadDate || !uploadTime || uploading" (click)="upload()">
                <mat-icon>add</mat-icon> {{ 'weekly.upload.create' | translate }}
              </button>
            </div>
            <p class="upload-hint">{{ 'weekly.upload.hint' | translate }}</p>
            <div class="or-chapter">
              <span class="or-sep">{{ 'weekly.fromChapter.or' | translate }}</span>
              <button mat-stroked-button (click)="openFromChapter()">
                <mat-icon>menu_book</mat-icon> {{ 'weekly.fromChapter.button' | translate }}
              </button>
            </div>
          </mat-card-content>
        </mat-card>
      }

      @if (loading) {
        <app-loading-spinner />
      } @else if (rows.length === 0) {
        <p class="empty-hint">{{ 'weekly.empty' | translate }}</p>
      } @else {
        <div class="wp-list">
          @for (r of rows; track r.id) {
            <mat-card class="wp-card">
              <div class="wp-row">
                <div class="wp-meta">
                  @if (auth.isAdmin) {
                    <input class="inline-title" [(ngModel)]="r.title" (change)="savePost(r)"
                           [placeholder]="'weekly.columns.title' | translate">
                    <input class="inline-desc" [(ngModel)]="r.description" (change)="savePost(r)" maxlength="500"
                           [placeholder]="'weekly.fields.descriptionOptional' | translate">
                    <div class="wp-edit-sched">
                      <input type="date" class="inline-date" [(ngModel)]="r.editDate" (change)="savePost(r)">
                      <input type="time" class="inline-time" [(ngModel)]="r.editTime" (change)="savePost(r)">
                    </div>
                  } @else {
                    @if (r.title) { <span class="wp-title">{{ r.title }}</span> }
                    @if (r.description) { <span class="wp-desc">{{ r.description }}</span> }
                    <span class="wp-sched">
                      {{ r.scheduledAt | date:'EEEE, dd.MM.yyyy' }} · {{ r.scheduledAt | date:'HH:mm' }} {{ 'weekly.oClock' | translate }}
                    </span>
                  }
                  @if (prog[r.id]; as p) {
                    <span class="wp-prog">
                      <span class="wp-solved" [attr.title]="'weekly.progress.solvedLabel' | translate">✓ {{ p.solvedCount }}</span>
                      <span class="wp-slash">/</span>
                      <span class="wp-failed" [attr.title]="'weekly.progress.failedLabel' | translate">✗ {{ p.playedCount - p.solvedCount }}</span>
                      <span class="wp-pct" [attr.title]="'weekly.progress.doneLabel' | translate">· {{ pct(p) }}%</span>
                      @if (p.totalSeconds > 0) {
                        <span class="wp-time" [attr.title]="'weekly.progress.timeLabel' | translate">· ⏱ {{ fmtTime(p.totalSeconds) }}</span>
                      }
                    </span>
                  }
                </div>
                <div class="wp-actions">
                  <button mat-stroked-button color="primary" [routerLink]="['/weekly', r.id]">
                    <mat-icon>play_arrow</mat-icon> {{ 'weekly.play' | translate }}
                  </button>
                  <button mat-icon-button (click)="toggleBoard(r)"
                          [attr.title]="'weekly.leaderboard.toggle' | translate" [attr.aria-expanded]="expandedId === r.id">
                    <mat-icon>{{ expandedId === r.id ? 'expand_less' : 'leaderboard' }}</mat-icon>
                  </button>
                  @if (auth.isAdmin) {
                    <button mat-icon-button color="warn" (click)="remove(r)" [attr.title]="'common.delete' | translate">
                      <mat-icon>delete</mat-icon>
                    </button>
                  }
                </div>
              </div>

              @if (expandedId === r.id) {
                <div class="lb">
                  @if (boardLoading[r.id]) {
                    <app-loading-spinner />
                  } @else if ((board[r.id]?.length ?? 0) > 0) {
                    <div class="lb-scroll">
                      <table class="lb-table">
                        <thead>
                          <tr>
                            <th class="lb-rank">#</th>
                            <th>{{ 'weekly.leaderboard.player' | translate }}</th>
                            <th class="lb-acc">{{ 'weekly.leaderboard.accuracy' | translate }}</th>
                            <th class="lb-time">{{ 'weekly.leaderboard.time' | translate }}</th>
                            @if (auth.isAdmin) { <th class="lb-info"></th> }
                          </tr>
                        </thead>
                        <tbody>
                          @for (p of board[r.id]; track $index; let i = $index) {
                            <tr>
                              <td class="lb-rank">{{ i + 1 }}</td>
                              <td class="lb-player">{{ p.discordUsername || p.name }}@if (p.completed) {<mat-icon class="lb-done" [attr.title]="'weekly.leaderboard.completed' | translate">emoji_events</mat-icon>}</td>
                              <td class="lb-acc">{{ p.solvedCount }}/{{ boardTotal[r.id] }} · {{ accuracyPct(p, boardTotal[r.id]) }}%</td>
                              <td class="lb-time">⏱ {{ fmtTime(p.totalSeconds) }}</td>
                              @if (auth.isAdmin) {
                                <td class="lb-info">
                                  <button mat-icon-button (click)="openBreakdown(r.id, p)"
                                          [attr.title]="'weekly.breakdown.open' | translate">
                                    <mat-icon>info_outline</mat-icon>
                                  </button>
                                </td>
                              }
                            </tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  } @else {
                    <span class="lb-empty">{{ 'weekly.leaderboard.empty' | translate }}</span>
                  }
                </div>
              }
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .weekly-container { max-width: 1000px; margin: 24px auto; padding: 0 16px; }
    .intro { color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 16px; }
    .empty-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }
    .upload-card { margin-bottom: 20px; }
    .upload-row { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
    .f-date, .f-time { width: 150px; }
    .f-title { flex: 1; min-width: 180px; }
    .upload-hint { color: color-mix(in srgb, currentColor 47%, transparent); font-size: 0.8rem; margin: 4px 0 0; }
    .or-chapter { display: flex; align-items: center; gap: 12px; margin-top: 12px; }
    .or-sep { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.85rem; }
    .inline-date, .inline-time { font: inherit; padding: 2px 4px; border: 1px solid #ccc; border-radius: 4px; }
    .inline-title { font: inherit; padding: 2px 4px; border: 1px solid #ccc; border-radius: 4px; width: 100%; max-width: 320px; }
    .inline-desc { font: inherit; font-size: 0.9rem; padding: 2px 4px; border: 1px solid #ccc; border-radius: 4px; width: 100%; max-width: 420px; }
    .f-desc { flex: 1; min-width: 200px; }
    .wp-desc { color: color-mix(in srgb, currentColor 78%, transparent); font-size: 0.9rem; white-space: pre-wrap; }

    /* Karten-Liste (responsiv statt fester Tabelle) */
    .wp-list { display: flex; flex-direction: column; gap: 8px; }
    .wp-card { padding: 12px 16px; }
    .wp-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .wp-meta { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .wp-edit-sched { display: flex; flex-wrap: wrap; gap: 6px; }
    .wp-title { font-weight: 600; overflow: hidden; text-overflow: ellipsis; }
    .wp-sched { color: color-mix(in srgb, currentColor 70%, transparent); font-size: 0.9rem; }
    .wp-actions { display: flex; align-items: center; flex-shrink: 0; }

    .wp-prog { font-variant-numeric: tabular-nums; }
    .wp-solved { color: #2e7d32; font-weight: 600; }
    .wp-failed { color: #c62828; font-weight: 600; }
    .wp-slash { color: color-mix(in srgb, currentColor 40%, transparent); margin: 0 4px; }
    .wp-pct { color: color-mix(in srgb, currentColor 65%, transparent); margin-left: 6px; }
    .wp-time { color: color-mix(in srgb, currentColor 65%, transparent); margin-left: 6px; }

    .lb { padding: 12px 0 4px; }
    .lb-scroll { overflow-x: auto; }
    .lb-table { width: 100%; max-width: 620px; border-collapse: collapse; font-variant-numeric: tabular-nums; }
    .lb-info { width: 2.5em; text-align: center; }
    .lb-info button { width: 32px; height: 32px; line-height: 32px; }
    .lb-table th, .lb-table td { text-align: left; padding: 4px 8px; border-bottom: 1px solid color-mix(in srgb, currentColor 10%, transparent); }
    .lb-table th { color: color-mix(in srgb, currentColor 47%, transparent); font-weight: 600; font-size: 0.8rem; }
    .lb-rank { width: 2.5em; color: color-mix(in srgb, currentColor 40%, transparent); }
    /* Lange Usernamen umbrechen, statt die Zeile zu verbreitern und die (i)-Spalte aus dem Bildschirm zu schieben */
    .lb-player { word-break: break-word; overflow-wrap: anywhere; }
    .lb-acc, .lb-time { white-space: nowrap; }
    .lb-acc { text-align: right; }
    .lb-time { text-align: right; color: color-mix(in srgb, currentColor 65%, transparent); }
    .lb-done { font-size: 16px; height: 16px; width: 16px; vertical-align: text-bottom; color: #f9a825; margin-left: 4px; }
    .lb-empty { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; padding: 8px 0; display: inline-block; }

    @media (max-width: 600px) {
      .weekly-container { margin: 16px auto; }
      .upload-row { flex-direction: column; align-items: stretch; }
      .f-date, .f-title { width: 100%; }
      .f-time { width: 100%; }
      .inline-title { max-width: none; }
      .wp-row { flex-direction: column; align-items: stretch; }
      .wp-actions { justify-content: flex-end; }
      .wp-prog { white-space: nowrap; }
    }
  `]
})
export class WeeklyListComponent implements OnInit {
  rows: WeeklyPostRow[] = [];
  loading = false;
  /** Per-User-Fortschritt je WeeklyPost-Id (nur Posts mit Versuchen). */
  prog: Record<number, WeeklyProgress> = {};

  /** Aufgeklappte Bestenliste (eine zur Zeit). */
  expandedId: number | null = null;
  boardLoading: Record<number, boolean> = {};
  /** Sortierte Bestenliste je Post (gecacht). */
  board: Record<number, WeeklyPlayerResult[]> = {};
  /** Puzzle-Gesamtzahl je Post (für die Genauigkeit). */
  boardTotal: Record<number, number> = {};

  uploadFile: File | null = null;
  uploadFileName = '';
  uploadDate = '';
  uploadTime = '19:00';
  uploadTitle = '';
  uploadDescription = '';
  uploading = false;

  constructor(
    public auth: AuthService,
    private weekly: WeeklyService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private dialog: MatDialog
  ) {}

  /** Admin: Detailaufschlüsselung eines Spielers öffnen (eine Zeile je Puzzle). */
  openBreakdown(weeklyId: number, p: WeeklyPlayerResult): void {
    this.dialog.open(WeeklyBreakdownDialogComponent, {
      data: { weeklyId, userId: p.userId, playerName: p.discordUsername || p.name },
      width: '640px', maxWidth: '95vw',
    });
  }

  ngOnInit(): void {
    this.loadPosts();
  }

  /** Öffnet den Dialog „Wochenpost aus Buch-Kapitel" (Buch + Kapitel wählen); lädt bei Erfolg neu. */
  openFromChapter(): void {
    const ref = this.dialog.open(WeeklyFromChapterDialogComponent, {
      data: { date: this.uploadDate, time: this.uploadTime },
      width: '480px', maxWidth: '95vw',
    });
    ref.afterClosed().subscribe(result => {
      if (result) {
        this.snackbar.info(this.translate.instant('weekly.created'), { action: 'common.ok', duration: 3000 });
        this.loadPosts();
      }
    });
  }

  loadPosts(): void {
    this.loading = true;
    this.weekly.getAll().subscribe({
      next: posts => {
        this.rows = posts.map(p => ({ ...p, editDate: weeklyDatePart(p.scheduledAt), editTime: weeklyTimePart(p.scheduledAt) }));
        this.suggestNextSlot();
        this.loading = false;
        this.loadProgress();
      },
      error: () => {
        this.snackbar.info(this.translate.instant('weekly.loadFailed'), { action: 'common.ok', duration: 3000 });
        this.loading = false;
      }
    });
  }

  /** Lädt den eigenen Fortschritt je Post (für die Spalte „gelöst/failed · %"). */
  private loadProgress(): void {
    if (!this.auth.isLoggedIn) return;
    this.weekly.getAllProgress().subscribe({
      next: list => {
        const map: Record<number, WeeklyProgress> = {};
        for (const p of list) map[p.weeklyPostId] = p;
        this.prog = map;
      },
      error: () => { /* Fortschritt ist optional — Übersicht funktioniert auch ohne */ }
    });
  }

  /** Prozent gespielt (von allen Puzzles des Posts). */
  pct(p: WeeklyProgress): number {
    return p.total > 0 ? Math.round(100 * p.playedCount / p.total) : 0;
  }

  /** Gesamtzeit als m:ss bzw. h:mm:ss. */
  fmtTime(seconds: number): string {
    const s = Math.max(0, Math.floor(seconds));
    const sec = s % 60, m = Math.floor(s / 60) % 60, h = Math.floor(s / 3600);
    const p2 = (n: number) => n.toString().padStart(2, '0');
    return h > 0 ? `${h}:${p2(m)}:${p2(sec)}` : `${m}:${p2(sec)}`;
  }

  /** Genauigkeit eines Spielers in % (gelöst / gesamt). */
  accuracyPct(p: WeeklyPlayerResult, total: number): number {
    return total > 0 ? Math.round(100 * p.solvedCount / total) : 0;
  }

  /** Bestenliste eines Posts auf-/zuklappen; lädt + sortiert beim ersten Öffnen (danach gecacht). */
  toggleBoard(row: WeeklyPost): void {
    if (this.expandedId === row.id) { this.expandedId = null; return; }
    this.expandedId = row.id;
    if (this.board[row.id]) return;   // schon geladen
    this.boardLoading[row.id] = true;
    this.weekly.getResults(row.id).subscribe({
      next: res => {
        this.boardTotal[row.id] = res.total;
        this.board[row.id] = sortLeaderboard(res.players, res.total);
        this.boardLoading[row.id] = false;
      },
      error: () => { this.board[row.id] = []; this.boardLoading[row.id] = false; },
    });
  }

  /** Prefill für den Upload: letzter Termin + 7 Tage, gleiche Uhrzeit; sonst heute + 19:00. */
  private suggestNextSlot(): void {
    // Liste ist nach Termin absteigend sortiert -> rows[0] = letzter Eintrag.
    const slot = nextWeeklySlot(this.rows.length > 0 ? this.rows[0].scheduledAt : null);
    this.uploadDate = slot.date;
    this.uploadTime = slot.time;
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files && input.files.length ? input.files[0] : null;
    // Client-seitige Validierung wie beim Repertoire-Upload: accept=".pgn" am Input
    // ist nur ein Hinweis (im Dateidialog auf „Alle Dateien" umstellbar). Nur .pgn
    // bis 10 MB akzeptieren — sonst Auswahl verwerfen und Hinweis zeigen.
    const MAX_BYTES = 10 * 1024 * 1024;
    if (file && (!file.name.toLowerCase().endsWith('.pgn') || file.size > MAX_BYTES)) {
      this.snackbar.info(this.translate.instant('weekly.upload.invalidFile'), { action: 'common.ok', duration: 4000 });
      this.uploadFile = null;
      this.uploadFileName = '';
      input.value = '';
      return;
    }
    this.uploadFile = file;
    this.uploadFileName = file?.name ?? '';
  }

  upload(): void {
    if (!this.uploadFile || !this.uploadDate || !this.uploadTime) return;
    this.uploading = true;
    const scheduledAt = `${this.uploadDate}T${this.uploadTime}:00`;
    this.weekly.create(this.uploadFile, scheduledAt, this.uploadTitle.trim() || undefined, this.uploadDescription.trim() || undefined).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('weekly.created'), { action: 'common.ok', duration: 3000 });
        this.uploading = false;
        this.uploadFile = null;
        this.uploadFileName = '';
        this.uploadTitle = '';
        this.uploadDescription = '';
        this.loadPosts();   // lädt neu + setzt nächsten Termin-Vorschlag
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('weekly.uploadFailed'), { action: 'common.ok', duration: 4000 });
        this.uploading = false;
      }
    });
  }

  savePost(row: WeeklyPostRow): void {
    if (!row.editDate || !row.editTime) return;
    const scheduledAt = `${row.editDate}T${row.editTime}:00`;
    this.weekly.update(row.id, { title: row.title, description: row.description ?? '', scheduledAt }).subscribe({
      next: p => { row.scheduledAt = p.scheduledAt; row.description = p.description ?? null; },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('weekly.saveFailed'), { action: 'common.ok', duration: 3000 });
        this.loadPosts();
      }
    });
  }

  remove(row: WeeklyPostRow): void {
    if (!confirm(this.translate.instant('weekly.deleteConfirm', { title: row.title }))) return;
    this.weekly.delete(row.id).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('weekly.deleted'), { action: 'common.ok', duration: 3000 });
        this.loadPosts();
      },
      error: () => this.snackbar.info(this.translate.instant('weekly.deleteFailed'), { action: 'common.ok', duration: 3000 })
    });
  }
}
