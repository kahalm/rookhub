import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { LineupsApiService, MovesKey, formatMoves } from '../core/lineups';
import { MovesEditorComponent } from './moves-editor.component';

/**
 * Die ersten Züge an einer Partie (2026-10-08): zeigt „1.e4 c5 2.Sf3 …" und — wer darf — „Züge eingeben" bzw. „ändern",
 * das den Zug-Editor öffnet. Genutzt in den Aufstellungen je Runde und an den Paarungen der eigenen Begegnung.
 * Mit `replaced` (0.724.0: am Brett liegt die Partie vor) nur noch grau „ersetzt durch die Partie" — keine Eingabe, kein
 * „ändern", mit `canDelete` ein „Löschen".
 */
@Component({
  selector: 'lh-board-moves',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MovesEditorComponent],
  template: `
    @if (replaced()) {
      @if (moves()) {
        <span class="bm bm-replaced">
          <span class="bm-moves">{{ shown() }}</span>
          <span class="bm-note">ersetzt durch die Partie</span>
          @if (canDelete()) {
            <button type="button" class="btn-link bm-del" [disabled]="busy()" (click)="remove()"
                    [attr.aria-label]="'Eingegebene Züge löschen, ' + title()">Löschen</button>
          }
          @if (error()) { <span class="err">{{ error() }}</span> }
        </span>
      }
    } @else if (moves() || canEdit()) {
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
    .bm-edit, .bm-del { white-space: nowrap; }
    .bm-replaced { color: var(--muted); }
    .bm-replaced .bm-moves { text-decoration: line-through; }
    .bm-note { font-size: 13px; font-style: italic; }
  `],
})
export class BoardMovesComponent {
  readonly key = input.required<MovesKey>();
  readonly title = input('');
  readonly initial = input<string | null>(null);
  readonly canEdit = input(false);
  readonly flipped = input(false);
  /** Am Brett liegt die Partie vor — der Handeintrag ist nur noch Geschichte (0.724.0). */
  readonly replaced = input(false);
  readonly canDelete = input(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  private readonly api = inject(LineupsApiService);
  /** Die aktuell gespeicherten Züge — folgt dem Eingang, bis hier gespeichert wird. */
  readonly moves = linkedSignal(() => this.initial());
  readonly shown = computed(() => formatMoves(this.moves()));
  readonly editing = signal(false);

  async remove(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.deleteMoves(this.key());
      this.moves.set(null);
    } catch {
      this.error.set('Löschen hat nicht geklappt.');
    } finally {
      this.busy.set(false);
    }
  }

  onSaved(moves: string | null): void {
    this.moves.set(moves);
    this.editing.set(false);
  }
}
