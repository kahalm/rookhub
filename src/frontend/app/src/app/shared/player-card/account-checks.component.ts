import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '@lh/core/league-api.service';
import { AccountChecks } from '@lh/core/league.models';
import { checkStatusLabel, checkSymbol, checksSummary } from './account-checks';

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
    <section class="chk" aria-label="Was geprüft wurde">
      @if (checks(); as c) {
        <p class="small chk-sum">{{ summary(c.items) }}
          @if (c.elo) { <span class="muted">Elo laut Meldeliste: {{ c.elo }}.</span> }</p>
        <ul class="chk-list">
          @for (i of c.items; track i.key) {
            <li [class]="'chk-' + i.status">
              <span class="chk-sym" [attr.title]="statusLabel(i.status)" aria-hidden="true">{{ symbol(i.status) }}</span>
              <span class="chk-label">{{ i.label }}<span class="sr-only"> ({{ statusLabel(i.status) }})</span></span>
              <span class="chk-text">{{ i.text }}</span>
            </li>
          }
        </ul>
        @if (!c.profileLoaded) { <p class="small muted">Das Profil war gerade nicht abrufbar — später nochmal aufklappen.</p> }
      } @else if (error()) {
        <p class="small err">{{ error() }}</p>
      } @else {
        <p class="small muted" role="status">Prüfe … (Profil und Partien werden frisch geholt)</p>
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

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.checks.set(await this.api.accountChecks(this.kind(), this.id()));
    } catch (err) {
      const status = err instanceof HttpErrorResponse ? err.status : 0;
      this.error.set(status === 404 ? 'Dieses Konto gibt es nicht mehr.' : 'Die Prüfung ließ sich gerade nicht laden.');
    }
  }
}
