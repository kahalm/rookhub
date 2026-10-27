import { Injectable, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { Observable, catchError, map, of, switchMap, tap } from 'rxjs';
import { ViewStateService } from '../../core/view-state.service';
import { AnalyzeChoice, AnalyzeChoiceDialogComponent } from './analyze-choice-dialog.component';
import { LineStudyService } from '../analysis/line-study.service';
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
/** Vorgabe beim Analysieren (0.855.0): jedes Mal fragen, immer mit oder immer ohne Engine. */
export type AnalyzeMode = 'ask' | 'engine' | 'blind';

/** Schlüssel im Anzeige-Zustand je Nutzer (`/api/view-state`) — die Vorgabe gilt auf allen Geräten. */
export const ANALYZE_CHOICE_KEY = 'analyze.choice';

@Injectable({ providedIn: 'root' })
export class AnalyzeGameService {
  private analyses = inject(GameAnalysisService);
  private games = inject(GamesService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  private viewState = inject(ViewStateService);
  /** Für das Analysebrett „ohne Engine" (0.855.0) — es baut seine Sitzung über diesen Dienst, ohne eigenen Konstruktor-Parameter. */
  readonly lineStudies = inject(LineStudyService);

  /** Die Vorgabe „mit/ohne Engine"; `ask` = das Fenster fragt. Einmal je Sitzung vom Server geholt. */
  readonly mode = signal<AnalyzeMode>('ask');
  private modeLoaded = false;

  /** Vorgabe holen (einmal; ohne Antwort bleibt es bei „fragen"). */
  loadMode(): Observable<AnalyzeMode> {
    if (this.modeLoaded) return of(this.mode());
    return this.viewState.get<{ mode?: string }>(ANALYZE_CHOICE_KEY).pipe(
      map(s => {
        const m = s?.mode;
        if (s !== undefined) this.modeLoaded = true;
        if (m === 'engine' || m === 'blind' || m === 'ask') this.mode.set(m);
        return this.mode();
      }),
    );
  }

  /** Vorgabe setzen (Profil oder „Entscheidung merken"). */
  setMode(mode: AnalyzeMode): void {
    this.mode.set(mode);
    this.modeLoaded = true;
    this.viewState.save(ANALYZE_CHOICE_KEY, { mode }).subscribe();
  }

  /** Mit oder ohne Engine? Steht eine Vorgabe, gilt sie; sonst fragt das Fenster. `null` = abgebrochen. */
  choose(): Observable<'engine' | 'blind' | null> {
    return this.loadMode().pipe(switchMap(mode => {
      if (mode !== 'ask') return of(mode);
      return this.dialog.open<AnalyzeChoiceDialogComponent, unknown, AnalyzeChoice>(AnalyzeChoiceDialogComponent,
        { width: '440px', maxWidth: '96vw' }).afterClosed().pipe(
        tap(c => { if (c?.remember) this.setMode(c.mode); }),
        map(c => c?.mode ?? null),
      );
    }));
  }

  /**
   * „Partie analysieren" mit der Wahl mit/ohne Engine (0.855.0). Mit Engine wie {@link submit}. Ohne Engine: die Partie
   * rechnet trotzdem im Hintergrund (sie bleibt dort verdeckt, wo man ohne Engine arbeitet), und das Analysebrett öffnet im
   * Modus „ohne Engine" mit dem PGN. Liefert `true` nur, wenn mit Engine eine Analyse läuft — die Aufrufer laden dann die Kurve.
   */
  analyze(url: string | null, status: GuessUploadStatus | null,
          pgn: string | null | undefined | (() => Observable<string>)): Observable<boolean> {
    return this.choose().pipe(switchMap(choice => {
      if (choice === 'engine') return url ? this.submit(url, status) : of(false);
      if (choice !== 'blind' || !pgn) return of(false);
      // Die Partienliste kennt das PGN nicht — sie reicht einen Abruf herein.
      const pgn$ = typeof pgn === 'function' ? pgn() : of(pgn);
      return pgn$.pipe(
        tap(text => this.startWithoutEngine(url, text)),
        map(() => false),
        catchError(() => { this.snackbar.warn(this.translate.instant('guess.upload.failed')); return of(false); }),
      );
    }));
  }

  /** „Ohne Engine": die Partie still im Hintergrund rechnen lassen (falls noch nicht geschehen) und das Analysebrett ohne
   *  Bewertungen öffnen — auch direkt aus dem ⋮ einer Partie, die schon analysiert ist. */
  startWithoutEngine(url: string | null, pgn: string): void {
    if (url) this.games.analyze(url, this.lang()).pipe(catchError(() => of(null))).subscribe();
    void this.router.navigate(['/analysis'], { state: { pgn, blind: true }, queryParams: { blind: 1 } });
  }

  private lang(): string {
    return this.translate.currentLang() || this.translate.getFallbackLang() || 'en';
  }

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
    const lang = this.translate.currentLang() || this.translate.getFallbackLang() || 'en';
    return this.games.analyze(url, lang).pipe(
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
