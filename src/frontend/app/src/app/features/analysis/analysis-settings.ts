/**
 * Gemerkte Einstellungen des Analysebretts (localStorage, je Gerät) — hier, damit auch andere Seiten sie lesen,
 * ohne `analysis.component.ts` (und damit das ganze Analysebrett) in ihr Bündel zu ziehen. Heute liest sie die
 * Live-Engine der Partieseite (`LiveEngineSession`): dieselbe Engine und Tiefe, die man am Analysebrett gewählt hat.
 */
export const ANALYSIS_DEPTH_KEY = 'rookhub_analysis_depth';
/** 'wasm' oder die Lichess-Engine-ID der zuletzt gewählten External Engine. */
export const ANALYSIS_PROVIDER_KEY = 'rookhub_analysis_engine_provider';
