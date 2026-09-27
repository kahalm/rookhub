import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { GamesService, PgnImportResult } from './games.service';

/** Größer nimmt der Server nicht (`SavedGameService.MaxImportChars`) — vorher abfangen spart den Upload. */
export const MAX_PGN_CHARS = 5_000_000;

/**
 * „PGN hochladen" auf `/games` (0.553.0): eine Datei wählen ODER das PGN einfügen — die Datei landet im Textfeld,
 * man sieht also, was hochgeht. Auch mehrere Partien auf einmal. Ging alles durch, schließt der Dialog mit dem
 * Ergebnis; scheiterten einzelne Partien, bleibt er offen und nennt sie (Nummer, Spieler, Grund).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-pgn-import-dialog',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressBarModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'games.pgnUpload.title' | translate }}</h2>
    <div mat-dialog-content class="content">
      <p class="hint">{{ 'games.pgnUpload.hint' | translate }}</p>
      <input #file type="file" accept=".pgn,.txt,application/x-chess-pgn,text/plain" hidden (change)="onFile($event)" />
      <button mat-stroked-button (click)="file.click()" [disabled]="busy()">
        <mat-icon>upload_file</mat-icon> {{ 'games.pgnUpload.file' | translate }}
      </button>
      @if (fileName()) { <span class="file-name">{{ fileName() }}</span> }
      <mat-form-field appearance="outline" class="text">
        <mat-label>{{ 'games.pgnUpload.paste' | translate }}</mat-label>
        <textarea matInput rows="10" [ngModel]="text()" (ngModelChange)="text.set($event); error.set(null)"
                  [disabled]="busy()" spellcheck="false"></textarea>
      </mat-form-field>
      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error(); as e) { <p class="error">{{ 'games.pgnUpload.error.' + e | translate }}</p> }
      @if (result(); as r) {
        <p class="summary">{{ 'games.pgnUpload.summary' | translate: { imported: r.imported, duplicates: r.duplicates, failed: r.failed.length } }}</p>
        @if (r.truncated) { <p class="error">{{ 'games.pgnUpload.truncated' | translate }}</p> }
        @if (r.failed.length) {
          <strong>{{ 'games.pgnUpload.failedTitle' | translate }}</strong>
          <ul class="failed">
            @for (f of r.failed; track f.index) {
              <li>#{{ f.index }} {{ f.white || '?' }} – {{ f.black || '?' }}: {{ 'games.pgnUpload.reason.' + f.reason | translate }}</li>
            }
          </ul>
        }
      }
    </div>
    <div mat-dialog-actions align="end">
      @if (result()) {
        <button mat-flat-button (click)="ref.close(result() ?? undefined)">{{ 'common.close' | translate }}</button>
      } @else {
        <button mat-button (click)="ref.close()">{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button (click)="upload()" [disabled]="busy() || !text().trim()">
          <mat-icon>cloud_upload</mat-icon> {{ 'games.pgnUpload.import' | translate }}
        </button>
      }
    </div>
  `,
  styles: [`
    .content { display: flex; flex-direction: column; gap: 8px; min-width: min(560px, 86vw); }
    .hint { margin: 0; font-size: 0.9rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .file-name { font-size: 0.85rem; }
    .text { width: 100%; }
    .text textarea { font-family: ui-monospace, monospace; font-size: 0.85rem; }
    .error { color: var(--mat-sys-error, #c62828); margin: 0; }
    .summary { margin: 0; font-weight: 500; }
    .failed { margin: 0; padding-left: 20px; font-size: 0.9rem; }
  `],
})
export class PgnImportDialogComponent {
  readonly ref = inject<MatDialogRef<PgnImportDialogComponent, PgnImportResult | undefined>>(MatDialogRef);
  private games = inject(GamesService);

  readonly text = signal('');
  readonly fileName = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly result = signal<PgnImportResult | null>(null);

  async onFile(e: Event): Promise<void> {
    const input = e.target as HTMLInputElement;
    const f = input.files?.[0];
    input.value = '';                       // dieselbe Datei noch einmal wählbar
    if (!f) return;
    if (f.size > MAX_PGN_CHARS * 4) { this.error.set('tooLarge'); return; }
    this.fileName.set(f.name);
    this.text.set(await f.text());
    this.error.set(null);
  }

  upload(): void {
    const pgn = this.text().trim();
    if (!pgn) { this.error.set('empty'); return; }
    if (pgn.length > MAX_PGN_CHARS) { this.error.set('tooLarge'); return; }
    this.busy.set(true);
    this.error.set(null);
    this.games.importPgn(pgn).subscribe({
      next: r => {
        this.busy.set(false);
        if (r.failed.length || r.truncated) this.result.set(r);   // offen lassen: der Nutzer soll sehen, was fehlt
        else this.ref.close(r);
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        const reason = err.error?.reason;
        this.error.set(reason === 'empty' || reason === 'tooLarge' ? reason : 'failed');
      },
    });
  }
}
