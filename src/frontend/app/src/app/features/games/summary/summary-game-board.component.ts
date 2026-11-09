import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { Move } from 'chess.js';
import { ChessBoardComponent } from '../../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../../shared/pgn-viewer/move-list.component';
import { isBoardHotkey } from '../../../shared/keyboard.util';
import { PreferencesService } from '../../../core/preferences.service';
import { positionsOf } from './summary-markdown.util';

/**
 * Seitenbrett der Partie-Zusammenfassung (Wunsch 2026-11-09: „neben der …/summary die Partie sehen und durchklicken"):
 * Brett, Blättern und Zugliste der Hauptlinie. Die Halbzug-Zählung ist die der Diagramme (`ply` 0 = Anfangsstellung), sie
 * kommt aus demselben `positionsOf`; `showPly` ist der Sprung von einem Diagramm. Ab 900 px klebt das Brett neben dem
 * Text, darunter steht es unter dem Text.
 */
@Component({
  selector: 'app-summary-game-board',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, MoveListComponent, MatButtonModule, MatCardModule, MatIconModule, TranslatePipe],
  template: `
    <mat-card class="side-card" [attr.aria-label]="'playerCard.replayGame' | translate">
      <div class="board-box">
        <app-chess-board [fen]="positions().fens[ply()]" [lastMove]="positions().lastMoves[ply()]" [flipped]="isFlipped()"
                         [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
      </div>
      <div class="nav" role="group" [attr.aria-label]="'playerCard.movesGroup' | translate">
        <button mat-icon-button type="button" class="nav-start" [disabled]="ply() === 0" (click)="goTo(0)"
                [attr.aria-label]="'playerCard.toStart' | translate"><mat-icon>first_page</mat-icon></button>
        <button mat-icon-button type="button" class="nav-back" [disabled]="ply() === 0" (click)="goTo(ply() - 1)"
                [attr.aria-label]="'playerCard.stepBack' | translate"><mat-icon>chevron_left</mat-icon></button>
        <button mat-icon-button type="button" class="nav-forward" [disabled]="ply() === lastPly()" (click)="goTo(ply() + 1)"
                [attr.aria-label]="'playerCard.stepForward' | translate"><mat-icon>chevron_right</mat-icon></button>
        <button mat-icon-button type="button" class="nav-end" [disabled]="ply() === lastPly()" (click)="goTo(lastPly())"
                [attr.aria-label]="'playerCard.toEnd' | translate"><mat-icon>last_page</mat-icon></button>
      </div>
      <div class="moves">
        <app-move-list [moves]="moves()" [currentMoveIndex]="ply() - 1" (moveClicked)="goTo($event + 1)" />
      </div>
    </mat-card>
  `,
  styles: [`
    :host { display: block; min-width: 0; margin-top: 16px; }
    .side-card { padding: 12px; }
    .board-box { width: min(100%, 480px, max(240px, calc(100vh - 160px))); margin: 0 auto; }
    .nav { display: flex; justify-content: center; gap: 4px; margin: 4px 0; max-width: 100%; }
    .moves { max-height: 40vh; overflow-y: auto; }
    app-move-list { display: block; }
    /* Ab 900 px wie Analysebrett und Löser (DESIGN 5.12): neben dem Text, klebt, nie höher als das Fenster —
       das Brett gibt der Höhe nach, die Zugliste scrollt in ihrem Kasten. */
    @media (min-width: 900px) {
      :host { position: sticky; top: 12px; align-self: start; margin-top: 0; }
      .side-card { max-height: calc(100vh - 24px); box-sizing: border-box; }
      .board-box { width: min(100%, max(240px, calc(100vh - 250px))); flex: 0 0 auto; }
      .nav { flex: 0 0 auto; }
      .moves { flex: 0 1 auto; min-height: 72px; max-height: none; }
    }
  `],
})
export class SummaryGameBoardComponent {
  readonly preferences = inject(PreferencesService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly pgn = input.required<string>();
  /** Blickrichtung wie die Diagramme der Seite; ein Sprung von einem Diagramm übernimmt dessen Richtung. */
  readonly flipped = input(false);

  readonly positions = computed(() => positionsOf(this.pgn()));
  readonly lastPly = computed(() => this.positions().sans.length);
  /** Halbzüge auf dem Brett, 0 = Anfangsstellung (wie `ply` der Diagramme). */
  readonly ply = signal(0);
  private readonly flipOverride = signal<boolean | null>(null);
  readonly isFlipped = computed(() => this.flipOverride() ?? this.flipped());

  /** Für die Zugliste: sie braucht nur Zug, Farbe und die Stellung davor (Zugnummer). */
  readonly moves = computed(() => {
    const { sans, fens } = this.positions();
    return sans.map((san, i) => ({ san, color: fens[i].split(' ')[1], before: fens[i] }) as unknown as Move);
  });

  goTo(ply: number): void {
    this.ply.set(Math.max(0, Math.min(this.lastPly(), ply)));
  }

  /** Sprung von einem Diagramm. Steht das Brett unter dem Text, holt er es ins Bild. */
  showPly(ply: number, flipped?: boolean): void {
    this.goTo(ply);
    if (flipped !== undefined) this.flipOverride.set(flipped);
    if (window.matchMedia('(max-width: 899.98px)').matches) this.host.nativeElement.scrollIntoView({ block: 'nearest' });
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if ((event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') || !isBoardHotkey(event)) return;
    event.preventDefault();
    this.goTo(this.ply() + (event.key === 'ArrowLeft' ? -1 : 1));
  }
}
