import { ChangeDetectionStrategy, Component, OnInit, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { ClubGame } from '../../core/club.models';
import { de } from '../../core/league-format';
import { PlayerCardComponent } from '../../shared/player-card.component';

/**
 * Vereinspartien (`/verein`): was die Mitglieder hochgeladen haben, neueste Jahre zuerst. Lesen darf, wer LeagueHub
 * sieht (`league.view`); hinzufügen die Vereinsgruppe (`league.contribute`). Ein Klick auf einen Ligaspieler öffnet
 * seine Spielerkarte — die Vereinspartien stehen dort mit drin.
 */
@Component({
  selector: 'lh-club-games-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PlayerCardComponent],
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nicht freigeschaltet</h2>
        <p>Angemeldet als {{ username }}. Die Vereinspartien sehen Admins und die Vereinsgruppe von SK Schwaz.</p>
      </section>
    } @else {
      <section class="club-intro">
        <h2>Vereinspartien</h2>
        <p class="muted">Partien, die Mitglieder von SK Schwaz hochgeladen haben — sie stehen auch auf den Spielerkarten der
          Gegner. Vom Datum bleibt nur das Jahr; „Schwaz" steht für ein Mitglied, das seinen Namen nicht zeigt.</p>
      </section>

      <form class="club-search" role="search" (submit)="$event.preventDefault(); search(q.value)">
        <input #q type="search" name="q" placeholder="Spieler oder Veranstaltung" aria-label="Suchen" [value]="query()" />
        <button type="submit" class="btn-sec">Suchen</button>
        @if (query()) { <button type="button" class="btn-link" (click)="q.value = ''; search('')">Alle zeigen</button> }
      </form>

      <div class="stand">
        <span>{{ total() === null ? 'Lade …' : countText() }}</span>
        @if (total()) { <button type="button" class="btn-sec" [disabled]="downloading()" (click)="download()">PGN herunterladen</button> }
        @if (canContribute) { <a class="btn-pri" routerLink="/verein/neu">Partien hinzufügen</a> }
        <span class="update-msg" [class.err]="!!error()" role="status">{{ error() ?? notice() ?? '' }}</span>
      </div>

      @if (total() === 0) {
        <section class="empty">
          <h2>{{ query() ? 'Nichts gefunden' : 'Noch keine Vereinspartien' }}</h2>
          @if (!query() && canContribute) {
            <p>Lade eine PGN-Datei hoch oder lies ein Partieformular ein — jede Partie mit einem Ligaspieler hilft der Vorbereitung.</p>
          }
        </section>
      } @else if (items().length) {
        <div class="roster-scroll">
          <table class="rtable club-table">
            <thead><tr><th class="num">Jahr</th><th>Weiß</th><th>Schwarz</th><th class="num">Ergebnis</th>
              <th class="hide-s">Eröffnung</th><th class="num hide-s">Züge</th><th><span class="sr">Aktionen</span></th></tr></thead>
            <tbody>
              @for (g of items(); track g.id) {
                <tr>
                  <td class="num">{{ g.year ?? '–' }}</td>
                  <td>@if (g.whiteFide) { <button type="button" class="pl" (click)="openCard(g.whiteFide, 'w')">{{ g.white }}</button> }
                      @else { <span [class.anon]="g.white === anon">{{ g.white }}</span> }
                      @if (g.whiteElo) { <span class="small"> {{ g.whiteElo }}</span> }</td>
                  <td>@if (g.blackFide) { <button type="button" class="pl" (click)="openCard(g.blackFide, 's')">{{ g.black }}</button> }
                      @else { <span [class.anon]="g.black === anon">{{ g.black }}</span> }
                      @if (g.blackElo) { <span class="small"> {{ g.blackElo }}</span> }</td>
                  <td class="num">{{ resultText(g.result) }}</td>
                  <td class="hide-s small">{{ de(g.opening) }}</td>
                  <td class="num hide-s small">{{ moves(g) }}</td>
                  <td class="num">@if (g.canDelete) {
                    <button type="button" class="btn-link" [disabled]="deleting() === g.id" (click)="remove(g)"
                            [attr.aria-label]="'Partie ' + g.white + ' – ' + g.black + ' löschen'">Löschen</button> }</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (items().length < (total() ?? 0)) {
          <p><button type="button" class="btn-sec" [disabled]="loading()" (click)="more()">Weitere laden</button></p>
        }
      }
      <lh-player-card />
    }
  `,
})
export class ClubGamesPageComponent implements OnInit {
  private readonly api = inject(ClubApiService).client();
  private readonly auth = inject(AuthService);
  private readonly card = viewChild(PlayerCardComponent);

  readonly allowed = this.auth.has('league.view');
  readonly canContribute = this.auth.has('league.contribute');
  readonly username = this.auth.currentUser?.username ?? '';
  readonly anon = 'Schwaz';
  readonly de = de;

  readonly items = signal<ClubGame[]>([]);
  readonly total = signal<number | null>(null);
  readonly query = signal('');
  readonly loading = signal(false);
  readonly downloading = signal(false);
  readonly deleting = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  /** Rückmeldung der Formular-Korrektur („übernommen"), per Router-Zustand mitgebracht. */
  readonly notice = signal<string | null>((history.state as { msg?: string } | null)?.msg ?? null);
  private page = 1;
  private seq = 0;

  ngOnInit(): void {
    if (this.allowed) void this.load(1);
  }

  search(q: string): void {
    this.query.set(q.trim());
    void this.load(1);
  }

  more(): void {
    void this.load(this.page + 1);
  }

  private async load(page: number): Promise<void> {
    const my = ++this.seq;
    this.loading.set(true);
    this.error.set(null);
    try {
      const r = await this.api.list(null, this.query() || null, page);
      if (my !== this.seq) return;
      this.page = r.page;
      this.items.set(page === 1 ? r.items : [...this.items(), ...r.items]);
      this.total.set(r.total);
    } catch (err) {
      if (my === this.seq) this.error.set(this.errorText(err));
    } finally {
      if (my === this.seq) this.loading.set(false);
    }
  }

  countText(): string {
    const n = this.total() ?? 0;
    return `${n} ${n === 1 ? 'Partie' : 'Partien'}${this.query() ? ` zu „${this.query()}"` : ''}`;
  }

  resultText(r: string): string {
    return r === '1/2-1/2' ? '½–½' : r === '*' ? '–' : r.replace('-', '–');
  }

  moves(g: ClubGame): number {
    return Math.ceil(g.plies / 2);
  }

  openCard(fide: string, color: 'w' | 's'): void {
    void this.card()?.open(fide, color, null, null);
  }

  async remove(g: ClubGame): Promise<void> {
    if (!confirm(`Partie ${g.white} – ${g.black}${g.year ? ` (${g.year})` : ''} aus der Vereins-Datenbank löschen?`)) return;
    this.deleting.set(g.id);
    try {
      await this.api.deleteGame(g.id);
      this.items.set(this.items().filter(x => x.id !== g.id));
      this.total.set(Math.max(0, (this.total() ?? 1) - 1));
    } catch (err) {
      this.error.set(err instanceof HttpErrorResponse && err.status === 403
        ? 'Diese Partie darfst du nicht löschen.' : 'Löschen hat nicht geklappt.');
    } finally {
      this.deleting.set(null);
    }
  }

  async download(): Promise<void> {
    this.downloading.set(true);
    try {
      const blob = await this.api.pgn(null, this.query() || null);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'vereinspartien.pgn';
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch {
      this.error.set('Die PGN-Datei konnte nicht geladen werden.');
    } finally {
      this.downloading.set(false);
    }
  }

  private errorText(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 403) return 'Die Vereinspartien sind für dein Konto nicht freigeschaltet.';
      if (err.status === 0) return 'Der Server ist gerade nicht erreichbar.';
      return `Fehler ${err.status}.`;
    }
    return String(err);
  }
}
