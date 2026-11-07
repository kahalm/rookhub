import { ChangeDetectionStrategy, ChangeDetectorRef, Component, HostListener, OnDestroy, OnInit, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { Location } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { Chess } from 'chess.js';
import type { Key } from 'chessground/types';
import { PuzzleBoardComponent } from '../../puzzles/puzzle-board.component';
import { PreferencesService } from '../../../core/preferences.service';
import { AnalysisEngineService } from '../../analysis/analysis-engine.service';
import { nearestMaiaElo } from '../../analysis/analysis-route';
import { Deviations, TRAINER_PASS_THROUGH, deviationText, deviationsUrl, plyLabel } from '../../../shared/player-card/training-lines';
import { readChapterColorOverrides } from '../../../shared/repertoire/repertoire-color.util';
import { cardLang, cardSan } from '../../../shared/player-card/card-lang';
import { localStore, readRaw, writeRaw } from '../../../core/local-json-store';
import { isBoardHotkey } from '../../../shared/keyboard.util';
import { IconLabelDirective } from '../../../shared/icon-label/icon-label.directive';
import { destsAt } from '../repertoire-trainer.util';
import { OpponentNoteView, opponentNoteView } from '../opponent-note.util';
import { TrainerReviewEngine, readReviewEngineOn, saveReviewEngineOn } from '../repertoire-trainer-review-engine';
import { DEFAULT_THRESHOLD_CP, DeviationJudge, THRESHOLD_OPTIONS } from './deviation-judge';
import { DeviationSession, linesOf } from './deviation-session';

/** Schwelle „gut" in Zentibauern — je Gerät (Vorgabe 30). */
export const DEVIATION_THRESHOLD_KEY = 'rookhub_deviation_threshold';

function readThreshold(): number {
  const v = Number(readRaw(localStore(), DEVIATION_THRESHOLD_KEY));
  return (THRESHOLD_OPTIONS as readonly number[]).includes(v) ? v : DEFAULT_THRESHOLD_CP;
}

/**
 * „Abweichungen trainieren" (0.892.0, Wunsch: „mach dafür einen 2. knopf — trainiere abweichungen"): eigene schlanke Seite statt
 * eines Modus im Repertoire-Trainer — die Abweichungen gehören zu keinem einzelnen Repertoire (alle markierten), ab der Abweichung
 * gibt es keine Linie zum Abfragen (die Engine urteilt), und am Ende steht Maia. Wiederverwendet: Brett, Engine-Dienst des
 * Analysebretts (Urteil + Nachbetrachtung), Partien-Anzeige des Trainers. Adresse
 * `/repertoires/deviations?deviations=prep:<id>|league:<fide>&color=…&name=…` (+ Filter der Karte).
 */
@Component({
  selector: 'app-deviation-trainer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule, TranslatePipe, PuzzleBoardComponent, IconLabelDirective],
  templateUrl: './deviation-trainer.component.html',
  styleUrls: ['./deviation-trainer.component.scss'],
})
export class DeviationTrainerComponent implements OnInit, OnDestroy {
  /** `prep:<id>` | `league:<fide>`. */
  opponent: string | null = null;
  name: string | null = null;
  data: Deviations | null = null;
  loadError = false;
  threshold = readThreshold();
  readonly thresholdOptions = THRESHOLD_OPTIONS;

  private readonly judge: DeviationJudge | null;
  readonly session: DeviationSession;
  private sub: Subscription | null = null;
  private destroyed = false;

  /** Nachbetrachtung am Linienende: Halbzug der gezeigten Stellung; Engine wie im Trainer. */
  reviewPly = 0;
  reviewEngineOn = readReviewEngineOn();
  private readonly reviewEngine: TrainerReviewEngine;

  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  readonly prefs = inject(PreferencesService);
  private readonly translate = inject(TranslateService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly location = inject(Location);
  private readonly engine = inject(AnalysisEngineService, { optional: true });

  constructor() {
    const engine = this.engine;
    this.judge = engine ? new DeviationJudge(engine) : null;
    const color = this.route.snapshot.queryParamMap.get('color') === 'b' ? 'b' : 'w';
    this.session = new DeviationSession({
      changed: () => this.changed(),
      prepare: fen => this.judge?.prepare(fen),
      judge: (fen, uci) => this.judge ? this.judge.judge(fen, uci, this.threshold) : Promise.resolve({ good: true, lossCp: 0, best: [] }),
      stopEngine: () => this.judge?.stop(),
    }, color);
    this.reviewEngine = new TrainerReviewEngine(() => this.engine, () => this.cdr.markForCheck());
  }

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    this.opponent = deviationsUrl(q.get('deviations')) ? q.get('deviations') : null;
    this.name = q.get('name');
    const url = deviationsUrl(this.opponent);
    if (!url) { this.session.phase = 'EMPTY'; return; }
    let params = new HttpParams();
    const color = q.get('color');
    if (color === 'w' || color === 'b') params = params.set('color', color);
    const rep = Number(q.get('repertoire')) || null;
    if (rep) {
      params = params.set('repertoire', rep);
      const flat = readChapterColorOverrides(rep);
      if (Object.keys(flat).length) params = params.set('chapterColors', JSON.stringify(flat));
    }
    for (const k of TRAINER_PASS_THROUGH) { const v = q.get(k); if (v) params = params.set(k, v); }
    this.sub = this.http.get<Deviations>(url, { params }).subscribe({
      next: d => {
        if (this.destroyed) return;
        this.data = d;
        this.session.start(linesOf(d.deviations), d.positions);
      },
      error: () => { this.loadError = true; this.session.phase = 'EMPTY'; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.sub?.unsubscribe();
    this.session.dispose();
    this.judge?.stop();
    this.reviewEngine.dispose();
  }

  private changed(): void {
    if (this.session.phase === 'END') {
      if (this.reviewPly > this.session.played.length || this.lastPhase !== 'END') this.reviewPly = this.session.played.length;
    }
    this.lastPhase = this.session.phase;
    this.syncReviewEngine();
    this.cdr.markForCheck();
  }
  private lastPhase: string | undefined;

  // ===== Anzeige =====
  get s(): DeviationSession { return this.session; }
  get lang(): string { return cardLang(this.translate); }
  get who(): string { return this.name?.trim() || this.translate.instant('opponentNote.thisOpponent'); }
  get orientation(): 'white' | 'black' { return this.s.color === 'w' ? 'white' : 'black'; }
  get reviewing(): boolean { return this.s.phase === 'END' && this.reviewPly !== this.s.played.length; }
  get fen(): string { return this.reviewing ? this.s.fens[this.reviewPly] : this.s.fen; }
  get turnColor(): 'white' | 'black' { return this.fen.split(' ')[1] === 'b' ? 'black' : 'white'; }
  get dests(): Map<Key, Key[]> { return this.s.playable ? destsAt(this.s.fen) : new Map(); }
  get lastMove(): [Key, Key] | undefined { return this.reviewing ? this.reviewLastMove : this.s.lastMove; }
  private get reviewLastMove(): [Key, Key] | undefined {
    if (this.reviewPly === 0) return undefined;
    const before = this.s.fens[this.reviewPly - 1], after = this.s.fens[this.reviewPly];
    return diffSquares(before, after);
  }

  /** „Hier spielt Maras 4…h6 (7×) — das steht nicht in deinem Repertoire." */
  get askText(): string {
    const ply = this.s.played.length - 1;
    const move = plyLabel(this.s.played, null, ply, this.lang);
    const line = this.s.current;
    const first = !!line && ply === line.path.length;   // der Abweichungszug selbst (nicht seine Fortsetzung)
    return this.translate.instant(first ? 'deviations.askDeviation' : 'deviations.askContinuation',
      { who: this.who, move, n: this.s.askGames });
  }

  /** „statt 3…Sf6" der laufenden Abweichung. */
  get insteadText(): string {
    const d = this.s.current?.deviation;
    return d && d.repertoire.length ? this.translate.instant('deviations.instead', { moves: deviationText(d, this.lang).instead }) : '';
  }

  get pathHintText(): string {
    const h = this.s.pathHint;
    if (!h) return '';
    return this.translate.instant('deviations.pathHint', { move: plyLabel([...this.s.played, h], null, this.s.played.length, this.lang) });
  }

  get verdictBest(): string {
    return (this.s.verdict?.best ?? []).map(b => `${cardSan(b.san, this.lang)} (${b.evalText})`).join(', ');
  }

  /** Die eigene Antwort in der Notation der Sprache („Sxc6"). */
  get answerLabel(): string { return cardSan(this.s.answerSan, this.lang); }

  get lossPawns(): string { return ((this.s.verdict?.lossCp ?? 0) / 100).toFixed(2); }

  /** Partien des Gegners zur gezeigten Stellung (nach seinen Partien abgewichen?). */
  get note(): OpponentNoteView | null {
    if (!this.data || !this.s.fens.length) return null;
    const ply = this.s.phase === 'END' ? this.reviewPly : this.s.played.length;
    return opponentNoteView(this.s.fens, this.s.played, ply, this.data.positions, {
      who: this.name, band: null, after: 'engine', lang: this.lang, t: (k, p) => this.translate.instant(k, p),
    });
  }

  /** Maia-Stufe ≈ Elo des Gegners (nächste verfügbare). */
  get maiaElo(): number { return nearestMaiaElo(this.data?.elo ?? 1800); }

  /** „Gegen Maia weiterspielen": Analysebrett ab der Schlussstellung, Maia-Karte offen, Stärke des Gegners, meine Seite unten. */
  get maiaLink(): string {
    const q = new URLSearchParams({ fen: this.s.fen, orientation: this.orientation, maia: String(this.maiaElo) });
    return '/analysis?' + q.toString();
  }

  /** Die ganze Linie im Analysebrett (Züge als UCI ab der Grundstellung). */
  get analysisLink(): string {
    return '/analysis?' + new URLSearchParams({ moves: uciOf(this.s.fens).join(' '), orientation: this.orientation }).toString();
  }

  get progress(): { done: number; total: number } { return { done: Math.min(this.s.index + 1, this.s.queue.length), total: this.s.queue.length }; }

  // ===== Bedienung =====
  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void { this.s.onMove(ev.orig, ev.dest, ev.promotion); }
  retry(): void { this.s.retry(); }
  showSolution(): void { this.s.showSolution(); }
  next(): void { this.s.next(); }
  skip(): void { this.s.skip(); }
  back(): void { this.location.back(); }

  setThreshold(v: number): void {
    if (!(THRESHOLD_OPTIONS as readonly number[]).includes(v)) return;
    this.threshold = v;
    writeRaw(localStore(), DEVIATION_THRESHOLD_KEY, String(v));
  }

  reviewTo(p: number): void {
    if (this.s.phase !== 'END') return;
    this.reviewPly = Math.max(0, Math.min(this.s.played.length, p));
    this.syncReviewEngine();
    this.cdr.markForCheck();
  }

  toggleReviewEngine(): void {
    if (!this.engine) return;
    this.reviewEngineOn = !this.reviewEngineOn;
    saveReviewEngineOn(this.reviewEngineOn);
    this.syncReviewEngine();
    this.cdr.markForCheck();
  }

  get reviewEvalText(): string { return this.reviewEngine.evalText; }
  get reviewWhiteHeight(): number { return this.reviewEngine.whiteHeight; }
  get reviewBestLine(): string { return this.reviewEngine.bestLine; }
  get reviewEngineRunning(): boolean { return this.reviewEngine.active; }

  private syncReviewEngine(): void {
    const want = this.s.phase === 'END' && this.reviewEngineOn && !!this.engine && !this.destroyed;
    this.reviewEngine.sync(want ? this.fen : null);
  }

  /** Leertaste/Enter = weiter (guter Zug, Linienende); am Linienende ←/→/Pos1/Ende = durchklicken. */
  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (e.ctrlKey || e.metaKey || e.altKey || !isBoardHotkey(e)) return;
    if (this.s.phase === 'END' && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) {
      this.reviewTo(e.key === 'ArrowLeft' ? this.reviewPly - 1 : e.key === 'ArrowRight' ? this.reviewPly + 1 : e.key === 'Home' ? 0 : this.s.played.length);
      e.preventDefault();
      return;
    }
    if ((e.key === ' ' || e.key === 'Enter') && this.s.tap()) e.preventDefault();
  }
}

/** Von-/Nach-Feld zwischen zwei Stellungen (für den letzten Zug beim Durchklicken). */
function diffSquares(before: string, after: string): [Key, Key] | undefined {
  const uci = uciBetween(before, after);
  return uci ? [uci.slice(0, 2) as Key, uci.slice(2, 4) as Key] : undefined;
}

function uciBetween(before: string, after: string): string | null {
  try {
    const c = new Chess(before);
    const target = after.split(' ').slice(0, 4).join(' ');
    for (const m of c.moves({ verbose: true })) {
      c.move(m);
      if (c.fen().split(' ').slice(0, 4).join(' ') === target) return m.from + m.to + (m.promotion ?? '');
      c.undo();
    }
  } catch { /* kaputte Stellung */ }
  return null;
}

function uciOf(fens: readonly string[]): string[] {
  const out: string[] = [];
  for (let i = 1; i < fens.length; i++) { const u = uciBetween(fens[i - 1], fens[i]); if (!u) break; out.push(u); }
  return out;
}
