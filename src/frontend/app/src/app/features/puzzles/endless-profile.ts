import { signal } from '@angular/core';
import { PuzzleRatingRange, PuzzleService } from './puzzle.service';
import { EndlessConfig, EndlessSession, EndlessStorageService, EndlessSyncResponse } from './endless-storage.service';
import { ChainBlockRequest } from './endless-chain.service';
import { EndlessFasttrackState } from './endless-fasttrack-state';
import { buildBatchThemes, ENDLESS_CHAIN_BLOCK } from './endless-prefetch.util';
import { AuthService } from '../../core/auth.service';

/**
 * Spieler-Seite des Endlos-Modus (Zerlegung A4): Konfiguration (Start-Rating, Themen, Tiefe, Schwellen), Rekord,
 * abgeschlossene Läufe, Rating-Spanne der Puzzle-DB und die daraus abgeleiteten Ketten-Parameter. Gehört zum
 * {@link EndlessRunStore}; reine Klasse ohne DI (Signals für die Anzeige).
 */
export class EndlessProfile {
  config: EndlessConfig = { startElo: 700, themes: '', stockfishDepth: 16 };
  readonly highscore = signal(0);
  readonly sessionHistory = signal<EndlessSession[]>([]);
  puzzleRange: PuzzleRatingRange = { min: 100, max: 3000 };
  /** Konfigurationswerte aus dem Deep-Link (?themes/?elo). Gelten für diesen Aufruf und werden nach dem
   *  asynchronen Server-Abgleich erneut angewendet, statt vom gespeicherten Server-Stand überschrieben zu werden. */
  readonly urlConfigOverrides: Partial<EndlessConfig> = {};
  /** Schwächste Themen des Users (für „5 schwächste Themen trainieren"). */
  worstThemes: string[] = [];
  /** Manuell eingegebene Themen, gemerkt während „schwächste Themen" aktiv ist (Wiederherstellung beim Abschalten). */
  private savedManualThemes = '';
  /** Fasttrack-Schwellen (T1/T2) der Ketten-Kurve. */
  readonly fasttrack = new EndlessFasttrackState();

  constructor(
    private readonly storage: EndlessStorageService,
    private readonly puzzleService: PuzzleService,
    private readonly auth: AuthService,
  ) {}

  /** Erster Lauf des Users (keine abgeschlossene Session in der Historie) → bewusst steile, schnell tödliche
   *  Kurve (2000 nach 15, 3000 nach 30 Puzzles), bis genug Daten für die adaptive Kurve vorliegen. */
  isFirstRun(): boolean { return this.sessionHistory().length === 0; }

  /** Lokaler Stand sofort (ohne Latenz). */
  loadLocal(): void {
    this.config = this.storage.loadConfig(this.config);
    this.highscore.set(this.storage.loadHighscore());
    this.sessionHistory.set(this.storage.loadSessionHistory());
  }

  /** Server-Stand einmischen. Basis ist der gespeicherte Stand: Deep-Link-Werte gehören nicht in den Speicher
   *  (sonst wäre z. B. „schwächste Themen" nach einem geteilten Themen-Link dauerhaft aus). Ein schon laufender
   *  Lauf (?start=1/?resume=1 vor der Antwort) behält seine Konfiguration — Kettenblöcke und Stockfish-Tiefe lesen
   *  `config`; im Config-Screen (`onConfigScreen`) gelten die Link-Werte weiter. */
  mergeServer(serverData: EndlessSyncResponse, onConfigScreen: boolean): void {
    const merged = this.storage.mergeServerData(
      this.storage.loadConfig(this.config), this.highscore(), this.sessionHistory(), serverData
    );
    if (onConfigScreen) {
      this.config = { ...this.storage.loadConfig(merged.config), ...this.urlConfigOverrides };
    }
    this.highscore.set(merged.highscore);
    this.sessionHistory.set(merged.history);
    this.computeFasttrackSteps();
  }

  /** Deep-Link-Werte (?themes/?elo) für diesen Aufruf übernehmen (siehe {@link urlConfigOverrides}). */
  applyUrlOverrides(overrides: Partial<EndlessConfig>): void {
    Object.assign(this.urlConfigOverrides, overrides);
    Object.assign(this.config, this.urlConfigOverrides);
  }

  clampConfig(): void {
    this.config.startElo = Math.max(this.puzzleRange.min, Math.min(this.puzzleRange.max, this.config.startElo));
  }

  /** Konfiguration lokal + am Server sichern; `activeGame` = mitzuschickender Spielstand (null = kein offener Lauf). */
  saveConfig(activeGame: object | null): void {
    this.storage.saveConfig(this.config);
    this.storage.saveProgressToServer(this.config, this.highscore(), activeGame);
  }

  /** Aktive Themen-Filter für die Sichtbar-Anzeige (Config + Run). Leer = kein Filter. */
  activeFilterThemes(): string[] {
    const src = (this.config.worstTags ? this.worstThemes.join(' ') : this.config.themes) || '';
    return src.trim().split(/\s+/).filter(Boolean);
  }

  /** Toggle „5 schwächste Themen trainieren": schwächste Themen laden und in die Editbox schreiben (sichtbar).
   *  `save` sichert die Konfiguration (die Seite kennt den Lauf-Zustand dafür). */
  toggleWorstTags(enabled: boolean, save: () => void): void {
    if (enabled) {
      this.savedManualThemes = this.config.themes;
      this.ensureWorstThemes(() => { this.config.themes = this.worstThemes.join(' '); save(); });
    } else {
      this.config.themes = this.savedManualThemes;
      this.worstThemes = [];   // beim nächsten Aktivieren frisch laden
      save();
    }
  }

  /** Lädt die schwächsten Themen (nur eingeloggt + Option aktiv), ruft danach cb. */
  ensureWorstThemes(cb: () => void): void {
    if (!this.config.worstTags || !this.auth.isLoggedIn || this.worstThemes.length) { cb(); return; }
    this.puzzleService.getWorstThemes().subscribe({
      next: t => { this.worstThemes = t; cb(); },
      error: () => cb(),
    });
  }

  /** Themen-Filter für die Ketten-Generierung: „schwächste Themen" (ODER) hat Vorrang vor dem manuellen Themenfeld (UND). */
  batchThemes(): { themes?: string; themesAny?: string } {
    return buildBatchThemes(this.config.worstTags ?? false, this.worstThemes, this.config.themes);
  }

  computeFasttrackSteps(): void { this.fasttrack.compute(this.config, this.sessionHistory()); }

  /** Parameter eines Ketten-Blocks [startIndex, startIndex+count) entlang der aktuellen Kurve. */
  chainRequest(startIndex: number, count = ENDLESS_CHAIN_BLOCK): ChainBlockRequest {
    return {
      startElo: this.config.startElo, avgFirst: this.fasttrack.avgFirst, avgSecond: this.fasttrack.avgSecond,
      ratingMax: this.puzzleRange.max, startIndex, firstRun: this.isFirstRun(), count,
    };
  }
}

/** Konfigurationswerte aus einem Deep-Link (z. B. geteilter Endless-Link): `?themes=fork,pin` (oder `?tags=…`) belegt den
 *  Themenfilter vor (Komma/Leerzeichen, schaltet „schwächste Themen" für diesen Aufruf aus), `?elo=1500` das Start-Rating. */
export function configOverridesFromQuery(get: (key: string) => string | null): Partial<EndlessConfig> {
  const overrides: Partial<EndlessConfig> = {};
  const themesParam = get('themes') ?? get('tags');
  if (themesParam) {
    const themes = themesParam.split(/[,\s]+/).map(t => t.trim()).filter(Boolean);
    if (themes.length) {
      overrides.worstTags = false;
      overrides.themes = [...new Set(themes)].join(' ');
    }
  }
  const eloParam = parseInt(get('elo') ?? '', 10);
  if (Number.isFinite(eloParam) && eloParam > 0) overrides.startElo = eloParam;
  return overrides;
}
