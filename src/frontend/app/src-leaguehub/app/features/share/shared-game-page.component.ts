import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Title } from '@angular/platform-browser';
import { GameReplayComponent } from '@rh/shared/player-card/game-replay.component';
import { LeagueApiService } from '../../core/league-api.service';
import { SharedLeagueGame } from '../../core/league.models';
import { tn } from '../../core/league-format';

/** Geteilte Ligapartie (`/partie/:token`) — OHNE Anmeldung, nur lesend und nur diese eine Partie. 404 → „Link ungültig", alles andere „Neu laden". */
@Component({
  selector: 'lh-shared-game-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GameReplayComponent],
  template: `
    @if (invalid()) {
      <section class="gate"><h2>Link ungültig</h2><p>Dieser Link ist abgelaufen oder die Partie ist nicht mehr verfügbar.</p></section>
    } @else if (failed()) {
      <section class="gate" role="alert">
        <h2>Gerade nicht erreichbar</h2>
        <p>Die Partie konnte nicht geladen werden.</p>
        <div class="actions"><button type="button" class="btn-sec" (click)="load()">Neu laden</button></div>
      </section>
    } @else if (game(); as g) {
      <article class="fixture">
        <h2 class="match">
          <span>{{ g.white ?? '–' }}@if (g.whiteElo) { <span class="muted elo">{{ g.whiteElo }}</span>}</span><span class="vs">–</span>
          <span>{{ g.black ?? '–' }}@if (g.blackElo) { <span class="muted elo">{{ g.blackElo }}</span>}</span>
          <span class="score">{{ g.result }}</span>
        </h2>
        <p class="when">{{ g.league }}@if (g.season) { {{ g.season }}} · Runde {{ g.round }}@if (g.board) { · Brett {{ g.board }}}</p>
        @if (g.home && g.away) { <p class="when">{{ tn(g.home) }} – {{ tn(g.away) }}</p> }
        <lh-game-replay [pgn]="g.pgn" />
        <p class="stand share-stand">Nur zum Ansehen · Link gültig bis {{ until(g.expires) }}</p>
      </article>
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class SharedGamePageComponent implements OnInit {
  private readonly api = inject(LeagueApiService);
  private readonly title = inject(Title);
  private readonly token = inject(ActivatedRoute).snapshot.paramMap.get('token') ?? '';

  readonly game = signal<SharedLeagueGame | null>(null);
  readonly invalid = signal(false);
  readonly failed = signal(false);
  readonly tn = tn;

  ngOnInit(): void { void this.load(); }

  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const g = await this.api.sharedGame(this.token);
      this.game.set(g);
      this.title.setTitle(`${g.white ?? '–'} – ${g.black ?? '–'} · LeagueHub`);
    } catch (err) {
      if ((err as HttpErrorResponse)?.status === 404) this.invalid.set(true); else this.failed.set(true);
    }
  }

  until(d: string): string { return d.split('-').reverse().join('.'); }
}
