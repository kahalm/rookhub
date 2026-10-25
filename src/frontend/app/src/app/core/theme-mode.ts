/**
 * Wie die Brett-Optik beim Lösen wechselt (Einstellung des Nutzers, `PreferencesService.themeMode`): fest, je Puzzle
 * zufällig oder „crazy". Liegt in `core/`, weil die Einstellungen sie speichern; die Anwendung auf das Brett steht in
 * `features/puzzles/board-theme.util.ts` (Codereview F8-013).
 */
export type ThemeMode = 'fixed' | 'random' | 'crazy';
