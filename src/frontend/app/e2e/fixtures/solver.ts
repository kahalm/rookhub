import { expect, Locator, Page } from '@playwright/test';

/** Spielweise je Bereich (SolveModeService, localStorage `rookhub_solve_modes`). */
export type SolveMode = 'easy' | 'training';

/**
 * Legt die Spielweise fuer Standard- und Endlos-Puzzles fest, BEVOR die Seite laedt. Ohne
 * gemerkte Wahl legt sich beim ersten Einstieg ein modaler Auswahldialog uebers Brett.
 * „easy" = Stufe 0 (Figuren normal ziehbar), „training" = die eingestellte
 * Visualisierungsstufe (`rookhub_visualization`), mindestens 1.
 */
export async function seedSolveMode(page: Page, mode: SolveMode): Promise<void> {
  await page.addInitScript((m: SolveMode) => {
    const at = Date.now();
    localStorage.setItem('rookhub_solve_modes', JSON.stringify({ puzzles: { mode: m, at }, endless: { mode: m, at } }));
  }, mode);
}

/** Die Statuskarte des Solvers; sie traegt den Zustand als `data-state`. */
export function statusCard(page: Page): Locator {
  return page.locator('.psc-card');
}

/** Wartet auf einen Solver-Zustand (AWAITING_USER_MOVE, THINKING, PLAYING, SOLVED, FAILED, …). */
export async function expectState(page: Page, state: string, timeout = 10_000): Promise<void> {
  await expect(statusCard(page)).toHaveAttribute('data-state', state, { timeout });
}
