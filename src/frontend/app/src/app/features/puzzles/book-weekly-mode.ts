import { BookPuzzleDto } from './puzzle.service';
import { WeeklyMode, weeklyTitleLabel } from '../weekly/weekly.service';
import { weeklyStartIndex as computeWeeklyStartIndex } from './book-nav.util';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { BookModeHost, BookModeStrategy } from './book-mode-host';

/**
 * Wochenpost-Modus des Buch-Lösers (/weekly/:weeklyId, Zerlegung Z18): Puzzles eines Posts sequenziell durchspielen.
 * Per-User-Fortschritt wird serverseitig gemerkt: jedes gespielte Puzzle (gelöst oder nicht) zählt; erledigt = alle
 * gespielt. `puzzle.id` ist hier der Sequenz-Index, KEINE BookPuzzle-Id.
 */
export class BookWeeklyMode implements BookModeStrategy {
  id: number | null = null;
  title = '';
  puzzles: BookPuzzleDto[] = [];
  index = 0;
  completed = false;
  /** Spielweise dieses Wochenposts: 'training' = Brett eingefroren (bisheriges Verhalten),
   *  'easy' = Figuren normal ziehen. Wird beim Start abgefragt und gilt für den ganzen Post;
   *  umschaltbar über die Aktionszeile (wirkt ab dem nächsten Puzzle-Start). */
  mode: WeeklyMode = 'training';
  /** Aufteilung der bereits gespielten Puzzles (Serverstand, für die Anzeige). */
  trainingCount = 0;
  easyCount = 0;
  /** Wurde in DIESEM Puzzle das Auge benutzt, nachdem der erste Zug gemacht war? Dann zählt das
   *  ganze Puzzle als „einfach" — aufgedeckte Figuren sind kein Trainingsmodus mehr. */
  revealedAfterMove = false;

  played = 0;                     // gespielte Puzzles (serverseitiger Stand)
  solved = 0;                     // davon gelöst
  seconds = 0;                    // Gesamtzeit über alle gespielten Puzzles (serverseitiger Stand)
  attemptRecorded = false;        // pro Puzzle nur einmal aufzeichnen

  constructor(private readonly h: BookModeHost) {}

  get total(): number { return this.puzzles.length; }
  /** „Wochenpost 3" — ein rein numerischer Titel stand sonst als nackte Zahl unter dem Kapitel. */
  get chipLabel(): string { return weeklyTitleLabel(this.title, this.h.translate); }
  get displayIndex(): number {
    return this.total === 0 ? 0 : Math.min(this.index + 1, this.total);
  }
  get percent(): number {
    if (this.total === 0) return 0;
    return this.completed ? 100 : Math.round(100 * this.index / this.total);
  }
  /** Gesamtzeit über alle gespielten Puzzles als m:ss bzw. h:mm:ss. */
  get timeDisplay(): string { return formatSecondsClock(this.seconds); }

  /** Wohin Anmelden/Registrieren zurückführen soll: genau diese Wochenpost. Ohne das landet
   *  ein Gast nach dem Login auf dem Dashboard und muss den geteilten Link neu suchen. */
  get returnUrl(): string { return this.id != null ? `/weekly/${this.id}` : '/weekly'; }

  /** Die Übersicht /weekly steht hinter authGuard; ein Gast (geteilter Link auf /weekly/:id) landete über
   *  Pfeil und „Zur Übersicht" ungefragt auf der Anmeldeseite → für Gäste keine Übersichts-Knöpfe. Anmelden
   *  bietet ihnen der Gast-Hinweis der Kontext-Karte an. */
  get canOpenOverview(): boolean { return this.h.isLoggedIn; }

  backToOverview(): void {
    this.h.router.navigate(['/weekly']);
  }

  load(): void {
    const h = this.h;
    if (this.id == null) return;
    const epoch = ++h.loadEpoch;
    h.state = 'LOADING';
    h.weeklyService.getPlay(this.id).subscribe({
      next: play => {
        if (epoch !== h.loadEpoch) return;
        this.title = play.title;
        this.puzzles = play.puzzles ?? [];
        this.index = 0;
        this.askMode();
        if (this.puzzles.length === 0) {
          this.completed = false;
          h.puzzle = null;
          h.state = 'COURSE_DONE';   // Done-Panel; weekly-Card zeigt „keine Puzzles"
          return;
        }
        // Fortschritt laden → Anzeige + Einstieg beim ersten NEUEN Puzzle (bei 100% von vorne).
        if (h.auth.isLoggedIn && this.id != null) {
          h.weeklyService.getProgress(this.id).subscribe({
            next: p => {
              if (epoch !== h.loadEpoch) return;
              this.played = p.playedCount;
              this.solved = p.solvedCount;
              this.trainingCount = p.trainingCount ?? 0;
              this.easyCount = p.easyCount ?? 0;
              this.seconds = p.totalSeconds;
              this.loadAt(computeWeeklyStartIndex(this.puzzles, p));
            },
            error: () => { if (epoch === h.loadEpoch) this.loadAt(0); },
          });
        } else {
          this.loadAt(0);
        }
      },
      error: () => { if (epoch !== h.loadEpoch) return; h.state = 'COURSE_DONE'; h.puzzle = null; }
    });
  }

  /** Spielweise beim Start erfragen. Einmal je Wochenpost und Sitzung: die Wahl steckt in
   *  sessionStorage, damit ein Reload mitten im Post nicht erneut fragt (und die Wertung
   *  konsistent bleibt). Anonyme Besucher werden nicht gefragt — für sie wird nichts gewertet. */
  askMode(): void {
    const h = this.h;
    if (!h.inWeekly || this.id == null) return;
    const key = `rookhub_weekly_mode_${this.id}`;
    let gemerkt: string | null = null;
    try { gemerkt = sessionStorage.getItem(key); } catch { /* Privatmodus */ }
    if (gemerkt === 'easy' || gemerkt === 'training') {
      this.mode = gemerkt;
      this.applyMode();
      return;
    }
    if (!h.auth.isLoggedIn) { this.applyMode(); return; }
    // UI-Sweep 2026-10-11 (r2-rk-weekly-mode-dialog): dieselbe Modus-Frage wie bei den Puzzles (Kacheln, Erklärtexte),
    // nur Titel/Hinweis „gilt für diesen Wochenpost"; vorher ein eigener Dialog mit Entwicklertext.
    h.solveMode.openDialog({
      title: h.translate.instant('weekly.mode.title'),
      intro: h.translate.instant('weekly.mode.intro'),
      hint: h.translate.instant('weekly.mode.switchHint'),
    }).subscribe(r => {
        this.mode = r?.mode === 'easy' ? 'easy' : 'training';
        try { sessionStorage.setItem(key, this.mode); } catch { /* Privatmodus */ }
        this.applyMode();
        // Das erste Puzzle steht schon — mit der frisch gewählten Spielweise neu aufsetzen.
        if (h.puzzle) h.setupPuzzle(h.puzzle);
      });
  }

  /** Visualisierung nach der gewählten Spielweise setzen: Training = eingefroren (Level 1),
   *  Einfach = normal ziehbar (0). Überschreibt für den Wochenpost bewusst die globale
   *  Einstellung — die Wertung hängt daran. */
  applyMode(): void {
    this.h.visualizationMode = this.mode === 'easy' ? 0 : 1;
    this.revealedAfterMove = false;
  }

  loadAt(index: number): void {
    if (index >= this.puzzles.length) {
      this.completed = true;
      // letztes Puzzle bleibt sichtbar (state SOLVED) — Card zeigt „abgeschlossen"
      return;
    }
    this.completed = false;
    this.h.gaveUp = false;
    this.attemptRecorded = false;   // neues Puzzle → wieder aufzeichenbar
    this.index = index;
    this.h.puzzle = this.puzzles[index];
    this.applyMode();               // Spielweise gilt je Puzzle neu (auch nach dem Umschalten)
    this.h.setupPuzzle(this.h.puzzle);
  }

  /**
   * Zeichnet das aktuelle Wochenpost-Puzzle als gespielt auf (gelöst oder nicht), pro Puzzle einmal.
   * Offline-fähig via Queue; nur eingeloggt (die Route ist authGuard — defensiv geprüft).
   */
  recordAttempt(solved: boolean): void {
    const h = this.h;
    if (!h.inWeekly || this.id == null || !h.puzzle || this.attemptRecorded) return;
    if (!h.auth.isLoggedIn) return;
    this.attemptRecorded = true;
    const puzzleIndex = h.puzzle.id;   // = Parser-Index der Wochenpost-Sequenz
    const wrongAttempts = h.wrongMoveCount;
    const mouseslips = h.mouseslipUsed ? 1 : 0;
    const url = `/api/weekly-posts/${this.id}/attempt`;
    // Auge nach dem ersten Zug benutzt → das ganze Puzzle zählt als „einfach", egal welcher
    // Modus eingestellt war: wer die Stellung aufdeckt, hat sie nicht mehr blind gerechnet.
    const mode: WeeklyMode = this.revealedAfterMove ? 'easy' : this.mode;
    const body = { puzzleIndex, solved, timeSeconds: h.solveSeconds, hintsUsed: h.hintsUsed, wrongAttempts, mouseslips, mode };
    if (!navigator.onLine) {
      h.offlineQueue.enqueue('POST', url, body);
      this.played = Math.min(this.played + 1, this.total || this.played + 1);
      if (solved) this.solved += 1;
      this.seconds += h.solveSeconds;
      if (mode === 'easy') this.easyCount += 1; else this.trainingCount += 1;
      return;
    }
    h.weeklyService.recordAttempt(this.id, puzzleIndex, solved, h.solveSeconds, h.hintsUsed, wrongAttempts, mouseslips, mode).subscribe({
      next: p => {
        this.played = p.playedCount;
        this.solved = p.solvedCount;
        this.seconds = p.totalSeconds;
        this.trainingCount = p.trainingCount ?? this.trainingCount;
        this.easyCount = p.easyCount ?? this.easyCount;
      },
      error: () => h.offlineQueue.enqueue('POST', url, body),
    });
  }

  /** Spielweise umschalten — wirkt ab dem NÄCHSTEN Puzzle, damit ein laufender Versuch nicht
   *  mitten im Rechnen die Regeln wechselt. Bereits gemeldete Puzzles bleiben, wie sie gewertet
   *  wurden. */
  toggleMode(): void {
    this.mode = this.mode === 'training' ? 'easy' : 'training';
    try { sessionStorage.setItem(`rookhub_weekly_mode_${this.id}`, this.mode); } catch { /* Privatmodus */ }
    this.h.snackbar.info(this.h.translate.instant(
      this.mode === 'easy' ? 'weekly.mode.switchedEasy' : 'weekly.mode.switchedTraining'),
      { duration: 3000 });
  }

  next(): void {
    this.loadAt(this.index + 1);
  }
}
