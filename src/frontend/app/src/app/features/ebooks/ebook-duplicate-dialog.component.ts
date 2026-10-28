import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { EbookDuplicate } from './ebook.service';

export interface EbookDuplicateDialogData {
  /** `certain`: Upload abgelehnt, das Buch gibt es schon (409). `probable`: hochgeladen, aber wahrscheinlich doppelt. */
  mode: 'certain' | 'probable';
  books: EbookDuplicate[];
  /** Titel des gerade hochgeladenen Buchs (Unterzeile). */
  uploadedTitle?: string;
  /** Bei gleicher Datei (`ebook_duplicate_file`) gibt es kein „trotzdem neu“. */
  allowForce?: boolean;
}

/** `open` = zum vorhandenen Buch (`id`), `force` = trotzdem neu hochladen, `keep` = das neue behalten. */
export type EbookDuplicateDialogResult = { action: 'open'; id: number } | { action: 'force' } | { action: 'keep' };

/**
 * Upload-Dublette (0.863.0): „Dieses Buch ist schon in der Bibliothek“ (sicher — ISBN/Text gleich, Upload abgelehnt) bzw.
 * „Ist das eines dieser Bücher?“ (wahrscheinlich — Buch schon angelegt). Je Buch Titel, Autor, Jahr, Formate und der Grund.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-ebook-duplicate-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (certain ? 'ebooks.duplicate.titleCertain' : 'ebooks.duplicate.titleProbable') | translate }}</h2>
    <mat-dialog-content>
      @if (data.uploadedTitle) { <p class="sub">{{ data.uploadedTitle }}</p> }
      <p class="lead">{{ (certain ? 'ebooks.duplicate.introCertain' : 'ebooks.duplicate.introProbable') | translate }}</p>
      <ul class="books">
        @for (b of data.books; track b.id) {
          <li>
            <div class="info">
              <div class="title">{{ b.title }}</div>
              <div class="meta">
                @if (b.author) { <span>{{ b.author }}</span> }
                @if (b.year) { <span>{{ b.year }}</span> }
                @if (b.formats.length) { <span>{{ formats(b) }}</span> }
              </div>
              <div class="why"><mat-icon aria-hidden="true">{{ reasonIcon(b) }}</mat-icon>{{ reasonKey(b) | translate: { pct: pct(b) } }}</div>
            </div>
            <button mat-stroked-button type="button" (click)="choose(b)">
              {{ (certain ? 'ebooks.duplicate.open' : 'ebooks.duplicate.takeExisting') | translate }}
            </button>
          </li>
        }
      </ul>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (certain) {
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        @if (data.allowForce !== false) {
          <button mat-button type="button" (click)="force()">{{ 'ebooks.duplicate.uploadAnyway' | translate }}</button>
        }
      } @else {
        <button mat-flat-button color="primary" type="button" (click)="keep()">{{ 'ebooks.duplicate.keep' | translate }}</button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .sub { margin: -8px 0 8px; font-size: .9rem; color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    .lead { margin: 0 0 8px; }
    .books { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    li { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; padding: 8px 0; border-top: 1px solid var(--mat-sys-outline-variant); }
    .info { flex: 1 1 220px; min-width: 0; }
    .title { font-weight: 500; overflow-wrap: anywhere; }
    .meta { display: flex; flex-wrap: wrap; gap: 0 12px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .why { display: flex; align-items: center; gap: 4px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .why mat-icon { font-size: 16px; width: 16px; height: 16px; }
    mat-dialog-actions { flex-wrap: wrap; row-gap: 4px; }
  `],
})
export class EbookDuplicateDialogComponent {
  readonly data = inject<EbookDuplicateDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<EbookDuplicateDialogComponent, EbookDuplicateDialogResult>);

  get certain(): boolean { return this.data.mode === 'certain'; }

  formats(b: EbookDuplicate): string { return b.formats.map(f => f.toUpperCase()).join(' · '); }

  pct(b: EbookDuplicate): number { return Math.round(b.similarity * 100); }

  reasonKey(b: EbookDuplicate): string {
    switch (b.reason) {
      case 'file': return 'ebooks.duplicate.reasonFile';
      case 'isbn': return 'ebooks.duplicate.reasonIsbn';
      case 'text': return 'ebooks.duplicate.reasonText';
      default: return 'ebooks.duplicate.reasonTitle';
    }
  }

  reasonIcon(b: EbookDuplicate): string {
    return b.reason === 'isbn' ? 'qr_code' : b.reason === 'text' ? 'subject' : b.reason === 'file' ? 'content_copy' : 'title';
  }

  choose(b: EbookDuplicate): void { this.ref.close({ action: 'open', id: b.id }); }
  force(): void { this.ref.close({ action: 'force' }); }
  keep(): void { this.ref.close({ action: 'keep' }); }
}
