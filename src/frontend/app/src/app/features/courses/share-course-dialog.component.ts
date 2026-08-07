import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { Observable } from 'rxjs';
import { CourseService } from './course.service';
import {
  SHARE_DIALOG_IMPORTS, SHARE_DIALOG_STYLES, SHARE_DIALOG_TEMPLATE,
  ShareBatchResult, ShareRecipient, ShareWithFriendsDialogBase,
} from '../../shared/share-with-friends/share-with-friends-dialog.base';

export interface ShareCourseDialogData {
  bookId: number;
  courseName: string;
}

/**
 * Dialog „Kurs teilen": Multi-Select über die Freundesliste (nur mit Freunden teilbar, wie
 * Puzzle-Challenges) + Liste der Nutzer, mit denen der Kurs bereits geteilt ist (je mit
 * Zurücknehmen-Knopf). Ablauf/Markup stecken in `ShareWithFriendsDialogBase` — hier steht
 * nur, WAS geteilt wird (Kurs-Service + i18n-Namensraum „courses.share").
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-share-course-dialog',
  standalone: true,
  imports: SHARE_DIALOG_IMPORTS,
  template: SHARE_DIALOG_TEMPLATE,
  styles: [SHARE_DIALOG_STYLES],
})
export class ShareCourseDialogComponent extends ShareWithFriendsDialogBase {
  readonly data = inject<ShareCourseDialogData>(MAT_DIALOG_DATA);
  private courseService = inject(CourseService);

  protected readonly i18nPrefix = 'courses.share';
  get targetName(): string { return this.data.courseName; }

  protected loadRecipients(): Observable<ShareRecipient[]> {
    return this.courseService.getShareRecipients(this.data.bookId);
  }

  protected shareWith(userIds: number[]): Observable<ShareBatchResult> {
    return this.courseService.shareCourse(this.data.bookId, userIds);
  }

  protected unshareFrom(userId: number): Observable<void> {
    return this.courseService.unshareCourse(this.data.bookId, userId);
  }
}
