import { Component, OnInit, HostListener, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { PgnViewerService } from '../../shared/pgn-viewer/pgn-viewer.service';
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireService, SharedLine } from '../../core/repertoire.service';
import { isBoardHotkey } from '../../shared/keyboard.util';

/**
 * Öffentliche Nur-Ansehen-Seite einer geteilten Repertoire-Linie (Route <c>/l/:token</c>, kein
 * Login nötig). Spiegelt die geteilte-Partie-Seite (<c>/g/:token</c>): reused den PgnViewerService
 * + chess-board/move-list.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-shared-line',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatCardModule, MatProgressSpinnerModule,
    TranslatePipe, ChessBoardComponent, MoveListComponent,
  ],
  providers: [PgnViewerService],
  template: `
    <div class="shared-page">
      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (notFound) {
        <mat-card class="empty-state">
          <mat-icon>link_off</mat-icon>
          <p>{{ 'repertoire.shareLine.notFound' | translate }}</p>
        </mat-card>
      } @else if (line) {
        <mat-card class="viewer">
          <div class="header">
            <span class="title">{{ line.title || ('repertoire.shareLine.defaultTitle' | translate) }}</span>
            <span class="meta">
              @if (line.repertoireName) { <span class="rep" [attr.title]="line.repertoireName">{{ line.repertoireName }}</span> }
              <span class="date">{{ line.createdAt | date:'mediumDate' }}</span>
              @if (line.expiresAt) {
                <span class="date">{{ 'repertoire.shareLine.validUntil' | translate: { date: (line.expiresAt | date:'mediumDate') } }}</span>
              }
            </span>
            <!-- Geteiltes Kapitel: je Linie eine Partie im PGN → Linie wählen, vor/zurück. -->
            @if (service.games.length > 1) {
              <div class="line-picker">
                <button mat-icon-button (click)="pickLine(service.currentGameIndex - 1)" [disabled]="service.currentGameIndex <= 0"
                        [attr.aria-label]="'repertoire.shareLine.prevLine' | translate"><mat-icon>chevron_left</mat-icon></button>
                <select class="line-select" [value]="service.currentGameIndex" (change)="pickLine(+$any($event.target).value)"
                        [attr.aria-label]="'repertoire.shareLine.pickLine' | translate">
                  @for (g of service.games; track $index) {
                    <option [value]="$index" [selected]="$index === service.currentGameIndex">{{ lineLabel($index) }}</option>
                  }
                </select>
                <button mat-icon-button (click)="pickLine(service.currentGameIndex + 1)" [disabled]="service.currentGameIndex >= service.games.length - 1"
                        [attr.aria-label]="'repertoire.shareLine.nextLine' | translate"><mat-icon>chevron_right</mat-icon></button>
                <span class="line-count">{{ 'repertoire.shareLine.lineOf' | translate: { n: service.currentGameIndex + 1, total: service.games.length } }}</span>
              </div>
            }
          </div>
          <div class="body">
            <div class="board-section">
              <div class="board-wrap">
                <app-chess-board [fen]="service.currentFen" [lastMove]="service.lastMove" [flipped]="flipped"
                                 [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                <div class="board-tap board-tap-prev" (click)="service.goBack()"></div>
                <div class="board-tap board-tap-next" (click)="service.goForward()"></div>
              </div>
              <div class="nav">
                <button mat-icon-button (click)="service.goToStart()" [disabled]="service.currentMoveIndex < 0"><mat-icon>skip_previous</mat-icon></button>
                <button mat-icon-button (click)="service.goBack()" [disabled]="service.currentMoveIndex < 0"><mat-icon>navigate_before</mat-icon></button>
                <button mat-icon-button (click)="service.goForward()" [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"><mat-icon>navigate_next</mat-icon></button>
                <button mat-icon-button (click)="service.goToEnd()" [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"><mat-icon>skip_next</mat-icon></button>
                <button mat-icon-button (click)="flipped = !flipped"><mat-icon>swap_vert</mat-icon></button>
              </div>
            </div>
            <div class="moves-section">
              @if (service.currentGame; as g) {
                <app-move-list [moves]="g.moves" [currentMoveIndex]="service.currentMoveIndex" [comments]="g.comments" (moveClicked)="service.goToMove($event)" />
              }
            </div>
          </div>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    /* Brettgröße wie auf der geteilten Partie (d2-rk-shared-board-fixed): wächst mit dem Fenster statt fest 400 px. */
    .shared-page { --board-size: clamp(360px, min(calc(100vh - 260px), calc(100vw - 520px)), 680px);
      max-width: calc(var(--board-size) + 520px); margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty-state mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .viewer { padding: 16px; }
    .header { display: flex; flex-direction: column; gap: 4px; margin-bottom: 12px; }
    .title { font-weight: 600; font-size: 1.1rem; }
    /* a1-rk-geteilte-linie-datum-handy: Datum bricht nie um, Trenner „ · “ als ::before des Datums (DESIGN 5.13). */
    .meta { display: flex; flex-wrap: wrap; row-gap: 2px; min-width: 0; font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .rep { font-weight: 500; }
    .date { white-space: nowrap; }
    .rep + .date::before { content: '·'; margin: 0 6px; }
    .date + .date::before { content: '·'; margin: 0 6px; }
    .line-picker { display: flex; align-items: center; gap: 4px; margin-top: 4px; min-width: 0; }
    .line-select { flex: 1 1 auto; min-width: 0; max-width: 420px; padding: 6px 8px; border-radius: 4px; font: inherit;
      border: 1px solid color-mix(in srgb, currentColor 20%, transparent); background: var(--mat-sys-surface-container); color: inherit; }
    .line-count { white-space: nowrap; font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); margin-left: 4px; }
    .body { display: flex; gap: 16px; align-items: flex-start; }
    .board-section { width: var(--board-size); display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: var(--board-size); }
    .board-wrap app-chess-board { display: block; width: var(--board-size); }
    .board-tap { display: none; position: absolute; top: 0; bottom: 0; width: 40%; z-index: 10; cursor: pointer; }
    .board-tap-prev { left: 0; }
    .board-tap-next { right: 0; }
    .nav { display: flex; gap: 4px; }
    .moves-section { flex: 1; border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; min-width: 180px; overflow: auto; max-height: 60vh; }
    /* a1-rk-geteilt-recon-tablet: zweispaltig erst ab 900 px (DESIGN 4.2). Tablet hochkant (769–899 px) stapelt Brett
       über Zugliste — nebeneinander blieb das Brett beim Mindestwert 360 px und die untere Hälfte leer. Das Brett nimmt
       die Kartenbreite, höchstens bis zur Fensterhöhe. */
    @media (max-width: 899.98px) {
      .body { flex-direction: column; align-items: stretch; }
      .board-section { width: 100%; max-width: 100%; align-items: center; }
      .board-wrap { width: min(100%, max(360px, calc(100vh - 260px))); }
      .board-wrap app-chess-board { width: 100%; }
      .nav { justify-content: center; padding: 4px 0; }
      .moves-section { width: 100%; max-height: 40vh; }
    }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { padding: 0; border-radius: 0; }
      .header { padding: 12px 16px; }
      /* Handy: „Name · Datum“ in EINER Zeile, der Name kürzt mit „…“ (5.3), das Datum bleibt ganz. */
      .meta { flex-wrap: nowrap; }
      .line-picker { padding: 0; }
      .line-count { display: none; }
      .rep { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .date { flex: none; }
      .board-wrap { width: 100%; }
      .board-tap { display: block; }
      .moves-section { border-left: none; border-right: none; border-radius: 0; border-bottom: none; }
    }
    /* a1-rk-geteilt-brett-ultrawide: Breitbild-Stufe (DESIGN 4.1) — Brett bis zur Fensterhöhe statt höchstens 680 px;
       die Seite rechnet weiter Brett + 520 px. */
    @media (min-width: 2400px) {
      .shared-page { --board-size: clamp(360px, calc(100vh - 260px), 1080px); }
    }
  `]
})
export class SharedLineComponent implements OnInit {
  line: SharedLine | null = null;
  loading = true;
  notFound = false;
  flipped = false;

  constructor(
    public service: PgnViewerService,
    private route: ActivatedRoute,
    private repertoires: RepertoireService,
    public preferences: PreferencesService,
  ) {}

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.repertoires.getSharedLine(token).subscribe({
      next: l => { this.line = l; this.service.loadPgn(l.pgn); this.loading = false; },
      error: () => { this.notFound = true; this.loading = false; },
    });
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    if (!isBoardHotkey(event)) return;
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.service.goBack(); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.service.goForward(); }
    else if (event.key === 'ArrowUp' && this.service.games.length > 1) { event.preventDefault(); this.pickLine(this.service.currentGameIndex - 1); }
    else if (event.key === 'ArrowDown' && this.service.games.length > 1) { event.preventDefault(); this.pickLine(this.service.currentGameIndex + 1); }
  }

  /** Linie eines geteilten Kapitels wählen (außerhalb des Bereichs: nichts). */
  pickLine(index: number): void {
    this.service.selectGame(index);
  }

  /** Eintrag der Linienauswahl: „3. Sizilianisch, Najdorf — 6. Lg5“ (Titel + letzter Zug, damit gleiche Titel unterscheidbar sind). */
  lineLabel(index: number): string {
    const g = this.service.games[index];
    if (!g) return '';
    const title = g.headers['Event'] && g.headers['Event'] !== '?' ? g.headers['Event'] : '';
    const last = g.moves.length > 0 ? g.moves[g.moves.length - 1] : null;
    let lastText = '';
    if (last) {
      const fen = g.fens[g.moves.length - 1] ?? '';
      const parts = fen.split(' ');
      const fullmove = Number(parts[5]) || 0;
      // fens[i] = Stellung VOR Halbzug i → Zugnummer + wer zieht; Schwarz ⇒ „N…Sf6“.
      lastText = parts[1] === 'b' ? `${fullmove}…${last.san}` : `${fullmove}. ${last.san}`;
    }
    return [`${index + 1}.`, title, lastText ? `— ${lastText}` : ''].filter(Boolean).join(' ');
  }
}
