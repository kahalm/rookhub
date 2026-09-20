import { Component, ChangeDetectionStrategy, Injectable, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA,
} from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { map, Observable } from 'rxjs';

/** Was die Rückfrage zeigt. `message` ist bereits übersetzter Text. */
export interface ConfirmData {
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
}

/**
 * Rückfrage als Material-Dialog statt `window.confirm`.
 *
 * <p><b>Warum nicht das eingebaute confirm:</b> im VOLLBILD rendert der Browser nur den Teilbaum des
 * Vollbild-Elements, und die native Rückfrage kam dahinter zu liegen — sichtbar war nichts, die
 * Seite reagierte aber nicht mehr (gemeldet 2026-09-20 an „Teil löschen"). Ein CDK-Overlay hängt
 * der <c>FullscreenOverlayService</c> ins Vollbild-Element um, es liegt also immer oben.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, TranslatePipe],
  template: `
    <div mat-dialog-content class="confirm-text">{{ data.message }}</div>
    <div mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">
        {{ data.cancelLabel || ('common.cancel' | translate) }}
      </button>
      <button mat-flat-button color="warn" [mat-dialog-close]="true" cdkFocusInitial>
        {{ data.confirmLabel || ('common.ok' | translate) }}
      </button>
    </div>
  `,
  styles: [`
    .confirm-text { white-space: pre-line; padding-top: 1rem; }
  `],
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<ConfirmDialogComponent, boolean>>(MatDialogRef);
}

/** Eine Zeile Rückfrage: `ask('…')` liefert genau ein `true`/`false`. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private dialog = inject(MatDialog);
  private translate = inject(TranslateService);

  /** `messageKey` ist ein i18n-Schlüssel; ein bereits übersetzter Text geht genauso durch. */
  ask(messageKey: string, params?: Record<string, unknown>): Observable<boolean> {
    const data: ConfirmData = { message: this.translate.instant(messageKey, params) };
    return this.dialog.open(ConfirmDialogComponent, { data, maxWidth: '32rem' })
      .afterClosed()
      .pipe(map(result => result === true));
  }
}
