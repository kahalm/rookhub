/**
 * Ein Symbol auf dem Brett: die Klasse des aktuellen Zugs am Zielfeld (seit 0.557.0, wie chess.com beim Durchsehen).
 * Liegt neben dem Brett (`ChessBoardComponent.badge`), damit auch shared/-Oberflächen (Spielerkarte) es ohne Import aus
 * features/ halten können; gezeichnet wird es in `features/games/move-badge.util.ts`.
 */
export interface BoardBadge {
  /** Zielfeld des Zugs („d5"; bei der Rochade das Feld des Königs). */
  square: string;
  /** Inhalt für Chessgrounds `customSvg` — Koordinaten 0..100 über dem Feld. */
  svg: string;
}
