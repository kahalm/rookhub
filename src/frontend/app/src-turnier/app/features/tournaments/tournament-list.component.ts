import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '@rh/core/snackbar.service';
import { Subscription } from '@rh/core/models';
import { TournamentListService } from '../../core/tournament-list.service';
import { OpenTournamentService } from '../../core/open-tournament.service';

/**
 * „Meine Turniere": die GEMERKTEN Turniere, und sonst nichts.
 *
 * <p><b>Was hier vorher stand</b> und warum es weg ist: eine Liste ALLER jemals geholten Turniere
 * — also auch der Turniere, die jemand anderes irgendwann einmal importiert hat — mit einem
 * Merken-, einem Ausblenden- und einem Details-Knopf pro Zeile, und darueber ein Formular, um ein
 * Turnier per chess-results-Nummer von Hand zu importieren. Beides beantwortet keine Frage, die
 * man an eine persoenliche Seite hat: gemerkt wird im KALENDER (dort sieht man, was es gibt), und
 * eine Nummer von Hand einzutippen ist kein Weg, den jemand freiwillig geht.</p>
 *
 * <p><b>Die Liste kommt aus den ABOS, nicht aus den geholten Turnieren</b> — ein Abruf statt
 * zwei. Ein gemerktes Turnier ist damit auch dann hier, wenn es noch niemand geholt hat; der
 * Klick auf den Titel stoesst das Holen an (siehe <see cref="OpenTournamentService"/>).</p>
 *
 * <p><b>Sortiert nach dem TERMIN, kommende zuerst.</b> Die Frage an eine Merkliste ist „was steht
 * als Naechstes an" — nicht „was habe ich zuletzt gemerkt". Turniere ohne Termin (Altbestand, der
 * vor dem Datumsfeld gemerkt wurde) stehen am Ende.</p>
 */
@Component({
  selector: 'app-tournament-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule,
    TranslatePipe, LoadingSpinnerComponent,
  ],
  template: `
    <div class="page">
      <h1>{{ 'tournaments.list.title' | translate }}</h1>

      @if (loading()) {
        <app-loading-spinner />
      } @else if (failed()) {
        <mat-card class="card">
          <p>{{ 'tournaments.list.crawlerUnavailable' | translate }}</p>
          <button mat-stroked-button (click)="load()">{{ 'common.retry' | translate }}</button>
        </mat-card>
      } @else if (upcoming().length === 0 && past().length === 0) {
        <!-- Leer heisst hier nicht „kaputt", sondern „noch nichts gemerkt" — und der Weg dorthin
             gehoert dazu, sonst ist die Seite eine Sackgasse. -->
        <mat-card class="card empty">
          <p>{{ 'tournaments.list.emptyBookmarks' | translate }}</p>
          <a mat-flat-button routerLink="/tournaments/calendar">
            <mat-icon>event</mat-icon> {{ 'nav.tournamentCalendar' | translate }}
          </a>
        </mat-card>
      } @else {
        @if (upcoming().length > 0) {
          <h2>{{ 'tournaments.list.upcoming' | translate }}</h2>
          <div class="rows">
            @for (sub of upcoming(); track sub.id) {
              <ng-container *ngTemplateOutlet="row; context: { $implicit: sub }" />
            }
          </div>
        }

        @if (past().length > 0) {
          <h2>{{ 'tournaments.list.past' | translate }}</h2>
          <div class="rows">
            @for (sub of past(); track sub.id) {
              <ng-container *ngTemplateOutlet="row; context: { $implicit: sub }" />
            }
          </div>
        }
      }
    </div>

    <ng-template #row let-sub>
      <mat-card class="row">
        <!-- Der TITEL ist der Weg ins Turnier. Ist es noch nicht geholt, wird der Auftrag
             eingereiht und die Seite wechselt hin, sobald er durch ist. -->
        <button type="button" class="row-open" (click)="open(sub)"
                [disabled]="opening() !== null"
                [class.busy]="opening() === sub.crawlerTournamentId">
          <span class="row-name">{{ sub.tournamentName }}</span>
          <span class="row-date muted">
            @if (sub.eventDate) { {{ sub.eventDate }} } @else { {{ 'tournaments.list.noDate' | translate }} }
          </span>
        </button>

        <button mat-icon-button class="row-drop" (click)="unbookmark(sub)"
                [disabled]="removing() === sub.id"
                [matTooltip]="'tournaments.actions.unsubscribe' | translate"
                [attr.aria-label]="'tournaments.actions.unsubscribe' | translate">
          <mat-icon>bookmark_remove</mat-icon>
        </button>
      </mat-card>
    </ng-template>
  `,
  styles: [`
    .page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 1rem; }
    h1 { font-size: 1.4rem; margin: 0 0 1rem; }
    h2 { font-size: 1rem; margin: 1.25rem 0 0.5rem; }

    .card { padding: 1rem; }
    .card.empty { display: flex; flex-direction: column; align-items: flex-start; gap: 0.75rem; }

    .rows { display: flex; flex-direction: column; gap: 0.5rem; }
    /* mat-card ist selbst „display: flex; flex-direction: column" — ohne ausdrueckliches „row"
       stapelte sich jede Merkzeile: Titel oben (zentriert, Klickflaeche nur textbreit), Icon
       darunter, Karte doppelt so hoch. */
    .row { display: flex; flex-direction: row; align-items: center; gap: 0.5rem; padding: 0.35rem 0.5rem 0.35rem 0.75rem; }

    .row-open {
      display: flex; align-items: baseline; gap: 0.75rem; flex: 1 1 auto; flex-wrap: wrap;
      /* min-width: 0 — sonst schiebt ein unbrechbarer Turniername (Unterstriche, CamelCase) bei
         360px das Icon aus der Karte und die Seite scrollt seitwaerts. */
      min-width: 0;
      min-height: 40px; padding: 0.35rem 0; border: 0; background: transparent; color: inherit;
      font: inherit; text-align: left; cursor: pointer;
    }
    .row-open:hover .row-name { text-decoration: underline; }
    .row-open:disabled { cursor: default; }
    .row-open.busy { opacity: 0.6; }

    .row-drop { flex: 0 0 auto; }

    .row-name { font-weight: 500; overflow-wrap: anywhere; }
    .row-date { font-size: 0.85rem; white-space: nowrap; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
  `],
})
export class TournamentListComponent implements OnInit {
  private readonly tournaments = inject(TournamentListService);
  private readonly opener = inject(OpenTournamentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  /** Alles aus HTTP liegt in Signalen — die Antwort trifft ausserhalb der Zone ein. */
  readonly subscriptions = signal<Subscription[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly removing = signal<number | null>(null);

  readonly opening = this.opener.opening;

  /** Kommende zuerst, nach Termin; ohne Termin gilt als kommend (wir wissen es nicht besser). */
  readonly upcoming = computed(() => this.subscriptions()
    .filter(s => !s.eventDate || s.eventDate >= this.today())
    .sort((a, b) => (a.eventDate ?? '9999').localeCompare(b.eventDate ?? '9999')));

  /** Vergangene, neueste zuerst. */
  readonly past = computed(() => this.subscriptions()
    .filter(s => !!s.eventDate && s.eventDate < this.today())
    .sort((a, b) => (b.eventDate ?? '').localeCompare(a.eventDate ?? '')));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.failed.set(false);

    this.tournaments.getSubscriptions().subscribe({
      next: subs => {
        this.subscriptions.set(subs);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  open(sub: Subscription): void {
    this.opener.open(sub.crawlerTournamentId);
  }

  /**
   * Merken zuruecknehmen. Die Zeile verschwindet SOFORT — auf eine Antwort zu warten, bevor sich
   * etwas ruehrt, laesst den Klick verloren wirken; scheitert der Aufruf, kommt sie zurueck und
   * die Meldung sagt es.
   *
   * <p><b>Mit Rueckgaengig.</b> Der Knopf ist ein reines Icon am rechten Rand, auf dem Handy ohne
   * Tooltip — ein Fehltipp waere sonst endgueltig, zurueck ginge es nur ueber den Kalender. Kein
   * Bestaetigungsdialog (eine Aktion pro Zeile, mobile Konvention = Undo). Rueckgaengig legt das
   * ALTE Objekt mit der neuen Id zurueck, nicht die Server-Antwort: so bleibt der Termin und die
   * Zeile landet wieder in ihrem Abschnitt.</p>
   */
  unbookmark(sub: Subscription): void {
    this.removing.set(sub.id);
    const before = this.subscriptions();
    this.subscriptions.set(before.filter(s => s.id !== sub.id));

    this.tournaments.unsubscribe(sub.id).subscribe({
      next: () => {
        this.removing.set(null);
        const ref = this.snackbar.show(this.translate.instant('tournaments.list.unsubscribed'),
          { action: 'common.undo', duration: 6000 });
        ref.onAction().subscribe(() => this.tournaments.subscribe(sub.crawlerTournamentId, sub.tournamentName).subscribe({
          next: created => this.subscriptions.update(list => [...list, { ...sub, id: created.id }]),
          error: () => this.snackbar.warn(this.translate.instant('tournaments.list.undoFailed')),
        }));
      },
      error: () => {
        this.subscriptions.set(before);
        this.removing.set(null);
        this.snackbar.warn(this.translate.instant('tournaments.list.unsubscribeFailed'));
      },
    });
  }

  private today(): string {
    const now = new Date();
    const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
  }
}
