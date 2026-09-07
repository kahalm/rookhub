import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap, take, takeWhile, timer } from 'rxjs';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '@rh/core/snackbar.service';
import { CrawlJob, Tournament } from '@rh/core/models';
import { CalendarEvent, buildIcs, downloadIcs, icsFileName } from '@rh/core/ics';
import { TournamentListService } from '../../core/tournament-list.service';
import { ReportEntryDialogComponent, ReportEntryDialogData } from './report-entry-dialog.component';
import { TournamentDirectoryService } from './tournament-directory.service';
import { TournamentMapComponent } from './tournament-map.component';
import { DirectoryEntry } from './tournament-directory.model';

/**
 * Ein Turnier aus dem Verzeichnis als eigene Seite.
 *
 * <p>Vorher war das eine Karte, die unter dem Kalender aufklappte — mit allen Nachteilen einer
 * Ansicht ohne Adresse: nicht teilbar, nicht als Lesezeichen zu sichern, der Zurueck-Knopf des
 * Browsers fuehrte aus dem Kalender heraus statt aus dem Detail. Der Eintrag wird hier bewusst
 * NEU geholt statt vom Kalender mitgereicht: der Weg hierher ist auch ein Link aus einer
 * Benachrichtigung, und ein abgesagtes Turnier steht in keiner gefilterten Liste mehr.</p>
 *
 * <p><b>Der Anzeigezustand liegt in SIGNALEN, nicht in einfachen Feldern</b> — und das ist hier
 * kein Stilfrage. Gemessen (0.413.2): die Antwort des HTTP-Aufrufs kommt AUSSERHALB der
 * Angular-Zone an (<c>NgZone.isInAngularZone()</c> ist im Abonnenten <c>false</c>), eine
 * Feldzuweisung loest dort also keine Aenderungserkennung aus. Auf belebten Seiten faellt das
 * nicht auf, weil irgendetwas anderes gleich darauf einen Durchlauf ausloest — diese Seite ist
 * still, und sie blieb im Ladezustand stehen, obwohl die Daten laengst da waren. Ein
 * Signal-Schreibvorgang meldet sich beim Aenderungserkennungs-Planer selbst und ist von der Zone
 * unabhaengig.</p>
 */
@Component({
  selector: 'app-tournament-directory-detail',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule,
    TranslatePipe, LoadingSpinnerComponent, TournamentMapComponent,
  ],
  templateUrl: './tournament-directory-detail.component.html',
  styleUrls: ['./tournament-directory-detail.component.scss'],
})
export class TournamentDirectoryDetailComponent implements OnInit {
  private readonly directory = inject(TournamentDirectoryService);
  private readonly tournaments = inject(TournamentListService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

  readonly entry = signal<DirectoryEntry | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly tilesFailed = signal(false);

  /** Das schon geholte Turnier, falls es eines gibt — dann fuehrt ein Knopf zu Ergebnissen. */
  readonly imported = signal<Tournament | null>(null);
  /** Laeuft gerade ein Holen-Auftrag (nach dem Merken)? */
  readonly importing = signal(false);

  /** Laeuft gerade ein Abo-Schreibvorgang? Sperrt den Knopf gegen den Doppelklick. */
  readonly busy = signal(false);

  ngOnInit(): void {
    this.route.paramMap.pipe(
      switchMap(params => {
        this.loading.set(true);
        this.notFound.set(false);
        this.entry.set(null);
        this.imported.set(null);
        return this.directory.get(params.get('id') ?? '').pipe(catchError(() => of(null)));
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(entry => {
      this.loading.set(false);
      this.entry.set(entry);
      this.notFound.set(entry === null);
      if (entry) this.lookupImported(entry);
    });
  }

  /**
   * Mittelpunkt fuer die kleine Karte. Als `computed` ist er von selbst STABIL: solange sich der
   * Eintrag nicht aendert, kommt dasselbe Objekt zurueck. Ein Getter mit frischem Objektliteral
   * liess die Karte in jedem Durchlauf neu einpassen — genau der Zoom-Fehler im Kalender.
   */
  readonly mapCentre = computed<{ lat: number; lon: number; radiusKm: number } | null>(() => {
    const e = this.entry();
    if (!e || e.lat == null || e.lon == null) return null;
    // Kein Umkreis, nur ein Ausschnitt: 6 km Halbkante zeigt Ort und Umgebung.
    return { lat: e.lat, lon: e.lon, radiusKm: 6 };
  });

  readonly pins = computed<DirectoryEntry[]>(() => {
    const e = this.entry();
    return e && e.lat != null ? [e] : [];
  });

  /**
   * „Falsches Event melden". Der Dialog fragt nicht nur, was falsch ist, sondern auch, wie solche
   * Turniere in der Region des Melders heissen — genau daran laesst sich die Einordnung kuenftiger
   * Ausgaben derselben Reihe verbessern.
   */
  reportEntry(): void {
    const entry = this.entry();
    if (!entry) return;
    const data: ReportEntryDialogData = { entry };
    this.dialog.open(ReportEntryDialogComponent, { data, width: '560px', maxHeight: '90vh' });
  }

  chessResultsUrl(id: string): string {
    return `https://chess-results.com/tnr${id}.aspx?lan=1`;
  }

  /**
   * Den Termin in den eigenen Kalender uebertragen — als .ics-Datei.
   *
   * <p>Ein Weg fuer alle Systeme: Android bietet beim Oeffnen den Kalender an, iOS uebergibt die
   * Datei direkt an Kalender, am Rechner uebernimmt sie Outlook, Apple Kalender oder
   * Thunderbird. Ein `intent://`-Link waere kuerzer, wirkt aber nur in Chrome fuer Android — und
   * der Termin laeuft hier ueber KEINEN fremden Dienst, was bei „privater Kalender" der Punkt
   * ist. Details in `core/ics.ts`.</p>
   */
  addToCalendar(): void {
    const event = this.calendarEvent();
    if (!event) return;
    if (downloadIcs(buildIcs(event), icsFileName(event.title))) {
      this.snackbar.success(this.translate.instant('tournamentDirectory.detail.calendarDone'));
    } else {
      this.snackbar.warn(this.translate.instant('tournamentDirectory.detail.calendarFailed'));
    }
  }

  /**
   * Der Termin, wie er in den Kalender geht — `null`, solange kein Startdatum bekannt ist (ohne
   * Datum gibt es keinen Termin, und der Knopf ist dann auch nicht zu sehen).
   *
   * <p>Oeffentlich, weil hier die ganze Abbildung Turnier → Termin steckt: sie laesst sich so
   * pruefen, ohne einen Download auszuloesen.</p>
   */
  calendarEvent(): CalendarEvent | null {
    const e = this.entry();
    if (!e?.startDate) return null;

    // Nur, was auch stimmt: chess-results liefert Rundenzahl und Gemeldete nicht immer.
    const facts = [
      e.timeControl,
      e.rounds ? this.translate.instant('tournamentDirectory.detail.rounds') + ': ' + e.rounds : null,
      e.playerCount ? this.translate.instant('tournamentDirectory.players', { count: e.playerCount }) : null,
      e.organizer ? this.translate.instant('tournamentDirectory.detail.organizer') + ': ' + e.organizer : null,
      e.chessResultsId === null ? null : this.chessResultsUrl(e.chessResultsId),
    ].filter(Boolean);

    return {
      // Dieselbe Kennung aktualisiert den Termin spaeter, statt ihn zu verdoppeln.
      uid: `chess-results-${e.id}@rookhub`,
      title: e.name,
      start: e.startDate,
      end: e.endDate,
      location: e.location,
      description: facts.join('\n'),
      url: e.chessResultsId === null ? null : this.chessResultsUrl(e.chessResultsId),
    };
  }

  onTilesFailed(): void {
    this.tilesFailed.set(true);
  }

  /**
   * „Merken" legt das Abo an UND holt das Turnier (siehe
   * `TournamentListService.bookmarkAndImport`). Hier wird der Auftrag zusaetzlich VERFOLGT: ist er
   * durch, erscheint der Knopf zu Teilnehmern und Ergebnissen von selbst — sonst muesste man die
   * Seite neu laden, um zu sehen, dass das Merken etwas gebracht hat.
   */
  bookmark(): void {
    const entry = this.entry();
    if (!entry) return;
    if (entry.chessResultsId === null) return;
    this.tournaments.bookmarkAndImport(entry.chessResultsId, entry.name).subscribe({
      next: ({ job }) => {
        // Neues Objekt statt Mutation: ein Signal meldet nur eine geaenderte REFERENZ.
        this.entry.set({ ...entry, subscribed: true });
        this.snackbar.success(this.translate.instant(
          job ? 'tournamentDirectory.bookmarkedImporting' : 'tournamentDirectory.bookmarked'));
        if (job) this.watchImport(job.id, entry);
      },
      error: () => this.snackbar.warn(this.translate.instant('tournamentDirectory.bookmarkError')),
    });
  }

  /**
   * Merken zuruecknehmen — auch das gehoert auf DIE Seite, auf der man es gesetzt hat. Geloescht
   * wird nur der Vermerk „melde mir Termin- und Ortsaenderungen"; das schon geholte Turnier samt
   * Teilnehmern und Tabelle bleibt (es gehoert nicht einem Nutzer).
   */
  removeBookmark(): void {
    const entry = this.entry();
    if (!entry?.chessResultsId || this.busy()) return;
    this.busy.set(true);

    this.tournaments.unsubscribeByTournament(entry.chessResultsId).subscribe({
      next: () => {
        this.busy.set(false);
        // Neues Objekt statt Mutation: ein Signal meldet nur eine geaenderte REFERENZ.
        this.entry.set({ ...entry, subscribed: false });
        this.snackbar.success(this.translate.instant('tournamentDirectory.bookmarkRemoved'));
      },
      error: () => {
        this.busy.set(false);
        this.snackbar.warn(this.translate.instant('tournamentDirectory.bookmarkRemoveError'));
      },
    });
  }

  /**
   * Verfolgt den Holen-Auftrag, bis er fertig ist — hoechstens rund zwei Minuten. Danach wird
   * nicht weitergefragt: der Auftrag laeuft serverseitig ohnehin weiter, und die naechste
   * Seitenansicht sieht das Ergebnis. Ein Fehler beim Nachfragen beendet das Verfolgen still.
   */
  private watchImport(jobId: number, entry: DirectoryEntry): void {
    this.importing.set(true);
    timer(2000, 3000).pipe(
      switchMap(() => this.tournaments.getCrawlJob(jobId).pipe(catchError(() => of(null)))),
      takeWhile(job => job !== null && job.status !== 'Completed' && job.status !== 'Failed', true),
      take(40),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(job => this.onImportProgress(job, entry));
  }

  private onImportProgress(job: CrawlJob | null, entry: DirectoryEntry): void {
    // Zwischenzeitlich ein anderes Turnier geoeffnet? Dann gehoert das Ergebnis nicht hierher.
    if (this.entry()?.id !== entry.id) return;
    if (job === null) { this.importing.set(false); return; }
    if (job.status === 'Completed') {
      this.importing.set(false);
      this.lookupImported(entry);
      this.snackbar.success(this.translate.instant('tournamentDirectory.detail.importDone'));
    } else if (job.status === 'Failed') {
      this.importing.set(false);
      this.snackbar.warn(this.translate.instant('tournamentDirectory.detail.importFailed'));
    }
  }

  back(): void {
    // Zurueck in den Kalender — nicht history.back(): der Einstieg kann eine Benachrichtigung
    // gewesen sein, und dann fuehrte der Verlauf aus der App heraus.
    this.router.navigate(['/tournaments/calendar']);
  }

  /**
   * Wurde das Turnier schon geholt, liegen Teilnehmer, Paarungen und Tabelle bereits hier — dann
   * ist der Sprung dorthin die eigentlich gesuchte Detailansicht. Die Crawler-Route loest sowohl
   * die interne Nummer als auch die chess-results-Nummer auf; dass die zurueckgegebene wirklich
   * unsere ist, wird geprueft (die beiden Nummernkreise koennen sich theoretisch ueberschneiden).
   */
  private lookupImported(entry: DirectoryEntry): void {
    // Gegen die verspaetete Antwort: bei einem reinen Parameterwechsel wird die Komponente NICHT
    // zerstoert, `takeUntilDestroyed` greift also nicht. Ohne den Vergleich mit dem gerade
    // ANGEZEIGTEN Eintrag setzt die Antwort zu Turnier A das Ergebnis, waehrend B auf dem Schirm
    // steht — der Knopf „Ergebnisse" fuehrte dann zum falschen Turnier.
    if (entry.chessResultsId === null) return;
    this.tournaments.getTournament(entry.chessResultsId).pipe(
      catchError(() => of(null)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(t => {
      if (this.entry()?.id !== entry.id) return;
      this.imported.set(t && t.chessResultsId === entry.chessResultsId ? t : null);
    });
  }
}
