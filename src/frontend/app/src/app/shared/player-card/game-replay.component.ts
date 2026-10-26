import { ChangeDetectionStrategy, Component, ElementRef, Input, OnChanges, SimpleChanges, computed, inject, signal, viewChild } from '@angular/core';
import { Chess } from 'chess.js';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { BoardArrow, ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { GameReviewComponent } from '@rh/features/games/game-review.component';
import { BoardBadge } from '@rh/shared/pgn-viewer/board-badge';
import { ParsedGame, START_FEN, parsePgnText } from '@rh/shared/pgn-viewer/pgn-parser';
import { cardDate, cardLang, cardSan } from './card-lang';

/**
 * Eine Partie nachspielen (Spielerkarte → „Letzte Partien", Wunsch 2026-09-28: „die letzten Partien sollen auch klickbar
 * sein"): Brett, Züge in der Notation der Kartensprache (LeagueHub deutsch), Blättern mit Knöpfen, Pfeiltasten und Klick auf einen Zug. Das Brett steht
 * aus Sicht des Spielers der Karte (`flipped`).
 *
 * <p>Mit `evalsUrl` (Vereinspartien, 0.593.0) steht darunter der Rückblick aus RookHub — Bewertungskurve, Genauigkeit,
 * Zug-Klassen, Computer-Linien — aus der Hintergrund-Analyse der Partie; Pfeil und Zug-Symbol kommen auf dieses Brett.
 * Ohne die Adresse (Spielerkarte) bleibt es beim bloßen Nachspielen.</p>
 *
 * <p>Mit `marks` (0.848.0, LeagueHub-Vereinspartien, Wunsch 2026-10-26: „in der Partie Sprungmarken zu den interessanten
 * Stellungen — nicht jede muss in der Partie vorkommen, kann auch in einer Variation markiert werden") steht unter der Zugliste
 * „Interessante Stellungen": eine Marke springt in der Partie an ihren Halbzug; eine Marke aus einer Variante springt an den
 * Abzweig und stellt die markierte Stellung aufs Brett (darüber die Variante, „Zurück zur Partie"). In der Zugliste trägt jeder
 * Zug mit einer Marke (bzw. einem Abzweig) einen Punkt.</p>
 */
@Component({
  selector: 'lh-game-replay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, GameReviewComponent, TranslatePipe],
  template: `
    <div class="replay" #box tabindex="0" (keydown)="key($event)" [attr.aria-label]="'playerCard.replayGame' | translate">
      <p class="replay-head"><b>{{ head('White') }} – {{ head('Black') }}</b>
        <span class="muted">{{ meta() }}</span></p>
      @if (game(); as g) {
        @if (g.moves.length) {
          <div class="replay-body">
            <div class="replay-board">
              <app-chess-board [fen]="fen()" [lastMove]="last()" [flipped]="flipped"
                               [arrows]="evalsUrl ? arrows() : []" [badge]="evalsUrl ? badge() : null" />
              @if (activeSide(); as sd) {
                <p class="replay-side small">{{ sd.line ? ('playerCard.markVariation' | translate: { line: sd.line }) : ('playerCard.markInVariation' | translate) }}
                  <button type="button" class="btn-link" (click)="side.set(null)">{{ 'playerCard.backToGame' | translate }}</button></p>
              }
              <div class="replay-nav" role="group" [attr.aria-label]="'playerCard.movesGroup' | translate">
                <button type="button" class="btn-sec" [attr.aria-label]="'playerCard.toStart' | translate" [disabled]="index() < 0" (click)="index.set(-1)">⏮</button>
                <button type="button" class="btn-sec" [attr.aria-label]="'playerCard.stepBack' | translate" [disabled]="index() < 0" (click)="step(-1)">◀</button>
                <button type="button" class="btn-sec" [attr.aria-label]="'playerCard.stepForward' | translate" [disabled]="index() >= g.moves.length - 1" (click)="step(1)">▶</button>
                <button type="button" class="btn-sec" [attr.aria-label]="'playerCard.toEnd' | translate" [disabled]="index() >= g.moves.length - 1"
                        (click)="index.set(g.moves.length - 1)">⏭</button>
              </div>
            </div>
            <div class="replay-moves">
              <ol>
                @for (p of pairs(); track p.n) {
                  <li><span class="muted">{{ p.n }}.</span>
                    <button type="button" class="mv" [class.on]="index() === p.w" [class.marked]="markedIdx().has(p.w)" (click)="index.set(p.w)">{{ p.ws }}</button>
                    @if (p.b >= 0) {
                      <button type="button" class="mv" [class.on]="index() === p.b" [class.marked]="markedIdx().has(p.b)" (click)="index.set(p.b)">{{ p.bs }}</button>
                    }
                  </li>
                }
              </ol>
              <p class="muted small">{{ result() }}</p>
              @if (markList().length) {
                <div class="replay-marks" role="group" [attr.aria-label]="'playerCard.marksTitle' | translate">
                  <span class="muted small">{{ 'playerCard.marksTitle' | translate }}:</span>
                  @for (m of markList(); track m.key) {
                    <button type="button" class="btn-link mark-jump" [class.on]="m.active" (click)="jump(m.mark)"
                            [title]="m.count > 1 ? ('playerCard.markedTimes' | translate: { count: m.count }) : ''">{{ m.label }}@if (m.count > 1) {<span class="muted"> ×{{ m.count }}</span>}</button>
                  }
                </div>
              }
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
          <p class="muted">{{ 'playerCard.noMovesGame' | translate }}</p>
        }
      } @else {
        <p class="muted">{{ 'playerCard.unreadable' | translate }}</p>
      }
    </div>
  `,
  styles: [`
    .replay-marks { display: flex; flex-wrap: wrap; align-items: baseline; gap: 2px 10px; margin: 6px 0 0; }
    .mark-jump.on { font-weight: 700; }
    .mv.marked::after { content: ''; display: inline-block; width: 6px; height: 6px; margin-left: 3px; border-radius: 50%;
      background: var(--rh-success, #2e7d32); vertical-align: middle; }
    .replay-side { margin: 6px 0; }
  `],
})
export class GameReplayComponent implements OnChanges {
  @Input({ required: true }) pgn = '';
  @Input() flipped = false;
  /** `GET …/evals` einer analysierten Partie — dann mit Rückblick; `null` = nur nachspielen. */
  @Input() evalsUrl: string | null = null;
  /** Interessante Stellungen der Partie (0.848.0) — leer = keine Sprungmarken. */
  @Input() marks: readonly ReplayMark[] = [];
  private readonly marksSig = signal<readonly ReplayMark[]>([]);
  /** Stellung aus einer Variante auf dem Brett — gilt nur, solange der Partiezug ihr Abzweig ist (`at`). */
  readonly side = signal<{ at: number; fen: string; line: string } | null>(null);
  readonly activeSide = computed(() => { const s = this.side(); return s && s.at === this.index() ? s : null; });

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  readonly game = signal<ParsedGame | null>(null);
  /** Halbzug auf dem Brett, -1 = Ausgangsstellung. */
  readonly index = signal(-1);
  /** Bester Zug und Zug-Klasse aus dem Rückblick (nur mit `evalsUrl`). */
  readonly arrows = signal<BoardArrow[]>([]);
  readonly badge = signal<BoardBadge | null>(null);

  readonly fen = computed(() => {
    const side = this.activeSide();
    if (side) return side.fen;
    const g = this.game();
    return g ? g.fens[this.index() + 1] ?? g.fens[0] : START_FEN;
  });

  readonly last = computed<[string, string] | undefined>(() => {
    if (this.activeSide()) return undefined;
    const g = this.game(), i = this.index();
    return g && i >= 0 && g.moves[i] ? [g.moves[i].from, g.moves[i].to] : undefined;
  });

  /** Züge paarweise; beginnt die Partie mit Schwarz am Zug, steht Weiß leer. */
  readonly pairs = computed(() => {
    const g = this.game();
    if (!g?.moves.length) return [];
    const lang = this.lang();
    const blackFirst = g.moves[0].color === 'b';
    const firstNo = Number(g.fens[0].split(' ')[5]) || 1;
    const out: { n: number; w: number; ws: string; b: number; bs: string }[] = [];
    let i = 0;
    if (blackFirst) {
      out.push({ n: firstNo, w: -1, ws: '…', b: 0, bs: cardSan(g.moves[0].san, lang) });
      i = 1;
    }
    for (; i < g.moves.length; i += 2) {
      out.push({
        n: firstNo + out.length, w: i, ws: cardSan(g.moves[i].san, lang),
        b: i + 1 < g.moves.length ? i + 1 : -1, bs: i + 1 < g.moves.length ? cardSan(g.moves[i + 1].san, lang) : '',
      });
    }
    return out;
  });

  private readonly translate = inject(TranslateService, { optional: true });
  /** Sprache der Karte als Signal: Züge und Datum folgen einem Sprachwechsel. */
  private readonly lang = signal(cardLang(this.translate));
  private readonly langSub = this.translate?.onLangChange.pipe(takeUntilDestroyed()).subscribe(() => this.lang.set(cardLang(this.translate)));

  readonly comment = computed(() => this.game()?.comments[this.index()] ?? '');
  readonly result = computed(() => {
    const r = this.game()?.headers['Result'] ?? '*';
    return r === '*' ? '' : r === '1/2-1/2' ? '½–½' : r.replace('-', '–');
  });
  readonly meta = computed(() => {
    const h = this.game()?.headers ?? {};
    return [h['Event'] && h['Event'] !== '?' ? h['Event'] : '', cardDate(h['Date'] ?? '', this.lang())].filter(x => x).join(' · ');
  });

  /** Halbzug-Indizes (`index`) mit einer Marke — Hauptlinie an ihrem Zug, Variante an ihrem Abzweig. */
  readonly markedIdx = computed(() => new Set(this.marksSig().flatMap(m =>
    m.ply != null ? [m.ply - 1] : m.variationFromPly != null ? [m.variationFromPly - 1] : [])));

  /** Die Sprungmarken mit Beschriftung: „12…Sf6" in der Partie, „Variante: 12…c5" aus einer Variante, sonst „in einer Variante". */
  readonly markList = computed(() => {
    const g = this.game(), lang = this.lang(), active = this.activeSide(), i = this.index();
    if (!g) return [];
    return this.marksSig().map(m => {
      const line = m.ply == null && m.variationFromPly != null ? this.variationLine(g, m, lang, 1) : '';
      const label = m.ply != null ? this.moveLabel(g, m.ply - 1, lang)
        : line ? (this.translate?.instant('playerCard.markVariation', { line }) ?? line)
        : (this.translate?.instant('playerCard.markInVariation') ?? '');
      const isActive = m.ply != null ? !active && i === m.ply - 1 : !!active && active.fen === m.fen;
      return { key: m.positionKey, mark: m, label, count: m.count, active: isActive };
    });
  });

  /** Zu einer Marke: in der Partie an ihren Halbzug, aus einer Variante an den Abzweig mit der Stellung auf dem Brett. */
  jump(m: ReplayMark): void {
    const g = this.game();
    if (m.ply != null) { this.side.set(null); this.index.set(m.ply - 1); return; }
    const at = m.variationFromPly != null ? m.variationFromPly - 1 : this.index();
    this.index.set(at);
    this.side.set({ at, fen: m.fen, line: g && m.variationFromPly != null ? this.variationLine(g, m, this.lang(), Infinity) : '' });
  }

  /** „12…Sf6" — Zugnummer und Zug an Index `i` (−1 = Ausgangsstellung). */
  private moveLabel(g: ParsedGame, i: number, lang: string): string {
    if (i < 0 || !g.moves[i]) return this.translate?.instant('playerCard.start') ?? '';
    const firstNo = Number(g.fens[0].split(' ')[5]) || 1;
    const blackFirst = g.moves[0].color === 'b' ? 1 : 0;
    const no = firstNo + Math.floor((i + blackFirst) / 2);
    return `${no}${g.moves[i].color === 'w' ? '.' : '…'}${cardSan(g.moves[i].san, lang)}`;
  }

  /** Die Variante ab ihrem Abzweig in Kartennotation („12…c5 13.Sf3"), höchstens `max` Züge; leer, wenn sie nicht passt. */
  private variationLine(g: ParsedGame, m: ReplayMark, lang: string, max: number): string {
    const base = g.fens[m.variationFromPly ?? -1];
    if (!base || !m.variationUci) return '';
    try {
      const chess = new Chess(base);
      const out: string[] = [];
      const moves = m.variationUci.split(' ');
      for (const u of max === Infinity ? moves : moves.slice(0, max)) {
        const color = chess.turn(), no = chess.moveNumber();
        const mv = chess.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u[4] });
        out.push(color === 'w' || out.length === 0 ? `${no}${color === 'w' ? '.' : '…'}${cardSan(mv.san, lang)}` : cardSan(mv.san, lang));
      }
      return out.join(' ');
    } catch {
      return '';
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    this.marksSig.set(this.marks ?? []);
    if (!changes['pgn'] && this.game()) return;   // nur die Marken kamen neu — die Partie bleibt, wo sie ist
    this.side.set(null);
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
    this.side.set(null);
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

/** Eine interessante Stellung der Partie (`GET /api/league/club/games/{id}/marks`, 0.848.0). */
export interface ReplayMark {
  fen: string;
  positionKey: string;
  /** Halbzüge bis zur Stellung in der Partie (Sprungmarke) — `null` = aus einer Variante. */
  ply: number | null;
  /** Aus einer Variante: Halbzug, an dem sie abzweigt, und ihre Züge (UCI) — fehlt bei älteren Markierungen. */
  variationFromPly: number | null;
  variationUci: string | null;
  /** So oft markiert. */
  count: number;
}
