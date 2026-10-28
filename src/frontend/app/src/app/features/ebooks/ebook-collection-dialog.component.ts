import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { EbookCollection, EbookService } from './ebook.service';

export interface EbookCollectionDialogData { ebookIds: number[]; }

/** „Zu Sammlung …“ (0.787.0): ausgewählte Bücher einer bestehenden oder neuen Sammlung zuordnen — verschieben oder
 *  (0.861.0) zusätzlich. */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-collection-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatRadioModule, MatSelectModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'ebooks.collection.assignTitle' | translate }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ (data.ebookIds.length === 1 ? 'ebooks.bulk.forCountOne' : 'ebooks.bulk.forCount') | translate: { n: data.ebookIds.length } }}</p>
      <mat-radio-group [(ngModel)]="mode" class="modes">
        <mat-radio-button value="new">{{ 'ebooks.collection.new' | translate }}</mat-radio-button>
        @if (collections().length > 0) {
          <mat-radio-button value="existing">{{ 'ebooks.collection.existing' | translate }}</mat-radio-button>
        }
      </mat-radio-group>
      @if (mode === 'new') {
        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'ebooks.collection.name' | translate }}</mat-label>
          <input matInput [(ngModel)]="name" maxlength="200" cdkFocusInitial />
        </mat-form-field>
      } @else {
        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'ebooks.collection.label' | translate }}</mat-label>
          <mat-select [(ngModel)]="collectionId">
            @for (c of collections(); track c.id) { <mat-option [value]="c.id">{{ c.name }} ({{ c.bookCount }})</mat-option> }
          </mat-select>
        </mat-form-field>
      }
      <mat-checkbox [(ngModel)]="additional">{{ 'ebooks.collection.additional' | translate }}</mat-checkbox>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="busy()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="busy() || !valid">
        <mat-icon>folder</mat-icon>{{ 'common.save' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .modes { display: flex; flex-direction: column; margin-bottom: 8px; }
    .full { width: 100%; }
    .hint { margin: 0 0 8px; font-size: .85rem; opacity: .7; }
  `],
})
export class EbookCollectionDialogComponent implements OnInit {
  readonly data = inject<EbookCollectionDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<EbookCollectionDialogComponent, EbookCollection | null>);
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  readonly collections = signal<EbookCollection[]>([]);
  readonly busy = signal(false);
  mode: 'new' | 'existing' = 'new';
  name = '';
  collectionId: number | null = null;
  /** 0.861.0: Bücher bleiben in ihrer bisherigen Sammlung und stehen zusätzlich in der gewählten. */
  additional = false;

  ngOnInit(): void {
    this.ebooks.collections().subscribe({ next: c => this.collections.set(c), error: () => {} });
  }

  get valid(): boolean { return this.mode === 'new' ? this.name.trim().length > 0 : this.collectionId != null; }

  save(): void {
    if (!this.valid) return;
    this.busy.set(true);
    const existing = this.mode === 'existing' ? this.collectionId : null;
    const newName = this.mode === 'new' ? this.name.trim() : null;
    this.ebooks.assignCollection(this.data.ebookIds, existing, newName, { additional: this.additional }).subscribe({
      next: c => this.ref.close(c),
      error: err => { this.busy.set(false); this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.saveFailed')); },
    });
  }
}
