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
import { directoryCalendarEvent } from './directory-calendar-event';
import { TournamentListService } from '../../core/tournament-list.service';
import { formatTournamentDates, formatTournamentDay } from '../../core/tournament-date';
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
        <!-- Nur mit dem Schalter „Unplausible Zeitraeume zeigen" in der Antwort (UX-039): der
             Hinweis sagt, warum dieser Eintrag sonst fehlt und dem Zeitraum nicht zu trauen ist. -->
        @if (entry.implausible) {
          <span class="badge warn implausible">{{ 'tournamentDirectory.card.implausible' | translate }}</span>
        }
        @if (ignored()) {
          <span class="badge">{{ 'tournamentDirectory.card.ignored' | translate }}</span>
        }
      </div>

      <div class="tc-actions">
        <!-- Merken setzt eine chess-results-Nummer voraus (Abo + Crawl-Auftrag tragen sie). Ein
             FIDE-Turnier hat keine, und ein Knopf, der ins Leere fuehrt, ist schlimmer als ein
             fehlender. -->
        <!-- Vorgelesen wird, was der Tooltip zeigt, plus der Turniername: der Knopf sagt so
             seinen Zustand („Merken aufheben" = schon gemerkt) und zu welchem Eintrag er gehoert —
             auf Seite 1 stehen bis zu 50 Karten mit denselben vier Symbolen. Bewusst KEIN
             aria-pressed dazu: ein wechselnder Name plus „gedrueckt" liest sich doppeldeutig
             („Merken aufheben, gedrueckt" — ist es jetzt gemerkt oder nicht?). -->
        @if (bookmarkable) {
          @let bookmarkText = (subscribed() ? 'tournamentDirectory.bookmarkRemove' : 'tournamentDirectory.bookmark') | translate;
          <button mat-icon-button (click)="bookmark()" [disabled]="busy()"
                  [matTooltip]="bookmarkText"
                  [attr.aria-label]="'tournamentDirectory.card.actionAria' | translate: { action: bookmarkText, name: entry.name }">
            <mat-icon [class.on]="subscribed()">{{ subscribed() ? 'bookmark' : 'bookmark_add' }}</mat-icon>
          </button>
        }

        @if (entry.startDate) {
          <button mat-icon-button (click)="addToCalendar()"
                  [matTooltip]="'tournamentDirectory.detail.toCalendar' | translate"
                  [attr.aria-label]="'tournamentDirectory.card.actionAria' | translate: { action: ('tournamentDirectory.detail.toCalendar' | translate), name: entry.name }">
            <mat-icon>event_available</mat-icon>
          </button>
        }

        @let ignoreText = (ignored() ? 'tournamentDirectory.card.show' : 'tournamentDirectory.card.hide') | translate;
        <button mat-icon-button (click)="toggleIgnore()" [disabled]="busy()"
                [matTooltip]="ignoreText"
                [attr.aria-label]="'tournamentDirectory.card.actionAria' | translate: { action: ignoreText, name: entry.name }">
          <mat-icon>{{ ignored() ? 'visibility' : 'visibility_off' }}</mat-icon>
        </button>

        <button mat-icon-button (click)="report()"
                [matTooltip]="'tournamentDirectory.report.cta' | translate"
                [attr.aria-label]="'tournamentDirectory.card.actionAria' | translate: { action: ('tournamentDirectory.report.cta' | translate), name: entry.name }">
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

    /* Der Name ist die EINZIGE Flaeche, die zur Detailseite fuehrt. Als Knopf ohne Padding war
       er bei einzeiligen Namen ~20 px hoch — halb so hoch wie das 40-px-Beruehrziel, ein knapp
       daneben gesetzter Tipp traf nichts. Padding vergroessert die Trefferflaeche, das negative
       Margin gleicht es aus, sodass Text und Kartenlayout genau bleiben, wo sie waren.
       position: relative hebt den Knopf in der Malreihenfolge ueber die Datumszeile darunter —
       sonst bekaeme deren Box die Tipps im ueberlappenden Streifen und die untere Haelfte des
       Zugewinns waere wirkungslos. */
    .tc-name {
      display: block;
      width: 100%;
      box-sizing: border-box;
      position: relative;
      font: inherit;
      font-weight: 500;
      text-align: left;
      background: none;
      border: 0;
      padding: 0.6rem 0;
      margin: -0.6rem 0;
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

    /* „Gemerkt" muss man SEHEN, ohne den Nachbarknopf zum Vergleich zu haben: gefuellte Marke,
       Akzentfarbe und ein getoenter Grund. Nur ein anderes Glyph (bookmark vs. bookmark_add) ist
       auf 24 px kein Unterschied, den man ohne Vergleich erkennt. */
    .tc-actions .on { color: var(--mat-sys-primary); }
    .tc-actions button:has(.on) {
      background: color-mix(in srgb, var(--mat-sys-primary) 16%, transparent);
    }

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
   * „Gemerkt" hat sich geaendert. Gebraucht von der KARTE: der Punkt zeigt diesen Zustand mit an
   * und muss sich sofort umfaerben — die Ausschnitts-Daten neu zu laden wuerde stattdessen das
   * gerade offene Popup zuschlagen.
   */
  @Output() subscribedChanged = new EventEmitter<{ entry: DirectoryEntry; subscribed: boolean }>();

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

  /** Laesst sich dieses Turnier merken? Nur mit chess-results-Nummer (Abo + Crawl-Auftrag). */
  get bookmarkable(): boolean {
    return this.entry.chessResultsId !== null;
  }

  get dateText(): string {
    const start = this.entry.startDate;
    const end = this.entry.endDate;
    // Sprachgerecht statt „2026-12-18" (Codereview F6-010, siehe tournament-date.ts).
    const lang = this.translate.currentLang();
    if (!start) return formatTournamentDay(end, lang);

    // Sind die Spieltermine bekannt, sagen sie mehr als der Zeitraum: eine Liga laeuft von
    // September bis April, gespielt wird an elf Tagen.
    const rounds = this.entry.roundDates ?? [];
    if (rounds.length > 1) {
      return this.translate.instant('tournamentDirectory.card.rounds', {
        count: rounds.length,
        from: formatTournamentDay(rounds[0].date, lang),
        to: formatTournamentDay(rounds[rounds.length - 1].date, lang),
      });
    }
    return formatTournamentDates(start, end, lang);
  }

  /**
   * „Merken" legt ein Abo an UND holt das Turnier (siehe `bookmarkAndImport`) — Termin- und
   * Ortsaenderungen werden dann gemeldet, und die Ergebnisse stehen bereit statt erst zum
   * Spielbeginn.
   */
  /**
   * Der Merken-Knopf ist ein UMSCHALTER: derselbe Knopf legt das Abo an und loest es wieder.
   *
   * <p>Vorher tat ein zweiter Klick gar nichts (`if (this.subscribed()) return`) — wer sich
   * vertippt hatte, musste das Abo woanders suchen. Ein Symbol, das seinen Zustand zeigt, muss
   * ihn auch zuruecknehmen koennen.</p>
   *
   * <p>Merken setzt eine chess-results-Nummer voraus: das Abo traegt sie, und der Crawl-Auftrag
   * braucht sie. Ein FIDE-Turnier hat keine — der Knopf erscheint dort gar nicht (siehe
   * `bookmarkable`), diese Pruefung ist der zweite Riegel.</p>
   */
  bookmark(): void {
    const chessResultsId = this.entry.chessResultsId;
    if (chessResultsId === null || this.busy()) return;
    if (this.subscribed()) {
      this.removeBookmark(chessResultsId);
      return;
    }
    this.busy.set(true);

    this.tournaments.bookmarkAndImport(chessResultsId, this.entry.name).subscribe({
      next: ({ job }) => {
        this.busy.set(false);
        this.subscribedOverride.set(true);
        this.subscribedChanged.emit({ entry: this.entry, subscribed: true });
        this.snackbar.success(this.translate.instant(
          job ? 'tournamentDirectory.bookmarkedImporting' : 'tournamentDirectory.bookmarked'));
      },
      error: () => {
        this.busy.set(false);
        this.snackbar.warn(this.translate.instant('tournamentDirectory.bookmarkError'));
      },
    });
  }

  /**
   * Merken zuruecknehmen. Das schon GEHOLTE Turnier bleibt bestehen — geloescht wird nur der
   * Vermerk „melde mir Termin- und Ortsaenderungen"; die Teilnehmerliste eines Turniers gehoert
   * nicht einem Nutzer.
   */
  private removeBookmark(chessResultsId: string): void {
    this.busy.set(true);
    this.tournaments.unsubscribeByTournament(chessResultsId).subscribe({
      next: () => {
        this.busy.set(false);
        this.subscribedOverride.set(false);
        this.subscribedChanged.emit({ entry: this.entry, subscribed: false });
        this.snackbar.success(this.translate.instant('tournamentDirectory.bookmarkRemoved'));
      },
      error: () => {
        this.busy.set(false);
        this.snackbar.warn(this.translate.instant('tournamentDirectory.bookmarkRemoveError'));
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

    this.directory.setIgnored(this.entry.id, next).subscribe({
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

  /**
   * Der Termin, wie er in den Kalender geht — `null` ohne Startdatum (dann fehlt der Knopf).
   * Dieselbe Abbildung wie auf der Detailseite ({@link directoryCalendarEvent}).
   */
  calendarEvent(): CalendarEvent | null {
    return directoryCalendarEvent(this.entry, this.translate);
  }
}
