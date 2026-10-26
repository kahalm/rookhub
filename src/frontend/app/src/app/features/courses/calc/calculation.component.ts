import {
  ChangeDetectionStrategy, Component, HostListener, OnDestroy, OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { IconLabelDirective } from '../../../shared/icon-label/icon-label.directive';
import { MatDialog } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Color, Key } from 'chessground/types';
import { Subscription } from 'rxjs';
import { PuzzleBoardComponent } from '../../puzzles/puzzle-board.component';
import { tryFreeMove, tryLoadFen } from '../../puzzles/puzzle-move.util';
import { PreferencesService } from '../../../core/preferences.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { CalcLinesComponent } from './calc-lines.component';
import { CalcResultCardComponent } from './calc-result-card.component';
import { CalcAnnotateCardComponent } from './calc-annotate-card.component';
import { CalcStatusPanelComponent } from './calc-status-panel.component';
import {
  CalcEval, CalcGlyph, CalcNode, CalcTree,
  createTree, deserializeTree, findNode, rootNode, setEvaluation, setGlyph,
  whiteToMove,
} from './calc-tree.util';
import {
  CALC_GRADE_OPTIONS, CALC_MAX_POINTS_PER_POSITION, CalcGrade, CalcReview, CalcReviewPatch,
  normalizeGrade,
} from './calc-review.util';
import {
  CalcBackend, CalcBook, CalcPosition, CalcPositionListItem, CalculationService,
} from './calculation.service';
import { CalcLocalMode } from './calc-local-mode';
import { CalcTrainingSession } from './calc-training-session';
import { CalcTreeCursor } from './calc-tree-cursor';
import { CalcTreeSaver } from './calc-tree-saver';
import { CalcPageMode } from './calc-page-mode';
import { CalcChapterModel } from './calc-chapter-model';
import { CalcPageLoader, calcStartFen } from './calc-page-loader';
import { CalcLabels } from './calc-labels';
import { CalcReviewController } from './calc-review.controller';
import { CalcGradeDialogComponent, CalcGradeDialogResult } from './calc-grade-dialog.component';
import { CalcSettingsDialogComponent } from './calc-settings-dialog.component';
import { BoardFullscreenButtonComponent } from '../../../shared/fullscreen/board-fullscreen-button.component';
import {
  CalcTimerDialogComponent, CalcTimerDialogData, CalcTimerDialogResult,
} from './calc-timer-dialog.component';
import { AuthService } from '../../../core/auth.service';
import { CourseLanguageService, CourseRef } from '../course-language.service';
import { CourseLangPickerComponent, MachineNoteComponent } from '../course-lang-picker.component';
import { isBoardHotkey } from '../../../shared/keyboard.util';
import {
  CalcPositionGroup,
} from './calc-chapters.util';

// Kapitelmodell (Schlüssel, Gruppen, Einstiegskapitel, Summen) als reine Funktionen: calc-chapters.util.ts.
export type { CalcPositionGroup } from './calc-chapters.util';

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
 *
 * **Ohne Konto geht es auch** (öffentliche Kurz-URL `/{slug}`): dann kommen die Stellungen lesend
 * vom öffentlichen Endpoint und alles Selbstgemachte — Baum, Festlegung, Zeit, Bewertung — bleibt
 * im localStorage DIESES Geräts (`LocalCalculationBackend`). Die Bedienung ist identisch; ein
 * dezenter Hinweis samt Anmelde-Link sagt, dass die Arbeit nirgendwo sonst liegt.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-calculation',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatIconModule, MatFormFieldModule,
    MatSelectModule, MatMenuModule, MatProgressSpinnerModule, MatTooltipModule, IconLabelDirective, TranslatePipe,
    RouterLink,
    PuzzleBoardComponent, CalcLinesComponent, BoardFullscreenButtonComponent,
    CourseLangPickerComponent, MachineNoteComponent,
    CalcResultCardComponent, CalcAnnotateCardComponent, CalcStatusPanelComponent,
  ],
  templateUrl: './calculation.component.html',
  styleUrls: ['./calculation.component.scss'],
})
export class CalculationComponent implements OnInit, OnDestroy {
  bookId!: number;
  /** Stellungen, Kapitel, Standort und Summen — siehe {@link CalcChapterModel}. */
  private readonly chapters = new CalcChapterModel();
  /** Anzeige-Texte (Kapitel-/Stellungsnamen, Punkte, Zeiten, Erklärungen) — siehe {@link CalcLabels}. */
  readonly labels = new CalcLabels({
    t: (key, params) => this.translate.instant(key, params),
    chapters: this.chapters,
    review: () => this.review,
    liveSeconds: () => this.liveSeconds,
    trainingStateKey: () => this.trainingStateKey,
  });
  /** Buch, Stellung und Ladezustand — siehe {@link CalcPageLoader}. */
  private readonly loader = new CalcPageLoader({
    backend: () => this.backend,
    bookId: () => this.bookId,
    lang: () => this.courseLang.requestLang(this.langRef),
    chapters: this.chapters,
    enterChapter: (chapterIndex, requested, updateUrl) => this.enterChapter(chapterIndex, requested, updateUrl),
    applyPosition: pos => this.applyPosition(pos),
    stopTraining: () => this.stopTraining(),
    track: sub => this.subs.add(sub),
  });
  get book(): CalcBook | null { return this.loader.book; }
  set book(book: CalcBook | null) { this.loader.book = book; }

  /** ALLE Stellungen des Buchs (Quelle der Kapitel-Gruppen) — gearbeitet wird nie auf dieser Liste. */
  get positions(): CalcPositionListItem[] { return this.chapters.positions; }
  set positions(items: CalcPositionListItem[]) { this.chapters.positions = items; }
  /** Die Kapitel in Lesereihenfolge; „ohne Kapitel" ist eines davon. */
  get groups(): CalcPositionGroup[] { return this.chapters.groups; }
  set groups(groups: CalcPositionGroup[]) { this.chapters.groups = groups; }
  /** Welches KAPITEL bearbeitet wird (Index in {@link groups}); -1 = noch keins gewählt. */
  get chapterIndex(): number { return this.chapters.chapterIndex; }
  set chapterIndex(index: number) { this.chapters.chapterIndex = index; }
  /** Stellung INNERHALB des Kapitels (Index in {@link chapterPositions}). */
  get index(): number { return this.chapters.index; }
  set index(index: number) { this.chapters.index = index; }

  /** Server oder Gerät (anonym: localStorage) samt Geräte-Hinweis/Warnung — siehe {@link CalcLocalMode}. */
  private readonly local = new CalcLocalMode();
  private get backend(): CalcBackend { return this.local.backend(this.api); }
  /** Läuft alles nur lokal? (dezenter Hinweis + Anmelde-Link in der Ansicht) */
  get localOnly(): boolean { return this.local.localOnly; }
  /** Der Gerätespeicher hat zuletzt NICHT mitgespielt — es liegt gerade NIRGENDS (Warnung statt Hinweis). */
  get localSaveFailed(): boolean { return this.local.saveFailed; }
  /** Der ruhige Hinweis „liegt nur auf diesem Gerät" wurde für DIESEN Kurs weggeklickt. */
  get noticeDismissed(): boolean { return this.local.noticeDismissed; }

  /** Anmelde-Link führt GENAU hierher zurück (inkl. `?pos=`/`?chapter=`). */
  get loginReturnUrl(): string { return this.router.url; }

  /** Ziel des Turms („Zurück“): angemeldet die Kursübersicht. Gäste kommen nur über die Kurz-URL eines öffentlichen
   *  Kurses hierher — /courses zeigte ihnen nur die Anmeldemaske (UX-024), sie gehen zur Startseite. */
  get homeLink(): string { return this.auth?.isLoggedIn ? '/courses' : '/'; }

  get showLocalNotice(): boolean { return this.local.showNotice; }
  get showLocalWarning(): boolean { return this.local.showWarning; }
  dismissLocalNotice(): void { this.local.dismissNotice(this.bookId); }
  /** Nur für diese Sitzung: der nächste Fehlschlag holt die Warnung zurück. */
  dismissLocalWarning(): void { this.local.dismissWarning(); }
  private setLocalSaveFailed(failed: boolean): void { this.local.setSaveFailed(failed); }

  get position(): CalcPosition | null { return this.loader.position; }
  set position(pos: CalcPosition | null) { this.loader.position = pos; }
  get loading(): boolean { return this.loader.loading; }
  set loading(value: boolean) { this.loader.loading = value; }
  get loadError(): boolean { return this.loader.loadError; }
  set loadError(value: boolean) { this.loader.loadError = value; }

  /** Position im Baum (Cursor, aktive Linie, Kommentar-Entwurf) — siehe {@link CalcTreeCursor}. */
  private readonly cursor = new CalcTreeCursor();

  /** Ausgangsstellung (nach dem Vorlauf) — das Brett zeigt IMMER nur diese. */
  get startFen(): string { return this.cursor.startFen; }
  /** FEN am aktuellen Cursor — nur für die Legalitätsprüfung der Klicks, nie sichtbar. */
  get cursorFen(): string { return this.cursor.cursorFen; }
  /** Stellung ist für chess.js nicht ladbar (Chessable-Muster-Diagramm) → keine Zug-Eingabe. */
  illegalPosition = false;

  get tree(): CalcTree { return this.cursor.tree; }
  get cursorId(): number { return this.cursor.cursorId; }
  /** Zähler für das Neuzeichnen des Panels nach In-place-Änderungen am Baum. */
  revision = 0;

  orientation: Color = 'white';

  /** Kapitel-Uhr, Stellungs-Stoppuhr, Startsperre und Start-Hinweis — siehe {@link CalcTrainingSession}. */
  private readonly training = new CalcTrainingSession({
    storageKey: () => `rookhub_calc_timer_${this.bookId}`,
    userId: () => this.auth?.currentUser?.userId ?? null,
    // Anonym meldet die Warnung „kann gerade nichts speichern" die Zeit ausdrücklich mit; zurückgenommen wird sie
    // hier nicht — das bleibt dem Baum-Speicher.
    onPersistFailed: () => this.setLocalSaveFailed(this.localOnly),
  }, {
    positionLoaded: () => !this.loadError && this.position !== null,
    currentPositionId: () => this.position?.id ?? null,
    creditSeconds: (bookPuzzleId, seconds, token) => this.reviews.creditSeconds(bookPuzzleId, seconds, token),
    warn: (textKey, action) => action
      ? this.snackbar.warn(this.translate.instant(textKey), { action: action.actionKey, duration: action.duration })
      : this.snackbar.warn(this.translate.instant(textKey)),
  });
  private get timer() { return this.training.timer; }

  get timerRunning(): boolean { return this.timer.running; }
  get timerSeconds(): number { return this.timer.seconds; }
  set timerSeconds(seconds: number) { this.timer.seconds = seconds; }
  readonly noDests = new Map<Key, Key[]>();

  // ===== Festlegung / Rechenzeit / Bewertung je STELLUNG ====================
  // Anders als der Kapitel-Timer oben gehören diese drei Werte dem Server (eigene Spalten,
  // auswertbar) und hängen an der Zeile (UserId, BookPuzzleId).

  /** Stand der ANGEZEIGTEN Stellung (Spiegel der Sprunglisten-Zeile). */
  get review(): CalcReview { return this.reviews.review; }

  /** Festlegung/Zeit/Stufe samt Sende-Warteschlange — siehe {@link CalcReviewController}. */
  private readonly reviews = new CalcReviewController({
    items: () => this.positions,
    currentId: () => this.position?.id ?? null,
    send: (bookPuzzleId, patch) => this.backend.saveReview(bookPuzzleId, patch),
    onChanged: () => this.dropServerSums(),
    onSaved: () => this.setLocalSaveFailed(false),
    // Anonym ist der „Server" der Gerätespeicher: dann ist der Hinweis „liegt nur auf diesem
    // Gerät" falsch — es liegt gerade nirgends, und das muss stehen bleiben (der Snackbar-
    // Hinweis ist nach ein paar Sekunden weg).
    onSaveFailed: () => {
      this.setLocalSaveFailed(this.localOnly);
      this.snackbar.warn(this.translate.instant('calc.review.saveFailed'));
    },
  });
  /** Die fünf Stufen der Selbstbewertung in Anzeige-Reihenfolge. */
  readonly gradeOptions = CALC_GRADE_OPTIONS;
  /** Höchstpunktzahl einer einzelnen Stellung (für „2 / 4"). */
  readonly maxPointsPerPosition = CALC_MAX_POINTS_PER_POSITION;
  /** Erreichte/erreichbare Punkte des AKTUELLEN KAPITELS (das ist die Arbeitseinheit). */
  get totalPoints(): number { return this.chapters.totalPoints; }
  get totalMaxPoints(): number { return this.chapters.totalMaxPoints; }
  /** Dieselben Zahlen fürs ganze Buch — dürfen vorkommen, aber nur ausdrücklich beschriftet. */
  get bookPoints(): number { return this.chapters.bookPoints; }
  get bookMaxPoints(): number { return this.chapters.bookMaxPoints; }
  /** Sekunden der laufenden Sitzung an dieser Stellung (nur Anzeige; gesendet wird das Delta). */
  get liveSeconds(): number { return this.training.liveSeconds; }

  // ===== Kapitel-Training starten/stoppen (CalcTrainingSession) ============

  toggleTraining(): void { this.training.toggle(); }
  /** Darf JETZT gemessen werden? Nur mit geladener Stellung (siehe {@link CalcTrainingSession.canTrain}). */
  get canTrain(): boolean { return this.training.canTrain; }
  /** Stoppen muss immer gehen — gesperrt wird nur das STARTEN ohne geladene Stellung. */
  get trainingDisabled(): boolean { return this.training.disabled; }
  get trainingActionKey(): string { return this.training.actionKey; }
  get trainingStateKey(): string { return this.training.stateKey; }
  /** Kapitel- und Stellungszeit messen (ohne geladene Stellung: Hinweis statt Start). */
  startTraining(): void { this.training.start(); }
  /** Stoppen heißt auch ABRECHNEN (die gemessene Stellungszeit geht noch raus). */
  stopTraining(): void { this.training.stop(); }

  /** Angezeigte kumulierte Kapitel-Zeit (m:ss bzw. h:mm:ss). */
  get timerDisplay(): string {
    return this.timer.display;
  }

  /** Kommentar-Entwurf zum ausgewählten Zug. */
  get cursorComment(): string { return this.cursor.comment; }
  set cursorComment(text: string) { this.cursor.comment = text; }

  /** Speichern des Baums (Autosave, Speicherstand, Warteschlange je Stellung) — siehe {@link CalcTreeSaver}. */
  private readonly saver = new CalcTreeSaver({
    backend: () => this.backend,
    currentId: () => this.position?.id ?? null,
    tree: () => this.tree,
    markDone: (bookPuzzleId, done) => this.chapters.markDone(bookPuzzleId, done),
    onSaved: () => this.setLocalSaveFailed(false),
    onFailed: () => this.reportSaveError(),
  });

  get saving(): boolean { return this.saver.saving; }
  get savedAt(): Date | null { return this.saver.savedAt; }

  private subs = new Subscription();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private api: CalculationService,
    /** Öffentlich, weil die Vorlage Brett- und Figurenart DIREKT von hier liest: eine lokale
     *  Kopie wurde nur beim Start gefüllt, eine Änderung im Einstellungs-Dialog kam nie an. */
    public prefs: PreferencesService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private auth: AuthService,
    private dialog: MatDialog,
    readonly courseLang: CourseLanguageService,
  ) {}

  // ===== Sprache der Kommentare (Kurs-Übersetzung, Stufe C) =================

  /** Dieser Kurs für die Sprachwahl. */
  get langRef(): CourseRef {
    return { bookId: this.bookId };
  }

  /** Sprache gewählt: merken und NUR die Texte nachladen — Baum, Uhr und Bewertung bleiben stehen. */
  pickLanguage(lang: string): void {
    this.courseLang.setChoice(this.langRef, lang);
    this.loader.refreshTexts();
  }

  /** „maschinell übersetzt" angeklickt → aufs Original. */
  showOriginal(): void {
    const source = this.courseLang.source(this.langRef);
    if (source) this.pickLanguage(source);
  }

  ngOnInit(): void {
    this.pageMode.enter();
    this.bookId = Number(this.route.snapshot.paramMap.get('bookId'));
    this.loader.requestedChapter = (this.route.snapshot.queryParamMap.get('chapter') || '').trim() || null;
    // Niemand angemeldet → Stellungen lesend vom öffentlichen Endpoint, alles Selbstgemachte im Gerät.
    this.local.enter(this.api, this.bookId, !!this.auth?.isLoggedIn);
    // Welche Sprachen der Kurs hat (Auswahl in der Befehlszeile) — still, ohne Übersicht keine Auswahl.
    this.courseLang.ensureLanguages(this.bookId);
    const requested = Number(this.route.snapshot.queryParamMap.get('pos')) || null;
    this.loadBook(requested);
    this.training.startLiveTicker();
  }

  /** `<body>`-Klasse, Navbar-Breite und Vollbild-Ziel — siehe {@link CalcPageMode}. */
  private readonly pageMode = new CalcPageMode();

  /** Element, das der Vollbild-Knopf in der Leiste ins Vollbild schickt (siehe {@link CalcPageMode.fsTarget}). */
  get fsTarget(): HTMLElement | null { return this.pageMode.fsTarget; }

  /** Brett- und Figurenart — alles, was in DIESEM Modus etwas bewirkt. */
  openSettings(): void {
    this.dialog.open(CalcSettingsDialogComponent, { autoFocus: false });
  }

  ngOnDestroy(): void {
    this.stopTraining();     // stoppt UND schöpft die gemessene Zeit ab
    this.pageMode.leave();
    this.training.stopLiveTicker();
    this.clearSaveTimer();
    this.flushSave();
    this.harvestWatch();     // gemessene Zeit noch abschöpfen …
    this.sendReviews();      // … und mitsamt Wahl/Punkten rausschicken
    this.subs.unsubscribe();
  }

  // ===== Laden ==============================================================

  private loadBook(requestedPositionId: number | null): void { this.loader.loadBook(requestedPositionId); }
  /** Eine Stellung laden (Epoche gegen überholte Antworten, siehe {@link CalcPageLoader.loadPosition}). */
  private loadPosition(bookPuzzleId: number): void { this.loader.loadPosition(bookPuzzleId); }

  private applyPosition(pos: CalcPosition): void {
    this.timer.syncChapter(pos.chapter);
    this.beginWatch(pos.id);
    this.reviews.load(pos);
    const { fen: startFen, illegal } = calcStartFen(pos);
    this.illegalPosition = illegal;
    this.orientation = whiteToMove(startFen) ? 'white' : 'black';
    const stored = deserializeTree(pos.treeJson, startFen);
    this.cursor.reset(stored ?? createTree(startFen), startFen);
    this.saver.reset(pos);
    this.revision++;
  }

  // ===== Kapitel (Stufe 1 der Auswahl) ======================================

  /** Das gerade bearbeitete Kapitel; `null`, solange nichts geladen ist. */
  get chapter(): CalcPositionGroup | null { return this.chapters.chapter; }

  /** Nur die Stellungen dieses Kapitels — die einzige Liste, auf der navigiert wird. */
  get chapterPositions(): CalcPositionListItem[] { return this.chapters.chapterPositions; }

  get hasNextChapter(): boolean { return this.chapters.hasNextChapter; }

  /**
   * Kapitel wechseln. Das ist bewusst ein SCHNITT: offene Speicherungen raus, Training stoppen
   * (damit die Kapitel-Zeit nicht über die Grenze hinweg weiterläuft) und in der neuen Liste bei
   * der ersten unbearbeiteten Stellung einsteigen.
   */
  selectChapter(chapterIndex: number): void {
    if (chapterIndex === this.chapterIndex || !this.groups[chapterIndex]) return;
    this.stopTraining();          // Kapitelwechsel beendet das Training (und rechnet es ab)
    this.leaveCurrentPosition();
    this.enterChapter(chapterIndex, null, true);
  }

  /** Der Weg ins nächste Kapitel — ausdrücklich per Knopf, nie von selbst (siehe {@link chapterIndex}). */
  goToNextChapter(): void {
    if (this.hasNextChapter) this.selectChapter(this.chapterIndex + 1);
  }

  /** Die angezeigte Stellung ist die letzte des Kapitels (reine Ortsangabe, siehe {@link CalcChapterModel}). */
  get atLastPosition(): boolean { return this.chapters.atLastPosition; }

  /** „Kapitel durchgearbeitet" — nur, wenn man am Ende ANGEKOMMEN ist (siehe {@link CalcChapterModel}). */
  get atChapterEnd(): boolean { return this.chapters.atChapterEnd; }

  /** Kapitel öffnen und die Einstiegsstellung laden (Deep-Link, sonst erste unbearbeitete). */
  private enterChapter(chapterIndex: number, requestedPositionId: number | null, updateUrl: boolean): void {
    const id = this.chapters.enter(chapterIndex, requestedPositionId);
    if (id === null) { this.loading = false; return; }
    if (updateUrl) this.syncUrl(id);
    this.loadPosition(id);
  }

  /** Kapitel bilden und die Anzeige-Nummern je Stellung übernehmen (siehe {@link CalcChapterModel.groupPositions}). */
  private groupPositions(positions: CalcPositionListItem[]): CalcPositionGroup[] {
    return this.chapters.groupPositions(positions);
  }

  /** Nach einer eigenen Änderung sind die Server-Summen überholt — ab jetzt selbst rechnen. */
  private dropServerSums(): void {
    this.chapters.dropServerSums();
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

    this.cursor.addMove({
      san: move.san,
      uci: move.from + move.to + (move.promotion ?? ''),
      fen: chess.fen(),
    });
    this.markDirty();
    this.offerTrainingStart();
  }

  private offerTrainingStart(): void {
    this.training.offerStart(() => this.startTraining());
  }

  // ===== Navigation im Baum =================================================

  setCursor(nodeId: number): void { this.cursor.setCursor(nodeId); }

  /** Neue Linie ab der Ausgangsstellung: (+) setzt den Cursor auf die Wurzel. */
  startNewLine(): void { this.cursor.startNewLine(); }

  goBack(): void { this.cursor.goBack(); }

  goForward(): void { this.cursor.goForward(); }

  /** Linie wechseln (↑/↓): Cursor auf das Blatt der vorherigen/nächsten Linie. */
  switchLine(delta: number): void { this.cursor.switchLine(delta); }

  // ===== Bearbeiten =========================================================

  /** Ausgewählten Zug samt Fortsetzung löschen (= „Zug zurück", wenn er der letzte ist). */
  deleteFromCursor(): void {
    if (!this.cursor.deleteFromCursor()) return;
    this.markDirty();
    this.syncChoiceWithTree();
  }

  deleteLine(leafId: number): void {
    this.cursor.deleteLine(leafId);
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
    this.cursor.saveComment();
    this.markDirty();
  }

  onLineComment(event: { nodeId: number; text: string }): void {
    this.cursor.setComment(event.nodeId, event.text);
    this.markDirty();
  }

  flipBoard(): void {
    this.orientation = this.orientation === 'white' ? 'black' : 'white';
  }

  // ===== Festlegung / Bewertung / Rechenzeit ================================

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
   * Ergebnis eintragen: eine benannte Stufe oder `null` = „noch nicht bewertet" (ausdrücklich
   * ≠ Stufe 0 „nicht gelöst"). Die Auswahl selbst passiert im Dialog, nicht mehr inline.
   */
  applyGrade(grade: CalcGrade | null): void {
    this.setReview({ grade });
  }

  isGrade(grade: CalcGrade): boolean {
    return this.review.grade === grade;
  }

  /**
   * Der Knopf „Ergebnis" öffnet die Auswahl. Bewusst ein Dialog statt fünf Schaltern in der
   * Seitenspalte: der Modus soll ein Brett bleiben, kein Formular (UI-Dichte-Regel).
   */
  openGradeDialog(): void {
    const ref = this.dialog.open<CalcGradeDialogComponent, unknown, CalcGradeDialogResult>(
      CalcGradeDialogComponent, {
        data: { grade: this.review.grade, chosenSan: this.review.chosenSan },
        width: '440px',
        autoFocus: false,
      });
    this.subs.add(ref.afterClosed().subscribe(result => {
      // Durchgelassen wird nur ein ECHTES Ergebnis: eine Zahl = Stufe, `null` = Bewertung
      // ausdrücklich zurücknehmen. Alles andere heißt „weggeklickt, nichts ändern".
      //
      // Bewusst NICHT auf `undefined` prüfen: was hier ankommt, befüllt teils ein fremdes
      // Framework (ein `mat-dialog-close`-Attribut schließt mit dem leeren STRING). Ein solcher
      // Wert liefe über `normalizeGrade('') === null` als `clearGrade` an den Server — wer eine
      // bestehende Bewertung nur ansieht und abbricht, verlöre sie. Ein Löschbefehl muss aus
      // einer AUSDRÜCKLICHEN Handlung kommen, nicht aus einem Vorgabewert.
      if (typeof result === 'number' && Number.isFinite(result)) {
        this.applyGrade(normalizeGrade(result));
      } else if (result === null) {
        this.applyGrade(null);
      }
    }));
  }

  /** Optimistisch anzeigen + an den Server einreihen. */
  private setReview(patch: CalcReviewPatch): void {
    this.reviews.set(patch);
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

  // ===== Rechenzeit messen (CalcTrainingSession) =============================

  private beginWatch(bookPuzzleId: number): void { this.training.beginWatch(bookPuzzleId); }
  private harvestWatch(): void { this.training.harvestWatch(); }

  // ===== Senden (eigener Endpoint neben dem Baum) ===========================

  private sendReviews(): void {
    this.reviews.flush();
  }

  // ===== Anzeige-Helfer =====================================================

  get cursorNode(): CalcNode | undefined { return this.cursor.node; }
  get atStart(): boolean { return this.cursor.atStart; }
  get lineCount(): number { return this.cursor.lineCount; }
  /** Bearbeitete Stellungen des KAPITELS (alle Zahlen dieser Ansicht meinen das Kapitel). */
  get doneCount(): number { return this.chapters.doneCount; }
  /** Stellungen des Kapitels — Nenner von „3 / 6" und „bearbeitet". */
  get positionCount(): number { return this.chapterPositions.length; }
  get whiteToMoveAtCursor(): boolean { return this.cursor.whiteToMove; }

  /**
   * Ist der Tastatur-Hinweis aufgeklappt? Bewusst NUR im Speicher: er klappt bei jedem Besuch
   * wieder zu. Eine dauerhaft ausgeklappte Erklärung wäre wieder das Möbelstück, das sie vorher
   * war — sie stand fest da und wurde nach dem zweiten Mal nicht mehr gelesen.
   */
  showKeys = false;

  toggleKeys(): void { this.showKeys = !this.showKeys; }

  /**
   * Sind die Zug-Anmerkungen (Symbole, Einschätzung, Kommentar) eingeblendet? Wie der
   * Tastatur-Hinweis bewusst NUR im Speicher und standardmäßig zu: sie sind Feinarbeit an einem
   * einzelnen Zug und nicht das, was man beim Rechnen braucht.
   */
  showAnnotations = false;

  toggleAnnotations(): void { this.showAnnotations = !this.showAnnotations; }

  /**
   * Kapitelzeit von Hand nachtragen — für den Fall, dass das Training zu spät gestartet wurde.
   * Bewusst NUR die Kapitel-Uhr (Merkhilfe auf diesem Gerät): die je Stellung gemessene Zeit
   * entsteht aus echten Abschnitten und bliebe sonst keine Messung mehr.
   */
  openTimerDialog(): void {
    const ref = this.dialog.open<CalcTimerDialogComponent, CalcTimerDialogData, CalcTimerDialogResult | undefined>(
      CalcTimerDialogComponent,
      {
        data: {
          seconds: this.timerSeconds,
          chapter: this.labels.chapterName || this.translate.instant('courses.noChapter'),
        },
        autoFocus: false,
      });
    ref.afterClosed().subscribe(result => {
      // Nur eine echte Zahl zählt: ein Fremdwert (leerer String vom Framework) ist „weggeklickt".
      if (typeof result !== 'number' || !Number.isFinite(result)) return;
      this.timer.setSeconds(Math.max(0, Math.floor(result)));
    });
  }

  // ===== Analyse + FEN kopieren =============================================

  /** Aktuelle Cursor-FEN in die Zwischenablage (immer verfügbar). */
  copyFen(): void {
    const fen = this.cursorFen || this.startFen;
    if (!fen || !navigator.clipboard) return;
    navigator.clipboard.writeText(fen).then(
      () => this.snackbar.info(this.translate.instant('calc.fenCopied')),
      () => this.snackbar.warn(this.translate.instant('common.error')),
    );
  }

  /** Öffnet den Analyse-Modus mit den Zügen EINER Linie (Wurzel → <c>leafId</c>). Jede Linie ist
   *  linear und passt damit in den (einlinigen) Analyse-Modus; ein „from" führt zurück hierher. */
  analyzeLine(leafId: number): void {
    this.openAnalysis(this.cursor.movesTo(leafId));
  }

  /** Analyse der aktuell im Baum stehenden Linie (Wurzel → Cursor) — Knopf nach dem Bewerten. */
  analyzeCurrentLine(): void {
    this.openAnalysis(this.cursor.movesTo(this.cursorId));
  }

  private openAnalysis(uci: string[]): void {
    this.router.navigate(['/analysis'], {
      queryParams: { fen: this.startFen, moves: uci.join(','), from: this.analysisReturnUrl() },
    });
  }

  /**
   * Rückweg aus der Analyse: dieselbe Seite MIT Stellung und Kapitel (wie {@link syncUrl}). Ohne
   * `?pos` wählte die neu gebaute Komponente die erste OFFENE Stellung des ersten offenen Kapitels
   * — die gerade bewertete hat jetzt aber einen Baum, man landete also woanders. Nicht einfach
   * `router.url`: der erste Einstieg schreibt `pos` gar nicht in die URL (`enterChapter(…, false)`).
   */
  private analysisReturnUrl(): string {
    const id = this.position?.id ?? this.chapterPositions[this.index]?.id ?? null;
    if (id === null) return this.router.url;
    return this.router.serializeUrl(this.router.createUrlTree([], {
      relativeTo: this.route,
      queryParams: { pos: id, chapter: this.chapter?.chapter ?? null },
      queryParamsHandling: 'merge',
    }));
  }

  // ===== Stellungs-Navigation (Stufe 2: INNERHALB des Kapitels) =============

  hasPrev(): boolean { return this.chapters.hasPrev(); }
  hasNext(): boolean { return this.chapters.hasNext(); }

  prevPosition(): void { if (this.hasPrev()) this.goToIndex(this.index - 1); }
  nextPosition(): void { if (this.hasNext()) this.goToIndex(this.index + 1); }

  /** Sprung in der Liste. Zeigt der Sprung aus dem Kapitel heraus, wird das Kapitel gewechselt. */
  jumpToPosition(bookPuzzleId: number): void {
    const idx = this.chapterPositions.findIndex(p => p.id === bookPuzzleId);
    if (idx >= 0) {
      if (idx !== this.index) this.goToIndex(idx);
      return;
    }
    const other = this.chapters.chapterOf(bookPuzzleId);
    if (other < 0) return;
    this.stopTraining();          // auch das ist ein Kapitelwechsel
    this.leaveCurrentPosition();
    this.enterChapter(other, bookPuzzleId, true);
  }

  private goToIndex(index: number): void {
    this.leaveCurrentPosition();
    // Hier wird NAVIGIERT — nur so entsteht „am Ende angekommen" (und geht beim Zurückgehen
    // wieder verloren).
    const id = this.chapters.moveTo(index);
    this.syncUrl(id);
    this.loadPosition(id);
  }

  /**
   * Alles abschließen, was an der GERADE angezeigten Stellung hängt: offene Speicherung raus und
   * die an ihr gemessene Zeit abschöpfen (sonst ginge sie verloren, falls die nächste Stellung gar
   * nicht erst lädt).
   */
  private leaveCurrentPosition(): void {
    this.clearSaveTimer();
    this.flushSave();
    this.harvestWatch();
  }

  /** URL nachziehen: Stellung UND Kapitel, damit ein Neuladen dieselbe Ansicht wiederherstellt. */
  private syncUrl(bookPuzzleId: number): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { pos: bookPuzzleId, chapter: this.chapter?.chapter ?? null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // ===== Speichern (CalcTreeSaver) ==========================================

  private markDirty(): void {
    this.revision++;
    this.saver.markDirty();
  }

  /** Speichert sofort (Autosave-Timer, Stellungswechsel, Verlassen der Seite). */
  flushSave(): void {
    this.saver.flush();
  }

  private reportSaveError(): void {
    // Kein `dirty = true`: das Flag hängt am GERADE geladenen Baum — nach einem Stellungswechsel
    // hätte es den nächsten Flush auf die falsche (neue) Stellung gelenkt und den bearbeiteten
    // Baum der alten Stellung verworfen. Der Wiederholversuch läuft über die Warteschlange des CalcTreeSaver (je Stellung).
    this.setLocalSaveFailed(this.localOnly);   // anonym = Gerätespeicher (siehe `localSaveFailed`)
    this.snackbar.warn(this.translate.instant('calc.saveFailed'));
  }

  private clearSaveTimer(): void {
    this.saver.cancelAutosave();
  }

  // ===== Tastatur ===========================================================

  /**
   * Reload (F5) / Tab schließen / App in den Hintergrund: `ngOnDestroy` läuft dabei NICHT, die
   * seit dem letzten {@link harvestWatch} gemessene Stellungszeit und ein noch offener
   * Baum-/Bewertungs-Stand (bis zu CalcTreeSaver.AUTOSAVE_MS) gingen sonst still verloren. Für anonyme Nutzer
   * ist der Flush synchron (localStorage) und damit auch beim Unload zuverlässig — genau der
   * „anonyme Arbeit geht nicht still verloren"-Pfad. `pagehide` statt `beforeunload`: es feuert
   * auch beim mobilen App-Wechsel und stört keinen bfcache. (Review-Fund 2026-08-09; Endless hat
   * dasselbe Muster längst.) Doppelt-Flush mit ngOnDestroy ist harmlos — leere Outbox, kein Send.
   */
  @HostListener('window:pagehide')
  onPageHide(): void {
    this.harvestWatch();     // gemessene Stellungszeit sichern (Kapitel-Uhr persistiert ohnehin je Sekunde)
    this.flushSave();        // offenen Baum-Stand raus/lokal schreiben
    this.sendReviews();      // Festlegung/Zeit/Bewertung mitschicken
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!isBoardHotkey(event)) return;

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
