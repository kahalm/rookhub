/**
 * Gemerkte Einstellungen des Analysebretts (localStorage, je Gerät) — hier, damit auch andere Seiten sie lesen,
 * ohne `analysis.component.ts` (und damit das ganze Analysebrett) in ihr Bündel zu ziehen. Heute liest sie die
 * Live-Engine der Partieseite (`LiveEngineSession`): dieselbe Engine und Tiefe, die man am Analysebrett gewählt hat.
 */
export const ANALYSIS_DEPTH_KEY = 'rookhub_analysis_depth';
/** 'wasm' oder die Lichess-Engine-ID der zuletzt gewählten External Engine. */
export const ANALYSIS_PROVIDER_KEY = 'rookhub_analysis_engine_provider';
/** Zahl der Engine-Linien am Analysebrett (1–5) — auch Vorbelegung eines Hintergrund-Auftrags aus dem ⋮-Menü. */
export const ANALYSIS_LINES_KEY = 'rookhub_analysis_lines';
/** Ab dieser Tiefe übernimmt die Bewertungsleiste des Analysebretts den Wert einer neuen Suche (auch die Zug-Prüfung im
 *  Maia-Sparring nimmt ihn als Mindesttiefe). Darunter schwanken die Zahlen stark (Tiefe 1–5 liegt gern eine Figur
 *  daneben) — die Leiste bliebe sonst bei jedem Zug unruhig. Muss unter dem kleinsten DEPTH_OPTIONS-Wert liegen, sonst
 *  erreicht eine vollständige Suche die Schwelle nie (endet sie vorher, gilt ihr letzter Wert trotzdem). */
export const EVAL_SETTLE_DEPTH = 10;
/** Engine an/aus am Analysebrett ('0' = aus, alles andere = an). */
export const ANALYSIS_ENGINE_ON_KEY = 'rookhub_analysis_engine';

// Bis 50: für eine externe Engine (mehrere Millionen Knoten/s) sind Tiefen jenseits von 30
// gut erreichbar. Mit der Browser-Engine dauern sie sehr lange — sie bleiben trotzdem
// wählbar, statt Optionen je nach Engine verschwinden zu lassen.
// ACHTUNG: Jeder Wert hier muss den Clamp in AnalysisEngineService.setDepth überleben,
// sonst wählt man 50 und bekommt stillschweigend weniger (Test hält das fest).
export const DEPTH_OPTIONS = [12, 16, 18, 20, 22, 26, 30, 35, 40, 45, 50];

/** Zahl der Engine-Linien, die das Brett anbietet (1–5). */
export function isLinesCount(n: number): boolean { return n >= 1 && n <= 5; }

/** Die drei Such-Einstellungen des Analysebretts. */
export interface AnalysisSearchSettings { linesCount: number; engineOn: boolean; depthSetting: number; }

/**
 * Gemerkte Einstellungen des Analysebretts lesen und schreiben (Zerlegung Z22). Jeder Zugriff ist abgesichert —
 * schon der Zugriff auf `localStorage` kann werfen (blockierte Cookies); dann bleibt es bei den Vorgaben.
 */
export class AnalysisSettings {
  /** Gemerkte Werte über `defaults` legen; fehlende oder ungültige behalten die Vorgabe. */
  load(defaults: AnalysisSearchSettings): AnalysisSearchSettings {
    const s = { ...defaults };
    try {
      const l = parseInt(localStorage.getItem(ANALYSIS_LINES_KEY) || '', 10);
      if (isLinesCount(l)) s.linesCount = l;
      s.engineOn = localStorage.getItem(ANALYSIS_ENGINE_ON_KEY) !== '0';
      const d = parseInt(localStorage.getItem(ANALYSIS_DEPTH_KEY) || '', 10);
      if (DEPTH_OPTIONS.includes(d)) s.depthSetting = d;
    } catch { /* kein Speicher: Vorgaben */ }
    return s;
  }

  saveEngineOn(on: boolean): void { this.write(ANALYSIS_ENGINE_ON_KEY, on ? '1' : '0'); }
  saveLines(n: number): void { this.write(ANALYSIS_LINES_KEY, String(n)); }
  saveDepth(d: number): void { this.write(ANALYSIS_DEPTH_KEY, String(d)); }
  /** Zuletzt gewählte Engine ('wasm' oder die Kennung einer externen). */
  saveProvider(id: string): void { this.write(ANALYSIS_PROVIDER_KEY, id); }
  loadProvider(): string | null {
    try { return localStorage.getItem(ANALYSIS_PROVIDER_KEY); } catch { return null; }
  }

  private write(key: string, value: string): void {
    try { localStorage.setItem(key, value); } catch { /* kein Speicher: nur diese Sitzung */ }
  }
}
