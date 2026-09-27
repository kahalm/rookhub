import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { readJson, writeJson, localStore } from '@rh/core/local-json-store';
import { LeagueApiService } from '../../core/league-api.service';
import { roundLabel, tn } from '../../core/league-format';
import { League, LeagueIndex } from '../../core/league.models';
import { FixtureViewComponent } from '../../shared/fixture-view.component';

const PICK_KEY = 'leaguehub';
const POLL_MS = 4000;

interface Pick { liga?: number; verein?: string }

/**
 * Admin-Ansicht: Liga, Runde und Verein wählen → die Begegnung mit den Gegner-Prognosen je Brett.
 * Die Auswahl steht in der Adresse (`?liga=&runde=&verein=`, teilbar unter Admins) und Liga + Verein
 * zusätzlich im Gerät. „Daten aktualisieren" ist ein Knopf, KEIN Zeitplan (Wunsch des Nutzers).
 */
@Component({
  selector: 'lh-league-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FixtureViewComponent],
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nur für Admins</h2>
        <p>Angemeldet als {{ username }}. LeagueHub ist vorerst nur für Admins freigeschaltet.</p>
      </section>
    } @else if (loadError()) {
      <section class="gate">
        <h2>Daten nicht geladen</h2>
        <p>{{ loadError() }}</p>
      </section>
    } @else if (index(); as ix) {
      @if (!ix.leagues.length) {
        <section class="gate">
          <h2>Noch keine Daten</h2>
          <p>Es liegt noch kein Bestand vor. Ein Admin spielt ihn einmal über den Import ein.</p>
        </section>
      } @else {
        <form class="pick" autocomplete="off" (submit)="$event.preventDefault()">
          <label>Liga
            <select (change)="pickLeague(+$any($event.target).value)">
              @for (l of ix.leagues; track l.tnr) { <option [value]="l.tnr" [selected]="l.tnr === tnr()">{{ l.name }}</option> }
            </select>
          </label>
          <label>Runde
            <select (change)="pickRound(+$any($event.target).value)">
              @for (r of league()?.rounds ?? []; track r.round) { <option [value]="r.round" [selected]="r.round === round()">{{ label(r) }}</option> }
            </select>
          </label>
          <label>Verein
            <select (change)="pickTeam($any($event.target).value)">
              @for (t of league()?.teams ?? []; track t) { <option [value]="t" [selected]="t === team()">{{ tn(t) }}</option> }
            </select>
          </label>
        </form>

        <div class="stand">
          <span>Stand der Daten: {{ ix.generated ?? '–' }}</span>
          @if (canManage) {
            <button type="button" class="btn-sec" [disabled]="updating()" (click)="startUpdate()">Daten aktualisieren</button>
          }
          <span class="update-msg" [class.err]="updateErr()" role="status" aria-live="polite">{{ updateMsg() }}</span>
        </div>

        @if (league(); as L) {
          <lh-fixture [leagueName]="L.name" [tnr]="canManage ? L.tnr : null" [round]="round()" [team]="team()"
                      [fixture]="fixture()" />
        } @else {
          <p class="muted">Lade Liga …</p>
        }
      }
    } @else {
      <p class="muted">Lade …</p>
    }

    @if (index(); as ix) {
      <div class="foot-note">
        <p>Quelle: Paarungen und Meldelisten von chess-results.com, Saisonen 2017/18 bis {{ ix.season }}.</p>
        <p>Die Prozente kommen aus einem Modell, das an rund 50 000 Einsätzen früherer Saisonen gelernt hat, wer aufgestellt wird:
          zuletzt gespielt, Einsätze in der Vorsaison, Meldeplatz, Termine anderer Vereinsteams am selben Tag. Die Bretter folgen der Meldeliste.</p>
      </div>
    }
  `,
})
export class LeaguePageComponent implements OnInit {
  private readonly api = inject(LeagueApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly allowed = this.auth.has('league.view');
  readonly canManage = this.auth.has('league.manage');
  readonly username = this.auth.currentUser?.username ?? '';

  readonly index = signal<LeagueIndex | null>(null);
  readonly league = signal<League | null>(null);
  readonly tnr = signal(0);
  readonly round = signal(0);
  readonly team = signal('');
  readonly loadError = signal<string | null>(null);
  readonly updating = signal(false);
  readonly updateMsg = signal('');
  readonly updateErr = signal(false);

  readonly fixture = computed(() => this.league()?.fixtures[this.team()]?.[String(this.round())]);
  readonly tn = tn;
  readonly label = roundLabel;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private polling = false;
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      if (this.pollTimer) clearTimeout(this.pollTimer);
    });
  }

  async ngOnInit(): Promise<void> {
    if (!this.allowed) return;
    const q = this.route.snapshot.queryParamMap;
    const saved = readJson<Pick>(localStore(), PICK_KEY) ?? {};
    const pref = {
      liga: Number(q.get('liga')) || saved.liga || 0,
      runde: Number(q.get('runde')) || 0,
      verein: q.get('verein') || saved.verein || '',
    };
    try {
      const ix = await this.api.index();
      this.index.set(ix);
      if (!ix.leagues.length) return;
      const tnr = ix.leagues.some(l => l.tnr === pref.liga) ? pref.liga : ix.leagues[0].tnr;
      await this.showLeague(tnr, pref.runde, pref.verein);
    } catch (err) {
      this.loadError.set(this.errorText(err));
    }
    if (this.canManage) {
      try {
        if ((await this.api.updateStatus()).running) this.poll();
      } catch { /* Status ist Beiwerk */ }
    }
  }

  async pickLeague(tnr: number): Promise<void> {
    await this.showLeague(tnr, 0, this.team());
  }

  pickRound(round: number): void {
    this.round.set(round);
    this.remember();
  }

  pickTeam(team: string): void {
    this.team.set(team);
    this.remember();
  }

  /** Liga laden und Runde/Verein wählen: gewünschte Runde, sonst die erste offene, sonst die letzte. */
  private async showLeague(tnr: number, wantRound: number, wantTeam: string, fresh = false): Promise<void> {
    this.tnr.set(tnr);
    this.league.set(null);
    let L: League;
    try {
      L = await this.api.league(tnr, fresh);
    } catch (err) {
      this.loadError.set(this.errorText(err));
      return;
    }
    if (this.tnr() !== tnr) return;   // inzwischen eine andere Liga gewählt
    const firstOpen = L.rounds.find(r => r.open) ?? L.rounds[L.rounds.length - 1];
    const round = L.rounds.some(r => r.round === wantRound) ? wantRound : firstOpen?.round ?? 1;
    const team = L.teams.includes(wantTeam) ? wantTeam : L.teams.includes('Schwaz') ? 'Schwaz' : L.teams[0] ?? '';
    this.round.set(round);
    this.team.set(team);
    this.league.set(L);
    this.remember();
  }

  private remember(): void {
    void this.router.navigate([], {
      queryParams: { liga: this.tnr(), runde: this.round(), verein: this.team() },
      replaceUrl: true,
    });
    writeJson(localStore(), PICK_KEY, { liga: this.tnr(), verein: this.team() } satisfies Pick);
  }

  async startUpdate(): Promise<void> {
    this.updating.set(true);
    try {
      await this.api.startUpdate();
      this.poll();
    } catch (err) {
      const status = err instanceof HttpErrorResponse ? err.status : 0;
      if (status === 409) { this.poll(); return; }
      this.updating.set(false);
      this.setMsg(status === 429 ? 'Die Daten wurden eben erst aktualisiert. Versuch es in zwei Minuten noch einmal.'
        : status === 403 ? 'Aktualisieren ist nur für Admins möglich.'
        : `Aktualisieren hat nicht geklappt (${status ? `HTTP ${status}` : 'keine Verbindung'}).`, true);
    }
  }

  private poll(): void {
    this.updating.set(true);
    this.setMsg('Aktualisiere: lade Paarungen, Aufstellungen und neue Partien der Gegner von chess-results und rechne neu. Das dauert ein paar Minuten.', false);
    // Höchstens EINE Nachfrage-Schleife: der Status-Blick beim Öffnen und ein 409 beim Klick können beide hierher führen.
    if (this.polling) return;
    this.polling = true;
    const tick = async () => {
      if (this.destroyed) return;
      let s;
      try { s = await this.api.updateStatus(); } catch { this.pollTimer = setTimeout(tick, POLL_MS); return; }
      if (s.running) { this.pollTimer = setTimeout(tick, POLL_MS); return; }
      this.polling = false;
      this.updating.set(false);
      if (s.ok) {
        this.api.clearCache();
        try {
          const ix = await this.api.index();
          this.index.set(ix);
          await this.showLeague(this.tnr(), this.round(), this.team(), true);
          this.setMsg(`Aktualisiert: ${s.message ?? ''}. Stand der Daten: ${ix.generated ?? '–'}.`, false);
        } catch (err) {
          this.setMsg(`Aktualisiert, aber neu laden hat nicht geklappt: ${this.errorText(err)}`, true);
        }
      } else {
        this.setMsg(`Aktualisierung fehlgeschlagen: ${s.message ?? 'unbekannter Fehler'}. Die bisherigen Daten bleiben stehen.`, true);
      }
    };
    this.pollTimer = setTimeout(tick, POLL_MS);
  }

  private setMsg(text: string, err: boolean): void {
    this.updateMsg.set(text);
    this.updateErr.set(err);
  }

  private errorText(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 403) return 'LeagueHub ist vorerst nur für Admins freigeschaltet.';
      if (err.status === 0) return 'Der Server ist gerade nicht erreichbar. Bitte später neu laden.';
      return `Fehler ${err.status}.`;
    }
    return String(err);
  }
}
