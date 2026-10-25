import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GuessAccept, GuessHistoryMove, GuessResult, GuessReviewMove, GuessService, GuessSession } from './guess.service';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { GuessPlayback } from './guess-playback';
import { GuessHistoryPanelComponent, GuessNote } from './guess-history-panel.component';
import { GuessSettingsMenuComponent } from './guess-settings-menu.component';
import { SanPipe } from '../../shared/chess/san.pipe';

/** Umwandlungsfigur aus dem SAN („e8=Q+" → „q"); leer, wenn der Zug keine Umwandlung ist. */
function promotionOf(san: string | undefined): string {
  const i = san?.indexOf('=') ?? -1;
  return i >= 0 && san!.length > i + 1 ? san![i + 1].toLowerCase() : '';
}

/** Was ein Rateversuch bis zur Antwort festhaelt (Stellung davor, geratener Halbzug). */
interface PendingGuess {
  id: number;
  uci: string | null;
  fenBefore: string;
  lastBefore: [string, string] | undefined;
  plyGuessed: number;
}

/** Stabil leer — eine neue Liste je Durchlauf waere fuer das OnPush-Kind eine neue Eingabe. */
const NO_LANGUAGES: string[] = [];

/**
 * Punktepartie (`/guess/:id`): Zug für Zug raten, sofort eine Rückmeldung samt Punkten, dann rückt
 * die Partie zwei Halbzüge weiter (eigener Zug + Antwort des Gegners).
 *
 * <p>Der Partiezug kommt erst mit der ANTWORT des Servers — vorher kennt der Client ihn nicht
 * (siehe `GuessSessionService`). Deshalb setzt das Brett den geratenen Zug auch nicht selbst um:
 * gezeigt wird, was der Server zurückmeldet.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-guess-board',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, TranslatePipe, ChessBoardComponent, LoadingSpinnerComponent, HelpHintComponent, SanPipe,
    GuessHistoryPanelComponent, GuessSettingsMenuComponent],
  templateUrl: './guess-board.component.html',
  styleUrl: './guess-board.component.scss',
})
export class GuessBoardComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private service = inject(GuessService);
  private prefs = inject(PreferencesService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  session: GuessSession | null = null;
  last: GuessResult | null = null;
  review: GuessReviewMove[] = [];
  loading = true;
  busy = false;

  /**
   * Abspielsteuerung nach einem Rateversuch: haelt einen abweichenden eigenen Zug bzw. einen
   * Kommentar fest und spielt die dazugekommenen Halbzuege einzeln ab (Zerlegung Z24).
   */
  readonly playback = new GuessPlayback({
    showStep: step => this.showStep(step),
    apply: s => this.apply(s),
    setBusy: busy => { this.busy = busy; },
    clearStepNote: () => { this.stepNote = null; },
    gameMoveUci: () => this.last ? this.last.gameMoveUci : undefined,
    changed: () => this.cdr.markForCheck(),
  });
  /** Ein Zug, der NICHT der Partiezug war, bleibt mit der Info darunter stehen — bis „Weiter". */
  get holding(): boolean { return this.playback.holding; }
  /** Wird gerade wegen eines Kommentars gehalten? Dann spielt „Weiter" den naechsten Schritt. */
  get holdingNote(): boolean { return this.playback.holdingNote; }

  /**
   * Welcher Zug der Eröffnung gerade angeschaut wird: `null` = die Aufgabe selbst, `-1` = die
   * Stellung vor dem ersten Zug, sonst der Index in `session.history`. Nur eine ANSICHT — der
   * Zustand der Sitzung bleibt davon unberührt, das Brett ist derweil gesperrt.
   */
  browseIndex: number | null = null;

  /** Was das Brett zeigt: die zu ratende Stellung bzw. nach einem Zug die Stellung danach. */
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  lastMove?: [string, string];

  private since = Date.now();

  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }
  get canGuess(): boolean {
    return !!this.session?.position && !this.busy && !this.holding && !this.browsing
      && this.session.status === 'running';
  }

  /**
   * „Schaut gerade zurueck" — und damit gesperrt. Der LETZTE Eintrag des Verlaufs erzeugt IMMER die
   * Aufgabenstellung (der Server schneidet die Liste genau davor ab), dort darf also gezogen werden.
   */
  get browsing(): boolean {
    const i = this.browseIndex;
    return i !== null && i !== (this.session?.history.length ?? 0) - 1;
  }

  /** Steht das Brett auf der Aufgabe (dann fuehrt „vor" nirgendwohin)? */
  get atTask(): boolean { return !this.browsing; }
  get atStart(): boolean { return this.browseIndex === -1; }

  /** Die Sprachen, in denen die Anmerkungen dieser Partie vorliegen. */
  get commentLanguages(): string[] { return this.session?.commentLanguages ?? NO_LANGUAGES; }

  /**
   * Sprache der Anmerkungen umschalten. Geholt wird die SITZUNG neu — die Kommentare haengen am
   * Verlauf, und der kommt vom Server; im Browser liegt immer nur die eine gewaehlte Fassung.
   * Die Wahl merkt sich das Geraet, damit sie nicht bei jeder Partie neu zu treffen ist.
   */
  chooseLanguage(code: string): void {
    if (!this.session || code === this.session.commentLanguage) return;
    this.language = code;
    try { localStorage.setItem(GuessBoardComponent.LanguageKey, code); } catch { /* gesperrt */ }

    this.service.get(this.session.id, code).subscribe({
      next: s => {
        this.session = s;
        // Der Hinweis ueber dem Brett gehoert zum zuletzt gespielten Zug und steht jetzt in der
        // neuen Sprache im Verlauf.
        this.stepNote = null;
        this.showOpeningNote(s);
        this.cdr.markForCheck();
      },
      error: () => { /* die alte Fassung bleibt stehen */ },
    });
  }

  /**
   * Der Kommentar des gerade angesehenen Zuges. Nur beim Durchblaettern — auf der Aufgabe selbst
   * (`browseIndex === null`) gibt es keinen: der Kommentar zum zu ratenden Zug waere die Loesung.
   * Gemerkt je Verlaufseintrag, damit das Kind (OnPush) eine STABILE Eingabe bekommt.
   */
  get browsedComment(): GuessNote | null {
    const i = this.browseIndex;
    // Wer blaettert, will DEN Zug lesen, den er angeklickt hat — sonst gilt der Kommentar aus dem
    // Spielverlauf (zum eben gespielten Partiezug bzw. zur Antwort des Gegners).
    if (i === null) return this.stepNote;
    if (i < 0) return null;
    const m = this.session?.history[i];
    if (!m?.comment) return null;
    if (m !== this.browsedNoteOf) {
      const dots = m.white ? '.' : '…';
      this.browsedNoteOf = m;
      this.browsedNote = { move: `${m.moveNumber}${dots}${m.san}`, text: m.comment, language: m.commentLanguage };
    }
    return this.browsedNote;
  }
  private browsedNoteOf?: GuessHistoryMove;
  private browsedNote: GuessNote | null = null;

  /**
   * Einen Halbzug vor (+1) oder zurueck (-1). Das ENDE ist der letzte Eintrag des Verlaufs, also die
   * Aufgabenstellung — weiter geht es nicht, und genau deshalb kann das Blaettern die Loesung nicht
   * verraten.
   */
  step(delta: number): void {
    const n = this.session?.history.length ?? 0;
    const cur = this.browseIndex ?? n - 1;
    const next = cur + delta;
    if (next >= n - 1) { this.browse(null); return; }
    this.browse(Math.max(-1, next));
  }

  /** Was das Brett zeigt: die Aufgabe (bzw. der gehaltene Zug) — oder die angeklickte Eröffnungsstellung. */
  get viewFen(): string {
    if (this.browseIndex === null) return this.boardFen;
    if (this.browseIndex < 0) return this.session?.startFen || this.boardFen;
    return this.session?.history[this.browseIndex]?.fen || this.boardFen;
  }

  /**
   * Der hervorgehobene Zug — als STABILES Tupel gemerkt. Ein Getter, der beim Blaettern jedes Mal
   * ein neues `[from, to]`-Literal liefert, laesst unter Default-Change-Detection in JEDEM
   * Durchlauf `ngOnChanges` des Bretts feuern; chessground zeichnet dann jedes Mal komplett neu.
   * Dieselbe Falle wie beim Kartenmittelpunkt im Turnierkalender.
   */
  private lastMoveCache?: [string, string];
  private lastMoveKey = '';

  get viewLastMove(): [string, string] | undefined {
    if (this.browseIndex === null) return this.lastMove;
    if (this.browseIndex < 0) return undefined;                 // Grundstellung: es gab keinen Zug
    const uci = this.session?.history[this.browseIndex]?.uci;
    const key = uci ?? '';
    if (key !== this.lastMoveKey) {
      this.lastMoveKey = key;
      this.lastMoveCache = uci ? [uci.slice(0, 2), uci.slice(2, 4)] : undefined;
    }
    return this.lastMoveCache;
  }

  /** Einen Eröffnungszug anschauen (`null` = zurück zur Aufgabe, `-1` = Grundstellung). */
  browse(index: number | null): void {
    this.browseIndex = index;
    this.cdr.markForCheck();
  }

  /**
   * Wann der Abstand zum Partiezug eine Zahl wert ist: wenn der eigene Zug schlechter war (dann will
   * man wissen, wie teuer) — und wenn er DEUTLICH besser war (dann will man wissen, wie viel man
   * gefunden hat). Dazwischen (gleichwertig, knapp besser: unter 0,2 Bauern Unterschied) sagt die
   * Zahl nichts, was die Stufe nicht schon sagt, und der Partiezug allein genügt.
   */
  get showsDelta(): boolean {
    const g = this.last?.grade;
    return g === 'worse' || g === 'muchWorse' || g === 'clearlyBetter';
  }

  /** Bewertungsänderung gegenüber dem Partiezug in Bauerneinheiten (siehe <c>showsDelta</c>). */
  get evalDelta(): string | null {
    const cp = this.last?.diffCp;
    if (cp === null || cp === undefined || !this.showsDelta) return null;
    const pawns = cp / 100;
    return (pawns > 0 ? '+' : '') + pawns.toFixed(2);
  }

  /**
   * UI-Sweep 2026-10-11 (r2-rg-guess-start): wie viele ganze Züge vor der Aufgabe vorgespielt sind
   * („Zug 10" → 9) — für den Hinweis beim Durchsehen der Eröffnung.
   */
  get preMoves(): number {
    const p = this.session?.position;
    return p ? Math.max(0, p.moveNumber - 1) : 0;
  }

  get moveLabel(): string {
    const p = this.session?.position;
    return p ? `${p.moveNumber}${p.whiteToMove ? '.' : '…'}` : '';
  }

  get progress(): number {
    const s = this.session;
    return s && s.totalGuesses > 0 ? Math.round((100 * s.movesPlayed) / s.totalGuesses) : 0;
  }

  ngOnInit(): void {
    this.loadAccept();
    this.loadLanguage();
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.service.get(id, this.language).subscribe({
      next: s => {
        this.apply(s);
        // Eine NEUE Sitzung faengt am Anfang der Partie an, damit man sich die Eroeffnung ansehen
        // kann; am letzten Eintrag des Verlaufs steht die Aufgabe, ab dort darf geraten werden.
        // Eine fortgesetzte Sitzung startet dagegen dort, wo man aufgehoert hat.
        if (s.history.length && s.movesPlayed === 0) this.browseIndex = -1;
        this.showOpeningNote(s);
        // Ein beendeter Lauf (aus „Deine Durchlaeufe" wieder geoeffnet) zeigt seinen Rueckblick
        // erneut — sonst gaebe es ihn nur direkt nach dem Schlusszug und nie wieder.
        if (s.status === 'done') this.loadReview(s.id);
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => { this.loading = false; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void { this.playback.dispose(); }

  /**
   * Blaettern mit den Pfeiltasten. Wer eine Partie nachspielt, hat die Hand nicht an der Maus —
   * und die vier Knoepfe unter dem Brett sind genau die vier Tasten, die dafuer auf jeder Tastatur
   * liegen (Pos1 = Anfang, Ende = zurueck zur Aufgabe).
   *
   * <p>Das VERRAET nichts: das Blaettern endet am letzten Eintrag des Verlaufs, also an der
   * Aufgabenstellung — die Fortsetzung liegt gar nicht im Browser.</p>
   *
   * <p>Zwei Faelle bleiben aussen vor: eine Eingabe (dort gehoeren die Tasten dem Cursor) und ein
   * offenes Menue/Dialog, wo die Pfeile die Auswahl bewegen. Ohne diese Ausnahme haette das
   * Einstellungs-Menue ueber dem Brett beim Tastendruck gleichzeitig die Auswahl verschoben und
   * das Brett umgeblaettert.</p>
   */
  @HostListener('window:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (e.shiftKey || !isBoardHotkey(e)) return;
    if (!this.session?.history.length) return;

    switch (e.key) {
      case 'ArrowLeft': this.step(-1); break;
      case 'ArrowRight': this.step(1); break;
      case 'Home': this.browse(-1); break;
      case 'End': this.browse(null); break;
      default: return;
    }
    e.preventDefault();
  }

  /** Der Speicher kann gesperrt sein (Privatmodus) — dann gilt eben die Vorgabe. */
  private loadAccept(): void {
    try {
      const raw = localStorage.getItem(GuessBoardComponent.AcceptKey);
      if (raw) {
        const v = JSON.parse(raw) as Partial<GuessAccept>;
        this.accept = { better: v.better !== false, similar: v.similar !== false };
      }
    } catch { /* Vorgabe behalten */ }
  }

  saveAccept(): void {
    try {
      localStorage.setItem(GuessBoardComponent.AcceptKey, JSON.stringify(this.accept));
    } catch { /* nicht speicherbar: gilt fuer diesen Aufruf trotzdem */ }
  }

  /**
   * Was als „erledigt" gilt. Wer den PARTIEZUG finden will, laesst einen besseren oder gleich guten
   * Zug NICHT zaehlen — die Aufgabe bleibt dann stehen. Bei einer Meisterpartie ist genau das der
   * Sinn: gesucht ist der Plan des Spielers, nicht der der Engine.
   *
   * Liegt im localStorage, weil es eine Gewohnheit des Nutzers ist und keine Eigenschaft der
   * einzelnen Partie — und weil die Punktepartie auch ohne Konto laeuft, wo es kein Profil gibt.
   */
  private static readonly AcceptKey = 'rookhub_guess_accept';
  accept: GuessAccept = { better: true, similar: true };

  /** Der eben abgelehnte Zug (SAN) — solange er dasteht, sucht der Nutzer weiter. */
  rejected: string | null = null;

  /** Der Kommentar zum gerade gezeigten Schritt (nicht zum Durchblaettern). */
  stepNote: GuessNote | null = null;

  /** Die zuletzt gewaehlte Sprache der Anmerkungen — geraetelokal, wie die Brett-Einstellungen. */
  private language: string | null = null;
  private static readonly LanguageKey = 'rookhub_guess_comment_lang';

  /** Beim ersten Mal gilt die Sprache der Oberflaeche: wer die Seite auf Deutsch liest, will die
   *  Anmerkungen auf Deutsch, wenn es sie gibt. Der Speicher kann gesperrt sein (Privatmodus). */
  private loadLanguage(): void {
    try { this.language = localStorage.getItem(GuessBoardComponent.LanguageKey); } catch { /* gesperrt */ }
    this.language ||= this.translate.currentLang() || null;
  }
  /** Einen Schritt der Abspielfolge aufs Brett legen (siehe `GuessPlayback`). */
  private showStep(step: GuessHistoryMove): void {
    this.boardFen = step.fen;
    this.lastMove = [step.uci.slice(0, 2), step.uci.slice(2, 4)];
    this.browseIndex = null;
    this.stepNote = step.comment
      ? { move: `${step.moveNumber}${step.white ? '.' : '…'}${step.san}`, text: step.comment }
      : null;
  }

  onMove(m: UserBoardMove): void {
    if (!this.canGuess) return;
    // Die Umwandlungsfigur (am Brett gewaehlt, auch eine Unterverwandlung) steht im SAN. Ohne sie ginge
    // „e7e8" statt „e7e8q" zum Server — dort kein legaler Zug, und JEDE Umwandlung waere mit 400 abgeprallt.
    this.send(m.from + m.to + promotionOf(m.san));
  }

  skip(): void {
    if (!this.canGuess) return;
    this.send(null);
  }

  /** Ein Rateversuch (`null` = Passen): Brett vorab umlegen, senden, Antwort einordnen. */
  private send(uci: string | null): void {
    const id = this.session?.id;
    if (!id) return;
    this.busy = true;
    this.stepNote = null;              // der Kommentar des VORIGEN Zuges ist erledigt
    this.rejected = null;
    const seconds = Math.min(3600, Math.max(0, Math.round((Date.now() - this.since) / 1000)));
    const guess: PendingGuess = {
      id, uci, fenBefore: this.boardFen, lastBefore: this.lastMove,
      // Der Halbzug, um den es geht — NACH der Antwort zeigt `session` schon die naechste Aufgabe.
      plyGuessed: this.session?.position?.ply ?? -1,
    };
    this.showOptimistic(uci);

    this.service.guess(id, uci, seconds, this.accept, this.language).subscribe({
      next: res => this.onGuessResult(res, guess),
      error: err => this.onGuessFailed(err, guess),
    });
  }

  /**
   * Den eigenen Zug SOFORT uebernehmen, nicht erst mit der Antwort. Sonst passiert Folgendes:
   * `busy` sperrt das Brett, das loest ein `ngOnChanges` mit der ALTEN Stellung aus, und
   * Chessground animiert die gerade gezogene Figur zurueck — 25 ms spaeter kommt die Antwort und
   * schiebt sie wieder hin. Genau das sichtbare Zucken beim Ziehen.
   */
  private showOptimistic(uci: string | null): void {
    if (!uci) return;
    const optimistic = fenAfterUci(this.boardFen, uci);
    if (optimistic) {
      this.boardFen = optimistic;
      this.lastMove = [uci.slice(0, 2), uci.slice(2, 4)];
    }
  }

  private onGuessResult(res: GuessResult, g: PendingGuess): void {
    this.busy = false;
    if (res.accepted === false) {
      this.rejectGuess(res, g);
      this.cdr.markForCheck();
      return;
    }
    this.rejected = null;
    this.last = res;
    const mine = g.uci ? fenAfterUci(g.fenBefore, g.uci) : null;
    if (res.session.status !== 'running') {
      this.finishSession(res, g, mine);
    } else if (mine && g.uci !== res.gameMoveUci && res.session.position) {
      this.holdOwnMove(res, g, mine);
    } else {
      // Partiezug getroffen oder gepasst: die dazugekommenen Halbzuege einzeln abspielen.
      this.playback.start(res.session, g.plyGuessed);
    }
    this.cdr.markForCheck();
  }

  /**
   * Guter Zug, aber gesucht ist der Partiezug: Brett zurueck auf die Aufgabe, nichts
   * gewertet, nichts weitergerueckt — nur die Auskunft, dass es ein anderer war.
   */
  private rejectGuess(res: GuessResult, g: PendingGuess): void {
    this.boardFen = g.fenBefore;
    this.lastMove = g.lastBefore;
    this.rejected = res.playedSan ?? null;
    this.session = res.session;
  }

  /**
   * Sitzung durch: es gibt keine naechste Aufgabe, `apply` wuerde das Brett also gar nicht
   * anfassen — die geschlagene Figur spraenge zurueck und das Brett zeigte die Stellung VOR
   * dem Schlusszug. Deshalb hier den letzten Zug selbst aufs Brett legen (beim Passen den
   * Partiezug, denn der ist ja gespielt worden).
   */
  private finishSession(res: GuessResult, g: PendingGuess, mine: string | null): void {
    const shown = mine ?? fenAfterUci(g.fenBefore, res.gameMoveUci);
    const shownUci = g.uci ?? res.gameMoveUci;
    this.session = res.session;
    this.playback.dropHold();
    this.browseIndex = null;
    if (shown) {
      this.boardFen = shown;
      this.lastMove = [shownUci.slice(0, 2), shownUci.slice(2, 4)];
    }
    this.loadReview(g.id);
  }

  /**
   * Ein anderer Zug als der Partiezug bleibt stehen, damit man sieht, was man gespielt hat —
   * die naechste Aufgabe wartet hinter „Weiter".
   */
  private holdOwnMove(res: GuessResult, g: PendingGuess, mine: string): void {
    this.session = res.session;              // Punkte/Fortschritt sofort mitnehmen
    // Fuer „Weiter": ab dem PARTIEZUG (das ist die Korrektur, die man sehen will) Schritt
    // fuer Schritt bis zur naechsten Aufgabe.
    this.playback.hold(res.session, g.plyGuessed);
    this.browseIndex = null;   // der eigene Zug soll zu sehen sein, nicht die Eroeffnung
    this.boardFen = mine;
    this.lastMove = [g.uci!.slice(0, 2), g.uci!.slice(2, 4)];
  }

  private onGuessFailed(err: { error?: { message?: string } } | null, g: PendingGuess): void {
    this.busy = false;
    // Der Zug wurde nicht gewertet — also auch nicht stehen lassen, sonst stuende das Brett
    // auf einer Stellung, die es fuer den Server nie gab.
    this.boardFen = g.fenBefore;
    this.lastMove = g.lastBefore;
    // Der Nutzer hat gerade gezogen — ein stiller Fehlschlag wäre hier das Schlimmste.
    this.snackbar.warn(err?.error?.message || this.translate.instant('guess.moveFailed'));
    this.cdr.markForCheck();
  }

  /** „Weiter" — zwei Haltegruende, zwei Bedeutungen (siehe `GuessPlayback.continue`). */
  continueGame(): void {
    if (this.playback.continue()) this.cdr.markForCheck();
  }

  /**
   * Der Kommentar zum ZULETZT vorgespielten Zug, sobald die Sitzung aufgeht.
   *
   * <p>Ab 0.468.0 bestimmt genau dieser Kommentar haeufig, WO die Partie einsteigt (siehe
   * <c>GuessStartPly</c>): der Kommentator hielt die Stellung fuer erklaerungsbeduerftig. Ihn dann
   * nur als Zeichen in der Zugliste zu fuehren, hiesse den Grund fuer den Einstieg zu verstecken —
   * man sitzt vor der Aufgabe und weiss nicht, warum sie hier anfaengt.</p>
   *
   * <p>Gezeigt wird der Kommentar des LETZTEN gespielten Zuges, nie der des gesuchten: die
   * Fortsetzung verlaesst den Server nicht, und der Verlauf enthaelt nur, was schon auf dem Brett
   * stand.</p>
   */
  private showOpeningNote(s: GuessSession): void {
    if (this.stepNote || !s.history.length) return;
    const last = s.history[s.history.length - 1];
    if (!last.comment) return;
    this.stepNote = { move: `${last.moveNumber}${last.white ? '.' : '…'}${last.san}`, text: last.comment };
  }


  private apply(s: GuessSession): void {
    this.session = s;
    this.since = Date.now();
    this.browseIndex = null;        // neue Aufgabe → Brett zurueck auf die Aufgabe
    if (s.position) {
      this.boardFen = s.position.fen;
      this.lastMove = s.position.lastMoveUci
        ? [s.position.lastMoveUci.slice(0, 2), s.position.lastMoveUci.slice(2, 4)]
        : undefined;
    }
  }

  /** Gab es in dieser Stellung einen besseren Zug als den der Partie? Nur dann lohnt das ⓘ. */
  hasBetter(r: GuessReviewMove): boolean {
    return !!r.bestSan && r.bestSan !== r.gameSan;
  }

  /**
   * Der Text hinter dem ⓘ. Den PARTIEZUG nur, wenn etwas anderes gespielt wurde — sonst steht er
   * schon in der Zeile und die Wiederholung sagt nichts.
   */
  infoText(r: GuessReviewMove): string {
    const lines: string[] = [];
    if (r.playedSan && r.playedSan !== r.gameSan) {
      lines.push(this.translate.instant('guess.info.gameMove',
        { move: r.gameSan, eval: r.gameEval || '?' }));
    }
    lines.push(this.translate.instant('guess.info.bestMove',
      { move: r.bestSan, eval: r.bestEval || '?' }));
    return lines.join('\n\n');
  }

  private loadReview(id: number): void {
    this.service.review(id).subscribe({
      next: rows => { this.review = rows; this.cdr.markForCheck(); },
      error: () => { /* der Rückblick ist Zugabe — die Sitzung ist bereits gewertet */ },
    });
  }
}
