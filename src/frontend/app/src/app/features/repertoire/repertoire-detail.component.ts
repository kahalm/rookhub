import { ChangeDetectorRef, Component, DoCheck, HostListener, OnInit, ChangeDetectionStrategy, inject, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { RepertoireService } from '../../core/repertoire.service';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { MatDialog } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { BoardArrow, ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { canonSan, sameArrows, treeArrows } from './repertoire-tree-arrows.util';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { START_FEN } from '../../shared/pgn-viewer/pgn-parser';
import { RepertoireLinesComponent } from './repertoire-lines.component';
import { RepertoireTreeComponent } from './repertoire-tree.component';
import { RepertoireEditComponent } from './repertoire-edit.component';
import { HoleBoardView, RepertoireHolesComponent } from './repertoire-holes.component';
import { isInfoLineGame } from './repertoire-info-line.util';
import { ExplorerPosition, ExplorerSettings, RepertoireExplorerService } from './repertoire-explorer.service';
import { Subscription, map, switchMap } from 'rxjs';
import { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import { TrainColor, chapterColorsOf, setChapterColorOverride } from '../../shared/repertoire/repertoire-color.util';
import { RepertoireViewerService, RepertoireLine } from './repertoire-viewer.service';
import { parsedGameToPgn } from './repertoire-line-pgn.util';
import { ShareLineDialogComponent } from './share-line-dialog.component';
import { CreateSubsetDialogComponent, CreateSubsetDialogData, CreateSubsetDialogResult } from './create-subset-dialog.component';
import { chaptersOf, subsetPgn } from './repertoire-subset.util';
import { MoveTreeService } from './move-tree.service';
import { exceedsSyncLimits } from '../../shared/pgn-viewer/pgn-parser';
import { commonStartFen, findPositionInGames, formatSansWithNumbers, normalizeFen } from './position-filter.util';
import { RepertoireDetail } from '../../core/models';
import { downloadBlob } from '../../shared/download.util';
import { pgnFileName, repertoireDownloadPgn } from '../../shared/pgn-export.util';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';

type ViewMode = 'lines' | 'tree' | 'holes' | 'edit';

/** Gerät merkt sich „Pfeile im Baum“ (an/aus). */
const TREE_ARROWS_KEY = 'rh.repertoire.treeArrows';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-repertoire-detail',
  standalone: true,
  imports: [IconLabelDirective,
    CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatButtonToggleModule,
    TranslatePipe, LoadingSpinnerComponent, ChessBoardComponent,
    RepertoireLinesComponent, RepertoireTreeComponent, RepertoireEditComponent, RepertoireHolesComponent,
  ],
  // Komponenten-bezogen (nicht providedIn:'root') — jede Instanz hat ihren eigenen
  // Viewer-/Tree-Zustand; per DI statt `new` für Testbarkeit.
  providers: [RepertoireViewerService, MoveTreeService],
  template: `
    @if (loading) {
      <app-loading-spinner />
    } @else if (repertoire) {
      <div class="detail-container">
        @if (parseProgress; as p) {
          <div class="parse-progress" role="status">
            <mat-icon>hourglass_top</mat-icon>
            {{ 'repertoire.detail.parsing' | translate: { done: p.done, total: p.total || '…' } }}
          </div>
        }
        <div class="detail-header">
          <div class="header-info">
            <h2 [attr.title]="repertoire.name">{{ repertoire.name }}</h2>
            <!-- b1-rk-keine-beschreibung: Untertitel nur mit Inhalt (keine Füllzeile „Keine Beschreibung“). -->
            @if (repertoire.description) {
              <span class="subtitle">{{ repertoire.description }}</span>
            }
          </div>
          <div class="header-actions">
            <!-- UI-Sweep z1 (z1-rk-rep-abfragen-doppelt): in der Linien-Ansicht steht „Abfragen“ als Hauptknopf in der
                 Linienleiste neben „Lernen“ — hier nur in Baum/Lochfinder, wo es die Leiste nicht gibt.
                 UI-Sweep 2026-10-12 (v1-rk-repertoire-abfragen-knopf-wandert): rechts neben der Moduswahl statt als
                 dritte Zeile unter dem Untertitel — der Kopf bleibt in allen Modi gleich hoch, Brett und Panel springen nicht. -->
            @if (mode !== 'lines') {
              <!-- Nachrunde: am Handy nur das Symbol (Beschriftung als aria-label/Tooltip), damit Knopf und
                   Moduswahl in EINER Zeile stehen und der Kopf in allen Modi gleich hoch bleibt. -->
              <!-- UI-Sweep a1 (a1-rk-holes-zwei-hauptknoepfe): im Lochfinder ist „Löcher suchen“ die Hauptaktion —
                   „Abfragen“ tritt dort als Rahmenknopf zurück (5.4: genau ein Primärknopf je Ansicht). -->
              @if (mode === 'holes') {
                <a mat-stroked-button class="train-btn" [routerLink]="['/repertoires', id, 'train']"
                   [attr.aria-label]="'repertoireTrainer.modeQuiz' | translate"
                   [attr.title]="'repertoireTrainer.modeQuiz' | translate">
                  <mat-icon>replay</mat-icon><span class="train-label">{{ 'repertoireTrainer.modeQuiz' | translate }}</span>
                </a>
              } @else {
                <a mat-flat-button color="primary" class="train-btn" [routerLink]="['/repertoires', id, 'train']"
                   [attr.aria-label]="'repertoireTrainer.modeQuiz' | translate"
                   [attr.title]="'repertoireTrainer.modeQuiz' | translate">
                  <mat-icon>replay</mat-icon><span class="train-label">{{ 'repertoireTrainer.modeQuiz' | translate }}</span>
                </a>
              }
            }
            <mat-button-toggle-group [value]="mode" (change)="setMode($event.value)" appearance="standard">
              <mat-button-toggle value="lines" [attr.aria-label]="'repertoire.detail.modeLines' | translate" [attr.title]="'repertoire.detail.modeLines' | translate">
                <mat-icon>list</mat-icon>
              </mat-button-toggle>
              <mat-button-toggle value="tree" [attr.aria-label]="'repertoire.detail.modeTree' | translate" [attr.title]="'repertoire.detail.modeTree' | translate">
                <mat-icon>account_tree</mat-icon>
              </mat-button-toggle>
              <mat-button-toggle value="holes" [attr.aria-label]="'repertoire.detail.modeHoles' | translate" [attr.title]="'repertoire.detail.modeHoles' | translate">
                <mat-icon>travel_explore</mat-icon>
              </mat-button-toggle>
              @if (repertoire.isOwner !== false) {
                <mat-button-toggle value="edit" [attr.aria-label]="'common.edit' | translate" [attr.title]="'common.edit' | translate">
                  <mat-icon>edit</mat-icon>
                </mat-button-toggle>
              }
            </mat-button-toggle-group>
          </div>
        </div>

        @if (mode === 'edit') {
          <div class="edit-panel">
            <app-repertoire-edit
              [repertoireId]="id"
              [files]="repertoire.files"
              (fileUploaded)="onFileChanged()"
              (fileDeleted)="onFileChanged()" />
          </div>
        } @else {
          <!-- a1-rk-holes-handy-brett-vor-werkzeug: im Lochfinder ohne gewähltes Loch zeigt das Brett nur die
               Grundstellung — am Handy steht dann das Werkzeug zuerst (.holes-idle, CSS unter 900 px). -->
          <div class="viewer-layout" [class.holes-idle]="mode === 'holes' && !holeView" [class.viewer-layout--tree]="mode === 'tree'">
            <div class="board-section">
              <app-chess-board
                [fen]="boardFen"
                [lastMove]="boardLastMove"
                [flipped]="boardFlipped"
                [playable]="filterBoardActive || mode === 'tree'"
                [arrows]="boardArrows"
                (fullscreenChange)="boardFullscreen = $event"
                (userMove)="onBoardMove($event)" />
              @if (mode === 'tree') {
                <!-- Baum: alle Fortsetzungen als Pfeile, Breite = Häufigkeit auf Lichess; im Brett-Vollbild immer an. -->
                <div class="tree-board-bar">
                  <button mat-stroked-button type="button" class="tree-arrows-toggle" [class.on]="treeArrowsOn"
                          [attr.aria-pressed]="treeArrowsOn" (click)="toggleTreeArrows()">
                    <mat-icon>{{ treeArrowsOn ? 'visibility' : 'visibility_off' }}</mat-icon>
                    {{ 'repertoire.tree.arrows' | translate }}
                  </button>
                  <span class="tree-board-hint">{{ 'repertoire.tree.dragHint' | translate }}</span>
                </div>
              }
              @if (filterBoardActive) {
                <!-- Stellungs-Filter: Züge auf dem Brett grenzen rechts die Linien auf die ein,
                     die die erreichte Stellung enthalten (Transpositionen zählen). -->
                <div class="filter-bar">
                  @if (filterStack.length > 0) {
                    <span class="filter-moves" [attr.title]="filterMovesText">{{ filterMovesText }}</span>
                    <button mat-icon-button (click)="undoFilterMove()"
                            [appIconLabel]="'repertoire.positionFilter.undo' | translate">
                      <mat-icon>undo</mat-icon>
                    </button>
                    <button mat-icon-button (click)="resetFilter()"
                            [appIconLabel]="'repertoire.positionFilter.reset' | translate">
                      <mat-icon>close</mat-icon>
                    </button>
                  } @else {
                    <span class="filter-hint">{{ 'repertoire.positionFilter.hint' | translate }}</span>
                  }
                </div>
                @if (filterStack.length > 0) {
                  <div class="filter-count">{{ 'repertoire.positionFilter.matches' | translate:{ count: visibleLines.length } }}</div>
                }
              }
              @if (mode === 'lines' && viewerService.selectedLineIndex >= 0) {
                <div class="nav-buttons">
                  <button mat-icon-button (click)="viewerService.goToStart()" [disabled]="viewerService.currentMoveIndex < 0">
                    <mat-icon>skip_previous</mat-icon>
                  </button>
                  <button mat-icon-button (click)="viewerService.goBack()" [disabled]="viewerService.currentMoveIndex < 0">
                    <mat-icon>navigate_before</mat-icon>
                  </button>
                  <button mat-icon-button (click)="viewerService.goForward()"
                          [disabled]="!viewerService.selectedGame || viewerService.currentMoveIndex >= viewerService.selectedGame.moves.length - 1">
                    <mat-icon>navigate_next</mat-icon>
                  </button>
                  <button mat-icon-button (click)="viewerService.goToEnd()"
                          [disabled]="!viewerService.selectedGame || viewerService.currentMoveIndex >= viewerService.selectedGame.moves.length - 1">
                    <mat-icon>skip_next</mat-icon>
                  </button>
                </div>
                @if (commentPreview) {
                  <div class="cmt-variation-bar">
                    <span>{{ 'book.variation.previewing' | translate }}</span>
                    <button mat-stroked-button type="button" (click)="exitCommentPreview()">
                      <mat-icon>undo</mat-icon> {{ 'common.back' | translate }}
                    </button>
                  </div>
                }
              }
            </div>
            <!-- UI-Sweep z1 (z1-rk-rep-brett-gross): die Linienliste ist nur so hoch wie ihr Inhalt (scrollt erst ab
                 Fensterhöhe); Zugansicht, Baum und Lochfinder behalten die feste Höhe (sie scrollen innen).
                 a1-rk-holes-karte-leer-hoch: der Lochfinder vor der ersten Suche ebenfalls nur so hoch wie sein Inhalt. -->
            <div class="side-panel" [class.side-panel--fit]="sidePanelFits">
              @if (mode === 'lines') {
                <app-repertoire-lines
                  [lines]="visibleLines"
                  [filtered]="filterStack.length > 0"
                  [selectedIndex]="viewerService.selectedLineIndex"
                  [moves]="viewerService.currentMoves"
                  [currentMoveIndex]="viewerService.currentMoveIndex"
                  [comments]="viewerService.currentComments"
                  [repertoireId]="id"
                  (lineSelected)="onLineSelected($event)"
                  (lineDeselected)="viewerService.deselectLine()"
                  (moveClicked)="viewerService.goToMove($event)"
                  (shareLine)="onShareLine($event)"
                  (downloadLine)="onDownloadLine($event)"
                  (createSubset)="onCreateSubset($event)"
                  (commentMovePreview)="onCommentMovePreview($event)"
                  (chapterColorChanged)="refreshChapterColors()" />
              } @else if (mode === 'holes') {
                <app-repertoire-holes
                  [repertoireId]="id"
                  [games]="trainableGames"
                  (holeSelected)="holeView = $event"
                  (searchActive)="holesSearchActive = $event" />
              } @else if (mode === 'tree') {
                <app-repertoire-tree
                  [children]="treeService.children"
                  [breadcrumbs]="treeService.breadcrumbs"
                  [popularity]="treePopularity()"
                  [popularityLoading]="treePopLoading()"
                  [popularityNote]="treePopNote()"
                  (nodeSelected)="treeService.selectChild($event); loadTreePopularity()"
                  (goUp)="treeService.goUp(); loadTreePopularity()"
                  (goToRoot)="treeService.goToRoot(); loadTreePopularity()"
                  (goToDepth)="treeService.goToDepth($event); loadTreePopularity()" />
              }
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .parse-progress { display: flex; align-items: center; gap: 6px; font-size: .85rem; margin-bottom: 8px;
      color: color-mix(in srgb, currentColor 65%, transparent); }
    .parse-progress mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .detail-container { padding: 1rem; max-width: min(var(--page-max-width), 96vw); margin: 0 auto; }
    .detail-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 1rem;
      gap: 1rem;
    }
    .header-info { display: flex; flex-direction: column; align-items: flex-start; gap: 6px; min-width: 0; }
    .header-info h2 { margin: 0; }
    .subtitle { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 14px; }
    .header-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }

    /* UI-Sweep z1 (z1-rk-rep-brett-gross): das Brett wächst mit der Fensterhöhe (400–640 px) statt fest 400 px. */
    .viewer-layout {
      --board-size: clamp(400px, calc(100vh - 300px), 640px);
      display: flex;
      flex-direction: row;
      gap: 1rem;
      align-items: flex-start;
    }
    .board-section {
      width: var(--board-size);
      flex-shrink: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
    }
    .board-section app-chess-board {
      display: block;
      width: var(--board-size);
    }
    .nav-buttons { display: flex; gap: 4px; }
    .cmt-variation-bar {
      display: flex; align-items: center; gap: 8px; margin-top: 6px;
      font-size: 0.85em; color: color-mix(in srgb, currentColor 70%, transparent);
    }
    .tree-board-bar { display: flex; align-items: center; gap: 8px; width: 100%; max-width: 400px; flex-wrap: wrap; }
    .tree-arrows-toggle.on { border-color: currentColor; }
    .tree-board-hint { font-size: 12px; color: color-mix(in srgb, currentColor 55%, transparent); }
    .filter-bar { display: flex; align-items: center; gap: 4px; width: 100%; max-width: var(--board-size, 400px); min-height: 40px; }
    .filter-moves { flex: 1; font-family: 'Roboto Mono', monospace; font-size: 13px;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .filter-hint { flex: 1; text-align: center; font-size: 12px;
      color: color-mix(in srgb, currentColor 55%, transparent); }
    .filter-count { font-size: 12px; color: color-mix(in srgb, currentColor 65%, transparent); }
    /* a1-rk-linienliste-scrollkasten: mindestens so hoch wie Brett + Hinweiszeile (vorher fest 440 px, am Laptop
       kürzer als das 468-px-Brett); am hohen Bildschirm wächst das Panel mit, statt die halbe Seite leer zu lassen. */
    .side-panel {
      flex: 1;
      min-width: 250px;
      border: 1px solid color-mix(in srgb, currentColor 15%, transparent);
      border-radius: 4px;
      height: max(calc(var(--board-size) + 56px), calc(100vh - 260px));
      overflow: hidden;
    }
    /* Weiterscroll-Hinweis ohne Skript: ein Schatten am unteren Rand (fest), den eine mitlaufende Abdeckung in
       Seitenfarbe am Listenende verdeckt — sichtbar also nur, solange unten noch etwas kommt. */
    .side-panel--fit {
      height: auto;
      max-height: max(calc(var(--board-size) + 56px), calc(100vh - 260px));
      overflow-y: auto;
      background:
        linear-gradient(to bottom, transparent, var(--mat-sys-surface) 70%) center bottom / 100% 32px no-repeat local,
        radial-gradient(farthest-side at 50% 100%, color-mix(in srgb, currentColor 30%, transparent), transparent)
          center bottom / 100% 14px no-repeat scroll;
    }
    /* a1-rk-repertoire-edit-breite: kein eigener 800-px-Deckel mehr — Ablage und Dateiliste nehmen die Kopfbreite
       (ab 900 px zweispaltig, siehe repertoire-edit). */

    @media (max-width: 899.98px) {
      .detail-header { flex-wrap: wrap; }
      /* Moduswahl (und in Baum/Lochfinder der Abfragen-Knopf) rechtsbündig in EINER Zeile — in allen Modi an derselben
         Stelle und gleich hoch (v1-rk-repertoire-abfragen-knopf-wandert, Nachrunde). */
      .header-actions { flex-wrap: nowrap; margin-left: auto; }
      .viewer-layout {
        flex-direction: column;
        align-items: center;
      }
      /* a1-rk-tablet-brett-400: Brett bis 640 px (Tablet), höchstens so hoch, dass darunter noch die Liste beginnt;
         am Handy wie bisher volle Breite. */
      /* d4-rk-repertoire-brett-winzig: Untergrenze 300 px — bei 640 px Fensterhöhe (Boox 200 %) ergab 100vh − 480px nur
         160 px. */
      .board-section { width: 100%; max-width: min(100%, 640px, max(300px, calc(100vh - 480px))); }
      .board-section app-chess-board { width: 100%; }
      .filter-bar { max-width: 100%; }
      .side-panel { width: 100%; min-width: 0; height: 400px; }
      /* a1-rk-linienliste-scrollkasten: einspaltig läuft die Liste mit der Seite — kein Kasten im Kasten. */
      .side-panel--fit { height: auto; max-height: none; overflow: visible; }
      /* a1-rk-holes-handy-brett-vor-werkzeug: Lochfinder ohne gewähltes Loch — Werkzeug vor dem leeren Brett. */
      .viewer-layout.holes-idle .side-panel { order: -1; }
    }
    /* Falz-Runde 16.10. (Baum am Tablet 73 %: 640-px-Brett über dem Baum): im Baum-Modus stehen Brett und Baum ab
       600 px nebeneinander — der Baum ist schmal (Zug, Prozent, Linienzahl) und braucht keine volle Breite (4.6 Regel 4). */
    @media (min-width: 600px) and (max-width: 899.98px) {
      .viewer-layout--tree { flex-direction: row; align-items: flex-start; }
      .viewer-layout--tree .board-section { width: min(56%, calc(100vh - 300px)); max-width: none; flex-shrink: 0; }
      .viewer-layout--tree .side-panel { flex: 1 1 0; width: auto; min-width: 0; height: max(400px, calc(100vh - 300px)); }
    }
    /* Niedrige Fenster (DESIGN 4.6, d4-rk-repertoire-brett-winzig): Brett ≈ Fensterhöhe − 300 px (Boox 480 × 640 →
       340 px), der Baum beginnt darunter; nie unter 300 px. */
    @media (max-width: 768px) and (max-height: 720px) {
      .board-section { max-width: min(100%, max(300px, calc(100vh - 300px))); }
    }
    /* Am Handy der Titel in EINER Zeile (ganzer Name im title-Attribut): ein zweizeiliger Repertoirename schob Brett
       und Baum um eine Zeile nach unten (Falz-Runde 16.10.). */
    @media (max-width: 599.98px) {
      .header-info { flex: 1 1 100%; }
      .header-info h2 { font-size: 1.15rem; line-height: 1.3; max-width: 100%;
        white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
      .detail-header { margin-bottom: .5rem; row-gap: .5rem; }
    }
    /* u1-rk-repertoire-brett: ab 2400 px die gemeinsame Breitbild-Breite (1 880 px) und ein Brett bis 1 040 px
       (höchstens Fensterhöhe) — vorher 640 px in 1 240 px, 64 % der Fläche leer. Die Seitenspalte behält ≈ 790 px. */
    @media (min-width: 2400px) {
      .detail-container { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
      .viewer-layout { --board-size: clamp(400px, calc(100vh - 300px), 1040px); }
      .side-panel { min-width: 480px; }
    }
    /* Abfragen ist am Handy ein Symbolknopf; der Name steht im aria-label. */
    .train-label { margin-left: 4px; }
    @media (max-width: 599.98px) {
      .train-label { display: none; }
      .train-btn { min-width: 48px; padding: 0 12px; }
      .train-btn mat-icon { margin: 0; }
    }
  `]
})
export class RepertoireDetailComponent implements OnInit, DoCheck {
  repertoire: RepertoireDetail | null = null;
  loading = true;
  mode: ViewMode = 'lines';
  /** Deep-Link-Ziel aus der Stellungssuche („Ansehen"): lineKey + Halbzug, einmalig nach dem Laden angewandt. */
  private focusLineKey: string | null = null;
  private readonly cdr = inject(ChangeDetectorRef);
  /** Lesestand eines großen Repertoires (Portionen, 0.712.0); `null` = fertig. */
  parseProgress: { done: number; total: number } | null = null;
  private focusPly: number | null = null;
  id!: number;

  /** Stellungs-Filter der Linien-Ansicht: auf dem Brett gespielte Züge (jeweils Stellung DANACH). */
  filterStack: { san: string; fen: string; lastMove: [string, string] }[] = [];
  /** Linien, die die aktuelle Filter-Stellung enthalten (nur gültig solange filterStack nicht leer). */
  private filteredLines: RepertoireLine[] = [];
  /** Je gameIndex der Halbzug (fens-Index) des Treffers — fürs Vorspulen beim Anwählen. */
  private filterPlyByGame = new Map<number, number>();

  /** Ausgangsstellung des Filterbretts = gemeinsame Startstellung der Linien (Endspiel-Repertoire: deren FEN);
   *  null = die Linien starten aus verschiedenen Stellungen, dann kein Filter (b1-rk-repertoire-filterbrett-fen-start). */
  filterRootFen: string | null = START_FEN;

  /** Brett spielbar = Linien-Ansicht ohne ausgewählte Linie (dann filtert es die Liste). */
  get filterBoardActive(): boolean {
    return this.mode === 'lines' && this.viewerService.selectedLineIndex < 0 && this.filterRootFen != null;
  }

  /** Brett-Vorschau eines angeklickten Kommentar-Zugs; gilt nur für die Linie + Stellung, in der geklickt wurde. */
  commentPreview: { fen: string; lastMove: [string, string]; line: number; move: number } | null = null;

  onCommentMovePreview(seg: { fen?: string; from?: string; to?: string }): void {
    if (!seg.fen || !seg.from || !seg.to) return;
    this.commentPreview = {
      fen: seg.fen, lastMove: [seg.from, seg.to],
      line: this.viewerService.selectedLineIndex, move: this.viewerService.currentMoveIndex,
    };
  }

  exitCommentPreview(): void { this.commentPreview = null; }

  /** Jeder Stellungs- oder Linienwechsel beendet die Vorschau (auch über Pfeiltasten/Zugliste). */
  ngDoCheck(): void {
    const p = this.commentPreview;
    if (p && (this.mode !== 'lines' || p.line !== this.viewerService.selectedLineIndex
      || p.move !== this.viewerService.currentMoveIndex)) {
      this.commentPreview = null;
    }
  }

  get boardFen(): string {
    if (this.commentPreview) return this.commentPreview.fen;
    if (this.mode === 'holes') return this.holeView?.fen ?? START_FEN;
    if (this.mode !== 'lines') return this.treeService.currentFen;
    if (this.viewerService.selectedLineIndex >= 0) return this.viewerService.currentFen;
    if (this.filterStack.length) return this.filterStack[this.filterStack.length - 1].fen;
    return this.filterRootFen ?? this.viewerService.lines[0]?.startFen ?? START_FEN;
  }

  get boardLastMove(): [string, string] | undefined {
    if (this.commentPreview) return this.commentPreview.lastMove;
    if (this.mode === 'holes') return this.holeView?.lastMove;
    if (this.mode !== 'lines') return this.treeService.lastMove;
    if (this.viewerService.selectedLineIndex >= 0) return this.viewerService.lastMove;
    return this.filterStack.length ? this.filterStack[this.filterStack.length - 1].lastMove : undefined;
  }

  /** Trainingsfarbe je Kapitel (Auto-Erkennung + eigene Festlegungen) — dieselbe Rechnung wie Trainer und Lochfinder. */
  private chapterColors = new Map<string, TrainColor>();

  /**
   * d3-rk-schwarz-repertoire-brett-nicht-gedreht: Schwarz unten, wenn die gewählte Linie zu einem Schwarz-Kapitel gehört —
   * ohne gewählte Linie, wenn ALLE Kapitel Schwarz sind (Schwarz-Repertoire). Wie im Trainer (`[orientation]`).
   */
  get boardFlipped(): boolean {
    const game = this.mode === 'lines' ? this.viewerService.selectedGame : null;
    if (game) return this.chapterColors.get((game.headers['Black'] || '').trim()) === 'b';
    const colors = [...this.chapterColors.values()];
    return colors.length > 0 && colors.every(c => c === 'b');
  }

  /** Nach dem Laden und nach jeder Farb-Festlegung im Linien-Panel neu rechnen. */
  refreshChapterColors(): void {
    this.chapterColors = chapterColorsOf(this.id, this.trainableGames);
    this.cdr.markForCheck();
  }

  /** Lochfinder: die angewählte Stellung (nach dem fehlenden Gegnerzug), sonst Grundstellung. */
  holeView: HoleBoardView | null = null;
  /** Lochfinder: Suche läuft oder hat ein Ergebnis — dann feste Panelhöhe (Trefferliste scrollt innen). */
  holesSearchActive = false;

  /** Panel so hoch wie sein Inhalt: Linienliste ohne gewählte Linie, Lochfinder vor der ersten Suche. */
  get sidePanelFits(): boolean {
    return (this.mode === 'lines' && this.viewerService.selectedLineIndex < 0)
      || (this.mode === 'holes' && !this.holesSearchActive);
  }

  /** Baum: Zugstatistik der aktuellen Stellung (wie oft wird welcher Zug dort gespielt) — dieselbe Quelle
   *  und Auswahl wie im Lochfinder und im Explorer des Analysebretts. Signale: HttpClient (fetch). */
  readonly treePopularity = signal<ExplorerPosition | null>(null);
  readonly treePopLoading = signal(false);
  /** i18n-Key eines Hinweises (Token fehlt, Lichess bremst, Fehler); null = keiner. */
  readonly treePopNote = signal<string | null>(null);
  private readonly treePopCache = new Map<string, ExplorerPosition>();
  private treePopSub: Subscription | null = null;

  /** Linien ohne Info-Linien — für die Farbe je Kapitel im Lochfinder (dieselbe Auswahl wie im Trainer). */
  trainableGames: ParsedGame[] = [];

  /** Rechts angezeigte Linien: bei aktivem Filter nur die Treffer, sonst alle. */
  get visibleLines(): RepertoireLine[] {
    if (!this.filterStack.length || this.viewerService.selectedLineIndex >= 0) return this.viewerService.lines;
    return this.filteredLines;
  }

  get filterMovesText(): string {
    return formatSansWithNumbers(this.filterStack.map(s => s.san), this.filterRootFen ?? undefined);
  }

  /** Baum: Pfeile an (gemerkt je Gerät); im Brett-Vollbild immer an. */
  treeArrowsOn = readRaw(localStore(), TREE_ARROWS_KEY) === '1';
  boardFullscreen = false;
  private arrowsCache: BoardArrow[] = [];

  toggleTreeArrows(): void {
    this.treeArrowsOn = !this.treeArrowsOn;
    writeRaw(localStore(), TREE_ARROWS_KEY, this.treeArrowsOn ? '1' : '0');
  }

  /** Pfeile fürs Brett — dieselbe Liste, solange sich nichts ändert (das Brett zeichnet sonst bei jedem Prüflauf neu). */
  get boardArrows(): BoardArrow[] {
    const want = this.mode === 'tree' && (this.treeArrowsOn || this.boardFullscreen)
      ? treeArrows(this.treeService.children.map(c => ({ san: c.san, from: c.node.from, to: c.node.to })), this.treePopularity())
      : [];
    if (!sameArrows(want, this.arrowsCache)) this.arrowsCache = want;
    return this.arrowsCache;
  }

  onBoardMove(move: UserBoardMove): void {
    if (this.mode === 'tree') this.onTreeBoardMove(move);
    else this.onFilterMove(move);
  }

  /** Baum: ein Zug auf dem Brett folgt der Fortsetzung mit diesem Zug; ein Zug, den das Repertoire hier nicht hat,
   *  springt zurück. */
  onTreeBoardMove(move: UserBoardMove): void {
    const san = canonSan(move.san);
    const child = this.treeService.children.find(c => canonSan(c.san) === san);
    if (!child) {
      this.board()?.resync();
      this.snackbar.quick(this.translate.instant('repertoire.tree.notInRepertoire', { san: move.san }));
      return;
    }
    this.treeService.selectChild(child.san);
    this.loadTreePopularity();
  }

  onFilterMove(move: UserBoardMove): void {
    this.filterStack.push({ san: move.san, fen: move.fen, lastMove: [move.from, move.to] });
    this.recomputeFilter();
  }

  undoFilterMove(): void {
    this.filterStack.pop();
    this.recomputeFilter();
  }

  resetFilter(): void {
    this.filterStack = [];
    this.recomputeFilter();
  }

  /** Linie aus der (ggf. gefilterten) Liste öffnen — bei aktivem Filter direkt zur Trefferstellung. */
  onLineSelected(index: number): void {
    this.viewerService.selectLine(index);
    if (!this.filterStack.length) return;
    const gameIndex = this.viewerService.lines[index]?.gameIndex ?? -1;
    const at = this.filterPlyByGame.get(gameIndex);
    if (at != null) this.viewerService.goToMove(at - 1);
  }

  /** Treffer einmal je Zug berechnen (nicht im Getter — der läuft in jedem CD-Zyklus). */
  private recomputeFilter(): void {
    const root = commonStartFen(this.viewerService.lines.map(l => l.startFen), START_FEN);
    // Andere Ausgangsstellung (frisch geladenes Repertoire) → alte Filterzüge passen nicht mehr.
    if (root == null || this.filterRootFen == null || normalizeFen(root) !== normalizeFen(this.filterRootFen)) this.filterStack = [];
    this.filterRootFen = root;
    if (!this.filterStack.length) {
      this.filteredLines = [];
      this.filterPlyByGame = new Map();
      return;
    }
    const fen = this.filterStack[this.filterStack.length - 1].fen;
    const res = findPositionInGames(this.viewerService.games, fen);
    this.filterPlyByGame = res.plyByGame;
    this.filteredLines = this.viewerService.lines.filter(l => res.gameIndices.has(l.gameIndex));
  }

  readonly board = viewChild(ChessBoardComponent);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private repertoireService: RepertoireService,
    public viewerService: RepertoireViewerService,
    public treeService: MoveTreeService,
    private dialog: MatDialog,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private explorer: RepertoireExplorerService,
  ) {}

  /**
   * Baum: die Beliebtheit der Züge in der aktuellen Stellung holen — für eigene wie gegnerische Züge
   * dieselbe Frage „wie oft wird dieser Zug hier gespielt?". Je Stellung und Auswahl einmal; eine
   * späte Antwort für eine schon verlassene Stellung wird verworfen.
   */
  loadTreePopularity(): void {
    if (this.mode !== 'tree') return;
    this.treePopSub?.unsubscribe();
    const fen = this.treeService.currentFen;
    this.treePopSub = this.explorer.effectiveSettings().pipe(
      switchMap(s => {
        const key = `${normalizeFen(fen)}|${selectionKey(s)}`;
        const hit = this.treePopCache.get(key);
        if (hit) return [{ r: hit, key }];
        this.treePopLoading.set(true);
        return this.explorer.position(fen, s).pipe(switchMap(r => [{ r, key }]));
      }),
    ).subscribe({
      next: ({ r, key }) => {
        this.treePopLoading.set(false);
        if (r.status === 'ok') this.treePopCache.set(key, r);
        this.treePopNote.set(r.status === 'tokenMissing' ? 'repertoire.tree.popToken'
          : r.status === 'tokenInvalid' ? 'repertoire.holes.tokenInvalid'
          : r.status === 'rateLimited' ? 'repertoire.tree.popRateLimited'
          : r.status === 'failed' ? 'repertoire.tree.popFailed'
          : null);
        this.treePopularity.set(r.status === 'ok' ? r : null);
      },
      error: () => {
        this.treePopLoading.set(false);
        this.treePopNote.set('repertoire.tree.popFailed');
        this.treePopularity.set(null);
      },
    });
  }

  /** Teilt eine einzelne Linie als öffentlichen Nur-Ansehen-Link: PGN bauen → API → Link-Dialog. */
  onShareLine(line: RepertoireLine): void {
    const game = this.viewerService.games[line.gameIndex];
    if (!game) return;
    const title = line.opening || line.chapter || `${line.white} vs ${line.black}`;
    const pgn = parsedGameToPgn(game, { title });
    this.repertoireService.shareLine(this.id, { pgn, title }).subscribe({
      next: res => {
        const url = `${location.origin}/l/${res.shareToken}`;
        this.dialog.open(ShareLineDialogComponent, { data: { url, lineTitle: title } });
      },
      error: () => this.snackbar.info(this.translate.instant('repertoire.shareLine.error')),
    });
  }

  /** Lädt eine einzelne Linie als PGN herunter: bevorzugt ihr Originaltext aus dem Repertoire (mit Varianten
   *  und Kommentaren), sonst aus der geparsten Linie gebaut — jeweils ohne die internen Marker [%alt]/[%info]. */
  onDownloadLine(line: RepertoireLine): void {
    const game = this.viewerService.games[line.gameIndex];
    if (!game) return;
    const title = line.opening || line.chapter || `${line.white} vs ${line.black}`;
    const raw = this.viewerService.rawGames[line.gameIndex];
    const pgn = repertoireDownloadPgn(raw ? raw.trimEnd() + '\n' : parsedGameToPgn(game, { title }));
    const suffix = [line.chapter, line.white].filter(s => s && s !== '?').join(' ') || title;
    downloadBlob(new Blob([pgn], { type: 'application/x-chess-pgn' }), pgnFileName(this.repertoire?.name, suffix));
  }

  /**
   * „Neues Repertoire aus Auswahl" (Wunsch 2026-10-19): Kapitel und/oder Linien wählen (markierte vorgewählt) → eigenes
   * Repertoire mit den ORIGINALabschnitten (Varianten, Kommentare, [FEN] bleiben), Art und Extension-Häkchen wie die
   * Quelle, privat. Die Kapitelfarben der Quelle (auch automatisch erkannte) gelten im neuen Repertoire weiter.
   */
  onCreateSubset(markedKeys: string[]): void {
    if (!this.repertoire || this.parseProgress) return;
    const source = this.repertoire;
    const lines = this.viewerService.lines;
    const ref = this.dialog.open<CreateSubsetDialogComponent, CreateSubsetDialogData, CreateSubsetDialogResult>(
      CreateSubsetDialogComponent,
      { data: { repertoireName: source.name, lines, markedKeys }, width: '640px', maxWidth: '96vw', autoFocus: false });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      const { pgn, count } = subsetPgn(this.viewerService.rawGames, result.gameIndices);
      if (!count) return;
      const colors = new Map(this.chapterColors);
      const chapters = chaptersOf(lines, new Set(result.gameIndices));
      const description = this.translate.instant('repertoire.subset.description', { source: source.name, n: count });
      this.repertoireService.create({
        name: result.name, description: description.slice(0, 1000), isPublic: false,
        kind: source.kind, useForExtension: source.useForExtension,
      }).pipe(
        switchMap(created => {
          const newId = (created as { id: number }).id;
          const form = new FormData();
          form.append('file', new File([pgn], 'auswahl.pgn', { type: 'application/x-chess-pgn' }));
          return this.repertoireService.uploadFile(newId, form).pipe(map(() => newId));
        }),
      ).subscribe({
        next: newId => {
          for (const ch of chapters) {
            const c = colors.get(ch);
            if (c) setChapterColorOverride(newId, ch, c);
          }
          this.snackbar.show(this.translate.instant('repertoire.subset.created', { name: result.name, n: count }),
            { action: 'repertoire.subset.open', duration: 8000 })
            .onAction().subscribe(() => this.router.navigate(['/repertoires', newId]));
        },
        error: () => this.snackbar.info(this.translate.instant('repertoire.subset.failed')),
      });
    });
  }

  ngOnInit(): void {
    this.id = +this.route.snapshot.paramMap.get('id')!;
    const modeParam = this.route.snapshot.queryParamMap.get('mode');
    if (modeParam === 'tree' || modeParam === 'holes' || modeParam === 'edit') {
      this.mode = modeParam;
    }
    // Deep-Link „Ansehen" aus der Stellungssuche: ?line=<lineKey>&ply=<n> → nach dem Laden auf genau
    // diese Linie springen (Linien-Ansicht) und bis zur gesuchten Stellung vorspulen.
    this.focusLineKey = this.route.snapshot.queryParamMap.get('line');
    const plyParam = this.route.snapshot.queryParamMap.get('ply');
    this.focusPly = plyParam != null ? parseInt(plyParam, 10) : null;
    if (this.focusLineKey && !modeParam) this.mode = 'lines';
    // Frisch angelegtes Trainings-Repertoire („Show me lines to train", 2026-10-07): ?trainColor=w|b legt die Trainingsfarbe
    // aller seiner Kapitel fest — die Auto-Erkennung könnte an der Auswahl der Linien kippen.
    const tc = this.route.snapshot.queryParamMap.get('trainColor');
    this.pendingTrainColor = tc === 'w' || tc === 'b' ? tc : null;
    this.loadRepertoire();
  }

  setMode(mode: ViewMode): void {
    // Der Lochfinder wird beim Moduswechsel neu aufgebaut — sein Ergebnis und das gewählte Loch gehen mit.
    if (mode !== this.mode) { this.holeView = null; this.holesSearchActive = false; }
    this.mode = mode;
    if (mode === 'tree') this.loadTreePopularity();
    this.router.navigate([], {
      queryParams: { mode },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onFileChanged(): void {
    this.loadRepertoire();
    this.loadCombinedPgn();
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    if (this.mode !== 'lines' || this.viewerService.selectedLineIndex < 0) return;
    if (!isBoardHotkey(event)) return;
    switch (event.key) {
      case 'ArrowLeft':
        event.preventDefault();
        this.viewerService.goBack();
        break;
      case 'ArrowRight':
        event.preventDefault();
        this.viewerService.goForward();
        break;
      case 'Home':
        event.preventDefault();
        this.viewerService.goToStart();
        break;
      case 'End':
        event.preventDefault();
        this.viewerService.goToEnd();
        break;
    }
  }

  private loadRepertoire(): void {
    this.loading = true;
    this.repertoireService.getDetail<RepertoireDetail>(this.id).subscribe({
      next: (r) => {
        this.repertoire = r;
        // Geteiltes (nicht eigenes) Repertoire: Bearbeiten-Modus ist nicht erlaubt → auf Linien zurück.
        if (r.isOwner === false && this.mode === 'edit') this.mode = 'lines';
        this.loading = false;
        this.loadCombinedPgn();
      },
      error: () => { this.loading = false; }
    });
  }

  private loadCombinedPgn(): void {
    this.repertoireService.getPgnText(this.id).subscribe({
      next: async (pgn) => {
        // In Portionen (0.712.0): ein großes Chessable-Repertoire braucht am Stück Sekunden; die Seite bleibt bedienbar
        // und zeigt den Fortschritt. Vorher las der Browser nur die ersten 2 MB/500 Partien.
        if (exceedsSyncLimits(pgn)) {
          this.parseProgress = { done: 0, total: 0 };
          this.cdr.markForCheck();
          await this.viewerService.loadPgnAsync(pgn, (done, total) => {
            this.parseProgress = { done, total };
            this.cdr.markForCheck();
          });
          this.parseProgress = null;
        } else {
          this.viewerService.loadPgn(pgn);
        }
        this.treeService.buildTreeFromGames(this.viewerService.games);
        this.cdr.markForCheck();
        this.trainableGames = this.viewerService.games.filter((_, i) => !isInfoLineGame(this.viewerService.rawGames[i]));
        this.applyTrainColor();
        this.refreshChapterColors();
        this.loadTreePopularity();   // direkt im Baum geöffnet (?mode=tree)
        this.applyFocusLine();
        this.recomputeFilter();   // Filter-Treffer gegen den frischen Linien-Stand
      },
      error: () => {
        this.viewerService.loadPgn('');
        this.treeService.buildTree('');
        this.recomputeFilter();
      }
    });
  }

  /** Trainingsfarbe aus ?trainColor= einmalig für jedes Kapitel festlegen (nur im eigenen Repertoire). */
  private pendingTrainColor: TrainColor | null = null;

  private applyTrainColor(): void {
    const color = this.pendingTrainColor;
    this.pendingTrainColor = null;
    if (!color || this.repertoire?.isOwner === false) return;
    for (const chapter of new Set(this.trainableGames.map(g => (g.headers['Black'] || '').trim())))
      setChapterColorOverride(this.id, chapter, color);
  }

  /** Springt einmalig auf die per Deep-Link (?line=&ply=) gewählte Linie/Stellung (Stellungssuche „Ansehen"). */
  private applyFocusLine(): void {
    if (!this.focusLineKey) return;
    const key = this.focusLineKey;
    const ply = this.focusPly;
    this.focusLineKey = null; // nur einmal anwenden
    this.focusPly = null;
    const index = this.viewerService.lines.findIndex(l => l.lineKey === key);
    if (index < 0) return;
    this.mode = 'lines';
    this.viewerService.selectLine(index);
    if (ply != null && ply > 0) this.viewerService.goToMove(ply - 1);
  }
}

/** Auswahl als Schlüssel — Meister haben keine Elo-/Tempo-Filter. */
function selectionKey(s: ExplorerSettings): string {
  return s.database === 'masters'
    ? `${s.source}|masters`
    : `${s.source}|lichess|${s.ratings.join(',')}|${s.speeds.join(',')}`;
}
