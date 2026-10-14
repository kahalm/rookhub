import { Component, Input, Output, EventEmitter, OnChanges, OnInit, SimpleChanges, computed, signal, ChangeDetectionStrategy } from '@angular/core';
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
import { MoveListComponent, MoveListCommentSegment } from '../../shared/pgn-viewer/move-list.component';
import { buildCommentSegments } from '../puzzles/comment-variation.util';
import { RepertoireLine } from './repertoire-viewer.service';
import { RepertoireTrainingService, LineStateDto } from './repertoire-training.service';
import { autoChapterColors, readChapterColorOverrides, rootSideOf, setChapterColorOverride, TrainColor } from './repertoire-color.util';
import { groupByChapter } from '../../shared/lines/chapter-groups.util';
import { MarkSet } from '../../shared/lines/mark-set';
import { SanPipe } from '../../shared/chess/san.pipe';
import { pgnHeaderText } from '../../shared/pgn-viewer/pgn-header.util';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';

/** Ein Chapter-Bucket mit seinen Linien. Reihenfolge = erstes Auftreten im PGN (die Gruppierung
 *  selbst liegt in `shared/lines/chapter-groups.util`, geteilt mit der Kurs-Durchsicht). */
interface ChapterGroup {
  chapter: string;
  lines: RepertoireLine[];
  expanded: boolean;
}

type LineStatus = 'new' | 'due' | 'scheduled' | 'paused';
// (Flashcards-Auswahl: siehe `picked` in der Komponente — Linien-Schlüssel, nicht Indizes.)

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-repertoire-lines',
  standalone: true,
  imports: [IconLabelDirective,
    CommonModule, RouterLink, MatListModule, MatIconModule, MatButtonModule, MatMenuModule,
    MatTooltipModule, TranslatePipe, MoveListComponent, SanPipe,
  ],
  template: `
    @if (selectedIndex >= 0) {
      <div class="move-view">
        <button mat-button (click)="lineDeselected.emit()" class="back-btn">
          <mat-icon>arrow_back</mat-icon> {{ 'repertoire.lines.allLines' | translate }}
        </button>
        <div class="line-header">
          <span class="players">{{ playersOf(lines[selectedIndex]) || ('repertoire.lines.lineN' | translate:{ n: selectedIndex + 1 }) }}</span>
          <span class="result">{{ lines[selectedIndex].result }}</span>
        </div>
        <div class="move-list-wrap">
          <app-move-list
            [moves]="moves"
            [currentMoveIndex]="currentMoveIndex"
            [comments]="comments"
            [commentSegments]="commentSegments"
            (moveClicked)="moveClicked.emit($event)"
            (commentMoveClicked)="commentMovePreview.emit($event)" />
        </div>
      </div>
    } @else {
      <div class="lines-list">
        @if (repertoireId != null && chapterGroups().length > 0) {
          <div class="course-bar">
            <!-- UI-Sweep 2026-10-11 (r2-rg-rep-toolbar): die Pillen tragen eine Beschriftung (vorher nur ein Icon,
                 die Bedeutung stand allein im Tooltip — am Handy gar nicht zu erfahren). -->
            <!-- UI-Sweep z1 (z1-rk-rep-abfragen-doppelt): EIN „Abfragen“ — als Hauptknopf hier neben „Lernen“. -->
            <a mat-flat-button color="primary" class="bar-btn bar-btn--quiz" [routerLink]="['/repertoires', repertoireId, 'train']"
               [matTooltip]="'repertoire.lines.sr.reviewAll' | translate">
              <mat-icon>replay</mat-icon><span class="bar-label">{{ 'repertoire.lines.sr.reviewShort' | translate }}</span>
            </a>
            <a mat-stroked-button class="bar-btn" [routerLink]="['/repertoires', repertoireId, 'train']" [queryParams]="{ mode: 'learn' }"
               [matTooltip]="'repertoire.lines.sr.learnAll' | translate">
              <mat-icon>school</mat-icon><span class="bar-label">{{ 'repertoire.lines.sr.learnShort' | translate }}</span>
            </a>
            <span class="spacer"></span>
            @if (marked.size > 0) {
              <a mat-stroked-button [routerLink]="['/repertoires', repertoireId, 'flashcards']"
                 [queryParams]="{ marked: 1 }"
                 [matTooltip]="'courses.flashcards.markedTooltip' | translate">
                <mat-icon>style</mat-icon>({{ marked.size }})
              </a>
            }
            <a mat-stroked-button class="bar-btn" [routerLink]="['/repertoires', repertoireId, 'flashcards']"
               [matTooltip]="'courses.flashcards.browseTooltip' | translate">
              <mat-icon>style</mat-icon><span class="bar-label">{{ 'repertoire.lines.sr.flashcardsShort' | translate }}</span>
            </a>
            <button mat-icon-button [matMenuTriggerFor]="courseMenu" [disabled]="busy"
                    [appIconLabel]="'repertoire.lines.sr.courseActions' | translate"><mat-icon>more_vert</mat-icon></button>
            <mat-menu #courseMenu="matMenu">
              <button mat-menu-item (click)="promote(allKeys())"><mat-icon>playlist_add</mat-icon>{{ 'repertoire.lines.sr.addAll' | translate }}</button>
              <button mat-menu-item (click)="makeDue(allKeys())"><mat-icon>bolt</mat-icon>{{ 'repertoire.lines.sr.makeAllDue' | translate }}</button>
              <button mat-menu-item (click)="setPaused(allKeys(), true)"><mat-icon>pause_circle</mat-icon>{{ 'repertoire.lines.sr.pauseAll' | translate }}</button>
              <button mat-menu-item (click)="setPaused(allKeys(), false)"><mat-icon>play_circle</mat-icon>{{ 'repertoire.lines.sr.resumeAll' | translate }}</button>
            </mat-menu>
          </div>
        }
        @if (chapterGroups().length === 0) {
          <div class="empty-state">{{ 'repertoire.lines.empty' | translate }}</div>
        } @else {
          @for (group of chapterGroups(); track group.chapter) {
            <div class="chapter-block">
              <div class="chapter-head">
                <button class="chapter-toggle" (click)="toggleChapter(group.chapter)"
                        [attr.aria-expanded]="group.expanded"
                        [attr.aria-label]="'repertoire.lines.toggleChapter' | translate">
                  <mat-icon>{{ group.expanded ? 'expand_more' : 'chevron_right' }}</mat-icon>
                  <!-- UI-Sweep 2026-10-11 (r2-rg-rep-question): „?" (PGN-Platzhalter) heißt „ohne Kapitel"; der
                       SCHLÜSSEL bleibt (Trainer, Kapitelfarben). -->
                  <span class="chapter-name">{{ chapterLabel(group.chapter) || ('repertoire.lines.noChapter' | translate) }}</span>
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
                <!-- b1-rk-repertoire-linien-handy-lang: je Kapitel höchstens LINES_PER_CHAPTER Linien, Rest auf Tipp. -->
                @for (line of shownLines(group); track line.gameIndex; let li = $index) {
                  <div class="line-item">
                    <!-- Persistente Flashcard-Markierung (Server, geräteübergreifend).
                         b1-rk-linienliste-auswahl-klein: 44-px-Trefferfläche um das Kästchen (vorher ~13 px am Rand). -->
                    <label class="line-pick-hit" [matTooltip]="'courses.flashcards.pick' | translate" (click)="$event.stopPropagation()">
                      <input type="checkbox" class="line-pick" [checked]="marked.has(line.lineKey)"
                             (click)="toggleMark(line, $event)"
                             [attr.aria-label]="'courses.flashcards.pick' | translate" />
                    </label>
                    <div class="line-main" role="button" tabindex="0"
                         (click)="lineSelected.emit(line.gameIndex)"
                         (keydown.enter)="lineSelected.emit(line.gameIndex)"
                         (keydown.space)="$event.preventDefault(); lineSelected.emit(line.gameIndex)">
                      <div class="line-players">{{ lineTitle(line) || ('repertoire.lines.lineN' | translate:{ n: li + 1 }) }}</div>
                      @if (line.opening) { <div class="line-opening">{{ line.opening }}</div> }
                      <div class="line-summary">{{ line.summary | san:'text' }}</div>
                    </div>
                    <!-- Status neben der Zeile statt im Titel; „+" (noch nicht im Pool) nimmt die Linie mit einem Tipp auf —
                         mit voller Trefferfläche (am Handy war es ein 18-px-Etikett). -->
                    @if (line.isInfo) {
                      <span class="sr-badge info" [matTooltip]="'repertoire.lines.infoTooltip' | translate">{{ 'repertoire.lines.info' | translate }}</span>
                    } @else if (repertoireId != null && status(line) === 'new') {
                      <button type="button" class="sr-add" [disabled]="busy" (click)="promote([line.lineKey])"
                              [matTooltip]="'repertoire.lines.sr.addToPool' | translate"
                              [attr.aria-label]="'repertoire.lines.sr.addToPool' | translate"><span class="sr-badge new">+</span></button>
                    } @else {
                      <span class="sr-badge" [ngClass]="status(line)"
                            [matTooltip]="badgeTooltip(line) | translate">{{ badge(line) }}</span>
                    }
                    @if (repertoireId != null) {
                      <button mat-icon-button class="line-menu-btn" [matMenuTriggerFor]="lineMenu" [disabled]="busy"
                              (click)="$event.stopPropagation()"
                              [attr.aria-label]="'repertoire.lines.sr.lineActions' | translate"><mat-icon>more_vert</mat-icon></button>
                      <mat-menu #lineMenu="matMenu">
                        <button mat-menu-item (click)="shareLine.emit(line)"><mat-icon>share</mat-icon>{{ 'repertoire.shareLine.action' | translate }}</button>
                        <button mat-menu-item (click)="downloadLine.emit(line)"><mat-icon>download</mat-icon>{{ 'repertoire.downloadLine' | translate }}</button>
                        @if (!line.isInfo) {
                          <a mat-menu-item [routerLink]="['/repertoires', repertoireId, 'train']" [queryParams]="{ mode: 'learn', line: line.lineKey }"><mat-icon>school</mat-icon>{{ 'repertoire.lines.sr.learn' | translate }}</a>
                          <button mat-menu-item (click)="promote([line.lineKey])"><mat-icon>playlist_add</mat-icon>{{ 'repertoire.lines.sr.addToPool' | translate }}</button>
                          <button mat-menu-item (click)="makeDue([line.lineKey])"><mat-icon>bolt</mat-icon>{{ 'repertoire.lines.sr.makeDue' | translate }}</button>
                          @if (status(line) === 'paused') {
                            <button mat-menu-item (click)="setPaused([line.lineKey], false)"><mat-icon>play_circle</mat-icon>{{ 'repertoire.lines.sr.resume' | translate }}</button>
                          } @else {
                            <button mat-menu-item (click)="setPaused([line.lineKey], true)"><mat-icon>pause_circle</mat-icon>{{ 'repertoire.lines.sr.pause' | translate }}</button>
                          }
                        }
                      </mat-menu>
                    }
                  </div>
                }
                @if (hiddenCount(group) > 0) {
                  <button mat-button type="button" class="show-more" (click)="showAll(group.chapter)">
                    {{ (hiddenCount(group) === 1 ? 'repertoire.lines.showMoreOne' : 'repertoire.lines.showMore') | translate: { count: hiddenCount(group) } }}
                  </button>
                }
              }
            </div>
          }
        }
      </div>
    }
  `,
  styles: [`
    /* b1-rk-linienliste-auswahl-klein: Tippziel 44 px (DESIGN 1.5), Kästchen 18 px mittig darin. */
    .line-pick-hit { flex: 0 0 auto; display: inline-flex; align-items: center; justify-content: center;
      min-width: 44px; min-height: 44px; cursor: pointer; }
    .line-pick { width: 18px; height: 18px; margin: 0; cursor: pointer; accent-color: var(--mat-sys-primary); }
    .show-more { display: block; margin: 4px auto 8px; }
    :host { display: block; height: 100%; }
    .lines-list { overflow-y: auto; height: 100%; }
    .course-bar { display: flex; align-items: center; gap: 6px; padding: 6px 8px;
      border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); position: sticky; top: 0;
      /* Material-M3-Surface-Token (adaptiert an Light/Dark); die alte --mat-app-background-color
         existiert in diesem Theme nicht → fiel im Dark-Mode auf Weiß zurück (heller Balken). */
      background: var(--mat-sys-surface-container, #fff); z-index: 1; }
    .course-bar .spacer { flex: 1; }
    /* UI-Sweep 2026-10-11 (r2-rg-rep-toolbar): Icon + kurze Beschriftung; ganz schmal nur das Icon. */
    .bar-btn mat-icon { margin-right: 4px; }
    @media (max-width: 380px) { .bar-btn .bar-label { display: none; } .bar-btn mat-icon { margin-right: 0; } }
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
    .line-item { display: flex; align-items: center; border-top: 1px solid color-mix(in srgb, currentColor 6%, transparent); }
    /* Titel direkt neben der Checkbox — der frühere 32-px-Einzug ließ am Handy eine Lücke; die 44-px-Trefferfläche
       des Kästchens bringt den Abstand schon mit. */
    .line-main { flex: 1; padding: 10px 8px 10px 0; cursor: pointer; transition: background 0.15s; min-width: 0; }
    .line-main:hover { background: color-mix(in srgb, currentColor 4%, transparent); }
    .line-menu-btn { flex: 0 0 auto; margin: 4px 2px 0 0; }
    .line-players { font-weight: 500; font-size: 14px; overflow-wrap: anywhere; }
    .line-item > .sr-badge { margin: 0 4px; }
    .sr-add { flex: 0 0 auto; display: inline-flex; align-items: center; justify-content: center; min-width: 40px; min-height: 40px;
      padding: 0; border: 0; background: none; color: inherit; cursor: pointer; border-radius: 50%; }
    .sr-add:hover:not(:disabled) { background: color-mix(in srgb, currentColor 8%, transparent); }
    .sr-add:disabled { cursor: default; opacity: .5; }
    .sr-add .sr-badge { font-size: 14px; padding: 2px 9px; }
    .line-opening { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 12px; margin-top: 2px; }
    .line-summary { font-family: 'Roboto Mono', monospace; font-size: 12px;
      color: color-mix(in srgb, currentColor 47%, transparent); margin-top: 4px;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sr-badge { flex: 0 0 auto; font-size: 11px; font-weight: 700; padding: 1px 7px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 12%, transparent); color: color-mix(in srgb, currentColor 65%, transparent); }
    .sr-badge.due { background: color-mix(in srgb, var(--rh-success) 18%, transparent); color: var(--rh-success); }
    .sr-badge.scheduled { background: color-mix(in srgb, var(--rh-info) 15%, transparent); color: var(--rh-info); }
    .sr-badge.paused { background: color-mix(in srgb, var(--rh-warn) 18%, transparent); color: var(--rh-warn); }
    .sr-badge.info { background: rgba(97,97,97,.15); color: #616161; }
    .move-view { display: flex; flex-direction: column; height: 100%; }
    .back-btn { align-self: flex-start; margin: 4px; }
    .line-header { display: flex; justify-content: space-between; padding: 4px 16px 8px;
      font-size: 14px; border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .players { font-weight: 500; }
    .result { color: var(--rh-accent); font-weight: 600; }
    .move-list-wrap { flex: 1; overflow: hidden; }
    .empty-state { color: color-mix(in srgb, currentColor 47%, transparent); }
  `],
})
export class RepertoireLinesComponent implements OnInit, OnChanges {
  // `lines` liegt hinter einem Signal, damit das `chapterGroups`-computed darauf reagiert (die
  // Linien werden async nachgeladen; ein plain @Input() würde das computed nicht invalidieren).
  private linesSig = signal<RepertoireLine[]>([]);
  @Input() set lines(v: RepertoireLine[]) {
    this.linesSig.set(v ?? []);
    this.initCollapsed();
  }
  get lines(): RepertoireLine[] { return this.linesSig(); }
  @Input() selectedIndex = -1;
  /** Stellungsfilter aktiv: dann alle Treffer offen, ohne Kapitel-Zuklappen und ohne „Weitere …“. */
  private filteredSig = signal(false);
  @Input() set filtered(v: boolean) { this.filteredSig.set(!!v); }
  get filtered(): boolean { return this.filteredSig(); }

  /** PERSISTENT als Flashcard markierte Linien (Linien-Schlüssel, Server-Zustand). */
  marked = new MarkSet<string>();
  private marksLoadedFor: number | null = null;

  /** Markierung umschalten — optimistisch, bei Fehler zurückrollen (siehe `MarkSet`). */
  toggleMark(line: RepertoireLine, event: Event): void {
    event.stopPropagation();
    const id = this.repertoireId;
    if (id == null) return;
    this.marked.toggle(line.lineKey, (key, next) => this.training.setFlashcardMark(id, key, next));
  }

  /** Markierungen laden, sobald die Repertoire-Id da ist (Input kann nach ngOnInit eintreffen). */
  private ensureMarksLoaded(): void {
    if (this.repertoireId == null || this.marksLoadedFor === this.repertoireId) return;
    this.marksLoadedFor = this.repertoireId;
    this.training.getFlashcardMarks(this.repertoireId).subscribe({
      next: m => this.marked.replace(m.lineKeys),
      error: () => {},
    });
  }

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
  @Output() downloadLine = new EventEmitter<RepertoireLine>();
  /** Ein Zug in einem Kommentar (auch einer eingefalteten Variante) wurde angeklickt → Brett-Vorschau. */
  @Output() commentMovePreview = new EventEmitter<MoveListCommentSegment>();
  /** Kommentare der gewählten Linie in Stücken mit anklickbaren Zügen (verankert über ihre Zugnummer). */
  commentSegments: { [moveIndex: number]: MoveListCommentSegment[] } = {};

  busy = false;
  private states = signal<Map<string, LineStateDto>>(new Map());
  private collapsed = signal<Set<string>>(new Set());
  /** Startzustand der Kapitel schon gesetzt (einmal je Komponente, beim ersten vollen Linienstand). */
  private collapsedInit = false;
  /** Kapitel, in denen der Nutzer „Weitere … anzeigen“ getippt hat. */
  private showAllIn = signal<Set<string>>(new Set());
  /** Manuelle Trainingsfarb-Overrides je Kapitel (localStorage, pro Gerät). */
  private colorOverrides = signal<Record<string, TrainColor>>({});

  constructor(private training: RepertoireTrainingService) {}

  ngOnInit(): void {
    this.loadStates();
    this.ensureMarksLoaded();
    if (this.repertoireId != null) this.colorOverrides.set(readChapterColorOverrides(this.repertoireId));
  }

  ngOnChanges(changes: SimpleChanges): void {
    this.ensureMarksLoaded();
    if (changes['moves'] || changes['comments'] || changes['selectedIndex'] || changes['lines']) {
      this.rebuildCommentSegments();
    }
  }

  private rebuildCommentSegments(): void {
    const line = this.lines?.[this.selectedIndex];
    const map: { [moveIndex: number]: MoveListCommentSegment[] } = {};
    if (line) {
      const ucis = this.moves.map(m => m.from + m.to + (m.promotion ?? ''));
      for (const [key, text] of Object.entries(this.comments ?? {})) {
        if (text) map[Number(key)] = buildCommentSegments(text, line.startFen, ucis);
      }
    }
    this.commentSegments = map;
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
    const filtered = this.filteredSig();
    return groupByChapter(this.lines, line => line.chapter || '')
      .map(g => ({ chapter: g.key, lines: g.lines, expanded: filtered || !collapsed.has(g.key) }));
  });

  /**
   * b1-rk-repertoire-linien-handy-lang (DESIGN 4.6 Regel 11): ein großes Repertoire lief seit der Liste ohne eigenen
   * Scrollkasten am Handy über ≈ 14 Bildschirme. Ab COLLAPSE_FROM Linien in mehreren Kapiteln startet nur das erste
   * Kapitel offen (der Zähler im Kopf zeigt die Menge); einmalig — danach gilt, was der Nutzer auf-/zuklappt.
   */
  private initCollapsed(): void {
    if (this.collapsedInit || this.filteredSig() || !this.lines.length) return;
    this.collapsedInit = true;
    if (this.lines.length <= RepertoireLinesComponent.COLLAPSE_FROM) return;
    const keys = groupByChapter(this.lines, line => line.chapter || '').map(g => g.key);
    if (keys.length > 1) this.collapsed.set(new Set(keys.slice(1)));
  }

  static readonly COLLAPSE_FROM = 20;
  static readonly LINES_PER_CHAPTER = 15;

  /** Sichtbare Linien eines Kapitels: höchstens LINES_PER_CHAPTER, außer bei Filter oder nach „Weitere …“. */
  shownLines(group: ChapterGroup): RepertoireLine[] {
    if (this.filteredSig() || this.showAllIn().has(group.chapter)) return group.lines;
    return group.lines.slice(0, RepertoireLinesComponent.LINES_PER_CHAPTER);
  }

  hiddenCount(group: ChapterGroup): number {
    return group.lines.length - this.shownLines(group).length;
  }

  showAll(chapter: string): void {
    this.showAllIn.set(new Set(this.showAllIn()).add(chapter));
  }

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

  /** Titel der Linie im Kapitel: Chessable trägt den Kapitelnamen als Schwarz-Kopf — innerhalb des Kapitels
   *  wiederholt „Weiß vs Kapitel" ihn nur (und brach am Handy auf 2–3 Zeilen um). */
  lineTitle(line: RepertoireLine): string {
    // UI-Sweep 2026-10-11 (r2-rg-rep-question): „?" bzw. leere Kopfzeilen gelten als fehlend — ganz ohne Namen
    // liefert das '' und die Vorlage schreibt „Linie n" (statt „?" oder „? vs ?").
    const white = pgnHeaderText(line.white);
    const black = pgnHeaderText(line.black);
    if (!white && !black) return '';
    if (line.chapter && line.black === line.chapter) return white || black;
    if (!white || !black) return white || black;
    return `${white} vs ${black}`;
  }

  /** „Weiß vs Schwarz" der Kopfzeilen ohne Platzhalter (UI-Sweep 2026-10-11, r2-rg-rep-question). */
  playersOf(line: RepertoireLine): string {
    return [pgnHeaderText(line.white), pgnHeaderText(line.black)].filter(s => s).join(' vs ');
  }

  /** Anzeige-Name eines Kapitels: der PGN-Platzhalter „?" zählt als „ohne Kapitel" (leer). */
  chapterLabel(chapter: string): string { return pgnHeaderText(chapter); }

  badgeTooltip(line: RepertoireLine): string {
    return 'repertoire.lines.sr.status.' + this.status(line);
  }

  // Info-Linien bleiben aus allen SR-Aktionen draußen (der Trainer fragt sie nicht ab).
  allKeys(): string[] { return this.lines.filter(l => !l.isInfo).map(l => l.lineKey); }
  chapterKeys(group: ChapterGroup): string[] { return group.lines.filter(l => !l.isInfo).map(l => l.lineKey); }

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
