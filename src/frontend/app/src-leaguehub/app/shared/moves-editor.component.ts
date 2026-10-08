import { ChangeDetectionStrategy, Component, ElementRef, HostListener, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ChessBoardComponent, UserBoardMove } from '@rh/shared/pgn-viewer/chess-board.component';
import { LineupsApiService, MAX_PLIES, MovesKey, fenAfter, formatMoves, lastMoveOf, movesErrorText, parseMoves } from '../core/lineups';
import {
  ExplorerPath, ExplorerPathsResult, ExplorerPathsService, MAX_SEARCH_PLIES, PIECES, START_PLACEMENT, Side, boardFromPlacement,
  blackOf, composeFen, emptyBoard, movePiece, placePiece, removePiece, formatGames, formatShare, parseFenInput, pathsErrorText, positionProblem,
} from '../core/position-setup';
import { SetupBoardComponent, pieceName, pieceSrc } from './setup-board.component';

/**
 * Die ersten Züge einer Ligapartie eingeben (2026-10-08): Brett zum Klicken UND Textfeld zum Tippen/Einfügen („e4 c5 Sf3",
 * mit oder ohne Zugnummern, deutsche oder englische Buchstaben) — beide bleiben gleich: ein Zug am Brett schreibt den Text
 * neu, getippter Text stellt das Brett (bis zum ersten falschen Zug). Zurück, Speichern, Löschen. Gespeichert wird über
 * `PUT …/moves`; der Server prüft noch einmal.
 *
 * Zweiter Modus „Stellung" (2026-10-08, Wunsch: „lass mich dort auch direkt Stellungen eingeben, schau in der lokalen
 * Lichess-DB nach, welche Eröffnungen am häufigsten zu der Stellung kommen, und schlag sie mir vor"): FEN-Feld und
 * Aufstell-Brett laufen synchron, „Zugfolgen vorschlagen" fragt `GET /api/explorer/paths`; ein Vorschlag wird in den
 * Modus „Züge" übernommen und wie gewohnt gespeichert.
 */
@Component({
  selector: 'lh-moves-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, SetupBoardComponent],
  template: `
    <div class="me-back" (click)="closed.emit()"></div>
    <section class="me-dialog" role="dialog" aria-modal="true" aria-labelledby="me-title">
      <h2 id="me-title" class="me-title">Züge · {{ title() }}</h2>
      <div class="me-modes" role="tablist" aria-label="Eingabe">
        <button type="button" role="tab" class="me-mode" [class.on]="mode() === 'moves'" [attr.aria-selected]="mode() === 'moves'"
                (click)="setMode('moves')">Züge</button>
        <button type="button" role="tab" class="me-mode" [class.on]="mode() === 'position'" [attr.aria-selected]="mode() === 'position'"
                (click)="setMode('position')">Stellung</button>
      </div>
      @if (mode() === 'moves') {
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
      } @else {
        <div class="me-body">
          <div class="me-board">
            <lh-setup-board #sb [board]="setup()" [flipped]="flipped()" [altPlace]="!!brush()" (squareClick)="onSquare($event)"
                           (squareAltClick)="onSquare($event, true)" (pieceMoved)="onPieceMoved($event)"
                           (pieceDropped)="onPieceDropped($event)" (pieceRemoved)="onPieceRemoved($event)" />
            <div class="me-palette" role="toolbar" aria-label="Figur zum Setzen wählen">
              @for (p of pieces; track p) {
                <button type="button" class="me-pc" [class.on]="brush() === p" [attr.aria-pressed]="brush() === p"
                        [attr.aria-label]="pieceName(p)" [title]="pieceName(p)" (click)="pick(p)"
                        (pointerdown)="sb.paletteDown(p, $event)">
                  <img [src]="pieceSrc(p)" alt="" draggable="false" />
                </button>
              }
              <button type="button" class="me-pc me-erase" [class.on]="brush() === 'x'" [attr.aria-pressed]="brush() === 'x'"
                      aria-label="Löschen: Feld leeren" title="Löschen: Feld leeren" (click)="pick('x')">✕</button>
            </div>
            <p class="me-hint muted">{{ brush() ? (brush() === 'x' ? 'Feld antippen = leeren.' : 'Feld antippen = ' + pieceName(brush()!) + ' setzen.')
              : 'Erst eine Figur (oder ✕) wählen, dann ein Feld antippen.' }}
              @if (brush() && brush() !== 'x') { Rechtsklick/langer Druck: in Schwarz setzen. }
              Figuren lassen sich auch ziehen (auch aus der Palette); außerhalb des Bretts loslassen = entfernen.</p>
          </div>
          <div class="me-side">
            <label class="me-label" for="me-fen">FEN (z. B. aus Lichess oder chess.com einfügen)</label>
            <input id="me-fen" class="me-text me-fen" type="text" spellcheck="false" autocapitalize="off" autocomplete="off"
                   [value]="fenText()" (input)="onFenInput($any($event.target).value)" />
            @if (fenError()) { <p class="err" role="alert">Diese FEN lässt sich nicht lesen — das Brett zeigt die letzte lesbare Stellung.</p> }
            <div class="me-row" role="radiogroup" aria-label="Seite am Zug">
              <button type="button" class="me-mode" role="radio" [class.on]="side() === 'w'" [attr.aria-checked]="side() === 'w'"
                      (click)="setSide('w')">Weiß am Zug</button>
              <button type="button" class="me-mode" role="radio" [class.on]="side() === 'b'" [attr.aria-checked]="side() === 'b'"
                      (click)="setSide('b')">Schwarz am Zug</button>
            </div>
            <div class="me-row">
              <button type="button" class="btn-link" (click)="startPosition()">Grundstellung</button>
              <button type="button" class="btn-link" (click)="clearBoard()">Brett leeren</button>
            </div>
            <p class="me-hint muted">Die Zugzahl ist bei einer von Hand aufgebauten Stellung unbekannt; die Suche prüft bis
              {{ searchPlies }} Halbzüge.</p>
            @if (setupProblem(); as sp) { <p class="err" role="alert">{{ sp }}</p> }
            @if (localExplorer() !== false) {
              <button type="button" class="btn-sec me-suggest" (click)="suggest()"
                      [disabled]="searching() || !!setupProblem() || fenError()">Zugfolgen vorschlagen</button>
            } @else {
              <p class="muted">Vorschläge gibt es nur mit dem lokalen Eröffnungs-Explorer — auf diesem Server ist keiner eingerichtet.</p>
            }
            @if (searching()) {
              <p class="me-searching muted" role="status"><span class="me-spin" aria-hidden="true"></span>Suche im Eröffnungs-Explorer …
                (bis zu 20 Sekunden)</p>
            }
            @if (searchError(); as e) { <p class="err" role="alert">{{ e }}</p> }
            @if (result(); as r) {
              <div class="me-result">
                @if (r.opening) { <p class="me-opening"><b>{{ r.opening.eco }}</b> {{ r.opening.name }}</p> }
                @if (r.failed) {
                  <p class="err">Der lokale Eröffnungs-Explorer hat nicht geantwortet — bitte später noch einmal versuchen.</p>
                } @else if (!r.paths.length && r.games === 0) {
                  <p class="muted me-none">Der Explorer kennt diese Stellung nicht (0 Partien) — Figuren und Seite am Zug prüfen.</p>
                } @else if (!r.paths.length) {
                  <p class="muted me-none">Stellung bekannt ({{ games(r.games) }}), aber keine Zugfolge innerhalb von {{ searchPlies }} Halbzügen gefunden.</p>
                } @else {
                  <p class="me-hint muted">Antippen übernimmt die Zugfolge.</p>
                  <ol class="me-paths">
                    @for (p of r.paths; track $index) {
                      <li>
                        <button type="button" class="me-path" (click)="takePath(p)">
                          <span class="me-path-moves">{{ show(p.moves) }}</span>
                          <span class="me-path-games muted">{{ games(p.estGames) }} · {{ share(p.share) }} der Partien in dieser Stellung</span>
                        </button>
                      </li>
                    }
                  </ol>
                }
                @if (r.truncated && !r.failed) { <p class="me-hint muted me-truncated">Suche am Budget abgebrochen — vielleicht gibt es mehr.</p> }
              </div>
            }
            <div class="me-actions">
              <button type="button" class="btn-link" (click)="closed.emit()">Abbrechen</button>
            </div>
          </div>
        </div>
      }
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
    .me-modes { display: flex; gap: 6px; margin: 0 0 12px; }
    .me-mode { font: 600 15px/1.2 var(--cond); padding: 6px 14px; min-height: 36px; border: 1.5px solid var(--line); border-radius: 18px;
      background: transparent; color: var(--ink); cursor: pointer; }
    .me-mode.on { background: var(--ink); color: var(--paper, #fff); border-color: var(--ink); }
    .me-row { display: flex; flex-wrap: wrap; gap: 6px 12px; align-items: center; margin: 8px 0; }
    .me-fen { font: 14px/1.3 ui-monospace, monospace; }
    .me-palette { display: grid; grid-template-columns: repeat(7, 1fr); gap: 4px; margin-top: 8px; }
    .me-pc { touch-action: none; aspect-ratio: 1; padding: 2px; border: 1.5px solid var(--line); border-radius: 6px; background: var(--paper, #fff);
      cursor: pointer; display: flex; align-items: center; justify-content: center; font-size: 18px; color: var(--ink); min-height: 36px; }
    .me-pc img { width: 100%; height: 100%; }
    .me-pc.on { border-color: var(--red); box-shadow: 0 0 0 2px var(--red) inset; }
    .me-suggest { margin: 4px 0 8px; }
    .me-searching { display: flex; align-items: center; gap: 8px; }
    .me-spin { width: 14px; height: 14px; border-radius: 50%; border: 2px solid var(--line); border-top-color: var(--ink);
      animation: me-spin .8s linear infinite; flex: none; }
    @keyframes me-spin { to { transform: rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .me-spin { animation: none; } }
    .me-opening { margin: 4px 0 6px; }
    .me-paths { list-style: none; margin: 0 0 8px; padding: 0; display: grid; gap: 6px; }
    .me-path { width: 100%; text-align: left; display: grid; gap: 2px; padding: 8px 10px; border: 1px solid var(--line);
      border-radius: 6px; background: var(--paper, #fff); color: var(--ink); cursor: pointer; font: 15px/1.35 var(--body); }
    .me-path:hover, .me-path:focus-visible { border-color: var(--ink); }
    .me-path-games { font-size: 13px; }
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

  // ---- Modus „Stellung" ----
  private readonly explorer = inject(ExplorerPathsService);
  readonly pieces = PIECES;
  readonly searchPlies = MAX_SEARCH_PLIES;
  readonly mode = signal<'moves' | 'position'>('moves');
  readonly setup = signal<readonly string[]>(boardFromPlacement(START_PLACEMENT)!);
  readonly side = signal<Side>('w');
  readonly fenText = signal(composeFen(this.setup(), 'w'));
  readonly fenError = signal(false);
  /** Gewählte Figur („K" … „p") oder „x" = löschen. */
  readonly brush = signal<string | null>(null);
  readonly setupProblem = computed(() => positionProblem(this.setup(), this.side()));
  /** `null` = noch nicht gefragt, sonst ob der Server einen lokalen Explorer hat. */
  readonly localExplorer = signal<boolean | null>(null);
  readonly searching = signal(false);
  readonly searchError = signal<string | null>(null);
  readonly result = signal<ExplorerPathsResult | null>(null);
  private searchSeq = 0;
  readonly pieceSrc = pieceSrc;
  readonly pieceName = pieceName;

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

  setMode(mode: 'moves' | 'position'): void {
    if (mode === this.mode()) return;
    if (mode === 'position') {
      // Startet mit der Stellung nach den eingegebenen Zügen.
      const [placement, side] = this.fen().split(' ');
      this.setup.set(boardFromPlacement(placement) ?? boardFromPlacement(START_PLACEMENT)!);
      this.side.set(side === 'b' ? 'b' : 'w');
      this.syncFen();
      if (this.localExplorer() === null) void this.explorer.hasLocal().then(v => this.localExplorer.set(v));
    }
    this.mode.set(mode);
  }

  pick(p: string): void {
    this.brush.set(this.brush() === p ? null : p);
  }

  /**
   * Feld angeklickt: die gewählte Figur setzen (dieselbe Figur noch einmal = Feld leeren, ✕ = leeren). `black` (Rechtsklick
   * bzw. langer Druck) setzt denselben Figurentyp in Schwarz.
   */
  onSquare(index: number, black = false): void {
    const b = this.brush();
    if (!b) return;
    const piece = b === 'x' ? '' : black ? blackOf(b) : b;
    const board = this.setup();
    this.setup.set(!piece || board[index] === piece ? removePiece(board, index) : placePiece(board, index, piece));
    this.syncFen();
  }

  /** Am Aufstell-Brett gezogen. */
  onPieceMoved(e: { from: number; to: number }): void {
    this.setup.set(movePiece(this.setup(), e.from, e.to));
    this.syncFen();
  }

  /** Aus der Palette aufs Brett gezogen. */
  onPieceDropped(e: { piece: string; to: number }): void {
    this.setup.set(placePiece(this.setup(), e.to, e.piece));
    this.syncFen();
  }

  /** Vom Brett weggezogen. */
  onPieceRemoved(e: { from: number }): void {
    this.setup.set(removePiece(this.setup(), e.from));
    this.syncFen();
  }

  setSide(side: Side): void {
    this.side.set(side);
    this.syncFen();
  }

  startPosition(): void {
    this.setup.set(boardFromPlacement(START_PLACEMENT)!);
    this.side.set('w');
    this.syncFen();
  }

  clearBoard(): void {
    this.setup.set(emptyBoard());
    this.syncFen();
  }

  /** FEN getippt/eingefügt: lesbar → Brett und Seite folgen, der Text bleibt, wie er getippt ist. */
  onFenInput(value: string): void {
    this.fenText.set(value);
    const parsed = parseFenInput(value);
    this.fenError.set(!parsed);
    if (!parsed) return;
    this.setup.set(parsed.board);
    this.side.set(parsed.side);
    this.clearResult();
  }

  private syncFen(): void {
    this.fenText.set(composeFen(this.setup(), this.side()));
    this.fenError.set(false);
    this.clearResult();
  }

  private clearResult(): void {
    this.result.set(null);
    this.searchError.set(null);
  }

  async suggest(): Promise<void> {
    if (this.setupProblem() || this.fenError()) return;
    const seq = ++this.searchSeq;
    this.searching.set(true);
    this.clearResult();
    try {
      const r = await this.explorer.paths(composeFen(this.setup(), this.side()));
      if (seq === this.searchSeq) this.result.set(r);
    } catch (err) {
      if (seq !== this.searchSeq) return;
      if (err instanceof HttpErrorResponse && (err.error as { reason?: string } | null)?.reason === 'noLocalExplorer') this.localExplorer.set(false);
      this.searchError.set(pathsErrorText(err));
    } finally {
      if (seq === this.searchSeq) this.searching.set(false);
    }
  }

  /** Vorschlag übernehmen: die Zugfolge kommt in den Modus „Züge" (Brett zeigt die Stellung, Speichern wie gewohnt). */
  takePath(p: ExplorerPath): void {
    this.setPlies(p.moves.slice(0, this.max));
    this.mode.set('moves');
  }

  show(moves: readonly string[]): string {
    return formatMoves(moves);
  }

  games(n: number): string {
    return formatGames(n);
  }

  share(x: number): string {
    return formatShare(x);
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
