import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { downloadBlob } from '../../shared/download.util';
import { pgnFileName } from '../../shared/pgn-export.util';
import { GameAnalysisService } from '../analysis/game-analysis.service';
import { GamesService } from './games.service';
import { GameRoastData, GameRoastDialogComponent } from './game-roast-dialog.component';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';

/** Was „PGN herunterladen" von einer Partie braucht. */
export interface PgnFileSource {
  pgn: string | null | undefined;
  white?: string | null;
  black?: string | null;
  playedAt?: string | null;
  createdAt?: string | null;
}

/**
 * Die Aktionen an EINER Partie (Vereinheitlichungs-Karte V33, Zerlegung Z26): PGN kopieren/herunterladen,
 * Teilen-Links kopieren, löschen, im Analysebrett öffnen, Formular-Foto, Roast, Lc0-Analyse anstoßen.
 * Eine Fassung je Aktion samt Rückmeldung; die Seite entscheidet nur, ob die Aktion angeboten wird.
 *
 * <p>Je SEITE bereitgestellt (`providers` der Komponente), nicht in der Wurzel: so öffnen die Dialoge über denselben
 * `MatDialog` (samt Injektor der Seite) wie vorher aus der Komponente heraus.</p>
 */
@Injectable()
export class GameActionsService {
  private games = inject(GamesService);
  private gameAnalyses = inject(GameAnalysisService);
  private scoresheets = inject(ScoresheetService);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private confirm = inject(ConfirmService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  /** Das PGN in die Zwischenablage (so, wie es gespeichert ist — samt Kopfdaten und Kommentaren). */
  copyPgn(pgn: string | null | undefined): void {
    if (!pgn) return;
    navigator.clipboard?.writeText(pgn).then(
      () => this.snackbar.copy(this.translate.instant('games.pgnCopied')),
      () => this.snackbar.warn(this.translate.instant('games.pgnCopyFailed')),
    );
  }

  /** Das PGN als Datei „Weiß_Schwarz_Datum.pgn". */
  downloadPgn(g: PgnFileSource | null | undefined): void {
    if (!g?.pgn) return;
    const players = [g.white, g.black].filter(n => !!n).join(' ');
    downloadBlob(new Blob([g.pgn.endsWith('\n') ? g.pgn : g.pgn + '\n'], { type: 'application/x-chess-pgn' }),
      pgnFileName(players || 'game', (g.playedAt ?? g.createdAt ?? '').slice(0, 10)));
  }

  /** Teilen-Link einer Partie in die Zwischenablage; scheitert das, steht der Link in der Meldung. */
  copyShareLink(shareToken: string | null | undefined): void {
    if (!shareToken) return;
    const url = this.games.shareUrl(shareToken);
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('games.shareCopied')),
      () => this.snackbar.warn(url),
    );
  }

  /** Link „Fehler nachspielen" einer Seite (0.740.0): der Teilen-Link mit `?train=`; ohne Teilen-Link `null`. */
  trainingShareUrl(shareToken: string | null | undefined, side: 'white' | 'black'): string | null {
    return shareToken ? `${this.games.shareUrl(shareToken)}?train=${side}` : null;
  }

  copyTrainingLink(url: string | null): void {
    if (!url) return;
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('games.shareTrainingCopied')),
      () => this.snackbar.warn(url),
    );
  }

  /**
   * Eigene Partie löschen — mit Rückfrage. Liefert `true`, sobald sie gelöscht ist (die Seite entscheidet, wohin
   * es danach geht); ein Fehlschlag wird gemeldet und liefert nichts.
   */
  deleteGame(id: number): Observable<true> {
    return new Observable<true>(sub => {
      const ask = this.confirm.ask('games.deleteConfirm').subscribe(ok => {
        if (!ok) { sub.complete(); return; }
        this.games.delete(id).subscribe({
          next: () => { sub.next(true); sub.complete(); },
          error: () => { this.snackbar.warn(this.translate.instant('games.deleteError')); sub.complete(); },
        });
      });
      return () => ask.unsubscribe();
    });
  }

  /** Im Analysebrett öffnen — mit dem GANZEN PGN (Kopfdaten, Kommentare); „Zurück" führt nach `from`. */
  openInAnalysis(pgn: string | null | undefined, from: string): void {
    if (!pgn) return;
    void this.router.navigate(['/analysis'], { state: { pgn }, queryParams: { from } });
  }

  /** Das Formular-Foto einer eingelesenen Partie anzeigen (Dialog) oder herunterladen. */
  photo(gameId: number, download: boolean): void {
    if (!download) { ScoresheetPhotoDialogComponent.open(this.dialog, gameId); return; }
    this.scoresheets.photo(gameId).subscribe({
      next: blob => openPhotoBlob(blob, photoFileName(gameId, blob)),
      error: () => this.snackbar.warn(this.translate.instant('games.photo.loadError')),
    });
  }

  /** „Roast my game" (0.535.0) — nur die eigene Partie. */
  roast(gameId: number, shareToken: string | null): void {
    this.dialog.open(GameRoastDialogComponent, {
      data: { gameId, shareUrl: shareToken ? this.games.shareUrl(shareToken) : null, shareToken: shareToken ?? null } satisfies GameRoastData,
      maxWidth: '96vw',
    });
  }

  /** „Mit Lc0 analysieren": den Auftrag anlegen; `started` läuft nur nach Erfolg. */
  startLc0(pgn: string | null | undefined, started: () => void): void {
    if (!pgn) return;
    this.gameAnalyses.startLc0(pgn).subscribe({
      next: () => {
        this.snackbar.success(this.translate.instant('games.lc0.started'));
        started();
      },
      error: () => this.snackbar.warn(this.translate.instant('games.lc0.failed')),
    });
  }

  /** Wartende/hängende Lc0-Analyse abbrechen (0.833.0) — danach bietet das ⋮ „Mit Lc0 analysieren" wieder an. */
  cancelLc0(id: number, done: () => void): void {
    this.gameAnalyses.delete(id).subscribe({
      next: () => { this.snackbar.success(this.translate.instant('games.lc0.cancelled')); done(); },
      error: () => this.snackbar.warn(this.translate.instant('games.lc0.cancelFailed')),
    });
  }

  /** Hängende Lc0-Analyse neu einreihen (0.833.0). */
  restartLc0(id: number, done: () => void): void {
    this.gameAnalyses.restart(id).subscribe({
      next: () => { this.snackbar.success(this.translate.instant('games.lc0.restarted')); done(); },
      error: () => this.snackbar.warn(this.translate.instant('games.lc0.restartFailed')),
    });
  }
}
