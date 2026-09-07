import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject, signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '@rh/core/snackbar.service';
import { CalendarEvent, buildIcs, downloadIcs, icsFileName } from '@rh/core/ics';
import { TournamentListService } from '../../core/tournament-list.service';
import { ReportEntryDialogComponent, ReportEntryDialogData } from './report-entry-dialog.component';
import { TournamentDirectoryService } from './tournament-directory.service';
import { DirectoryEntry } from './tournament-directory.model';

/**
 * Die Kurzansicht EINES Turniers — Name, Termin, Ort, Merkmale und vier Aktionen.
 *
 * <p><b>Warum eine Komponente fuer drei Ansichten.</b> Liste, Karte und Kalender zeigen dasselbe
 * Turnier, und vorher zeigten sie es dreimal verschieden: die Liste als Karte mit Lesezeichen, die
 * Karte als von Hand gebautes Popup ohne Aktionen, der Kalender als nackter Knopf mit dem Namen.
 * Wer auf der Karte ein Turnier fand, musste erst auf die Detailseite, um es zu merken. Dieselbe
 * Kurzansicht ueberall heisst: dieselben vier Symbole ueberall — merken, in den Kalender
 * uebertragen, ausblenden, melden.</p>
 *
 * <p><b>Die vier Aktionen macht die Komponente SELBST.</b> Alle vier betreffen genau dieses
 * Turnier und nichts sonst; sie in drei Elternkomponenten je viermal zu verdrahten waere
 * derselbe Code dreimal. Nach draussen geht nur, was den Eltern gehoert: der Sprung auf die
 * Detailseite (die Liste merkt vorher ihren Filterzustand) und die Nachricht, dass sich der
 * Ausblend-Zustand geaendert hat (die Liste laesst die Zeile dann fallen).</p>
 */
@Component({
  selector: 'app-tournament-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // MatDialogModule gehoert hierher und nicht in die Eltern: die Karte oeffnet den
  // Melde-Dialog selbst, und sie steht in drei verschiedenen Eltern (Liste, Karten-Popup,
  // Kalender-Fenster) — auf deren Importe darf sie sich nicht verlassen.
  imports: [
    CommonModule, MatButtonModule, MatDialogModule, MatIconModule, MatTooltipModule,
    TranslatePipe,
  ],
  template: `
    <div class="tc" [class.tc-overview]="overview" [class.cancelled]="entry.cancelled"
         [class.ignored]="ignored()">
      <div class="tc-head">
        <button type="button" class="tc-name" (click)="selected.emit(entry)"
                [matTooltip]="'tournamentDirectory.card.openDetail' | translate">
          {{ entry.name }}
        </button>
      </div>

      <div class="tc-line">
        <mat-icon inline>event</mat-icon>
        <span>{{ dateText }}</span>
      </div>

      @if (entry.location) {
        <div class="tc-line">
          <mat-icon inline>place</mat-icon>
          <span>{{ entry.location }}</span>
          @if (entry.geoSource === 'Region') {
            <mat-icon inline class="approx"
                      [matTooltip]="'tournamentDirectory.approximate' | translate">help_outline</mat-icon>
          }
        </div>
      }

      <!-- Bei mehreren Spielorten sagen, WELCHER gemeint ist — sonst zeigen n Punkte auf der
           Karte n-mal denselben Text und man weiss nicht, worauf man geklickt hat. -->
      @if (venueName && entry.venues.length > 1) {
        <div class="tc-line">
          <mat-icon inline>my_location</mat-icon>
          <span>{{ 'tournamentDirectory.map.thisVenue' | translate: { name: venueName } }}</span>
        </div>
      }

      <div class="tc-meta">
        @if (entry.distanceKm !== null) {
          <span class="badge">{{ 'tournamentDirectory.distance' | translate: { km: entry.distanceKm } }}</span>
        }
        <span class="badge">{{ 'tournamentDirectory.speed.' + entry.speed | translate }}</span>
        @if (entry.kind === 'Team') {
          <span class="badge">{{ 'tournamentDirectory.kind.Team' | translate }}</span>
        }
        @for (group of entry.ageGroups; track group) {
          <span class="badge">{{ 'tournamentDirectory.age.' + group | translate }}</span>
        }
        @if (entry.gender !== 'Open') {
          <span class="badge">{{ 'tournamentDirectory.gender.' + entry.gender | translate }}</span>
        }
        @if (entry.playerCount) {
          <span class="badge">{{ 'tournamentDirectory.players' | translate: { count: entry.playerCount } }}</span>
        }
        @if (entry.groupSize > 1) {
          <span class="badge">{{ 'tournamentDirectory.groupCount' | translate: { count: entry.groupSize } }}</span>
        }
        @if (entry.cancelled) {
          <span class="badge warn">{{ 'tournamentDirectory.cancelled' | translate }}</span>
        }
        @if (ignored()) {
          <span class="badge">{{ 'tournamentDirectory.card.ignored' | translate }}</span>
        }
      </div>

      <div class="tc-actions">
        <button mat-icon-button (click)="bookmark()" [disabled]="busy()"
                [matTooltip]="(subscribed() ? 'tournamentDirectory.bookmarkedTooltip' : 'tournamentDirectory.bookmark') | translate"
                [attr.aria-label]="'tournamentDirectory.bookmark' | translate">
          <mat-icon [class.on]="subscribed()">{{ subscribed() ? 'bookmark' : 'bookmark_add' }}</mat-icon>
        </button>

        @if (entry.startDate) {
          <button mat-icon-button (click)="addToCalendar()"
                  [matTooltip]="'tournamentDirectory.detail.toCalendar' | translate"
                  [attr.aria-label]="'tournamentDirectory.detail.toCalendar' | translate">
            <mat-icon>event_available</mat-icon>
          </button>
        }

        <button mat-icon-button (click)="toggleIgnore()" [disabled]="busy()"
                [matTooltip]="(ignored() ? 'tournamentDirectory.card.show' : 'tournamentDirectory.card.hide') | translate"
                [attr.aria-label]="'tournamentDirectory.card.hide' | translate">
          <mat-icon>{{ ignored() ? 'visibility' : 'visibility_off' }}</mat-icon>
        </button>

        <button mat-icon-button (click)="report()"
                [matTooltip]="'tournamentDirectory.report.cta' | translate"
                [attr.aria-label]="'tournamentDirectory.report.cta' | translate">
          <mat-icon>flag</mat-icon>
        </button>
      </div>
    </div>
  `,
  styles: [`
    .tc {
      display: flex;
      flex-direction: column;
      gap: 0.3rem;
    }

    .tc.cancelled, .tc.ignored { opacity: 0.6; }

    .tc-name {
      font: inherit;
      font-weight: 500;
      text-align: left;
      background: none;
      border: 0;
      padding: 0;
      color: inherit;
      cursor: pointer;
    }

    .tc-name:hover { text-decoration: underline; }
    .tc-name:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }

    .tc-line {
      display: flex;
      align-items: center;
      gap: 0.35rem;
      font-size: 0.85rem;
      color: color-mix(in srgb, currentColor 75%, transparent);
    }

    .approx { opacity: 0.65; }

    .tc-meta {
      display: flex;
      flex-wrap: wrap;
      gap: 0.3rem;
      margin-top: 0.2rem;
    }

    .badge {
      font-size: 0.72rem;
      padding: 1px 7px;
      border-radius: 999px;
      background: color-mix(in srgb, currentColor 10%, transparent);
    }

    .badge.warn { background: color-mix(in srgb, var(--mat-sys-error) 22%, transparent); }

    /* Die Aktionsleiste sitzt am unteren Rand und rueckt nach rechts — in der Liste stehen so
       alle Karten mit ihren Symbolen auf einer Linie, unabhaengig davon, wie viele Zeilen der
       Turniername braucht. */
    .tc-actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.1rem;
      margin-top: auto;
      padding-top: 0.2rem;
    }

    .tc-actions .on { color: var(--mat-sys-primary); }

    /* Im Karten-Popup und im Kalender-Fenster ist der Platz knapp: kleinere Symbole, kein
       zusaetzlicher Abstand oben. */
    .tc-overview .tc-actions { margin-top: 0.3rem; padding-top: 0; }
    .tc-overview .tc-name { font-size: 0.95rem; }
  `],
})
export class TournamentCardComponent {
  private readonly directory = inject(TournamentDirectoryService);
  private readonly tournaments = inject(TournamentListService);
  private readonly dialog = inject(MatDialog);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  @Input({ required: true }) entry!: DirectoryEntry;

  /** Gedrängte Fassung für Karten-Popup und Kalender-Fenster. */
  @Input() overview = false;

  /** Bei mehreren Spielorten: der Ort, dessen Punkt angeklickt wurde. */
  @Input() venueName: string | null = null;

  @Output() selected = new EventEmitter<DirectoryEntry>();

  /**
   * Der Ausblend-Zustand hat sich geaendert. Die Liste laesst die Zeile daraufhin fallen, wenn
   * ihr Filter Ausgeblendete nicht mitanzeigt — sonst bliebe eine Zeile stehen, die beim
   * naechsten Laden verschwindet.
   */
  @Output() ignoredChanged = new EventEmitter<{ entry: DirectoryEntry; ignored: boolean }>();

  /**
   * Eigene Signale fuer die drei Zustaende, die diese Komponente selbst aendert. Sie werden aus
   * dem Eintrag vorbelegt und danach hier gefuehrt: der Eintrag gehoert der Elternliste, und ihn
   * zu mutieren erreichte deren Anzeige ohnehin nicht (OnPush).
   */
  readonly busy = signal(false);
  private readonly subscribedOverride = signal<boolean | null>(null);
  private readonly ignoredOverride = signal<boolean | null>(null);

  subscribed(): boolean {
    return this.subscribedOverride() ?? this.entry.subscribed;
  }

  ignored(): boolean {
    return this.ignoredOverride() ?? this.entry.ignored;
  }

  get dateText(): string {
    const start = this.entry.startDate;
    const end = this.entry.endDate;
    if (!start) return end ?? '';

    // Sind die Spieltermine bekannt, sagen sie mehr als der Zeitraum: eine Liga laeuft von
    // September bis April, gespielt wird an elf Tagen.
    const rounds = this.entry.roundDates ?? [];
    if (rounds.length > 1) {
      return this.translate.instant('tournamentDirectory.card.rounds',
        { count: rounds.length, from: rounds[0].date, to: rounds[rounds.length - 1].date });
    }
    return !end || end === start ? start : `${start} – ${end}`;
  }

  /**
   * „Merken" legt ein Abo an UND holt das Turnier (siehe `bookmarkAndImport`) — Termin- und
   * Ortsaenderungen werden dann gemeldet, und die Ergebnisse stehen bereit statt erst zum
   * Spielbeginn.
   */
  bookmark(): void {
    if (this.subscribed() || this.busy()) return;
    this.busy.set(true);

    this.tournaments.bookmarkAndImport(this.entry.chessResultsId, this.entry.name).subscribe({
      next: ({ job }) => {
        this.busy.set(false);
        this.subscribedOverride.set(true);
        this.snackbar.success(this.translate.instant(
          job ? 'tournamentDirectory.bookmarkedImporting' : 'tournamentDirectory.bookmarked'));
      },
      error: () => {
        this.busy.set(false);
        this.snackbar.warn(this.translate.instant('tournamentDirectory.bookmarkError'));
      },
    });
  }

  /** Der Termin geht an den Kalender des Geraets — ueber keinen fremden Dienst. */
  addToCalendar(): void {
    const event = this.calendarEvent();
    if (!event) return;

    if (!downloadIcs(buildIcs(event), icsFileName(this.entry.name))) {
      this.snackbar.warn(this.translate.instant('tournamentDirectory.detail.calendarFailed'));
    }
  }

  toggleIgnore(): void {
    if (this.busy()) return;
    const next = !this.ignored();
    this.busy.set(true);

    this.directory.setIgnored(this.entry.chessResultsId, next).subscribe({
      next: () => {
        this.busy.set(false);
        this.ignoredOverride.set(next);
        this.snackbar.success(this.translate.instant(
          next ? 'tournamentDirectory.card.hidden' : 'tournamentDirectory.card.shown'));
        this.ignoredChanged.emit({ entry: this.entry, ignored: next });
      },
      error: () => {
        this.busy.set(false);
        this.snackbar.warn(this.translate.instant('tournamentDirectory.card.hideError'));
      },
    });
  }

  report(): void {
    const data: ReportEntryDialogData = { entry: this.entry };
    this.dialog.open(ReportEntryDialogComponent, { data, width: '560px', maxHeight: '90vh' });
  }

  private calendarEvent(): CalendarEvent | null {
    const entry = this.entry;
    if (!entry.startDate) return null;

    const lines = [
      entry.location,
      entry.timeControl,
      entry.rounds ? this.translate.instant('tournamentDirectory.detail.roundsCount', { count: entry.rounds }) : null,
      entry.organizer,
      `https://chess-results.com/tnr${entry.chessResultsId}.aspx?lan=1`,
    ].filter((l): l is string => !!l);

    return {
      uid: `directory-${entry.chessResultsId}@rookhub`,
      title: entry.name,
      start: entry.startDate,
      end: entry.endDate ?? entry.startDate,
      location: entry.location ?? undefined,
      description: lines.join('\n'),
    };
  }
}
