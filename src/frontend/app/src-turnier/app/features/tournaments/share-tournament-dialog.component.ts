import { AfterViewInit, Component, ElementRef, Inject, ChangeDetectionStrategy, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { QrCodeComponent } from '@rh/shared/qr-code/qr-code.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-share-tournament-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, TranslatePipe, QrCodeComponent],
  template: `
    <!-- d8-t-dialog-titel-fett: Titel/Inhalt/Knöpfe aus dem Baukasten (DESIGN 5.6) -->
    <h2 mat-dialog-title>{{ 'tournaments.share.title' | translate }}</h2>
    <mat-dialog-content>
      <div class="qr-container">
        <app-qr-code [data]="data.url" [width]="220" />
      </div>
      <div class="link-row">
        <!-- d8-t-teilen-link-feld-abgeschnitten: beim Antippen ganz markiert (zum Kopieren), sonst steht der Anfang „https://“ -->
        <input class="link-input" [value]="data.url" readonly #linkInput (focus)="linkInput.select()" />
        <button mat-icon-button [attr.aria-label]="'tournaments.share.copyLink' | translate" (click)="copyLink()">
          <mat-icon>content_copy</mat-icon>
        </button>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .qr-container { display: flex; justify-content: center; margin-bottom: 1rem; }
    .link-row { display: flex; align-items: center; gap: 0.5rem; }
    /* d8-t-share-url-feld-hell: Fläche, Schrift und Rahmen nur aus Tokens (ohne hellen Festwert-Rückfall); hinten mit „…“
       gekürzt statt vorne abgeschnitten. */
    .link-input {
      flex: 1; min-width: 0; padding: 0.5rem; border: 1px solid var(--mat-sys-outline-variant); border-radius: 4px;
      font-size: 0.85rem; background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface);
      text-overflow: ellipsis;
    }
    /* iOS Safari zoomt den Viewport, sobald ein Feld unter 16px den Fokus bekommt: der Dialog sprang beim Tipp ins
       Link-Feld und die Seite blieb nach dem Schliessen gezoomt. Desktop behaelt 0.85rem; min-width: 0 haelt den
       Flex-Input bei 16px in der Zeile. */
    @media (max-width: 768px) { .link-input { font-size: 16px; } }
  `]
})
export class ShareTournamentDialogComponent implements AfterViewInit {
  private readonly linkInput = viewChild<ElementRef<HTMLInputElement>>('linkInput');

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: { url: string },
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  /** d8-t-teilen-link-abgeschnitten: das Feld steht am Anfang, auch falls es doch den Fokus bekam (Einfügemarke am Ende). */
  ngAfterViewInit(): void {
    const el = this.linkInput()?.nativeElement;
    if (el) el.scrollLeft = 0;
  }

  copyLink(): void {
    if (navigator.clipboard?.writeText) {
      navigator.clipboard.writeText(this.data.url).then(() => {
        this.snackbar.copy(this.translate.instant('tournaments.share.linkCopied'));
      }).catch(() => this.fallbackCopy());
    } else {
      this.fallbackCopy();
    }
  }

  private fallbackCopy(): void {
    const textarea = document.createElement('textarea');
    textarea.value = this.data.url;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    try {
      document.execCommand('copy');
      this.snackbar.copy(this.translate.instant('tournaments.share.linkCopied'));
    } catch {
      this.snackbar.copy(this.translate.instant('tournaments.share.copyFailed'));
    }
    document.body.removeChild(textarea);
  }
}
