import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-repertoire-detail',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatListModule, MatSnackBarModule, LoadingSpinnerComponent],
  template: `
    @if (loading) {
      <app-loading-spinner />
    } @else if (repertoire) {
      <div class="detail-container">
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ repertoire.name }}</mat-card-title>
            <mat-card-subtitle>{{ repertoire.description || 'No description' }} | {{ repertoire.isPublic ? 'Public' : 'Private' }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <div class="upload-area"
                 (dragover)="onDragOver($event)"
                 (drop)="onDrop($event)"
                 (click)="fileInput.click()">
              <mat-icon>cloud_upload</mat-icon>
              <p>Drag & drop PGN files here or click to upload</p>
              <input #fileInput type="file" accept=".pgn" multiple (change)="onFileSelect($event)" hidden>
            </div>

            <h3>Files ({{ repertoire.files.length }})</h3>
            <mat-list>
              @for (file of repertoire.files; track file.id) {
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
          </mat-card-content>
        </mat-card>
      </div>
    }
  `,
  styles: [`
    .detail-container { padding: 2rem; max-width: 800px; margin: 0 auto; }
    .upload-area {
      border: 2px dashed #ccc; border-radius: 8px; padding: 2rem;
      text-align: center; cursor: pointer; margin: 1rem 0;
      transition: border-color 0.2s;
    }
    .upload-area:hover { border-color: #3f51b5; }
    .upload-area mat-icon { font-size: 48px; width: 48px; height: 48px; color: #888; }
    .empty-text { padding: 1rem; color: #888; }
  `]
})
export class RepertoireDetailComponent implements OnInit {
  repertoire: any = null;
  loading = true;
  private id!: number;

  constructor(private route: ActivatedRoute, private http: HttpClient, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.id = +this.route.snapshot.paramMap.get('id')!;
    this.loadRepertoire();
  }

  loadRepertoire(): void {
    this.loading = true;
    this.http.get(`/api/repertoires/${this.id}`).subscribe({
      next: (r) => { this.repertoire = r; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

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
      this.http.post(`/api/repertoires/${this.id}/files`, formData).subscribe({
        next: () => {
          this.loadRepertoire();
          this.snackBar.open(`Uploaded ${files[i].name}`, 'Close', { duration: 2000 });
        },
        error: (err) => this.snackBar.open(err.error?.message || 'Upload failed', 'Close', { duration: 3000 })
      });
    }
  }

  downloadFile(fileId: number, fileName: string): void {
    this.http.get(`/api/repertoires/${this.id}/files/${fileId}`, { responseType: 'blob' }).subscribe(blob => {
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
      this.http.delete(`/api/repertoires/${this.id}/files/${fileId}`).subscribe(() => this.loadRepertoire());
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }
}
