/**
 * Maia-3 als Sparringsgegner im Analysebrett: die festen Werte an EINER Stelle.
 *
 * Das Modell (`maia3_simplified.onnx`, CSSLab, GPL-3.0) liegt nicht im Repo — `maia-model/fetch.sh`
 * holt es beim Build per gepinntem Commit. `version` sind die ersten acht Zeichen der sha256 aus
 * fetch.sh, `bytes` die Größe von dort: DeploymentConfigTests.Maia_ModelPin_AndDelivery_StayConsistent
 * hält beide Stellen zusammen. Wer den Pin ändert, ändert ihn dort UND hier.
 *
 * `bytes` ist mehr als eine Info: eine fehlende Datei unter /assets/ beantwortet der SPA-Fallback
 * mit 200 und der index.html — ob das Modell da ist, erkennt man deshalb nur an der GRÖSSE, nie am
 * Status (siehe `MaiaModelStore`). `version` hängt am Cache-Schlüssel im Browser: ein neuer Pin
 * bedeutet einen neuen Eintrag, der alte wird beim nächsten Laden weggeräumt.
 */
export const MAIA_MODEL = { url: '/assets/maia/maia3_simplified.onnx', version: '405bf76c', bytes: 45683686 } as const;

/** Die Stärken zur Auswahl — Elo als ROHER Wert ans Modell (`elo_self` = `elo_oppo`). Maia ist auf
 *  Lichess-Partien trainiert; als Faustregel liegt die Zahl rund 200 über der FIDE-Elo. */
export const MAIA_ELO_OPTIONS = [600, 800, 1000, 1200, 1400, 1600, 1800, 2000, 2200, 2400, 2600] as const;
export const MAIA_DEFAULT_ELO = 1600;
/** localStorage: die zuletzt gewählte Stärke, je Gerät. */
export const MAIA_ELO_KEY = 'rookhub_analysis_maia_elo';
/** Nucleus-Schwelle der Zugwahl: gewürfelt wird nur unter den Zügen, die zusammen bis hierher reichen —
 *  der seltene Rest (Fehlgriffe im Promillebereich) fällt weg, menschliche Vielfalt bleibt. */
export const MAIA_TOP_P = 0.95;

/** „Schlechte Züge melden": ab diesem Verlust (Bauern, aus Sicht des Ziehenden) gilt ein eigener Zug als nicht gut. */
export const MAIA_BAD_MOVE_PAWNS = 0.2;
/** Bis zu dieser Tiefe (höchstens die eingestellte) rechnet die stille Engine die Stellung nach dem eigenen Zug, bevor
 *  geurteilt wird — Vorher- und Nachher-Wert werden bei GLEICHER Tiefe verglichen. */
export const MAIA_CHECK_DEPTH = 14;
/** localStorage: „Schlechte Züge melden" an/aus, je Gerät (Vorgabe aus). */
export const MAIA_WARN_KEY = 'rookhub_analysis_maia_warn';
/** localStorage: „Bewertungsleiste anlassen" an/aus, je Gerät (Vorgabe aus). */
export const MAIA_EVALBAR_KEY = 'rookhub_analysis_maia_evalbar';
