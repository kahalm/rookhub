import { leagueHubUrl } from '../../core/partner-site';
import {
  Component, DoCheck, OnInit, HostListener, inject, ChangeDetectionStrategy, computed, effect, signal, viewChild,
  untracked, DestroyRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { BoardArrow, ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { PgnViewerService } from '../../shared/pgn-viewer/pgn-viewer.service';
import { PreferencesService } from '../../core/preferences.service';
import { HandoffService } from '../../core/handoff.service';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { downloadBlob } from '../../shared/download.util';
import { pgnFileName } from '../../shared/pgn-export.util';
import { GameAnalysisService, GuessUploadStatus } from '../analysis/game-analysis.service';
import { AnalyzeGameService } from './analyze-game.service';
import { GamesService, SharedGame } from './games.service';
import { GameReviewComponent } from './game-review.component';
import { GameEvalsStatus } from './game-review.util';
import { DeepStored } from './deep-analysis.util';
import { MistakesBySide, NO_MISTAKES, mistakesOf, trainingSide } from './mistakes.util';
import { MistakesTrainerComponent } from './mistakes-trainer.component';
import { MistakesSession } from './mistakes-session';
import { MistakeJudgeService } from './mistake-judge.service';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { ExternalEngineInfo, ExternalEngineService, engineTagKey, isEngineOffline } from '../analysis/external-engine.service';
import { ANALYSIS_DEPTH_KEY, ANALYSIS_PROVIDER_KEY } from '../analysis/analysis-settings';
import { LiveEngineSession } from './live-engine-session';
import { LiveEnginePanelComponent } from './live-engine-panel.component';
import { PositionMenuComponent } from '../analysis/position-menu.component';
import { BoardBadge } from './move-badge.util';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';
import { GameRoastData, GameRoastDialogComponent } from './game-roast-dialog.component';
import { SimilarGamesComponent } from './similar-games.component';
import { isBoardHotkey } from '../../shared/keyboard.util';

/** „Kurz erzählt" entsteht nach der Analyse in ein paar Sekunden — so oft und so lange fragt die eigene Seite nach. */
const RECAP_TRIES = 8;
const RECAP_RETRY_MS = 15_000;
/** Ab so viel Bewegung (px) bzw. Dauer (ms) ist eine Berührung des Bretts kein Tipp mehr, sondern Ziehen/Halten. */
const TAP_SLOP_PX = 10;
const TAP_MAX_MS = 500;

/**
 * Nachspiel-Seite einer Partie — in ZWEI Rollen, dieselbe Ansicht:
 * - <c>/g/:token</c>: die geteilte Partie, öffentlich, kein Login nötig (Route ohne <c>data.mode</c>);
 * - <c>/games/:id</c> (<c>data.mode = 'own'</c>): die eigene gespeicherte Partie aus <c>/games</c>. Bis 0.512.0
 *   öffnete die Liste dafür den PGN-Viewer-DIALOG; der Nutzer wollte eine Seite (2026-09-23) — und die gab es
 *   für den Teilen-Link längst, samt Kurve und Analysieren-Knopf. Eigenes dazu: Zurück-Pfeil, Teilen-Link.
 * Reused den PgnViewerService + chess-board/move-list aus dem PGN-Viewer.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-shared-game',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatIconModule, MatCardModule, MatProgressSpinnerModule, MatTooltipModule,
    TranslatePipe, ChessBoardComponent, MoveListComponent, PositionRepertoiresComponent, GameReviewComponent,
    MistakesTrainerComponent, LiveEnginePanelComponent, PositionMenuComponent, MatMenuModule, MatDialogModule,
    SimilarGamesComponent,
  ],
  providers: [PgnViewerService],
  template: `
    <div class="shared-page">
      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (notFound) {
        <mat-card class="empty">
          <mat-icon>link_off</mat-icon>
          <p>{{ notFoundKey | translate }}</p>
        </mat-card>
      } @else if (game) {
        <mat-card class="viewer">
          <div class="header">
            @if (own) {
              <a mat-icon-button routerLink="/games" class="back" [matTooltip]="'common.back' | translate" [attr.aria-label]="'common.back' | translate">
                <mat-icon>arrow_back</mat-icon>
              </a>
            }
            <div class="header-main">
              <span class="players">
                <strong>{{ game.white || '?' }}</strong>@if (game.whiteElo) { <span class="elo">({{ game.whiteElo }})</span> }
                –
                <strong>{{ game.black || '?' }}</strong>@if (game.blackElo) { <span class="elo">({{ game.blackElo }})</span> }
              </span>
              <span class="meta">
                @if (game.result && game.result !== '*') { <span class="result">{{ game.result }}</span> }
                <span>{{ game.source === 'scoresheet' ? ('games.source.scoresheet' | translate) : game.source }}</span>
                @if (club) {
                  @if (game.year) { <span class="date">{{ game.year }}</span> }
                } @else {
                  <span class="date">{{ (game.playedAt || game.createdAt) | date:'mediumDate' }}</span>
                }
              </span>
            </div>
            <!-- Am PC in die Kopfzeile: als eigene Zeile unter Brett und Zugliste war der Knopf so breit wie
                 die Karte und stand mitten im Leeren. Auf dem Handy fällt die Kopfzeile in eine Spalte. -->
            <div class="header-actions">
              <!-- Die Partie wird im Hintergrund gerechnet (Haus-Engine, feste Tiefe), die Bewertungskurve erscheint
                   darauf unter dem Brett. Ohne Anmeldung führt der Klick zur Anmeldung und wieder hierher zurück —
                   der Knopf bleibt sichtbar, damit man weiß, dass es den Weg gibt. Ist die Kurve fertig, entfällt
                   er; solange sie rechnet, ist er gesperrt und sagt es. -->
              @if (!club && reviewStatus() !== 'done') {
                <button mat-flat-button color="primary" class="analyze" (click)="analyze()"
                        [disabled]="analyzing() || analysisRunning() || uploadStatus()?.engineAvailable === false"
                        [matTooltip]="analyzeTooltip() | translate">
                  <mat-icon>{{ analyzing() ? 'hourglass_top' : 'insights' }}</mat-icon> {{ 'games.analyze' | translate }}
                </button>
              }
              @if (game.sourceUrl) {
                <a mat-stroked-button [href]="game.sourceUrl" target="_blank" rel="noopener" class="original">
                  <mat-icon>open_in_new</mat-icon> {{ 'games.openOriginal' | translate }}
                </a>
              }
              <!-- „Eigene Fehler nachspielen" (0.516.0) — erscheint erst, wenn die Analyse Aufgaben hergibt.
                   Seit 0.518.1 auf dem Brett DIESER Seite; solange das Training läuft, steht die Leiste unter
                   dem Brett und der Knopf entfällt. -->
              @if (mistakeTotal() > 0 && !training()) {
                <button mat-stroked-button class="mistakes" (click)="trainMistakes()">
                  <mat-icon>replay</mat-icon> {{ 'games.mistakes.button' | translate: { count: mistakeTotal() } }}
                </button>
              }
              @if (own && shareToken) {
                <button mat-stroked-button class="share" (click)="share()">
                  <mat-icon>share</mat-icon> {{ 'games.share' | translate }}
                </button>
              }
              <!-- ⋮: PGN kopieren/herunterladen für JEDEN Betrachter (das PGN liegt ohnehin im Browser, 0.553.0);
                   für die eigene Partie dazu korrigieren, Roast und — bei einer eingelesenen — das Formular-Foto. -->
              @if (game) {
                <button mat-icon-button class="game-menu" [matMenuTriggerFor]="gameMenu" (menuOpened)="loadLiveEngines()"
                        [matTooltip]="'common.moreActions' | translate" [attr.aria-label]="'common.moreActions' | translate">
                  <mat-icon>more_vert</mat-icon>
                </button>
                <mat-menu #gameMenu="matMenu">
                  <button mat-menu-item class="to-analysis" (click)="openInAnalysis()">
                    <mat-icon>biotech</mat-icon><span>{{ 'games.openInAnalysis' | translate }}</span>
                  </button>
                  @if (loggedIn) {
                    <!-- Live-Engine waehlen (0.681.0): dieselbe Wahl wie am Analysebrett, hier ohne Umweg dorthin. -->
                    <button mat-menu-item class="live-engine-menu" [matMenuTriggerFor]="engineMenu">
                      <mat-icon>memory</mat-icon><span>{{ 'games.live.engineMenu' | translate }}</span>
                    </button>
                  }
                  @if (canLc0()) {
                    <!-- Ganze Partie auf Lc0 (0.692.0): nur Vereinsmitglieder, nur solange es keine Lc0-Analyse gibt. -->
                    <button mat-menu-item class="lc0-game" (click)="analyzeLc0()">
                      <mat-icon>psychology</mat-icon><span>{{ 'games.lc0.menu' | translate }}</span>
                    </button>
                  }
                  <button mat-menu-item (click)="copyPgn()">
                    <mat-icon>content_copy</mat-icon><span>{{ 'games.pgnCopy' | translate }}</span>
                  </button>
                  <button mat-menu-item (click)="downloadPgn()">
                    <mat-icon>file_download</mat-icon><span>{{ 'common.downloadPgn' | translate }}</span>
                  </button>
                  @if (club && game.clubCanCorrect && leagueHub) {
                    <!-- Vereinspartie korrigieren (0.660.0): in LeagueHub, mit dem aufbewahrten Formular -->
                    <a mat-menu-item [href]="leagueHub + '/verein/partie/' + game.clubId + '/korrigieren'">
                      <mat-icon>edit_note</mat-icon><span>{{ 'games.edit.menu' | translate }}</span>
                    </a>
                  }
                  @if (own && gameId) {
                    <a mat-menu-item [routerLink]="['/games', gameId, 'edit']">
                      <mat-icon>edit_note</mat-icon><span>{{ 'games.edit.menu' | translate }}</span>
                    </a>
                    <button mat-menu-item (click)="roast()">
                      <mat-icon>local_fire_department</mat-icon><span>{{ 'games.roast.menu' | translate }}</span>
                    </button>
                    @if (clubImport) {
                      <button mat-menu-item (click)="toClub()">
                        <mat-icon>groups</mat-icon><span>{{ 'games.clubImport' | translate }}</span>
                      </button>
                    }
                    @if (scanId) {
                      <button mat-menu-item (click)="photo(false)">
                        <mat-icon>image</mat-icon><span>{{ 'games.photo.show' | translate }}</span>
                      </button>
                      <button mat-menu-item (click)="photo(true)">
                        <mat-icon>download</mat-icon><span>{{ 'games.photo.download' | translate }}</span>
                      </button>
                    }
                  }
                </mat-menu>
                <mat-menu #engineMenu="matMenu">
                  <button mat-menu-item class="engine-choice" (click)="chooseLiveEngine(null)">
                    <mat-icon>{{ currentEngineId() === null ? 'radio_button_checked' : 'radio_button_unchecked' }}</mat-icon>
                    <span>{{ 'games.live.browser' | translate }}</span>
                  </button>
                  @for (e of liveEngines(); track e.id) {
                    <button mat-menu-item class="engine-choice" [disabled]="engineOffline(e)" (click)="chooseLiveEngine(e)">
                      <mat-icon>{{ currentEngineId() === e.id ? 'radio_button_checked' : 'radio_button_unchecked' }}</mat-icon>
                      <span>{{ e.name }}@if (engineTag(e); as t) { · {{ t | translate }} }</span>
                    </button>
                  }
                </mat-menu>
              }
            </div>
          </div>
          <!-- „Kurz erzählt" (0.541.0): dieselbe Zeile wie in der Link-Vorschau, vom Sprachmodell aus der Analyse. -->
          @if (recap(); as text) {
            <p class="recap">
              <mat-icon [matTooltip]="'games.recap.hint' | translate" [attr.aria-label]="'games.recap.hint' | translate">auto_stories</mat-icon>
              <span>{{ text }}</span>
            </p>
          }
          <div class="body">
            <div class="board-section">
              <div class="board-wrap">
                @if (training(); as t) {
                  @if (trainingAnalysis(); as a) {
                    <!-- „Analysieren" im Training: frei weiterrechnen ab der Aufgabe, mit der Live-Engine (blauer Pfeil). -->
                    <app-chess-board [fen]="a.session.fen(a.base)" [lastMove]="a.session.lastMove() ?? a.lastMove" [flipped]="t.flipped()"
                                     [playable]="true" (userMove)="a.session.play($event, a.base)"
                                     [arrows]="a.session.arrows()"
                                     [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                  } @else {
                    <!-- Dasselbe Brett wie beim Nachspielen, nur mit der Stellung der Aufgabe und spielbar. -->
                    <app-chess-board [fen]="t.boardFen()" [lastMove]="t.lastMove()" [flipped]="t.flipped()"
                                     [playable]="t.playable()" (userMove)="onTrainingMove($event)"
                                     [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                  }
                } @else if (live(); as l) {
                  <!-- Live-Engine: das Brett ist spielbar (eigene Nebenvariante), die Tippzonen fallen weg — sie lägen
                       über dem Brett und schluckten jeden Zug. Der blaue Pfeil ist der beste Zug der Live-Engine. -->
                  <app-chess-board class="board-tapnav" [fen]="l.fen(service.currentFen)" [lastMove]="l.lastMove() ?? service.lastMove"
                                   [flipped]="flipped" [playable]="true" [clickToMove]="!coarsePointer"
                                   (userMove)="l.play($event, service.currentFen)"
                                   (pointerdown)="onBoardPointerDown($event)" (pointerup)="onBoardTap($event)"
                                   (pointercancel)="tapStart = null"
                                   [arrows]="l.arrows()"
                                   [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                } @else {
                  <!-- Tipp oder Zug (0.654.0, Wunsch 2026-10-04: „erkennst du den Unterschied zwischen Tippen und Ziehen?"):
                       vorher lagen links/rechts unsichtbare Tippzonen ÜBER dem Brett und schluckten jede Berührung. Jetzt
                       ist das Brett spielbar; ein kurzer Tipp ohne Bewegung auf das linke/rechte Fünftel-Paar blättert
                       (onBoardTap), ein gezogener Zug startet die eigene Variante mit der Live-Engine (onBoardMove). Am
                       Handy nur Ziehen — sonst wäre ein Tipp auf eine Figur „auswählen" statt „blättern". -->
                  <app-chess-board class="board-tapnav" [fen]="service.currentFen" [lastMove]="service.lastMove" [flipped]="flipped"
                                   [arrows]="bestArrows()" [badge]="moveBadge()" [playable]="true" [clickToMove]="!coarsePointer"
                                   (userMove)="onBoardMove($event)"
                                   (pointerdown)="onBoardPointerDown($event)" (pointerup)="onBoardTap($event)"
                                   (pointercancel)="tapStart = null"
                                   [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                }
              </div>
              @if (training(); as t) {
                <app-mistakes-trainer class="trainer-slot" [session]="t" (closed)="endTraining()"
                                      [analyzing]="!!trainingAnalysis()" (analyze)="toggleTrainingAnalysis()" />
                @if (trainingAnalysis(); as a) {
                  <app-live-engine-panel class="live-slot" [session]="a.session" [gameFen]="a.base" (closed)="stopTrainingAnalysis()" />
                }
              } @else {
              <div class="nav">
                <!-- Am Handy (siehe @media): Zurück/Vor breit in der Mitte, Anfang/Ende mit Abstand an den Rand —
                     dicht nebeneinander traf man statt „einen Zug" oft „ganz an den Anfang/das Ende" (2026-09-24). -->
                <!-- In einer eigenen Variante (Live-Engine) laufen alle vier durch SIE (0.667.0): ◀ ▶ einen Zug, ⏮ an ihren
                     Anfang (die Züge bleiben, ▶ holt sie wieder), ⏭ an ihr Ende. Erst am Anfang der Variante geht es in der
                     Partie weiter. Zurück zur Partie: Knopf in der Live-Leiste. -->
                <button mat-icon-button class="nav-start" (click)="navStart()" [disabled]="!canNavBack()"
                        [attr.aria-label]="'pgnViewer.nav.first' | translate"><mat-icon>skip_previous</mat-icon></button>
                <button mat-icon-button class="nav-prev" (click)="navPrev()" [disabled]="!canNavBack()"
                        [attr.aria-label]="'pgnViewer.nav.previous' | translate"><mat-icon>navigate_before</mat-icon></button>
                <button mat-icon-button class="nav-next" (click)="navNext()" [disabled]="!canNavForward()"
                        [attr.aria-label]="'pgnViewer.nav.next' | translate"><mat-icon>navigate_next</mat-icon></button>
                <button mat-icon-button class="nav-end" (click)="navEnd()" [disabled]="!canNavForward()"
                        [attr.aria-label]="'pgnViewer.nav.last' | translate"><mat-icon>skip_next</mat-icon></button>
                <button mat-icon-button class="nav-flip" (click)="flipped = !flipped"><mat-icon>swap_vert</mat-icon></button>
                <button mat-icon-button class="live-toggle" [class.on]="!!live()" (click)="toggleLive()"
                        [attr.aria-pressed]="!!live()"
                        [matTooltip]="'games.live.toggle' | translate" [attr.aria-label]="'games.live.toggle' | translate">
                  <mat-icon>memory</mat-icon>
                </button>
                <!-- ⋮ für die Stellung auf dem Brett (0.527.0) — auch die einer eigenen Nebenvariante der Live-Engine. -->
                <app-position-menu class="nav-menu" [fen]="positionFen()" [orientation]="flipped ? 'black' : 'white'" [deep]="deepStored()" />
              </div>
              @if (live(); as l) {
                <app-live-engine-panel class="live-slot" [session]="l" [gameFen]="service.currentFen" [steps]="false" (closed)="stopLive()" />
              }
              }
            </div>
            <div class="moves-section">
              @if (service.currentGame; as g) {
                <app-move-list [moves]="g.moves" [currentMoveIndex]="service.currentMoveIndex" [comments]="g.comments" (moveClicked)="service.goToMove($event)" />
              }
            </div>
            <!-- Auswertung, Repertoire, ähnliche Partien: am breiten Bildschirm eine eigene dritte Spalte (0.714.0) — vorher
                 standen sie unter dem Brett, die Brettspalte wurde lang und das Brett klein, rechts blieb ein Drittel leer.
                 Schmaler: unter Brett und Zugliste; am Handy direkt unter dem Brett (vor der Zugliste, wie bisher). -->
            <div class="side-section">
              @if (service.currentGame; as g) {
                <app-game-review class="review-slot" [evalsUrl]="evalsUrl" [withExplanations]="!club" [fens]="g.fens" [moves]="g.moves"
                                 [currentIndex]="service.currentMoveIndex" [engineHidden]="!!training()" [liveEngine]="!!live()" [offGame]="!!live()?.variation()?.length" [withAlternatives]="loggedIn"
                                 (arrowsChange)="bestArrows.set($event)" (badgeChange)="moveBadge.set($event)"
                                 (moveClicked)="service.goToMove($event)"
                                 (statusChange)="reviewStatus.set($event)"
                                 (mistakesChange)="mistakes.set($event)"
                                 (storedChange)="reviewStored.set($event)" />
              }
              <app-position-repertoires class="pr-slot" appearance="row" [fen]="service.currentFen" />
              <!-- „Ähnliche Meisterpartien" (0.544.0): eingeklappt, lädt erst beim Aufklappen. -->
              <app-similar-games class="similar-slot" [url]="similarUrl" />
            </div>
          </div>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    /* Breiter als die übrigen Seiten (0.714.0): Brett, Zugliste und Auswertung stehen nebeneinander. */
    .shared-page { max-width: min(1760px, 96vw); margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    /* Die Karte umschließt ihren Inhalt und steht mittig. Das Brett wächst mit dem Fenster: so hoch, dass
       Kopfzeile und Steuerleiste noch Platz haben, und so breit, dass Zugliste (und am PC die Auswertung) daneben
       passen — nie unter 360 px (gemeldet 2026-09-23: 400 px auf 2250 px Breite) und nie über 760 px. */
    .viewer {
      --board-size: clamp(360px, min(calc(100vh - 260px - var(--recap-room, 0px)), calc(100vw - 440px)), 760px);
      --moves-width: 280px;
      --side-width: clamp(420px, 30vw, 520px);
      width: fit-content; max-width: 100%; margin: 0 auto; padding: 18px 24px 22px; box-sizing: border-box;
      border-radius: 14px;
    }
    /* „Kurz erzählt“ steht über dem Brett — sein Platz geht von der Höhe ab, sonst ragt das Brett samt Leiste unten raus. */
    .viewer:has(> .recap) { --recap-room: 80px; }
    .header {
      display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center;
      gap: 8px 16px; margin-bottom: 14px;
    }
    .header:not(:has(.back)) { grid-template-columns: minmax(0, 1fr) auto; }
    .header-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .players { font-size: 1.2rem; line-height: 1.3; }
    .players strong { font-weight: 600; }
    .players .elo { font-weight: 400; font-size: 0.8em; color: color-mix(in srgb, currentColor 60%, transparent); }
    .meta { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; font-size: 0.82rem; }
    .meta > span {
      padding: 1px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 7%, transparent);
      color: color-mix(in srgb, currentColor 75%, transparent);
    }
    .meta > .result { background: color-mix(in srgb, var(--rh-accent) 16%, transparent); color: var(--rh-accent); font-weight: 600; }
    .header-actions { display: flex; align-items: center; justify-content: flex-end; gap: 8px; flex-wrap: wrap; }
    /* Breite 0 + Mindestbreite 100 %: der Satz trägt nichts zur Breite der Karte bei — sie umschließt Brett und Zugliste,
       ein langer Absatz dehnte sie sonst auf die ganze Seite. */
    .recap {
      width: 0; min-width: 100%; box-sizing: border-box; margin: 0 0 14px; padding: 10px 14px;
      display: flex; gap: 10px; align-items: flex-start; border-radius: 10px;
      background: color-mix(in srgb, var(--rh-accent) 7%, transparent);
      font-size: 0.92rem; line-height: 1.5; color: color-mix(in srgb, currentColor 85%, transparent);
    }
    .recap mat-icon { flex: 0 0 auto; font-size: 20px; width: 20px; height: 20px; margin-top: 1px; color: var(--rh-accent); }
    .original, .analyze { white-space: nowrap; }
    .body { display: flex; flex-wrap: wrap; gap: 20px; align-items: flex-start; }
    .side-section { display: flex; flex-direction: column; gap: 12px; flex: 1 1 100%; min-width: 0; }
    /* Die drei Blöcke rechts als ruhige Karten mit gleichem Rahmen; ein leerer Rückblick (noch keine Analyse) fällt weg. */
    .side-section > .review-slot,
    .side-section > .pr-slot,
    .side-section > .similar-slot {
      box-sizing: border-box; border-radius: 10px;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent);
    }
    .side-section > .review-slot { padding: 12px 14px; }
    .side-section > .review-slot:empty, .side-section > .pr-slot:empty { display: none; }
    /* Breit genug für drei Spalten: ein FESTES Raster (Brett | Zugliste | Auswertung). Die Spaltenbreiten hängen
       nicht am Inhalt — vorher wuchs die Karte, sobald die Auswertung erschien, und lange Linien wurden rechts
       abgeschnitten (gemeldet 2026-10-10). */
    @media (min-width: 1280px) {
      .viewer { --board-size: clamp(360px, min(calc(100vh - 250px - var(--recap-room, 0px)), calc(100vw - var(--moves-width) - var(--side-width) - 140px)), 760px); }
      .body {
        display: grid; flex-wrap: nowrap; gap: 20px;
        grid-template-columns: var(--board-size) var(--moves-width) var(--side-width);
      }
      .side-section {
        max-height: calc(var(--board-size) + 56px); overflow-y: auto; overflow-x: hidden;
        scrollbar-gutter: stable; padding-right: 2px;
      }
    }
    .board-section { width: var(--board-size); display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: var(--board-size); }
    .board-wrap app-chess-board { display: block; width: var(--board-size); }
    .board-tapnav { -webkit-user-select: none; user-select: none; -webkit-touch-callout: none; }
    /* Reihenfolge überall (seit 0.667.0 auch am PC, vorher nur am Handy): Drehen · Anfang ‖ ◀ ▶ ‖ Ende · Live · ⋮ —
       die häufigen Knöpfe groß in der Mitte, die Sprünge mit Abstand daneben: dicht nebeneinander traf man statt
       „einen Zug" oft „ganz an den Anfang/das Ende" (gemeldet 2026-09-24 am Handy, 2026-10-05 am PC). */
    .nav { display: flex; align-items: center; gap: 4px; }
    .nav .nav-flip { order: 0; }
    .nav .nav-start { order: 1; margin-right: 20px; opacity: 0.7; }
    .nav .nav-prev { order: 2; }
    .nav .nav-next { order: 3; }
    .nav .nav-end { order: 4; margin-left: 20px; opacity: 0.7; }
    .nav .live-toggle { order: 5; }
    .nav .nav-menu { order: 6; }
    .nav .nav-prev, .nav .nav-next {
      width: 64px; height: 44px; border-radius: 10px; overflow: hidden;
      background: color-mix(in srgb, currentColor 8%, transparent);
    }
    .nav .nav-prev mat-icon, .nav .nav-next mat-icon { font-size: 30px; width: 30px; height: 30px; }
    .pr-slot, .review-slot, .trainer-slot, .live-slot, .similar-slot { display: block; width: 100%; }
    .live-toggle.on { color: #42a5f5; }
    /* Die Zugliste ist so hoch wie das Brett und scrollt in sich; eine feste Breite, damit die zwei Zugspalten
       nebeneinander stehen statt — bei einer Spalte, die den Rest der Karte füllt — mit einer Handbreit Luft
       dazwischen. */
    .moves-section {
      width: var(--moves-width); height: var(--board-size); flex-shrink: 0; box-sizing: border-box;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 10px; overflow: auto;
    }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { width: auto; padding: 0; border-radius: 0; }
      .header { position: relative; grid-template-columns: auto minmax(0, 1fr); padding: 12px 52px 12px 16px; margin-bottom: 4px; }
      .header-actions { grid-column: 1 / -1; flex-direction: column; align-items: stretch; }
      /* Nur das ⋮ (es sitzt absolut oben rechts): die leere Zeile samt Abstand fällt weg. */
      .header-actions:not(:has(> button:not(.game-menu), > a)) { margin-top: -8px; }
      /* Das ⋮ oben rechts neben den Namen statt allein in einer eigenen Zeile. */
      .header .game-menu { position: absolute; top: 8px; right: 8px; }
      .recap { margin: 0 16px 12px; width: auto; min-width: 0; }
      .side-section { padding: 0 12px; }
      .body { flex-direction: column; align-items: stretch; }
      .side-section { order: 1; }   /* am Handy: Auswertung vor der Zugliste, wie bisher */
      .moves-section { order: 2; }
      .board-section { width: 100%; max-width: 100%; align-items: center; }
      .board-wrap { width: 100%; }
      .board-wrap app-chess-board { width: 100%; }
      /* Reihenfolge am Handy: Drehen · Anfang ‖ ◀ ▶ ‖ Ende · Live — die häufigen Knöpfe groß in der Mitte, die
         Sprünge an den Rand mit Abstand, damit ein daneben getroffener Tipp nicht die ganze Partie überspringt. */
      .nav { width: 100%; box-sizing: border-box; padding: 6px 8px; }
      .nav .nav-start { margin-right: 14px; }
      .nav .nav-end { margin-left: 14px; }
      .nav .nav-prev, .nav .nav-next { flex: 1 1 0; width: auto; height: 48px; }
      .nav .nav-prev mat-icon, .nav .nav-next mat-icon { font-size: 32px; width: 32px; height: 32px; }
      .moves-section {
        width: 100%; height: auto; max-height: 40vh;
        border-left: none; border-right: none; border-radius: 0; border-bottom: none;
      }
    }
  `]
})
export class SharedGameComponent implements OnInit, DoCheck {
  private auth = inject(AuthService);
  private gameAnalyses = inject(GameAnalysisService);
  private handoff = inject(HandoffService);
  /** „In die Vereins-Datenbank" (LeagueHub, Wunsch 2026-09-28): nur mit dem Recht dazu und wenn es ein LeagueHub zu diesem
   * RookHub gibt. Die Partie wird drüben geladen und läuft durch dieselbe Übersicht wie ein PGN-Upload. */
  readonly clubImport = this.auth.has('league.contribute') && !!this.handoff.leagueHubUrl;
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private analyzeGame = inject(AnalyzeGameService);
  private mistakeJudge = inject(MistakeJudgeService);
  private externalEngines = inject(ExternalEngineService);
  private scoresheets = inject(ScoresheetService);
  private dialog = inject(MatDialog);

  game: SharedGame | null = null;
  loading = true;
  notFound = false;
  flipped = false;
  /** Eigene Partie (`/games/:id`) statt Teilen-Link — entscheidet Datenquelle, Adressen und Kopfzeile. */
  own = false;
  /** Vereinspartie aus LeagueHub (`/club-games/:id`, 0.653.0): Bewertungen aus der Hintergrund-Analyse des Vereins,
   *  kein „Partie analysieren" (die rechnet von selbst), keine Erklärungen (die gibt es dort nicht). */
  club = false;
  /** Teilen-Token der eigenen Partie (für „Teilen-Link kopieren"). */
  shareToken: string | null = null;

  get notFoundKey(): string { return this.own || this.club ? 'games.loadError' : 'games.shared.notFound'; }
  /** `GET …/evals` dieser Partie — anonym die Kurve des Teilenden, angemeldet ersatzweise die eigene. */
  evalsUrl: string | null = null;
  /** `GET …/similar` — ähnliche Meisterpartien (0.544.0). */
  similarUrl: string | null = null;
  private analyzeUrl = '';
  // Signale statt Felder: sie ändern sich in HTTP-Antworten und in der Ausgabe der Kind-Komponente,
  // und nur ein gelesenes Signal markiert die Ansicht zuverlässig zum Neuzeichnen (Angular 22).
  /** Läuft gerade der Aufruf „Partie analysieren"? Sperrt den Knopf gegen Doppelklick — zwei Klicks
   *  binnen Millisekunden sähe auch der Server noch nicht als dieselbe Analyse. */
  readonly analyzing = signal(false);
  /** Ob eine Engine da ist und wie viele Partien noch frei sind — nur angemeldet abgefragt (der
   *  Endpunkt braucht ein Konto); ohne Antwort bleibt der Knopf benutzbar und der Server entscheidet. */
  readonly uploadStatus = signal<GuessUploadStatus | null>(null);
  /** Stand der Kurve unter dem Brett (`none` = noch keine Analyse → Knopf anbieten). */
  readonly reviewStatus = signal<GameEvalsStatus>('none');
  private readonly review = viewChild(GameReviewComponent);
  /** Abfragbare Fehler beider Seiten, gemeldet vom Rückblick unter dem Brett. */
  readonly mistakes = signal<MistakesBySide>(NO_MISTAKES);
  /** Wessen Partie — als Signal, damit Zähler und Trainer-Seite nachziehen, sobald die Partie da ist. */
  private readonly ownerSide = signal<'white' | 'black' | null>(null);
  /** Die Seite, die der Trainer abfragt — der Knopf zählt NUR sie (siehe `trainingSide`). */
  readonly mistakeSide = computed(() => trainingSide(this.mistakes(), this.ownerSide()));
  readonly mistakeTotal = computed(() => mistakesOf(this.mistakes(), this.mistakeSide()).length);

  analysisRunning(): boolean {
    const s = this.reviewStatus();
    return s === 'pending' || s === 'running';
  }

  /** Laufendes Training „Eigene Fehler nachspielen" — `null` = die Seite zeigt die Partie. */
  readonly training = signal<MistakesSession | null>(null);
  /** Id der eigenen Partie (`/games/:id`) — ohne sie wird nichts gemeldet (geteilte Ansicht). */
  gameId: number | null = null;

  toClub(): void {
    if (this.gameId) void this.handoff.jumpToLeagueHub(`verein/neu?partie=${this.gameId}`);
  }
  /** Formular-Einlesung der eigenen Partie (0.529.0) — `null` = kein Foto. */
  scanId: number | null = null;
  /** LeagueHub-Adresse für „Korrigieren" einer Vereinspartie (0.660.0); `null` außerhalb der Partner-Domains. */
  readonly leagueHub = leagueHubUrl();
  /** Schon gemeldete Halbzüge und Aufgabenzahl: verhindert, dass jeder Zug dieselbe Meldung wiederholt. */
  private readonly reportedPlies = new Set<number>();
  private reportedTotal = -1;
  /** Pfeil für den besten Zug, geliefert vom Rückblick (Schalter dort); im Training leer. */
  readonly bestArrows = signal<BoardArrow[]>([]);
  /** Klasse des aktuellen Zugs als Symbol am Zielfeld (vom Rückblick; im Training und mit Live-Engine leer). */
  readonly moveBadge = signal<BoardBadge | null>(null);

  /**
   * „Kurz erzählt" (0.541.0) über der Partie. Der Teilen-Link bringt den Text gleich mit; die eigene Seite holt ihn, sobald
   * die Analyse fertig ist — er entsteht dann gerade (oder, bei einer älteren Analyse, durch genau diesen Abruf), deshalb
   * fragt sie ein paar Mal nach.
   */
  readonly recap = signal<string | null>(null);
  private recapTries = 0;
  private recapTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly loadRecap = effect(() => {
    if (this.reviewStatus() !== 'done') return;
    untracked(() => {
      if (this.own && this.gameId && !this.recap() && this.recapTries === 0) this.fetchRecap();
    });
  });
  private readonly stopRecapOnDestroy = inject(DestroyRef).onDestroy(() => {
    if (this.recapTimer) clearTimeout(this.recapTimer);
  });

  private fetchRecap(): void {
    if (!this.gameId) return;
    this.recapTries++;
    this.games.recap(this.gameId).subscribe({
      next: r => {
        if (r.text) { this.recap.set(r.text); return; }
        if (r.pending && this.recapTries < RECAP_TRIES) this.recapTimer = setTimeout(() => this.fetchRecap(), RECAP_RETRY_MS);
      },
      error: () => { /* ohne Nacherzählung bleibt die Seite wie bisher */ },
    });
  }

  /**
   * Zugliste und Kurve laufen mit: je Aufgabe springt die Partie auf die Stellung VOR dem Fehler — man
   * sieht, wo in der Partie man ist, und nach dem Beenden steht man genau dort.
   */
  private readonly followTask = effect(() => {
    const m = this.training()?.current();
    if (m) this.service.goToMove(m.ply - 1);
  });

  /**
   * Trainiert wird die Seite des Besitzers; ohne Zuordnung (fremde geteilte Partie) die mit den meisten
   * Fehlern — in der Leiste lässt sich umschalten, sobald beide Seiten welche haben. Gespielt wird auf dem
   * Brett dieser Seite (bis 0.518.0 ein Dialog mit eigenem, kleinerem Brett).
   */
  /** Jeder Treffer wird gemeldet — auch der, den die Browser-Engine erst verzögert bestätigt. */
  private readonly meldeTreffer = effect(() => {
    const t = this.training();
    const solved = t?.solvedPlies() ?? [];
    if (t && solved.length) untracked(() => this.reportMistakes(t, solved));
  });

  /** Live-Engine + eigene Züge (0.525.0) — im Fehler-Training aus, dort verriete sie die Lösung. */
  readonly live = signal<LiveEngineSession | null>(null);
  private readonly stopLiveOnDestroy = inject(DestroyRef).onDestroy(() => { this.stopLive(); this.stopTrainingAnalysis(); });

  /** Grobe Zeiger (Handy, Tablet): dort heißt ein Tipp aufs Brett „blättern", gezogen wird per Drag. */
  readonly coarsePointer = typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches;
  /** Beginn einer Berührung auf dem Brett — ob daraus ein Tipp wird, entscheidet das Loslassen. */
  tapStart: { x: number; y: number; t: number } | null = null;

  onBoardPointerDown(e: PointerEvent): void {
    this.tapStart = e.pointerType === 'touch' && e.isPrimary ? { x: e.clientX, y: e.clientY, t: Date.now() } : null;
  }

  /** Ein TIPP (kurz, ohne Bewegung) auf die linken bzw. rechten 40 % des Bretts = Zug zurück bzw. vor — wie vorher die
   *  Tippzonen. Alles mit Bewegung ist ein Ziehen und gehört dem Brett. Nur Touch: mit der Maus gibt es die Pfeile. */
  onBoardTap(e: PointerEvent): void {
    const s = this.tapStart;
    this.tapStart = null;
    if (!s || e.pointerType !== 'touch') return;
    if (Math.hypot(e.clientX - s.x, e.clientY - s.y) > TAP_SLOP_PX || Date.now() - s.t > TAP_MAX_MS) return;
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
    if (r.width <= 0) return;
    const rel = (e.clientX - r.left) / r.width;
    if (rel < 0.4) this.navPrev();
    else if (rel > 0.6) this.navNext();
  }

  /** Eine Figur auf dem Partie-Brett gezogen: wie auf Lichess eine eigene Variante ab hier — dafür gibt es die Live-Engine
   *  (sie führt die Nebenvariante und rechnet sie gleich). Ist sie aus, wird sie dafür eingeschaltet. */
  onBoardMove(move: UserBoardMove): void {
    if (!this.live()) this.toggleLive();
    this.live()?.play(move, this.service.currentFen);
  }

  toggleLive(): void {
    if (this.live()) { this.stopLive(); return; }
    const session = this.createLiveSession();
    this.live.set(session);
    session.sync(this.service.currentMoveIndex, this.service.currentFen);
    this.useStoredRemoteEngine(session);
  }

  /** Eigene Engine-Instanz je Seite — als Methode, damit Tests keinen echten Stockfish starten müssen. */
  protected createLiveSession(): LiveEngineSession {
    return new LiveEngineSession(undefined, this.storedDepth());
  }

  /** Die Stellung auf dem Brett — in der Nebenvariante der Live-Engine deren Ende, sonst der Partiezug. */
  positionFen(): string {
    return this.live()?.fen(this.service.currentFen) ?? this.service.currentFen;
  }

  stopLive(): void {
    this.live()?.destroy();
    this.live.set(null);
  }

  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  /** Hinterlegtes der Partie-Analyse zur Stellung auf dem Brett (aus dem Rückblick). */
  readonly reviewStored = signal<DeepStored>({ sf: null, lc0: null });
  /** „Tiefe Analyse" im ⋮-Menü (0.686.0): nur angemeldet; in einer eigenen Nebenvariante gibt es nichts Hinterlegtes. */
  deepStored(): DeepStored | null {
    if (!this.loggedIn) return null;
    return this.live()?.variation()?.length ? { sf: null, lc0: null } : this.reviewStored();
  }

  /** Externe Engines fuer die Live-Wahl im ⋮ (0.681.0) — ohne die Hintergrund-Engines, die gehoeren den Auftraegen
   *  (dieselbe Regel wie beim Uebernehmen der gemerkten Wahl). */
  readonly liveEngines = signal<ExternalEngineInfo[]>([]);

  loadLiveEngines(): void {
    if (!this.auth.isLoggedIn) return;
    this.externalEngines.listEngines().subscribe({
      next: r => {
        const background = r.backgroundEngineIds ?? [];
        this.liveEngines.set(r.engines.filter(e => !background.includes(e.id)));
      },
      error: () => this.liveEngines.set([]),
    });
  }

  /** Die gerade gewaehlte Live-Engine: die der laufenden Sitzung, sonst die gemerkte Wahl; `null` = Browser. */
  currentEngineId(): string | null {
    const session = this.live();
    if (session) return session.engineId();
    try {
      const stored = localStorage.getItem(ANALYSIS_PROVIDER_KEY);
      return stored && stored !== 'wasm' ? stored : null;
    } catch { return null; }
  }

  engineOffline(e: ExternalEngineInfo): boolean { return isEngineOffline(e); }
  engineTag(e: ExternalEngineInfo): string | null { return engineTagKey(e); }

  /**
   * Live-Engine waehlen (⋮ → Live-Engine). Gemerkt wird sie unter DEMSELBEN Schluessel wie am Analysebrett, die Wahl
   * gilt also auf beiden Seiten. Laeuft die Live-Engine schon, wechselt sie sofort; sonst startet sie mit der Wahl.
   */
  chooseLiveEngine(e: ExternalEngineInfo | null): void {
    try { localStorage.setItem(ANALYSIS_PROVIDER_KEY, e?.id ?? 'wasm'); } catch { /* kein Speicher: gilt nur jetzt */ }
    const session = this.live();
    if (!session) { this.toggleLive(); return; }
    if (e) session.useRemote(e, (id, work) => this.externalEngines.analyse(id, work));
    else session.useBrowser();
  }

  /**
   * „Analysieren" im Fehler-Training (seit 0.526.2): das Brett wird frei, die Live-Engine rechnet. Gilt bis zur nächsten
   * Aufgabe: `ngDoCheck` beendet die Analyse, sobald die Aufgabe, die Seite oder die Phase („nochmal") wechselt.
   *
   * WO die Analyse anfängt, hängt daran, ob die Lösung schon bekannt ist (seit 0.527.2, gemeldet 2026-09-24):
   * - gefunden/gezeigt: ab der Stellung VOR dem Fehler, der Zug steht schon drauf — ← darf dorthin zurück;
   * - nach einem Fehlversuch: ab der Stellung NACH dem eigenen Zug, und das ist der Anfang der Variante. Wer ← dahinter
   *   zurückkönnte, stünde in der Aufgabenstellung, und die Engine zeigte dort Pfeil und Linien des gesuchten Zugs.
   */
  readonly trainingAnalysis = signal<{
    session: LiveEngineSession; base: string; index: number; side: string;
    /** Zug, der auf dem Brett markiert bleibt, solange die Variante leer ist (der eigene Fehlversuch). */
    lastMove?: [string, string];
  } | null>(null);

  toggleTrainingAnalysis(): void {
    if (this.trainingAnalysis()) { this.stopTrainingAnalysis(); return; }
    const t = this.training();
    const m = t?.current();
    if (!t || !m) return;
    const session = this.createLiveSession();
    const move = t.lastMove();
    if (t.phase() === 'wrong') {
      const base = t.boardFen();
      session.sync(-2, base);
      this.useStoredRemoteEngine(session);
      this.trainingAnalysis.set({ session, base, index: t.index(), side: t.side(), lastMove: move ?? undefined });
      return;
    }
    session.sync(-2, m.fenBefore);
    const san = t.phase() === 'right' ? t.foundSan() : m.bestSan;
    if (move && t.boardFen() !== m.fenBefore) {
      session.play({ from: move[0], to: move[1], san, fen: t.boardFen() }, m.fenBefore);
    }
    this.useStoredRemoteEngine(session);
    this.trainingAnalysis.set({ session, base: m.fenBefore, index: t.index(), side: t.side() });
  }

  stopTrainingAnalysis(): void {
    this.trainingAnalysis()?.session.destroy();
    this.trainingAnalysis.set(null);
  }

  /** Mit der Partie abgleichen: geblättert → Nebenvariante weg, neue Stellung → rechnen (billig ohne Änderung). */
  ngDoCheck(): void {
    this.live()?.sync(this.service.currentMoveIndex, this.service.currentFen);
    const a = this.trainingAnalysis();
    if (a) {
      const t = this.training();
      const phase = t?.phase();
      if (!t || t.index() !== a.index || t.side() !== a.side || phase === 'ask' || phase === 'checking' || phase === 'done') {
        this.stopTrainingAnalysis();
      } else {
        a.session.sync(-2, a.base);
      }
    }
  }

  /** Dieselbe Tiefe wie am Analysebrett (dort gewählt und gemerkt), sonst 22. */
  private storedDepth(): number {
    try {
      const d = parseInt(localStorage.getItem(ANALYSIS_DEPTH_KEY) || '', 10);
      return d >= 6 && d <= 50 ? d : 22;
    } catch { return 22; }
  }

  /**
   * Hat man am Analysebrett eine externe Engine gewählt (Lichess-Anbindung, z. B. die eigene Cloud-Engine), rechnet
   * auch hier sie — sonst Stockfish im Browser. Hintergrund-Engines bleiben außen vor (sie gehören den Aufträgen).
   */
  private useStoredRemoteEngine(session: LiveEngineSession): void {
    if (!this.auth.isLoggedIn) return;
    let stored: string | null = null;
    try { stored = localStorage.getItem(ANALYSIS_PROVIDER_KEY); } catch { /* kein Speicher → Browser */ }
    if (!stored || stored === 'wasm') return;
    this.externalEngines.listEngines().subscribe({
      next: r => {
        const background = r.backgroundEngineIds ?? [];
        const info = r.engines.find(e => e.id === stored && !background.includes(e.id));
        // Nur, solange die Session noch läuft: die Live-Engine ODER die „Analysieren"-Leiste des Fehler-Trainings
        // (dort ist live() immer null, trainMistakes beendet sie — F4-008).
        const active = this.live() === session || this.trainingAnalysis()?.session === session;
        if (info && active) session.useRemote(info, (id, work) => this.externalEngines.analyse(id, work));
      },
      error: () => { /* bleibt beim Browser */ },
    });
  }

  trainMistakes(): void {
    this.stopLive();
    // Nicht gelistete Züge prüft die Browser-Engine nach (nur wo die Analyse das offen lässt).
    this.training.set(new MistakesSession(this.mistakes(), this.mistakeSide(),
      (m, fen) => this.mistakeJudge.judge(m, fen), fen => this.mistakeJudge.evaluate(fen)));
  }

  onTrainingMove(e: UserBoardMove): void {
    this.training()?.onMove(e);
  }

  endTraining(): void {
    // Auch ohne neuen Treffer melden: dann steht in der Übersicht „0 von 7", und man sieht, dass die
    // Partie schon einmal offen war. Der Server vereinigt additiv, hier geht nichts verloren.
    const t = this.training();
    if (t) this.reportMistakes(t, t.solvedPlies());
    this.stopTrainingAnalysis();
    this.training.set(null);
  }

  /**
   * Gefundene Fehler an RookHub melden — daraus zeigt `/games` „4 von 7 · 3 offen". Nur bei der EIGENEN
   * Partie (die geteilte Ansicht kennt keine Id des Betrachters). Still im Fehlerfall: das ist
   * Buchführung im Hintergrund, und die nächste Meldung trägt denselben Stand erneut.
   */
  private reportMistakes(session: MistakesSession, solved: readonly number[]): void {
    if (!this.own || !this.gameId) return;
    const neu = solved.filter(p => !this.reportedPlies.has(p));
    if (!neu.length && this.reportedTotal === session.list().length) return;
    neu.forEach(p => this.reportedPlies.add(p));
    this.reportedTotal = session.list().length;
    this.games.recordMistakes(this.gameId, session.list().length, [...solved]).subscribe({
      error: () => { neu.forEach(p => this.reportedPlies.delete(p)); this.reportedTotal = -1; },
    });
  }

  analyzeTooltip(): string {
    if (this.analysisRunning()) return 'games.review.running';
    return this.uploadStatus()?.engineAvailable === false ? 'guess.upload.noEngine' : 'games.analyzeHint';
  }

  constructor(
    public service: PgnViewerService,
    private route: ActivatedRoute,
    private games: GamesService,
    public preferences: PreferencesService,
  ) {}

  ngOnInit(): void {
    this.own = this.route.snapshot.data?.['mode'] === 'own';
    this.club = this.route.snapshot.data?.['mode'] === 'club';
    if (this.club) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      this.evalsUrl = this.games.clubEvalsUrl(id);
      this.games.clubGame(id).subscribe({
        next: g => this.show(g),
        error: () => { this.notFound = true; this.loading = false; },
      });
      return;
    }
    if (this.own) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      this.gameId = id;
      this.evalsUrl = this.games.evalsUrl(id);
      this.similarUrl = this.games.similarUrl(id);
      this.analyzeUrl = this.games.analyzeUrl(id);
      this.games.get(id).subscribe({
        next: g => { this.shareToken = g.shareToken; this.scanId = g.scanId ?? null; this.show(g); },
        error: () => { this.notFound = true; this.loading = false; },
      });
      return;
    }
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.evalsUrl = this.games.sharedEvalsUrl(token);
    this.similarUrl = this.games.sharedSimilarUrl(token);
    this.analyzeUrl = this.games.sharedAnalyzeUrl(token);
    this.games.getShared(token).subscribe({
      next: g => {
        // Der eigene Teilen-Link: dieselbe Ansicht wie über die Partienliste (Zurück-Pfeil, Teilen-Knopf, gemerktes
        // Fehler-Training …) — gewünscht 2026-09-24. `replaceUrl`, damit „Zurück" im Browser nicht wieder hierher führt.
        if (g.ownGameId && this.auth.isLoggedIn) {
          this.router.navigate(['/games', g.ownGameId], { replaceUrl: true });
          return;
        }
        this.show(g);
      },
      error: () => { this.notFound = true; this.loading = false; },
    });
  }

  private show(g: SharedGame): void {
    this.game = g;
    this.recap.set(g.recap ?? null);
    this.ownerSide.set(g.ownerSide ?? null);
    // Aus der Sicht des Besitzers: spielte er Schwarz, startet das Brett gedreht (Flip-Knopf bleibt).
    this.flipped = g.ownerSide === 'black';
    this.service.loadPgn(g.pgn);
    // `?ply=n`: aus einer geernteten Aufgabe hierher — auf die Stellung nach n Halbzügen springen
    const ply = Number(this.route.snapshot.queryParamMap?.get('ply'));
    if (Number.isInteger(ply) && ply > 0) this.service.goToMove(ply - 1);
    this.loading = false;
    if (this.auth.isLoggedIn) {
      this.analyzeGame.status().subscribe(u => this.uploadStatus.set(u));
    }
  }

  /** Das Formular-Foto der eigenen, eingelesenen Partie anzeigen (Dialog) oder herunterladen. */
  /** „Roast my game" (0.535.0) — nur die eigene Partie. */
  roast(): void {
    if (!this.gameId) return;
    this.dialog.open(GameRoastDialogComponent, {
      data: { gameId: this.gameId, shareUrl: this.shareToken ? this.games.shareUrl(this.shareToken) : null } satisfies GameRoastData,
      maxWidth: '96vw',
    });
  }

  photo(download: boolean): void {
    if (!this.gameId) return;
    if (!download) { ScoresheetPhotoDialogComponent.open(this.dialog, this.gameId); return; }
    this.scoresheets.photo(this.gameId).subscribe({
      next: blob => openPhotoBlob(blob, photoFileName(this.gameId!, blob)),
      error: () => this.snackbar.warn(this.translate.instant('games.photo.loadError')),
    });
  }

  /** Im Analysebrett öffnen — mit dem GANZEN PGN (Kopfdaten, Kommentare), nicht nur den Zügen (0.592.0); „Zurück" führt
   *  hierher. Übergabe per Router-State wie aus der Partienliste. */
  openInAnalysis(): void {
    const pgn = this.game?.pgn;
    if (!pgn) return;
    void this.router.navigate(['/analysis'], { state: { pgn }, queryParams: { from: this.router.url } });
  }

  /** Das PGN der Partie in die Zwischenablage (so, wie es gespeichert ist — samt Kopfdaten und Kommentaren). */
  /** „Mit Lc0 analysieren" — angemeldet, Vereinsmitglied, und die Partie hat noch keine Lc0-Analyse. */
  canLc0(): boolean {
    return this.loggedIn && this.auth.has('league.view') && !!this.game?.pgn && !this.review()?.hasLc0();
  }

  analyzeLc0(): void {
    const pgn = this.game?.pgn;
    if (!pgn) return;
    this.gameAnalyses.startLc0(pgn).subscribe({
      next: () => {
        this.snackbar.success(this.translate.instant('games.lc0.started'));
        this.review()?.refreshAlternatives();
      },
      error: () => this.snackbar.warn(this.translate.instant('games.lc0.failed')),
    });
  }

  copyPgn(): void {
    const pgn = this.game?.pgn;
    if (!pgn) return;
    navigator.clipboard?.writeText(pgn).then(
      () => this.snackbar.copy(this.translate.instant('games.pgnCopied')),
      () => this.snackbar.warn(this.translate.instant('games.pgnCopyFailed')),
    );
  }

  /** Das PGN als Datei „Weiß_Schwarz_Datum.pgn". */
  downloadPgn(): void {
    const g = this.game;
    if (!g?.pgn) return;
    const players = [g.white, g.black].filter(n => !!n).join(' ');
    downloadBlob(new Blob([g.pgn.endsWith('\n') ? g.pgn : g.pgn + '\n'], { type: 'application/x-chess-pgn' }),
      pgnFileName(players || 'game', (g.playedAt ?? g.createdAt ?? '').slice(0, 10)));
  }

  /** Teilen-Link der eigenen Partie in die Zwischenablage — wie in der Liste. */
  share(): void {
    if (!this.shareToken) return;
    const url = this.games.shareUrl(this.shareToken);
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('games.shareCopied')),
      () => this.snackbar.warn(url),
    );
  }

  /** Die Partie rechnen lassen (siehe {@link AnalyzeGameService}). Die Seite ist ohne Anmeldung
   *  erreichbar, der Einwurf nicht: ohne Konto geht es zur Anmeldung und danach wieder hierher. Danach
   *  bleibt man HIER — die Kurve lädt neu und fragt nach, bis die Partie durch ist. */
  analyze(): void {
    if (!this.game || this.analyzing()) return;
    if (!this.auth.isLoggedIn) {
      this.snackbar.info(this.translate.instant('games.analyzeLogin'));
      this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
      return;
    }
    this.analyzing.set(true);
    this.analyzeGame.submit(this.analyzeUrl, this.uploadStatus()).subscribe(ok => {
      this.analyzing.set(false);
      if (ok) this.review()?.reload();
    });
  }

  // ----- Knöpfe unter dem Brett, Tipp aufs Brett, ← → (0.667.0): in einer eigenen Variante durch SIE -----

  /** Steht man in einer eigenen Variante (Live-Engine, mindestens ein eigener Zug auf dem Brett oder dahinter)? */
  private inVariation(): LiveEngineSession | null {
    const l = this.live();
    return l && (l.variation().length > 0 || l.canRedo()) ? l : null;
  }

  canNavBack(): boolean {
    return !!this.inVariation()?.variation().length || this.service.currentMoveIndex >= 0;
  }

  canNavForward(): boolean {
    const v = this.inVariation();
    if (v && v.variation().length) return v.canRedo();   // am Variantenende geht es nicht in der Partie weiter
    const g = this.service.currentGame;
    return !!v?.canRedo() || (!!g && this.service.currentMoveIndex < g.moves.length - 1);
  }

  navPrev(): void {
    const v = this.inVariation();
    if (v && v.variation().length) { v.undo(this.service.currentFen); return; }
    this.service.goBack();
  }

  navNext(): void {
    const v = this.inVariation();
    if (v) { if (v.canRedo()) v.redo(this.service.currentFen); return; }
    this.service.goForward();
  }

  /** ⏮: in einer Variante an ihren Anfang (Abzweig, die Züge bleiben), sonst an den Partieanfang. */
  navStart(): void {
    const v = this.inVariation();
    if (v && v.variation().length) { v.toStart(this.service.currentFen); return; }
    this.service.goToStart();
  }

  /** ⏭: in einer Variante an ihr Ende, sonst ans Partieende. */
  navEnd(): void {
    const v = this.inVariation();
    if (v) { v.toEnd(this.service.currentFen); return; }
    this.service.goToEnd();
  }

  /** ← / → in einer eigenen Variante; `true`, wenn die Taste dort etwas getan hat (oder am Variantenende nichts tun darf). */
  private stepVariation(event: KeyboardEvent, session: LiveEngineSession, baseFen: string): boolean {
    if (event.key === 'ArrowLeft' && session.variation().length) {
      event.preventDefault(); session.undo(baseFen); return true;
    }
    if (event.key === 'ArrowRight' && (session.canRedo() || session.variation().length)) {
      event.preventDefault(); session.redo(baseFen); return true;
    }
    return false;
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    // Nicht im Titel-Feld des Auftragsdialogs (der Cursor blieb stehen, die Partie dahinter blaetterte) und
    // nicht in einem offenen Menue/Dialog.
    if (!isBoardHotkey(event)) return;
    // Im Training zeigt das Brett die Aufgabe — die Pfeile blätterten sonst unsichtbar in der Partie darunter. In
    // der Analyse einer Aufgabe laufen sie durch die eigene Variante.
    const t = this.training();
    if (t) {
      // Leertaste nach der Lösung (gefunden oder gezeigt) = nächste Aufgabe (seit 0.526.2). Der Fokus geht vorher
      // vom Knopf weg — sonst löste die Leertaste den zuletzt geklickten Knopf beim Loslassen ein zweites Mal aus.
      if (event.key === ' ' && (t.phase() === 'right' || t.phase() === 'shown')) {
        event.preventDefault();
        (document.activeElement as HTMLElement | null)?.blur?.();
        t.next();
        return;
      }
      const a = this.trainingAnalysis();
      if (a) this.stepVariation(event, a.session, a.base);
      return;
    }
    // In der eigenen Nebenvariante laufen ← und → durch sie (← zurück, → wieder vor); an ihrem Anfang blättert ←
    // wie gewohnt in der Partie.
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.navPrev(); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.navNext(); }
  }
}
