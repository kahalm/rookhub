import { ChangeDetectionStrategy, Component, DestroyRef, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';

export interface ScoresheetPhotoData { gameId: number; }

/**
 * Das Formular-Foto einer eingelesenen Partie (⋮-Menü „Foto anzeigen", 0.529.0). Ein Dialog statt eines neuen
 * Tabs: das Bild kommt über den HttpClient (Anmelde-Token), und ein `window.open` NACH einer Anfrage schluckt
 * der Popup-Blocker.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-scoresheet-photo-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'games.photo.title' | translate }}</h2>
    <div mat-dialog-content class="content">
      @if (url(); as src) {
        <div class="scroll" [class.zoom]="zoom()">
          <img [src]="src" [alt]="'games.photo.title' | translate" (click)="zoom.set(!zoom())" />
        </div>
      } @else if (failed()) {
        <p>{{ 'games.photo.loadError' | translate }}</p>
      } @else {
        <div class="center"><mat-spinner diameter="36"></mat-spinner></div>
      }
    </div>
    <div mat-dialog-actions align="end">
      <button mat-button (click)="zoom.set(!zoom())" [disabled]="!url()">
        <mat-icon>{{ zoom() ? 'zoom_out' : 'zoom_in' }}</mat-icon>
      </button>
      <button mat-button (click)="download()" [disabled]="!url()">
        <mat-icon>download</mat-icon> {{ 'games.photo.download' | translate }}
      </button>
      <button mat-flat-button color="primary" mat-dialog-close>{{ 'common.close' | translate }}</button>
    </div>
  `,
  styles: [`
    .content { padding-top: 4px; }
    .center { display: flex; justify-content: center; padding: 32px; }
    .scroll { max-height: 75vh; overflow: auto; text-align: center; }
    .scroll img { max-width: 100%; max-height: 72vh; object-fit: contain; cursor: zoom-in; }
    .scroll.zoom img { max-width: none; max-height: none; width: 200%; cursor: zoom-out; }
  `],
})
export class ScoresheetPhotoDialogComponent implements OnInit, OnDestroy {
  private data = inject<ScoresheetPhotoData>(MAT_DIALOG_DATA);
  private service = inject(ScoresheetService);
  private destroyRef = inject(DestroyRef);

  readonly url = signal<string | null>(null);
  readonly failed = signal(false);
  readonly zoom = signal(false);
  private blob: Blob | null = null;

  ngOnInit(): void {
    this.service.photo(this.data.gameId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => { this.blob = blob; this.url.set(URL.createObjectURL(blob)); },
      error: () => this.failed.set(true),
    });
  }

  ngOnDestroy(): void {
    const u = this.url();
    if (u) URL.revokeObjectURL(u);
  }

  download(): void {
    if (this.blob) openPhotoBlob(this.blob, photoFileName(this.data.gameId, this.blob));
  }

  /** Öffnet den Dialog — geteilt von Partienliste und Partieseite. */
  static open(dialog: MatDialog, gameId: number): void {
    dialog.open(ScoresheetPhotoDialogComponent, { data: { gameId }, width: '960px', maxWidth: '96vw' });
  }
}
