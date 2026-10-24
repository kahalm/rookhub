import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { formatPuzzleTime } from './puzzle-format.util';

/** Ein Puzzle der Lauf-Liste, so wie der Ergebnis-Bildschirm es zeigt. */
export interface EndlessResultPuzzle {
  puzzleNumber: number;
  puzzleId: number;
  rating: number;
  solved: boolean;
}

/**
 * Ergebnis-Bildschirme des Endlos-Modus (Zerlegung Z19): „Sieg“ (Kette durchgespielt) und „Game Over“ — auch als
 * Detail-Ansicht eines Laufs aus der History (`historyView`). Reine Darstellung: Werte kommen als Inputs, jede
 * Aktion geht als Output an `EndlessPuzzleComponent`.
 *
 * `mistakes`/`puzzles` sind die Listen des Laufs; sie ändern sich nicht mehr, sobald ein Ergebnis-Bildschirm steht
 * (der Container ersetzt sie nur, bevor er hierher wechselt).
 */
@Component({
  selector: 'app-endless-result',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatButtonModule, MatIconModule, TranslatePipe],
  templateUrl: './endless-result.component.html',
  styleUrls: ['./endless-result.component.scss'],
})
export class EndlessResultComponent {
  readonly kind = input.required<'won' | 'gameover'>();
  readonly maxRating = input(0);
  readonly solved = input(0);
  readonly lives = input(0);
  readonly level = input(0);
  readonly sessionSeconds = input(0);
  readonly mistakes = input<readonly number[]>([]);
  readonly puzzles = input<readonly EndlessResultPuzzle[]>([]);
  readonly isNewHighscore = input(false);
  readonly isLoggedIn = input(false);
  readonly lastSessionId = input<number | null>(null);
  readonly lastSessionArchived = input(false);
  readonly archiving = input(false);
  /** Lauf aus der History statt gerade beendeter Lauf → nur „Zurück zur History“. */
  readonly historyView = input(false);

  readonly playAgain = output<void>();
  readonly backToPuzzles = output<void>();
  readonly backToHistory = output<void>();
  readonly archive = output<void>();
  readonly openPuzzle = output<number>();

  formatTime(seconds: number): string {
    return formatPuzzleTime(seconds);
  }
}
