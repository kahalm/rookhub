import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpEventType } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { EBOOK_ACCEPT, Ebook, EbookGroupOption, EbookMeta, EbookService, formatBytes } from './ebook.service';

export interface EbookEditDialogData {
  /** Vorhandenes Buch (bearbeiten) oder `null` (hochladen). */
  ebook: Ebook | null;
  groups: EbookGroupOption[];
  maxFileBytes: number;
}

/**
 * Ein Dialog für beides: „Hochladen" (Datei + Metadaten + Sichtbarkeit) und „Bearbeiten" (ohne Datei). Schließt mit
 * dem gespeicherten Buch; beim Hochladen zeigt der Balken den Fortschritt, weil ein Buch Minuten brauchen kann.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-edit-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressBarModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (isNew ? 'ebooks.dialog.uploadTitle' : 'ebooks.dialog.editTitle') | translate }}</h2>
    <mat-dialog-content>
      @if (isNew) {
        <div class="file-row">
          <input #fileInput type="file" [accept]="accept" hidden (change)="onFile($event)" />
          <button mat-stroked-button type="button" (click)="fileInput.click()" [disabled]="busy()">
            <mat-icon>attach_file</mat-icon>{{ 'ebooks.dialog.chooseFile' | translate }}
          </button>
          <span class="file-name">
            @if (file) { {{ file.name }} · {{ size(file.size) }} } @else { {{ 'ebooks.dialog.noFile' | translate }} }
          </span>
        </div>
        <p class="hint">{{ 'ebooks.dialog.fileHint' | translate: { max: size(data.maxFileBytes) } }}</p>
      }

      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'ebooks.meta.title' | translate }}</mat-label>
        <input matInput [(ngModel)]="title" maxlength="300" required />
      </mat-form-field>
      <div class="two">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'ebooks.meta.author' | translate }}</mat-label>
          <input matInput [(ngModel)]="author" maxlength="300" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="year">
          <mat-label>{{ 'ebooks.meta.year' | translate }}</mat-label>
          <input matInput type="number" [(ngModel)]="year" min="1400" max="2200" />
        </mat-form-field>
      </div>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'ebooks.meta.tags' | translate }}</mat-label>
        <input matInput [(ngModel)]="tags" maxlength="600" [placeholder]="'ebooks.meta.tagsHint' | translate" />
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'ebooks.meta.description' | translate }}</mat-label>
        <textarea matInput [(ngModel)]="description" maxlength="2000" rows="2"></textarea>
      </mat-form-field>

      <div class="groups">
        <div class="groups-title">{{ 'ebooks.visibilityLabel' | translate }}</div>
        @if (data.groups.length === 0) {
          <p class="hint">{{ 'ebooks.visibility.noGroups' | translate }}</p>
        } @else {
          @for (g of data.groups; track g.id) {
            <mat-checkbox [checked]="selected.has(g.id)" (change)="toggle(g.id, $event.checked)">
              {{ g.isEveryone ? ('ebooks.visibility.everyone' | translate) : g.name }}
            </mat-checkbox>
          }
        }
        <p class="hint">{{ (selected.size === 0 ? 'ebooks.visibility.onlyMeHint' : 'ebooks.visibility.groupsHint') | translate }}</p>
      </div>

      @if (progress() !== null) {
        <mat-progress-bar [mode]="progress()! >= 100 ? 'indeterminate' : 'determinate'" [value]="progress()!"></mat-progress-bar>
        <p class="hint">{{ 'ebooks.dialog.uploading' | translate: { pct: progress() } }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="busy()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="busy() || !canSave">
        <mat-icon>{{ isNew ? 'upload' : 'save' }}</mat-icon>{{ (isNew ? 'ebooks.upload' : 'common.save') | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { display: flex; flex-direction: column; gap: 4px; max-height: 70vh; }
    .full { width: 100%; }
    .two { display: grid; grid-template-columns: 1fr minmax(110px, 140px); gap: 8px; }
    .file-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 4px; }
    .file-name { flex: 1 1 160px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .9rem; }
    .hint { margin: 0 0 8px; font-size: .85rem; opacity: .7; }
    .groups { display: flex; flex-direction: column; margin-top: 4px; }
    .groups-title { font-weight: 500; margin-bottom: 2px; }
    @media (max-width: 480px) { .two { grid-template-columns: 1fr; } }
  `],
})
export class EbookEditDialogComponent {
  readonly data = inject<EbookEditDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<EbookEditDialogComponent, Ebook | null>);
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  readonly accept = EBOOK_ACCEPT;
  readonly isNew = this.data.ebook == null;
  file: File | null = null;
  title = this.data.ebook?.title ?? '';
  author = this.data.ebook?.author ?? '';
  year: number | null = this.data.ebook?.year ?? null;
  tags = (this.data.ebook?.tags ?? []).join(', ');
  description = this.data.ebook?.description ?? '';
  selected = new Set<number>(this.data.ebook?.groupIds ?? []);
  readonly busy = signal(false);
  readonly progress = signal<number | null>(null);

  get canSave(): boolean {
    return this.title.trim().length > 0 && (!this.isNew || this.file != null);
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    if (file.size > this.data.maxFileBytes) {
      this.snackbar.warn(this.translate.instant('ebooks.dialog.fileTooLarge', { max: this.size(this.data.maxFileBytes) }));
      return;
    }
    this.file = file;
    if (!this.title.trim()) this.title = file.name.replace(/\.[^.]+$/, '').replace(/_/g, ' ').trim();
  }

  toggle(groupId: number, checked: boolean): void {
    if (checked) this.selected.add(groupId); else this.selected.delete(groupId);
  }

  size(bytes: number): string { return formatBytes(bytes, this.translate.currentLang() ?? undefined); }

  private meta(): EbookMeta {
    return {
      title: this.title.trim(),
      author: this.author.trim(),
      year: this.year ?? null,
      tags: this.tags.split(/[,;]/).map(t => t.trim()).filter(t => t.length > 0),
      description: this.description.trim(),
      groupIds: [...this.selected],
    };
  }

  save(): void {
    if (!this.canSave) return;
    this.busy.set(true);
    if (this.isNew && this.file) {
      this.progress.set(0);
      this.ebooks.upload(this.file, this.meta()).subscribe({
        next: event => {
          if (event.type === HttpEventType.UploadProgress && event.total) this.progress.set(Math.round(100 * event.loaded / event.total));
          else if (event.type === HttpEventType.Response) this.ref.close(event.body);
        },
        error: err => this.fail(err),
      });
    } else if (this.data.ebook) {
      // PUT: `year: null` heißt „keine Angabe" — das Backend sieht null als „unverändert", deshalb 0 schicken (→ wird null).
      const m = this.meta();
      this.ebooks.update(this.data.ebook.id, { ...m, year: m.year ?? 0 }).subscribe({
        next: saved => this.ref.close(saved),
        error: err => this.fail(err),
      });
    }
  }

  private fail(err: unknown): void {
    this.busy.set(false);
    this.progress.set(null);
    this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.saveFailed'));
  }
}
