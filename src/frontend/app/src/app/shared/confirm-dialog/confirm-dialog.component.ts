import { Component, ChangeDetectionStrategy, Injectable, InjectionToken, inject } from '@angular/core';
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
 * Feste Knopf-Beschriftungen für Oberflächen ohne Sprachwahl. LeagueHub und ClubHub sind deutsch und
 * stellen keine Sprache ein — ohne diese Vorgabe stünde unter der deutschen Frage „Cancel“/„OK“ aus der
 * englischen Rückfall-Übersetzung. RookHub selbst gibt nichts vor und übersetzt `common.cancel`/`common.ok`.
 */
export const CONFIRM_LABELS = new InjectionToken<{ confirm: string; cancel: string }>('CONFIRM_LABELS');

/**
 * Rückfrage als Material-Dialog statt `window.confirm`.
 *
 * <p><b>Warum nicht das eingebaute confirm:</b> im VOLLBILD rendert der Browser nur den Teilbaum des
 * Vollbild-Elements, und die native Rückfrage kam dahinter zu liegen — sichtbar war nichts, die
 * Seite reagierte aber nicht mehr (gemeldet 2026-09-20 an „Teil löschen"). Ein CDK-Overlay hängt
 * der <c>FullscreenOverlayService</c> ins Vollbild-Element um, es liegt also immer oben.</p>
 *
 * <p>Dazu (Codereview W5 F8-005): eine native Rückfrage lässt sich im Browser für die Seite
 * abschalten („keine weiteren Dialoge“) und liefert danach still `false` — Löschen tat dann nichts —,
 * und ihre Knöpfe stehen in der Browsersprache statt in der App-Sprache. Deshalb läuft JEDE Rückfrage
 * der Oberflächen hierüber; ein Quellscan-Test (<c>NativeConfirmGuardTests</c>) hält das fest.</p>
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
  private labels = inject(CONFIRM_LABELS, { optional: true });

  /** `messageKey` ist ein i18n-Schlüssel; ein bereits übersetzter Text geht genauso durch. */
  ask(messageKey: string, params?: Record<string, unknown>): Observable<boolean> {
    const data: ConfirmData = {
      message: this.translate.instant(messageKey, params),
      confirmLabel: this.labels?.confirm,
      cancelLabel: this.labels?.cancel,
    };
    return this.dialog.open(ConfirmDialogComponent, { data, maxWidth: '32rem' })
      .afterClosed()
      .pipe(map(result => result === true));
  }
}
