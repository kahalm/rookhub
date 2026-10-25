import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { BookPuzzleDto, PuzzleService } from './puzzle.service';
import type { SolverCoreState } from './base-puzzle-solver';
import { CourseService } from '../courses/course.service';
import { CourseLanguageService, CourseRef } from '../courses/course-language.service';
import { WeeklyService } from '../weekly/weekly.service';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { ChallengeService } from '../../core/challenge.service';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';

/**
 * Was die Modus-Strategien des Buch-Lösers (Kurs, Wochenpost, Tagespuzzle, einzelnes Buch-Puzzle) von der
 * Komponente brauchen (Zerlegung Z18). Die Komponente ist der Wirt: sie hält Puzzle, Lös-Automat und Lade-Epoche,
 * die Strategien laden die nächste Aufgabe, melden Ergebnisse und navigieren je Modus.
 *
 * Dienste werden LIVE über den Wirt gelesen (nicht beim Bau kopiert) — so bleibt ein im Test ausgetauschter Dienst
 * auch für die Strategie gültig.
 */
/** Endzustände des Buch-/Kurs-Lösers: 'INFO' = Info-/Erklärlinie (kein Quiz, nur Durchklicken), 'COURSE_DONE' =
 *  nichts mehr zu tun (Kurs/Kapitel fertig, Woche leer). Die übrigen setzt der Lös-Automat. */
export type BookEndState = 'SOLVED' | 'FAILED' | 'INFO' | 'COURSE_DONE';
/** Alle Zustände des Buch-/Kurs-Lösers. */
export type BookState = SolverCoreState | BookEndState;

export interface BookModeHost {
  puzzle: BookPuzzleDto | null;
  /** Zustand des Lösers ({@link BookState}). */
  state: BookState;
  loadError: boolean;
  gaveUp: boolean;
  /** Monotone Epoche je Ladevorgang — eine ältere, langsamer auflösende Antwort wird verworfen. */
  loadEpoch: number;
  /** „Erneut versuchen“ nach einem Ladefehler. */
  retryFn: (() => void) | null;
  visualizationMode: number;

  readonly inCourse: boolean;
  readonly inWeekly: boolean;
  readonly isDaily: boolean;
  /** Datum des Tagespuzzles („YYYYMMDD“), sonst null. */
  readonly daily: { readonly date: string | null };
  readonly standalone: boolean;
  readonly isLoggedIn: boolean;
  readonly mouseslipUsed: boolean;
  readonly wrongMoveCount: number;
  /** Zu wertende Lösezeit des aktuellen Versuchs. */
  readonly solveSeconds: number;
  /** Höchste aufgedeckte Tipp-Stufe des aktuellen Versuchs. */
  readonly hintsUsed: number;
  /** Spielweise, unter der das aktuelle Puzzle tatsächlich läuft (gemeldeter Wert). */
  readonly attemptSolveMode: SolveMode;
  /** `?lang=` für die nächste Anfrage (null außerhalb von Kurs/Buch). */
  readonly requestLang: string | null;
  readonly langRef: CourseRef | null;

  readonly puzzleService: PuzzleService;
  readonly courseService: CourseService;
  readonly weeklyService: WeeklyService;
  readonly router: Router;
  readonly translate: TranslateService;
  readonly auth: AuthService;
  readonly snackbar: SnackbarService;
  readonly offlineQueue: OfflineQueueService;
  readonly challengeService: ChallengeService;
  readonly solveMode: SolveModeService;
  readonly courseLang: CourseLanguageService;

  /** Puzzle aufs Brett bringen (Spielweise, Info-Linie, Lös-Automat). */
  setupPuzzle(p: BookPuzzleDto): void;
  /** Nach dem Laden einer Linie: Sprachen merken, ggf. Texte in der gewählten Sprache nachholen. */
  afterLineLoaded(p: BookPuzzleDto, sentLang: string | null): void;
  /** Frischer Ladevorgang: Uhr anhalten und auf 0, Alternativ-/Aufgeben-Marker zurück. */
  resetForLoad(): void;
  /** Laufende Lösungs-Wiedergabe anhalten. */
  clearSolutionPlay(): void;
}

/** Gemeinsamer Vertrag der Modi mit eigener Puzzle-Folge (Kurs, Wochenpost, Buch). */
export interface BookModeStrategy {
  /** Eins weiter im jeweiligen Modus (Auto-Weiter, „Nächstes Puzzle“, Überspringen). */
  next(): void;
  /** Versuch melden (gelöst/nicht gelöst); jede Strategie prüft selbst, ob sie zuständig ist. */
  recordAttempt(solved: boolean): void;
}
