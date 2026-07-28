import { Component, HostListener, OnDestroy, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Color, Key } from 'chessground/types';
import { Subscription } from 'rxjs';
import { PuzzleBoardComponent } from '../../puzzles/puzzle-board.component';
import { applyUci, tryFreeMove, tryLoadFen } from '../../puzzles/puzzle-move.util';
import { PreferencesService } from '../../../core/preferences.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { CalcLinesComponent } from './calc-lines.component';
import {
  CALC_EVALS, CALC_GLYPHS, CalcEval, CalcGlyph, CalcNode, CalcTree,
  addMove, createTree, deserializeTree, findNode, isEmpty, lines, pathToRoot,
  removeLine, removeSubtree, serializeTree, setComment, setEvaluation, setGlyph, whiteToMove,
} from './calc-tree.util';
import { CalcBook, CalcPosition, CalcPositionListItem, CalculationService } from './calculation.service';

/** Stellungen eines Kapitels für die Sprungliste. */
interface CalcPositionGroup {
  chapter: string | null;
  items: CalcPositionListItem[];
}

/**
 * Kalkulations-Modus für Kalkulationsbücher (`Book.IsCalculation`): der Nutzer sieht NUR die
 * Stellung (FEN + optionaler Aufgabentext) — es gibt keine Lösung. Das Brett bleibt STRIKT
 * eingefroren: Klicks werden als Züge erfasst (für beide Seiten), verändern das Brett aber nicht.
 * Sichtbar wird die Rechnung ausschließlich als Notation im Linien-Panel.
 *
 * Bedienung: Zug klicken = anhängen · (+) = neue Linie ab der Ausgangsstellung · ←/→ = innerhalb
 * der Linie navigieren · ↑/↓ = Linie wechseln · Zug mitten in einer Linie auswählen und einen
 * anderen Zug klicken = Abzweigung · Symbolleiste = Zug-/Stellungsbewertung · Kommentar je Zug/Linie.
 * Gespeichert wird pro Nutzer und Stellung serverseitig (automatisch, entprellt).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-calculation',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatIconModule, MatFormFieldModule,
    MatSelectModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe, RouterLink,
    PuzzleBoardComponent, CalcLinesComponent,
  ],
  templateUrl: './calculation.component.html',
  styleUrls: ['./calculation.component.scss'],
})
export class CalculationComponent implements OnInit, OnDestroy {
  bookId!: number;
  book: CalcBook | null = null;
  positions: CalcPositionListItem[] = [];
  groups: CalcPositionGroup[] = [];
  index = 0;

  position: CalcPosition | null = null;
  loading = true;
  loadError = false;

  /** Ausgangsstellung (nach dem Vorlauf) — das Brett zeigt IMMER nur diese. */
  startFen = '';
  /** FEN am aktuellen Cursor — nur für die Legalitätsprüfung der Klicks, nie sichtbar. */
  cursorFen = '';
  /** Stellung ist für chess.js nicht ladbar (Chessable-Muster-Diagramm) → keine Zug-Eingabe. */
  illegalPosition = false;

  tree: CalcTree = createTree('');
  cursorId = 0;
  /** Linie, auf der man sich „bewegt" (für →/↑/↓). */
  private activeLeafId = 0;
  /** Zähler für das Neuzeichnen des Panels nach In-place-Änderungen am Baum. */
  revision = 0;

  orientation: Color = 'white';
  boardTheme = 'brown';
  pieceSet = 'cburnett';
  readonly noDests = new Map<Key, Key[]>();

  readonly glyphs = CALC_GLYPHS;
  readonly evals = CALC_EVALS;

  /** Kommentar-Entwurf zum ausgewählten Zug. */
  cursorComment = '';

  saving = false;
  savedAt: Date | null = null;
  private dirty = false;
  private hadStoredTree = false;
  private saveTimer?: ReturnType<typeof setTimeout>;
  private static readonly AUTOSAVE_MS = 1200;

  private subs = new Subscription();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private api: CalculationService,
    private prefs: PreferencesService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.bookId = Number(this.route.snapshot.paramMap.get('bookId'));
    const requested = Number(this.route.snapshot.queryParamMap.get('pos')) || null;
    this.loadBook(requested);
  }

  ngOnDestroy(): void {
    this.clearSaveTimer();
    this.flushSave();
    this.subs.unsubscribe();
  }

  // ===== Laden ==============================================================

  private loadBook(requestedPositionId: number | null): void {
    this.loading = true;
    this.loadError = false;
    this.subs.add(this.api.getBook(this.bookId).subscribe({
      next: book => {
        this.book = book;
        this.positions = book.positions;
        this.groups = this.groupPositions(book.positions);
        if (this.positions.length === 0) { this.loading = false; return; }
        const wanted = requestedPositionId != null
          ? this.positions.findIndex(p => p.id === requestedPositionId)
          : -1;
        // Ohne Deep-Link bei der ersten noch unbearbeiteten Stellung einsteigen.
        const firstOpen = this.positions.findIndex(p => !p.hasTree);
        this.index = wanted >= 0 ? wanted : (firstOpen >= 0 ? firstOpen : 0);
        this.loadPosition(this.positions[this.index].id);
      },
      error: () => { this.loading = false; this.loadError = true; },
    }));
  }

  private loadPosition(bookPuzzleId: number): void {
    this.loading = true;
    this.subs.add(this.api.getPosition(bookPuzzleId).subscribe({
      next: pos => {
        this.position = pos;
        this.applyPosition(pos);
        this.loading = false;
      },
      error: () => { this.loading = false; this.loadError = true; },
    }));
  }

  private applyPosition(pos: CalcPosition): void {
    this.startFen = this.buildStartFen(pos);
    this.orientation = whiteToMove(this.startFen) ? 'white' : 'black';
    const stored = deserializeTree(pos.treeJson, this.startFen);
    this.hadStoredTree = !!pos.treeJson;
    this.tree = stored ?? createTree(this.startFen);
    this.cursorId = this.tree.rootId;
    this.activeLeafId = this.tree.rootId;
    this.cursorFen = this.startFen;
    this.cursorComment = '';
    this.dirty = false;
    this.savedAt = pos.treeUpdatedAt ? new Date(pos.treeUpdatedAt) : null;
    this.revision++;
  }

  /**
   * Ausgangsstellung: Header-FEN plus den (nicht lösungsrelevanten) Vorlauf `setupMoves`.
   * Scheitert das — illegale Muster-FEN oder unspielbarer Zug —, bleibt die Header-FEN stehen und
   * die Zug-Eingabe wird gesperrt (das Brett selbst rendert auch illegale Stellungen).
   */
  private buildStartFen(pos: CalcPosition): string {
    this.illegalPosition = false;
    const chess = tryLoadFen(pos.fen);
    if (!chess) { this.illegalPosition = true; return pos.fen; }
    const setup = (pos.setupMoves || '').split(' ').filter(m => m.length >= 4);
    for (const uci of setup) {
      try { applyUci(chess, uci); }
      catch { this.illegalPosition = true; return pos.fen; }
    }
    return chess.fen();
  }

  private groupPositions(positions: CalcPositionListItem[]): CalcPositionGroup[] {
    const out: CalcPositionGroup[] = [];
    for (const p of positions) {
      const chapter = p.chapter?.trim() ? p.chapter : null;
      const last = out.at(-1);
      if (last && last.chapter === chapter) last.items.push(p);
      else out.push({ chapter, items: [p] });
    }
    return out;
  }

  // ===== Zug-Eingabe (Brett bleibt eingefroren) =============================

  onMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (this.illegalPosition) return;
    const node = findNode(this.tree, this.cursorId);
    if (!node) return;
    const chess = tryLoadFen(node.fen);
    if (!chess) return;
    const move = tryFreeMove(chess, event.orig, event.dest, event.promotion);
    if (!move) return;                          // illegal → stillschweigend ignorieren

    const added = addMove(this.tree, this.cursorId, {
      san: move.san,
      uci: move.from + move.to + (move.promotion ?? ''),
      fen: chess.fen(),
    });
    this.setCursor(added.id);
    this.markDirty();
  }

  // ===== Navigation im Baum =================================================

  setCursor(nodeId: number): void {
    const node = findNode(this.tree, nodeId);
    if (!node) return;
    this.cursorId = node.id;
    this.cursorFen = node.fen || this.startFen;
    this.cursorComment = node.comment ?? '';
    // „Aktive Linie" mitziehen: liegt der Cursor nicht mehr auf ihr, die erste Fortsetzung nehmen.
    const onActiveLine = pathToRoot(this.tree, this.activeLeafId).some(n => n.id === node.id);
    if (!onActiveLine) this.activeLeafId = this.leafUnder(node.id);
  }

  /** Neue Linie ab der Ausgangsstellung: (+) setzt den Cursor auf die Wurzel. */
  startNewLine(): void {
    this.setCursor(this.tree.rootId);
  }

  goBack(): void {
    const node = findNode(this.tree, this.cursorId);
    if (node?.parentId != null) this.setCursor(node.parentId);
  }

  goForward(): void {
    const node = findNode(this.tree, this.cursorId);
    if (!node || node.childIds.length === 0) return;
    const path = pathToRoot(this.tree, this.activeLeafId).map(n => n.id);
    this.setCursor(node.childIds.find(id => path.includes(id)) ?? node.childIds[0]);
  }

  /** Linie wechseln (↑/↓): Cursor auf das Blatt der vorherigen/nächsten Linie. */
  switchLine(delta: number): void {
    const all = lines(this.tree);
    if (all.length === 0) return;
    const current = all.findIndex(l => l.leafId === this.activeLeafId);
    const next = ((current < 0 ? 0 : current) + delta + all.length) % all.length;
    this.activeLeafId = all[next].leafId;
    this.setCursor(all[next].leafId);
  }

  private leafUnder(nodeId: number): number {
    let node = findNode(this.tree, nodeId);
    while (node && node.childIds.length > 0) node = findNode(this.tree, node.childIds[0]);
    return node?.id ?? nodeId;
  }

  // ===== Bearbeiten =========================================================

  /** Ausgewählten Zug samt Fortsetzung löschen (= „Zug zurück", wenn er der letzte ist). */
  deleteFromCursor(): void {
    if (this.cursorId === this.tree.rootId) return;
    const parentId = removeSubtree(this.tree, this.cursorId);
    this.activeLeafId = this.leafUnder(parentId);
    this.setCursor(parentId);
    this.markDirty();
  }

  deleteLine(leafId: number): void {
    const cursor = removeLine(this.tree, leafId);
    this.activeLeafId = this.leafUnder(cursor);
    this.setCursor(cursor);
    this.markDirty();
  }

  applyGlyph(glyph: CalcGlyph): void {
    if (this.cursorId === this.tree.rootId) return;
    setGlyph(this.tree, this.cursorId, glyph);
    this.markDirty();
  }

  applyEval(evaluation: CalcEval): void {
    if (this.cursorId === this.tree.rootId) return;
    setEvaluation(this.tree, this.cursorId, evaluation);
    this.markDirty();
  }

  clearAnnotations(): void {
    if (this.cursorId === this.tree.rootId) return;
    setGlyph(this.tree, this.cursorId, undefined);
    setEvaluation(this.tree, this.cursorId, undefined);
    this.markDirty();
  }

  saveCursorComment(): void {
    setComment(this.tree, this.cursorId, this.cursorComment);
    this.markDirty();
  }

  onLineComment(event: { nodeId: number; text: string }): void {
    setComment(this.tree, event.nodeId, event.text);
    if (event.nodeId === this.cursorId) this.cursorComment = event.text.trim();
    this.markDirty();
  }

  flipBoard(): void {
    this.orientation = this.orientation === 'white' ? 'black' : 'white';
  }

  // ===== Anzeige-Helfer =====================================================

  get cursorNode(): CalcNode | undefined { return findNode(this.tree, this.cursorId); }
  get atStart(): boolean { return this.cursorId === this.tree.rootId; }
  get lineCount(): number { return lines(this.tree).length; }
  get doneCount(): number { return this.positions.filter(p => p.hasTree).length; }
  get whiteToMoveAtCursor(): boolean { return whiteToMove(this.cursorFen || this.startFen); }

  /** Notation des Pfades zum Cursor („wo stehe ich") — der einzige Ort, an dem der Vorlauf sichtbar ist. */
  get cursorPathSan(): string {
    return pathToRoot(this.tree, this.cursorId).slice(1).map(n => n.san).join(' ');
  }

  get currentPositionLabel(): string {
    const pos = this.position;
    if (!pos) return '';
    return pos.title?.trim() ? pos.title : `#${pos.round}`;
  }

  positionLabel(item: CalcPositionListItem): string {
    return item.title?.trim() ? item.title : `#${item.round}`;
  }

  // ===== Stellungs-Navigation ==============================================

  hasPrev(): boolean { return this.index > 0; }
  hasNext(): boolean { return this.index < this.positions.length - 1; }

  prevPosition(): void { if (this.hasPrev()) this.goToIndex(this.index - 1); }
  nextPosition(): void { if (this.hasNext()) this.goToIndex(this.index + 1); }

  jumpToPosition(bookPuzzleId: number): void {
    const idx = this.positions.findIndex(p => p.id === bookPuzzleId);
    if (idx >= 0 && idx !== this.index) this.goToIndex(idx);
  }

  private goToIndex(index: number): void {
    this.clearSaveTimer();
    this.flushSave();
    this.index = index;
    const id = this.positions[index].id;
    this.router.navigate([], {
      relativeTo: this.route, queryParams: { pos: id }, queryParamsHandling: 'merge', replaceUrl: true,
    });
    this.loadPosition(id);
  }

  // ===== Speichern ==========================================================

  private markDirty(): void {
    this.revision++;
    this.dirty = true;
    this.clearSaveTimer();
    this.saveTimer = setTimeout(() => { this.saveTimer = undefined; this.flushSave(); },
      CalculationComponent.AUTOSAVE_MS);
  }

  /** Speichert sofort (Autosave-Timer, Stellungswechsel, Verlassen der Seite). */
  flushSave(): void {
    if (!this.dirty || !this.position) return;
    const bookPuzzleId = this.position.id;
    this.dirty = false;

    // Leerer Baum: gespeicherten Stand verwerfen (nicht „{}" ablegen).
    if (isEmpty(this.tree)) {
      this.markPositionDone(bookPuzzleId, false);
      if (!this.hadStoredTree) return;
      this.hadStoredTree = false;
      this.api.deleteTree(bookPuzzleId).subscribe({
        next: () => { this.savedAt = null; },
        error: () => this.reportSaveError(),
      });
      return;
    }

    this.saving = true;
    this.api.saveTree(bookPuzzleId, serializeTree(this.tree)).subscribe({
      next: res => {
        this.saving = false;
        this.hadStoredTree = true;
        this.savedAt = new Date(res.updatedAt);
        this.markPositionDone(bookPuzzleId, true);
      },
      error: () => { this.saving = false; this.reportSaveError(); },
    });
  }

  private markPositionDone(bookPuzzleId: number, done: boolean): void {
    const item = this.positions.find(p => p.id === bookPuzzleId);
    if (item) item.hasTree = done;
  }

  private reportSaveError(): void {
    this.dirty = true;                                  // beim nächsten Anstoß erneut versuchen
    this.snackbar.warn(this.translate.instant('calc.saveFailed'));
  }

  private clearSaveTimer(): void {
    if (this.saveTimer) { clearTimeout(this.saveTimer); this.saveTimer = undefined; }
  }

  // ===== Tastatur ===========================================================

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey) return;
    const target = event.target as HTMLElement | null;
    const tag = target?.tagName?.toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || target?.isContentEditable) return;

    switch (event.key) {
      case 'ArrowLeft': this.goBack(); break;
      case 'ArrowRight': this.goForward(); break;
      case 'ArrowUp': this.switchLine(-1); break;
      case 'ArrowDown': this.switchLine(1); break;
      default: return;
    }
    event.preventDefault();
  }
}
