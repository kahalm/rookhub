import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap, takeWhile, timer } from 'rxjs';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentListService } from './tournament-list.service';

/**
 * Ein Turnier oeffnen, das vielleicht noch gar nicht geholt ist.
 *
 * <p><b>Warum das mehr ist als ein Link.</b> Der Verlauf und die Merkliste kennen ein Turnier nur
 * ueber seine chess-results-Nummer. Ist es schon geholt, fuehrt der Weg direkt zu Teilnehmern,
 * Paarungen und Ergebnissen; ist es das nicht, muss erst ein Holen-Auftrag laufen — und der
 * braucht bis zu zwei Minuten. Ins Verzeichnis zu verlinken hilft nicht: der naechtliche Sweep
 * liest nur ein Fenster von 30 Tagen zurueck, ein 2024 gespieltes Turnier steht dort NIE.</p>
 *
 * <p><b>Warum als Dienst und nicht in der Komponente.</b> Der Ablauf (nachsehen, einreihen,
 * nachfragen, Deckel, Meldung) stand im Turnierverlauf; die Merkliste braucht ihn genauso. Ein
 * zweites Mal getippt waere es derselbe Poll-Mechanismus an zwei Stellen — und die zweite wird
 * beim naechsten Fix vergessen.</p>
 */
@Injectable({ providedIn: 'root' })
export class OpenTournamentService {
  private readonly tournaments = inject(TournamentListService);
  private readonly router = inject(Router);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  /**
   * Der Poll braucht ein ENDE, das nicht am Ergebnis haengt. Ohne dieses Abo-Ende lief der
   * 4-Sekunden-Takt weiter, auch wenn niemand mehr hinsieht — im Test bis zum Abbruch des
   * Browsers, in der Anwendung bis zum Neuladen der Seite. Der Dienst haengt am Wurzel-Injektor,
   * endet also mit der Anwendung.
   */
  private readonly destroyRef = inject(DestroyRef);

  /** Wie lange auf einen Holen-Auftrag gewartet wird: 4 s x 30 = rund zwei Minuten. */
  private static readonly PollMs = 4000;
  private static readonly MaxPolls = 30;

  /**
   * Welches Turnier gerade geoeffnet wird (chess-results-Nummer). Sperrt weitere Klicks und
   * traegt die Wartezeit-Anzeige der aufrufenden Seite.
   */
  readonly opening = signal<string | null>(null);

  open(chessResultsId: string): void {
    if (this.opening()) return;
    this.opening.set(chessResultsId);

    this.tournaments.getTournament(chessResultsId).pipe(
      catchError(() => of(null)),
    ).subscribe(tournament => {
      if (tournament) {
        this.opening.set(null);
        void this.router.navigate(['/tournaments', tournament.id]);
        return;
      }
      this.fetchThenOpen(chessResultsId);
    });
  }

  private fetchThenOpen(chessResultsId: string): void {
    this.snackbar.info(this.translate.instant('turnier.history.fetching'));

    this.tournaments.startCrawl(chessResultsId).pipe(
      catchError(() => of(null)),
    ).subscribe(job => {
      if (!job) {
        this.opening.set(null);
        this.snackbar.warn(this.translate.instant('turnier.history.fetchFailed'));
        return;
      }
      this.pollImport(chessResultsId);
    });
  }

  private pollImport(chessResultsId: string): void {
    timer(0, OpenTournamentService.PollMs).pipe(
      switchMap(() => this.tournaments.getTournament(chessResultsId).pipe(catchError(() => of(null)))),
      // Solange nichts da ist, weiterfragen — hoechstens `MaxPolls` mal.
      takeWhile((t, i) => t === null && i < OpenTournamentService.MaxPolls, true),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(tournament => {
      if (tournament) {
        this.opening.set(null);
        void this.router.navigate(['/tournaments', tournament.id]);
      } else if (this.opening() === chessResultsId) {
        // Deckel erreicht: der Auftrag laeuft serverseitig weiter, hier wird nicht laenger gewartet.
        this.opening.set(null);
        this.snackbar.info(this.translate.instant('turnier.history.fetchSlow'));
      }
    });
  }
}
