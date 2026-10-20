import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** Antwort der Rückfrage: ein Diagramm fehlt, der Text ist falsch — oder nichts (Abbrechen, Esc, daneben klicken). */
export type MissedAnswer = 'diagram' | 'text' | undefined;

/**
 * Rückfrage beim Doppelklick auf eine Buchstelle ohne erkanntes Diagramm (0.788.0; drei Antworten seit 20.10.2026):
 * „Fehlt ein Diagramm?“ → `'diagram'`, „Text falsch“ → `'text'`, „Abbrechen“/Esc/daneben → `undefined` (nichts gespeichert).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-ebook-missed-diagram-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'ebooks.reader.missedTitle' | translate }}</h2>
    <mat-dialog-content><p class="hint">{{ 'ebooks.reader.missedHint' | translate }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="undefined">{{ 'common.cancel' | translate }}</button>
      <button mat-stroked-button [mat-dialog-close]="'text'"><mat-icon>text_fields</mat-icon>{{ 'ebooks.reader.missedText' | translate }}</button>
      <button mat-flat-button color="primary" [mat-dialog-close]="'diagram'" cdkFocusInitial><mat-icon>grid_on</mat-icon>{{ 'ebooks.reader.missedYes' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`.hint { margin: 0; opacity: .8; }`],
})
export class EbookMissedDiagramDialogComponent {}
