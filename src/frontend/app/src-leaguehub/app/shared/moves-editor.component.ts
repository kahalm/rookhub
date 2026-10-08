import { ChangeDetectionStrategy, Component, ElementRef, HostListener, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ChessBoardComponent, UserBoardMove } from '@rh/shared/pgn-viewer/chess-board.component';
import { LineupsApiService, MAX_PLIES, MovesKey, fenAfter, formatMoves, lastMoveOf, movesErrorText, parseMoves } from '../core/lineups';

/**
 * Die ersten Züge einer Ligapartie eingeben (2026-10-08): Brett zum Klicken UND Textfeld zum Tippen/Einfügen („e4 c5 Sf3",
 * mit oder ohne Zugnummern, deutsche oder englische Buchstaben) — beide bleiben gleich: ein Zug am Brett schreibt den Text
 * neu, getippter Text stellt das Brett (bis zum ersten falschen Zug). Zurück, Speichern, Löschen. Gespeichert wird über
 * `PUT …/moves`; der Server prüft noch einmal.
 */
@Component({
  selector: 'lh-moves-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent],
  template: `
    <div class="me-back" (click)="closed.emit()"></div>
    <section class="me-dialog" role="dialog" aria-modal="true" aria-labelledby="me-title">
      <h2 id="me-title" class="me-title">Züge · {{ title() }}</h2>
      <div class="me-body">
        <div class="me-board">
          <app-chess-board [fen]="fen()" [lastMove]="last()" [flipped]="flipped()" [playable]="!busy() && plies().length < max"
                           (userMove)="onBoardMove($event)" />
        </div>
        <div class="me-side">
          <label class="me-label" for="me-text">Züge (z. B. „1.e4 c5 2.Sf3 d6")</label>
          <textarea id="me-text" class="me-text" rows="4" spellcheck="false" autocapitalize="off" autocomplete="off"
                    [value]="text()" (input)="onType($any($event.target).value)"></textarea>
          <p class="me-hint muted">{{ plies().length }} von höchstens {{ max }} Halbzügen. Deutsche (S, L, T, D) oder englische
            Buchstaben, mit oder ohne Zugnummern.</p>
          @if (problem(); as p) { <p class="err" role="alert">{{ p }}</p> }
          <div class="me-actions">
            <button type="button" class="btn-sec" (click)="back()" [disabled]="busy() || !plies().length">← Zurück</button>
            <button type="button" class="btn-sec me-save" (click)="save()" [disabled]="busy() || !!parseError()">Speichern</button>
            @if (initial()) {
              <button type="button" class="btn-link" (click)="remove()" [disabled]="busy()">Löschen</button>
            }
            <button type="button" class="btn-link" (click)="closed.emit()">Abbrechen</button>
          </div>
        </div>
      </div>
    </section>
  `,
  styles: [`
    :host { position: fixed; inset: 0; z-index: 1000; display: flex; align-items: flex-start; justify-content: center; overflow-y: auto; padding: 24px 12px; }
    .me-back { position: fixed; inset: 0; background: rgba(0, 0, 0, .45); }
    .me-dialog { position: relative; background: var(--surface, #fff); color: var(--ink); border-radius: 10px; padding: 16px;
      width: min(760px, 100%); box-shadow: 0 10px 40px rgba(0, 0, 0, .3); }
    .me-title { margin: 0 0 12px; font: 600 20px/1.2 var(--cond); overflow-wrap: anywhere; }
    .me-body { display: grid; grid-template-columns: minmax(0, 340px) minmax(0, 1fr); gap: 16px; }
    .me-board { width: 100%; max-width: 340px; }
    .me-label { display: block; font: 600 14px/1.2 var(--cond); color: var(--muted); margin-bottom: 4px; }
    .me-text { width: 100%; box-sizing: border-box; font: 16px/1.4 var(--body); padding: 8px; border: 1.5px solid var(--line); border-radius: 6px;
      background: var(--paper, #fff); color: var(--ink); resize: vertical; }
    .me-hint { font-size: 13px; margin: 4px 0 8px; }
    .me-actions { display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: center; }
    @media (max-width: 640px) {
      :host { padding: 8px; }
      .me-body { grid-template-columns: minmax(0, 1fr); }
      .me-board { max-width: none; }
    }
  `],
})
export class MovesEditorComponent implements OnInit {
  private readonly api = inject(LineupsApiService);
  private readonly host = inject(ElementRef);

  readonly key = input.required<MovesKey>();
  /** „Brett 2: Ackermann – Brunner". */
  readonly title = input('');
  /** Die schon hinterlegten Züge (englische SAN mit Leerzeichen). */
  readonly initial = input<string | null>(null);
  /** Brett aus Sicht von Schwarz (der eigene Spieler hatte Schwarz). */
  readonly flipped = input(false);
  /** Gespeichert: die neuen Züge (englische SAN) oder `null` = gelöscht. */
  readonly saved = output<string | null>();
  readonly closed = output<void>();

  readonly max = MAX_PLIES;
  readonly plies = signal<string[]>([]);
  readonly text = signal('');
  readonly busy = signal(false);
  readonly serverError = signal<string | null>(null);
  /** Fehler im getippten Text (das Brett zeigt die Züge davor). */
  readonly parseError = signal<string | null>(null);
  readonly problem = computed(() => this.parseError() ?? this.serverError());
  readonly fen = computed(() => fenAfter(this.plies()));
  readonly last = computed(() => lastMoveOf(this.plies()));

  ngOnInit(): void {
    const p = parseMoves(this.initial() ?? '');
    this.plies.set(p.sans);
    this.text.set(formatMoves(p.sans));
    setTimeout(() => (this.host.nativeElement as HTMLElement).querySelector<HTMLTextAreaElement>('.me-text')?.focus());
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (!this.busy()) this.closed.emit();
  }

  /** Getippt: das Brett folgt bis zum ersten falschen Zug, der Text bleibt, wie er getippt ist. */
  onType(value: string): void {
    this.text.set(value);
    this.serverError.set(null);
    const p = parseMoves(value);
    this.plies.set(p.sans);
    this.parseError.set(p.error ? movesErrorText(p.error) : null);
  }

  /** Am Brett gezogen: anhängen, Text neu schreiben. */
  onBoardMove(m: UserBoardMove): void {
    if (this.plies().length >= this.max) return;
    this.setPlies([...this.plies(), m.san]);
  }

  back(): void {
    this.setPlies(this.plies().slice(0, -1));
  }

  private setPlies(sans: string[]): void {
    this.plies.set(sans);
    this.text.set(formatMoves(sans));
    this.parseError.set(null);
    this.serverError.set(null);
  }

  async save(): Promise<void> {
    await this.send(this.plies().join(' '));
  }

  async remove(): Promise<void> {
    await this.send('');
  }

  private async send(moves: string): Promise<void> {
    this.busy.set(true);
    this.serverError.set(null);
    try {
      this.saved.emit(await this.api.saveMoves(this.key(), moves));
    } catch (err) {
      const status = err instanceof HttpErrorResponse ? err.status : 0;
      this.serverError.set(status
        ? movesErrorText(err instanceof HttpErrorResponse ? err.error : null, status)
        : 'Keine Verbindung — bitte noch einmal versuchen.');
    } finally {
      this.busy.set(false);
    }
  }
}
