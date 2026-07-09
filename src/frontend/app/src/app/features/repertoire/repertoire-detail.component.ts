import { Component, HostListener, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { RepertoireService } from '../../core/repertoire.service';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { MatDialog } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { RepertoireLinesComponent } from './repertoire-lines.component';
import { RepertoireTreeComponent } from './repertoire-tree.component';
import { RepertoireEditComponent } from './repertoire-edit.component';
import { RepertoireViewerService, RepertoireLine } from './repertoire-viewer.service';
import { parsedGameToPgn } from './repertoire-line-pgn.util';
import { ShareLineDialogComponent } from './share-line-dialog.component';
import { MoveTreeService } from './move-tree.service';
import { RepertoireDetail } from '../../core/models';

type ViewMode = 'lines' | 'tree' | 'edit';

@Component({
  selector: 'app-repertoire-detail',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatButtonToggleModule,
    TranslateModule, LoadingSpinnerComponent, ChessBoardComponent,
    RepertoireLinesComponent, RepertoireTreeComponent, RepertoireEditComponent,
  ],
  // Komponenten-bezogen (nicht providedIn:'root') — jede Instanz hat ihren eigenen
  // Viewer-/Tree-Zustand; per DI statt `new` für Testbarkeit.
  providers: [RepertoireViewerService, MoveTreeService],
  template: `
    @if (loading) {
      <app-loading-spinner />
    } @else if (repertoire) {
      <div class="detail-container">
        <div class="detail-header">
          <div class="header-info">
            <h2>{{ repertoire.name }}</h2>
            <span class="subtitle">{{ repertoire.description || ('repertoire.detail.noDescription' | translate) }}</span>
            <a mat-raised-button color="primary" class="train-btn" [routerLink]="['/repertoires', id, 'train']">
              <mat-icon>fitness_center</mat-icon> {{ 'repertoireTrainer.modeQuiz' | translate }}
            </a>
          </div>
          <mat-button-toggle-group [value]="mode" (change)="setMode($event.value)" appearance="standard">
            <mat-button-toggle value="lines" [attr.aria-label]="'repertoire.detail.modeLines' | translate" [attr.title]="'repertoire.detail.modeLines' | translate">
              <mat-icon>list</mat-icon>
            </mat-button-toggle>
            <mat-button-toggle value="tree" [attr.aria-label]="'repertoire.detail.modeTree' | translate" [attr.title]="'repertoire.detail.modeTree' | translate">
              <mat-icon>account_tree</mat-icon>
            </mat-button-toggle>
            @if (repertoire.isOwner !== false) {
              <mat-button-toggle value="edit" [attr.aria-label]="'repertoire.detail.modeEdit' | translate" [attr.title]="'repertoire.detail.modeEdit' | translate">
                <mat-icon>edit</mat-icon>
              </mat-button-toggle>
            }
          </mat-button-toggle-group>
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
          <div class="viewer-layout">
            <div class="board-section">
              <app-chess-board
                [fen]="mode === 'lines' ? viewerService.currentFen : treeService.currentFen"
                [lastMove]="mode === 'lines' ? viewerService.lastMove : treeService.lastMove" />
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
              }
            </div>
            <div class="side-panel">
              @if (mode === 'lines') {
                <app-repertoire-lines
                  [lines]="viewerService.lines"
                  [selectedIndex]="viewerService.selectedLineIndex"
                  [moves]="viewerService.currentMoves"
                  [currentMoveIndex]="viewerService.currentMoveIndex"
                  [comments]="viewerService.currentComments"
                  [repertoireId]="id"
                  (lineSelected)="viewerService.selectLine($event)"
                  (lineDeselected)="viewerService.deselectLine()"
                  (moveClicked)="viewerService.goToMove($event)"
                  (shareLine)="onShareLine($event)" />
              } @else if (mode === 'tree') {
                <app-repertoire-tree
                  [children]="treeService.children"
                  [breadcrumbs]="treeService.breadcrumbs"
                  (nodeSelected)="treeService.selectChild($event)"
                  (goUp)="treeService.goUp()"
                  (goToRoot)="treeService.goToRoot()"
                  (goToDepth)="treeService.goToDepth($event)" />
              }
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .detail-container { padding: 1rem; max-width: 1200px; margin: 0 auto; }
    .detail-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 1rem;
      gap: 1rem;
    }
    .header-info h2 { margin: 0; }
    .subtitle { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 14px; }

    .viewer-layout {
      display: flex;
      flex-direction: row;
      gap: 1rem;
      align-items: flex-start;
    }
    .board-section {
      width: 400px;
      flex-shrink: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
    }
    .board-section app-chess-board {
      display: block;
      width: 400px;
    }
    .nav-buttons { display: flex; gap: 4px; }
    .side-panel {
      flex: 1;
      min-width: 250px;
      border: 1px solid #e0e0e0;
      border-radius: 4px;
      height: 440px;
      overflow: hidden;
    }
    .edit-panel {
      max-width: 800px;
    }

    @media (max-width: 900px) {
      .detail-header { flex-wrap: wrap; }
      .viewer-layout {
        flex-direction: column;
        align-items: center;
      }
      .board-section { width: 100%; max-width: 400px; }
      .board-section app-chess-board { width: 100%; }
      .side-panel { width: 100%; min-width: 0; height: 400px; }
    }
  `]
})
export class RepertoireDetailComponent implements OnInit {
  repertoire: RepertoireDetail | null = null;
  loading = true;
  mode: ViewMode = 'lines';
  id!: number;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private repertoireService: RepertoireService,
    public viewerService: RepertoireViewerService,
    public treeService: MoveTreeService,
    private dialog: MatDialog,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

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

  ngOnInit(): void {
    this.id = +this.route.snapshot.paramMap.get('id')!;
    const modeParam = this.route.snapshot.queryParamMap.get('mode');
    if (modeParam === 'tree' || modeParam === 'edit') {
      this.mode = modeParam;
    }
    this.loadRepertoire();
  }

  setMode(mode: ViewMode): void {
    this.mode = mode;
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
      next: (pgn) => {
        this.viewerService.loadPgn(pgn);
        this.treeService.buildTree(pgn);
      },
      error: () => {
        this.viewerService.loadPgn('');
        this.treeService.buildTree('');
      }
    });
  }
}
