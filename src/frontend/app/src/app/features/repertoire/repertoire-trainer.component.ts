import { Component, OnInit, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
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
import { PreferencesService } from '../../core/preferences.service';
import { RepertoireTrainingService, RepertoireCardStateDto, ReviewCardRequest } from './repertoire-training.service';
import { buildRepertoireGraph, cardsForColor, normSan, RepCard } from './repertoire-tree.util';

type Phase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE';
type Outcome = 'correct' | 'tolerated' | 'wrong';

const NEW_LIMIT = 20;
const COLOR_KEY = (id: number) => `rookhub_rep_train_color_${id}`;

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

    <div *ngSwitchDefault class="play">
      <app-puzzle-board
        [fen]="fen" [orientation]="color === 'w' ? 'white' : 'black'"
        [turnColor]="color === 'w' ? 'white' : 'black'"
        [dests]="dests" [lastMove]="lastMove" [viewOnly]="phase === 'FEEDBACK'"
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
          <p *ngIf="outcome === 'tolerated'"><mat-icon>info</mat-icon>
            {{ 'repertoireTrainer.tolerated' | translate: { move: expectedDisplay } }}</p>
          <p *ngIf="outcome === 'wrong'"><mat-icon>cancel</mat-icon>
            {{ 'repertoireTrainer.wrong' | translate: { move: expectedDisplay } }}</p>
          <button mat-raised-button color="primary" (click)="next()">{{ 'repertoireTrainer.continue' | translate }}</button>
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
    .hint { color: var(--mdc-theme-text-secondary-on-background, #666); }
  `],
})
export class RepertoireTrainerComponent implements OnInit {
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
  private statesByKey = new Map<string, RepertoireCardStateDto>();

  constructor(
    private route: ActivatedRoute,
    private training: RepertoireTrainingService,
    public prefs: PreferencesService,
    private translate: TranslateService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.repertoireId = Number(this.route.snapshot.paramMap.get('id')) || 0;
    const saved = localStorage.getItem(COLOR_KEY(this.repertoireId));
    if (saved === 'w' || saved === 'b') this.color = saved;

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

  private graphCache: ReturnType<typeof buildRepertoireGraph> | null = null;

  setColor(c: 'w' | 'b'): void {
    if (c === this.color || !this.graphCache) return;
    this.color = c;
    localStorage.setItem(COLOR_KEY(this.repertoireId), c);
    this.allCards = cardsForColor(this.graphCache, c);
    this.buildQueue();
  }

  private buildQueue(): void {
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

  private get current(): RepCard | null {
    return this.index < this.queue.length ? this.queue[this.index] : null;
  }

  private showCurrent(): void {
    const card = this.current;
    if (!card) { this.phase = 'DONE'; this.cdr.markForCheck(); return; }
    this.fen = card.fenBefore + ' 0 1';   // normFen → volle FEN für chess.js/Brett
    this.lastMove = undefined;
    try {
      const c = new Chess(this.fen);
      this.dests = calcDests(c);
    } catch { this.dests = new Map(); }
    this.phase = 'PLAYING';
    this.cdr.markForCheck();
  }

  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const card = this.current;
    if (!card || this.phase !== 'PLAYING') return;

    let san = '';
    try {
      const c = new Chess(this.fen);
      const mv = c.move({ from: ev.orig, to: ev.dest, promotion: (ev.promotion as any) || 'q' });
      san = normSan(mv.san);
      this.lastMove = [ev.orig, ev.dest];
    } catch { return; }   // illegaler Zug ignorieren

    const expected = normSan(card.expected);
    const accepted = card.accepted.map(normSan);
    let grade: ReviewCardRequest['grade'];
    if (san === expected) { this.outcome = 'correct'; this.correct++; grade = 2; }
    else if (accepted.includes(san)) { this.outcome = 'tolerated'; grade = 1; }
    else { this.outcome = 'wrong'; this.wrong++; grade = 0; }

    this.expectedDisplay = card.expected;
    this.phase = 'FEEDBACK';

    // Bei „falsch" die Karte am Sitzungsende erneut zeigen.
    if (this.outcome === 'wrong') this.queue.push(card);

    this.training.review(this.repertoireId, { cardKey: card.cardKey, expectedMove: card.expected, grade })
      .subscribe({ next: st => this.statesByKey.set(st.cardKey, st), error: () => {} });

    this.cdr.markForCheck();
  }

  next(): void {
    this.index++;
    this.showCurrent();
  }
}
