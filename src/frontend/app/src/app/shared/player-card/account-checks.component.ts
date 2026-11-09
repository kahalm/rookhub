import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '@lh/core/league-api.service';
import { AccountCheckItem, AccountChecks } from '@lh/core/league.models';
import { checkStatusLabel, checkSymbol, checksSummary } from './account-checks';
import { injectCardTx } from './card-tx';

/**
 * Konto-Prüfung (i) (0.619.0, Wunsch 2026-09-30: „mach bei den Konten immer ein (i) und zeig an, was alles geprüft wurde").
 * Die Liste unter einem Konto bzw. Vorschlag: je Prüfung Zeichen + Ergebnis in Worten + ein Satz. Lädt beim Aufklappen —
 * der Server holt dafür das Profil frisch von Lichess bzw. chess.com.
 */
@Component({
  selector: 'lh-account-checks',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="chk" [attr.aria-label]="tx('playerCard.accounts.checksTitle', 'Was geprüft wurde')">
      @if (checks(); as c) {
        <p class="small chk-sum">{{ summaryText(c.items) }}
          @if (c.elo) { <span class="muted">{{ tx('playerCard.accounts.checks.elo', 'Elo laut Meldeliste: ' + c.elo + '.', { elo: c.elo }) }}</span> }</p>
        <ul class="chk-list">
          @for (i of c.items; track i.key) {
            <li [class]="'chk-' + i.status">
              <span class="chk-sym" [attr.title]="statusText(i.status)" aria-hidden="true">{{ symbol(i.status) }}</span>
              <span class="chk-label">{{ i.label }}<span class="sr-only"> ({{ statusText(i.status) }})</span></span>
              <span class="chk-text">{{ i.text }}</span>
            </li>
          }
        </ul>
        @if (!c.profileLoaded) { <p class="small muted">{{ tx('playerCard.accounts.checks.noProfile', 'Das Profil war gerade nicht abrufbar — später nochmal aufklappen.') }}</p> }
      } @else if (error()) {
        <p class="small err">{{ error() }}</p>
      } @else {
        <p class="small muted" role="status">{{ tx('playerCard.accounts.checks.loading', 'Prüfe … (Profil und Partien werden frisch geholt)') }}</p>
      }
    </section>
  `,
})
export class AccountChecksComponent implements OnInit {
  readonly kind = input.required<'account' | 'suggestion'>();
  readonly id = input.required<number>();

  private readonly api = inject(LeagueApiService);
  readonly checks = signal<AccountChecks | null>(null);
  readonly error = signal<string | null>(null);
  readonly symbol = checkSymbol;
  readonly statusLabel = checkStatusLabel;
  readonly summary = checksSummary;
  /** Sprache wie die Karte (Codereview F3-011); deutsche Vorgaben aus `account-checks.ts`. */
  readonly tx = injectCardTx();

  statusText(status: AccountCheckItem['status']): string {
    return this.tx(`playerCard.accounts.checks.status.${status ?? 'none'}`, checkStatusLabel(status));
  }

  /** „4 sprechen dafür, 1 macht stutzig." in der Sprache der Karte (deutsch: {@link checksSummary}). */
  summaryText(items: AccountCheckItem[]): string {
    const n = (s: AccountCheckItem['status']) => items.filter(i => i.status === s).length;
    const part = (s: 'ok' | 'weak' | 'warn' | 'fail') => {
      const count = n(s);
      return count ? this.tx(`playerCard.accounts.checks.sum.${s}${count === 1 ? 'One' : ''}`, '', { count }) : null;
    };
    const parts = (['ok', 'weak', 'warn', 'fail'] as const).map(part);
    if (parts.some(p => p === '')) return checksSummary(items);    // ohne Übersetzung: die deutsche Vorgabe
    const shown = parts.filter(p => !!p);
    return shown.length ? shown.join(', ') + '.' : this.tx('playerCard.accounts.checks.sum.none', checksSummary(items));
  }

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.checks.set(await this.api.accountChecks(this.kind(), this.id()));
    } catch (err) {
      const status = err instanceof HttpErrorResponse ? err.status : 0;
      this.error.set(status === 404 ? this.tx('playerCard.accounts.checks.gone', 'Dieses Konto gibt es nicht mehr.')
        : this.tx('playerCard.accounts.checks.loadFailed', 'Die Prüfung ließ sich gerade nicht laden.'));
    }
  }
}
