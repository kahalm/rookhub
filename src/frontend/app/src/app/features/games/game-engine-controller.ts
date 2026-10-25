import { signal } from '@angular/core';
import { ExternalEngineInfo, ExternalEngineService } from '../analysis/external-engine.service';
import { ANALYSIS_DEPTH_KEY, ANALYSIS_PROVIDER_KEY } from '../analysis/analysis-settings';
import { LiveEngineSession } from './live-engine-session';
import { MistakesSession } from './mistakes-session';

/** Was die Engine-Steuerung von der Partieseite braucht. */
export interface GameEngineHost {
  /** Eigene Engine-Instanz je Seite — über die Seite, damit Tests keinen echten Stockfish starten müssen. */
  createSession(): LiveEngineSession;
  currentFen(): string;
  currentMoveIndex(): number;
  /** Laufendes Fehler-Training (`null` = die Seite zeigt die Partie). */
  training(): MistakesSession | null;
  loggedIn(): boolean;
}

/** „Analysieren" im Fehler-Training: Sitzung, Ausgangsstellung und die Aufgabe, zu der sie gehört. */
export interface TrainingAnalysis {
  session: LiveEngineSession; base: string; index: number; side: string;
  /** Zug, der auf dem Brett markiert bleibt, solange die Variante leer ist (der eigene Fehlversuch). */
  lastMove?: [string, string];
}

/**
 * Engine der Partieseite (Zerlegung Z26, aus `SharedGameComponent`): die Live-Engine mit eigener Nebenvariante
 * (0.525.0), ihre Engine-Wahl im ⋮ (0.681.0) und „Analysieren" im Fehler-Training (0.526.2). Hält den Zustand als
 * Signale; die Seite reicht Stellung, Training und die Sitzungs-Fabrik herein und ruft {@link sync} je Prüflauf.
 */
export class GameEngineController {
  /** Live-Engine + eigene Züge (0.525.0) — im Fehler-Training aus, dort verriete sie die Lösung. */
  readonly live = signal<LiveEngineSession | null>(null);

  /**
   * „Analysieren" im Fehler-Training (seit 0.526.2): das Brett wird frei, die Live-Engine rechnet. Gilt bis zur nächsten
   * Aufgabe: {@link sync} beendet die Analyse, sobald die Aufgabe, die Seite oder die Phase („nochmal") wechselt.
   *
   * WO die Analyse anfängt, hängt daran, ob die Lösung schon bekannt ist (seit 0.527.2, gemeldet 2026-09-24):
   * - gefunden/gezeigt: ab der Stellung VOR dem Fehler, der Zug steht schon drauf — ← darf dorthin zurück;
   * - nach einem Fehlversuch: ab der Stellung NACH dem eigenen Zug, und das ist der Anfang der Variante. Wer ← dahinter
   *   zurückkönnte, stünde in der Aufgabenstellung, und die Engine zeigte dort Pfeil und Linien des gesuchten Zugs.
   */
  readonly trainingAnalysis = signal<TrainingAnalysis | null>(null);

  /** Externe Engines fuer die Live-Wahl im ⋮ (0.681.0) — ohne die Hintergrund-Engines, die gehoeren den Auftraegen
   *  (dieselbe Regel wie beim Uebernehmen der gemerkten Wahl). */
  readonly liveEngines = signal<ExternalEngineInfo[]>([]);

  constructor(private readonly host: GameEngineHost, private readonly externalEngines: ExternalEngineService) {}

  toggleLive(): void {
    if (this.live()) { this.stopLive(); return; }
    const session = this.host.createSession();
    this.live.set(session);
    session.sync(this.host.currentMoveIndex(), this.host.currentFen());
    this.useStoredRemoteEngine(session);
  }

  stopLive(): void {
    this.live()?.destroy();
    this.live.set(null);
  }

  /** Die Stellung auf dem Brett — in der Nebenvariante der Live-Engine deren Ende, sonst der Partiezug. */
  positionFen(): string {
    return this.live()?.fen(this.host.currentFen()) ?? this.host.currentFen();
  }

  /** Steht man in einer eigenen Variante (Live-Engine, mindestens ein eigener Zug auf dem Brett oder dahinter)? */
  inVariation(): LiveEngineSession | null {
    const l = this.live();
    return l && (l.variation().length > 0 || l.canRedo()) ? l : null;
  }

  loadLiveEngines(): void {
    if (!this.host.loggedIn()) return;
    this.externalEngines.listEngines().subscribe({
      next: r => {
        const background = r.backgroundEngineIds ?? [];
        this.liveEngines.set(r.engines.filter(e => !background.includes(e.id)));
      },
      error: () => this.liveEngines.set([]),
    });
  }

  /** Die gerade gewaehlte Live-Engine: die der laufenden Sitzung, sonst die gemerkte Wahl; `null` = Browser. */
  currentEngineId(): string | null {
    const session = this.live();
    if (session) return session.engineId();
    try {
      const stored = localStorage.getItem(ANALYSIS_PROVIDER_KEY);
      return stored && stored !== 'wasm' ? stored : null;
    } catch { return null; }
  }

  /**
   * Live-Engine waehlen (⋮ → Live-Engine). Gemerkt wird sie unter DEMSELBEN Schluessel wie am Analysebrett, die Wahl
   * gilt also auf beiden Seiten. Laeuft die Live-Engine schon, wechselt sie sofort; sonst startet sie mit der Wahl.
   */
  chooseLiveEngine(e: ExternalEngineInfo | null): void {
    try { localStorage.setItem(ANALYSIS_PROVIDER_KEY, e?.id ?? 'wasm'); } catch { /* kein Speicher: gilt nur jetzt */ }
    const session = this.live();
    if (!session) { this.toggleLive(); return; }
    if (e) session.useRemote(e, (id, work) => this.externalEngines.analyse(id, work));
    else session.useBrowser();
  }

  toggleTrainingAnalysis(): void {
    if (this.trainingAnalysis()) { this.stopTrainingAnalysis(); return; }
    const t = this.host.training();
    const m = t?.current();
    if (!t || !m) return;
    const session = this.host.createSession();
    const move = t.lastMove();
    if (t.phase() === 'wrong') {
      const base = t.boardFen();
      session.sync(-2, base);
      this.useStoredRemoteEngine(session);
      this.trainingAnalysis.set({ session, base, index: t.index(), side: t.side(), lastMove: move ?? undefined });
      return;
    }
    session.sync(-2, m.fenBefore);
    const san = t.phase() === 'right' ? t.foundSan() : m.bestSan;
    if (move && t.boardFen() !== m.fenBefore) {
      session.play({ from: move[0], to: move[1], san, fen: t.boardFen() }, m.fenBefore);
    }
    this.useStoredRemoteEngine(session);
    this.trainingAnalysis.set({ session, base: m.fenBefore, index: t.index(), side: t.side() });
  }

  stopTrainingAnalysis(): void {
    this.trainingAnalysis()?.session.destroy();
    this.trainingAnalysis.set(null);
  }

  /**
   * Mit der Partie abgleichen (aus `ngDoCheck` der Seite): geblättert → Nebenvariante weg, neue Stellung → rechnen
   * (billig ohne Änderung); die Trainings-Analyse endet, sobald Aufgabe, Seite oder Phase nicht mehr passen.
   */
  sync(): void {
    this.live()?.sync(this.host.currentMoveIndex(), this.host.currentFen());
    const a = this.trainingAnalysis();
    if (a) {
      const t = this.host.training();
      const phase = t?.phase();
      if (!t || t.index() !== a.index || t.side() !== a.side || phase === 'ask' || phase === 'checking' || phase === 'done') {
        this.stopTrainingAnalysis();
      } else {
        a.session.sync(-2, a.base);
      }
    }
  }

  /** Beim Verlassen der Seite: beide Sitzungen beenden. */
  destroy(): void { this.stopLive(); this.stopTrainingAnalysis(); }

  /** Dieselbe Tiefe wie am Analysebrett (dort gewählt und gemerkt), sonst 22. */
  storedDepth(): number {
    try {
      const d = parseInt(localStorage.getItem(ANALYSIS_DEPTH_KEY) || '', 10);
      return d >= 6 && d <= 50 ? d : 22;
    } catch { return 22; }
  }

  /**
   * Hat man am Analysebrett eine externe Engine gewählt (Lichess-Anbindung, z. B. die eigene Cloud-Engine), rechnet
   * auch hier sie — sonst Stockfish im Browser. Hintergrund-Engines bleiben außen vor (sie gehören den Aufträgen).
   */
  private useStoredRemoteEngine(session: LiveEngineSession): void {
    if (!this.host.loggedIn()) return;
    let stored: string | null = null;
    try { stored = localStorage.getItem(ANALYSIS_PROVIDER_KEY); } catch { /* kein Speicher → Browser */ }
    if (!stored || stored === 'wasm') return;
    this.externalEngines.listEngines().subscribe({
      next: r => {
        const background = r.backgroundEngineIds ?? [];
        const info = r.engines.find(e => e.id === stored && !background.includes(e.id));
        // Nur, solange die Session noch läuft: die Live-Engine ODER die „Analysieren"-Leiste des Fehler-Trainings
        // (dort ist live() immer null, trainMistakes beendet sie — F4-008).
        const active = this.live() === session || this.trainingAnalysis()?.session === session;
        if (info && active) session.useRemote(info, (id, work) => this.externalEngines.analyse(id, work));
      },
      error: () => { /* bleibt beim Browser */ },
    });
  }
}
