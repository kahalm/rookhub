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
