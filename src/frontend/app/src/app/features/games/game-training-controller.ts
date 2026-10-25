import { computed, effect, signal, untracked } from '@angular/core';
import { UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { GamesService } from './games.service';
import { Mistake, MistakesBySide, NO_MISTAKES, mistakesOf, trainingSide, withoutPlies } from './mistakes.util';
import { MistakesSession } from './mistakes-session';
import { MistakeJudgeService } from './mistake-judge.service';

/** Was das Fehler-Training von der Partieseite braucht. */
export interface GameTrainingHost {
  /** Live-Engine aus — im Training verriete sie die Lösung. */
  stopLive(): void;
  stopTrainingAnalysis(): void;
  /** Zugliste/Kurve auf einen Halbzug-Index stellen. */
  goToMove(index: number): void;
  /** Eigene Partie (`/games/:id`)? Nur dann wird gemeldet und ausgeblendet. */
  own(): boolean;
  gameId(): number | null;
}

/**
 * „Eigene Fehler nachspielen" auf der Partieseite (0.516.0; Zerlegung Z26, aus `SharedGameComponent`): welche Fehler
 * abgefragt werden (ohne ausgeblendete, Seite des Besitzers bzw. aus dem Trainings-Link), die laufende Sitzung, der
 * Selbststart per `?train=` und das Melden der Treffer. Die Effekte entstehen im Konstruktor — er muss im
 * Injektionskontext laufen (Feld-Initialisierer der Seite).
 */
export class GameTrainingController {
  /** Abfragbare Fehler beider Seiten, gemeldet vom Rückblick unter dem Brett. */
  readonly mistakes = signal<MistakesBySide>(NO_MISTAKES);
  /** Vom Besitzer ausgeblendete Halbzüge („nicht mehr zeigen", 0.748.0) — nur bei der eigenen Partie geladen. */
  readonly dismissedPlies = signal<readonly number[]>([]);
  /** Was der Trainer abfragt: die Fehler ohne die ausgeblendeten. */
  readonly trainable = computed(() => withoutPlies(this.mistakes(), this.dismissedPlies()));
  /** Wessen Partie — als Signal, damit Zähler und Trainer-Seite nachziehen, sobald die Partie da ist. */
  readonly ownerSide = signal<'white' | 'black' | null>(null);
  /** Seite aus dem geteilten Trainings-Link (`?train=white|black`, 0.740.0) — schlägt die Seite des Besitzers. */
  readonly trainSide = signal<'white' | 'black' | null>(null);
  /** Die Seite, die der Trainer abfragt — der Knopf zählt NUR sie (siehe `trainingSide`). */
  readonly mistakeSide = computed(() => this.trainSide() ?? trainingSide(this.trainable(), this.ownerSide()));
  readonly mistakeTotal = computed(() => mistakesOf(this.trainable(), this.mistakeSide()).length);
  /** Laufendes Training „Eigene Fehler nachspielen" — `null` = die Seite zeigt die Partie. */
  readonly training = signal<MistakesSession | null>(null);

  /** Mit `?train=` geöffnet: das Training startet von selbst, sobald die Analyse Aufgaben dieser Seite hergibt (einmal). */
  private autoTrain = false;
  /** Schon gemeldete Halbzüge und Aufgabenzahl: verhindert, dass jeder Zug dieselbe Meldung wiederholt. */
  private readonly reportedPlies = new Set<number>();
  private reportedTotal = -1;

  constructor(
    private readonly host: GameTrainingHost,
    private readonly games: GamesService,
    private readonly judge: MistakeJudgeService,
  ) {
    effect(() => {
      const side = this.trainSide();
      if (!side || !this.autoTrain || this.training()) return;
      if (mistakesOf(this.trainable(), side).length === 0) return;
      this.autoTrain = false;
      untracked(() => this.trainMistakes());
    });
    /**
     * Zugliste und Kurve laufen mit: je Aufgabe springt die Partie auf die Stellung VOR dem Fehler — man
     * sieht, wo in der Partie man ist, und nach dem Beenden steht man genau dort.
     */
    effect(() => {
      const m = this.training()?.current();
      if (m) this.host.goToMove(m.ply - 1);
    });
    /** Jeder Treffer wird gemeldet — auch der, den die Browser-Engine erst verzögert bestätigt. */
    effect(() => {
      const t = this.training();
      const solved = t?.solvedPlies() ?? [];
      if (t && solved.length) untracked(() => this.reportMistakes(t, solved));
    });
  }

  /** Aus dem Trainings-Link (`?train=`): diese Seite abfragen und einmal von selbst starten. */
  startFromLink(side: 'white' | 'black'): void {
    this.trainSide.set(side);
    this.autoTrain = true;
  }

  /**
   * Trainiert wird die Seite des Besitzers; ohne Zuordnung (fremde geteilte Partie) die mit den meisten
   * Fehlern — in der Leiste lässt sich umschalten, sobald beide Seiten welche haben. Gespielt wird auf dem
   * Brett dieser Seite (bis 0.518.0 ein Dialog mit eigenem, kleinerem Brett).
   */
  trainMistakes(): void {
    this.host.stopLive();
    // Nicht gelistete Züge prüft die Browser-Engine nach (nur wo die Analyse das offen lässt).
    const gameId = this.host.gameId();
    this.training.set(new MistakesSession(this.trainable(), this.mistakeSide(),
      (m, fen) => this.judge.judge(m, fen), fen => this.judge.evaluate(fen),
      this.host.own() && gameId ? m => this.dismissMistake(m) : undefined));
  }

  /** Alle ausgeblendeten Fehler dieser Partie wieder einblenden. */
  restoreDismissed(): void {
    const gameId = this.host.gameId();
    if (!gameId) return;
    const before = this.dismissedPlies();
    this.dismissedPlies.set([]);
    this.games.dismissMistake(gameId, -1, false, mistakesOf(this.mistakes(), this.mistakeSide()).length)
      .subscribe({ error: () => this.dismissedPlies.set(before) });
  }

  onMove(e: UserBoardMove): void {
    this.training()?.onMove(e);
  }

  end(): void {
    // Auch ohne neuen Treffer melden: dann steht in der Übersicht „0 von 7", und man sieht, dass die
    // Partie schon einmal offen war. Der Server vereinigt additiv, hier geht nichts verloren.
    const t = this.training();
    if (t) this.reportMistakes(t, t.solvedPlies());
    this.host.stopTrainingAnalysis();
    this.training.set(null);
  }

  /** Anzahl Aufgaben je Seite — für die Menüpunkte „Fehler-Training teilen". */
  mistakeCount(side: 'white' | 'black'): number { return mistakesOf(this.mistakes(), side).length; }

  /** „Diesen Fehler nicht mehr zeigen" (0.748.0): auf dem Server merken; ein Fehlschlag bleibt still (wie die Meldung). */
  private dismissMistake(m: Mistake): void {
    const gameId = this.host.gameId();
    if (!gameId) return;
    this.dismissedPlies.update(p => p.includes(m.ply) ? p : [...p, m.ply]);
    const total = mistakesOf(this.mistakes(), m.white ? 'white' : 'black').length;
    this.games.dismissMistake(gameId, m.ply, true, total).subscribe({ error: () => { /* nächster Versuch beim nächsten Klick */ } });
  }

  /**
   * Gefundene Fehler an RookHub melden — daraus zeigt `/games` „4 von 7 · 3 offen". Nur bei der EIGENEN
   * Partie (die geteilte Ansicht kennt keine Id des Betrachters). Still im Fehlerfall: das ist
   * Buchführung im Hintergrund, und die nächste Meldung trägt denselben Stand erneut.
   */
  private reportMistakes(session: MistakesSession, solved: readonly number[]): void {
    const gameId = this.host.gameId();
    if (!this.host.own() || !gameId) return;
    const neu = solved.filter(p => !this.reportedPlies.has(p));
    if (!neu.length && this.reportedTotal === session.list().length) return;
    neu.forEach(p => this.reportedPlies.add(p));
    this.reportedTotal = session.list().length;
    this.games.recordMistakes(gameId, session.list().length, [...solved]).subscribe({
      error: () => { neu.forEach(p => this.reportedPlies.delete(p)); this.reportedTotal = -1; },
    });
  }
}
