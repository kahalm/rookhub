import { ChangeDetectionStrategy, Component, OnInit, inject, signal, viewChild } from '@angular/core';
import { PlayerSearchComponent } from './player-search.component';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { ClubGame, RosterPerson, SideDecision } from '../../core/club.models';
import { reasonText } from '../../core/club-format';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { de } from '../../core/league-format';
import { PlayerCardComponent } from '../../shared/player-card.component';

/**
 * Vereinspartien (`/verein`): was die Mitglieder hochgeladen haben, neueste Jahre zuerst. Lesen darf, wer LeagueHub
 * sieht (`league.view`); hinzufügen die Vereinsgruppe (`league.contribute`). Ein Klick auf einen Spieler mit FIDE-ID
 * öffnet seine Spielerkarte — die Vereinspartien stehen dort mit drin —, ohne FIDE-ID die Korrektur. Namen und Ergebnis
 * korrigiert, wer die Partie auch löschen darf (Wunsch 2026-09-28); „Schwaz" bleibt anonym.
 */
type Side = 'white' | 'black';

@Component({
  selector: 'lh-club-games-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PlayerCardComponent, PlayerSearchComponent],
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
                  @for (k of sides; track k) {
                    <td>@if (fideOf(g, k); as f) { <button type="button" class="pl" (click)="openCard(f, k === 'white' ? 'w' : 's')">{{ nameOf(g, k) }}</button> }
                        @else if (isAnon(g, k)) { <span class="anon">{{ nameOf(g, k) }}</span> }
                        @else if (g.canDelete) { <button type="button" class="pl unknown" (click)="edit(g)"
                                  [attr.title]="'Kein Spieler zugeordnet (keine FIDE-ID, also keine Spielerkarte) — zum Zuordnen klicken'">{{ nameOf(g, k) }}
                                  <span class="small muted" aria-hidden="true">✎</span></button> }
                        @else { <span>{{ nameOf(g, k) }}</span> }
                        @if (k === 'white' ? g.whiteElo : g.blackElo) { <span class="small"> {{ k === 'white' ? g.whiteElo : g.blackElo }}</span> }</td>
                  }
                  <td class="num">{{ resultText(g.result) }}</td>
                  <td class="hide-s small">{{ de(g.opening) }}</td>
                  <td class="num hide-s small">{{ moves(g) }}</td>
                  <td class="num">
                    @if (rookHub && g.uci) {
                      <a class="btn-link" [href]="analysisUrl(g)" target="_blank" rel="noopener"
                         [attr.aria-label]="'Partie ' + g.white + ' – ' + g.black + ' im Analysebrett von RookHub öffnen'">Analyse</a>
                    }
                    @if (g.canDelete) {
                    <button type="button" class="btn-link" (click)="edit(g)"
                            [attr.aria-label]="'Partie ' + g.white + ' – ' + g.black + ' bearbeiten'">Bearbeiten</button>
                    <button type="button" class="btn-link" [disabled]="deleting() === g.id" (click)="remove(g)"
                            [attr.aria-label]="'Partie ' + g.white + ' – ' + g.black + ' löschen'">Löschen</button> }</td>
                </tr>
                @if (editing(); as e) {
                  @if (e.id === g.id) {
                    <tr class="edit-row"><td colspan="7">
                      <div class="side-edit">
                        <div class="field-row">
                          @for (k of sides; track k) {
                            <div class="field">{{ k === 'white' ? 'Weiß' : 'Schwarz' }}
                              @if (isAnon(g, k)) { <span class="anon">{{ anon }}</span> <span class="small muted">bleibt anonym</span> }
                              @else {
                                <lh-player-search [client]="api" [text]="nameOf(g, k)" [label]="k === 'white' ? 'Weiß' : 'Schwarz'"
                                                  (textChange)="typed(k, $event)" (picked)="picked(k, $event)" />
                                @if (e[k]; as d) { <span class="small ok-text">{{ d.fide ? d.name + ' (FIDE ' + d.fide + ')' : 'getippt: ' + d.name }}</span> }
                                <label class="anon-inline small"><input type="checkbox" [checked]="!!e[k]?.replace" (change)="setReplace(g, k, $any($event.target).checked)" />
                                  durch „{{ anon }}" ersetzen</label>
                              }
                            </div>
                          }
                          <label class="field narrow">Ergebnis
                            <select (change)="setResult($any($event.target).value)">
                              @for (r of results; track r) { <option [value]="r" [selected]="e.result === r">{{ r === '*' ? 'unbekannt' : r }}</option> }
                            </select>
                          </label>
                        </div>
                        <div class="actions">
                          <button type="button" class="btn-pri" [disabled]="saving()" (click)="save()">{{ saving() ? 'Speichere …' : 'Speichern' }}</button>
                          <button type="button" class="btn-link" (click)="editing.set(null)">Abbrechen</button>
                          @if (editError()) { <span class="err small">{{ editError() }}</span> }
                        </div>
                      </div>
                    </td></tr>
                  }
                }
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
  readonly api = inject(ClubApiService).client();
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
  readonly sides: Side[] = ['white', 'black'];
  readonly rookHub = rookHubUrlForLeagueHub();
  readonly results = ['1-0', '0-1', '1/2-1/2', '*'];
  /** Die Partie, die gerade korrigiert wird — je Seite die Festlegung (fehlt = unverändert) und das Ergebnis. */
  readonly editing = signal<{ id: number; white: SideDecision | null; black: SideDecision | null; result: string } | null>(null);
  readonly saving = signal(false);
  readonly editError = signal<string | null>(null);
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

  nameOf(g: ClubGame, k: Side): string { return k === 'white' ? g.white : g.black; }
  fideOf(g: ClubGame, k: Side): string | null { return k === 'white' ? g.whiteFide : g.blackFide; }
  /** „Schwaz" ohne FIDE-ID an einer anonymisierten Partie — bleibt, wie es ist. */
  isAnon(g: ClubGame, k: Side): boolean { return g.anonymized && !this.fideOf(g, k) && this.nameOf(g, k) === this.anon; }

  edit(g: ClubGame): void {
    const cur = this.editing();
    if (cur?.id === g.id) { this.editing.set(null); return; }
    this.editing.set({ id: g.id, white: null, black: null, result: g.result });
    this.editError.set(null);
  }

  typed(k: Side, text: string): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, [k]: text.trim() ? { name: text.trim(), fide: null, replace: false } : null });
  }

  /** Ein Spieler von Schwaz wird zu „Schwaz" — Vorgabe wie beim Hochladen (der Server erzwingt es ohnehin). */
  picked(k: Side, p: RosterPerson): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, [k]: { name: p.name, fide: p.fide, replace: p.club } });
  }

  setReplace(g: ClubGame, k: Side, on: boolean): void {
    const e = this.editing();
    if (!e) return;
    const cur = e[k] ?? { name: this.nameOf(g, k), fide: this.fideOf(g, k), replace: false };
    this.editing.set({ ...e, [k]: { ...cur, replace: on } });
  }

  /** RookHubs Analysebrett mit der Partie (öffentlich, neuer Tab). */
  analysisUrl(g: ClubGame): string {
    return `${this.rookHub}/analysis?moves=${encodeURIComponent(g.uci ?? '')}`;
  }

  setResult(r: string): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, result: r });
  }

  async save(): Promise<void> {
    const e = this.editing();
    if (!e) return;
    this.saving.set(true);
    this.editError.set(null);
    try {
      const g = await this.api.updateGame(e.id, { white: e.white, black: e.black, result: e.result });
      this.items.set(this.items().map(x => x.id === g.id ? g : x));
      this.editing.set(null);
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? err.error?.reason : null;
      this.editError.set(reason === 'anonymous' ? '„Schwaz" bleibt anonym.' : reason ? reasonText(reason)
        : err instanceof HttpErrorResponse && err.status === 403 ? 'Diese Partie darfst du nicht ändern.' : 'Speichern hat nicht geklappt.');
    } finally {
      this.saving.set(false);
    }
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
