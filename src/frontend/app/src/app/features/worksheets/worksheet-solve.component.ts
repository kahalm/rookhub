import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { PreferencesService } from '../../core/preferences.service';
import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { LineSolver } from '../../shared/chess/line-solver';
import { SharedWorksheet, SharedWorksheetItem, WorksheetService } from './worksheet.service';

/** Was gerade an der aktuellen Aufgabe passiert. */
export type SolveState = 'solving' | 'wrong' | 'solved' | 'given-up' | 'free';

/**
 * Eine Aufgabe des Blatts: Zustand, Aufgeben, Zurücksetzen, Rechenbrett. Was „richtig gezogen"
 * heißt, steht NICHT hier, sondern im gemeinsamen {@link LineSolver} (`shared/chess/line-solver.ts`)
 * — dieselbe Regel, nach der die Puzzles urteilen.
 *
 * <p>Geurteilt wird bewusst OHNE die Umwandlungsfigur aus dem Dialog: auf dem Blatt entscheiden
 * Start und Ziel, und was auf dem Brett landet, setzt die Lösung. Genau das ist die Sonderregel,
 * die hier früher ausgeschrieben stand (`expected.length === 5 && expected.startsWith(orig + dest)`).</p>
 */
export class WorksheetTask {
  private readonly solver: LineSolver;
  state: SolveState;
  readonly solution: string[];

  constructor(public readonly item: SharedWorksheetItem) {
    this.solution = (item.solutionMoves || '').split(' ').filter(m => m.length >= 4);
    this.solver = new LineSolver({ fen: item.fen, line: this.solution.map(uci => ({ uci })) });
    // Ohne Lösung ist die Aufgabe eine zum RECHNEN: das Brett bleibt frei, nichts wird geprüft.
    this.state = this.solution.length === 0 ? 'free' : 'solving';
  }

  get chess(): Chess { return this.solver.chess; }
  /** Halbzug-Index in {@link solution}, der als Nächstes vom Lösenden erwartet wird. */
  get index(): number { return this.solver.ply; }
  get fen(): string { return this.solver.chess.fen(); }
  get orientation(): 'white' | 'black' { return this.item.orientation; }
  get turnColor(): 'white' | 'black' { return this.solver.turn === 'w' ? 'white' : 'black'; }
  get finished(): boolean { return this.state === 'solved' || this.state === 'given-up'; }
  get dests(): Map<Key, Key[]> { return this.finished ? new Map() : this.solver.dests(); }

  /**
   * Zug des Lösenden. Richtig = derselbe Zug wie in der Lösung; dann folgt die Gegnerantwort
   * sofort. Falsch = die Stellung bleibt, wie sie war (kein Zurücknehmen nötig, es wurde nie
   * gezogen), und die Aufgabe steht weiter offen.
   */
  play(orig: string, dest: string, promotion?: string): boolean {
    if (this.state === 'free') { this.solver.playFree(orig, dest, promotion); return true; }
    if (this.finished) return false;

    if (this.solver.judge(orig, dest) !== 'correct') { this.state = 'wrong'; return false; }

    this.solver.playExpected();
    this.solver.opponentReply();   // der Antwortzug gehört nicht zur Aufgabe
    this.state = this.solver.done ? 'solved' : 'solving';
    return true;
  }

  /** Aufgeben: die restliche Lösung wird vorgespielt (wie im Solver nach „Aufgeben"). */
  giveUp(): void {
    if (this.finished || this.state === 'free') return;
    while (!this.solver.done && this.solver.playExpected() !== null) { /* Rest vorspielen */ }
    this.state = 'given-up';
  }

  /** Zurück auf die Ausgangsstellung (Rechenbrett oder nach einem Fehlversuch). */
  reset(): void {
    if (!this.solver.startFenAccepted) return;   // unbrauchbare FEN: das Ersatzbrett bleibt stehen
    this.solver.reset();
    this.state = this.solution.length === 0 ? 'free' : 'solving';
  }
}

/**
 * Das GETEILTE Aufgabenblatt hinter dem Link (`/w/:token`) — ohne Anmeldung, das ist der Sinn des
 * QR-Codes auf dem Ausdruck: Blatt auf dem Tisch, Handy daneben, Aufgabe für Aufgabe am Brett.
 *
 * <p>Gelöst wird gegen die Lösung, die beim Zusammenstellen mit ins Blatt gewandert ist. Aufgaben
 * ohne Lösung (eine von Hand eingefügte Stellung, eine Stellung aus einer Kommentar-Variante)
 * bleiben ein RECHENBRETT: Züge sind frei, nichts wird bewertet — besser als eine Aufgabe, die
 * jeden Zug für falsch erklärt.</p>
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-worksheet-solve',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule,
    MatProgressSpinnerModule, TranslatePipe, PuzzleBoardComponent,
  ],
  templateUrl: './worksheet-solve.component.html',
  styleUrls: ['./worksheet-solve.component.scss'],
})
export class WorksheetSolveComponent implements OnInit {
  loading = true;
  notFound = false;
  sheet: SharedWorksheet | null = null;
  tasks: WorksheetTask[] = [];
  current = 0;
  /** Auf Anhieb gelöst (ohne Fehlzug, ohne Aufgeben) — die Zahl am Ende des Blatts. */
  private cleanSolves = new Set<number>();
  private stumbled = new Set<number>();

  private route = inject(ActivatedRoute);
  private worksheets = inject(WorksheetService);
  private prefs = inject(PreferencesService);
  private cdr = inject(ChangeDetectorRef);

  get task(): WorksheetTask | null { return this.tasks[this.current] ?? null; }
  get total(): number { return this.tasks.length; }
  get progress(): number { return this.total === 0 ? 0 : ((this.current + 1) / this.total) * 100; }
  get atEnd(): boolean { return this.total > 0 && this.current >= this.total - 1; }
  get solvedCount(): number { return this.cleanSolves.size; }
  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }
  /** Alle Aufgaben abgearbeitet → Schlussbild statt Brett. */
  done = false;

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.worksheets.getShared(token).subscribe({
      next: sheet => {
        this.sheet = sheet;
        this.tasks = sheet.items.map(i => new WorksheetTask(i));
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => { this.loading = false; this.notFound = true; this.cdr.markForCheck(); },
    });
  }

  onMove(e: { orig: string; dest: string; promotion?: string }): void {
    const task = this.task;
    if (!task) return;
    if (!task.play(e.orig, e.dest, e.promotion)) this.stumbled.add(this.current);
    // „Auf Anhieb" zählt nur, wer vorher nicht danebengegriffen und die Lösung nicht gesehen hat.
    else if (task.state === 'solved' && !this.stumbled.has(this.current)) this.cleanSolves.add(this.current);
    this.cdr.markForCheck();
  }

  /** Nach einem Fehlzug weiterprobieren: die Stellung stand ja noch, nur der Hinweis geht weg. */
  retry(): void {
    const task = this.task;
    if (task?.state === 'wrong') { task.state = 'solving'; this.cdr.markForCheck(); }
  }

  giveUp(): void { this.task?.giveUp(); this.cdr.markForCheck(); }
  resetBoard(): void { this.task?.reset(); this.cdr.markForCheck(); }

  next(): void {
    if (this.current < this.total - 1) { this.current++; this.cdr.markForCheck(); }
    else { this.done = true; this.cdr.markForCheck(); }
  }

  prev(): void {
    if (this.current > 0) { this.current--; this.done = false; this.cdr.markForCheck(); }
  }

  /** Von vorn — ohne die Seite neu zu laden (Verein: das Blatt geht reihum). */
  restart(): void {
    this.tasks.forEach(t => t.reset());
    this.cleanSolves.clear();
    this.stumbled.clear();
    this.current = 0;
    this.done = false;
    this.cdr.markForCheck();
  }
}
