import {
  ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, linkedSignal, signal, untracked,
} from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { GameRoast, GamesService } from './games.service';

/** „Kurz erzählt" entsteht nach der Analyse in ein paar Sekunden — so oft und so lange fragt die eigene Seite nach. */
const RECAP_TRIES = 8;
const RECAP_RETRY_MS = 15_000;

/**
 * „Kurz erzählt" (0.541.0) bzw. der freigegebene Roast (0.742.0) über dem Brett der Partieseite — herausgezogen aus
 * `SharedGameComponent` (Hausregel „große Dateien beim Anfassen verkleinern"), Verhalten unverändert.
 *
 * Der Teilen-Link bringt den Text gleich mit (`text`); die eigene Seite holt ihn, sobald die Analyse fertig ist — er
 * entsteht dann gerade (oder, bei einer älteren Analyse, durch genau diesen Abruf), deshalb fragt sie ein paar Mal nach.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-game-recap',
  standalone: true,
  imports: [MatIconModule, MatTooltipModule, TranslatePipe],
  host: { '[class.has-text]': '!!roast() || !!recap()' },
  template: `
    <!-- Mit Roast geteilt (0.742.0, /g/{token}?roast={id}): der freigegebene Roast statt „Kurz erzählt". -->
    @if (roast(); as r) {
      <p class="recap roast" [class.open]="open()" (click)="open.set(!open())">
        <span class="roast-icon" [matTooltip]="'games.roast.title' | translate" [attr.aria-label]="'games.roast.title' | translate">🔥</span>
        <span class="recap-text">{{ r.text }}</span>
      </p>
    } @else if (recap(); as text) {
      <!-- „Kurz erzählt" (0.541.0): dieselbe Zeile wie in der Link-Vorschau, vom Sprachmodell aus der Analyse. -->
      <!-- Am Handy auf drei Zeilen gekürzt; Antippen zeigt alles (v1-rk-first-view-lead-long-mobile). -->
      <p class="recap" [class.open]="open()" (click)="open.set(!open())">
        <mat-icon [matTooltip]="'games.recap.hint' | translate" [attr.aria-label]="'games.recap.hint' | translate">auto_stories</mat-icon>
        <span class="recap-text">{{ text }}</span>
      </p>
    }
  `,
  styles: [`
    :host { display: block; }
    /* Breite 0 + Mindestbreite 100 %: der Satz trägt nichts zur Breite der Karte bei — sie umschließt Brett und Zugliste,
       ein langer Absatz dehnte sie sonst auf die ganze Seite. */
    .recap {
      width: 0; min-width: 100%; box-sizing: border-box; margin: 0 0 14px; padding: 10px 14px;
      display: flex; gap: 10px; align-items: flex-start; border-radius: 10px;
      background: color-mix(in srgb, var(--rh-accent) 7%, transparent);
      font-size: 0.92rem; line-height: 1.5; color: color-mix(in srgb, currentColor 85%, transparent);
    }
    .recap.roast { background: color-mix(in srgb, #e64a19 9%, transparent); white-space: pre-wrap; }
    .roast-icon { flex: 0 0 auto; font-size: 18px; line-height: 1.3; }
    .recap mat-icon { flex: 0 0 auto; font-size: 20px; width: 20px; height: 20px; margin-top: 1px; color: var(--rh-accent); }
    @media (max-width: 768px) {
      .recap { margin: 0 16px 12px; width: auto; min-width: 0; cursor: pointer; }
      /* „Kurz erzählt" höchstens drei Zeilen (4.6 Regel 5), Antippen zeigt alles. */
      .recap:not(.open) > .recap-text {
        display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden;
      }
    }
  `],
})
export class GameRecapComponent {
  private readonly games = inject(GamesService);

  /** Text, den der Teilen-Link mitbringt (`SharedGame.recap`). */
  readonly text = input<string | null | undefined>(null);
  /** Der freigegebene Roast hinter `?roast=` — steht dann statt „Kurz erzählt". */
  readonly roast = input<GameRoast | null>(null);
  /** Eigene Partie (`/games/:id`): nur dort wird nachgefragt. */
  readonly own = input(false);
  readonly gameId = input<number | null>(null);
  /** Analyse fertig — erst dann gibt es etwas zu erzählen. */
  readonly analysisDone = input(false);

  /** Angezeigter Text: der mitgebrachte, bis ein Abruf ihn liefert. */
  readonly recap = linkedSignal<string | null>(() => this.text() ?? null);
  /** Ganz zeigen — am Handy sonst auf drei Zeilen gekürzt (v1-rk-first-view-lead-long-mobile). */
  readonly open = signal(false);

  private tries = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private readonly load = effect(() => {
    if (!this.analysisDone()) return;
    untracked(() => {
      if (this.own() && this.gameId() && !this.recap() && this.tries === 0) this.fetch();
    });
  });
  private readonly stopOnDestroy = inject(DestroyRef).onDestroy(() => {
    if (this.timer) clearTimeout(this.timer);
  });

  private fetch(): void {
    const id = this.gameId();
    if (!id) return;
    this.tries++;
    this.games.recap(id).subscribe({
      next: r => {
        if (r.text) { this.recap.set(r.text); return; }
        if (r.pending && this.tries < RECAP_TRIES) this.timer = setTimeout(() => this.fetch(), RECAP_RETRY_MS);
      },
      error: () => { /* ohne Nacherzählung bleibt die Seite wie bisher */ },
    });
  }
}
