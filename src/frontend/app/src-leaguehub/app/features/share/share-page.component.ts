import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Title } from '@angular/platform-browser';
import { LeagueApiService } from '../../core/league-api.service';
import { tn } from '../../core/league-format';
import { GameSources, SharedFixture } from '../../core/league.models';
import { FixtureViewComponent } from '../../shared/fixture-view.component';
import { RoundLineupsComponent } from '../../shared/round-lineups.component';
import { ClubContextService } from '../../core/club-context.service';

/**
 * Geteilte Begegnung (`/s/:token`) — OHNE Anmeldung. Zeigt genau die geteilte Begegnung samt Meldeliste
 * und Spielerkarten; Online-Konten nur „sicher" (entscheidet der Server). Abgelaufen/widerrufen (404) → „Link ungültig";
 * jeder andere Fehler (429 aus demselben Vereins-WLAN, Funkloch, 5xx) sagt das und bietet „Neu laden" — F7-009.
 * Seit 0.838.0 (Wunsch 2026-10-25: „die Aufstellung für die Runde soll auch bei den anonymen Links verfügbar sein") darunter
 * derselbe Knopf wie auf der Ligaseite, „Alle Aufstellungen der Runde N" (`lh-round-lineups` über den Link, nur lesen;
 * Zustand in der Adresse `?aufstellungen=1`).
 */
@Component({
  selector: 'lh-share-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FixtureViewComponent, RoundLineupsComponent, RouterLink],
  template: `
    @if (invalid()) {
      <section class="gate">
        <h2>Link ungültig</h2>
        <p>Dieser Link ist abgelaufen oder wurde widerrufen.</p>
      </section>
    } @else if (loadError(); as e) {
      <section class="gate" role="alert">
        <h2>Gerade nicht erreichbar</h2>
        <p>{{ e }}</p>
        <div class="actions"><button type="button" class="btn-sec" (click)="load()">Neu laden</button></div>
      </section>
    } @else if (data(); as d) {
      <!-- Ganz oben und auffällig (Wunsch des Nutzers): wer den Link bekommt, soll seine Partien beisteuern. -->
      <!-- UI-Sweep 2026-10-11 (d2-l-teilen-cta): kurz statt ~360 px — Titel, (i) und zwei Knöpfe nebeneinander; der Erklärtext
           hinter (i). Die Stand-Zeile am Handy einzeilig (Datenstand erst ab 641 px). -->
      <section class="cta" aria-labelledby="cta-title">
        <div class="cta-head">
          <!-- 0.838.2, Wunsch 2026-10-25: „Gegen die Liga gespielt?“ klang schief — es geht um die Partien dieser Runde. -->
          <h2 id="cta-title">In der Runde gespielt? Partien hochladen</h2>
          <button type="button" class="chk-btn" [attr.aria-expanded]="ctaInfo()" aria-label="Wie das Hochladen funktioniert"
                  title="Wie das Hochladen funktioniert" (click)="ctaInfo.set(!ctaInfo())">i</button>
        </div>
        @if (ctaInfo()) {
          <p class="cta-info">Ein Foto vom Partieformular oder eine PGN-Datei genügt — ohne Anmeldung. Jede Partie hilft der Vorbereitung;
            Spieler von {{ d.club?.name ?? 'unserem Verein' }} werden nach außen durch „{{ d.club?.anonName ?? 'Verein' }}“ ersetzt, und es wird
            nicht gespeichert, wer hochgeladen hat.</p>
        }
        <div class="cta-actions">
          <a class="btn-pri" [routerLink]="['/s', token, 'hochladen']" [queryParams]="{ art: 'formular' }">Formular fotografieren</a>
          <a class="btn-sec" [routerLink]="['/s', token, 'hochladen']" title="PGN-Datei hochladen">PGN</a>
        </div>
      </section>
      <p class="stand share-stand">Nur zum Ansehen<span class="desk-only"> · Stand der Daten: {{ d.generated }}</span> · <span class="desk-only">Link </span>gültig bis {{ until(d.expires) }}</p>
      <lh-fixture [leagueName]="d.league" [round]="d.round" [team]="d.team" [fixture]="d.fixture" [shareToken]="token" [sources]="sources()" />
      <!-- 0.838.0: alle Aufstellungen der Runde auch über den Link — derselbe Textknopf wie auf der Ligaseite, nur lesen. -->
      <div class="lineups-toggle share-lineups">
        <button type="button" class="btn-link" [attr.aria-expanded]="showLineups()" aria-controls="round-lineups" (click)="toggleLineups()">
          <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" d="M4 6h16M4 12h16M4 18h16M9 4v16"/></svg>
          {{ showLineups() ? 'Aufstellungen ausblenden' : 'Alle Aufstellungen der Runde ' + d.round }}
        </button>
      </div>
      @if (showLineups()) {
        <div id="round-lineups"><lh-round-lineups [shareToken]="token" [shareTeam]="d.team" [coveredFides]="covered()" /></div>
      }
      <div class="foot-note">
        <p>Quelle: Paarungen und Meldelisten von chess-results.com bzw. dem Ligamanager des Bayerischen Schachbunds; Partien aus Lumbra's
          GigaBase, der ChessBase-Megabase, der Partiedatenbank von chess-results.com und den Vereinspartien{{ d.club ? ' von ' + d.club.name : '' }}.</p>
        <p>Die Prozente kommen aus einem Modell, das an früheren Saisonen gelernt hat, wer aufgestellt wird. Die Bretter folgen der Meldeliste.</p>
      </div>
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
  styles: [`
    .share-lineups { margin: 14px 0 6px; }
    .share-lineups .btn-link { display: inline-flex; align-items: center; gap: 6px; }
  `],
})
export class SharePageComponent implements OnInit {
  private readonly api = inject(LeagueApiService);
  private readonly title = inject(Title);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  /** Der Verein des Links gilt auf dieser Seite (Karten, Paarungen): 0.698.0. */
  private readonly clubs = inject(ClubContextService);
  readonly token = this.route.snapshot.paramMap.get('token') ?? '';
  readonly data = signal<SharedFixture | null>(null);
  /** Alle Aufstellungen der Runde aufgeklappt (0.838.0) — in der Adresse `?aufstellungen=1`. */
  readonly showLineups = signal(this.route.snapshot.queryParamMap?.get('aufstellungen') === '1');
  /** FIDE-IDs bzw. Schlüssel der Meldeliste des Links — nur für sie öffnet die Spielerkarte ohne Anmeldung. */
  readonly covered = computed(() => (this.data()?.fixture.roster ?? []).map(r => r.fide ?? r.key ?? '').filter(Boolean));
  readonly invalid = signal(false);
  /** Partien je Quelle wie auf der Startseite (0.627.0; als Tabelle mit Gegner seit 0.628.0); fehlt die Zählung, fehlt nur die Tabelle. */
  readonly sources = signal<GameSources | null>(null);
  /** Vorübergehender Fehler (kein 404): der Link kann gültig sein — Klartext und „Neu laden" statt „Link ungültig". */
  readonly loadError = signal<string | null>(null);
  /** Erklärtext der Aufforderung hinter (i) (d2-l-teilen-cta). */
  readonly ctaInfo = signal(false);

  ngOnInit(): Promise<void> {
    return this.load();
  }

  async load(): Promise<void> {
    this.loadError.set(null);
    try {
      const d = await this.api.shared(this.token);
      this.data.set(d);
      this.clubs.useShareClub(d.club ?? null);
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

  toggleLineups(): void {
    this.showLineups.update(v => !v);
    void this.router.navigate([], { queryParams: { aufstellungen: this.showLineups() ? 1 : null }, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
