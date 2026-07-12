import { Component, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { forkJoin } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FriendsService } from '../../core/friends.service';
import { Friend } from '../../core/models';
import { CourseService, CourseShareRecipient } from './course.service';
import { SnackbarService } from '../../core/snackbar.service';

export interface ShareCourseDialogData {
  bookId: number;
  courseName: string;
}

/**
 * Dialog „Kurs teilen": Multi-Select über die Freundesliste (nur mit Freunden teilbar, wie
 * Puzzle-Challenges) + Liste der Nutzer, mit denen der Kurs bereits geteilt ist (je mit
 * Zurücknehmen-Knopf). Self-contained — ruft CourseService direkt.
 */
@Component({
  selector: 'app-share-course-dialog',
  standalone: true,
  imports: [
    CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatCheckboxModule,
    MatProgressSpinnerModule, MatTooltipModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>{{ 'courses.share.title' | translate:{ name: data.courseName } }}</h2>
    <mat-dialog-content>
      @if (loading) {
        <div class="center"><mat-spinner diameter="32"></mat-spinner></div>
      } @else {
        <p class="hint">{{ 'courses.share.hint' | translate }}</p>

        @if (recipients.length > 0) {
          <div class="section">
            <h3>{{ 'courses.share.sharedWith' | translate }}</h3>
            <ul class="recipient-list">
              @for (r of recipients; track r.userId) {
                <li class="recipient-row">
                  <mat-icon class="ok-icon">check_circle</mat-icon>
                  <span class="rname">{{ r.displayName || r.username }}</span>
                  <button mat-icon-button class="remove-btn" [disabled]="busy"
                          [matTooltip]="'courses.share.unshareTooltip' | translate"
                          (click)="unshare(r)">
                    <mat-icon>close</mat-icon>
                  </button>
                </li>
              }
            </ul>
          </div>
        }

        <div class="section">
          <h3>{{ 'courses.share.pickFriends' | translate }}</h3>
          @if (selectableFriends.length === 0) {
            <p class="empty">{{ 'courses.share.noFriends' | translate }}</p>
          } @else {
            <div class="friend-list">
              @for (f of selectableFriends; track f.userId) {
                <mat-checkbox [checked]="selected.has(f.userId)" (change)="toggle(f.userId, $event.checked)">
                  {{ f.displayName || f.username }}
                </mat-checkbox>
              }
            </div>
          }
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.close' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="selected.size === 0 || busy" (click)="share()">
        {{ 'courses.share.shareButton' | translate:{ count: selected.size } }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .center { display: flex; justify-content: center; padding: 24px; }
    .hint { margin: 0 0 12px; font-size: 0.88rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .section { margin-bottom: 14px; min-width: min(360px, 80vw); }
    .section h3 { font-size: 0.85rem; font-weight: 600; margin: 0 0 6px; opacity: 0.75; }
    .recipient-list { list-style: none; margin: 0; padding: 0; }
    .recipient-row { display: flex; align-items: center; gap: 8px; padding: 2px 0; }
    .ok-icon { color: #4caf50; font-size: 18px; width: 18px; height: 18px; }
    .rname { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .remove-btn { width: 30px; height: 30px; padding: 3px; }
    .remove-btn mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .friend-list { display: flex; flex-direction: column; gap: 4px; max-height: 260px; overflow-y: auto; }
    .empty { font-style: italic; font-size: 0.85rem; color: color-mix(in srgb, currentColor 55%, transparent); margin: 0; }
  `]
})
export class ShareCourseDialogComponent implements OnInit {
  loading = true;
  busy = false;
  friends: Friend[] = [];
  recipients: CourseShareRecipient[] = [];
  selected = new Set<number>();

  constructor(
    public dialogRef: MatDialogRef<ShareCourseDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: ShareCourseDialogData,
    private friendsService: FriendsService,
    private courseService: CourseService,
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  ngOnInit(): void {
    forkJoin({
      friends: this.friendsService.getFriends(),
      recipients: this.courseService.getShareRecipients(this.data.bookId)
    }).subscribe({
      next: ({ friends, recipients }) => {
        this.friends = friends;
        this.recipients = recipients;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.snackbar.info(this.translate.instant('courses.share.loadFailed'), { action: 'common.ok', duration: 3000 });
        this.dialogRef.close();
      }
    });
  }

  /** Freunde, die noch nicht Empfänger sind (bereits geteilte stehen in der oberen Liste). */
  get selectableFriends(): Friend[] {
    const shared = new Set(this.recipients.map(r => r.userId));
    return this.friends.filter(f => !shared.has(f.userId));
  }

  toggle(userId: number, checked: boolean): void {
    if (checked) this.selected.add(userId); else this.selected.delete(userId);
  }

  share(): void {
    if (this.selected.size === 0 || this.busy) return;
    this.busy = true;
    const ids = [...this.selected];
    this.courseService.shareCourse(this.data.bookId, ids).subscribe({
      next: res => {
        this.busy = false;
        this.selected.clear();
        // Frisch geteilte Empfänger oben einsortieren (aus der geladenen Freundesliste).
        const known = new Set(this.recipients.map(r => r.userId));
        for (const f of this.friends) {
          if (ids.includes(f.userId) && !known.has(f.userId)) {
            this.recipients = [...this.recipients, {
              userId: f.userId, username: f.username, displayName: f.displayName, sharedAt: new Date().toISOString()
            }];
          }
        }
        const skipped = res.skipped?.length ?? 0;
        if (res.shared > 0 && skipped === 0) {
          this.snackbar.success(this.translate.instant('courses.share.shared', { count: res.shared }));
        } else if (res.shared > 0) {
          this.snackbar.info(this.translate.instant('courses.share.sharedPartial', { shared: res.shared, skipped }));
        } else {
          this.snackbar.info(this.translate.instant('courses.share.nothingShared'), { action: 'common.ok', duration: 3000 });
        }
      },
      error: err => {
        this.busy = false;
        this.snackbar.info(err?.error?.message || this.translate.instant('courses.share.failed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  unshare(r: CourseShareRecipient): void {
    if (this.busy) return;
    this.busy = true;
    this.courseService.unshareCourse(this.data.bookId, r.userId).subscribe({
      next: () => {
        this.busy = false;
        this.recipients = this.recipients.filter(x => x.userId !== r.userId);
        this.snackbar.info(this.translate.instant('courses.share.unshared', { name: r.displayName || r.username }), { action: 'common.ok', duration: 2500 });
      },
      error: () => {
        this.busy = false;
        this.snackbar.info(this.translate.instant('courses.share.failed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }
}
