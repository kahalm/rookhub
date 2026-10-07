import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { CommentSegment } from './comment-variation.util';

/** Ab dieser Länge (Zeichen aller Absätze zusammen) bietet der Löser den Kommentar zusätzlich im Fenster an —
 *  etwa so viel, wie in der Seitenleiste sechs Zeilen füllt (Wunsch 2026-10-07: „länger als Dear Reader, When
 *  my friend … thus"). Kürzere Kommentare bleiben ohne Klick. */
export const LONG_COMMENT_CHARS = 300;

/** Gesamtlänge eines aufgelösten Kommentars (Text + Zug-Chips). */
export function commentLength(blocks: CommentSegment[][]): number {
  let n = 0;
  for (const block of blocks) for (const seg of block) n += (seg.text ?? seg.move ?? '').length;
  return n;
}

export interface CommentDialogData {
  title: string | null;
  subtitle: string | null;
  blocks: CommentSegment[][];
}

/**
 * Ein langer Kurs-Kommentar (Vorwort, Kapitel-Einleitung) im Fenster: mehr Platz, größere Schrift, ruhige
 * Zeilenlänge. Der Löser zeigt den Kommentar weiter VOLLSTÄNDIG (der Nutzer soll nicht klicken müssen) — das
 * Fenster ist ein Angebot. Ein Zug im Text schließt es und gibt das Segment zurück: der Löser spielt die
 * Variante dann am Brett vor, genau wie beim Klick im Kasten.
 */
@Component({
  selector: 'app-comment-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="cd-head">
      <div class="cd-titles">
        @if (data.title) { <h2 class="cd-title">{{ data.title }}</h2> }
        @if (data.subtitle) { <p class="cd-sub">{{ data.subtitle }}</p> }
      </div>
      <button mat-icon-button type="button" mat-dialog-close [attr.aria-label]="'common.close' | translate">
        <mat-icon>close</mat-icon>
      </button>
    </div>
    <mat-dialog-content class="cd-body">
      @for (block of data.blocks; track $index) {
        <p>@for (seg of block; track $index) {@if (seg.move) {<button type="button" class="cmt-move" (click)="pick(seg)">{{ seg.move }}</button>} @else {<span>{{ seg.text }}</span>}}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (hasMoves) { <span class="cd-hint">{{ 'book.commentDialog.moveHint' | translate }}</span> }
      <button mat-flat-button type="button" mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .cd-head { display: flex; align-items: flex-start; gap: 0.5rem; padding: 1rem 0.75rem 0.25rem 1.5rem; }
    .cd-titles { flex: 1; min-width: 0; }
    .cd-title { margin: 0; font-size: 1.25rem; font-weight: 600; }
    .cd-sub { margin: 0.15rem 0 0; font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .cd-body { max-width: 68ch; }
    .cd-body p { margin: 0 0 0.9rem; font-size: 1rem; line-height: 1.6; white-space: pre-wrap; overflow-wrap: break-word; }
    .cd-hint { flex: 1; font-size: 0.8rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .cmt-move {
      font: inherit; font-weight: 600; cursor: pointer; color: var(--mat-sys-primary, #1565c0);
      background: color-mix(in srgb, currentColor 10%, transparent);
      border: 1px solid color-mix(in srgb, currentColor 35%, transparent);
      border-radius: 4px; padding: 0 4px; margin: 0 1px; white-space: nowrap;
    }
  `],
})
export class CommentDialogComponent {
  readonly data = inject<CommentDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<CommentDialogComponent, CommentSegment | undefined>);
  readonly hasMoves = this.data.blocks.some(b => b.some(s => !!s.move));

  pick(seg: CommentSegment): void { this.ref.close(seg); }
}
