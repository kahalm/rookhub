import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';

import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { calcDests } from '../puzzles/puzzle-move.util';
import { StockfishService } from '../puzzles/stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireTrainingService, RepertoireCardStateDto, ReviewCardRequest } from './repertoire-training.service';
import { buildRepertoireGraph, cardsForColor, normSan, RepCard } from './repertoire-tree.util';

type Phase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE';
type Outcome = 'correct' | 'tolerated' | 'wrong';

const NEW_LIMIT = 20;
const COLOR_KEY = (id: number) => `rookhub_rep_train_color_${id}`;
// Auto-Weiter nach richtigem/geduldetem Zug (kein „Weiter"-Knopf). Geduldet länger,
// damit der gespielte (zurückzunehmende) Zug lesbar auf dem Brett stehen bleibt;
// antippen springt sofort weiter.
const ADVANCE_MS: Record<Outcome, number> = { correct: 700, tolerated: 1800, wrong: 0 };
// Falscher Zug: bleibt diese Zeitspanne sichtbar auf dem Brett stehen, bevor er
// zurückgenommen wird (Buttons „Mausrutscher"/„Lösung zeigen" bleiben sichtbar).
const WRONG_HOLD_MS = 1000;

@Component({
  selector: 'app-repertoire-trainer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, RouterLink, MatCardModule, MatButtonModule, MatButtonToggleModule,
    MatIconModule, MatProgressBarModule, TranslateModule, PuzzleBoardComponent,
  ],
  template: `
<div class="trainer">
  <div class="bar">
    <a mat-button [routerLink]="['/repertoires', repertoireId]"><mat-icon>arrow_back</mat-icon> {{ 'common.back' | translate }}</a>
    <span class="title">{{ 'repertoireTrainer.title' | translate }}</span>
    <mat-button-toggle-group [value]="color" (change)="setColor($event.value)" hideSingleSelectionIndicator="true" aria-label="Color">
      <mat-button-toggle value="w">{{ 'repertoireTrainer.white' | translate }}</mat-button-toggle>
      <mat-button-toggle value="b">{{ 'repertoireTrainer.black' | translate }}</mat-button-toggle>
    </mat-button-toggle-group>
  </div>

  <ng-container [ngSwitch]="phase">
    <div *ngSwitchCase="'LOADING'" class="center">{{ 'common.loading' | translate }}</div>

    <mat-card *ngSwitchCase="'EMPTY'" class="msg">
      <mat-icon>school</mat-icon>
      <p>{{ 'repertoireTrainer.noCards' | translate }}</p>
    </mat-card>

    <mat-card *ngSwitchCase="'DONE'" class="msg">
      <mat-icon>celebration</mat-icon>
      <p>{{ 'repertoireTrainer.done' | translate: { correct: correct, total: sessionTotal } }}</p>
      <button mat-raised-button color="primary" (click)="restart()">{{ 'repertoireTrainer.again' | translate }}</button>
    </mat-card>

    <div *ngSwitchDefault class="play" (click)="onPlayClick()">
      <app-puzzle-board
        [fen]="fen" [orientation]="color === 'w' ? 'white' : 'black'"
        [turnColor]="color === 'w' ? 'white' : 'black'"
        [dests]="dests" [lastMove]="lastMove" [viewOnly]="phase === 'FEEDBACK' && !(outcome === 'wrong' && !wrongRevealed)"
        [boardTheme]="prefs.boardTheme" [pieceSet]="prefs.pieceSet"
        (moveMade)="onMove($event)">
      </app-puzzle-board>

      <div class="side">
        <mat-progress-bar mode="determinate" [value]="progressPct"></mat-progress-bar>
        <div class="counts">
          <span>{{ 'repertoireTrainer.remaining' | translate }}: {{ queue.length - index }}</span>
          <span class="ok">✓ {{ correct }}</span>
          <span class="bad">✗ {{ wrong }}</span>
        </div>
        <p class="prompt">{{ (color === 'w' ? 'repertoireTrainer.whiteToMove' : 'repertoireTrainer.blackToMove') | translate }}</p>

        <div *ngIf="phase === 'FEEDBACK'" class="feedback" [ngClass]="outcome">
          <p *ngIf="outcome === 'correct'"><mat-icon>check_circle</mat-icon> {{ 'repertoireTrainer.correct' | translate }}</p>
          <!-- Geduldet: keinen richtigen Zug verraten, nur „spielbarer Zug" anzeigen. -->
          <p *ngIf="outcome === 'tolerated'"><mat-icon>info</mat-icon> {{ 'repertoireTrainer.toleratedPlayable' | translate }}</p>
          <ng-container *ngIf="outcome === 'wrong'">
            <p *ngIf="!wrongRevealed"><mat-icon>cancel</mat-icon> {{ 'repertoireTrainer.wrongNoHint' | translate }}</p>
            <p *ngIf="wrongRevealed"><mat-icon>cancel</mat-icon>
              {{ 'repertoireTrainer.wrong' | translate: { move: expectedDisplay } }}</p>
            <p class="eval-info" *ngIf="evalLoading"><mat-icon>hourglass_top</mat-icon> {{ 'repertoireTrainer.evalLoading' | translate }}</p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === 'missed'"><mat-icon>flash_on</mat-icon> {{ 'repertoireTrainer.evalMateMissed' | translate }}</p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === 'allowed'"><mat-icon>flash_on</mat-icon> {{ 'repertoireTrainer.evalMateAllowed' | translate }}</p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === null && evalDeltaPawns !== null && evalDeltaPawns < -0.05">
              <mat-icon>trending_down</mat-icon>
              {{ 'repertoireTrainer.evalWorse' | translate: { delta: evalDeltaAbsDisplay } }}
            </p>
            <p class="eval-info" *ngIf="!evalLoading && evalMateNote === null && evalDeltaPawns !== null && evalDeltaPawns >= -0.05 && evalDeltaPawns <= 0.05">
              <mat-icon>drag_handle</mat-icon> {{ 'repertoireTrainer.evalEqual' | translate }}
            </p>
            <div class="wrong-actions" *ngIf="!wrongRevealed">
              <button mat-raised-button color="primary" (click)="showSolution(); $event.stopPropagation()">
                <mat-icon>visibility</mat-icon> {{ 'repertoireTrainer.showSolution' | translate }}
              </button>
            </div>
            <button *ngIf="wrongRevealed" mat-raised-button color="primary" (click)="next(); $event.stopPropagation()">{{ 'repertoireTrainer.continue' | translate }}</button>
          </ng-container>
          <p *ngIf="outcome !== 'wrong'" class="tap-hint">{{ 'repertoireTrainer.tapToContinue' | translate }}</p>
        </div>
        <p *ngIf="phase === 'PLAYING'" class="hint">{{ 'repertoireTrainer.playYourMove' | translate }}</p>
      </div>
    </div>
  </ng-container>
</div>
  `,
  styles: [`
    .trainer { max-width: 920px; margin: 0 auto; padding: 8px; }
    .bar { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
    .bar .title { font-weight: 600; flex: 1; }
    .center, .msg { text-align: center; padding: 32px; }
    .msg { display: flex; flex-direction: column; align-items: center; gap: 12px; }
    .msg mat-icon { font-size: 40px; height: 40px; width: 40px; }
    .play { display: flex; gap: 20px; flex-wrap: wrap; align-items: flex-start; }
    app-puzzle-board { flex: 1 1 360px; max-width: 480px; }
    .side { flex: 1 1 240px; min-width: 220px; }
    .counts { display: flex; gap: 14px; margin: 10px 0; }
    .counts .ok { color: #2e7d32; } .counts .bad { color: #c62828; }
    .prompt { font-weight: 600; }
    .feedback { padding: 10px; border-radius: 8px; }
    .feedback p { display: flex; align-items: center; gap: 8px; margin: 0 0 12px; }
    .feedback.correct { background: rgba(46,125,50,.12); }
    .feedback.tolerated { background: rgba(255,160,0,.15); }
    .feedback.wrong { background: rgba(198,40,40,.12); }
    .tap-hint { font-size: 12px; opacity: .7; margin: 0; }
    .hint { color: var(--mdc-theme-text-secondary-on-background, #666); }
    .wrong-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 8px; }
    .eval-info { font-size: 13px; opacity: .85; margin: 0 0 8px; }
  `],
})
export class RepertoireTrainerComponent implements OnInit, OnDestroy {
  repertoireId = 0;
  phase: Phase = 'LOADING';
  color: 'w' | 'b' = 'w';

  fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  dests = new Map<Key, Key[]>();
  lastMove?: [Key, Key];

  private allCards: RepCard[] = [];
  queue: RepCard[] = [];
  index = 0;
  sessionTotal = 0;
  correct = 0;
  wrong = 0;

  outcome: Outcome = 'correct';
  expectedDisplay = '';
  /** Bei falschem Zug erst nach Klick auf „Lösung zeigen" wahr — bis dahin keinen Hint preisgeben. */
  wrongRevealed = false;
  /** Stockfish-Eval-Vergleich (Repertoire-Zug vs. eigener Zug) während FEEDBACK. */
  evalLoading = false;
  /** Differenz aus Spieler-Sicht in „Bauern" (negativ = eigener Zug schlechter). */
  evalDeltaPawns: number | null = null;
  /** Mate-Hinweis: 'missed' = Spieler-Mate verpasst, 'allowed' = Gegen-Mate erlaubt, null = keine Mate-Linie. */
  evalMateNote: 'missed' | 'allowed' | null = null;
  /** Karte+Zug, deren Eval gerade berechnet werden — alte Antworten ignorieren bei Karten-/Zugwechsel. */
  private evalEpoch = 0;
  /** Letzter falscher Zug (wird für Mouseslip auf Rückgängig gebraucht). */
  private wrongMove: { orig: Key; dest: Key; promotion?: string } | null = null;
  /** Bei falschem Zug gemerkter Server-Review-Trigger (wird erst bei „Lösung zeigen" gefeuert). */
  private pendingWrongReview: (() => void) | null = null;
  private statesByKey = new Map<string, RepertoireCardStateDto>();
  private advanceTimer: ReturnType<typeof setTimeout> | null = null;
  /** Timer, der einen falschen Zug nach kurzer Sichtbarkeit zurücknimmt (Buttons bleiben). */
  private wrongRevertTimer: ReturnType<typeof setTimeout> | null = null;
  /** Ausgangsstellung der aktuellen Karte (für das Zurücknehmen nach dem Sicht-Halt). */
  private startFen = '';

  constructor(
    private route: ActivatedRoute,
    private training: RepertoireTrainingService,
    public prefs: PreferencesService,
    private translate: TranslateService,
    private cdr: ChangeDetectorRef,
    private stockfish: StockfishService,
  ) {}

  ngOnInit(): void {
    this.repertoireId = Number(this.route.snapshot.paramMap.get('id')) || 0;
    const saved = localStorage.getItem(COLOR_KEY(this.repertoireId));
    if (saved === 'w' || saved === 'b') this.color = saved;
    // Stockfish warmup im Hintergrund — beim ersten falschen Zug ist der Worker dann schon bereit.
    this.stockfish.init().catch(() => {});

    forkJoin({
      pgn: this.training.getPgn(this.repertoireId),
      states: this.training.getCards(this.repertoireId),
    }).subscribe({
      next: ({ pgn, states }) => {
        const graph = buildRepertoireGraph(pgn);
        if (!saved) this.color = graph.guessedColor;
        this.allCards = cardsForColor(graph, this.color);
        // Karten für die ANDERE Farbe vorbauen wir bei Bedarf (setColor rebuildet aus PGN).
        this.graphCache = graph;
        this.statesByKey = new Map(states.map(s => [s.cardKey, s]));
        this.buildQueue();
      },
      error: () => { this.phase = 'EMPTY'; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void { this.clearAdvance(); }

  private graphCache: ReturnType<typeof buildRepertoireGraph> | null = null;

  setColor(c: 'w' | 'b'): void {
    if (c === this.color || !this.graphCache) return;
    this.clearAdvance();
    this.color = c;
    localStorage.setItem(COLOR_KEY(this.repertoireId), c);
    this.allCards = cardsForColor(this.graphCache, c);
    this.buildQueue();
  }

  private buildQueue(): void {
    this.clearAdvance();
    const now = Date.now();
    const due: RepCard[] = [];
    const fresh: RepCard[] = [];
    for (const card of this.allCards) {
      const st = this.statesByKey.get(card.cardKey);
      if (!st) fresh.push(card);
      else if (new Date(st.dueAt).getTime() <= now) due.push(card);
    }
    this.queue = [...due, ...fresh.slice(0, NEW_LIMIT)];
    this.index = 0;
    this.correct = 0;
    this.wrong = 0;
    this.sessionTotal = this.queue.length;
    if (this.queue.length === 0) { this.phase = 'EMPTY'; this.cdr.markForCheck(); return; }
    this.showCurrent();
  }

  restart(): void { this.buildQueue(); }

  get progressPct(): number {
    return this.sessionTotal === 0 ? 0 : Math.round((this.index / this.sessionTotal) * 100);
  }

  /** Formatierter absoluter Eval-Verlust (z.B. "1.4") für die Lokalisierung. */
  get evalDeltaAbsDisplay(): string {
    if (this.evalDeltaPawns === null) return '';
    return Math.abs(this.evalDeltaPawns).toFixed(2).replace(/\.?0+$/, '');
  }

  private get current(): RepCard | null {
    return this.index < this.queue.length ? this.queue[this.index] : null;
  }

  private showCurrent(): void {
    const card = this.current;
    if (!card) { this.phase = 'DONE'; this.cdr.markForCheck(); return; }
    this.fen = card.fenBefore + ' 0 1';   // normFen → volle FEN für chess.js/Brett
    this.startFen = this.fen;
    this.lastMove = undefined;
    this.wrongRevealed = false;
    this.wrongMove = null;
    this.pendingWrongReview = null;
    this.evalLoading = false;
    this.evalDeltaPawns = null;
    this.evalMateNote = null;
    this.evalEpoch++;
    try {
      const c = new Chess(this.fen);
      this.dests = calcDests(c);
    } catch { this.dests = new Map(); }
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const card = this.current;
    if (!card) return;
    // Nach einem falschen Zug (noch nicht „Lösung zeigen") bleibt das Brett spielbar → der Spieler
    // kann SOFORT erneut ziehen, ohne vorher „Mausrutscher" klicken zu müssen. Dann den alten
    // wrong-Zustand verwerfen und den neuen Zug als frischen Versuch derselben Karte werten.
    const wrongRetry = this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
    if (this.phase !== 'PLAYING' && !wrongRetry) return;
    if (wrongRetry) {
      this.clearWrongRevert();
      this.pendingWrongReview = null;
      this.wrongMove = null;
      this.evalLoading = false; this.evalDeltaPawns = null; this.evalMateNote = null; this.evalEpoch++;
    }

    this.startFen = this.fen;   // Ausgangsstellung der Karte (für späteres Zurücknehmen)
    let san = '';
    let fenAfterPlayerMove = '';
    try {
      const c = new Chess(this.fen);
      const mv = c.move({ from: ev.orig, to: ev.dest, promotion: (ev.promotion as any) || 'q' });
      san = normSan(mv.san);
      fenAfterPlayerMove = c.fen();
      this.lastMove = [ev.orig, ev.dest];
    } catch { return; }   // illegaler Zug ignorieren

    const expected = normSan(card.expected);
    const accepted = card.accepted.map(normSan);
    let grade: ReviewCardRequest['grade'];
    if (san === expected) {
      this.outcome = 'correct'; this.correct++; grade = 2;
      // Richtigen Zug auf dem Brett STEHEN lassen (sonst wird er kurz zurückgenommen und
      // beim nächsten Karten-Schritt erneut gespielt → Flackern).
      this.fen = fenAfterPlayerMove;
    } else if (accepted.includes(san)) {
      this.outcome = 'tolerated'; grade = 1;
      // Spielbarer (aber nicht der Hauptzug): Zug erst sichtbar stehen lassen (this.fen =
      // Stellung NACH dem Zug, Markierung bleibt), erst nach ADVANCE_MS in retryCurrentCard
      // zurücknehmen → kein sofortiges Zurückspringen.
      this.fen = fenAfterPlayerMove;
    } else {
      this.outcome = 'wrong'; grade = 0;   // wrong-Counter erst beim „Lösung zeigen"
      // Falschen Zug SOFORT zurücknehmen (Brett zurück auf die Ausgangsstellung), damit der Spieler
      // ohne Umweg direkt erneut ziehen kann; der Versuch bleibt als lastMove-Markierung sichtbar.
      this.fen = this.startFen;
      this.lastMove = [ev.orig, ev.dest];
    }

    this.expectedDisplay = card.expected;
    this.wrongRevealed = false;
    this.phase = 'FEEDBACK';

    if (this.outcome === 'wrong') {
      // Falsche Karte: noch KEIN Review senden, noch KEIN Re-Queue — erst wenn der Spieler
      // „Lösung zeigen" wählt. Ein sofortiger neuer Zug (Retry) zählt nicht als Fehler.
      this.wrongMove = { orig: ev.orig, dest: ev.dest, promotion: ev.promotion };
      this.pendingWrongReview = () =>
        this.training.review(this.repertoireId, { cardKey: card.cardKey, expectedMove: card.expected, grade: 0 })
          .subscribe({ next: st => this.statesByKey.set(st.cardKey, st), error: () => {} });
      this.kickOffEvalCompare(card, fenAfterPlayerMove);
      // Brett bleibt spielbar (Retry ohne „Mausrutscher"): Zugmöglichkeiten aus der Ausgangsstellung.
      try { this.dests = calcDests(new Chess(this.startFen)); } catch { this.dests = new Map(); }
    } else {
      this.training.review(this.repertoireId, { cardKey: card.cardKey, expectedMove: card.expected, grade })
        .subscribe({ next: st => this.statesByKey.set(st.cardKey, st), error: () => {} });
      // Richtig/geduldet: kein „Weiter"-Knopf — automatisch weiterspielen (Tippen springt sofort).
      this.scheduleAdvance(ADVANCE_MS[this.outcome]);
    }

    this.cdr.markForCheck();
  }

  /** Klick irgendwo im Spielbereich überspringt die Auto-Weiter-Wartezeit (richtig/geduldet).
   *  Im wrong-Zustand passiert hier nichts — der Spieler entscheidet bewusst Mouseslip oder Show. */
  onPlayClick(): void {
    if (this.phase === 'FEEDBACK' && this.outcome !== 'wrong' && this.advanceTimer !== null) this.runAdvance();
  }

  /** „Mouseslip": kein Penalty, kein Server-Review, kein Re-Queue. Zurück zur Karte. */
  mouseslip(): void {
    this.clearWrongRevert();
    this.pendingWrongReview = null;
    this.wrongMove = null;
    this.wrongRevealed = false;
    this.evalLoading = false;
    this.evalDeltaPawns = null;
    this.evalMateNote = null;
    this.evalEpoch++;   // laufende Eval-Antworten verwerfen
    this.outcome = 'correct';   // neutraler Zustand für CSS-Klasse
    this.fen = this.startFen;   // ggf. noch sichtbaren falschen Zug zurücknehmen
    this.lastMove = undefined;
    try { this.dests = calcDests(new Chess(this.fen)); } catch { this.dests = new Map(); }
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  /** „Lösung zeigen": wertet die Karte jetzt als falsch (Server-Review + Re-Queue), SPIELT den
   *  richtigen Zug auf dem Brett (sichtbar + markiert) und enthüllt ihn im Text. */
  showSolution(): void {
    if (this.wrongRevealed) return;
    this.clearWrongRevert();
    this.wrongRevealed = true;
    this.wrong++;
    const card = this.current;
    if (card) {
      // Erwarteten Zug ab der Ausgangsstellung spielen → Brett zeigt den korrekten Zug + Markierung.
      try {
        const c = new Chess(this.startFen);
        const mv = c.move(card.expected);
        if (mv) {
          this.fen = c.fen();
          this.lastMove = [mv.from as Key, mv.to as Key];
          this.dests = new Map();   // Brett gesperrt bis „Weiter"
        }
      } catch { /* SAN nicht spielbar → nur Text-Reveal */ }
      this.queue.push(card);
    }
    this.pendingWrongReview?.();
    this.pendingWrongReview = null;
    this.cdr.markForCheck();
  }

  /** Startet zwei Stockfish-Evals (Spielerzug vs. Repertoire-Zug) und berechnet die Differenz
   *  aus Spieler-Sicht. Läuft im Hintergrund; FEEDBACK-Anzeige aktualisiert sich, sobald fertig. */
  private kickOffEvalCompare(card: RepCard, fenAfterPlayerMove: string): void {
    const epoch = ++this.evalEpoch;
    this.evalLoading = true;
    this.evalDeltaPawns = null;
    this.evalMateNote = null;

    let fenAfterRep = '';
    try {
      const c = new Chess(this.fen);
      c.move(card.expected);
      fenAfterRep = c.fen();
    } catch { this.evalLoading = false; this.cdr.markForCheck(); return; }

    const parseCpWhite = (s: string): { cp: number; mateFor: 'w' | 'b' | null } => {
      if (!s) return { cp: 0, mateFor: null };
      if (s.startsWith('#-')) return { cp: -100000 + parseInt(s.slice(2), 10), mateFor: 'b' };
      if (s.startsWith('#'))  return { cp:  100000 - parseInt(s.slice(1), 10), mateFor: 'w' };
      const v = parseFloat(s);
      return { cp: isNaN(v) ? 0 : Math.round(v * 100), mateFor: null };
    };

    Promise.all([
      this.stockfish.getEval(fenAfterPlayerMove, 14).catch(() => ''),
      this.stockfish.getEval(fenAfterRep, 14).catch(() => ''),
    ]).then(([sPlayer, sRep]) => {
      if (epoch !== this.evalEpoch) return;
      this.evalLoading = false;
      if (!sPlayer || !sRep) { this.cdr.markForCheck(); return; }

      const ep = parseCpWhite(sPlayer);
      const er = parseCpWhite(sRep);
      const sign = this.color === 'w' ? 1 : -1;
      const cpPlayer = ep.cp * sign;
      const cpRep    = er.cp * sign;

      // Mate-Sonderfälle: gewinnbares Matt verpasst / Matt erlaubt
      const playerWasInWin = er.mateFor && (er.mateFor === this.color);
      const playerNowLosing = ep.mateFor && (ep.mateFor !== this.color);
      if (playerNowLosing) this.evalMateNote = 'allowed';
      else if (playerWasInWin && !ep.mateFor) this.evalMateNote = 'missed';

      this.evalDeltaPawns = (cpPlayer - cpRep) / 100;   // negativ = eigener Zug schlechter
      this.cdr.markForCheck();
    }).catch(() => {
      if (epoch !== this.evalEpoch) return;
      this.evalLoading = false;
      this.cdr.markForCheck();
    });
  }

  private scheduleAdvance(ms: number): void {
    this.clearAdvance();
    this.advanceTimer = setTimeout(() => { this.advanceTimer = null; this.runAdvance(); }, ms);
  }

  /** Nach dem Reveal: richtig → nächste Karte; geduldet → dieselbe Karte erneut (Hauptzug üben),
   *  damit der Trainer NICHT selbst den Hauptzug vorspielt. */
  private runAdvance(): void {
    if (this.outcome === 'tolerated') this.retryCurrentCard();
    else this.next();
  }

  /** Geduldeter Zug stand sichtbar auf dem Brett → jetzt zurücknehmen (Brett auf fenBefore)
   *  und dieselbe Stellung erneut spielbar machen, ohne im Zug-Verlauf weiterzuspringen. */
  private retryCurrentCard(): void {
    this.clearAdvance();
    this.outcome = 'correct';   // neutraler CSS-Zustand
    this.fen = this.startFen;   // geduldeten Zug zurücknehmen
    this.lastMove = undefined;
    try { this.dests = calcDests(new Chess(this.fen)); } catch { this.dests = new Map(); }
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  private scheduleWrongRevert(): void {
    this.clearWrongRevert();
    this.wrongRevertTimer = setTimeout(() => { this.wrongRevertTimer = null; this.revertWrongHold(); }, WRONG_HOLD_MS);
  }

  /** Nimmt den noch sichtbaren falschen Zug zurück, bleibt aber im FEEDBACK (Buttons sichtbar). */
  private revertWrongHold(): void {
    if (this.outcome !== 'wrong' || this.phase !== 'FEEDBACK') return;
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.cdr.markForCheck();
  }

  private clearWrongRevert(): void {
    if (this.wrongRevertTimer !== null) { clearTimeout(this.wrongRevertTimer); this.wrongRevertTimer = null; }
  }

  private clearAdvance(): void {
    if (this.advanceTimer !== null) { clearTimeout(this.advanceTimer); this.advanceTimer = null; }
    this.clearWrongRevert();
  }

  next(): void {
    this.clearAdvance();
    this.index++;
    this.showCurrent();
  }
}
