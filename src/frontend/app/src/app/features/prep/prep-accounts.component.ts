import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslatePipe } from '@ngx-translate/core';
import { AccountSuggestion } from '@lh/core/league.models';
import { AccountSuggestionsComponent } from '@rh/shared/player-card/account-suggestions.component';
import { PrepApiService } from './prep-api.service';
import { PrepLeagueApi } from './prep-league-api';

/**
 * „Online-Konten suchen" auf der Spielerseite (Phase 4) — nur, wenn die Karte es anbietet (`accountSearch`: `prep.manage`, Schalter
 * an, FIDE-ID). Ein Knopf startet EINE Suche (die Konto-Suche von LeagueHub, einige Sekunden); die offenen Vorschläge zeigt der
 * geteilte Baustein `lh-account-suggestions` samt (i)-Prüfung — seine Aufrufe lenkt `PrepLeagueApi` auf `/api/prep/*`. Nach dem
 * Übernehmen meldet der Abschnitt `changed`, die Seite lädt die Karte neu (das Konto steht dann dort). Sagt der Server eine
 * (i)-Prüfung ab (Suche läuft, Seite bremst), steht der Grund hier (`PrepLeagueApi.checkNote`).
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
      <p class="muted small">{{ 'prep.accounts.after' | translate }}</p>
    </section>
  `,
  styles: [`
    .prep-accounts { margin-top: 14px; padding: 14px 18px; border: 1px solid var(--line); border-radius: 10px;
      background: var(--surface); color: var(--ink); }
    .prep-accounts h3 { margin: 0 0 6px; font: 600 18px/1.2 var(--cond); }
    .prep-accounts p { margin: 6px 0; }
    .actions { display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: center; margin: 10px 0; }
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

  private apply(r: { items: AccountSuggestion[]; perHour: number; remaining: number }): void {
    this.items.set(r.items);
    this.perHour.set(r.perHour);
    this.remaining.set(r.remaining);
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
    if (accepted) this.changed.emit();
  }
}

/** Absagen des Servers → Text. */
export const SCAN_ERRORS: Record<string, string> = {
  busy: 'prep.accounts.busy',
  limit: 'prep.accounts.limit',
  rateLimited: 'prep.accounts.rateLimited',
  unreachable: 'prep.accounts.unreachable',
  disabled: 'prep.accounts.disabled',
};
