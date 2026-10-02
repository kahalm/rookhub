import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslatePipe } from '@ngx-translate/core';
import { Account, AccountSuggestion } from '@lh/core/league.models';
import { AccountSuggestionsComponent } from '@rh/shared/player-card/account-suggestions.component';
import { siteLabel } from '@rh/shared/player-card/account-format';
import { PrepApiService } from './prep-api.service';
import { PrepSuggestionList } from './prep.models';
import { PrepLeagueApi } from './prep-league-api';

/**
 * „Online-Konten suchen" auf der Spielerseite (Phase 4) — nur, wenn die Karte es anbietet (`accountSearch`: `prep.manage`, Schalter
 * an, FIDE-ID). Ein Knopf startet EINE Suche (die Konto-Suche von LeagueHub, einige Sekunden); die offenen Vorschläge zeigt der
 * geteilte Baustein `lh-account-suggestions` samt (i)-Prüfung — seine Aufrufe lenkt `PrepLeagueApi` auf `/api/prep/*`. Nach dem
 * Übernehmen meldet der Abschnitt `changed`, die Seite lädt die Karte neu (das Konto steht dann dort). Sagt der Server eine
 * (i)-Prüfung ab (Suche läuft, Seite bremst), steht der Grund hier (`PrepLeagueApi.checkNote`).
 * Darunter die eingetragenen Konten (0.639.0): umstufen und — nach Rückfrage — entfernen, damit ein Fehlgriff nicht nur in der
 * Datenbank zu beheben ist. Steht der Spieler auch in LeagueHub, nur zum Ansehen: gepflegt wird dort.
 */
@Component({
  selector: 'app-prep-accounts',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, AccountSuggestionsComponent],
  template: `
    <section class="prep-accounts" aria-labelledby="prep-accounts-title">
      <h3 id="prep-accounts-title">{{ 'prep.accounts.title' | translate }}</h3>
      <p class="muted small">{{ 'prep.accounts.intro' | translate: { perHour: perHour() } }}</p>
      <div class="actions">
        <button type="button" class="btn-sec" [disabled]="scanning() || remaining() === 0" (click)="scan()">
          {{ (scanning() ? 'prep.accounts.scanning' : 'prep.accounts.scan') | translate }}</button>
        @if (remaining() !== null) { <span class="muted small">{{ 'prep.accounts.remaining' | translate: { n: remaining() } }}</span> }
      </div>
      @if (note(); as n) { <p class="small" [class.err]="n.err" role="status">{{ n.key | translate: { n: n.n } }}</p> }
      @if (league.checkNote(); as k) { <p class="small err check-note" role="status">{{ k | translate }}</p> }
      @if (items().length) {
        <lh-account-suggestions [items]="items()" (decided)="onDecided($event.accepted)" />
      } @else if (loaded() && !scanning()) {
        <p class="muted small">{{ 'prep.accounts.none' | translate }}</p>
      }
      @if (accounts().length) {
        <h4 class="acc-title">{{ 'prep.accounts.listTitle' | translate }}</h4>
        @if (leagueHub()) { <p class="muted small league-note">{{ 'prep.accounts.leagueHub' | translate }}</p> }
        <ul class="prep-acc-list">
          @for (a of accounts(); track a.id) {
            <li>
              <div class="acc-row">
                <a [href]="a.url" target="_blank" rel="noopener">{{ label(a.site) }}: {{ a.user }}</a>
                <span class="conf" [class.sure]="isSure(a)">{{ (isSure(a) ? 'prep.accounts.sure' : 'prep.accounts.unsure') | translate }}</span>
                <span class="muted small">{{ 'prep.accounts.games' | translate: { n: a.games ?? 0 } }}</span>
              </div>
              @if (a.comment) { <div class="muted small acc-comment">{{ a.comment }}</div> }
              @if (!leagueHub()) {
                @if (confirming() === a.id) {
                  <div class="acc-actions confirm" role="group">
                    <span class="small">{{ 'prep.accounts.removeAsk' | translate: { n: a.games ?? 0 } }}</span>
                    <button type="button" class="btn-sec danger" [disabled]="working() === a.id" (click)="remove(a)">
                      {{ 'prep.accounts.removeYes' | translate }}</button>
                    <button type="button" class="btn-link" (click)="confirming.set(null)">{{ 'common.cancel' | translate }}</button>
                  </div>
                } @else {
                  <div class="acc-actions">
                    <button type="button" class="btn-link reclassify" [disabled]="working() === a.id" (click)="setSure(a, !isSure(a))">
                      {{ (isSure(a) ? 'prep.accounts.makeUnsure' : 'prep.accounts.makeSure') | translate }}</button>
                    <button type="button" class="btn-link remove" [disabled]="working() === a.id" (click)="confirming.set(a.id ?? null)">
                      {{ 'prep.accounts.remove' | translate }}</button>
                  </div>
                }
              }
            </li>
          }
        </ul>
      }
      <p class="muted small">{{ 'prep.accounts.after' | translate }}</p>
    </section>
  `,
  styles: [`
    .prep-accounts { margin-top: 14px; padding: 14px 18px; border: 1px solid var(--line); border-radius: 10px;
      background: var(--surface); color: var(--ink); }
    .prep-accounts h3 { margin: 0 0 6px; font: 600 18px/1.2 var(--cond); }
    .prep-accounts p { margin: 6px 0; }
    .actions { display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: center; margin: 10px 0; }
    .acc-title { margin: 14px 0 4px; font: 600 15px/1.2 var(--cond); }
    .prep-acc-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .prep-acc-list li { padding: 8px 10px; border: 1px solid var(--line); border-radius: 6px; }
    .acc-row { display: flex; flex-wrap: wrap; gap: 4px 10px; align-items: baseline; }
    .acc-row a { overflow-wrap: anywhere; }
    .conf { font-size: 12px; padding: 0 6px; border: 1px solid var(--line); border-radius: 4px; }
    .conf.sure { border-color: currentColor; }
    .acc-actions { display: flex; flex-wrap: wrap; gap: 6px 12px; align-items: center; margin-top: 4px; }
    .acc-comment { margin-top: 2px; }
    .danger { color: var(--red); border-color: var(--red); }
  `],
})
export class PrepAccountsComponent {
  private readonly api = inject(PrepApiService);
  /** Dieselbe Instanz, über die der eingebundene Baustein prüft (Spielerseite: `useExisting`). */
  protected readonly league = inject(PrepLeagueApi);

  readonly playerId = input.required<number>();
  /** Ein Vorschlag wurde übernommen — die Karte soll neu laden. */
  readonly changed = output<void>();

  readonly items = signal<AccountSuggestion[]>([]);
  readonly loaded = signal(false);
  readonly scanning = signal(false);
  readonly perHour = signal(0);
  readonly remaining = signal<number | null>(null);
  readonly note = signal<{ key: string; n?: number; err: boolean } | null>(null);
  /** Eingetragene Konten (0.639.0); `leagueHub` = nur ansehen, gepflegt wird in LeagueHub. */
  readonly accounts = signal<Account[]>([]);
  readonly leagueHub = signal(false);
  /** Konto, dessen Entfernen gerade nachgefragt wird. */
  readonly confirming = signal<number | null>(null);
  /** Konto, das gerade geändert wird. */
  readonly working = signal<number | null>(null);
  readonly label = siteLabel;

  constructor() {
    effect(() => {
      const id = this.playerId();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: number): Promise<void> {
    this.loaded.set(false);
    this.note.set(null);
    try {
      const r = await this.api.suggestions(id);
      if (id !== this.playerId()) return;
      this.apply(r);
    } catch {
      if (id === this.playerId()) this.note.set({ key: 'prep.accounts.loadError', err: true });
    } finally {
      if (id === this.playerId()) this.loaded.set(true);
    }
  }

  private apply(r: PrepSuggestionList): void {
    this.items.set(r.items);
    this.perHour.set(r.perHour);
    this.remaining.set(r.remaining);
    this.accounts.set(r.accounts ?? []);
    this.leagueHub.set(r.leagueHub === true);
  }

  async scan(): Promise<void> {
    const id = this.playerId();
    this.scanning.set(true);
    this.note.set(null);
    this.league.checkNote.set(null);
    try {
      const r = await this.api.scanSuggestions(id);
      if (id !== this.playerId()) return;
      this.apply(r);
      const n = r.found ?? 0;
      this.note.set({ key: n === 0 ? 'prep.accounts.foundNone' : 'prep.accounts.found', n, err: false });
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      const reason = e?.error?.reason as string | undefined;
      this.note.set({ key: SCAN_ERRORS[reason ?? ''] ?? 'prep.accounts.unreachable', err: true });
      if (reason === 'limit') this.remaining.set(0);
    } finally {
      this.scanning.set(false);
      this.loaded.set(true);
    }
  }

  onDecided(accepted: boolean): void {
    if (!accepted) return;
    this.changed.emit();
    const id = this.playerId();
    // Das neue Konto in die Liste darunter holen — still, die Meldung bleibt.
    this.api.suggestions(id).then(r => { if (id === this.playerId()) this.apply(r); }, () => undefined);
  }

  isSure(a: Account): boolean {
    return a.conf === SURE;
  }

  async setSure(a: Account, sure: boolean): Promise<void> {
    if (a.id == null) return;
    this.working.set(a.id);
    this.note.set(null);
    try {
      const r = await this.api.updateAccount(a.id, { sure });
      this.accounts.update(list => list.map(x => x.id === a.id ? { ...x, ...r } : x));
      this.note.set({ key: sure ? 'prep.accounts.madeSure' : 'prep.accounts.madeUnsure', err: false });
      this.changed.emit();
    } catch (err) {
      this.accountError(a, err);
    } finally {
      this.working.set(null);
    }
  }

  async remove(a: Account): Promise<void> {
    if (a.id == null) return;
    this.working.set(a.id);
    this.note.set(null);
    try {
      await this.api.deleteAccount(a.id);
      this.accounts.update(list => list.filter(x => x.id !== a.id));
      this.note.set({ key: 'prep.accounts.removed', err: false });
      this.changed.emit();
    } catch (err) {
      this.accountError(a, err);
    } finally {
      this.confirming.set(null);
      this.working.set(null);
    }
  }

  private accountError(a: Account, err: unknown): void {
    const reason = (err instanceof HttpErrorResponse ? err.error?.reason : undefined) as string | undefined;
    if (reason === 'leagueHub') this.leagueHub.set(true);
    if (reason === 'notFound') this.accounts.update(list => list.filter(x => x.id !== a.id));
    this.note.set({ key: ACCOUNT_ERRORS[reason ?? ''] ?? 'prep.accounts.actionFailed', err: true });
  }
}

/** So heißt „gesichert" beim Server (`LeagueOnlineAccountService.Sure`). */
const SURE = 'sicher';

/** Absagen beim Pflegen eines Kontos → Text. */
export const ACCOUNT_ERRORS: Record<string, string> = {
  leagueHub: 'prep.accounts.leagueHubOnly',
  notFound: 'prep.accounts.accountGone',
  disabled: 'prep.accounts.disabled',
};

/** Absagen des Servers → Text. */
export const SCAN_ERRORS: Record<string, string> = {
  busy: 'prep.accounts.busy',
  limit: 'prep.accounts.limit',
  rateLimited: 'prep.accounts.rateLimited',
  unreachable: 'prep.accounts.unreachable',
  disabled: 'prep.accounts.disabled',
};
