import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { apiErrorText } from '../../core/api-error';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { BoardArrow, ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { SanPipe } from '../../shared/chess/san.pipe';
import { localizeSan } from '../../shared/chess/keyboard-move.util';
import { whiteToMove } from '../../shared/chess/fen-side';
import { TrainingClipPlayerComponent } from './training-clip-player.component';
import {
  DrillItem, attemptOutcome, clipRange, countResults, drillItemsOfLesson, drillItemsOfQuestions, isTargetMove, startsGroup,
} from './training-drill.util';
import { TrainingStatus, TrainingVideosService, trainingPartKey } from './training-videos.service';

/** So lange steht ein falscher Zug auf dem Brett, bevor es zurückspringt (der Text darunter bleibt stehen). */
const WRONG_MOVE_SHOW_MS = 700;

/**
 * „Nachspielen“ — `/training-videos/:id/drill` (eine Stunde) und `/training-videos/questions` (Fragen-Satz über alle
 * Stunden, `data.source = 'questions'`). Je Stellung: ist es eine Aufgabe (gespielter Zug oder Frage, Zug aus der FEN
 * legal), sucht man den Zug auf dem Brett — falsch zählt einen Versuch, „Hinweis“ kreist die ziehende Figur ein,
 * „Auflösen“ zeigt den Pfeil (zählt als falsch), drei Fehlversuche ebenso. Danach Trainerkommentar, Varianten,
 * Erklärung und „Erklärung hören“ (nur der Ausschnitt). Stand je Aufgabe per PUT. Regeln: `training-drill.util.ts`.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-training-drill',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, MatSlideToggleModule, TranslatePipe, IconLabelDirective,
    LoadErrorComponent, LoadingSpinnerComponent, ChessBoardComponent, SanPipe, TrainingClipPlayerComponent],
  template: `
    <div class="page">
      <div class="header">
        <div class="title">
          <a mat-icon-button [routerLink]="backLink()" [appIconLabel]="'common.back' | translate"><mat-icon>arrow_back</mat-icon></a>
          <h1>{{ (questions ? 'trainingVideos.questionSet' : 'trainingVideos.replay') | translate }}</h1>
        </div>
        @if (!loading() && allItems().length) {
          <mat-slide-toggle class="only-wrong" [checked]="onlyWrong()" (change)="setOnlyWrong($event.checked)">
            {{ 'trainingVideos.drill.onlyWrong' | translate }}
          </mat-slide-toggle>
        }
      </div>

      @if (loading()) {
        <app-loading-spinner />
      } @else if (notFound()) {
        <div class="empty-state">
          <p>{{ 'trainingVideos.lesson.notFound' | translate }}</p>
          <a mat-stroked-button routerLink="/training-videos">{{ 'trainingVideos.toList' | translate }}</a>
        </div>
      } @else if (loadFailed()) {
        <app-load-error (retry)="load()" />
      } @else if (items().length === 0) {
        <p class="empty-state">{{ (onlyWrong() ? 'trainingVideos.drill.noWrong' : 'trainingVideos.drill.empty') | translate }}</p>
      } @else if (finished()) {
        <section class="done">
          <h2>{{ 'trainingVideos.drill.done' | translate }}</h2>
          <p class="rh-dots counts">
            <span class="ok">✓ {{ 'trainingVideos.drill.countSolved' | translate: { count: counts().solved } }}</span>
            <span class="bad">✗ {{ 'trainingVideos.drill.countWrong' | translate: { count: counts().wrong } }}</span>
            <span>{{ 'trainingVideos.drill.countSkipped' | translate: { count: counts().skipped } }}</span>
          </p>
          <div class="actions">
            @if (counts().wrong > 0) {
              <button mat-flat-button color="primary" type="button" class="tv-repeat" (click)="repeatWrong()">
                <mat-icon>replay</mat-icon>{{ 'trainingVideos.drill.repeatWrong' | translate }}
              </button>
            }
            <button mat-stroked-button type="button" class="tv-restart" (click)="restart()">{{ 'trainingVideos.drill.restart' | translate }}</button>
            <a mat-button [routerLink]="backLink()">{{ (questions ? 'trainingVideos.toList' : 'trainingVideos.drill.toLesson') | translate }}</a>
          </div>
        </section>
      } @else if (current(); as it) {
        <p class="progress" aria-live="polite">{{ 'trainingVideos.drill.progress' | translate: { n: index() + 1, total: items().length } }}</p>
        @if (groupHead()) {
          <h2 class="group">
            <span class="rh-dots">
              @if (it.gameLabel) { <span>{{ it.gameLabel }}</span> }
              @if (questions || !it.gameLabel) {
                <span>{{ 'trainingVideos.drill.lessonOf' | translate: { date: (it.date | date: 'mediumDate') } }} · {{ partKey(it.part) | translate }}</span>
              }
              @if (questions && it.rookhubGameId) { <a [routerLink]="['/games', it.rookhubGameId]">{{ 'trainingVideos.drill.toGame' | translate }}</a> }
            </span>
          </h2>
        }
        <div class="layout">
          <div class="board-col">
            <app-chess-board [fen]="boardFen()" [flipped]="flipped()" [playable]="taskActive()" [lastMove]="lastMove()"
                             [arrows]="arrows()" (userMove)="onMove($event)" />
            <p class="board-hint">{{ (whiteMoves() ? 'common.whiteToMove' : 'common.blackToMove') | translate }}</p>
          </div>
          <div class="side">
            @if (it.target) {
              <p class="task">{{ it.segment.question?.prompt || ('trainingVideos.drill.findMove' | translate) }}</p>
            }
            @if (message(); as m) {
              <p class="msg" [class.ok]="status() === 'solved'" [class.bad]="status() === 'wrong'" role="status">{{ m }}</p>
            }
            @if (taskActive()) {
              <div class="actions">
                <button mat-stroked-button type="button" class="tv-hint" (click)="showHint()" [disabled]="hintShown()">
                  <mat-icon>lightbulb</mat-icon>{{ 'trainingVideos.drill.hint' | translate }}
                </button>
                <button mat-stroked-button type="button" class="tv-reveal" (click)="reveal()">
                  <mat-icon>visibility</mat-icon>{{ 'trainingVideos.drill.reveal' | translate }}
                </button>
                <button mat-button type="button" class="tv-skip" (click)="skip()">{{ 'trainingVideos.drill.skip' | translate }}</button>
              </div>
            } @else {
              @let seg = it.segment;
              @if (seg.comment) {
                <section class="block">
                  <h3>{{ 'trainingVideos.trainerComment' | translate }}</h3>
                  <p>{{ seg.comment }}</p>
                  @if (seg.quote) { <p class="quote">{{ 'trainingVideos.quote' | translate: { text: seg.quote } }}</p> }
                </section>
              }
              @if (seg.question?.answerLine; as line) {
                <p><span class="lbl">{{ 'trainingVideos.drill.answerLine' | translate }}:</span> {{ line | san: 'text' }}</p>
              }
              @if (seg.question?.explanation; as ex) {
                <section class="block"><h3>{{ 'trainingVideos.drill.explanation' | translate }}</h3><p>{{ ex }}</p></section>
              }
              @if (seg.variations) {
                <section class="block"><h3>{{ 'trainingVideos.drill.variations' | translate }}</h3><p class="variations">{{ seg.variations | san: 'text' }}</p></section>
              }
              @if (it.hasVideo) {
                <app-training-clip-player [lessonId]="it.lessonId" [start]="clip().start" [end]="clip().end" />
              }
              <div class="actions">
                <button mat-flat-button color="primary" type="button" class="tv-next" (click)="next()">
                  {{ 'common.next' | translate }}<mat-icon iconPositionEnd>arrow_forward</mat-icon>
                </button>
              </div>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .page { max-width: var(--page-max-width); margin: 0 auto; padding: 16px; box-sizing: border-box; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 8px 12px; flex-wrap: wrap; }
    .title { display: flex; align-items: center; gap: 4px; min-width: 0; }
    .header h1 { margin: 0; font-size: 1.4rem; }
    .empty-state { text-align: center; padding: 2rem 1rem; }
    .progress { margin: 8px 0; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
    .group { font-size: 1rem; margin: 8px 0; overflow-wrap: anywhere; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
    .board-col { width: min(100%, 480px); justify-self: center; }
    .side { min-width: 0; display: flex; flex-direction: column; gap: 8px; overflow-wrap: anywhere; }
    .side p { margin: 0; }
    .task { font-weight: 500; font-size: 1.05rem; }
    .msg { padding: 6px 10px; border-radius: 8px; border: 1px solid var(--mat-sys-outline-variant); }
    .msg.ok { color: var(--rh-success); border-color: currentColor; }
    .msg.bad { color: var(--rh-error); border-color: currentColor; }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .actions button, .actions a { min-height: 44px; }
    .block h3 { font-size: .9rem; margin: 0 0 2px; color: var(--mat-sys-on-surface-variant); }
    .quote { font-style: italic; margin-top: 4px !important; }
    .lbl { color: var(--mat-sys-on-surface-variant); }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: .9rem; }
    .done h2 { margin: 16px 0 8px; }
    .counts .ok { color: var(--rh-success); }
    .counts .bad { color: var(--rh-error); }
    @media (min-width: 900px) {
      .layout { grid-template-columns: minmax(0, 480px) minmax(0, 1fr); align-items: start; }
    }
  `],
})
export class TrainingDrillComponent implements OnInit {
  private readonly svc = inject(TrainingVideosService);
  private readonly route = inject(ActivatedRoute);
  private readonly translate = inject(TranslateService);
  private readonly snackbar = inject(SnackbarService);

  /** Fragen-Satz über alle Stunden statt einer Stunde. */
  readonly questions = this.route.snapshot.data?.['source'] === 'questions';
  private lessonId: number | null = null;

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly notFound = signal(false);
  readonly allItems = signal<DrillItem[]>([]);
  /** Die gerade gespielte Liste (Schnappschuss, damit sie sich beim Speichern nicht unter dem Index verschiebt). */
  readonly items = signal<DrillItem[]>([]);
  readonly onlyWrong = signal(false);
  readonly index = signal(0);
  readonly finished = signal(false);
  /** Ausgang je Segment in DIESEM Durchgang. */
  readonly results = signal<ReadonlyMap<number, TrainingStatus>>(new Map());

  // Zustand der aktuellen Stellung
  readonly attempts = signal(0);
  readonly hintShown = signal(false);
  readonly status = signal<TrainingStatus | null>(null);
  readonly message = signal<string | null>(null);
  readonly boardFen = signal('');
  readonly lastMove = signal<[string, string] | undefined>(undefined);
  readonly arrows = signal<readonly BoardArrow[]>([]);
  private resetTimer: number | null = null;

  readonly current = computed(() => this.items()[this.index()] ?? null);
  readonly taskActive = computed(() => !!this.current()?.target && this.status() === null);
  readonly groupHead = computed(() => startsGroup(this.items(), this.index()));
  readonly flipped = computed(() => !whiteToMove(this.current()?.fen));
  readonly whiteMoves = computed(() => whiteToMove(this.boardFen()));
  readonly clip = computed(() => clipRange(this.current()!.segment));
  readonly counts = computed(() => countResults(this.results()));
  readonly backLink = computed(() => this.questions || this.lessonId == null ? ['/training-videos'] : ['/training-videos', this.lessonId]);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearTimer());
  }

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);
    const unknown = this.translate.instant('trainingVideos.unknownPlayer');
    const done = (items: DrillItem[]) => {
      this.allItems.set(items);
      this.loading.set(false);
      const seg = Number(this.route.snapshot.queryParamMap.get('seg'));
      this.begin(items, Number.isInteger(seg) ? seg : null);
    };
    const failed = (err: HttpErrorResponse) => {
      if (err?.status === 404) this.notFound.set(true); else this.loadFailed.set(true);
      this.loading.set(false);
    };
    if (this.questions) {
      this.svc.questions(false).subscribe({ next: list => done(drillItemsOfQuestions(list ?? [])), error: failed });
      return;
    }
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!Number.isInteger(id) || id <= 0) { this.notFound.set(true); this.loading.set(false); return; }
    this.lessonId = id;
    this.svc.get(id).subscribe({ next: l => done(drillItemsOfLesson(l, unknown)), error: failed });
  }

  /** Durchgang mit `items` beginnen, bei Segment `startSeg` (sonst vorn). */
  private begin(items: DrillItem[], startSeg: number | null): void {
    this.items.set(items);
    this.results.set(new Map());
    this.finished.set(false);
    const at = startSeg == null ? -1 : items.findIndex(i => i.segment.id === startSeg);
    this.enter(Math.max(0, at));
  }

  private enter(i: number): void {
    this.clearTimer();
    this.index.set(i);
    const it = this.items()[i];
    this.attempts.set(0);
    this.hintShown.set(false);
    this.status.set(null);
    this.message.set(null);
    this.arrows.set([]);
    this.lastMove.set(undefined);
    this.boardFen.set(it?.fen ?? '');
  }

  setOnlyWrong(on: boolean): void {
    this.onlyWrong.set(on);
    const cur = this.current()?.segment.id ?? null;
    this.begin(on ? this.wrongOnes(this.allItems()) : this.allItems(), cur);
  }

  /** Falsch = in diesem Durchgang falsch, sonst laut gespeichertem Stand. */
  private wrongOnes(list: DrillItem[]): DrillItem[] {
    const r = this.results();
    return list.filter(i => (r.get(i.segment.id) ?? i.segment.progress?.status) === 'wrong');
  }

  repeatWrong(): void {
    const wrong = this.wrongOnes(this.items());
    this.onlyWrong.set(true);
    this.begin(wrong, null);
  }

  restart(): void { this.begin(this.items(), null); }

  next(): void {
    if (this.index() + 1 >= this.items().length) { this.clearTimer(); this.finished.set(true); return; }
    this.enter(this.index() + 1);
  }

  onMove(m: UserBoardMove): void {
    const it = this.current();
    if (!it?.target || !this.taskActive()) return;
    if (isTargetMove(it.target, m)) {
      this.boardFen.set(m.fen);
      this.lastMove.set([m.from, m.to]);
      this.arrows.set([]);
      const n = this.attempts();
      this.message.set(this.translate.instant(n > 0 ? 'trainingVideos.drill.solvedAfter' : 'trainingVideos.drill.solved',
        { move: this.sanText(it.target.san), count: n }));
      this.finish(it, 'solved');
      return;
    }
    const failed = this.attempts() + 1;
    this.attempts.set(failed);
    if (attemptOutcome(false, failed) === 'wrong') {
      this.showSolution(it, 'trainingVideos.drill.wrongFinal');
      this.finish(it, 'wrong');
      return;
    }
    this.message.set(this.translate.instant('trainingVideos.drill.wrong', { move: this.sanText(m.san), n: failed, max: 3 }));
    // Den falschen Zug kurz stehen lassen, dann zurück auf die Aufgabe (die Meldung bleibt).
    this.boardFen.set(m.fen);
    this.lastMove.set([m.from, m.to]);
    this.clearTimer();
    this.resetTimer = window.setTimeout(() => {
      this.resetTimer = null;
      if (this.current() !== it || !this.taskActive()) return;
      this.boardFen.set(it.fen);
      this.lastMove.set(undefined);
    }, WRONG_MOVE_SHOW_MS);
  }

  showHint(): void {
    const it = this.current();
    if (!it?.target) return;
    this.hintShown.set(true);
    this.arrows.set([{ from: it.target.from, to: it.target.from, brush: 'green' }]);
    this.message.set(this.translate.instant('trainingVideos.drill.hintText', { square: it.target.from }));
  }

  reveal(): void {
    const it = this.current();
    if (!it?.target) return;
    this.showSolution(it, 'trainingVideos.drill.revealed');
    this.finish(it, 'wrong');
  }

  skip(): void {
    const it = this.current();
    if (!it?.target) return;
    this.showSolution(it, 'trainingVideos.drill.skipped');
    this.finish(it, 'skipped');
  }

  private showSolution(it: DrillItem, key: string): void {
    this.clearTimer();
    this.boardFen.set(it.fen);
    this.lastMove.set(undefined);
    this.arrows.set([{ from: it.target!.from, to: it.target!.to, brush: 'green' }]);
    this.message.set(this.translate.instant(key, { move: this.sanText(it.target!.san) }));
  }

  /** Ausgang festhalten und speichern; ein gescheitertes Speichern wird gemeldet (keine vorgetäuschte Sicherung). */
  private finish(it: DrillItem, status: TrainingStatus): void {
    this.status.set(status);
    const r = new Map(this.results());
    r.set(it.segment.id, status);
    this.results.set(r);
    const attempts = this.attempts();
    this.svc.saveProgress(it.lessonId, it.segment.id, status, attempts).subscribe({
      next: progress => this.allItems.update(list => list.map(x => x.segment.id === it.segment.id
        ? { ...x, segment: { ...x.segment, progress } } : x)),
      error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'trainingVideos.drill.saveFailed')),
    });
  }

  private sanText(san: string): string {
    return localizeSan(san, this.translate.currentLang() ?? null);
  }

  private clearTimer(): void {
    if (this.resetTimer !== null) { clearTimeout(this.resetTimer); this.resetTimer = null; }
  }

  partKey(part: string | null | undefined): string { return trainingPartKey(part); }
}
