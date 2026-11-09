import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { apiErrorText } from '../../../core/api-error';
import { ClubGameSummary, ClubGameSummaryReadiness, ClubGameSummaryService } from './club-game-summary.service';

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
        <a mat-stroked-button class="summary-link" [routerLink]="[kind() === 'own' ? '/games' : '/club-games', gameId(), 'summary']">
          <mat-icon>article</mat-icon> {{ 'games.summary.open' | translate }}
        </a>
      }
      @if (canRequest()) {
        @if (s.status === 'requested' || s.status === 'running') {
          <span class="summary-wait">
            <mat-icon>hourglass_top</mat-icon>
            @if (waiting(); as w) {
              <!-- 0.914.0: angefordert, aber die Hintergrund-Rechnung ist noch nicht durch — Claude schreibt erst danach -->
              <span class="wait-text">{{ 'games.summary.waitingForAnalysis' | translate }}
                <span class="wait-progress">{{ progressText(w) }}</span></span>
            } @else {
              {{ (s.status === 'running' ? 'games.summary.running' : 'games.summary.requested') | translate }}
            }
          </span>
        } @else if (kind() === 'own' && s.quotaLeft === 0 && (!s.markdown || allowAgain())) {
          <!-- 0.923.0: Tageskontingent verbraucht — sagen, statt einen Knopf anzubieten, der nur scheitert -->
          <span class="summary-wait">{{ 'games.summary.quotaUsed' | translate: { limit: s.quotaLimit } }}</span>
        } @else if (!s.markdown || allowAgain()) {
          <!-- 0.916.0 (Wunsch 2026-11-09): „Neu schreiben" nicht prominent in der Partie-Kopfzeile, nur auf der
               Zusammenfassungsseite selbst; die Kopfzeile bietet nur das erste Anfordern an. -->
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
    .summary-wait mat-icon { font-size: 18px; width: 18px; height: 18px; flex: 0 0 auto; }
    .wait-text { display: inline-flex; flex-wrap: wrap; gap: 0 6px; }
    .wait-progress { white-space: nowrap; }
  `],
})
export class ClubSummaryLinkComponent implements OnInit {
  readonly gameId = input.required<number>();
  /** Darf eine vorhandene Zusammenfassung hier neu angefordert werden? Nur auf der Zusammenfassungsseite (0.916.0). */
  readonly allowAgain = input(false);
  /** `own` (0.923.0): eigene Partie — der Besitzer fordert an (Kontingent); `club`: Vereinspartie, nur Admins. */
  readonly kind = input<'club' | 'own'>('club');

  private service = inject(ClubGameSummaryService);
  private auth = inject(AuthService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  readonly isAdmin = this.auth.isAdmin;
  readonly canRequest = computed(() => this.kind() === 'own' || this.isAdmin);
  readonly state = signal<ClubGameSummary | null>(null);
  readonly busy = signal(false);
  /** Offene Anforderung, deren Hintergrund-Rechnung noch läuft (0.914.0). */
  readonly waiting = computed<ClubGameSummaryReadiness | null>(() => {
    const s = this.state();
    const r = s?.readiness;
    return s && (s.status === 'requested' || s.status === 'running') && r && !r.ready ? r : null;
  });

  constructor() {
    // Solange eine Anforderung offen ist, jede Minute nachsehen (Fortschritt, fertig) — still bei Fehlern.
    const timer = setInterval(() => {
      const st = this.state()?.status;
      if (st === 'requested' || st === 'running') this.load();
    }, 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  ngOnInit(): void { this.load(); }

  private load(): void {
    // still bei Fehlern: ohne Stand gibt es einfach keinen Knopf
    (this.kind() === 'own' ? this.service.getOwn(this.gameId()) : this.service.get(this.gameId())).subscribe({ next: s => this.state.set(s), error: () => this.state.set(null) });
  }

  /** „Vertiefung 56/62 · Fehler-Vergleiche 0/11" — was noch fehlt, knapp. */
  progressText(r: ClubGameSummaryReadiness): string {
    const t = (k: string, p?: object) => this.translate.instant('games.summary.' + k, p);
    if (!r.hasAnalysis) return t('progressAnalysis');
    // 0.919.3 (Wunsch 09.11.: „zeig mir den Fortschritt, x/x tief/schnell“): schneller Durchgang mit Zahlen.
    if (!r.analysisDone) {
      return r.totalPositions > 0
        ? t('progressFast', { done: r.analyzedPositions ?? 0, total: r.totalPositions }) + ' · ' + t('progressRefineNext')
        : t('progressAnalysis');
    }
    const parts: string[] = [];
    if (r.refining) parts.push(t('progressRefine', { done: r.refinedPositions, total: r.totalPositions }));
    parts.push(r.comparisonsPlanned
      ? t('progressComparisons', { done: r.comparisonsDone, total: r.comparisonsTotal })
      : t('progressComparisonsPending'));
    return parts.join(' · ');
  }

  request(): void {
    this.busy.set(true);
    (this.kind() === 'own'
      ? this.service.requestOwn(this.gameId(), this.translate.getCurrentLang() || 'de')
      : this.service.request(this.gameId())).subscribe({
      next: s => {
        this.state.set(s);
        this.busy.set(false);
        const waits = !!s.readiness && !s.readiness.ready;
        this.snackbar.show(this.translate.instant(waits ? 'games.summary.requestedWaitingToast' : 'games.summary.requestedToast'));
      },
      error: e => {
        this.busy.set(false);
        this.snackbar.show(apiErrorText(e, this.translate, 'games.summary.requestFailed'));
      },
    });
  }
}
