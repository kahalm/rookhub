import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { Observable, catchError, map, of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { GameAnalysisService, GuessUploadStatus } from '../analysis/game-analysis.service';

/**
 * „Partie analysieren" — EIN Weg für die geteilte Partie (`/g/:token`) und die Partienliste (`/games`):
 * derselbe Einwurf wie „Partie einwerfen" auf der Punktepartie-Seite (`POST /api/game-analyses/guess`,
 * Tiefe und Engine setzt der Server), danach der Sprung dorthin, wo die Partie mit Fortschritt erscheint.
 * Die Absage-Gründe formuliert dieselbe i18n-Tabelle (`guess.upload.reason.*`) — der Server nennt nur den
 * Grund, nicht den Satz. Zwei Aufrufer mit je eigener Fassung davon liefen beim ersten Fix auseinander.
 */
@Injectable({ providedIn: 'root' })
export class AnalyzeGameService {
  private analyses = inject(GameAnalysisService);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  /** Ob eine Engine da ist und wie viele Partien noch frei sind — nur angemeldet abfragen (der Endpunkt
   *  braucht ein Konto). `null`, wenn die Auskunft fehlt: dann bleibt der Knopf benutzbar und der Server
   *  entscheidet beim Einwurf. */
  status(): Observable<GuessUploadStatus | null> {
    return this.analyses.guessUploadStatus().pipe(catchError(() => of(null)));
  }

  /** Wirft die Partie ein; meldet Erfolg (und führt auf die Punktepartie-Seite) bzw. den Grund der Absage.
   *  Liefert `true`, wenn die Rechnung angestoßen wurde — die Aufrufer halten nur ihren Busy-Zustand. */
  submit(pgn: string, title: string | undefined, status: GuessUploadStatus | null): Observable<boolean> {
    return this.analyses.createForGuess(pgn, title).pipe(
      map(() => {
        this.snackbar.success(this.translate.instant('guess.upload.started'));
        this.router.navigate(['/guess']);
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

  /** Titel der Analyse aus den Spielernamen („Weiß – Schwarz"); ohne beide Namen keiner. */
  static titleOf(white: string | null | undefined, black: string | null | undefined): string | undefined {
    return white && black ? `${white} – ${black}` : undefined;
  }
}
