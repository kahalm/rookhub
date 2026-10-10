import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { RouterModule } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { WeeklyService, WeeklyPost, WeeklyProgress, WeeklyPlayerResult, sortLeaderboard, nextWeeklySlot, weeklyDatePart, weeklyTimePart, weeklyScheduledAtUtc, weeklyDisplayTime, weeklyTitleLabel } from './weekly.service';
import { WeeklyScheduleFieldsComponent } from './weekly-schedule-fields.component';
import { WeeklyBreakdownDialogComponent } from './weekly-breakdown-dialog.component';
import { WeeklyFromChapterDialogComponent } from './weekly-from-chapter-dialog.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { apiErrorText } from '../../core/api-error';
import { formatSecondsClock } from '../../shared/clock-format.util';

interface WeeklyPostRow extends WeeklyPost {
  editDate: string;   // YYYY-MM-DD (Admin-Edit)
  editTime: string;   // HH:mm
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-weekly-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatDialogModule, MatMenuModule,
    TranslatePipe, LoadingSpinnerComponent, WeeklyScheduleFieldsComponent
  ],
  template: `
    <div class="weekly-container">
      <h1>{{ 'weekly.title' | translate }}</h1>
      <p class="intro">{{ 'weekly.intro' | translate }}</p>

      @if (canManage) {
        <mat-card class="upload-card">
          <mat-card-header><mat-card-title>{{ 'weekly.upload.title' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            <div class="upload-row">
              <input #pgnInput type="file" accept=".pgn" hidden (change)="onFileSelected($event)">
              <button mat-stroked-button (click)="pgnInput.click()">
                <mat-icon>upload_file</mat-icon> {{ uploadFileName || ('weekly.upload.choosePgn' | translate) }}
              </button>
              <app-weekly-schedule-fields [(date)]="uploadDate" [(time)]="uploadTime" />
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
            @if (previewAt(uploadDate, uploadTime); as at) {
              <p class="sched-preview">{{ 'weekly.schedulePreview' | translate:{ date: (at | date:'EEE, dd.MM.yyyy'), time: (at | date:'HH:mm') } }}</p>
            }
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
              @if (editId === r.id && draft; as d) {
                <div class="wp-edit">
                  <div class="upload-row">
                    <mat-form-field appearance="outline" class="f-title">
                      <mat-label>{{ 'weekly.columns.title' | translate }}</mat-label>
                      <input matInput [(ngModel)]="d.title">
                    </mat-form-field>
                    <mat-form-field appearance="outline" class="f-desc">
                      <mat-label>{{ 'weekly.fields.descriptionOptional' | translate }}</mat-label>
                      <input matInput [(ngModel)]="d.description" maxlength="500">
                    </mat-form-field>
                    <app-weekly-schedule-fields [(date)]="d.date" [(time)]="d.time" />
                  </div>
                  @if (previewAt(d.date, d.time); as at) {
                    <p class="sched-preview">{{ 'weekly.schedulePreview' | translate:{ date: (at | date:'EEE, dd.MM.yyyy'), time: (at | date:'HH:mm') } }}</p>
                  }
                  <div class="wp-edit-actions">
                    <button mat-button (click)="cancelEdit()">{{ 'common.cancel' | translate }}</button>
                    <button mat-flat-button color="primary" [disabled]="!d.date || !d.time || saving" (click)="saveEdit(r)">
                      {{ 'common.save' | translate }}
                    </button>
                  </div>
                </div>
              } @else {
                <div class="wp-row">
                  <div class="wp-head">
                    <div class="wp-meta">
                      <span class="wp-title">{{ titleLabel(r) }}</span>
                      <span class="wp-sched">
                        {{ displayTime(r) | date:'EEE, dd.MM.yyyy' }} · {{ displayTime(r) | date:'HH:mm' }}@if (puzzleCount(r); as n) { · {{ 'weekly.puzzleCount' | translate:{ count: n } }}}
                      </span>
                      @if (r.description) { <span class="wp-desc">{{ r.description }}</span> }
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
                    @if (canManage) {
                      <div class="wp-manage">
                        <button mat-icon-button class="wp-edit-btn" (click)="startEdit(r)" [attr.aria-label]="'common.edit' | translate"
                                [attr.title]="'common.edit' | translate">
                          <mat-icon>edit</mat-icon>
                        </button>
                        <button mat-icon-button [matMenuTriggerFor]="more" [attr.aria-label]="'common.moreActions' | translate"
                                [attr.title]="'common.moreActions' | translate">
                          <mat-icon>more_vert</mat-icon>
                        </button>
                        <mat-menu #more="matMenu">
                          <button mat-menu-item (click)="remove(r)">
                            <mat-icon>delete</mat-icon> {{ 'common.delete' | translate }}
                          </button>
                        </mat-menu>
                      </div>
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
                  </div>
                </div>
              }

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
                            @if (canManage) { <th class="lb-info"></th> }
                          </tr>
                        </thead>
                        <tbody>
                          @for (p of board[r.id]; track $index; let i = $index) {
                            <tr>
                              <td class="lb-rank">{{ i + 1 }}</td>
                              <td class="lb-player">{{ p.discordUsername || p.name }}@if (p.completed) {<mat-icon class="lb-done" [attr.title]="'weekly.leaderboard.completed' | translate">emoji_events</mat-icon>}</td>
                              <td class="lb-acc">{{ p.solvedCount }}/{{ boardTotal[r.id] }} · {{ accuracyPct(p, boardTotal[r.id]) }}%</td>
                              <td class="lb-time">⏱ {{ fmtTime(p.totalSeconds) }}</td>
                              @if (canManage) {
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
    .f-title { flex: 1; min-width: 180px; }
    .upload-hint { color: color-mix(in srgb, currentColor 47%, transparent); font-size: 0.8rem; margin: 4px 0 0; }
    .or-chapter { display: flex; align-items: center; gap: 12px; margin-top: 12px; }
    .or-sep { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.85rem; }
    .sched-preview { color: color-mix(in srgb, currentColor 70%, transparent); font-size: 0.85rem; margin: 0; }
    .f-desc { flex: 1; min-width: 200px; }
    .wp-desc { color: color-mix(in srgb, currentColor 78%, transparent); font-size: 0.9rem; white-space: pre-wrap; }

    /* Karten-Liste (responsiv statt fester Tabelle) */
    .wp-list { display: flex; flex-direction: column; gap: 8px; }
    .wp-card { padding: 12px 16px; }
    .wp-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    /* Kopf: Titel/Termin links, Stift + ⋮ rechts daneben (Lesemodus; bearbeitet wird über den Stift) */
    .wp-head { display: flex; align-items: flex-start; gap: 4px; flex: 1; min-width: 0; }
    .wp-meta { display: flex; flex-direction: column; gap: 4px; min-width: 0; flex: 1; }
    .wp-manage { display: flex; align-items: center; flex-shrink: 0; }
    .wp-title { font-weight: 600; font-size: 1rem; overflow: hidden; text-overflow: ellipsis; }
    .wp-edit { display: flex; flex-direction: column; gap: 8px; }
    .wp-edit-actions { display: flex; justify-content: flex-end; gap: 8px; }
    .wp-sched { color: color-mix(in srgb, currentColor 70%, transparent); font-size: 0.85rem; }
    .wp-actions { display: flex; align-items: center; flex-shrink: 0; }

    .wp-prog { font-variant-numeric: tabular-nums; }
    .wp-solved { color: var(--rh-success); font-weight: 600; }
    .wp-failed { color: var(--rh-error); font-weight: 600; }
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
      .f-title { width: 100%; }
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

  /** Karte im Bearbeiten-Modus (eine zur Zeit) und ihr Entwurf — die Liste zeigt sonst nur Text. */
  editId: number | null = null;
  draft: { title: string; description: string; date: string; time: string } | null = null;
  saving = false;

  constructor(
    public auth: AuthService,
    private weekly: WeeklyService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private dialog: MatDialog,
    private confirm: ConfirmService,
  ) {}

  /** Anlegen, Bearbeiten, Löschen und Spieler-Aufschlüsselung: wer `weeklyposts.manage` hat (Admins immer) — wie der
   *  Server; vorher hing es am Admin-Flag, eine Rolle mit dem Recht sah die Knöpfe nicht (Codereview F5-005). */
  get canManage(): boolean {
    return this.auth.has('weeklyposts.manage');
  }

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

  /** Termin für die Anzeige in Ortszeit (Server liefert UTC). */
  displayTime(r: WeeklyPost): number | string { return weeklyDisplayTime(r.scheduledAt); }

  /** „Wochenpost 3" statt der nackten „3" (siehe weeklyTitleLabel). */
  titleLabel(r: WeeklyPost): string { return weeklyTitleLabel(r.title, this.translate); }

  /** Anzahl Puzzles, soweit bekannt (aus dem eigenen Fortschritt bzw. der geladenen Bestenliste). */
  puzzleCount(r: WeeklyPost): number | null {
    return this.prog[r.id]?.total || this.boardTotal[r.id] || null;
  }

  /** Vorschau „Erscheint am …" für Datum + Uhrzeit (Wandzeit, über dieselbe UTC-Umrechnung wie beim Speichern). */
  previewAt(date: string, time: string): number | null {
    if (!date || !time) return null;
    const t = weeklyDisplayTime(weeklyScheduledAtUtc(date, time));
    return typeof t === 'number' && !isNaN(t) ? t : null;
  }

  startEdit(r: WeeklyPostRow): void {
    this.editId = r.id;
    this.draft = { title: r.title ?? '', description: r.description ?? '', date: r.editDate, time: r.editTime };
  }

  cancelEdit(): void {
    this.editId = null;
    this.draft = null;
  }

  /** Entwurf übernehmen und speichern; die Karte geht erst bei Erfolg zurück in den Lesemodus. */
  saveEdit(r: WeeklyPostRow): void {
    const d = this.draft;
    if (!d || !d.date || !d.time) return;
    r.title = d.title.trim();
    r.description = d.description;
    r.editDate = d.date;
    r.editTime = d.time;
    this.savePost(r);
  }

  /** Prozent gespielt (von allen Puzzles des Posts). */
  pct(p: WeeklyProgress): number {
    return p.total > 0 ? Math.round(100 * p.playedCount / p.total) : 0;
  }

  /** Gesamtzeit als m:ss bzw. h:mm:ss. */
  fmtTime(seconds: number): string { return formatSecondsClock(seconds); }

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
    const scheduledAt = weeklyScheduledAtUtc(this.uploadDate, this.uploadTime);
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
        this.snackbar.info(apiErrorText(err, this.translate, 'weekly.uploadFailed'), { action: 'common.ok', duration: 4000 });
        this.uploading = false;
      }
    });
  }

  savePost(row: WeeklyPostRow): void {
    if (!row.editDate || !row.editTime) return;
    const scheduledAt = weeklyScheduledAtUtc(row.editDate, row.editTime);
    this.saving = true;
    this.weekly.update(row.id, { title: row.title, description: row.description ?? '', scheduledAt }).subscribe({
      next: p => {
        row.scheduledAt = p.scheduledAt;
        row.description = p.description ?? null;
        if (p.title !== undefined) row.title = p.title;
        this.saving = false;
        if (this.editId === row.id) this.cancelEdit();
      },
      error: err => {
        this.saving = false;
        this.snackbar.info(apiErrorText(err, this.translate, 'weekly.saveFailed'), { action: 'common.ok', duration: 3000 });
        this.cancelEdit();
        this.loadPosts();
      }
    });
  }

  remove(row: WeeklyPostRow): void {
    this.confirm.ask('weekly.deleteConfirm', { title: row.title }).subscribe(ok => {
      if (!ok) return;
      this.weekly.delete(row.id).subscribe({
        next: () => {
          this.snackbar.info(this.translate.instant('weekly.deleted'), { action: 'common.ok', duration: 3000 });
          this.loadPosts();
        },
        error: () => this.snackbar.info(this.translate.instant('weekly.deleteFailed'), { action: 'common.ok', duration: 3000 })
      });
    });
  }
}
