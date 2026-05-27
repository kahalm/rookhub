import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RepertoireFile } from '../../core/models';

@Component({
  selector: 'app-repertoire-edit',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, MatListModule, MatSnackBarModule],
  template: `
    <div class="edit-container">
      <div class="upload-area"
           (dragover)="onDragOver($event)"
           (drop)="onDrop($event)"
           (click)="fileInput.click()">
        <mat-icon>cloud_upload</mat-icon>
        <p>Drag & drop PGN files here or click to upload</p>
        <input #fileInput type="file" accept=".pgn" multiple (change)="onFileSelect($event)" hidden>
      </div>

      <h3>Files ({{ files.length }})</h3>
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
          <p class="empty-text">No files yet. Upload PGN files to get started!</p>
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
    .upload-area mat-icon { font-size: 48px; width: 48px; height: 48px; color: #888; }
    .empty-text { padding: 1rem; color: #888; }
  `]
})
export class RepertoireEditComponent {
  @Input() repertoireId!: number;
  @Input() files: RepertoireFile[] = [];

  @Output() fileUploaded = new EventEmitter<void>();
  @Output() fileDeleted = new EventEmitter<void>();

  constructor(private http: HttpClient, private snackBar: MatSnackBar) {}

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
  }

  private uploadFiles(files: FileList): void {
    for (let i = 0; i < files.length; i++) {
      const formData = new FormData();
      formData.append('file', files[i]);
      this.http.post(`/api/repertoires/${this.repertoireId}/files`, formData).subscribe({
        next: () => {
          this.snackBar.open(`Uploaded ${files[i].name}`, 'Close', { duration: 2000 });
          this.fileUploaded.emit();
        },
        error: (err) => this.snackBar.open(err.error?.message || 'Upload failed', 'Close', { duration: 3000 })
      });
    }
  }

  downloadFile(fileId: number, fileName: string): void {
    this.http.get(`/api/repertoires/${this.repertoireId}/files/${fileId}`, { responseType: 'blob' }).subscribe(blob => {
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      a.click();
      window.URL.revokeObjectURL(url);
    });
  }

  deleteFile(fileId: number): void {
    if (confirm('Delete this file?')) {
      this.http.delete(`/api/repertoires/${this.repertoireId}/files/${fileId}`).subscribe(() => {
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
