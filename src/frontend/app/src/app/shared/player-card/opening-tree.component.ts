import { ChangeDetectionStrategy, Component, Input, OnChanges, SimpleChanges, computed, inject, signal } from '@angular/core';
import { Chess } from 'chess.js';
import { ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { LeagueApiService } from '@lh/core/league-api.service';
import { de } from '@lh/core/league-format';
import { OpeningTree, TreeFilter } from '@lh/core/league.models';
import { DEFAULT_TREE_FILTER } from './tree-filter';

/** „5.Sf3" bzw. „5…Sf6" für den Halbzug an Stelle `ply` (0-basiert). */
export function moveLabel(ply: number, san: string): string {
  const no = Math.floor(ply / 2) + 1;
  return `${no}${ply % 2 === 0 ? '.' : '…'}${de(san)}`;
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
  imports: [ChessBoardComponent],
  template: `
    <div class="tree">
      <div class="tree-head">
        <div class="seg" role="group" aria-label="Farbe im Baum">
          <button type="button" [attr.aria-pressed]="color() === 'w'" (click)="setColor('w')">Mit Weiß</button>
          <button type="button" [attr.aria-pressed]="color() === 's'" (click)="setColor('s')">Mit Schwarz</button>
        </div>
        <span class="muted small" role="status">@if (loading()) { Lade … } @else if (data(); as d) { {{ d.total }} Partien in dieser Stellung
          @if (active().source === 'both' && d.online !== undefined) { (davon {{ d.board ?? 0 }} am Brett, {{ d.online }} online) } }</span>
      </div>
      <nav class="crumbs" aria-label="Zugfolge">
        <button type="button" class="btn-link" [disabled]="!line().length" (click)="back(0)">Start</button>
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
          @if (error()) { <p class="err small">{{ error() }}</p> }
          @if (data(); as d) {
            @if (d.moves.length) {
              <table>
                <thead><tr><th>Zug</th><th class="num">Partien</th><th class="num">Score</th><th class="num">zuletzt</th></tr></thead>
                <tbody>
                  @for (m of d.moves; track m.san) {
                    <tr>
                      <td><button type="button" class="pl" (click)="play(m.san)">{{ label(line().length, m.san) }}</button>
                        <span class="tbar"><i [style.width.%]="share(m.n, d.total)"></i></span></td>
                      <td class="num">{{ m.n }} <span class="muted small">({{ share(m.n, d.total) }} %)</span></td>
                      <td class="num">{{ m.score === null ? '–' : m.score + ' %' }}</td>
                      <td class="num muted">{{ m.last ?? '' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            } @else {
              <p class="muted small">Keine Partie ging von hier weiter.</p>
            }
            @if (d.ended) { <p class="muted small">{{ d.ended }} Partien enden hier (kürzer oder ohne weitere Züge).</p> }
            <p class="muted small">Score aus Sicht von {{ d.name.split(',')[0] }}. {{ color() === 'w' ? 'Weiß' : 'Schwarz' }}-Partien.
              @if (active().source !== 'board') { Online-Partien der gesicherten Konten (unsichere per Schalter), höchstens der letzten fünf Jahre. }</p>
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

  private readonly api = inject(LeagueApiService);
  readonly color = signal<'w' | 's'>('w');
  readonly line = signal<string[]>([]);
  readonly data = signal<OpeningTree | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly position = computed(() => fenAfter(this.line()));
  readonly label = moveLabel;
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
      if (my === this.seq) this.error.set('Der Eröffnungsbaum konnte nicht geladen werden.');
    } finally {
      if (my === this.seq) this.loading.set(false);
    }
  }
}
