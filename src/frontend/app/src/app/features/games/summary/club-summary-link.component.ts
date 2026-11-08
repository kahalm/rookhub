import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { apiErrorText } from '../../../core/api-error';
import { ClubGameSummary, ClubGameSummaryService } from './club-game-summary.service';

/**
 * Kopfzeile der Vereinspartie (0.913.0): „Zusammenfassung" führt zur eigenen Seite, sobald es eine gibt. Ein Admin kann
 * sie anfordern bzw. neu anfordern — geschrieben wird sie vom Watcher (Claude), das dauert einige Minuten.
 */
@Component({
  selector: 'app-club-summary-link',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @if (state(); as s) {
      @if (s.markdown) {
        <a mat-stroked-button class="summary-link" [routerLink]="['/club-games', gameId(), 'summary']">
          <mat-icon>article</mat-icon> {{ 'games.summary.open' | translate }}
        </a>
      }
      @if (isAdmin) {
        @if (s.status === 'requested' || s.status === 'running') {
          <span class="summary-wait">
            <mat-icon>hourglass_top</mat-icon>
            {{ (s.status === 'running' ? 'games.summary.running' : 'games.summary.requested') | translate }}
          </span>
        } @else {
          <button mat-button class="summary-request" [disabled]="busy()" (click)="request()">
            <mat-icon>auto_awesome</mat-icon>
            {{ (s.markdown ? 'games.summary.requestAgain' : 'games.summary.request') | translate }}
          </button>
        }
      }
    }
  `,
  styles: [`
    :host { display: contents; }
    .summary-wait { display: inline-flex; align-items: center; gap: 4px; font-size: 0.85rem;
      color: var(--mat-sys-on-surface-variant); }
    .summary-wait mat-icon { font-size: 18px; width: 18px; height: 18px; }
  `],
})
export class ClubSummaryLinkComponent implements OnInit {
  readonly gameId = input.required<number>();

  private service = inject(ClubGameSummaryService);
  private auth = inject(AuthService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  readonly isAdmin = this.auth.isAdmin;
  readonly state = signal<ClubGameSummary | null>(null);
  readonly busy = signal(false);

  ngOnInit(): void {
    // still bei Fehlern: ohne Stand gibt es einfach keinen Knopf
    this.service.get(this.gameId()).subscribe({ next: s => this.state.set(s), error: () => this.state.set(null) });
  }

  request(): void {
    this.busy.set(true);
    this.service.request(this.gameId()).subscribe({
      next: s => {
        this.state.set(s);
        this.busy.set(false);
        this.snackbar.show(this.translate.instant('games.summary.requestedToast'));
      },
      error: e => {
        this.busy.set(false);
        this.snackbar.show(apiErrorText(e, this.translate, 'games.summary.requestFailed'));
      },
    });
  }
}
