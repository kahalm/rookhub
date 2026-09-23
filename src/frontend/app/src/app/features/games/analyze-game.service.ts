import { Injectable, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Observable, catchError, map, of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { GameAnalysisService, GuessUploadStatus } from '../analysis/game-analysis.service';
import { GamesService } from './games.service';

/**
 * „Partie analysieren" — EIN Weg für die geteilte Partie (`/g/:token`), die Partienliste (`/games`) und
 * den Nachspiel-Dialog: `POST /api/games/{id}/analyze` bzw. `…/shared/{token}/analyze`. Das PGN liegt
 * am Server; Tiefe, Engine und den Titel setzt er selbst, und er rechnet nur, wenn es noch keine
 * brauchbare Analyse gibt (sonst `reused`).
 *
 * Nach dem Einwurf BLEIBT man auf der Seite: die Bewertungskurve erscheint dort und fragt nach, bis
 * die Partie durch ist (`GameReviewComponent`). Bis 0.511 ging es danach auf die Punktepartie-Seite —
 * dort erscheint eine solche Analyse aber gar nicht mehr (eigener Ursprung `SavedGame`).
 * Die Absage-Gründe formuliert dieselbe i18n-Tabelle wie der Einwurf auf der Punktepartie-Seite
 * (`guess.upload.reason.*`) — der Server nennt nur den Grund, nicht den Satz.
 */
@Injectable({ providedIn: 'root' })
export class AnalyzeGameService {
  private analyses = inject(GameAnalysisService);
  private games = inject(GamesService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  /** Ob eine Engine da ist und wie viele Partien noch frei sind — nur angemeldet abfragen (der Endpunkt
   *  braucht ein Konto). `null`, wenn die Auskunft fehlt: dann bleibt der Knopf benutzbar und der Server
   *  entscheidet beim Einwurf. */
  status(): Observable<GuessUploadStatus | null> {
    return this.analyses.guessUploadStatus().pipe(catchError(() => of(null)));
  }

  /** Lässt die Partie hinter `url` rechnen; meldet Start, „lief schon" oder den Grund der Absage.
   *  Liefert `true`, wenn es eine Analyse gibt (neu oder wiederverwendet) — die Aufrufer halten nur
   *  ihren Busy-Zustand und laden danach die Kurve neu. */
  submit(url: string, status: GuessUploadStatus | null): Observable<boolean> {
    return this.games.analyze(url).pipe(
      map(result => {
        this.snackbar.success(this.translate.instant(result.reused ? 'games.review.reused' : 'games.review.started'));
        return true;
      }),
      catchError(err => {
        const reason = err?.error?.reason;
        this.snackbar.warn(reason
          ? this.translate.instant('guess.upload.reason.' + reason, { max: status?.maxGames })
          : this.translate.instant('guess.upload.failed'));
        return of(false);
      }),
    );
  }
}
