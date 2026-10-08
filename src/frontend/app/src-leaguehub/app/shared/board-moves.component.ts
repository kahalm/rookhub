import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, signal } from '@angular/core';
import { MovesKey, formatMoves } from '../core/lineups';
import { MovesEditorComponent } from './moves-editor.component';

/**
 * Die ersten Züge an einer Partie (2026-10-08): zeigt „1.e4 c5 2.Sf3 …" und — wer darf — „Züge eingeben" bzw. „ändern",
 * das den Zug-Editor öffnet. Genutzt in den Aufstellungen je Runde und an den Paarungen der eigenen Begegnung.
 */
@Component({
  selector: 'lh-board-moves',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MovesEditorComponent],
  template: `
    @if (moves() || canEdit()) {
      <span class="bm">
        @if (moves()) { <span class="bm-moves">{{ shown() }}</span> }
        @if (canEdit()) {
          <button type="button" class="btn-link bm-edit" (click)="editing.set(true)"
                  [attr.aria-label]="(moves() ? 'Züge ändern' : 'Züge eingeben') + ', ' + title()">{{ moves() ? 'Züge ändern' : 'Züge eingeben' }}</button>
        }
      </span>
    }
    @if (editing()) {
      <lh-moves-editor [key]="key()" [title]="title()" [initial]="moves()" [flipped]="flipped()"
                       (saved)="onSaved($event)" (closed)="editing.set(false)" />
    }
  `,
  styles: [`
    .bm { display: inline-flex; flex-wrap: wrap; align-items: baseline; gap: 2px 8px; font-size: 14px; }
    .bm-moves { font-family: var(--body); overflow-wrap: anywhere; }
    .bm-edit { white-space: nowrap; }
  `],
})
export class BoardMovesComponent {
  readonly key = input.required<MovesKey>();
  readonly title = input('');
  readonly initial = input<string | null>(null);
  readonly canEdit = input(false);
  readonly flipped = input(false);
  /** Die aktuell gespeicherten Züge — folgt dem Eingang, bis hier gespeichert wird. */
  readonly moves = linkedSignal(() => this.initial());
  readonly shown = computed(() => formatMoves(this.moves()));
  readonly editing = signal(false);

  onSaved(moves: string | null): void {
    this.moves.set(moves);
    this.editing.set(false);
  }
}
