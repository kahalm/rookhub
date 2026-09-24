import { Component, OnInit, HostListener, inject, ChangeDetectionStrategy, computed, effect, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { PgnViewerService } from '../../shared/pgn-viewer/pgn-viewer.service';
import { PreferencesService } from '../../core/preferences.service';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GuessUploadStatus } from '../analysis/game-analysis.service';
import { AnalyzeGameService } from './analyze-game.service';
import { GamesService, SharedGame } from './games.service';
import { GameReviewComponent } from './game-review.component';
import { GameEvalsStatus } from './game-review.util';
import { MistakesBySide, NO_MISTAKES, mistakesOf, trainingSide } from './mistakes.util';
import { MistakesTrainerComponent } from './mistakes-trainer.component';
import { MistakesSession } from './mistakes-session';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';

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
    MistakesTrainerComponent,
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
                <span>{{ game.source }}</span>
                <span class="date">{{ (game.playedAt || game.createdAt) | date:'mediumDate' }}</span>
              </span>
            </div>
            <!-- Am PC in die Kopfzeile: als eigene Zeile unter Brett und Zugliste war der Knopf so breit wie
                 die Karte und stand mitten im Leeren. Auf dem Handy fällt die Kopfzeile in eine Spalte. -->
            <div class="header-actions">
              <!-- Die Partie wird im Hintergrund gerechnet (Haus-Engine, feste Tiefe), die Bewertungskurve erscheint
                   darauf unter dem Brett. Ohne Anmeldung führt der Klick zur Anmeldung und wieder hierher zurück —
                   der Knopf bleibt sichtbar, damit man weiß, dass es den Weg gibt. Ist die Kurve fertig, entfällt
                   er; solange sie rechnet, ist er gesperrt und sagt es. -->
              @if (reviewStatus() !== 'done') {
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
            </div>
          </div>
          <div class="body">
            <div class="board-section">
              <div class="board-wrap">
                @if (training(); as t) {
                  <!-- Dasselbe Brett wie beim Nachspielen, nur mit der Stellung der Aufgabe und spielbar. -->
                  <app-chess-board [fen]="t.boardFen()" [lastMove]="t.lastMove()" [flipped]="t.flipped()"
                                   [playable]="t.playable()" (userMove)="onTrainingMove($event)"
                                   [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                } @else {
                  <app-chess-board [fen]="service.currentFen" [lastMove]="service.lastMove" [flipped]="flipped"
                                   [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                  <!-- Die Tippzonen blieben im Training über dem Brett liegen und schluckten jeden Zug. -->
                  <div class="board-tap board-tap-prev" (click)="service.goBack()"></div>
                  <div class="board-tap board-tap-next" (click)="service.goForward()"></div>
                }
              </div>
              @if (training(); as t) {
                <app-mistakes-trainer class="trainer-slot" [session]="t" (closed)="endTraining()" />
              } @else {
              <div class="nav">
                <button mat-icon-button (click)="service.goToStart()" [disabled]="service.currentMoveIndex < 0"><mat-icon>skip_previous</mat-icon></button>
                <button mat-icon-button (click)="service.goBack()" [disabled]="service.currentMoveIndex < 0"><mat-icon>navigate_before</mat-icon></button>
                <button mat-icon-button (click)="service.goForward()" [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"><mat-icon>navigate_next</mat-icon></button>
                <button mat-icon-button (click)="service.goToEnd()" [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"><mat-icon>skip_next</mat-icon></button>
                <button mat-icon-button (click)="flipped = !flipped"><mat-icon>swap_vert</mat-icon></button>
              </div>
              }
              @if (service.currentGame; as g) {
                <app-game-review class="review-slot" [evalsUrl]="evalsUrl" [fens]="g.fens" [moves]="g.moves"
                                 [currentIndex]="service.currentMoveIndex"
                                 (moveClicked)="service.goToMove($event)"
                                 (statusChange)="reviewStatus.set($event)"
                                 (mistakesChange)="mistakes.set($event)" />
              }
              <app-position-repertoires class="pr-slot" [fen]="service.currentFen" />
            </div>
            <div class="moves-section">
              @if (service.currentGame; as g) {
                <app-move-list [moves]="g.moves" [currentMoveIndex]="service.currentMoveIndex" [comments]="g.comments" (moveClicked)="service.goToMove($event)" />
              }
            </div>
          </div>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .shared-page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    /* Die Karte umschließt ihren Inhalt (Brett + Zugliste) und steht mittig, statt sich auf die Seitenbreite zu
       dehnen und rechts von der Zugliste leer zu bleiben. Das Brett wächst mit dem Fenster: so hoch, dass
       Kopfzeile und Steuerleiste noch Platz haben, und so breit, dass die Zugliste daneben passt — aber nie
       über 640 px (darüber wird es ein Poster) und nie unter 360 px (gemeldet 2026-09-23: 400 px auf einem
       2250 px breiten Bildschirm, zwei Drittel der Seite leer). */
    .viewer {
      --board-size: clamp(360px, min(calc(100vh - 300px), calc(100vw - 440px)), 640px);
      width: fit-content; max-width: 100%; margin: 0 auto; padding: 16px 20px 20px; box-sizing: border-box;
    }
    .header { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px 16px; flex-wrap: wrap; margin-bottom: 12px; }
    .header-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .players { font-size: 1.05rem; }
    .players .elo { font-weight: 400; font-size: 0.85em; color: color-mix(in srgb, currentColor 60%, transparent); }
    .meta { display: flex; gap: 10px; font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .result { color: #1976d2; font-weight: 600; }
    .header-actions { display: flex; align-items: center; gap: 8px; flex: 0 0 auto; flex-wrap: wrap; }
    .original, .analyze { white-space: nowrap; }
    .body { display: flex; gap: 20px; align-items: flex-start; }
    .board-section { width: var(--board-size); display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: var(--board-size); }
    .board-wrap app-chess-board { display: block; width: var(--board-size); }
    .board-tap {
      display: none;
      position: absolute;
      top: 0; bottom: 0;
      width: 40%;
      z-index: 10;
      cursor: pointer;
    }
    .board-tap-prev { left: 0; }
    .board-tap-next { right: 0; }
    .nav { display: flex; gap: 4px; }
    .pr-slot, .review-slot, .trainer-slot { display: block; width: 100%; }
    /* Die Zugliste ist so hoch wie das Brett und scrollt in sich; eine feste Breite, damit die zwei Zugspalten
       nebeneinander stehen statt — bei einer Spalte, die den Rest der Karte füllt — mit einer Handbreit Luft
       dazwischen. */
    .moves-section {
      width: 300px; height: var(--board-size); flex-shrink: 0; box-sizing: border-box;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; overflow: auto;
    }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { width: auto; padding: 0; border-radius: 0; }
      .header { flex-direction: column; align-items: stretch; padding: 12px 16px; }
      .header-actions { flex-direction: column; align-items: stretch; }
      .body { flex-direction: column; align-items: stretch; }
      .board-section { width: 100%; max-width: 100%; align-items: center; }
      .board-wrap { width: 100%; }
      .board-wrap app-chess-board { width: 100%; }
      .board-tap { display: block; }
      .nav { justify-content: center; padding: 4px 0; }
      .moves-section {
        width: 100%; height: auto; max-height: 40vh;
        border-left: none; border-right: none; border-radius: 0; border-bottom: none;
      }
    }
  `]
})
export class SharedGameComponent implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private analyzeGame = inject(AnalyzeGameService);

  game: SharedGame | null = null;
  loading = true;
  notFound = false;
  flipped = false;
  /** Eigene Partie (`/games/:id`) statt Teilen-Link — entscheidet Datenquelle, Adressen und Kopfzeile. */
  own = false;
  /** Teilen-Token der eigenen Partie (für „Teilen-Link kopieren"). */
  shareToken: string | null = null;

  get notFoundKey(): string { return this.own ? 'games.loadError' : 'games.shared.notFound'; }
  /** `GET …/evals` dieser Partie — anonym die Kurve des Teilenden, angemeldet ersatzweise die eigene. */
  evalsUrl: string | null = null;
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
  trainMistakes(): void {
    this.training.set(new MistakesSession(this.mistakes(), this.mistakeSide()));
  }

  onTrainingMove(e: UserBoardMove): void {
    this.training()?.onMove(e);
  }

  endTraining(): void {
    this.training.set(null);
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
    if (this.own) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      this.evalsUrl = this.games.evalsUrl(id);
      this.analyzeUrl = this.games.analyzeUrl(id);
      this.games.get(id).subscribe({
        next: g => { this.shareToken = g.shareToken; this.show(g); },
        error: () => { this.notFound = true; this.loading = false; },
      });
      return;
    }
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.evalsUrl = this.games.sharedEvalsUrl(token);
    this.analyzeUrl = this.games.sharedAnalyzeUrl(token);
    this.games.getShared(token).subscribe({
      next: g => this.show(g),
      error: () => { this.notFound = true; this.loading = false; },
    });
  }

  private show(g: SharedGame): void {
    this.game = g;
    this.ownerSide.set(g.ownerSide ?? null);
    // Aus der Sicht des Besitzers: spielte er Schwarz, startet das Brett gedreht (Flip-Knopf bleibt).
    this.flipped = g.ownerSide === 'black';
    this.service.loadPgn(g.pgn);
    this.loading = false;
    if (this.auth.isLoggedIn) {
      this.analyzeGame.status().subscribe(u => this.uploadStatus.set(u));
    }
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

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    // Im Training zeigt das Brett die Aufgabe — die Pfeile blätterten sonst unsichtbar in der Partie darunter.
    if (this.training()) return;
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.service.goBack(); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.service.goForward(); }
  }
}
