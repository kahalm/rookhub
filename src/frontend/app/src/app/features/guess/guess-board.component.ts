import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GuessResult, GuessReviewMove, GuessService, GuessSession } from './guess.service';

/** Umwandlungsfigur aus dem SAN („e8=Q+" → „q"); leer, wenn der Zug keine Umwandlung ist. */
function promotionOf(san: string | undefined): string {
  const i = san?.indexOf('=') ?? -1;
  return i >= 0 && san!.length > i + 1 ? san![i + 1].toLowerCase() : '';
}

/**
 * Punktepartie (`/guess/:id`): Zug für Zug raten, sofort eine Rückmeldung samt Punkten, dann rückt
 * die Partie zwei Halbzüge weiter (eigener Zug + Antwort des Gegners).
 *
 * <p>Der Partiezug kommt erst mit der ANTWORT des Servers — vorher kennt der Client ihn nicht
 * (siehe `GuessSessionService`). Deshalb setzt das Brett den geratenen Zug auch nicht selbst um:
 * gezeigt wird, was der Server zurückmeldet.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-guess-board',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, TranslatePipe, ChessBoardComponent, LoadingSpinnerComponent, HelpHintComponent],
  template: `
    <div class="gb-container">
      @if (loading) {
        <app-loading-spinner />
      } @else if (!session) {
        <mat-card><mat-card-content>
          <p>{{ 'guess.notFound' | translate }}</p>
          <a mat-stroked-button routerLink="/guess">{{ 'guess.back' | translate }}</a>
        </mat-card-content></mat-card>
      } @else {
        <div class="header">
          <div>
            <h1>{{ session.title || ('guess.untitled' | translate) }}</h1>
            <p class="muted small">
              {{ (session.guessWhite ? 'guess.playingWhite' : 'guess.playingBlack') | translate }}
              · {{ 'guess.score' | translate:{ points: session.points, max: session.maxPoints } }}
              @if (session.movesPlayed > 0) {
                · {{ 'guess.hits' | translate:{ hits: session.gameMoveHits, moves: session.movesPlayed } }}
              }
            </p>
          </div>
          <a mat-stroked-button routerLink="/guess"><mat-icon>arrow_back</mat-icon> {{ 'guess.back' | translate }}</a>
        </div>

        <div class="body">
          <div class="board-col">
            <app-chess-board [fen]="viewFen" [lastMove]="viewLastMove" [flipped]="!session.guessWhite"
                             [boardTheme]="boardTheme" [pieceSet]="pieceSet"
                             [playable]="canGuess" (userMove)="onMove($event)" />

            @if (session.status === 'running' && !holding) {
              <div class="actions">
                <button mat-stroked-button (click)="skip()" [disabled]="busy || browsing">
                  <mat-icon>skip_next</mat-icon> {{ 'guess.skip' | translate }}
                </button>
                <span class="muted small">{{ 'guess.yourTurn' | translate:{ move: moveLabel } }}</span>
              </div>
            }
            <!-- Dein Zug steht auf dem Brett; DARUNTER, was die Partie gespielt hat. -->
            @if (holding && last) {
              <div class="held" [class]="'g-' + (last.grade || 'skipped')">
                <span class="hg">{{ 'guess.gameMoveWouldBe' | translate:{ move: last.gameMoveSan } }}</span>
                @if (evalDelta) {
                  <span class="hd">{{ 'guess.evalDelta' | translate:{ delta: evalDelta } }}</span>
                }
                <button mat-flat-button color="primary" (click)="continueGame()">
                  {{ 'guess.continue' | translate }} <mat-icon>arrow_forward</mat-icon>
                </button>
              </div>
            }
            @if (last) {
              <mat-card class="feedback" [class]="'g-' + (last.grade || 'skipped')">
                <mat-card-content>
                  <div class="fb-head">
                    <span class="pts">{{ last.points > 0 ? '+' : '' }}{{ last.points }}</span>
                    <span class="grade">{{ ('guess.grade.' + (last.grade || 'skipped')) | translate }}</span>
                  </div>
                  <p class="fb-line">
                    @if (last.playedSan) {
                      <span>{{ 'guess.youPlayed' | translate:{ move: last.playedSan } }}</span> ·
                    }
                    <span>{{ 'guess.gamePlayed' | translate:{ move: last.gameMoveSan } }}</span>
                    @if (last.replySan) { · <span>{{ 'guess.reply' | translate:{ move: last.replySan } }}</span> }
                  </p>
                  @if (last.evalText) {
                    <p class="muted small">{{ 'guess.evalAfter' | translate:{ eval: last.evalText } }}</p>
                  }
                </mat-card-content>
              </mat-card>
            }

            <!-- Die Partie bis hierhin (Eröffnung + gelöste Züge): blättern oder direkt anklicken. -->
            @if (session.history.length) {
              <div class="opening">
                <div class="onav">
                  <button mat-icon-button (click)="browse(-1)" [disabled]="atStart"
                          [attr.title]="'guess.startPosition' | translate"><mat-icon>first_page</mat-icon></button>
                  <button mat-icon-button (click)="step(-1)" [disabled]="atStart"
                          [attr.title]="'guess.prevMove' | translate"><mat-icon>chevron_left</mat-icon></button>
                  <button mat-icon-button (click)="step(1)" [disabled]="atTask"
                          [attr.title]="'guess.nextMove' | translate"><mat-icon>chevron_right</mat-icon></button>
                  <button mat-stroked-button (click)="browse(null)" [disabled]="atTask">
                    {{ 'guess.toTask' | translate }}
                  </button>
                </div>
                <div class="omoves">
                  @for (row of historyRows; track row.no) {
                    <div class="orow">
                      <span class="no">{{ row.no }}.</span>
                      @if (row.wIdx >= 0) {
                        <button type="button" class="mv" [class.on]="browseIndex === row.wIdx"
                                (click)="browse(row.wIdx)">{{ row.w }}</button>
                      } @else { <span class="mv muted">…</span> }
                      @if (row.b !== null) {
                        <button type="button" class="mv" [class.on]="browseIndex === row.bIdx"
                                (click)="browse(row.bIdx)">{{ row.b }}</button>
                      } @else { <span></span> }
                    </div>
                  }
                </div>
              </div>
            }
          </div>

          <div class="side-col">
            @if (session.status === 'done') {
              <mat-card class="done">
                <mat-card-content>
                  <h2>{{ 'guess.finished' | translate }}</h2>
                  <p class="final">{{ 'guess.score' | translate:{ points: session.points, max: session.maxPoints } }}</p>
                  <p class="muted small">{{ 'guess.hits' | translate:{ hits: session.gameMoveHits, moves: session.movesPlayed } }}</p>
                </mat-card-content>
              </mat-card>

              @if (review.length) {
                <mat-card>
                  <mat-card-content>
                    <h3>{{ 'guess.review' | translate }}</h3>
                    <div class="review">
                      @for (r of review; track r.ply) {
                        <div class="rev-row" [class]="'g-' + (r.grade || 'skipped')">
                          <span class="no">{{ r.moveNumber }}{{ r.white ? '.' : '…' }}</span>
                          <span class="game">{{ r.gameSan }}</span>
                          <span class="yours">{{ r.playedSan || '–' }}</span>
                          <span class="pts">{{ r.points > 0 ? '+' : '' }}{{ r.points }}</span>
                          <!-- Nur wo es etwas Besseres gab als den Partiezug. -->
                          @if (hasBetter(r)) {
                            <app-help-hint [text]="infoText(r)" icon="info_outline" />
                          } @else { <span></span> }
                        </div>
                      }
                    </div>
                  </mat-card-content>
                </mat-card>
              }
            } @else {
              <mat-progress-bar mode="determinate" [value]="progress"></mat-progress-bar>
              <p class="muted small">{{ 'guess.progress' | translate:{ done: session.movesPlayed, total: session.totalGuesses } }}</p>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .gb-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 12px; }
    .header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h1 { margin: 0; font-size: 1.4rem; }
    h2 { margin: 0 0 4px; font-size: 1.1rem; }
    h3 { margin: 0 0 8px; font-size: 1rem; }
    .body { display: flex; gap: 16px; align-items: flex-start; flex-wrap: wrap; margin-top: 12px; }
    .board-col { flex: 1 1 320px; max-width: 520px; }
    .side-col { flex: 1 1 280px; max-width: 440px; display: flex; flex-direction: column; gap: 10px; }
    .actions { display: flex; align-items: center; gap: 10px; margin-top: 8px; flex-wrap: wrap; }
    .held { display: flex; align-items: center; gap: 10px; margin-top: 8px; flex-wrap: wrap; }
    /* Eröffnung: Navigation oben, darunter die Züge UNTEREINANDER (Nr. · Weiß · Schwarz). */
    .opening { margin-top: 8px; }
    .onav { display: flex; align-items: center; gap: 2px; flex-wrap: wrap; }
    .onav button[mat-stroked-button] { margin-left: 8px; }
    /* Schmal halten: eine Zugliste, die sich ueber die ganze Brettbreite zieht, liest sich schlecht. */
    .omoves { margin-top: 4px; max-width: 260px; max-height: 30vh; overflow-y: auto; }
    .orow { display: grid; grid-template-columns: 34px 1fr 1fr; gap: 4px; align-items: baseline;
            padding: 1px 2px; }
    .orow .no { color: color-mix(in srgb, currentColor 55%, transparent); font-size: .8rem;
                text-align: right; font-variant-numeric: tabular-nums; }
    .orow .mv { text-align: left; background: none; border: 0; color: inherit; font: inherit;
                padding: 1px 6px; border-radius: 3px; cursor: pointer; }
    .orow .mv:hover { background: color-mix(in srgb, currentColor 12%, transparent); }
    .orow .mv.on { background: color-mix(in srgb, currentColor 22%, transparent); font-weight: 600; }
    .held .hg { font-weight: 600; }
    .held .hd { font-variant-numeric: tabular-nums; }
    .held.g-worse .hd, .held.g-muchWorse .hd { color: #c62828; }
    .held.g-clearlyBetter .hd { color: #2e7d32; }
    .feedback .fb-head { display: flex; align-items: baseline; gap: 10px; }
    .feedback .pts { font-size: 1.6rem; font-weight: 700; font-variant-numeric: tabular-nums; }
    .feedback .grade { font-weight: 600; }
    .fb-line { margin: 6px 0 0; }
    .final { font-size: 1.4rem; font-weight: 700; margin: 0; }
    /* Farbe nach Güte — dieselbe Ordnung wie die Punkte. */
    .g-clearlyBetter .pts, .g-better .pts, .g-onlyMove .pts, .g-gameMove .pts { color: #2e7d32; }
    .g-similar .pts { color: #558b2f; }
    .g-worse .pts, .g-skipped .pts { color: color-mix(in srgb, currentColor 60%, transparent); }
    .g-muchWorse .pts { color: #c62828; }
    /* Der Rückblick kann lang sein — er scrollt IN SICH, nicht die Seite. */
    .review { display: flex; flex-direction: column; gap: 2px; max-height: 50vh; overflow-y: auto; }
    .rev-row { display: grid; grid-template-columns: 44px 1fr 1fr 44px 22px; gap: 8px; align-items: baseline;
               padding: 2px 4px; border-radius: 3px; }
    .rev-row .no { color: color-mix(in srgb, currentColor 55%, transparent); font-size: .8rem; }
    .rev-row .game { font-weight: 600; }
    .rev-row .yours { color: color-mix(in srgb, currentColor 75%, transparent); }
    .rev-row .pts { text-align: right; font-variant-numeric: tabular-nums; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class GuessBoardComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private service = inject(GuessService);
  private prefs = inject(PreferencesService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  session: GuessSession | null = null;
  last: GuessResult | null = null;
  review: GuessReviewMove[] = [];
  loading = true;
  busy = false;

  /**
   * Ein Zug, der NICHT der Partiezug war, bleibt auf dem Brett stehen — mit der Info darunter,
   * was die Partie gespielt hat. Erst „Weiter" rückt auf die nächste Aufgabe vor. Die Sitzung ist
   * serverseitig längst weitergerückt; zurückgehalten wird nur die ANZEIGE (`pending`).
   */
  holding = false;
  private pending: GuessSession | null = null;

  /**
   * Welcher Zug der Eröffnung gerade angeschaut wird: `null` = die Aufgabe selbst, `-1` = die
   * Stellung vor dem ersten Zug, sonst der Index in `session.history`. Nur eine ANSICHT — der
   * Zustand der Sitzung bleibt davon unberührt, das Brett ist derweil gesperrt.
   */
  browseIndex: number | null = null;

  /** Was das Brett zeigt: die zu ratende Stellung bzw. nach einem Zug die Stellung danach. */
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  lastMove?: [string, string];

  private since = Date.now();

  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }
  get canGuess(): boolean {
    return !!this.session?.position && !this.busy && !this.holding && !this.browsing
      && this.session.status === 'running';
  }

  /**
   * „Schaut gerade zurueck" — und damit gesperrt. Der LETZTE Eintrag des Verlaufs erzeugt IMMER die
   * Aufgabenstellung (der Server schneidet die Liste genau davor ab), dort darf also gezogen werden.
   */
  get browsing(): boolean {
    const i = this.browseIndex;
    return i !== null && i !== (this.session?.history.length ?? 0) - 1;
  }

  /** Steht das Brett auf der Aufgabe (dann fuehrt „vor" nirgendwohin)? */
  get atTask(): boolean { return !this.browsing; }
  get atStart(): boolean { return this.browseIndex === -1; }

  /** Der Verlauf als Zeilen „Nr. · Weiss · Schwarz" — je Halbzug der Index fuers Anklicken. */
  get historyRows(): { no: number; w: string; wIdx: number; b: string | null; bIdx: number }[] {
    const rows: { no: number; w: string; wIdx: number; b: string | null; bIdx: number }[] = [];
    const history = this.session?.history ?? [];
    for (let i = 0; i < history.length; i++) {
      const m = history[i];
      if (m.white) rows.push({ no: m.moveNumber, w: m.san, wIdx: i, b: null, bIdx: -1 });
      else if (rows.length && rows[rows.length - 1].b === null) {
        rows[rows.length - 1].b = m.san;
        rows[rows.length - 1].bIdx = i;
      } else rows.push({ no: m.moveNumber, w: '…', wIdx: -1, b: m.san, bIdx: i });
    }
    return rows;
  }

  /**
   * Einen Halbzug vor (+1) oder zurueck (-1). Das ENDE ist der letzte Eintrag des Verlaufs, also die
   * Aufgabenstellung — weiter geht es nicht, und genau deshalb kann das Blaettern die Loesung nicht
   * verraten.
   */
  step(delta: number): void {
    const n = this.session?.history.length ?? 0;
    const cur = this.browseIndex ?? n - 1;
    const next = cur + delta;
    if (next >= n - 1) { this.browse(null); return; }
    this.browse(Math.max(-1, next));
  }

  /** Was das Brett zeigt: die Aufgabe (bzw. der gehaltene Zug) — oder die angeklickte Eröffnungsstellung. */
  get viewFen(): string {
    if (this.browseIndex === null) return this.boardFen;
    if (this.browseIndex < 0) return this.session?.startFen || this.boardFen;
    return this.session?.history[this.browseIndex]?.fen || this.boardFen;
  }

  /**
   * Der hervorgehobene Zug — als STABILES Tupel gemerkt. Ein Getter, der beim Blaettern jedes Mal
   * ein neues `[from, to]`-Literal liefert, laesst unter Default-Change-Detection in JEDEM
   * Durchlauf `ngOnChanges` des Bretts feuern; chessground zeichnet dann jedes Mal komplett neu.
   * Dieselbe Falle wie beim Kartenmittelpunkt im Turnierkalender.
   */
  private lastMoveCache?: [string, string];
  private lastMoveKey = '';

  get viewLastMove(): [string, string] | undefined {
    if (this.browseIndex === null) return this.lastMove;
    if (this.browseIndex < 0) return undefined;                 // Grundstellung: es gab keinen Zug
    const uci = this.session?.history[this.browseIndex]?.uci;
    const key = uci ?? '';
    if (key !== this.lastMoveKey) {
      this.lastMoveKey = key;
      this.lastMoveCache = uci ? [uci.slice(0, 2), uci.slice(2, 4)] : undefined;
    }
    return this.lastMoveCache;
  }

  /** Einen Eröffnungszug anschauen (`null` = zurück zur Aufgabe, `-1` = Grundstellung). */
  browse(index: number | null): void {
    this.browseIndex = index;
    this.cdr.markForCheck();
  }

  /**
   * Wann der Abstand zum Partiezug eine Zahl wert ist: wenn der eigene Zug schlechter war (dann will
   * man wissen, wie teuer) — und wenn er DEUTLICH besser war (dann will man wissen, wie viel man
   * gefunden hat). Dazwischen (gleichwertig, knapp besser: unter 0,2 Bauern Unterschied) sagt die
   * Zahl nichts, was die Stufe nicht schon sagt, und der Partiezug allein genügt.
   */
  get showsDelta(): boolean {
    const g = this.last?.grade;
    return g === 'worse' || g === 'muchWorse' || g === 'clearlyBetter';
  }

  /** Bewertungsänderung gegenüber dem Partiezug in Bauerneinheiten (siehe <c>showsDelta</c>). */
  get evalDelta(): string | null {
    const cp = this.last?.diffCp;
    if (cp === null || cp === undefined || !this.showsDelta) return null;
    const pawns = cp / 100;
    return (pawns > 0 ? '+' : '') + pawns.toFixed(2);
  }

  get moveLabel(): string {
    const p = this.session?.position;
    return p ? `${p.moveNumber}${p.whiteToMove ? '.' : '…'}` : '';
  }

  get progress(): number {
    const s = this.session;
    return s && s.totalGuesses > 0 ? Math.round((100 * s.movesPlayed) / s.totalGuesses) : 0;
  }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.service.get(id).subscribe({
      next: s => {
        this.apply(s);
        // Eine NEUE Sitzung faengt am Anfang der Partie an, damit man sich die Eroeffnung ansehen
        // kann; am letzten Eintrag des Verlaufs steht die Aufgabe, ab dort darf geraten werden.
        // Eine fortgesetzte Sitzung startet dagegen dort, wo man aufgehoert hat.
        if (s.history.length && s.movesPlayed === 0) this.browseIndex = -1;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => { this.loading = false; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void { /* nichts zu lösen — die Zeit wird je Zug gemeldet */ }

  onMove(m: UserBoardMove): void {
    if (!this.canGuess) return;
    // Das Brett meldet nur Ausgangs- und Zielfeld; die Umwandlungsfigur steht ausschliesslich im SAN
    // (`applyUserMove` wandelt immer in eine Dame um). Ohne sie ginge „e7e8" statt „e7e8q" zum
    // Server — dort kein legaler Zug, und JEDE Umwandlung waere mit 400 abgeprallt.
    this.send(m.from + m.to + promotionOf(m.san));
  }

  skip(): void {
    if (!this.canGuess) return;
    this.send(null);
  }

  private send(uci: string | null): void {
    const id = this.session?.id;
    if (!id) return;
    this.busy = true;
    const seconds = Math.min(3600, Math.max(0, Math.round((Date.now() - this.since) / 1000)));
    const fenBefore = this.boardFen;
    const lastBefore = this.lastMove;

    // Den eigenen Zug SOFORT uebernehmen, nicht erst mit der Antwort. Sonst passiert Folgendes:
    // `busy` sperrt das Brett, das loest ein `ngOnChanges` mit der ALTEN Stellung aus, und
    // Chessground animiert die gerade gezogene Figur zurueck — 25 ms spaeter kommt die Antwort und
    // schiebt sie wieder hin. Genau das sichtbare Zucken beim Ziehen.
    if (uci) {
      const optimistic = fenAfterUci(fenBefore, uci);
      if (optimistic) {
        this.boardFen = optimistic;
        this.lastMove = [uci.slice(0, 2), uci.slice(2, 4)];
      }
    }

    this.service.guess(id, uci, seconds).subscribe({
      next: res => {
        this.busy = false;
        this.last = res;
        const mine = uci ? fenAfterUci(fenBefore, uci) : null;
        if (res.session.status !== 'running') {
          // Sitzung durch: es gibt keine naechste Aufgabe, `apply` wuerde das Brett also gar nicht
          // anfassen — die geschlagene Figur spraenge zurueck und das Brett zeigte die Stellung VOR
          // dem Schlusszug. Deshalb hier den letzten Zug selbst aufs Brett legen (beim Passen den
          // Partiezug, denn der ist ja gespielt worden).
          const shown = mine ?? fenAfterUci(fenBefore, res.gameMoveUci);
          const shownUci = uci ?? res.gameMoveUci;
          this.session = res.session;
          this.holding = false;
          this.pending = null;
          this.browseIndex = null;
          if (shown) {
            this.boardFen = shown;
            this.lastMove = [shownUci.slice(0, 2), shownUci.slice(2, 4)];
          }
          this.loadReview(id);
        } else if (mine && uci !== res.gameMoveUci && res.session.position) {
          // Ein anderer Zug als der Partiezug bleibt stehen, damit man sieht, was man gespielt hat —
          // die naechste Aufgabe wartet hinter „Weiter".
          this.session = res.session;              // Punkte/Fortschritt sofort mitnehmen
          this.pending = res.session;
          this.holding = true;
          this.browseIndex = null;   // der eigene Zug soll zu sehen sein, nicht die Eroeffnung
          this.boardFen = mine;
          this.lastMove = [uci!.slice(0, 2), uci!.slice(2, 4)];
        } else {
          // Partiezug getroffen oder gepasst: sofort auf die naechste Aufgabe.
          this.apply(res.session);
        }
        this.cdr.markForCheck();
      },
      error: err => {
        this.busy = false;
        // Der Zug wurde nicht gewertet — also auch nicht stehen lassen, sonst stuende das Brett
        // auf einer Stellung, die es fuer den Server nie gab.
        this.boardFen = fenBefore;
        this.lastMove = lastBefore;
        // Der Nutzer hat gerade gezogen — ein stiller Fehlschlag wäre hier das Schlimmste.
        this.snackbar.warn(err?.error?.message || this.translate.instant('guess.moveFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  /** „Weiter": die zurueckgehaltene naechste Aufgabe aufs Brett holen. */
  continueGame(): void {
    const s = this.pending;
    this.pending = null;
    this.holding = false;
    if (s) this.apply(s);          // setzt Brett + Denkzeit-Start; die Lesezeit zaehlt nicht mit
    this.cdr.markForCheck();
  }

  private apply(s: GuessSession): void {
    this.session = s;
    this.since = Date.now();
    this.browseIndex = null;        // neue Aufgabe → Brett zurueck auf die Aufgabe
    if (s.position) {
      this.boardFen = s.position.fen;
      this.lastMove = s.position.lastMoveUci
        ? [s.position.lastMoveUci.slice(0, 2), s.position.lastMoveUci.slice(2, 4)]
        : undefined;
    }
  }

  /** Gab es in dieser Stellung einen besseren Zug als den der Partie? Nur dann lohnt das ⓘ. */
  hasBetter(r: GuessReviewMove): boolean {
    return !!r.bestSan && r.bestSan !== r.gameSan;
  }

  /**
   * Der Text hinter dem ⓘ. Den PARTIEZUG nur, wenn etwas anderes gespielt wurde — sonst steht er
   * schon in der Zeile und die Wiederholung sagt nichts.
   */
  infoText(r: GuessReviewMove): string {
    const lines: string[] = [];
    if (r.playedSan && r.playedSan !== r.gameSan) {
      lines.push(this.translate.instant('guess.info.gameMove',
        { move: r.gameSan, eval: r.gameEval || '?' }));
    }
    lines.push(this.translate.instant('guess.info.bestMove',
      { move: r.bestSan, eval: r.bestEval || '?' }));
    return lines.join('\n\n');
  }

  private loadReview(id: number): void {
    this.service.review(id).subscribe({
      next: rows => { this.review = rows; this.cdr.markForCheck(); },
      error: () => { /* der Rückblick ist Zugabe — die Sitzung ist bereits gewertet */ },
    });
  }
}
