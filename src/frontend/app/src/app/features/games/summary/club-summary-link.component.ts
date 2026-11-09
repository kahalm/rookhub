import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
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
  imports: [RouterLink, MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    @if (state(); as s) {
      @if (s.markdown) {
        <a mat-stroked-button class="summary-link" [routerLink]="summaryLink()">
          <mat-icon>article</mat-icon> {{ 'games.summary.open' | translate }}
        </a>
      }
      @if (canRequest()) {
        @if (s.status === 'requested' || s.status === 'running') {
          <span class="summary-wait">
            <mat-icon>hourglass_top</mat-icon>
            @if (waiting(); as w) {
              <!-- Angefordert, die Hintergrund-Rechnung läuft noch: kurz in der Kopfzeile, der Fortschritt hinter dem (i)
                   (Wunsch 2026-11-09 — der lange Satz quetschte die Spielernamen zusammen). Antippen geht auch am Handy. -->
              {{ 'games.summary.requested' | translate }}
              <button type="button" class="wait-info" #tip="matTooltip" (click)="tip.toggle()"
                      [matTooltip]="('games.summary.waitingForAnalysis' | translate) + ' ' + progressText(w)"
                      [attr.aria-label]="('games.summary.waitingForAnalysis' | translate) + ' ' + progressText(w)">
                <mat-icon>info_outline</mat-icon>
              </button>
            } @else {
              {{ (s.status === 'running' ? 'games.summary.running' : 'games.summary.requested') | translate }}
              @if (elapsed(); as e) { <span class="summary-timer">{{ e }}</span> }
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
    .summary-timer { font-variant-numeric: tabular-nums; }
    .summary-wait { white-space: nowrap; }
    .wait-info { all: unset; cursor: pointer; display: inline-flex; color: inherit; border-radius: 50%; }
    .wait-info:hover, .wait-info:focus-visible { color: var(--rh-accent, currentColor); }
  `],
})
export class ClubSummaryLinkComponent implements OnInit {
  readonly gameId = input.required<number>();
  /** Darf eine vorhandene Zusammenfassung hier neu angefordert werden? Nur auf der Zusammenfassungsseite (0.916.0). */
  readonly allowAgain = input(false);
  /** `own` (0.923.0): eigene Partie — der Besitzer fordert an (Kontingent); `club`: Vereinspartie, nur Admins. */
  readonly kind = input<'club' | 'own'>('club');
  /** LeagueHub-Teilen-Link (Wunsch 2026-11-09): nur der Link zur fertigen Zusammenfassung, nichts anfordern. */
  readonly shareToken = input<string | null>(null);
  summaryLink(): (string | number)[] {
    const token = this.shareToken();
    return token !== null ? ['/s', token, 'club-games', this.gameId(), 'summary']
      : [this.kind() === 'own' ? '/games' : '/club-games', this.gameId(), 'summary'];
  }

  private service = inject(ClubGameSummaryService);
  private auth = inject(AuthService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  readonly isAdmin = this.auth.isAdmin;
  readonly canRequest = computed(() => this.shareToken() === null && (this.kind() === 'own' || this.isAdmin));
  readonly state = signal<ClubGameSummary | null>(null);
  readonly busy = signal(false);
  /** Offene Anforderung, deren Hintergrund-Rechnung noch läuft (0.914.0). */
  readonly waiting = computed<ClubGameSummaryReadiness | null>(() => {
    const s = this.state();
    const r = s?.readiness;
    return s && (s.status === 'requested' || s.status === 'running') && r && !r.ready ? r : null;
  });

  /** Sekunden, die seit der letzten Antwort des Servers vergangen sind (nur während Claude schreibt). */
  private readonly sinceLoad = signal(0);
  /** Zähler neben „wird geschrieben“: Minuten:Sekunden, `null`, solange nicht geschrieben wird. */
  readonly elapsed = computed(() => {
    const base = this.state()?.runningSeconds;
    return this.state()?.status === 'running' && base != null ? formatElapsed(base + this.sinceLoad()) : null;
  });

  constructor() {
    // Solange eine Anforderung offen ist, nachsehen (Fortschritt, fertig) — still bei Fehlern. Beim Schreiben läuft der
    // Zähler je Sekunde und das Nachsehen alle 10 s: es dauert nur wenige Minuten, er soll nicht über das Ende hinauslaufen.
    let seconds = 0;
    const timer = setInterval(() => {
      const st = this.state()?.status;
      if (st === 'running') this.sinceLoad.update(v => v + 1);
      seconds++;
      if ((st === 'running' && seconds % 10 === 0) || (st === 'requested' && seconds % 60 === 0)) this.load();
    }, 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  ngOnInit(): void { this.load(); }

  private load(): void {
    // still bei Fehlern: ohne Stand gibt es einfach keinen Knopf
    const token = this.shareToken();
    (token !== null ? this.service.getShared(token, this.gameId())
      : this.kind() === 'own' ? this.service.getOwn(this.gameId()) : this.service.get(this.gameId())).subscribe({ next: s => { this.sinceLoad.set(0); this.state.set(s); }, error: () => this.state.set(null) });
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
        this.sinceLoad.set(0);
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

/** „2:05“, ab einer Stunde „1:02:05“. */
export function formatElapsed(totalSeconds: number): string {
  const t = Math.max(0, Math.floor(totalSeconds));
  const s = String(t % 60).padStart(2, '0');
  if (t < 3600) return `${Math.floor(t / 60)}:${s}`;
  return `${Math.floor(t / 3600)}:${String(Math.floor(t / 60) % 60).padStart(2, '0')}:${s}`;
}
