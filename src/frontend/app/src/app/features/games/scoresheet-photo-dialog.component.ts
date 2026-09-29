import { ChangeDetectionStrategy, Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe } from '@ngx-translate/core';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';

export interface ScoresheetPhotoData { gameId: number; /** Welche Seite zuerst (ab 1). */ page?: number; }

/**
 * Das Formular-Foto einer eingelesenen Partie (⋮-Menü „Foto anzeigen", 0.529.0). Ein Dialog statt eines neuen
 * Tabs: das Bild kommt über den HttpClient (Anmelde-Token), und ein `window.open` NACH einer Anfrage schluckt
 * der Popup-Blocker.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-scoresheet-photo-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatButtonToggleModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'games.photo.title' | translate }}</h2>
    <div mat-dialog-content class="content">
      <!-- Formular über mehrere Blätter (0.600.0): die Seitenzahl kommt mit dem ersten Foto (Header X-Page-Count). -->
      @if (pageCount() > 1) {
        <mat-button-toggle-group class="pager" [value]="page()" (change)="show($event.value)" hideSingleSelectionIndicator>
          @for (n of pageNumbers(); track n) {
            <mat-button-toggle [value]="n">{{ 'scoresheet.page' | translate: { n } }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
      }
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
    .pager { margin-bottom: 8px; }
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
  readonly page = signal(1);
  readonly pageCount = signal(1);
  readonly pageNumbers = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i + 1));
  /** Geladene Seiten (Blob + Adresse) — Zurückblättern lädt nicht neu. */
  private readonly loaded = new Map<number, { blob: Blob; url: string }>();

  ngOnInit(): void {
    this.show(Math.max(1, this.data.page ?? 1));
  }

  ngOnDestroy(): void {
    for (const p of this.loaded.values()) URL.revokeObjectURL(p.url);
  }

  show(page: number): void {
    this.page.set(page);
    const have = this.loaded.get(page);
    if (have) { this.url.set(have.url); return; }
    this.url.set(null);
    this.failed.set(false);
    this.service.photoPage(this.data.gameId, page).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => {
        const entry = { blob: r.blob, url: URL.createObjectURL(r.blob) };
        this.loaded.set(page, entry);
        this.pageCount.set(r.pageCount);
        if (this.page() === page) this.url.set(entry.url);
      },
      error: () => { if (this.page() === page) this.failed.set(true); },
    });
  }

  download(): void {
    const p = this.loaded.get(this.page());
    if (p) openPhotoBlob(p.blob, photoFileName(this.data.gameId, p.blob, this.page()));
  }

  /** Öffnet den Dialog — geteilt von Partienliste, Partieseite und Korrekturseite (dort mit der gezeigten Seite). */
  static open(dialog: MatDialog, gameId: number, page = 1): void {
    dialog.open(ScoresheetPhotoDialogComponent, { data: { gameId, page }, width: '960px', maxWidth: '96vw' });
  }
}
