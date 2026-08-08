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
import { VisibilityStopwatch } from '../../puzzles/visibility-stopwatch';
import { PreferencesService } from '../../../core/preferences.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { CalcLinesComponent } from './calc-lines.component';
import {
  CALC_EVALS, CALC_GLYPHS, CalcEval, CalcGlyph, CalcNode, CalcTree, evalNameKey, glyphNameKey,
  addMove, createTree, deserializeTree, findNode, isEmpty, lines, pathToRoot,
  removeLine, removeSubtree, rootNode, serializeTree, setComment, setEvaluation, setGlyph,
  whiteToMove,
} from './calc-tree.util';
import {
  CALC_GRADE_OPTIONS, CALC_MAX_POINTS_PER_POSITION, CalcGrade, CalcReview, CalcReviewPatch,
  applyReviewPatch, emptyReview, formatScore, formatSeconds, gradePoints, isNoopPatch, maxPoints,
  mergeReviewPatch, newSecondsToken, normalizeGrade, sumPoints, sumSeconds,
} from './calc-review.util';
import {
  CalcBook, CalcPosition, CalcPositionListItem, CalcReviewSaved, CalculationService,
} from './calculation.service';

/** Stellungen eines Kapitels für die Sprungliste — samt der Kapitel-Summen. */
interface CalcPositionGroup {
  chapter: string | null;
  items: CalcPositionListItem[];
  /** Erreichte Punkte des Kapitels. */
  points: number;
  /** Erreichbare Punkte des Kapitels — jede Summe wird MIT ihrem Maximum genannt. */
  maxPoints: number;
  /** Summe der Rechenzeit des Kapitels (Sekunden). */
  seconds: number;
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

  // ===== Kapitel-Timer =====================================================
  // Kumuliert die Rechenzeit JE KAPITEL (nicht je Stellung): beim Stellungswechsel innerhalb
  // desselben Kapitels läuft derselbe Zähler weiter, beim Kapitelwechsel wird der Topf des
  // neuen Kapitels geladen. Persistiert je Gerät in localStorage (`rookhub_calc_timer_<bookId>`,
  // Map Kapitelname→Sekunden) — bewusst ohne Server-Anteil: der Timer ist ein Trainingswerkzeug,
  // kein Wertungsbestandteil.
  timerRunning = false;
  timerSeconds = 0;
  /** Kapitel-Schlüssel des laufenden Zählers ('' = „ohne Kapitel"); null = noch nicht geladen. */
  private timerChapterKey: string | null = null;
  private timerHandle?: ReturnType<typeof setInterval>;
  readonly noDests = new Map<Key, Key[]>();

  // ===== Festlegung / Rechenzeit / Bewertung je STELLUNG ====================
  // Anders als der Kapitel-Timer oben gehören diese drei Werte dem Server (eigene Spalten,
  // auswertbar) und hängen an der Zeile (UserId, BookPuzzleId).

  /** Stand der ANGEZEIGTEN Stellung (Spiegel der Sprunglisten-Zeile). */
  review: CalcReview = emptyReview();
  /** Die fünf Stufen der Selbstbewertung in Anzeige-Reihenfolge. */
  readonly gradeOptions = CALC_GRADE_OPTIONS;
  /** Höchstpunktzahl einer einzelnen Stellung (für „2 / 4"). */
  readonly maxPointsPerPosition = CALC_MAX_POINTS_PER_POSITION;
  /** Erreichte/erreichbare Punkte des ganzen Kurses. */
  totalPoints = 0;
  totalMaxPoints = 0;
  /** Sekunden der laufenden Sitzung an dieser Stellung (nur Anzeige; gesendet wird das Delta). */
  liveSeconds = 0;

  /** Misst die AKTIVE Zeit an der Stellung (pausiert, wenn der Tab weg ist). */
  private readonly watch = new VisibilityStopwatch();
  /** Stellung, zu der die laufende Uhr gehört; null = keine Uhr. */
  private watchPositionId: number | null = null;
  private liveHandle?: ReturnType<typeof setInterval>;
  /** Server-Summen je Kapitel; nach der ersten eigenen Änderung rechnet die Ansicht selbst. */
  private serverSums = new Map<string, { points: number; maxPoints: number; seconds: number }>();
  private serverTotals: { points: number; maxPoints: number } | null = null;

  readonly glyphs = CALC_GLYPHS;
  readonly evals = CALC_EVALS;

  // ===== Kapitel-Timer =====================================================

  toggleTimer(): void {
    if (this.timerRunning) this.pauseTimer(); else this.startTimer();
  }

  startTimer(): void {
    if (this.timerRunning) return;
    this.timerRunning = true;
    this.timerHandle = setInterval(() => {
      this.timerSeconds++;
      // Jede Sekunde persistieren: übersteht Tab-Schließen/Navigieren ohne eigenen Flush-Pfad.
      this.persistTimer();
    }, 1000);
  }

  pauseTimer(): void {
    if (!this.timerRunning) return;
    this.timerRunning = false;
    if (this.timerHandle !== undefined) { clearInterval(this.timerHandle); this.timerHandle = undefined; }
    this.persistTimer();
  }

  /** Angezeigte kumulierte Kapitel-Zeit (m:ss bzw. h:mm:ss). */
  get timerDisplay(): string {
    return formatSeconds(this.timerSeconds);
  }

  /**
   * Beim Stellungswechsel den Zähler-Topf des Kapitels nachziehen: gleiches Kapitel → weiterzählen,
   * anderes Kapitel → alten Stand sichern und den des neuen Kapitels laden. Ein laufender Timer
   * läuft über den Wechsel hinweg weiter (zählt dann ins neue Kapitel).
   */
  private syncTimerChapter(chapter: string | null): void {
    const key = chapter ?? '';
    if (key === this.timerChapterKey) return;
    if (this.timerChapterKey !== null) this.persistTimer();
    this.timerChapterKey = key;
    this.timerSeconds = this.readTimerStore()[key] ?? 0;
  }

  private timerStorageKey(): string {
    return `rookhub_calc_timer_${this.bookId}`;
  }

  private readTimerStore(): Record<string, number> {
    try {
      const parsed = JSON.parse(localStorage.getItem(this.timerStorageKey()) ?? '{}');
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch { return {}; }
  }

  private persistTimer(): void {
    if (this.timerChapterKey === null) return;
    const store = this.readTimerStore();
    store[this.timerChapterKey] = this.timerSeconds;
    try { localStorage.setItem(this.timerStorageKey(), JSON.stringify(store)); } catch { /* voll/gesperrt */ }
  }

  /**
   * Erklärung eines Symbols fürs Mouseover: erst was es bedeutet („Weiß gewinnt"), dann der
   * Bedienhinweis. Der Übersetzungs-Schlüssel kommt aus `calc-tree.util` — dieselbe Quelle wie
   * die Symbolliste, damit kein Symbol ohne Erklärung bleibt.
   */
  glyphTooltip(glyph: CalcGlyph): string {
    return this.symbolTooltip(glyphNameKey(glyph));
  }

  evalTooltip(evaluation: CalcEval): string {
    return this.symbolTooltip(evalNameKey(evaluation));
  }

  private symbolTooltip(nameKey: string): string {
    return `${this.translate.instant(nameKey)} (${this.translate.instant('calc.symbolToggleHint')})`;
  }

  /** Kommentar-Entwurf zum ausgewählten Zug. */
  cursorComment = '';

  saving = false;
  savedAt: Date | null = null;
  private dirty = false;
  /** Noch nicht bestätigte Speicherungen JE STELLUNG (BookPuzzleId → serialisierter Baum, `null` =
   *  löschen). Der Snapshot muss an der Stellung hängen, zu der er gehört: `this.tree` ist nach
   *  einem Stellungswechsel schon ersetzt, ein gescheiterter Save wäre sonst unwiederbringlich weg. */
  private outbox = new Map<number, string | null>();
  private hadStoredTree = false;
  private saveTimer?: ReturnType<typeof setTimeout>;
  private static readonly AUTOSAVE_MS = 1200;

  private subs = new Subscription();
  /** Entwertet überholte Ladevorgänge (siehe loadPosition). */
  private loadEpoch = 0;

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
    // Nur Anzeige: die Stoppuhr selbst zählt ohne Takt weiter (und pausiert bei verstecktem Tab).
    this.liveHandle = setInterval(() => { this.liveSeconds = this.currentWatchSeconds(); }, 1000);
  }

  ngOnDestroy(): void {
    this.pauseTimer();
    if (this.liveHandle !== undefined) { clearInterval(this.liveHandle); this.liveHandle = undefined; }
    this.clearSaveTimer();
    this.flushSave();
    this.harvestWatch();     // gemessene Zeit noch abschöpfen …
    this.sendReviews();      // … und mitsamt Wahl/Punkten rausschicken
    this.subs.unsubscribe();
  }

  // ===== Laden ==============================================================

  private loadBook(requestedPositionId: number | null): void {
    this.loading = true;
    this.loadError = false;
    this.subs.add(this.api.getBook(this.bookId).subscribe({
      next: book => {
        this.book = book;
        this.positions = book.positions.map(p => this.normalizeItem(p));
        this.groups = this.groupPositions(this.positions);
        this.takeServerSums(book);
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

  /**
   * FALLE: bei schnellem Weiterklicken laufen zwei Ladevorgänge parallel und können
   * out-of-order eintreffen — die ältere Antwort würde Brett/Baum auf die VORHERIGE Stellung
   * setzen, während Index/URL/Sprungliste schon auf der neuen stehen (Eingaben landeten dann
   * unter der falschen Stellung). Der Epoch-Zähler entwertet jede überholte Antwort.
   */
  private loadPosition(bookPuzzleId: number): void {
    this.loading = true;
    const epoch = ++this.loadEpoch;
    this.subs.add(this.api.getPosition(bookPuzzleId).subscribe({
      next: pos => {
        if (epoch !== this.loadEpoch) return;
        this.position = pos;
        this.applyPosition(pos);
        this.loading = false;
      },
      error: () => {
        if (epoch !== this.loadEpoch) return;
        this.loading = false;
        this.loadError = true;
      },
    }));
  }

  private applyPosition(pos: CalcPosition): void {
    this.syncTimerChapter(pos.chapter);
    this.beginWatch(pos.id);
    this.loadReview(pos);
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
    // Ein noch laufender Save gehört zur ALTEN Stellung — sein Spinner darf hier nicht weiterlaufen.
    this.saving = false;
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
      else out.push({ chapter, items: [p], points: 0, maxPoints: 0, seconds: 0 });
    }
    return out;
  }

  /** Fehlende Felder eines älteren/knapperen Server-Standes auffüllen (nie `undefined` anzeigen). */
  private normalizeItem(item: CalcPositionListItem): CalcPositionListItem {
    item.chosenSan = item.chosenSan ?? null;
    item.chosenUci = item.chosenUci ?? null;
    item.secondsSpent = item.secondsSpent ?? 0;
    item.grade = normalizeGrade(item.grade);
    return item;
  }

  /**
   * Summen: solange der Nutzer nichts geändert hat, gelten die FERTIGEN Server-Summen. Sobald er
   * bewertet, rechnet die Ansicht aus den Zeilen nach — sonst stünden Kapitel- und Kurssumme bis
   * zum nächsten Laden auf dem alten Stand.
   */
  private takeServerSums(book: CalcBook): void {
    this.serverSums.clear();
    for (const c of book.chapters ?? []) {
      this.serverSums.set(c.chapter ?? '', {
        points: c.points ?? 0,
        maxPoints: c.maxPoints ?? 0,
        // `secondsSum`, nicht `secondsSpent`: der Server liefert eine SUMME (siehe
        // CalcChapterSummary). Ein Tippfehler hier fällt nicht auf — die Zeit stünde still auf 0.
        seconds: c.secondsSum ?? 0,
      });
    }
    this.serverTotals = typeof book.points === 'number'
      ? { points: book.points, maxPoints: book.maxPoints ?? maxPoints(this.positions.length) }
      : null;
    this.refreshSums();
  }

  /** Kapitel-Summen + Kurssumme neu setzen (Server-Werte, solange vorhanden). */
  private refreshSums(): void {
    for (const group of this.groups) {
      const fromServer = this.serverSums.get(group.chapter ?? '');
      group.points = fromServer ? fromServer.points : sumPoints(group.items);
      // Das Maximum hängt nur an der Zahl der Stellungen — der Server darf es liefern, die
      // Ansicht kann es aber jederzeit selbst ausrechnen.
      group.maxPoints = fromServer?.maxPoints || maxPoints(group.items.length);
      group.seconds = fromServer ? fromServer.seconds : sumSeconds(group.items);
    }
    this.totalPoints = this.serverTotals ? this.serverTotals.points : sumPoints(this.positions);
    this.totalMaxPoints = this.serverTotals?.maxPoints || maxPoints(this.positions.length);
  }

  /** Nach einer eigenen Änderung sind die Server-Summen überholt — ab jetzt selbst rechnen. */
  private dropServerSums(): void {
    this.serverSums.clear();
    this.serverTotals = null;
    this.refreshSums();
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
    this.syncChoiceWithTree();
  }

  deleteLine(leafId: number): void {
    const cursor = removeLine(this.tree, leafId);
    this.activeLeafId = this.leafUnder(cursor);
    this.setCursor(cursor);
    this.markDirty();
    this.syncChoiceWithTree();
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

  // ===== Festlegung / Bewertung / Rechenzeit ================================

  /** Stand der Stellung übernehmen: aus der Sprunglisten-Zeile, ergänzt um das, was die
   *  Einzelstellung mitliefert (falls der Server die Werte auch dort führt). */
  private loadReview(pos: CalcPosition): void {
    const item = this.positions.find(p => p.id === pos.id);
    this.review = {
      chosenSan: pos.chosenSan !== undefined ? pos.chosenSan : (item?.chosenSan ?? null),
      chosenUci: pos.chosenUci !== undefined ? pos.chosenUci : (item?.chosenUci ?? null),
      secondsSpent: pos.secondsSpent !== undefined ? pos.secondsSpent : (item?.secondsSpent ?? 0),
      grade: normalizeGrade(pos.grade !== undefined ? pos.grade : (item?.grade ?? null)),
    };
    this.writeReviewToList(pos.id);
  }

  /**
   * Stern an einem ERSTEN Zug: legt sich darauf fest. Genau eine Festlegung je Stellung —
   * ein anderer Zug verschiebt sie, derselbe Zug nimmt sie zurück (null).
   */
  chooseMove(nodeId: number): void {
    const node = findNode(this.tree, nodeId);
    if (!node || node.parentId !== this.tree.rootId) return;   // nur Kinder der Wurzel
    const clearing = this.review.chosenUci === node.uci;
    this.setReview(clearing
      ? { chosenSan: null, chosenUci: null }
      : { chosenSan: node.san, chosenUci: node.uci });
  }

  /**
   * Eine Stufe wählen. Die Auswahl geht über die BEDEUTUNG, nicht über eine Zahl — dieselbe Stufe
   * noch einmal nimmt die Bewertung zurück („noch nicht bewertet", ausdrücklich ≠ Stufe 0).
   */
  setGrade(grade: CalcGrade): void {
    this.setReview({ grade: this.review.grade === grade ? null : grade });
  }

  isGrade(grade: CalcGrade): boolean {
    return this.review.grade === grade;
  }

  /** Optimistisch anzeigen + an den Server einreihen. */
  private setReview(patch: CalcReviewPatch): void {
    const bookPuzzleId = this.position?.id;
    if (bookPuzzleId == null) return;
    this.review = applyReviewPatch(this.review, patch);
    this.writeReviewToList(bookPuzzleId);
    this.dropServerSums();
    this.queueReview(bookPuzzleId, patch);
  }

  /** Sprunglisten-Zeile nachziehen (Zeit/Stufe/Wahl stehen dort in der Übersicht). */
  private writeReviewToList(bookPuzzleId: number): void {
    const item = this.positions.find(p => p.id === bookPuzzleId);
    if (!item) return;
    item.chosenSan = this.review.chosenSan;
    item.chosenUci = this.review.chosenUci;
    item.secondsSpent = this.review.secondsSpent;
    item.grade = this.review.grade;
  }

  /**
   * Wurde der festgelegte Zug aus dem Baum gelöscht, ist die Festlegung gegenstandslos —
   * sonst bliebe eine Wahl stehen, die in keiner Linie mehr vorkommt.
   */
  private syncChoiceWithTree(): void {
    const chosen = this.review.chosenUci;
    if (!chosen) return;
    const stillThere = rootNode(this.tree).childIds
      .some(id => findNode(this.tree, id)?.uci === chosen);
    if (!stillThere) this.setReview({ chosenSan: null, chosenUci: null });
  }

  // ===== Rechenzeit messen ==================================================

  /** Uhr für die angezeigte Stellung neu aufziehen (vorherige Zeit vorher abschöpfen). */
  private beginWatch(bookPuzzleId: number): void {
    this.harvestWatch();
    this.watchPositionId = bookPuzzleId;
    this.watch.start(0);
    this.liveSeconds = 0;
  }

  /** Gemessene Sekunden als DELTA einreihen und die Uhr anhalten (idempotent). */
  private harvestWatch(): void {
    if (this.watchPositionId === null) return;
    const bookPuzzleId = this.watchPositionId;
    this.watchPositionId = null;
    this.liveSeconds = 0;
    const seconds = this.watch.stop();
    if (seconds <= 0) return;
    // Auch lokal sofort gutschreiben: die Sprungliste soll die Zeit nicht erst nach der
    // Server-Antwort zeigen (die Antwort korrigiert die Zahl gleich darauf).
    if (this.isCurrent(bookPuzzleId)) {
      this.review = applyReviewPatch(this.review, { secondsDelta: seconds });
    } else {
      const item = this.positions.find(p => p.id === bookPuzzleId);
      if (item) item.secondsSpent = (item.secondsSpent ?? 0) + seconds;
    }
    this.writeReviewToList(bookPuzzleId);
    this.dropServerSums();
    // Jedes gemessene Delta bekommt seine eigene Marke: der Server ADDIERT die Zeit, und ein
    // Wiederholversuch (Antwort verloren, Anfrage angekommen) darf sie nicht doppelt buchen.
    this.queueReview(bookPuzzleId, { secondsDelta: seconds, secondsToken: newSecondsToken() });
  }

  private currentWatchSeconds(): number {
    return this.watchPositionId === null ? 0 : this.watch.elapsedSeconds;
  }

  // ===== Senden (eigener Endpoint neben dem Baum) ===========================

  private reviewOutbox = new Map<number, CalcReviewPatch>();
  private reviewInFlight = new Set<number>();

  private queueReview(bookPuzzleId: number, patch: CalcReviewPatch): void {
    const merged = mergeReviewPatch(this.reviewOutbox.get(bookPuzzleId) ?? {}, patch);
    if (isNoopPatch(merged)) this.reviewOutbox.delete(bookPuzzleId);
    else this.reviewOutbox.set(bookPuzzleId, merged);
    this.sendReviews();
  }

  /** Je Stellung immer nur EINE Anfrage unterwegs — wie beim Baum-Speichern: der Server-Upsert
   *  kennt kein Versions-Token, zwei parallele Anfragen könnten vertauscht ankommen (und ein
   *  Zeit-Delta doppelt zählen, wenn eine davon wiederholt werden muss). */
  private sendReviews(): void {
    for (const [bookPuzzleId, patch] of [...this.reviewOutbox]) {
      if (this.reviewInFlight.has(bookPuzzleId)) continue;
      this.reviewOutbox.delete(bookPuzzleId);
      this.reviewInFlight.add(bookPuzzleId);
      this.api.saveReview(bookPuzzleId, patch).subscribe({
        next: res => {
          this.reviewInFlight.delete(bookPuzzleId);
          this.applyServerReview(bookPuzzleId, res);
          if (this.reviewOutbox.has(bookPuzzleId)) this.sendReviews();
        },
        error: () => {
          this.reviewInFlight.delete(bookPuzzleId);
          // Genau diesen Stand wieder einreihen: ein inzwischen aufgelaufener neuerer gewinnt bei
          // Wahl/Punkten, die Zeit-Deltas addieren sich (gemessene Zeit geht nie verloren).
          // WICHTIG: `patch` steht vorn, damit SEINE Zeit-Marke erhalten bleibt — der Fehler kann
          // ein Timeout sein, die Anfrage also angekommen. Mit einer neuen Marke würde der Server
          // die schon verbuchte Zeit ein zweites Mal addieren.
          const merged = mergeReviewPatch(patch, this.reviewOutbox.get(bookPuzzleId) ?? {});
          if (!isNoopPatch(merged)) this.reviewOutbox.set(bookPuzzleId, merged);
          // Bewusst kein sofortiger Neuversuch (sonst heiße Schleife bei totem Server) —
          // der nächste Stellungswechsel/Verlassen schickt es mit.
          this.snackbar.warn(this.translate.instant('calc.review.saveFailed'));
        },
      });
    }
  }

  /** Antwort des Servers ist die Wahrheit — vor allem bei `secondsSpent` (dort wird addiert). */
  private applyServerReview(bookPuzzleId: number, res: Partial<CalcReviewSaved> | null | undefined): void {
    if (!res) return;
    // Ein noch offener Änderungswunsch derselben Stellung gehört NICHT überschrieben — die
    // Server-Antwort kennt ihn noch nicht.
    const pending = this.reviewOutbox.get(bookPuzzleId);
    const item = this.positions.find(p => p.id === bookPuzzleId);
    if (item) {
      if (res.chosenSan !== undefined) item.chosenSan = res.chosenSan;
      if (res.chosenUci !== undefined) item.chosenUci = res.chosenUci;
      // Liegt schon ein neueres Zeit-Delta in der Warteschlange, ist die Server-Zahl überholt:
      // die Zeile würde sonst kurz zu klein anzeigen und erst nach der nächsten Antwort springen.
      if (typeof res.secondsSpent === 'number' && !pending?.secondsDelta) {
        item.secondsSpent = res.secondsSpent;
      }
      if (res.grade !== undefined) item.grade = normalizeGrade(res.grade);
    }
    if (this.isCurrent(bookPuzzleId)) {
      if (!pending) {
        this.review = {
          chosenSan: res.chosenSan ?? null,
          chosenUci: res.chosenUci ?? null,
          secondsSpent: res.secondsSpent ?? this.review.secondsSpent,
          grade: normalizeGrade(res.grade),
        };
      }
    }
    this.dropServerSums();
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

  /** Rechenzeit an der ANGEZEIGTEN Stellung inkl. der laufenden Sitzung. */
  get positionTimeDisplay(): string {
    return formatSeconds(this.review.secondsSpent + this.liveSeconds);
  }

  timeDisplay(seconds: number): string {
    return formatSeconds(seconds);
  }

  /** Punkte der ANGEZEIGTEN Stellung („2 / 4"); null-Stufe hat keine Punktzahl, sondern gar keine. */
  get positionPoints(): number {
    return gradePoints(this.review.grade);
  }

  /** Punkte einer Stellung aus der Sprungliste. */
  pointsOf(item: CalcPositionListItem): number {
    return gradePoints(item.grade);
  }

  /** Kurzform der Stufe für die Sprungliste („Zug, ohne Hauptfolge"). */
  gradeShortLabel(grade: CalcGrade): string {
    return this.translate.instant(this.gradeOptions[grade].shortKey);
  }

  /** Punktzahl IMMER mit Maximum — „14" allein ist ohne die Zahl der Stellungen nicht lesbar. */
  scoreDisplay(points: number, max: number): string {
    return formatScore(points, max);
  }

  /** Kapitel-Kopf der Sprungliste: Name plus die Summen des Kapitels („14 / 24 Pkt · 12:30"). */
  chapterLabel(group: CalcPositionGroup): string {
    const name = group.chapter || this.translate.instant('courses.noChapter');
    const score = formatScore(group.points, group.maxPoints);
    const summary = group.seconds
      ? this.translate.instant('calc.review.chapterSummary', { score, time: formatSeconds(group.seconds) })
      : this.translate.instant('calc.review.chapterScore', { score });
    return `${name} · ${summary}`;
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
    // Die an DIESER Stellung gemessene Zeit gehört noch zu ihr — vor dem Wechsel abschöpfen
    // (sonst ginge sie verloren, falls die nächste Stellung gar nicht erst lädt).
    this.harvestWatch();
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
    if (this.dirty && this.position) {
      const bookPuzzleId = this.position.id;
      this.dirty = false;
      // Leerer Baum: gespeicherten Stand verwerfen (nicht „{}" ablegen).
      if (isEmpty(this.tree)) {
        this.markPositionDone(bookPuzzleId, false);
        if (this.hadStoredTree) { this.hadStoredTree = false; this.outbox.set(bookPuzzleId, null); }
        // Sonst nur einen noch offenen Stand entwerten: ohne das würde ein zuvor
        // fehlgeschlagener Save (Eintrag liegt noch in der Outbox) den gerade bewusst
        // GELEERTEN Baum beim nächsten Senden doch wieder hochschreiben.
        else this.outbox.delete(bookPuzzleId);
      } else {
        this.outbox.set(bookPuzzleId, serializeTree(this.tree));
      }
    }
    this.sendOutbox();
  }

  /** Laufende Nummer je Stellung: ein spät scheiternder ALTER Save darf einen inzwischen
   *  erfolgreich gespeicherten neueren Stand nicht per Requeue zurückrollen. */
  private sendSeq = new Map<number, number>();

  private nextSeq(bookPuzzleId: number): number {
    const n = (this.sendSeq.get(bookPuzzleId) ?? 0) + 1;
    this.sendSeq.set(bookPuzzleId, n);
    return n;
  }

  /** Nur re-queuen, wenn seit dem Absenden kein neuerer Stand derselben Stellung losgeschickt wurde. */
  private requeueIfLatest(bookPuzzleId: number, seq: number, json: string | null): void {
    if (this.sendSeq.get(bookPuzzleId) !== seq) return;
    this.outbox.set(bookPuzzleId, json);
  }

  /** Stellungen mit einer JETZT laufenden Anfrage. Je Stellung darf immer nur EINE unterwegs sein:
   *  der Server-Upsert ist ein reines „last write wins" ohne Versions-Token, zwei parallele Saves
   *  derselben Stellung könnten also in umgekehrter Reihenfolge ankommen — der ältere Baum bliebe
   *  gespeichert, ohne dass die Oberfläche etwas davon merkt (Autosave alle 1,2 s, eine langsame
   *  Verbindung genügt). Der jüngere Stand wartet stattdessen in der Outbox und geht raus, sobald
   *  die laufende Anfrage durch ist. */
  private inFlight = new Set<number>();

  /** Alles Offene rausschicken — auch Stände von Stellungen, die inzwischen verlassen wurden.
   *  Stellungen mit laufender Anfrage bleiben in der Outbox (siehe {@link inFlight}). */
  private sendOutbox(): void {
    if (this.outbox.size === 0) return;
    const pending = [...this.outbox].filter(([bookPuzzleId]) => !this.inFlight.has(bookPuzzleId));
    for (const [bookPuzzleId, json] of pending) {
      this.outbox.delete(bookPuzzleId);
      this.inFlight.add(bookPuzzleId);
      const seq = this.nextSeq(bookPuzzleId);
      if (json === null) this.sendDelete(bookPuzzleId, seq); else this.sendSave(bookPuzzleId, json, seq);
    }
  }

  /** Antwort da: Slot freigeben. Nach ERFOLG einen inzwischen aufgelaufenen neueren Stand direkt
   *  nachschicken; nach einem FEHLER nicht (sonst dreht sich bei totem Server eine heiße Schleife) —
   *  der wartet dann auf den nächsten Autosave/Flush. */
  private finishSend(bookPuzzleId: number, drain: boolean): void {
    this.inFlight.delete(bookPuzzleId);
    if (drain && this.outbox.has(bookPuzzleId)) this.sendOutbox();
  }

  private sendSave(bookPuzzleId: number, json: string, seq: number): void {
    if (this.isCurrent(bookPuzzleId)) this.saving = true;
    this.api.saveTree(bookPuzzleId, json).subscribe({
      next: res => {
        if (this.isCurrent(bookPuzzleId)) {
          this.saving = false;
          this.hadStoredTree = true;
          this.savedAt = new Date(res.updatedAt);
        }
        this.markPositionDone(bookPuzzleId, true);
        this.finishSend(bookPuzzleId, true);
      },
      error: () => {
        if (this.isCurrent(bookPuzzleId)) this.saving = false;
        // GENAU diesen Stand erneut einreihen — aber nur, wenn er noch der jüngste ist: weder ein
        // inzwischen aufgelaufener neuerer Stand in der Outbox noch ein bereits gesendeter (seq).
        if (!this.outbox.has(bookPuzzleId)) this.requeueIfLatest(bookPuzzleId, seq, json);
        this.finishSend(bookPuzzleId, false);
        this.reportSaveError();
      },
    });
  }

  private sendDelete(bookPuzzleId: number, seq: number): void {
    this.api.deleteTree(bookPuzzleId).subscribe({
      next: () => {
        if (this.isCurrent(bookPuzzleId)) this.savedAt = null;
        this.finishSend(bookPuzzleId, true);
      },
      error: () => {
        if (!this.outbox.has(bookPuzzleId)) this.requeueIfLatest(bookPuzzleId, seq, null);
        this.finishSend(bookPuzzleId, false);
        this.reportSaveError();
      },
    });
  }

  /** Gehört die Antwort noch zur angezeigten Stellung? Sonst dürfen `saving`/`savedAt` nicht angefasst
   *  werden — eine spät eintreffende Antwort der ALTEN Stellung schriebe sonst in die neue Ansicht. */
  private isCurrent(bookPuzzleId: number): boolean {
    return this.position?.id === bookPuzzleId;
  }

  private markPositionDone(bookPuzzleId: number, done: boolean): void {
    const item = this.positions.find(p => p.id === bookPuzzleId);
    if (item) item.hasTree = done;
  }

  private reportSaveError(): void {
    // Kein `dirty = true`: das Flag hängt am GERADE geladenen Baum — nach einem Stellungswechsel
    // hätte es den nächsten Flush auf die falsche (neue) Stellung gelenkt und den bearbeiteten
    // Baum der alten Stellung verworfen. Der Wiederholversuch läuft über `outbox` (je Stellung).
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
