import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RepertoireService } from '../../core/repertoire.service';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin, of, catchError, map } from 'rxjs';
import { RepertoireFile } from '../../core/models';
import { SnackbarService } from '../../core/snackbar.service';

@Component({
  selector: 'app-repertoire-edit',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, MatListModule, TranslateModule],
  template: `
    <div class="edit-container">
      <div class="upload-area"
           (dragover)="onDragOver($event)"
           (drop)="onDrop($event)"
           (click)="fileInput.click()">
        <mat-icon>cloud_upload</mat-icon>
        <p>{{ 'repertoire.edit.uploadHint' | translate }}</p>
        <input #fileInput type="file" accept=".pgn" multiple (change)="onFileSelect($event)" hidden>
      </div>

      <h3>{{ 'repertoire.edit.files' | translate: { count: files.length } }}</h3>
      <mat-list>
        @for (file of files; track file.id) {
          <mat-list-item>
            <mat-icon matListItemIcon>description</mat-icon>
            <span matListItemTitle>{{ file.fileName }}</span>
            <span matListItemLine>{{ formatSize(file.fileSize) }} | {{ file.uploadedAt | date }}</span>
            <div matListItemMeta>
              <button mat-icon-button (click)="downloadFile(file.id, file.fileName)">
                <mat-icon>download</mat-icon>
              </button>
              <button mat-icon-button color="warn" (click)="deleteFile(file.id)">
                <mat-icon>delete</mat-icon>
              </button>
            </div>
          </mat-list-item>
        } @empty {
          <p class="empty-text">{{ 'repertoire.edit.empty' | translate }}</p>
        }
      </mat-list>
    </div>
  `,
  styles: [`
    .edit-container { padding: 8px; overflow-y: auto; height: 100%; }
    .upload-area {
      border: 2px dashed #ccc; border-radius: 8px; padding: 2rem;
      text-align: center; cursor: pointer; margin: 0 0 1rem;
      transition: border-color 0.2s;
    }
    .upload-area:hover { border-color: #3f51b5; }
    .upload-area mat-icon { font-size: 48px; width: 48px; height: 48px; color: color-mix(in srgb, currentColor 47%, transparent); }
    .empty-text { padding: 1rem; color: color-mix(in srgb, currentColor 47%, transparent); }
  `]
})
export class RepertoireEditComponent {
  @Input() repertoireId!: number;
  @Input() files: RepertoireFile[] = [];

  @Output() fileUploaded = new EventEmitter<void>();
  @Output() fileDeleted = new EventEmitter<void>();

  constructor(private repertoireService: RepertoireService, private snackbar: SnackbarService, private translate: TranslateService) {}

  onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer?.files) {
      this.uploadFiles(event.dataTransfer.files);
    }
  }

  onFileSelect(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files) {
      this.uploadFiles(input.files);
    }
    // Input zuruecksetzen, damit dieselbe Datei erneut ausgewaehlt werden kann.
    input.value = '';
  }

  private uploadFiles(files: FileList): void {
    const list = Array.from(files);
    if (list.length === 0) return;

    // Client-seitige Validierung: der versteckte Input filtert nur per accept=".pgn",
    // Drag&Drop umgeht das. Nur .pgn-Dateien bis 10 MB hochladen.
    const MAX_BYTES = 10 * 1024 * 1024;
    const valid = list.filter(f => f.name.toLowerCase().endsWith('.pgn') && f.size <= MAX_BYTES);
    const rejected = list.length - valid.length;
    if (rejected > 0)
      this.snackbar.info(this.translate.instant('repertoire.edit.filesSkipped', { count: rejected }));
    if (valid.length === 0) return;

    // Alle Uploads buendeln und den Reload (fileUploaded) GENAU EINMAL nach Abschluss
    // ausloesen statt nach jedem einzelnen Erfolg (vorher: N Reloads + kombinierter
    // PGN-Reload pro Datei). Teilfehler werden pro Datei abgefangen, damit ein
    // fehlgeschlagener Upload die erfolgreichen nicht verwirft.
    const uploads = valid.map(file => {
      const formData = new FormData();
      formData.append('file', file);
      return this.repertoireService.uploadFile(this.repertoireId, formData).pipe(
        map(() => ({ name: file.name, ok: true })),
        catchError((err) => of({ name: file.name, ok: false, error: err?.error?.message || 'Upload failed' }))
      );
    });

    forkJoin(uploads).subscribe(results => {
      const ok = results.filter(r => r.ok).length;
      const failed = results.length - ok;
      const msg = failed === 0
        ? this.translate.instant('repertoire.edit.uploadSuccess', { count: ok })
        : this.translate.instant('repertoire.edit.uploadPartial', { ok, failed });
      this.snackbar.info(msg);
      this.fileUploaded.emit();
    });
  }

  downloadFile(fileId: number, fileName: string): void {
    this.repertoireService.downloadFile(this.repertoireId, fileId).subscribe({
      next: blob => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: () => this.snackbar.info(this.translate.instant('repertoire.edit.downloadFailed')),
    });
  }

  deleteFile(fileId: number): void {
    if (confirm(this.translate.instant('repertoire.edit.deleteConfirm'))) {
      this.repertoireService.deleteFile(this.repertoireId, fileId).subscribe(() => {
        this.fileDeleted.emit();
      });
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }
}
