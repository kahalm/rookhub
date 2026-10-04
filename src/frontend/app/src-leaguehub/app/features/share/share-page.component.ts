import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Title } from '@angular/platform-browser';
import { LeagueApiService } from '../../core/league-api.service';
import { tn } from '../../core/league-format';
import { GameSources, SharedFixture } from '../../core/league.models';
import { FixtureViewComponent } from '../../shared/fixture-view.component';
import { GameSourcesComponent } from '../../shared/game-sources.component';

/**
 * Geteilte Begegnung (`/s/:token`) — OHNE Anmeldung. Zeigt genau die geteilte Begegnung samt Meldeliste
 * und Spielerkarten; Online-Konten nur „sicher" (entscheidet der Server). Abgelaufen/widerrufen (404) → „Link ungültig";
 * jeder andere Fehler (429 aus demselben Vereins-WLAN, Funkloch, 5xx) sagt das und bietet „Neu laden" — F7-009.
 */
@Component({
  selector: 'lh-share-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FixtureViewComponent, GameSourcesComponent, RouterLink],
  template: `
    @if (invalid()) {
      <section class="gate">
        <h2>Link ungültig</h2>
        <p>Dieser Link ist abgelaufen oder wurde widerrufen.</p>
      </section>
    } @else if (loadError(); as e) {
      <section class="gate">
        <h2>Gerade nicht erreichbar</h2>
        <p>{{ e }}</p>
        <div class="actions"><button type="button" class="btn-sec" (click)="load()">Neu laden</button></div>
      </section>
    } @else if (data(); as d) {
      <!-- Ganz oben und auffällig (Wunsch des Nutzers): wer den Link bekommt, soll seine Partien beisteuern. -->
      <section class="cta" aria-labelledby="cta-title">
        <h2 id="cta-title">Hast du gegen Spieler aus der Liga gespielt? Lade deine Partien hoch.</h2>
        <p>Ein Foto vom Partieformular oder eine PGN-Datei genügt — ohne Anmeldung. Jede Partie hilft der Vorbereitung;
          Spieler von Schwaz werden nach außen durch „Schwaz“ ersetzt, und es wird nicht gespeichert, wer hochgeladen hat.</p>
        <div class="cta-actions">
          <a class="btn-pri" [routerLink]="['/s', token, 'hochladen']" [queryParams]="{ art: 'formular' }">Partieformular fotografieren</a>
          <a class="btn-sec" [routerLink]="['/s', token, 'hochladen']">PGN hochladen</a>
        </div>
      </section>
      <p class="stand">Geteilte Begegnung, nur zum Ansehen. Stand der Daten: {{ d.generated }}, Link gültig bis {{ until(d.expires) }}.</p>
      @if (sources(); as s) { <lh-game-sources [sources]="s" [league]="d.league" [opponent]="d.fixture.opp" /> }
      <lh-fixture [leagueName]="d.league" [round]="d.round" [team]="d.team" [fixture]="d.fixture" [shareToken]="token" />
      <div class="foot-note">
        <p>Quelle: Paarungen und Meldelisten von chess-results.com; Partien aus Lumbra's GigaBase, der ChessBase-Megabase, der Partiedatenbank von chess-results.com und den Vereinspartien von SK Schwaz.</p>
        <p>Die Prozente kommen aus einem Modell, das an früheren Saisonen gelernt hat, wer aufgestellt wird. Die Bretter folgen der Meldeliste.</p>
      </div>
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class SharePageComponent implements OnInit {
  private readonly api = inject(LeagueApiService);
  private readonly title = inject(Title);
  readonly token = inject(ActivatedRoute).snapshot.paramMap.get('token') ?? '';
  readonly data = signal<SharedFixture | null>(null);
  readonly invalid = signal(false);
  /** Partien je Quelle wie auf der Startseite (0.627.0; als Tabelle mit Gegner seit 0.628.0); fehlt die Zählung, fehlt nur die Tabelle. */
  readonly sources = signal<GameSources | null>(null);
  /** Vorübergehender Fehler (kein 404): der Link kann gültig sein — Klartext und „Neu laden" statt „Link ungültig". */
  readonly loadError = signal<string | null>(null);

  ngOnInit(): Promise<void> {
    return this.load();
  }

  async load(): Promise<void> {
    this.loadError.set(null);
    try {
      const d = await this.api.shared(this.token);
      this.data.set(d);
      this.title.setTitle(`${tn(d.team)} – Runde ${d.round} | LeagueHub`);
    } catch (err) {
      const status = err instanceof HttpErrorResponse ? err.status : null;
      if (status === 404) this.invalid.set(true);
      else this.loadError.set(
        status === 429 ? 'Gerade kamen sehr viele Anfragen aus deinem Netz (etwa dasselbe WLAN). Bitte in einer Minute neu laden.'
        : status === 0 ? 'Der Server ist gerade nicht erreichbar — prüfe die Verbindung und lade neu.'
        : status !== null ? `Der Server hatte ein Problem (HTTP ${status}). Bitte gleich noch einmal versuchen.`
        : 'Die Begegnung ließ sich gerade nicht laden. Bitte neu laden.');
      return;
    }
    try {
      this.sources.set(await this.api.sources(this.token));
    } catch {
      this.sources.set(null);
    }
  }

  until(iso: string): string {
    return (iso || '').split('-').reverse().join('.');
  }
}
