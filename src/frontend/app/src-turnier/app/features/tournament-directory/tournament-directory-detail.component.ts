import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '@rh/core/snackbar.service';
import { Tournament } from '@rh/core/models';
import { TournamentListService } from '../../core/tournament-list.service';
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

  readonly entry = signal<DirectoryEntry | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly tilesFailed = signal(false);

  /** Das schon geholte Turnier, falls es eines gibt — dann fuehrt ein Knopf zu Ergebnissen. */
  readonly imported = signal<Tournament | null>(null);

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

  chessResultsUrl(id: string): string {
    return `https://chess-results.com/tnr${id}.aspx?lan=1`;
  }

  onTilesFailed(): void {
    this.tilesFailed.set(true);
  }

  bookmark(): void {
    const entry = this.entry();
    if (!entry) return;
    this.tournaments.subscribe(entry.chessResultsId, entry.name).subscribe({
      next: () => {
        // Neues Objekt statt Mutation: ein Signal meldet nur eine geaenderte REFERENZ.
        this.entry.set({ ...entry, subscribed: true });
        this.snackbar.success(this.translate.instant('tournamentDirectory.bookmarked'));
      },
      error: () => this.snackbar.warn(this.translate.instant('tournamentDirectory.bookmarkError')),
    });
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
    this.tournaments.getTournament(entry.chessResultsId).pipe(
      catchError(() => of(null)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(t => {
      this.imported.set(t && t.chessResultsId === entry.chessResultsId ? t : null);
    });
  }
}
