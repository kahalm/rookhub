import { ChangeDetectionStrategy, Component, ElementRef, Input, OnChanges, computed, signal, viewChild } from '@angular/core';
import { BoardArrow, ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { GameReviewComponent } from '@rh/features/games/game-review.component';
import { BoardBadge } from '@rh/features/games/move-badge.util';
import { ParsedGame, START_FEN, parsePgnText } from '@rh/shared/pgn-viewer/pgn-parser';
import { de, pgnDate } from '@lh/core/league-format';

/**
 * Eine Partie nachspielen (Spielerkarte → „Letzte Partien", Wunsch 2026-09-28: „die letzten Partien sollen auch klickbar
 * sein"): Brett, Züge in deutscher Notation, Blättern mit Knöpfen, Pfeiltasten und Klick auf einen Zug. Das Brett steht
 * aus Sicht des Spielers der Karte (`flipped`).
 *
 * <p>Mit `evalsUrl` (Vereinspartien, 0.593.0) steht darunter der Rückblick aus RookHub — Bewertungskurve, Genauigkeit,
 * Zug-Klassen, Computer-Linien — aus der Hintergrund-Analyse der Partie; Pfeil und Zug-Symbol kommen auf dieses Brett.
 * Ohne die Adresse (Spielerkarte) bleibt es beim bloßen Nachspielen.</p>
 */
@Component({
  selector: 'lh-game-replay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, GameReviewComponent],
  template: `
    <div class="replay" #box tabindex="0" (keydown)="key($event)" aria-label="Partie nachspielen">
      <p class="replay-head"><b>{{ head('White') }} – {{ head('Black') }}</b>
        <span class="muted">{{ meta() }}</span></p>
      @if (game(); as g) {
        @if (g.moves.length) {
          <div class="replay-body">
            <div class="replay-board">
              <app-chess-board [fen]="fen()" [lastMove]="last()" [flipped]="flipped"
                               [arrows]="evalsUrl ? arrows() : []" [badge]="evalsUrl ? badge() : null" />
              <div class="replay-nav" role="group" aria-label="Züge">
                <button type="button" class="btn-sec" aria-label="Zum Anfang" [disabled]="index() < 0" (click)="index.set(-1)">⏮</button>
                <button type="button" class="btn-sec" aria-label="Zug zurück" [disabled]="index() < 0" (click)="step(-1)">◀</button>
                <button type="button" class="btn-sec" aria-label="Zug vor" [disabled]="index() >= g.moves.length - 1" (click)="step(1)">▶</button>
                <button type="button" class="btn-sec" aria-label="Zum Ende" [disabled]="index() >= g.moves.length - 1"
                        (click)="index.set(g.moves.length - 1)">⏭</button>
              </div>
            </div>
            <div class="replay-moves">
              <ol>
                @for (p of pairs(); track p.n) {
                  <li><span class="muted">{{ p.n }}.</span>
                    <button type="button" class="mv" [class.on]="index() === p.w" (click)="index.set(p.w)">{{ p.ws }}</button>
                    @if (p.b >= 0) {
                      <button type="button" class="mv" [class.on]="index() === p.b" (click)="index.set(p.b)">{{ p.bs }}</button>
                    }
                  </li>
                }
              </ol>
              <p class="muted small">{{ result() }}</p>
              @if (comment()) { <p class="replay-comment small">{{ comment() }}</p> }
            </div>
          </div>
          @if (evalsUrl) {
            <app-game-review class="replay-review" [evalsUrl]="evalsUrl" [fens]="g.fens" [moves]="g.moves"
                             [currentIndex]="index()" [withExplanations]="false"
                             (moveClicked)="index.set($event)" (arrowsChange)="arrows.set($event)"
                             (badgeChange)="badge.set($event)" />
          }
        } @else {
          <p class="muted">Diese Partie hat keine Züge.</p>
        }
      } @else {
        <p class="muted">Diese Partie lässt sich nicht lesen.</p>
      }
    </div>
  `,
})
export class GameReplayComponent implements OnChanges {
  @Input({ required: true }) pgn = '';
  @Input() flipped = false;
  /** `GET …/evals` einer analysierten Partie — dann mit Rückblick; `null` = nur nachspielen. */
  @Input() evalsUrl: string | null = null;

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  readonly game = signal<ParsedGame | null>(null);
  /** Halbzug auf dem Brett, -1 = Ausgangsstellung. */
  readonly index = signal(-1);
  /** Bester Zug und Zug-Klasse aus dem Rückblick (nur mit `evalsUrl`). */
  readonly arrows = signal<BoardArrow[]>([]);
  readonly badge = signal<BoardBadge | null>(null);

  readonly fen = computed(() => {
    const g = this.game();
    return g ? g.fens[this.index() + 1] ?? g.fens[0] : START_FEN;
  });

  readonly last = computed<[string, string] | undefined>(() => {
    const g = this.game(), i = this.index();
    return g && i >= 0 && g.moves[i] ? [g.moves[i].from, g.moves[i].to] : undefined;
  });

  /** Züge paarweise; beginnt die Partie mit Schwarz am Zug, steht Weiß leer. */
  readonly pairs = computed(() => {
    const g = this.game();
    if (!g?.moves.length) return [];
    const blackFirst = g.moves[0].color === 'b';
    const firstNo = Number(g.fens[0].split(' ')[5]) || 1;
    const out: { n: number; w: number; ws: string; b: number; bs: string }[] = [];
    let i = 0;
    if (blackFirst) {
      out.push({ n: firstNo, w: -1, ws: '…', b: 0, bs: de(g.moves[0].san) });
      i = 1;
    }
    for (; i < g.moves.length; i += 2) {
      out.push({
        n: firstNo + out.length, w: i, ws: de(g.moves[i].san),
        b: i + 1 < g.moves.length ? i + 1 : -1, bs: i + 1 < g.moves.length ? de(g.moves[i + 1].san) : '',
      });
    }
    return out;
  });

  readonly comment = computed(() => this.game()?.comments[this.index()] ?? '');
  readonly result = computed(() => {
    const r = this.game()?.headers['Result'] ?? '*';
    return r === '*' ? '' : r === '1/2-1/2' ? '½–½' : r.replace('-', '–');
  });
  readonly meta = computed(() => {
    const h = this.game()?.headers ?? {};
    return [h['Event'] && h['Event'] !== '?' ? h['Event'] : '', pgnDate(h['Date'] ?? '')].filter(x => x).join(' · ');
  });

  ngOnChanges(): void {
    let g: ParsedGame | null = null;
    try { g = parsePgnText(this.pgn)[0] ?? null; } catch { g = null; }
    this.game.set(g);
    this.index.set(-1);
    setTimeout(() => this.box()?.nativeElement.focus({ preventScroll: true }));
  }

  head(k: 'White' | 'Black'): string {
    const v = this.game()?.headers[k];
    return v && v !== '?' ? v : '?';
  }

  step(d: number): void {
    const n = this.game()?.moves.length ?? 0;
    this.index.set(Math.max(-1, Math.min(n - 1, this.index() + d)));
  }

  key(ev: KeyboardEvent): void {
    const n = this.game()?.moves.length ?? 0;
    const map: Record<string, () => void> = {
      ArrowLeft: () => this.step(-1), ArrowRight: () => this.step(1),
      Home: () => this.index.set(-1), End: () => this.index.set(n - 1),
    };
    const fn = map[ev.key];
    if (!fn) return;
    ev.preventDefault();
    fn();
  }
}
