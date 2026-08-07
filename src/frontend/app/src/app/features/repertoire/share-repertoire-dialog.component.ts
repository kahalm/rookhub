import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { Observable } from 'rxjs';
import { RepertoireService } from '../../core/repertoire.service';
import {
  SHARE_DIALOG_IMPORTS, SHARE_DIALOG_STYLES, SHARE_DIALOG_TEMPLATE,
  ShareBatchResult, ShareRecipient, ShareWithFriendsDialogBase,
} from '../../shared/share-with-friends/share-with-friends-dialog.base';

export interface ShareRepertoireDialogData {
  repertoireId: number;
  repertoireName: string;
}

/**
 * Dialog „Repertoire teilen": Multi-Select über die Freundesliste (nur mit Freunden teilbar) +
 * Liste der Nutzer, mit denen es bereits geteilt ist (je mit Zurücknehmen-Knopf). Ablauf/Markup
 * teilt er sich mit dem Kurs-Dialog über `ShareWithFriendsDialogBase`; hier steht nur, WAS
 * geteilt wird (Repertoire-Service + i18n-Namensraum „repertoire.share").
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-share-repertoire-dialog',
  standalone: true,
  imports: SHARE_DIALOG_IMPORTS,
  template: SHARE_DIALOG_TEMPLATE,
  styles: [SHARE_DIALOG_STYLES],
})
export class ShareRepertoireDialogComponent extends ShareWithFriendsDialogBase {
  readonly data = inject<ShareRepertoireDialogData>(MAT_DIALOG_DATA);
  private repertoireService = inject(RepertoireService);

  protected readonly i18nPrefix = 'repertoire.share';
  get targetName(): string { return this.data.repertoireName; }

  protected loadRecipients(): Observable<ShareRecipient[]> {
    return this.repertoireService.getShareRecipients(this.data.repertoireId);
  }

  protected shareWith(userIds: number[]): Observable<ShareBatchResult> {
    return this.repertoireService.share(this.data.repertoireId, userIds);
  }

  protected unshareFrom(userId: number): Observable<void> {
    return this.repertoireService.unshare(this.data.repertoireId, userId);
  }
}
