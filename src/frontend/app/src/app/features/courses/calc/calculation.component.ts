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
import { applyUci, tryFreeMove, tryLoadFen } from '../../puzzles/puzzle-move.util';
import { VisibilityStopwatch } from '../../puzzles/visibility-stopwatch';
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
  formatScore, gradePoints, maxPoints, newSecondsToken, normalizeGrade, sumPoints,
} from './calc-review.util';
import {
  CalcBackend, CalcBook, CalcPosition, CalcPositionListItem, CalculationService,
} from './calculation.service';
import { LocalCalculationBackend } from './calc-local.backend';
import { CalcTrainingTimer } from './calc-training-timer';
import { CalcTreeCursor } from './calc-tree-cursor';
import { CalcTreeSaver } from './calc-tree-saver';
import { CalcPageMode } from './calc-page-mode';
import { CalcReviewController } from './calc-review.controller';
import { CalcGradeDialogComponent, CalcGradeDialogResult } from './calc-grade-dialog.component';
import { CalcSettingsDialogComponent } from './calc-settings-dialog.component';
import { BoardFullscreenButtonComponent } from '../../../shared/fullscreen/board-fullscreen-button.component';
import {
  CalcTimerDialogComponent, CalcTimerDialogData, CalcTimerDialogResult,
} from './calc-timer-dialog.component';
import { readCalcNoticeDismissed, writeCalcNoticeDismissed } from './calc-local.util';
import { AuthService } from '../../../core/auth.service';
import { formatSecondsClock } from '../../../shared/clock-format.util';
import { CourseLanguageService, CourseRef } from '../course-language.service';
import { labelOr } from '../course-language.util';
import { CourseLangPickerComponent, MachineNoteComponent } from '../course-lang-picker.component';
import { isBoardHotkey } from '../../../shared/keyboard.util';
import {
  CalcChapterSums, CalcPositionGroup, applyChapterSums, chapterGroupLabel, groupByChapter,
  pickChapterIndex, serverChapterSums,
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
  book: CalcBook | null = null;
  /** ALLE Stellungen des Buchs (Quelle der Kapitel-Gruppen) — gearbeitet wird nie auf dieser Liste. */
  positions: CalcPositionListItem[] = [];
  /** Die Kapitel in Lesereihenfolge; „ohne Kapitel" ist eines davon. */
  groups: CalcPositionGroup[] = [];
  /**
   * Welches KAPITEL bearbeitet wird (Index in {@link groups}); -1 = noch keins gewählt.
   *
   * Der Modus ist kapitelweise: Weiter/Zurück, Sprungliste, Zähler und Punkte beziehen sich alle
   * auf dieses eine Kapitel. Am Kapitelende wird BEWUSST nicht weitergesprungen — sonst mischte
   * die Kapitel-Zeit (siehe Training) still über Kapitelgrenzen hinweg.
   */
  chapterIndex = -1;
  /** Stellung INNERHALB des Kapitels (Index in {@link chapterPositions}). */
  index = 0;

  /**
   * Ist der Nutzer am Kapitelende ANGEKOMMEN (durchnavigiert), statt dort einzusteigen? Nur dann
   * darf „Kapitel durchgearbeitet" behauptet werden (siehe {@link atChapterEnd}). Wird beim
   * Betreten eines Kapitels zurückgesetzt und bei jedem Sprung innerhalb des Kapitels nachgeführt.
   */
  private arrivedAtChapterEnd = false;

  /**
   * Angezeigte Nummer je Stellung: ihre Position IM KAPITEL, beginnend bei 1 (BookPuzzleId →
   * Nummer). Bewusst nicht `round`: das ist die buchweit fortlaufende Nummer und hat Lücken,
   * sobald Linien gelöscht wurden („#7" bis „#12" in einem Kapitel mit sechs Stellungen).
   * Reine Anzeige — in der Datenbank wird NICHTS umnummeriert.
   */
  private chapterNumbers = new Map<number, number>();

  // ===== Woher kommen die Daten? ===========================================
  // Angemeldet: der Server. Anonym (öffentlicher Kalkulations-Kurs per Kurz-URL): der
  // localStorage dieses Geräts. Genau EINE Öffnung gibt es serverseitig — LESEND, und nur für
  // ausdrücklich öffentlich freigegebene Bücher; geschrieben wird anonym gar nichts.
  private localBackend: LocalCalculationBackend | null = null;

  /** Server oder Gerät — die einzige Stelle, die diesen Unterschied kennt. */
  private get backend(): CalcBackend { return this.localBackend ?? this.api; }

  /** Läuft alles nur lokal? (dezenter Hinweis + Anmelde-Link in der Ansicht) */
  get localOnly(): boolean { return this.localBackend !== null; }

  /**
   * Der Gerätespeicher hat zuletzt NICHT mitgespielt (Privatmodus, Quota voll, gesperrt). Dann ist
   * „liegt nur auf diesem Gerät" die falsche Auskunft — es liegt gerade NIRGENDS. Der Hinweis oben
   * wechselt deshalb auf eine Warnung. Wird bei der nächsten erfolgreichen Speicherung zurückgesetzt.
   */
  localSaveFailed = false;

  /**
   * Der ruhige Hinweis „liegt nur auf diesem Gerät" wurde für DIESEN Kurs weggeklickt
   * (localStorage, siehe `calc-local.util.ts`). Er ist ein Vorschlag — den darf man abstellen.
   */
  noticeDismissed = false;

  /**
   * Die WARNUNG „kann gerade gar nicht gespeichert werden" wurde weggeklickt. Bewusst NUR im
   * Speicher und bewusst nicht dauerhaft: sie meldet Datenverlust, keinen Vorschlag — beim
   * nächsten fehlgeschlagenen Schreibversuch steht sie wieder da (siehe {@link setLocalSaveFailed}).
   */
  warningDismissed = false;

  /** Anmelde-Link führt GENAU hierher zurück (inkl. `?pos=`/`?chapter=`). */
  get loginReturnUrl(): string { return this.router.url; }

  /** Ziel des Turms („Zurück“): angemeldet die Kursübersicht. Gäste kommen nur über die Kurz-URL eines öffentlichen
   *  Kurses hierher — /courses zeigte ihnen nur die Anmeldemaske (UX-024), sie gehen zur Startseite. */
  get homeLink(): string { return this.auth?.isLoggedIn ? '/courses' : '/'; }

  /** Der ruhige Geräte-Hinweis (anonym, Speicher tut es) — wegklickbar. */
  get showLocalNotice(): boolean {
    return this.localOnly && !this.localSaveFailed && !this.noticeDismissed;
  }

  /** Die Warnung, dass gerade NICHTS gespeichert werden kann — nur für die Sitzung wegklickbar. */
  get showLocalWarning(): boolean {
    return this.localOnly && this.localSaveFailed && !this.warningDismissed;
  }

  dismissLocalNotice(): void {
    this.noticeDismissed = true;
    writeCalcNoticeDismissed(this.bookId);
  }

  /** Nur für diese Sitzung: der nächste Fehlschlag holt die Warnung zurück. */
  dismissLocalWarning(): void {
    this.warningDismissed = true;
  }

  /** Einzige Stelle, die `localSaveFailed` setzt: jeder NEUE Fehlschlag zeigt die Warnung wieder. */
  private setLocalSaveFailed(failed: boolean): void {
    if (failed) this.warningDismissed = false;
    this.localSaveFailed = failed;
  }

  /**
   * Kapitel-Wunsch aus `?chapter=` (Kurz-URL `/{slug}/{kapitel}`) — er WÄHLT das Kapitel vor,
   * filtert aber nichts mehr weg: gewechselt werden kann weiterhin zu jedem anderen Kapitel.
   * Was in der URL stand, kann anders geschrieben sein als der Kapitelname im Buch.
   */
  private requestedChapter: string | null = null;

  position: CalcPosition | null = null;
  loading = true;
  loadError = false;

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

  // ===== Kapitel-Training (Timer) ==========================================
  // Kumuliert die Rechenzeit JE KAPITEL — Uhr, Topf und Besitzer stehen in {@link CalcTrainingTimer}.
  // Solange nicht gestartet wurde, misst auch die Stellungs-Uhr nichts (siehe `beginWatch`)
  // und es geht kein `addSeconds` an den Server.
  private readonly timer = new CalcTrainingTimer({
    storageKey: () => `rookhub_calc_timer_${this.bookId}`,
    userId: () => this.auth?.currentUser?.userId ?? null,
    // Anonym meldet die Warnung „kann gerade nichts speichern" die Zeit ausdrücklich mit; zurückgenommen wird sie
    // hier nicht — das bleibt dem Baum-Speicher.
    onPersistFailed: () => this.setLocalSaveFailed(this.localOnly),
  });

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
  totalPoints = 0;
  totalMaxPoints = 0;
  /** Dieselben Zahlen fürs ganze Buch — dürfen vorkommen, aber nur ausdrücklich beschriftet. */
  bookPoints = 0;
  bookMaxPoints = 0;
  /** Sekunden der laufenden Sitzung an dieser Stellung (nur Anzeige; gesendet wird das Delta). */
  liveSeconds = 0;

  /** Misst die AKTIVE Zeit an der Stellung (pausiert, wenn der Tab weg ist). */
  private readonly watch = new VisibilityStopwatch();
  /** Stellung, zu der die laufende Uhr gehört; null = keine Uhr. */
  private watchPositionId: number | null = null;
  private liveHandle?: ReturnType<typeof setInterval>;
  /** Server-Summen je Kapitel; nach der ersten eigenen Änderung rechnet die Ansicht selbst. */
  private serverSums = new Map<string, CalcChapterSums>();
  private serverTotals: { points: number; maxPoints: number } | null = null;


  // ===== Kapitel-Training starten/stoppen ==================================

  toggleTraining(): void {
    if (this.timerRunning) this.stopTraining(); else this.startTraining();
  }

  /**
   * Darf JETZT gemessen werden? Nur mit geladener Stellung: der Kapitel-Topf
   * (`CalcTrainingTimer.hasChapter`) entsteht erst in {@link applyPosition} — ohne ihn speichert
   * die Uhr nichts und `beginWatch` hätte keine Stellung, der die Zeit gehört. Scheitert
   * das Laden (`loadError`), tickte die Anzeige also vor sich hin, ohne irgendetwas zu messen oder
   * zu sichern: eine Uhr, die lügt. Dann lieber gar nicht erst starten (und sagen, warum).
   */
  get canTrain(): boolean {
    return !this.loadError && this.position !== null && this.timer.hasChapter;
  }

  /** Stoppen muss immer gehen — gesperrt wird nur das STARTEN ohne geladene Stellung. */
  get trainingDisabled(): boolean {
    return !this.timerRunning && !this.canTrain;
  }

  /** Beschriftung/Erklärung des Trainings-Knopfes — inklusive des Grundes, wenn es nicht geht. */
  get trainingActionKey(): string {
    if (this.timerRunning) return 'calc.timer.stop';
    return this.canTrain ? 'calc.timer.start' : 'calc.timer.unavailable';
  }

  /** Laufzustand in Worten; ohne ladbare Stellung steht dort der Grund statt „gestoppt". */
  get trainingStateKey(): string {
    if (this.timerRunning) return 'calc.timer.running';
    return this.canTrain ? 'calc.timer.stopped' : 'calc.timer.unavailable';
  }

  /**
   * Ab jetzt wird gemessen — und zwar beides: die kumulierte KAPITEL-Zeit und (über die
   * VisibilityStopwatch) die Rechenzeit der angezeigten STELLUNG. Ohne diesen Start läuft nichts;
   * das bloße Ansehen einer Stellung ist kein Training.
   */
  startTraining(): void {
    if (this.timerRunning) return;
    // Ohne geladene Stellung misst und speichert nichts (siehe {@link canTrain}) — statt still ins
    // Leere zu zählen, geht der Start gar nicht erst los und sagt es.
    if (!this.canTrain) {
      this.snackbar.warn(this.translate.instant('calc.timer.unavailable'));
      return;
    }
    this.timer.start();
    // Die Stellungs-Uhr wird erst hier aufgezogen (`beginWatch` misst nur bei laufendem Training).
    if (this.position) this.beginWatch(this.position.id);
  }

  /**
   * Stoppen heißt auch ABRECHNEN: die bis hierher gemessene Stellungszeit geht noch raus
   * (`harvestWatch`), sonst wäre sie mit dem Stopp verloren.
   */
  stopTraining(): void {
    if (!this.timerRunning) return;
    this.timer.stop();
    this.harvestWatch();
  }

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
    markDone: (bookPuzzleId, done) => this.markPositionDone(bookPuzzleId, done),
    onSaved: () => this.setLocalSaveFailed(false),
    onFailed: () => this.reportSaveError(),
  });

  get saving(): boolean { return this.saver.saving; }
  get savedAt(): Date | null { return this.saver.savedAt; }

  private subs = new Subscription();
  /** Entwertet überholte Ladevorgänge (siehe loadPosition). */
  private loadEpoch = 0;

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
    this.refreshTexts();
  }

  /** „maschinell übersetzt" angeklickt → aufs Original. */
  showOriginal(): void {
    const source = this.courseLang.source(this.langRef);
    if (source) this.pickLanguage(source);
  }

  /**
   * Überschriften und Aufgabentext in der gewählten Sprache holen. Übernommen werden nur die TEXTE
   * (Labels, Kommentar, Sprache, maschinell?) — die Stellungsliste trägt daneben Festlegung, Zeit und
   * Bewertung, die hier gerade in Arbeit sein können.
   */
  private refreshTexts(): void {
    const lang = this.courseLang.requestLang(this.langRef);
    this.subs.add(this.backend.getBook(this.bookId, lang).subscribe({
      next: book => {
        const byId = new Map(book.positions.map(p => [p.id, p]));
        for (const item of this.positions) {
          const fresh = byId.get(item.id);
          if (!fresh) continue;
          item.titleLabel = fresh.titleLabel ?? null;
          item.chapterLabel = fresh.chapterLabel ?? null;
        }
        for (const g of this.groups) g.label = chapterGroupLabel(g.items, g.chapter);
        const pos = this.position;
        if (!pos) return;
        this.subs.add(this.backend.getPosition(pos.id, lang).subscribe({
          next: fresh => {
            if (this.position?.id !== fresh.id) return;   // inzwischen weitergeblättert
            Object.assign(this.position, {
              comment: fresh.comment,
              titleLabel: fresh.titleLabel ?? null,
              chapterLabel: fresh.chapterLabel ?? null,
              commentLanguage: fresh.commentLanguage ?? null,
              commentMachine: !!fresh.commentMachine,
            });
          },
          error: () => { /* der Text bleibt, wie er war */ },
        }));
      },
      error: () => { /* Beiwerk — die Arbeit an der Stellung geht weiter */ },
    }));
  }

  ngOnInit(): void {
    this.pageMode.enter();
    this.bookId = Number(this.route.snapshot.paramMap.get('bookId'));
    this.requestedChapter = (this.route.snapshot.queryParamMap.get('chapter') || '').trim() || null;
    // Niemand angemeldet → Stellungen lesend vom öffentlichen Endpoint, alles Selbstgemachte
    // in den localStorage. Serverseitige Persistenz bleibt angemeldeten Nutzern vorbehalten.
    if (!this.auth?.isLoggedIn && Number.isFinite(this.bookId)) {
      this.localBackend = new LocalCalculationBackend(this.api, this.bookId);
    }
    this.noticeDismissed = readCalcNoticeDismissed(this.bookId);
    // Welche Sprachen der Kurs hat (Auswahl in der Befehlszeile) — still, ohne Übersicht keine Auswahl.
    this.courseLang.ensureLanguages(this.bookId);
    const requested = Number(this.route.snapshot.queryParamMap.get('pos')) || null;
    this.loadBook(requested);
    // Nur Anzeige: die Stoppuhr selbst zählt ohne Takt weiter (und pausiert bei verstecktem Tab).
    this.liveHandle = setInterval(() => { this.liveSeconds = this.currentWatchSeconds(); }, 1000);
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
    this.subs.add(this.backend.getBook(this.bookId, this.courseLang.requestLang(this.langRef)).subscribe({
      next: book => {
        this.book = book;
        this.positions = book.positions.map(p => this.normalizeItem(p));
        this.groups = this.groupPositions(this.positions);
        this.takeServerSums(book);
        if (this.positions.length === 0) { this.loading = false; return; }
        // Erst das KAPITEL, dann die Stellung darin — der Modus arbeitet ein Kapitel durch.
        this.enterChapter(pickChapterIndex(this.groups, this.requestedChapter, requestedPositionId), requestedPositionId, false);
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
    this.subs.add(this.backend.getPosition(bookPuzzleId, this.courseLang.requestLang(this.langRef)).subscribe({
      next: pos => {
        if (epoch !== this.loadEpoch) return;
        // Ein früherer Fehlschlag (Funkloch, 502 beim Deploy) darf nicht kleben bleiben: sonst
        // bliebe `canTrain` für den Rest des Besuchs false, obwohl diese Stellung geladen ist.
        this.loadError = false;
        this.position = pos;
        this.applyPosition(pos);
        this.loading = false;
      },
      error: () => {
        if (epoch !== this.loadEpoch) return;
        this.loading = false;
        this.loadError = true;
        // Beim WEITERblättern stünde sonst weiter das Brett der VORHERIGEN Stellung da, während
        // Index, URL und Sprungliste schon auf der neuen stehen — dieselbe Verwechslung, gegen die
        // oben der Epoch-Zähler schützt, nur über den Fehlerpfad. Die Arbeit an der alten Stellung
        // ist zu diesem Zeitpunkt bereits abgeflossen (`leaveCurrentPosition` vor dem Laden), es
        // geht also nichts verloren; ohne Stellung zeigt die Vorlage die Fehlermeldung.
        this.position = null;
        // Und ohne Stellung wird nicht gemessen: sonst tickte die Kapitel-Uhr weiter, während man
        // auf eine Fehlermeldung schaut. `stopTraining` sichert die bis hierhin gemessene Zeit.
        this.stopTraining();
      },
    }));
  }

  private applyPosition(pos: CalcPosition): void {
    this.timer.syncChapter(pos.chapter);
    this.beginWatch(pos.id);
    this.reviews.load(pos);
    const startFen = this.buildStartFen(pos);
    this.orientation = whiteToMove(startFen) ? 'white' : 'black';
    const stored = deserializeTree(pos.treeJson, startFen);
    this.cursor.reset(stored ?? createTree(startFen), startFen);
    this.saver.reset(pos);
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

  // ===== Kapitel (Stufe 1 der Auswahl) ======================================

  /** Das gerade bearbeitete Kapitel; `null`, solange nichts geladen ist. */
  get chapter(): CalcPositionGroup | null {
    return this.groups[this.chapterIndex] ?? null;
  }

  /** Nur die Stellungen dieses Kapitels — die einzige Liste, auf der navigiert wird. */
  get chapterPositions(): CalcPositionListItem[] {
    return this.chapter?.items ?? [];
  }

  /** Anzeigename des Kapitels („ohne Kapitel" ist auch einer). */
  get chapterName(): string {
    const group = this.chapter;
    if (!group) return '';
    return group.label || group.chapter || this.translate.instant('courses.noChapter');
  }

  get hasNextChapter(): boolean { return this.chapterIndex >= 0 && this.chapterIndex < this.groups.length - 1; }

  get nextChapterName(): string {
    const next = this.groups[this.chapterIndex + 1];
    if (!next) return '';
    return next.label || next.chapter || this.translate.instant('courses.noChapter');
  }

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

  /**
   * Reine ORTSANGABE: die angezeigte Stellung ist die letzte des Kapitels. Daran hängt der WEG ins
   * nächste Kapitel — der darf immer offen stehen, auch wenn hier gerade erst eingestiegen wurde.
   */
  get atLastPosition(): boolean {
    const items = this.chapterPositions;
    return items.length > 0 && this.index >= items.length - 1;
  }

  /**
   * „Kapitel durchgearbeitet" — das ist eine BEHAUPTUNG, und die muss stimmen. Sie gilt erst, wenn
   * man am Ende ANGEKOMMEN ist (siehe {@link arrivedAtChapterEnd}), nicht wenn man dort startet:
   * am Index allein hängend meldete ein Kapitel mit genau EINER Stellung ab der ersten Sekunde
   * „durch", ebenso ein Deep-Link (`?pos=`) auf die letzte Stellung.
   */
  get atChapterEnd(): boolean {
    return this.atLastPosition && this.arrivedAtChapterEnd;
  }

  /** Kapitel öffnen und die Einstiegsstellung laden (Deep-Link, sonst erste unbearbeitete). */
  private enterChapter(chapterIndex: number, requestedPositionId: number | null, updateUrl: boolean): void {
    this.chapterIndex = chapterIndex;
    // Einsteigen ist kein Durcharbeiten — auch nicht, wenn der Einstieg auf der letzten Stellung
    // liegt (Deep-Link, Kapitel mit einer einzigen Stellung).
    this.arrivedAtChapterEnd = false;
    this.refreshSums();
    const items = this.chapterPositions;
    if (items.length === 0) { this.loading = false; return; }
    const wanted = requestedPositionId != null ? items.findIndex(p => p.id === requestedPositionId) : -1;
    const firstOpen = items.findIndex(p => !p.hasTree);
    this.index = wanted >= 0 ? wanted : (firstOpen >= 0 ? firstOpen : 0);
    const id = items[this.index].id;
    if (updateUrl) this.syncUrl(id);
    this.loadPosition(id);
  }

  /** Kapitel bilden ({@link groupByChapter}) und die Anzeige-Nummern je Stellung übernehmen. */
  private groupPositions(positions: CalcPositionListItem[]): CalcPositionGroup[] {
    const { groups, numbers } = groupByChapter(positions);
    this.chapterNumbers = numbers;
    return groups;
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
    this.serverSums = serverChapterSums(book.chapters);
    this.serverTotals = this.pickTotals(book);
    this.refreshSums();
  }

  /** Buchsumme, falls der Server eine mitliefert (sonst rechnet {@link refreshSums} sie selbst). */
  private pickTotals(book: CalcBook): { points: number; maxPoints: number } | null {
    return typeof book.points === 'number'
      ? { points: book.points, maxPoints: book.maxPoints ?? maxPoints(this.positions.length) }
      : null;
  }

  /**
   * Kapitel-Summen + Buchsumme neu setzen (Server-Werte, solange vorhanden).
   *
   * Die angezeigte Gesamtsumme ist die des KAPITELS: der Nutzer steht in einem Kapitel, eine Zahl
   * über das ganze Buch gehörte zu nichts Sichtbarem („19 / 48" unter sechs Stellungen). Sie kommt
   * fertig aus `chapters[]` — dieselbe serverseitige Wahrheit im richtigen Zuschnitt, KEIN zweiter
   * Rechenweg im Client. Die Buchsumme steht daneben, aber ausdrücklich als solche beschriftet.
   */
  private refreshSums(): void {
    applyChapterSums(this.groups, this.serverSums);
    const chapter = this.chapter;
    this.totalPoints = chapter ? chapter.points : 0;
    this.totalMaxPoints = chapter ? chapter.maxPoints : 0;
    this.bookPoints = this.serverTotals ? this.serverTotals.points : sumPoints(this.positions);
    this.bookMaxPoints = this.serverTotals?.maxPoints || maxPoints(this.positions.length);
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

    this.cursor.addMove({
      san: move.san,
      uci: move.from + move.to + (move.promotion ?? ''),
      fen: chess.fen(),
    });
    this.markDirty();
    this.offerTrainingStart();
  }

  /**
   * Wer anfängt zu rechnen, während das Training nicht läuft, misst nichts — und merkt es oft
   * erst hinterher. Ein Hinweis mit Sofort-Schalter statt eines Dialogs: er hält beim Rechnen
   * niemanden auf. Gefragt wird EINMAL je Besuch; wer nicht will, soll nicht bei jedem Zug
   * erneut gefragt werden.
   */
  private offerTrainingStart(): void {
    if (this.timerRunning || this.timerAsked || !this.canTrain) return;
    this.timerAsked = true;
    const ref = this.snackbar.warn(this.translate.instant('calc.timer.notRunning'), {
      action: 'calc.timer.start',
      duration: 10000,
    });
    ref.onAction().subscribe(() => this.startTraining());
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

  /** Beschriftung des Knopfes: unbewertet „Ergebnis", sonst die gewählte Stufe. */
  get gradeButtonLabel(): string {
    return this.review.grade === null
      ? this.translate.instant('calc.review.result')
      : this.gradeShortLabel(this.review.grade);
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

  // ===== Rechenzeit messen ==================================================

  /**
   * Uhr für die angezeigte Stellung neu aufziehen (vorherige Zeit vorher abschöpfen).
   *
   * NUR bei gestartetem Kapitel-Training: das bloße Ansehen einer Stellung ist kein Training, und
   * ohne laufende Uhr geht auch kein `addSeconds` an den Server.
   */
  private beginWatch(bookPuzzleId: number): void {
    this.harvestWatch();
    if (!this.timerRunning) return;
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
    // Jedes gemessene Delta bekommt seine eigene Marke: der Server ADDIERT die Zeit, und ein
    // Wiederholversuch (Antwort verloren, Anfrage angekommen) darf sie nicht doppelt buchen.
    this.reviews.creditSeconds(bookPuzzleId, seconds, newSecondsToken());
  }

  private currentWatchSeconds(): number {
    return this.watchPositionId === null ? 0 : this.watch.elapsedSeconds;
  }

  // ===== Senden (eigener Endpoint neben dem Baum) ===========================

  private sendReviews(): void {
    this.reviews.flush();
  }

  // ===== Anzeige-Helfer =====================================================

  get cursorNode(): CalcNode | undefined { return this.cursor.node; }
  get atStart(): boolean { return this.cursor.atStart; }
  get lineCount(): number { return this.cursor.lineCount; }
  /** Bearbeitete Stellungen des KAPITELS (alle Zahlen dieser Ansicht meinen das Kapitel). */
  get doneCount(): number { return this.chapterPositions.filter(p => p.hasTree).length; }
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

  /** Wurde in diesem Besuch schon gefragt, ob das Training starten soll? Nur EINMAL fragen. */
  private timerAsked = false;

  /** Erklärung an der Uhr: Laufzustand plus der Hinweis, dass man sie anklicken kann. */
  get timerTooltip(): string {
    return `${this.translate.instant(this.trainingStateKey)}\n`
      + this.translate.instant('calc.timer.editHint');
  }

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
          chapter: this.chapterName || this.translate.instant('courses.noChapter'),
        },
        autoFocus: false,
      });
    ref.afterClosed().subscribe(result => {
      // Nur eine echte Zahl zählt: ein Fremdwert (leerer String vom Framework) ist „weggeklickt".
      if (typeof result !== 'number' || !Number.isFinite(result)) return;
      this.timer.setSeconds(Math.max(0, Math.floor(result)));
    });
  }

  /**
   * Erklärung am Kapitelstand in der Befehlszeile. Die BUCH-Summe steht hier statt als eigene
   * Zeile: interessant, aber nie dringend — und als Dauerzeile war sie die vierte Punktzahl auf
   * einem Schirm.
   */
  get pointsTooltip(): string {
    const chapter = this.translate.instant('calc.review.totalPointsChapterHint');
    const book = this.translate.instant('calc.review.bookPoints', {
      score: formatScore(this.bookPoints, this.bookMaxPoints),
    });
    return `${chapter}\n${book}`;
  }

  positionLabel(item: CalcPositionListItem): string {
    const title = labelOr(item.titleLabel, item.title);
    return title?.trim() ? title : `#${this.chapterNumberOf(item)}`;
  }

  /**
   * Die ANGEZEIGTE Nummer einer Stellung: ihre Position im Kapitel (1, 2, 3 …). Fällt auf
   * `round` zurück, solange die Gruppen noch nicht stehen — `round`/`id` selbst bleiben
   * unverändert, sie sind die Identität der Linie.
   */
  chapterNumberOf(item: CalcPositionListItem): string {
    const number = this.chapterNumbers.get(item.id);
    return number ? String(number) : item.round;
  }

  /** Rechenzeit an der ANGEZEIGTEN Stellung inkl. der laufenden Sitzung. */
  get positionTimeDisplay(): string {
    return formatSecondsClock(this.review.secondsSpent + this.liveSeconds);
  }

  timeDisplay(seconds: number): string {
    return formatSecondsClock(seconds);
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
    const name = group.label || group.chapter || this.translate.instant('courses.noChapter');
    const score = formatScore(group.points, group.maxPoints);
    const summary = group.seconds
      ? this.translate.instant('calc.review.chapterSummary', { score, time: formatSecondsClock(group.seconds) })
      : this.translate.instant('calc.review.chapterScore', { score });
    return `${name} · ${summary}`;
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

  hasPrev(): boolean { return this.index > 0; }
  hasNext(): boolean { return this.index < this.chapterPositions.length - 1; }

  prevPosition(): void { if (this.hasPrev()) this.goToIndex(this.index - 1); }
  nextPosition(): void { if (this.hasNext()) this.goToIndex(this.index + 1); }

  /** Sprung in der Liste. Zeigt der Sprung aus dem Kapitel heraus, wird das Kapitel gewechselt. */
  jumpToPosition(bookPuzzleId: number): void {
    const idx = this.chapterPositions.findIndex(p => p.id === bookPuzzleId);
    if (idx >= 0) {
      if (idx !== this.index) this.goToIndex(idx);
      return;
    }
    const other = this.groups.findIndex(g => g.items.some(p => p.id === bookPuzzleId));
    if (other < 0) return;
    this.stopTraining();          // auch das ist ein Kapitelwechsel
    this.leaveCurrentPosition();
    this.enterChapter(other, bookPuzzleId, true);
  }

  private goToIndex(index: number): void {
    this.leaveCurrentPosition();
    this.index = index;
    // Hier wird NAVIGIERT — nur so entsteht „am Ende angekommen" (und geht beim Zurückgehen
    // wieder verloren).
    this.arrivedAtChapterEnd = this.atLastPosition;
    const id = this.chapterPositions[index].id;
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

  private markPositionDone(bookPuzzleId: number, done: boolean): void {
    const item = this.positions.find(p => p.id === bookPuzzleId);
    if (item) item.hasTree = done;
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
