import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { LeagueApiService } from '../core/league-api.service';

interface GameShareOut { url?: string; until?: string; copied?: boolean; text?: string; error?: boolean }

/**
 * Link auf EINE Ligapartie (Wunsch 2026-11-09: „ich will auch eine ligapartie teilen können (als angemeldeter user)"): die
 * Auslöser sitzen in `lh-fixture` (⋮-Menü) und `lh-board-game` und rufen `create`; hier steht nur das Ergebnis.
 * Der Link wird gleich kopiert; scheitert das (Rechte des Browsers), bleibt das Feld zum Markieren.
 */
@Component({
  selector: 'lh-game-share',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (out(); as o) {
      <div class="share-out" aria-live="polite">
        @if (o.url) {
          <p>{{ o.copied ? 'Link kopiert. ' : '' }}Wer diesen Link hat, sieht ohne Anmeldung nur diese Partie. Der Link läuft am {{ o.until }} ab.</p>
          <div class="linkrow">
            <input type="text" readonly [value]="o.url" aria-label="Link zur Partie">
            <button type="button" class="btn-sec" (click)="copy(o)">{{ o.copied ? 'Kopiert' : 'Kopieren' }}</button>
          </div>
        } @else {
          <p [class.err]="o.error">{{ o.text }}</p>
        }
      </div>
    }
  `,
})
export class GameShareComponent {
  private readonly api = inject(LeagueApiService);
  readonly out = signal<GameShareOut | null>(null);

  async create(tnr: number, round: number, team: string, board: number): Promise<void> {
    this.out.set({ text: 'Link wird erstellt …' });
    try {
      const r = await this.api.createGameShare(tnr, round, team, board);
      const o: GameShareOut = { url: `${location.origin}/partie/${r.token}`, until: r.expires.split('-').reverse().join('.') };
      this.out.set(o);
      await this.copy(o);
    } catch {
      this.out.set({ text: 'Link nicht erstellt.', error: true });
    }
  }

  async copy(o: GameShareOut): Promise<void> {
    try { await navigator.clipboard.writeText(o.url!); this.out.set({ ...o, copied: true }); } catch { /* Feld ist markierbar */ }
  }
}
