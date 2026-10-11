import { DestroyRef, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { NotificationService } from '@rh/core/notification.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { requireAccount } from '../../core/require-account';
import { TournamentDetailService } from './tournament-detail.service';

/** Wie oft der Stand eines beobachteten Turniers abgefragt wird. */
export const ROUND_MONITOR_POLL_MS = 30000;

/**
 * „Beobachten" (Runden-Monitor) eines Turniers — EINE Fassung fuer die Detailansicht (/tournaments/:id) und den
 * geteilten Link (/t/:id).
 *
 * UI-Sweep 2026-10-11 (r2-t-public-actions): vorher stand die ganze Logik (Status laden, an/aus, 30-s-Abfrage,
 * Meldung bei neuer Runde, Ablauf) nur in `TournamentDetailComponent`; der geteilte Link schickte zum Beobachten
 * erst in die „Volle Ansicht". Statt die Abfrage ein zweites Mal abzuschreiben, liegt sie hier; was bei einer
 * neuen Runde NEU zu laden ist, entscheidet die Seite (`onNewRound`).
 *
 * Zustand in Signalen: die Antworten kommen ausserhalb der Zone an (Angular 22) — die Detailansicht (Default-CD)
 * liest sie genauso wie der geteilte Link.
 *
 * Muss im Injektionskontext entstehen (Feld-Initialisierer der Komponente): er holt seine Dienste per `inject` und
 * beendet die Abfrage selbst, wenn die Komponente zerstoert wird.
 */
export class TournamentRoundMonitor {
  readonly monitoring = signal(false);
  readonly activeUntil = signal<Date | null>(null);
  readonly toggling = signal(false);

  private lastKnownRounds = 0;
  private pollHandle: ReturnType<typeof setInterval> | null = null;

  private readonly api = inject(TournamentDetailService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly notifications = inject(NotificationService);

  /**
   * @param tournamentId liefert die Turnier-Id der Adresse (erst in ngOnInit bekannt, deshalb eine Funktion)
   * @param onNewRound laedt nach, was die Seite bei einer neuen Runde neu zeigen muss
   */
  constructor(private readonly tournamentId: () => string, private readonly onNewRound: (round: number) => void) {
    inject(DestroyRef).onDestroy(() => this.stopPoll());
  }

  /** Stand holen — nur angemeldet: die Beobachtung gehoert einem Konto, ein Gast bekaeme nur 401. */
  load(): void {
    if (!this.auth.isLoggedIn) return;
    this.api.getMonitor(this.tournamentId()).subscribe({
      next: (res) => {
        this.monitoring.set(res.active);
        this.activeUntil.set(res.activeUntil ? new Date(res.activeUntil) : null);
        if (res.active && res.lastKnownRounds) {
          this.lastKnownRounds = res.lastKnownRounds;
          this.startPoll();
        }
      },
      error: () => {},
    });
  }

  /** An/aus. Beobachten meldet neue Runden in die Glocke — abgemeldet geht es zur Anmeldung und zurueck hierher. */
  toggle(): void {
    if (!requireAccount(this.auth, this.router)) return;
    this.toggling.set(true);
    const id = this.tournamentId();
    if (this.monitoring()) {
      this.api.stopMonitor(id).subscribe({
        next: () => {
          this.monitoring.set(false);
          this.activeUntil.set(null);
          this.toggling.set(false);
          this.stopPoll();
          this.snackbar.success(this.translate.instant('tournaments.monitor.stopped'));
        },
        error: () => {
          this.toggling.set(false);
          this.snackbar.info(this.translate.instant('tournaments.monitor.stopFailed'));
        },
      });
    } else {
      this.notifications.requestPermission();
      this.api.startMonitor(id).subscribe({
        next: (res) => {
          this.monitoring.set(true);
          this.activeUntil.set(res.activeUntil ? new Date(res.activeUntil) : null);
          this.lastKnownRounds = res.lastKnownRounds || 0;
          this.toggling.set(false);
          this.startPoll();
          this.snackbar.success(this.translate.instant('tournaments.monitor.activated'));
        },
        error: () => {
          this.toggling.set(false);
          this.snackbar.info(this.translate.instant('tournaments.monitor.activateFailed'));
        },
      });
    }
  }

  private startPoll(): void {
    this.stopPoll();
    this.pollHandle = setInterval(() => this.check(), ROUND_MONITOR_POLL_MS);
  }

  /** Ein Abfrage-Schritt: abgelaufen → aus; serverseitig beendet → aus; neue Runde → melden und nachladen. */
  private check(): void {
    const until = this.activeUntil();
    if (until && new Date() > until) {
      this.markStopped();
      return;
    }
    this.api.getMonitor(this.tournamentId()).subscribe({
      next: (res) => {
        if (!res.active) {
          this.markStopped();
          return;
        }
        if (res.lastKnownRounds > this.lastKnownRounds) {
          const newRound = res.lastKnownRounds;
          this.lastKnownRounds = newRound;
          // Browser-Benachrichtigung, Snackbar als Rueckfall
          this.notifications.notify(this.translate.instant('tournaments.monitor.newRoundTitle'), {
            body: this.translate.instant('tournaments.monitor.newRoundBody', { round: newRound }),
            icon: '/favicon.ico',
          });
          this.snackbar.warn(this.translate.instant('tournaments.monitor.newRoundSnack', { round: newRound }));
          this.onNewRound(newRound);
        }
      },
    });
  }

  private markStopped(): void {
    this.monitoring.set(false);
    this.activeUntil.set(null);
    this.stopPoll();
  }

  private stopPoll(): void {
    if (this.pollHandle) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
  }
}
