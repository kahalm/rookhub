import { ChangeDetectionStrategy, Component, DestroyRef, OnChanges, SimpleChanges, inject, input, output, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, Subject, debounceTime, map, of, switchMap } from 'rxjs';
import {
  TABLEBASE_MAX_PIECES, TablebaseCategory, TablebaseMove, TablebaseResult, TablebaseService, tablebasePieceCount,
} from './tablebase.service';

/** Was die Tablebase für die Bewertungsleiste sagt — Weiß-Sicht; `fen` = für welche Stellung. */
export interface TablebaseVerdict { fen: string; whiteHeight: number; text: string; }

/** Gewonnen/verloren zählen; „verflucht"/„gesegnet" ist nach der 50-Züge-Regel ein Remis. */
const WINNING: ReadonlySet<TablebaseCategory> = new Set(['win']);
const LOSING: ReadonlySet<TablebaseCategory> = new Set(['loss']);

/**
 * „Tablebase (Lichess)" in der Live-Analyse (0.729.0, Wunsch 2026-10-10: „bei der Live-Analyse ab 7 Steinen Lichess um die
 * Wahrheit fragen — parallel Stockfish, sollte ich keine Antwort bekommen"). Ab {@link TABLEBASE_MAX_PIECES} Steinen fragt
 * die Leiste `GET /api/tablebase` (200 ms nach dem letzten Stellungswechsel — Durchklicken kostet sonst eine Anfrage je
 * Zug) und zeigt das genaue Ergebnis samt Bewertung jedes Zugs; ein Klick spielt ihn. Stockfish rechnet daneben weiter;
 * antwortet Lichess nicht, sagt die Leiste das und sonst ändert sich nichts. Über `verdict` übernimmt die
 * Bewertungsleiste das Ergebnis.
 */
@Component({
  selector: 'app-tablebase-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, TranslatePipe],
  template: `
    @if (applies()) {
      <section class="tb">
        <div class="head">
          <mat-icon class="icon">menu_book</mat-icon>
          <span class="title">{{ 'analysis.tablebase.title' | translate }}</span>
          @if (result(); as r) {
            @if (r.status === 'ok') {
              <span class="verdict" [class]="'verdict ' + tone(r.category)">{{ verdictKey(r) | translate: verdictParams(r) }}</span>
              @if (distance(r.dtm, r.dtz); as d) { <span class="dist">{{ d.key | translate: { n: d.n } }}</span> }
            } @else {
              <span class="muted">{{ (r.status === 'rateLimited' ? 'analysis.tablebase.rateLimited' : 'analysis.tablebase.unavailable') | translate }}</span>
            }
          } @else {
            <span class="muted">{{ 'analysis.tablebase.asking' | translate }}</span>
          }
        </div>
        @if (result()?.status === 'ok' && result()!.moves.length) {
          <div class="moves">
            @for (m of result()!.moves; track m.uci) {
              <button type="button" class="move" [class]="'move ' + tone(m.category)" (click)="playSan.emit(m.san)">
                <span class="san">{{ m.san }}</span>
                <span class="res">{{ ('analysis.tablebase.cat.' + m.category) | translate }}</span>
                @if (moveDistance(m); as d) { <span class="d">{{ d.key | translate: { n: d.n } }}</span> }
              </button>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    :host { display: block; }
    .tb { margin-top: 10px; padding-top: 8px; border-top: 1px solid color-mix(in srgb, currentColor 18%, transparent); }
    .head { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; font-size: .85rem; }
    .icon { font-size: 18px; width: 18px; height: 18px; opacity: .7; }
    .title { font-weight: 600; }
    .verdict { font-weight: 700; padding: 1px 8px; border-radius: 10px; }
    .win { color: #1b5e20; background: rgba(76, 175, 80, .18); }
    .loss { color: #b71c1c; background: rgba(244, 67, 54, .16); }
    .draw { color: inherit; background: color-mix(in srgb, currentColor 10%, transparent); }
    .dist, .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .moves { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 6px; }
    .move { display: inline-flex; align-items: baseline; gap: 6px; border: 0; border-radius: 6px; padding: 3px 8px; cursor: pointer;
      font: inherit; font-size: .82rem; }
    .move:hover, .move:focus-visible { outline: 2px solid currentColor; outline-offset: 1px; }
    .san { font-family: 'Courier New', monospace; font-weight: 700; }
    .d { opacity: .7; font-size: .75rem; }
  `],
})
export class TablebasePanelComponent implements OnChanges {
  private readonly api = inject(TablebaseService);
  private readonly translate = inject(TranslateService);

  readonly fen = input.required<string>();
  /** Ein Zug aus der Liste angeklickt (SAN in der Stellung `fen`). */
  readonly playSan = output<string>();
  /** Ergebnis für die Bewertungsleiste; `null`, solange (oder wenn) die Tablebase nichts sagt. */
  readonly verdict = output<TablebaseVerdict | null>();

  readonly applies = signal(false);
  readonly result = signal<TablebaseResult | null>(null);
  private readonly fens = new Subject<string>();

  constructor() {
    const sub = this.fens.pipe(
      debounceTime(200),
      switchMap(fen => {
        const r$: Observable<TablebaseResult | null> = this.isTablebaseFen(fen) ? this.api.lookup(fen) : of(null);
        return r$.pipe(map(r => ({ fen, r })));
      }),
    ).subscribe(({ fen, r }) => {
      if (fen !== this.fen()) return;   // Antwort einer schon verlassenen Stellung
      this.result.set(r);
      this.verdict.emit(r ? verdictOf(fen, r) : null);
      if (r && r.status !== 'ok') this.api.forget(fen);
    });
    inject(DestroyRef).onDestroy(() => sub.unsubscribe());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['fen']) return;
    const fen = this.fen();
    const applies = this.isTablebaseFen(fen);
    this.applies.set(applies);
    this.result.set(null);
    this.verdict.emit(null);
    if (applies) this.fens.next(fen);
  }

  private isTablebaseFen(fen: string): boolean {
    const n = tablebasePieceCount(fen);
    return n > 0 && n <= TABLEBASE_MAX_PIECES;
  }

  tone(c: TablebaseCategory | null): 'win' | 'loss' | 'draw' {
    return c && WINNING.has(c) ? 'win' : c && LOSING.has(c) ? 'loss' : 'draw';
  }

  /** Text für das Ergebnis der Stellung („Weiß gewinnt", „Remis", „Matt" …). */
  verdictKey(r: TablebaseResult): string {
    if (r.checkmate) return 'analysis.tablebase.checkmate';
    if (r.stalemate) return 'analysis.tablebase.stalemate';
    if (r.insufficientMaterial) return 'analysis.tablebase.insufficient';
    switch (r.category) {
      case 'win': case 'loss': return 'analysis.tablebase.wins';
      case 'cursed-win': case 'blessed-loss': return 'analysis.tablebase.cursed';
      case 'draw': return 'analysis.tablebase.draw';
      default: return 'analysis.tablebase.unknown';
    }
  }

  verdictParams(r: TablebaseResult): { side: string } {
    const whiteToMove = this.fen().split(' ')[1] !== 'b';
    const moverWins = r.category === 'win' || r.category === 'cursed-win';
    const white = moverWins ? whiteToMove : !whiteToMove;
    return { side: this.translate.instant(white ? 'analysis.tablebase.white' : 'analysis.tablebase.black') };
  }

  /** Matt in n (wenn bekannt), sonst DTZ — Halbzüge bis zum nächsten Schlag-/Bauernzug. */
  distance(dtm: number | null, dtz: number | null): { key: string; n: number } | null {
    if (dtm != null && dtm !== 0) return { key: 'analysis.tablebase.mateIn', n: Math.ceil(Math.abs(dtm) / 2) };
    if (dtz != null && dtz !== 0) return { key: 'analysis.tablebase.dtz', n: Math.abs(dtz) };
    return null;
  }

  moveDistance(m: TablebaseMove): { key: string; n: number } | null {
    if (m.checkmate) return { key: 'analysis.tablebase.mateNow', n: 0 };
    return m.category === 'draw' ? null : this.distance(m.dtm, m.dtz);
  }
}

/** Ergebnis → Bewertungsleiste (Weiß-Sicht). Verflucht/gesegnet = Remis nach der 50-Züge-Regel. */
export function verdictOf(fen: string, r: TablebaseResult): TablebaseVerdict | null {
  if (r.status !== 'ok' || !r.category) return null;
  const whiteToMove = fen.split(' ')[1] !== 'b';
  if (r.category === 'win' || r.category === 'loss') {
    const whiteWins = (r.category === 'win') === whiteToMove;
    return { fen, whiteHeight: whiteWins ? 100 : 0, text: whiteWins ? '1-0 (TB)' : '0-1 (TB)' };
  }
  if (r.category === 'draw' || r.category === 'cursed-win' || r.category === 'blessed-loss') {
    return { fen, whiteHeight: 50, text: '½ (TB)' };
  }
  return null;
}
