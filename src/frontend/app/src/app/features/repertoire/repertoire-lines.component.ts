import { Component, Input, Output, EventEmitter, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { Move } from 'chess.js';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { RepertoireLine } from './repertoire-viewer.service';
import { RepertoireTrainingService, LineStateDto } from './repertoire-training.service';
import { autoChapterColors, readChapterColorOverrides, rootSideOf, setChapterColorOverride, TrainColor } from './repertoire-color.util';

/** Ein Chapter-Bucket mit seinen Linien. Reihenfolge = erstes Auftreten im PGN. */
interface ChapterGroup {
  chapter: string;
  lines: RepertoireLine[];
  expanded: boolean;
}

type LineStatus = 'new' | 'due' | 'scheduled' | 'paused';

@Component({
  selector: 'app-repertoire-lines',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatListModule, MatIconModule, MatButtonModule, MatMenuModule,
    MatTooltipModule, TranslatePipe, MoveListComponent,
  ],
  template: `
    @if (selectedIndex >= 0) {
      <div class="move-view">
        <button mat-button (click)="lineDeselected.emit()" class="back-btn">
          <mat-icon>arrow_back</mat-icon> {{ 'repertoire.lines.allLines' | translate }}
        </button>
        <div class="line-header">
          <span class="players">{{ lines[selectedIndex].white }} vs {{ lines[selectedIndex].black }}</span>
          <span class="result">{{ lines[selectedIndex].result }}</span>
        </div>
        <div class="move-list-wrap">
          <app-move-list
            [moves]="moves"
            [currentMoveIndex]="currentMoveIndex"
            [comments]="comments"
            (moveClicked)="moveClicked.emit($event)" />
        </div>
      </div>
    } @else {
      <div class="lines-list">
        @if (repertoireId != null && chapterGroups().length > 0) {
          <div class="course-bar">
            <a mat-stroked-button [routerLink]="['/repertoires', repertoireId, 'train']"
               [matTooltip]="'repertoire.lines.sr.reviewAll' | translate">
              <mat-icon>fitness_center</mat-icon>
            </a>
            <a mat-stroked-button [routerLink]="['/repertoires', repertoireId, 'train']" [queryParams]="{ mode: 'learn' }"
               [matTooltip]="'repertoire.lines.sr.learnAll' | translate">
              <mat-icon>school</mat-icon>
            </a>
            <span class="spacer"></span>
            <button mat-icon-button [matMenuTriggerFor]="courseMenu" [disabled]="busy"
                    [matTooltip]="'repertoire.lines.sr.courseActions' | translate"><mat-icon>more_vert</mat-icon></button>
            <mat-menu #courseMenu="matMenu">
              <button mat-menu-item (click)="promote(allKeys())"><mat-icon>playlist_add</mat-icon>{{ 'repertoire.lines.sr.addAll' | translate }}</button>
              <button mat-menu-item (click)="makeDue(allKeys())"><mat-icon>bolt</mat-icon>{{ 'repertoire.lines.sr.makeAllDue' | translate }}</button>
              <button mat-menu-item (click)="setPaused(allKeys(), true)"><mat-icon>pause_circle</mat-icon>{{ 'repertoire.lines.sr.pauseAll' | translate }}</button>
              <button mat-menu-item (click)="setPaused(allKeys(), false)"><mat-icon>play_circle</mat-icon>{{ 'repertoire.lines.sr.resumeAll' | translate }}</button>
            </mat-menu>
          </div>
        }
        @if (chapterGroups().length === 0) {
          <div class="empty">{{ 'repertoire.lines.empty' | translate }}</div>
        } @else {
          @for (group of chapterGroups(); track group.chapter) {
            <div class="chapter-block">
              <div class="chapter-head">
                <button class="chapter-toggle" (click)="toggleChapter(group.chapter)"
                        [attr.aria-expanded]="group.expanded"
                        [attr.aria-label]="'repertoire.lines.toggleChapter' | translate">
                  <mat-icon>{{ group.expanded ? 'expand_more' : 'chevron_right' }}</mat-icon>
                  <span class="chapter-name">{{ group.chapter || ('repertoire.lines.noChapter' | translate) }}</span>
                  <span class="chapter-count">{{ group.lines.length }}</span>
                </button>
                @if (repertoireId != null) {
                  <span class="chapter-color" [ngClass]="chapterColor(group)"
                        [matTooltip]="'repertoire.lines.color.tooltip' | translate">
                    {{ (chapterColor(group) === 'w' ? 'repertoire.lines.color.white' : 'repertoire.lines.color.black') | translate }}
                  </span>
                  <button mat-icon-button [matMenuTriggerFor]="chapterMenu" [disabled]="busy"
                          [attr.aria-label]="'repertoire.lines.sr.chapterActions' | translate"><mat-icon>more_vert</mat-icon></button>
                  <mat-menu #chapterMenu="matMenu">
                    @if (group.chapter) {
                      <a mat-menu-item [routerLink]="['/repertoires', repertoireId, 'train']" [queryParams]="{ mode: 'learn', chapter: group.chapter }"><mat-icon>school</mat-icon>{{ 'repertoire.lines.sr.learn' | translate }}</a>
                      <a mat-menu-item [routerLink]="['/repertoires', repertoireId, 'train']" [queryParams]="{ chapter: group.chapter }"><mat-icon>fitness_center</mat-icon>{{ 'repertoire.lines.sr.review' | translate }}</a>
                    }
                    <button mat-menu-item (click)="promote(chapterKeys(group))"><mat-icon>playlist_add</mat-icon>{{ 'repertoire.lines.sr.addToPool' | translate }}</button>
                    <button mat-menu-item (click)="makeDue(chapterKeys(group))"><mat-icon>bolt</mat-icon>{{ 'repertoire.lines.sr.makeDue' | translate }}</button>
                    <button mat-menu-item (click)="setPaused(chapterKeys(group), true)"><mat-icon>pause_circle</mat-icon>{{ 'repertoire.lines.sr.pause' | translate }}</button>
                    <button mat-menu-item (click)="setPaused(chapterKeys(group), false)"><mat-icon>play_circle</mat-icon>{{ 'repertoire.lines.sr.resume' | translate }}</button>
                    <div class="menu-label">{{ 'repertoire.lines.color.title' | translate }}</div>
                    <button mat-menu-item (click)="setChapterColor(group, 'w')">
                      <mat-icon>{{ chapterColor(group) === 'w' ? 'radio_button_checked' : 'radio_button_unchecked' }}</mat-icon>{{ 'repertoire.lines.color.white' | translate }}
                    </button>
                    <button mat-menu-item (click)="setChapterColor(group, 'b')">
                      <mat-icon>{{ chapterColor(group) === 'b' ? 'radio_button_checked' : 'radio_button_unchecked' }}</mat-icon>{{ 'repertoire.lines.color.black' | translate }}
                    </button>
                  </mat-menu>
                }
              </div>
              @if (group.expanded) {
                @for (line of group.lines; track line.gameIndex) {
                  <div class="line-item">
                    <div class="line-main" role="button" tabindex="0"
                         (click)="lineSelected.emit(line.gameIndex)"
                         (keydown.enter)="lineSelected.emit(line.gameIndex)"
                         (keydown.space)="$event.preventDefault(); lineSelected.emit(line.gameIndex)">
                      <div class="line-players">
                        <span>{{ line.white }} vs {{ line.black }}</span>
                        <span class="sr-badge" [ngClass]="status(line)"
                              [matTooltip]="badgeTooltip(line) | translate">{{ badge(line) }}</span>
                      </div>
                      @if (line.opening) { <div class="line-opening">{{ line.opening }}</div> }
                      <div class="line-summary">{{ line.summary }}</div>
                    </div>
                    @if (repertoireId != null) {
                      <button mat-icon-button class="line-menu-btn" [matMenuTriggerFor]="lineMenu" [disabled]="busy"
                              (click)="$event.stopPropagation()"
                              [attr.aria-label]="'repertoire.lines.sr.lineActions' | translate"><mat-icon>more_vert</mat-icon></button>
                      <mat-menu #lineMenu="matMenu">
                        <button mat-menu-item (click)="shareLine.emit(line)"><mat-icon>share</mat-icon>{{ 'repertoire.shareLine.action' | translate }}</button>
                        <a mat-menu-item [routerLink]="['/repertoires', repertoireId, 'train']" [queryParams]="{ mode: 'learn', line: line.lineKey }"><mat-icon>school</mat-icon>{{ 'repertoire.lines.sr.learn' | translate }}</a>
                        <button mat-menu-item (click)="promote([line.lineKey])"><mat-icon>playlist_add</mat-icon>{{ 'repertoire.lines.sr.addToPool' | translate }}</button>
                        <button mat-menu-item (click)="makeDue([line.lineKey])"><mat-icon>bolt</mat-icon>{{ 'repertoire.lines.sr.makeDue' | translate }}</button>
                        @if (status(line) === 'paused') {
                          <button mat-menu-item (click)="setPaused([line.lineKey], false)"><mat-icon>play_circle</mat-icon>{{ 'repertoire.lines.sr.resume' | translate }}</button>
                        } @else {
                          <button mat-menu-item (click)="setPaused([line.lineKey], true)"><mat-icon>pause_circle</mat-icon>{{ 'repertoire.lines.sr.pause' | translate }}</button>
                        }
                      </mat-menu>
                    }
                  </div>
                }
              }
            </div>
          }
        }
      </div>
    }
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .lines-list { overflow-y: auto; height: 100%; }
    .course-bar { display: flex; align-items: center; gap: 6px; padding: 6px 8px;
      border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); position: sticky; top: 0;
      /* Material-M3-Surface-Token (adaptiert an Light/Dark); die alte --mat-app-background-color
         existiert in diesem Theme nicht → fiel im Dark-Mode auf Weiß zurück (heller Balken). */
      background: var(--mat-sys-surface-container, #fff); z-index: 1; }
    .course-bar .spacer { flex: 1; }
    .chapter-block { border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    .chapter-head { display: flex; align-items: center; gap: 4px; padding: 4px 4px 4px 4px;
      background: color-mix(in srgb, currentColor 5%, transparent); }
    .chapter-toggle { flex: 1; display: flex; align-items: center; gap: 4px;
      background: none; border: none; padding: 8px 4px; text-align: left; cursor: pointer;
      font: inherit; color: inherit; }
    .chapter-toggle:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .chapter-name { font-weight: 600; flex: 1; }
    .chapter-count { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 12px;
      padding: 2px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 10%, transparent); }
    .chapter-color { flex: 0 0 auto; font-size: 11px; font-weight: 700; padding: 1px 8px; border-radius: 999px;
      border: 1px solid color-mix(in srgb, currentColor 30%, transparent); cursor: default; }
    .chapter-color.w { background: #f5f5f5; color: #222; }
    .chapter-color.b { background: #222; color: #f5f5f5; }
    .menu-label { padding: 6px 16px 2px; font-size: 11px; font-weight: 700; text-transform: uppercase;
      letter-spacing: .04em; opacity: .6; }
    .line-item { display: flex; align-items: flex-start; border-top: 1px solid color-mix(in srgb, currentColor 6%, transparent); }
    .line-main { flex: 1; padding: 10px 8px 10px 32px; cursor: pointer; transition: background 0.15s; min-width: 0; }
    .line-main:hover { background: color-mix(in srgb, currentColor 4%, transparent); }
    .line-menu-btn { flex: 0 0 auto; margin: 4px 2px 0 0; }
    .line-players { display: flex; justify-content: space-between; align-items: center; gap: 8px;
      font-weight: 500; font-size: 14px; }
    .line-opening { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 12px; margin-top: 2px; }
    .line-summary { font-family: 'Roboto Mono', monospace; font-size: 12px;
      color: color-mix(in srgb, currentColor 47%, transparent); margin-top: 4px;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sr-badge { flex: 0 0 auto; font-size: 11px; font-weight: 700; padding: 1px 7px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 12%, transparent); color: color-mix(in srgb, currentColor 65%, transparent); }
    .sr-badge.due { background: rgba(46,125,50,.18); color: #2e7d32; }
    .sr-badge.scheduled { background: rgba(21,101,192,.15); color: #1565c0; }
    .sr-badge.paused { background: rgba(255,160,0,.18); color: #e65100; }
    .move-view { display: flex; flex-direction: column; height: 100%; }
    .back-btn { align-self: flex-start; margin: 4px; }
    .line-header { display: flex; justify-content: space-between; padding: 4px 16px 8px;
      font-size: 14px; border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .players { font-weight: 500; }
    .result { color: #1976d2; font-weight: 600; }
    .move-list-wrap { flex: 1; overflow: hidden; }
    .empty { padding: 2rem; text-align: center; color: color-mix(in srgb, currentColor 47%, transparent); }
  `],
})
export class RepertoireLinesComponent implements OnInit {
  // `lines` liegt hinter einem Signal, damit das `chapterGroups`-computed darauf reagiert (die
  // Linien werden async nachgeladen; ein plain @Input() würde das computed nicht invalidieren).
  private linesSig = signal<RepertoireLine[]>([]);
  @Input() set lines(v: RepertoireLine[]) { this.linesSig.set(v ?? []); }
  get lines(): RepertoireLine[] { return this.linesSig(); }
  @Input() selectedIndex = -1;
  @Input() moves: Move[] = [];
  @Input() currentMoveIndex = -1;
  @Input() comments: { [moveIndex: number]: string } = {};
  /** Repertoire-Id (für Deep-Links + SR-Aktionen). Ohne Id keine SR-UI. */
  @Input() repertoireId: number | null = null;

  @Output() lineSelected = new EventEmitter<number>();
  @Output() lineDeselected = new EventEmitter<void>();
  @Output() moveClicked = new EventEmitter<number>();
  /** „Diese Linie als öffentlichen Link teilen" — der Container baut das PGN + ruft die API. */
  @Output() shareLine = new EventEmitter<RepertoireLine>();

  busy = false;
  private states = signal<Map<string, LineStateDto>>(new Map());
  private collapsed = signal<Set<string>>(new Set());
  /** Manuelle Trainingsfarb-Overrides je Kapitel (localStorage, pro Gerät). */
  private colorOverrides = signal<Record<string, TrainColor>>({});

  constructor(private training: RepertoireTrainingService) {}

  ngOnInit(): void {
    this.loadStates();
    if (this.repertoireId != null) this.colorOverrides.set(readChapterColorOverrides(this.repertoireId));
  }

  private loadStates(): void {
    if (this.repertoireId == null) return;
    this.training.getLineStates(this.repertoireId).subscribe({
      next: list => this.states.set(new Map(list.map(s => [s.lineKey, s]))),
      error: () => {},
    });
  }

  readonly chapterGroups = computed<ChapterGroup[]>(() => {
    const collapsed = this.collapsed();
    const map = new Map<string, RepertoireLine[]>();
    for (const line of this.lines) {
      const key = line.chapter || '';
      let bucket = map.get(key);
      if (!bucket) { bucket = []; map.set(key, bucket); }
      bucket.push(line);
    }
    return [...map.entries()].map(([chapter, lines]) => ({ chapter, lines, expanded: !collapsed.has(chapter) }));
  });

  toggleChapter(chapter: string): void {
    const next = new Set(this.collapsed());
    if (next.has(chapter)) next.delete(chapter); else next.add(chapter);
    this.collapsed.set(next);
  }

  // ===== Trainingsfarbe je Kapitel =====

  /** Automatisch erkannte Trainingsfarbe je Kapitel (Mehrheit der „Seite des letzten Zugs"). */
  private readonly autoColors = computed<Map<string, TrainColor>>(() =>
    autoChapterColors(this.lines.map(l => ({
      chapter: l.chapter, side: l.lastMoveSide, rootSide: rootSideOf(l.startFen),
    }))));

  /** Effektive Trainingsfarbe eines Kapitels: manueller Override, sonst Auto-Erkennung. */
  chapterColor(group: ChapterGroup): TrainColor {
    return this.colorOverrides()[group.chapter] ?? this.autoColors().get(group.chapter) ?? 'w';
  }

  /** Trainingsfarbe eines Kapitels dauerhaft festlegen (überschreibt die Auto-Erkennung). */
  setChapterColor(group: ChapterGroup, color: TrainColor): void {
    if (this.repertoireId == null) return;
    setChapterColorOverride(this.repertoireId, group.chapter, color);
    this.colorOverrides.set({ ...this.colorOverrides(), [group.chapter]: color });
  }

  // ===== SR-Status + Aktionen =====

  status(line: RepertoireLine): LineStatus {
    const st = this.states().get(line.lineKey);
    if (!st) return 'new';
    if (st.paused) return 'paused';
    if (!st.inPool) return 'new';
    return new Date(st.dueAt).getTime() <= Date.now() ? 'due' : 'scheduled';
  }

  /** Kurz-Badge: neu / ⏸ / Stufe (Sx). */
  badge(line: RepertoireLine): string {
    const s = this.status(line);
    if (s === 'new') return '+';
    if (s === 'paused') return '⏸';
    const st = this.states().get(line.lineKey);
    return 'S' + (st?.level ?? 0);
  }

  badgeTooltip(line: RepertoireLine): string {
    return 'repertoire.lines.sr.status.' + this.status(line);
  }

  allKeys(): string[] { return this.lines.map(l => l.lineKey); }
  chapterKeys(group: ChapterGroup): string[] { return group.lines.map(l => l.lineKey); }

  promote(keys: string[]): void {
    if (this.repertoireId == null || !keys.length) return;
    this.run(this.training.promote(this.repertoireId, keys));
  }
  makeDue(keys: string[]): void {
    if (this.repertoireId == null || !keys.length) return;
    this.run(this.training.makeDue(this.repertoireId, keys));
  }
  setPaused(keys: string[], paused: boolean): void {
    if (this.repertoireId == null || !keys.length) return;
    this.run(this.training.setPaused(this.repertoireId, keys, paused));
  }

  private run(obs: Observable<unknown>): void {
    this.busy = true;
    obs.subscribe({ next: () => { this.busy = false; this.loadStates(); }, error: () => { this.busy = false; } });
  }
}
