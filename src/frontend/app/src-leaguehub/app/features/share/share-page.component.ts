import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { LeagueApiService } from '../../core/league-api.service';
import { tn } from '../../core/league-format';
import { SharedFixture } from '../../core/league.models';
import { FixtureViewComponent } from '../../shared/fixture-view.component';

/**
 * Geteilte Begegnung (`/s/:token`) — OHNE Anmeldung. Zeigt genau die geteilte Begegnung samt Meldeliste
 * und Spielerkarten; Online-Konten nur „sicher" (entscheidet der Server). Abgelaufen/widerrufen → „Link ungültig".
 */
@Component({
  selector: 'lh-share-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FixtureViewComponent, RouterLink],
  template: `
    @if (invalid()) {
      <section class="gate">
        <h2>Link ungültig</h2>
        <p>Dieser Link ist abgelaufen oder wurde widerrufen.</p>
      </section>
    } @else if (data(); as d) {
      <!-- Ganz oben und auffällig (Wunsch des Nutzers): wer den Link bekommt, soll seine Partien beisteuern. -->
      <section class="cta" aria-labelledby="cta-title">
        <h2 id="cta-title">Hast du gegen Spieler aus der Liga gespielt? Lade deine Partien hoch.</h2>
        <p>Ein Foto vom Partieformular oder eine PGN-Datei genügt — ohne Anmeldung. Jede Partie hilft der Vorbereitung;
          Spieler von Schwaz werden durch „Schwaz“ ersetzt, und es wird nicht gespeichert, wer hochgeladen hat.</p>
        <div class="cta-actions">
          <a class="btn-pri" [routerLink]="['/s', token, 'hochladen']" [queryParams]="{ art: 'formular' }">Partieformular fotografieren</a>
          <a class="btn-sec" [routerLink]="['/s', token, 'hochladen']">PGN hochladen</a>
        </div>
      </section>
      <p class="stand">Geteilte Begegnung, nur zum Ansehen. Stand der Daten: {{ d.generated }}, Link gültig bis {{ until(d.expires) }}.</p>
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

  async ngOnInit(): Promise<void> {
    try {
      const d = await this.api.shared(this.token);
      this.data.set(d);
      this.title.setTitle(`${tn(d.team)} – Runde ${d.round} | LeagueHub`);
    } catch {
      this.invalid.set(true);
    }
  }

  until(iso: string): string {
    return (iso || '').split('-').reverse().join('.');
  }
}
