import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, effect, inject, signal, viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { ClampMoreDirective } from '../../shared/clamp-more/clamp-more.directive';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { SanPipe } from '../../shared/chess/san.pipe';
import { whiteToMove } from '../../shared/chess/fen-side';
import { activeSegmentAt, isValidFen } from './training-drill.util';
import { TrainingGame, TrainingLesson, TrainingSegment, TrainingVideosService, trainingGameLabel } from './training-videos.service';

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const KNOWN_TAGS = new Set(['question', 'key', 'mistake', 'plan', 'opening', 'endgame', 'tactic']);

/**
 * `/training-videos/:id` — eine Trainerstunde: Video (natives `<video>`, Ausweis `?t=`) und Brett mit der Stellung des
 * gewählten bzw. gerade laufenden Segments, daneben die Segmentliste (Zeit, Zug, Kommentar, Schlagwörter). Ein Klick
 * auf ein Segment spult das Video dorthin; beim Abspielen folgt die Markierung (und das Brett) der Videozeit. `?t=` und
 * `?seg=` springen. Darunter die Partien der Stunde (Link zur RookHub-Partie, Analysebaum im Analysebrett).
 *
 * `timeupdate` (≈ 4×/s) hängt per `addEventListener` am Element, nicht im Template: ein Template-Listener zeichnete die
 * Seite bei jedem Ereignis neu; so ändert sich nur das Signal, und das nur, wenn ein anderes Segment dran ist.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-training-lesson',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, TranslatePipe, ClampMoreDirective, IconLabelDirective,
    LoadErrorComponent, LoadingSpinnerComponent, ChessBoardComponent, SanPipe],
  template: `
    <div class="page">
      @if (loading()) {
        <app-loading-spinner />
      } @else if (notFound()) {
        <div class="empty-state">
          <p>{{ 'trainingVideos.lesson.notFound' | translate }}</p>
          <a mat-stroked-button routerLink="/training-videos">{{ 'trainingVideos.toList' | translate }}</a>
        </div>
      } @else if (loadFailed()) {
        <app-load-error (retry)="load()" />
      } @else if (lesson(); as l) {
        <div class="header">
          <div class="title">
            <a mat-icon-button routerLink="/training-videos" [appIconLabel]="'common.back' | translate"><mat-icon>arrow_back</mat-icon></a>
            <h1>{{ 'trainingVideos.lesson.title' | translate: { date: (l.date | date: 'mediumDate') } }}</h1>
          </div>
          <div class="header-actions">
            <a mat-flat-button color="primary" class="tv-drill" [routerLink]="['/training-videos', l.id, 'drill']"
               [queryParams]="current() ? { seg: current()!.id } : {}">
              <mat-icon>replay</mat-icon>{{ 'trainingVideos.replay' | translate }}
            </a>
          </div>
        </div>
        <div class="rh-dots meta">
          @if (l.durationSec) { <span>{{ clock(l.durationSec) }}</span> }
          <span>{{ (segments().length === 1 ? 'trainingVideos.positionsOne' : 'trainingVideos.positions') | translate: { count: segments().length } }}</span>
          @if (l.ownerName) { <span>{{ 'trainingVideos.owner' | translate: { name: l.ownerName } }}</span> }
        </div>
        @if (l.summary) {
          <p class="summary" id="tv-lesson-sum" [appClampMore]="l.summary" [clampLines]="3" [clampExpanded]="summaryOpen()">
            <span class="clamp-text"></span>
            <button type="button" class="clamp-more" aria-controls="tv-lesson-sum" [attr.aria-expanded]="summaryOpen()"
                    (click)="summaryOpen.set(!summaryOpen())">{{ (summaryOpen() ? 'common.showLess' : 'common.showMore') | translate }}</button>
          </p>
        }

        <div class="layout">
          <div class="media">
            @if (l.hasVideo) {
              @if (videoSrc(); as src) {
                <video #video class="video" controls preload="metadata" playsinline [src]="src" (error)="videoFailed.set(true)"></video>
              }
              @if (videoFailed()) {
                <p class="video-error" role="alert">
                  <mat-icon aria-hidden="true">cloud_off</mat-icon>{{ 'trainingVideos.lesson.videoFailed' | translate }}
                  <button mat-button type="button" (click)="reloadVideo()">{{ 'common.retry' | translate }}</button>
                </p>
              }
            } @else {
              <p class="no-video">{{ 'trainingVideos.lesson.noVideo' | translate }}</p>
            }
            <div class="board-wrap">
              <app-chess-board [fen]="boardFen()" [flipped]="flipped()" />
              <div class="board-bar">
                <span class="board-hint">{{ (whiteMoves() ? 'common.whiteToMove' : 'common.blackToMove') | translate }}</span>
                <button mat-icon-button type="button" (click)="flipped.set(!flipped())" [appIconLabel]="'trainingVideos.flip' | translate">
                  <mat-icon>swap_vert</mat-icon>
                </button>
              </div>
            </div>
            @if (current(); as s) {
              <div class="current" aria-live="polite">
                @if (s.move) { <strong class="cur-move">{{ s.move | san }}</strong> }
                @if (s.comment) { <p>{{ s.comment }}</p> }
                @if (s.quote) { <p class="quote">{{ 'trainingVideos.quote' | translate: { text: s.quote } }}</p> }
                @if (s.variations) { <p class="variations"><span class="lbl">{{ 'trainingVideos.drill.variations' | translate }}:</span> {{ s.variations | san: 'text' }}</p> }
              </div>
            }
          </div>

          <section class="segments" [attr.aria-label]="'trainingVideos.lesson.segments' | translate">
            <h2>{{ 'trainingVideos.lesson.segments' | translate }}</h2>
            <ol class="seg-list" #segList>
              @for (s of segments(); track s.id) {
                <li>
                  <button type="button" class="seg" [class.active]="s.id === selectedId()" [attr.aria-current]="s.id === selectedId() ? 'true' : null"
                          [attr.data-seg]="s.id" (click)="select(s, true)">
                    <span class="seg-head">
                      <span class="time">{{ clock(s.startSec) }}</span>
                      @if (s.move) { <strong>{{ s.move | san }}</strong> }
                      @if (s.moveKind === 'analysis') { <span class="chip">{{ 'trainingVideos.moveKind.analysis' | translate }}</span> }
                      @for (t of knownTags(s); track t) { <span class="chip">{{ 'trainingVideos.tags.' + t | translate }}</span> }
                      @if (!s.fen) { <span class="chip">{{ 'trainingVideos.lesson.noPosition' | translate }}</span> }
                    </span>
                    @if (s.comment) { <span class="seg-comment">{{ s.comment }}</span> }
                  </button>
                </li>
              }
            </ol>
          </section>
        </div>

        @if (l.games.length) {
          <section class="games">
            <h2>{{ 'trainingVideos.lesson.games' | translate }}</h2>
            <ul>
              @for (g of l.games; track g.key) {
                <li class="game">
                  <div class="game-info">
                    <span class="game-name">{{ label(g) }}</span>
                    <span class="rh-dots game-meta">
                      <span class="chip">{{ kindKey(g) | translate }}</span>
                      @if (g.event) { <span>{{ g.event }}</span> }
                      @if (g.year) { <span>{{ g.year }}</span> }
                      @if (g.result) { <span>{{ g.result }}</span> }
                    </span>
                  </div>
                  <div class="game-actions">
                    @if (l.hasVideo && g.fromSec != null) {
                      <button mat-button type="button" class="tv-game-seek" (click)="seek(g.fromSec, true)">▶ {{ clock(g.fromSec) }}</button>
                    }
                    @if (g.rookhubGameId) {
                      <a mat-button class="tv-game-open" [routerLink]="['/games', g.rookhubGameId]"><mat-icon>sports_esports</mat-icon>{{ 'trainingVideos.lesson.openGame' | translate }}</a>
                    }
                    @if (l.analysisPgns[g.key] || l.pgns[g.key]) {
                      <button mat-button type="button" class="tv-game-analysis" (click)="openAnalysis(g)">
                        <mat-icon>account_tree</mat-icon>{{ (l.analysisPgns[g.key] ? 'trainingVideos.lesson.openAnalysis' : 'trainingVideos.lesson.openBoard') | translate }}
                      </button>
                    }
                  </div>
                </li>
              }
            </ul>
          </section>
        }
      }
    </div>
  `,
  styles: [`
    .page { max-width: var(--page-max-width); margin: 0 auto; padding: 16px; box-sizing: border-box; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .title { display: flex; align-items: center; gap: 4px; min-width: 0; }
    .header h1 { margin: 0; font-size: 1.4rem; overflow-wrap: anywhere; }
    .header-actions { display: flex; gap: .5rem; flex-wrap: wrap; }
    .meta { font-size: .85rem; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; margin: 4px 0 8px; }
    .summary { margin: 0 0 16px; overflow-wrap: anywhere; }
    .clamp-more { background: none; border: 0; padding: 0 2px; color: var(--rh-accent); cursor: pointer; font: inherit; text-decoration: underline; }
    .empty-state { text-align: center; padding: 2rem 1rem; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
    .media { min-width: 0; display: flex; flex-direction: column; gap: 12px; }
    .video { width: 100%; max-width: 100%; max-height: 70vh; background: black; border-radius: 8px; display: block; }
    .video-error { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; color: var(--rh-error); margin: 0; }
    .no-video { margin: 0; color: var(--mat-sys-on-surface-variant); }
    .board-wrap { width: min(100%, 420px); align-self: center; }
    .board-bar { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .current { overflow-wrap: anywhere; }
    .current p { margin: 4px 0; }
    .quote { font-style: italic; }
    .lbl { color: var(--mat-sys-on-surface-variant); }
    .segments h2, .games h2 { font-size: 1.1rem; margin: 0 0 8px; }
    .seg-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
    .seg { display: flex; flex-direction: column; gap: 2px; width: 100%; min-height: 44px; text-align: start; padding: 8px 10px;
      background: none; color: inherit; font: inherit; border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; cursor: pointer; }
    .seg:hover { background: color-mix(in srgb, var(--rh-accent) 8%, transparent); }
    /* gewählt: Fläche UND Rahmen (Bedeutung nie nur über Farbe, E-Ink: aria-current → Innenrahmen + fett) */
    .seg.active { border: 2px solid var(--rh-accent); background: color-mix(in srgb, var(--rh-accent) 14%, transparent); padding: 7px 9px; }
    .seg-head { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
    .time { font-variant-numeric: tabular-nums; color: var(--mat-sys-on-surface-variant); font-size: .85rem; }
    .seg-comment { overflow-wrap: anywhere; font-size: .92rem; }
    .chip { font-size: .72rem; padding: 1px 7px; border-radius: 999px; background: color-mix(in srgb, currentColor 10%, transparent); white-space: nowrap; }
    .games { margin-top: 24px; }
    .games ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .game { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 4px 12px;
      border-bottom: 1px solid var(--mat-sys-outline-variant); padding-bottom: 6px; }
    .game-info { display: flex; flex-direction: column; min-width: 0; }
    .game-name { font-weight: 500; overflow-wrap: anywhere; }
    .game-meta { font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .game-actions { display: flex; flex-wrap: wrap; gap: 4px; }
    .game-actions > * { min-height: 44px; }
    @media (min-width: 900px) {
      .layout { grid-template-columns: minmax(0, 1.25fr) minmax(300px, 1fr); align-items: start; }
      .seg-list { max-height: calc(100vh - 180px); overflow-y: auto; padding-right: 4px; }
    }
  `],
})
export class TrainingLessonComponent implements OnInit {
  private readonly svc = inject(TrainingVideosService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly lesson = signal<TrainingLesson | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly notFound = signal(false);
  readonly summaryOpen = signal(false);
  readonly videoSrc = signal<string | null>(null);
  readonly videoFailed = signal(false);
  readonly flipped = signal(false);
  readonly selectedId = signal<number | null>(null);

  /** Segmente nach Zeit. */
  readonly segments = computed(() => [...(this.lesson()?.segments ?? [])].sort((a, b) => a.startSec - b.startSec || a.id - b.id));
  readonly current = computed(() => this.segments().find(s => s.id === this.selectedId()) ?? null);
  /** Brett: Stellung des gewählten Segments, sonst die zuletzt gezeigte (ein Segment ohne FEN lässt das Brett stehen). */
  private lastFen = START_FEN;
  readonly boardFen = computed(() => {
    const f = this.current()?.fen;
    if (isValidFen(f)) this.lastFen = f;
    else if (!this.current()) this.lastFen = this.segments().find(s => isValidFen(s.fen))?.fen ?? START_FEN;
    return this.lastFen;
  });
  readonly whiteMoves = computed(() => whiteToMove(this.boardFen()));

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private readonly segList = viewChild<ElementRef<HTMLElement>>('segList');
  /** Sprung, der wartet, bis das Video seine Länge kennt (vorher ignorieren Browser `currentTime`). */
  private pendingSeek: { t: number; play: boolean } | null = null;
  private lastActiveId: number | null = null;

  constructor() {
    effect(onCleanup => {
      const el = this.video()?.nativeElement;
      if (!el) return;
      const onTime = () => this.onTimeUpdate(el.currentTime);
      const onMeta = () => this.applyPendingSeek(el);
      el.addEventListener('timeupdate', onTime);
      el.addEventListener('loadedmetadata', onMeta);
      if (el.readyState >= 1) this.applyPendingSeek(el);
      onCleanup(() => { el.removeEventListener('timeupdate', onTime); el.removeEventListener('loadedmetadata', onMeta); });
    });
    inject(DestroyRef).onDestroy(() => this.video()?.nativeElement.pause());
  }

  ngOnInit(): void { this.load(); }

  load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);
    if (!Number.isInteger(id) || id <= 0) { this.notFound.set(true); this.loading.set(false); return; }
    this.svc.get(id).subscribe({
      next: l => {
        this.lesson.set(l);
        this.loading.set(false);
        this.applyQuery();
        if (l.hasVideo) this.loadVideo();
      },
      error: (err: HttpErrorResponse) => {
        if (err?.status === 404) this.notFound.set(true); else this.loadFailed.set(true);
        this.loading.set(false);
      },
    });
  }

  private applyQuery(): void {
    const q = this.route.snapshot.queryParamMap;
    const seg = Number(q.get('seg'));
    const t = Number(q.get('t'));
    const s = Number.isInteger(seg) ? this.segments().find(x => x.id === seg) : undefined;
    if (s) this.selectedId.set(s.id);
    const at = q.has('t') && Number.isFinite(t) ? t : s?.startSec;
    if (at != null) {
      this.pendingSeek = { t: Math.max(0, at), play: false };
      if (!s) { const a = activeSegmentAt(this.segments(), at); if (a) this.selectedId.set(a.id); }
    }
    this.lastActiveId = this.selectedId();
  }

  private loadVideo(): void {
    const l = this.lesson();
    if (!l) return;
    this.videoFailed.set(false);
    this.svc.accessToken().subscribe({
      next: token => this.videoSrc.set(this.svc.videoUrl(l.id, token)),
      error: () => this.videoFailed.set(true),
    });
  }

  reloadVideo(): void {
    const el = this.video()?.nativeElement;
    if (el && !this.pendingSeek) this.pendingSeek = { t: el.currentTime, play: false };
    this.svc.forgetToken();
    this.videoSrc.set(null);
    this.loadVideo();
  }

  /** Segment wählen: Brett zeigt seine Stellung; mit `seekVideo` spult das Video an seinen Anfang. */
  select(s: TrainingSegment, seekVideo: boolean): void {
    this.selectedId.set(s.id);
    this.lastActiveId = s.id;
    if (seekVideo && this.lesson()?.hasVideo) this.seek(s.startSec, false);
  }

  /** Video an `t`; `play` = danach abspielen (sonst bleibt der Zustand, wie er ist). */
  seek(t: number, play: boolean): void {
    const el = this.video()?.nativeElement;
    if (!el || el.readyState < 1) { this.pendingSeek = { t, play }; return; }
    el.currentTime = t;
    if (play) void el.play().catch(() => { /* Autoplay-Sperre: der Nutzer drückt selbst ▶ */ });
  }

  private applyPendingSeek(el: HTMLVideoElement): void {
    const p = this.pendingSeek;
    if (!p) return;
    this.pendingSeek = null;
    el.currentTime = p.t;
    if (p.play) void el.play().catch(() => { /* s. o. */ });
  }

  /** Während das Video läuft, folgt die Auswahl der Zeit — gesetzt wird nur, wenn ein ANDERES Segment dran ist. */
  private onTimeUpdate(t: number): void {
    const active = activeSegmentAt(this.segments(), t);
    const id = active?.id ?? null;
    if (id === null || id === this.lastActiveId) return;
    this.lastActiveId = id;
    this.selectedId.set(id);
    this.scrollListTo(id);
  }

  /** Nur der Kasten der Liste scrollt (zweispaltig), nie die Seite. */
  private scrollListTo(id: number): void {
    const box = this.segList()?.nativeElement;
    if (!box || box.scrollHeight <= box.clientHeight) return;
    const item = box.querySelector<HTMLElement>(`[data-seg="${id}"]`);
    if (!item) return;
    const top = item.offsetTop - box.offsetTop;
    if (top < box.scrollTop || top + item.offsetHeight > box.scrollTop + box.clientHeight) box.scrollTop = Math.max(0, top - 8);
  }

  /** Analysebaum (bzw. Partie) im Analysebrett — als Router-State wie „In Analyse öffnen“ der Partienliste. */
  openAnalysis(g: TrainingGame): void {
    const l = this.lesson();
    const pgn = l?.analysisPgns[g.key] || l?.pgns[g.key];
    if (pgn) void this.router.navigate(['/analysis'], { state: { pgn } });
  }

  knownTags(s: TrainingSegment): string[] { return (s.tags ?? []).filter(t => KNOWN_TAGS.has(t)); }
  clock(sec: number): string { return formatSecondsClock(sec); }
  label(g: TrainingGame): string { return trainingGameLabel(g, this.translate.instant('trainingVideos.unknownPlayer')); }
  kindKey(g: TrainingGame): string {
    return ['own', 'lesson', 'exercise'].includes(g.kind) ? 'trainingVideos.kind.' + g.kind : 'trainingVideos.kind.lesson';
  }
}
