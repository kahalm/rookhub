import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { AuthService } from '@rh/core/auth.service';
import { BoardArrow } from '@rh/shared/pgn-viewer/chess-board.component';
import { ExternalEngineInfo, ExternalEngineService, isEngineOffline } from '@rh/features/analysis/external-engine.service';
import { LiveEngineSession } from '@rh/features/games/live-engine-session';
import { AnalysisEngineService } from '@rh/features/analysis/analysis-engine.service';
import { de } from '../../core/league-format';

/** Gemerkte Wahl der Prüfseite (localStorage): an/aus und welche Engine (`wasm` = Stockfish im Browser). */
export const SCAN_ENGINE_KEY = 'lh-scan-engine';
export const SCAN_ENGINE_DEPTH = 20;
/** Linien auf der Prüfseite (Wunsch 2026-10-10: „immer 5, 10 wäre besser") — 10 im Browser, externe Engines liefern 5. */
export const SCAN_ENGINE_LINES = 10;

export interface ScanEngineChoice { on: boolean; engine: string; }

export function readScanEngineChoice(): ScanEngineChoice {
  try {
    const v = JSON.parse(localStorage.getItem(SCAN_ENGINE_KEY) ?? 'null') as Partial<ScanEngineChoice> | null;
    return { on: v?.on === true, engine: typeof v?.engine === 'string' && v.engine ? v.engine : 'wasm' };
  } catch { return { on: false, engine: 'wasm' }; }
}

function writeScanEngineChoice(c: ScanEngineChoice): void {
  try { localStorage.setItem(SCAN_ENGINE_KEY, JSON.stringify(c)); } catch { /* ohne Speicher gilt die Wahl nur jetzt */ }
}

/**
 * Engine auf der Formular-Prüfseite (Wunsch 2026-10-08: „eine Engine zuschalten können, wahlweise welche"): rechnet die
 * Stellung, an der der Cursor steht — Linien darunter, bester Zug als blauer Pfeil (über `arrowsChange` an das Brett der
 * Seite). Nur eine Hilfe beim Lesen: was auf dem Formular steht, entscheidet weiter der Mensch. Standardmäßig AUS — die
 * Wahl bleibt je Gerät gemerkt. Externe Engines (eigener Broker, Lichess) nur angemeldet; die Hintergrund-Engines der
 * Analyseaufträge stehen wie am Analysebrett nicht zur Wahl.
 */
@Component({
  selector: 'lh-scan-engine',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe],
  template: `
    <div class="scan-engine">
      <div class="se-head">
        <label class="se-toggle"><input type="checkbox" [checked]="on()" (change)="toggle($any($event.target).checked)" /> Engine</label>
        <select class="se-pick" aria-label="Engine wählen" [value]="engine()" (change)="choose($any($event.target).value)">
          <option value="wasm" [selected]="engine() === 'wasm'">Stockfish (im Browser)</option>
          @for (e of engines(); track e.id) {
            <option [value]="e.id" [selected]="engine() === e.id" [disabled]="offline(e)">{{ e.name }}{{ offline(e) ? ' · offline' : '' }}</option>
          }
        </select>
        @if (session(); as s) {
          @if (on()) {
            @if (s.fallback()) { <span class="se-note warn">antwortet nicht — rechnet im Browser</span> }
            @if (s.depth() > 0) { <span class="se-note">Tiefe {{ s.depth() }}</span> }
            @if (s.isLc0() && s.nodes() > 0) { <span class="se-note">{{ s.nodes() | number:'1.0-0' }} Knoten</span> }
          }
        }
      </div>
      @if (on()) {
        @if (session(); as s) {
          <ol class="se-lines">
            @for (i of slots(); track i) {
              @if (s.lines()[i]; as l) {
                <li><span class="se-eval" [class.white]="l.positive">{{ l.evalText }}</span> <span class="se-san">{{ de(l.san) }}</span></li>
              } @else {
                <li class="empty" aria-hidden="true">&nbsp;</li>
              }
            }
          </ol>
        }
      }
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .se-head { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
    .se-toggle { display: inline-flex; align-items: center; gap: 6px; font-weight: 600; min-height: 44px; cursor: pointer; }
    .se-pick { min-height: 36px; max-width: 100%; }
    .se-note { font-size: .85em; opacity: .75; }
    .se-note.warn { color: var(--rh-warn, #b26a00); opacity: 1; }
    .se-lines { list-style: none; margin: 6px 0 0; padding: 0; font-variant-numeric: tabular-nums; }
    .se-lines li { display: flex; gap: 8px; padding: 2px 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-height: 1.4em; }
    .se-eval { flex: 0 0 auto; min-width: 3.6em; padding: 0 4px; border-radius: 4px; background: #333; color: #fff; text-align: center; font-weight: 600; }
    .se-eval.white { background: #eee; color: #222; }
    .se-san { overflow: hidden; text-overflow: ellipsis; }
  `],
})
export class ScanEngineComponent implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly external = inject(ExternalEngineService);

  /** Stellung, die gerechnet wird (Cursor der Prüfseite). */
  readonly fen = input.required<string>();
  /** Bester Zug als Pfeil — die Seite legt ihn zu ihren eigenen auf das Brett. */
  readonly arrowsChange = output<BoardArrow[]>();

  /** Immer so viele Zeilen wie die gewählte Engine liefert (leere als Platzhalter) — die Liste springt nicht. */
  readonly slots = computed(() => Array.from({ length: this.engine() === 'wasm' ? SCAN_ENGINE_LINES
    : Math.min(SCAN_ENGINE_LINES, AnalysisEngineService.MaxRemoteMultiPv) }, (_, i) => i));
  readonly de = de;
  readonly on = signal(false);
  readonly engine = signal('wasm');
  readonly engines = signal<ExternalEngineInfo[]>([]);
  readonly session = signal<LiveEngineSession | null>(null);

  constructor() {
    const c = readScanEngineChoice();
    this.engine.set(c.engine);
    if (c.on) this.start();
    this.loadEngines();
    // Stellung wechselt → rechnen (sync wirft dabei nichts weg, es gibt hier keine Nebenvariante).
    effect(() => {
      const fen = this.fen();
      const s = this.session();
      if (s && fen) untracked(() => s.sync(0, fen));
    });
    effect(() => {
      const s = this.session();
      const arrows = s && this.on() ? s.arrows() : [];
      untracked(() => this.arrowsChange.emit(arrows));
    });
  }

  offline(e: ExternalEngineInfo): boolean { return isEngineOffline(e); }

  toggle(on: boolean): void {
    if (on === this.on()) return;
    if (on) this.start(); else this.stop();
    this.save();
  }

  choose(id: string): void {
    this.engine.set(id || 'wasm');
    this.save();
    const s = this.session();
    if (s) this.applyEngine(s);
  }

  ngOnDestroy(): void { this.stop(); }

  /** Eigene Engine-Instanz — als Methode, damit Tests keinen echten Stockfish starten. */
  protected createSession(): LiveEngineSession {
    return new LiveEngineSession(undefined, SCAN_ENGINE_DEPTH, SCAN_ENGINE_LINES);
  }

  private start(): void {
    const s = this.createSession();
    this.session.set(s);
    this.on.set(true);
    this.applyEngine(s);
  }

  private stop(): void {
    this.session()?.destroy();
    this.session.set(null);
    this.on.set(false);
  }

  private applyEngine(s: LiveEngineSession): void {
    const info = this.engines().find(e => e.id === this.engine());
    if (info) s.useRemote(info, (id, work) => this.external.analyse(id, work));
    else s.useBrowser();
  }

  private save(): void { writeScanEngineChoice({ on: this.on(), engine: this.engine() }); }

  private loadEngines(): void {
    if (!this.auth.isLoggedIn) return;
    this.external.listEngines().subscribe({
      next: r => {
        const background = r.backgroundEngineIds ?? [];
        this.engines.set(r.engines.filter(e => !background.includes(e.id)));
        // Gemerkte externe Engine: erst jetzt bekannt — die laufende Sitzung darauf umstellen.
        const s = this.session();
        if (s && this.engine() !== 'wasm') this.applyEngine(s);
      },
      error: () => this.engines.set([]),
    });
  }
}
