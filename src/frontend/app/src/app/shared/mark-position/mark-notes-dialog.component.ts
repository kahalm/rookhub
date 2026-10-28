import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { MarkOrigin, MarkedPositionsService } from './marked-positions.service';
import { MarkNotesEditorComponent } from './mark-notes-editor.component';

export interface MarkNotesDialogData {
  fen: string;
  origin: MarkOrigin;
}

/** Wie das Fenster endete: gespeichert, Markierung entfernt oder abgebrochen (`undefined`). */
export type MarkNotesDialogResult = 'saved' | 'removed' | undefined;

/**
 * Das Fenster am „+"-Knopf (0.862.0, Wunsch 2026-10-27: „wenn ich auf das + klicke, sollte ich Tags vergeben können,
 * außerdem Kommentare dazu abgeben"): Schlagworte + Kommentar der gerade markierten Stellung, dazu „Markierung entfernen".
 * Kurzes Formular → 420 px (docs/DESIGN.md 5.6).
 */
@Component({
  selector: 'app-mark-notes-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, TranslatePipe, MarkNotesEditorComponent],
  template: `
    <h2 mat-dialog-title>{{ 'markPosition.title' | translate }}</h2>
    <mat-dialog-content>
      <app-mark-notes-editor [(tags)]="tags" [(comment)]="comment" [(editing)]="editing" [fen]="fen" />
      @if (error()) { <p class="err" role="alert">{{ 'markPosition.saveFailed' | translate }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions>
      <button mat-button type="button" class="remove" [disabled]="busy()" (click)="remove()">{{ 'markPosition.remove' | translate }}</button>
      <span class="spacer"></span>
      @if (editing()) {
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button color="primary" type="button" class="save" [disabled]="busy()" (click)="save()">{{ 'common.save' | translate }}</button>
      } @else {
        <button mat-button type="button" class="close" mat-dialog-close>{{ 'common.close' | translate }}</button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .spacer { flex: 1; }
    .remove { color: var(--mat-sys-error); }
    .err { color: var(--mat-sys-error); margin: 8px 0 0; }
  `],
})
export class MarkNotesDialogComponent {
  private readonly data = inject<MarkNotesDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<MarkNotesDialogComponent, MarkNotesDialogResult>>(MatDialogRef);
  private readonly service = inject(MarkedPositionsService);

  readonly tags = signal<string[]>(this.service.notesOf(this.data.fen).tags);
  readonly comment = signal(this.service.notesOf(this.data.fen).comment ?? '');
  readonly fen = this.data.fen;
  /** Lesen oder Bearbeiten (0.865.0) — der Editor entscheidet beim Öffnen, der Bleistift schaltet um. */
  readonly editing = signal(false);
  readonly busy = signal(false);
  readonly error = signal(false);

  save(): void {
    this.busy.set(true);
    this.error.set(false);
    const comment = this.comment().trim();
    this.service.setNotes(this.data.fen, { tags: this.tags(), comment: comment || null }).subscribe({
      next: () => this.ref.close('saved'),
      error: () => { this.busy.set(false); this.error.set(true); },
    });
  }

  remove(): void {
    this.busy.set(true);
    this.service.toggle(this.data.fen, this.data.origin).subscribe({
      next: () => this.ref.close('removed'),
      error: () => { this.busy.set(false); this.error.set(true); },
    });
  }
}
