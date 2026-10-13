import { ChangeDetectionStrategy, Component, Input, OnChanges, SimpleChanges, computed, inject, signal } from '@angular/core';
import { Chess } from 'chess.js';
import { ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { PLAYER_CARD_API } from './player-card-api';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { cardLang, cardPct, cardSan } from './card-lang';
import { OpeningTree, TreeFilter } from '@lh/core/league.models';
import { DEFAULT_TREE_FILTER } from './tree-filter';

/** „5.Sf3" bzw. „5…Sf6" für den Halbzug an Stelle `ply` (0-basiert), in den Figurenbuchstaben von `lang` (Vorgabe deutsch). */
export function moveLabel(ply: number, san: string, lang = 'de'): string {
  const no = Math.floor(ply / 2) + 1;
  return `${no}${ply % 2 === 0 ? '.' : '…'}${cardSan(san, lang)}`;
}

/** Stellung nach der Zugfolge; bei einem Zug, der nicht geht, die davor. */
export function fenAfter(line: readonly string[]): { fen: string; last?: [string, string] } {
  const chess = new Chess();
  let last: [string, string] | undefined;
  for (const san of line) {
    try { const m = chess.move(san); last = [m.from, m.to]; } catch { break; }
  }
  return { fen: chess.fen(), last };
}

/**
 * Eröffnungsbaum eines Spielers auf der Spielerkarte (Wunsch 2026-09-28): mit der gewählten Farbe, Zug für Zug — je
 * Fortsetzung wie oft, wie viel Prozent und mit welchem Score er sie gespielt hat, dazu das jüngste Jahr. Ein Klick geht
 * tiefer, die Zugleiste zurück. Gezählt wird am Server über alle seine Partien (Lumbra, Megabase, chess-results, Verein),
 * seit 0.605.0 wahlweise mit den geholten Online-Partien seiner Konten (Zeitformat wählbar) und nur für die letzten x Jahre.
 * Den Filter wählt seit 0.617.0 die Karte (`lh-tree-filter-bar`) — er gilt dort auch für das Eröffnungsprofil.
 */
@Component({
  selector: 'lh-opening-tree',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, TranslatePipe],
  template: `
    <div class="tree">
      <div class="tree-head">
        <div class="seg" role="group" [attr.aria-label]="'playerCard.treeColorGroup' | translate">
          <button type="button" [attr.aria-pressed]="color() === 'w'" (click)="setColor('w')">{{ 'playerCard.withWhite' | translate }}</button>
          <button type="button" [attr.aria-pressed]="color() === 's'" (click)="setColor('s')">{{ 'playerCard.withBlack' | translate }}</button>
        </div>
        <span class="muted small" role="status">@if (loading()) { {{ 'common.loading' | translate }} } @else if (data(); as d) { {{ (d.total === 1 ? 'playerCard.treeTotalOne' : 'playerCard.treeTotal') | translate: { n: d.total } }}
          @if (active().source === 'both' && d.online !== undefined) { {{ 'playerCard.treeSplit' | translate: { board: d.board ?? 0, online: d.online } }} } }</span>
      </div>
      <nav class="crumbs" [attr.aria-label]="'playerCard.crumbs' | translate">
        <button type="button" class="btn-link" [disabled]="!line().length" (click)="back(0)">{{ 'playerCard.start' | translate }}</button>
        @for (m of line(); track $index) {
          <span aria-hidden="true">›</span>
          <button type="button" class="btn-link" [disabled]="$index === line().length - 1" (click)="back($index + 1)">{{ label($index, m) }}</button>
        }
      </nav>
      <div class="tree-body">
        <div class="tree-board">
          <app-chess-board [fen]="position().fen" [lastMove]="position().last" [flipped]="color() === 's'" />
        </div>
        <div class="tree-moves">
          @if (error(); as e) { <p class="err small">{{ e | translate }}</p> }
          @if (data(); as d) {
            @if (d.moves.length) {
              <table>
                <thead><tr><th>{{ 'playerCard.thMove' | translate }}</th><th class="num">{{ 'playerCard.thGames' | translate }}</th>
                  <th class="num">{{ 'playerCard.thScore' | translate }}</th><th class="num">{{ 'playerCard.thLast' | translate }}</th></tr></thead>
                <tbody>
                  @for (m of d.moves; track m.san) {
                    <tr>
                      <td><button type="button" class="pl" (click)="play(m.san)">{{ label(line().length, m.san) }}</button>
                        <span class="tbar"><i [style.width.%]="share(m.n, d.total)"></i></span></td>
                      <td class="num">{{ m.n }} <span class="muted small">({{ pct(share(m.n, d.total)) }})</span></td>
                      <td class="num">{{ m.score === null ? '–' : pct(m.score) }}</td>
                      <td class="num muted">{{ m.last ?? '' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            } @else {
              <p class="muted small">{{ 'playerCard.noContinuation' | translate }}</p>
            }
            @if (d.ended) { <p class="muted small">{{ (d.ended === 1 ? 'playerCard.endedOne' : 'playerCard.ended') | translate: { n: d.ended } }}</p> }
            <p class="muted small">{{ 'playerCard.scoreFrom' | translate: { name: d.name.split(',')[0] } }} {{ (color() === 'w' ? 'playerCard.whiteGames' : 'playerCard.blackGames') | translate }}
              @if (active().source !== 'board') { {{ 'playerCard.treeOnlineNote' | translate }} }</p>
          }
        </div>
      </div>
    </div>
  `,
})
export class OpeningTreeComponent implements OnChanges {
  @Input({ required: true }) fide!: string;
  @Input() token: string | null = null;
  @Input() startColor: 'w' | 's' = 'w';
  /** Der WIRKSAME Filter der Karte (0.617.0 — die Leiste sitzt auf der Karte und gilt auch fürs Eröffnungsprofil). */
  @Input() filter: TreeFilter = DEFAULT_TREE_FILTER;
  readonly active = signal<TreeFilter>(DEFAULT_TREE_FILTER);

  private readonly api = inject(PLAYER_CARD_API);
  readonly color = signal<'w' | 's'>('w');
  readonly line = signal<string[]>([]);
  readonly data = signal<OpeningTree | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly position = computed(() => fenAfter(this.line()));
  private readonly translate = inject(TranslateService, { optional: true });
  /** Zug mit Nummer in den Figurenbuchstaben der Kartensprache (RookHub: Oberfläche, LeagueHub: deutsch). */
  label(ply: number, san: string): string { return moveLabel(ply, san, cardLang(this.translate)); }
  pct(p: number): string { return cardPct(p, cardLang(this.translate)); }
  private seq = 0;

  ngOnChanges(changes: SimpleChanges): void {
    this.active.set(this.filter);
    // Anderer Spieler oder andere Farbe: von vorn. Nur neue Zahlen (Karte nach einer Konto-Änderung neu): Stellung bleibt.
    if (changes['fide'] || changes['startColor'] || changes['token']) {
      this.color.set(this.startColor);
      this.line.set([]);
    }
    void this.load();
  }

  setColor(c: 'w' | 's'): void {
    if (c === this.color()) return;
    this.color.set(c);
    this.line.set([]);
    void this.load();
  }

  play(san: string): void {
    this.line.update(l => [...l, san]);
    void this.load();
  }

  back(n: number): void {
    this.line.update(l => l.slice(0, n));
    void this.load();
  }

  share(n: number, total: number): number {
    return total ? Math.round((100 * n) / total) : 0;
  }

  private async load(): Promise<void> {
    const my = ++this.seq;
    this.loading.set(true);
    this.error.set(null);
    try {
      const t = await this.api.tree(this.fide, this.color(), this.line(), this.token, this.active());
      if (my === this.seq) this.data.set(t);
    } catch {
      if (my === this.seq) this.error.set('playerCard.treeError');
    } finally {
      if (my === this.seq) this.loading.set(false);
    }
  }
}
