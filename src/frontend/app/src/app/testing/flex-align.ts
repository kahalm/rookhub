/**
 * Berechnete Ausrichtung einer Flexbox vergleichbar machen (26.11.2026): Chrome Headless 158 meldet für
 * `align-items`/`justify-content: flex-start|flex-end` in `getComputedStyle` zeitweise `flow-start`/`flow-end`
 * (neue Schreibweise der Box-Ausrichtung) — die CI wurde dadurch zufällig rot, obwohl die Lage stimmte. Beide
 * Schreibweisen bedeuten in einer Flexbox dasselbe; geprüft wird daher immer die `flex-…`-Form.
 */
export function flexAlign(value: string): string {
  return value.trim().replace(/^flow-(start|end)$/, 'flex-$1');
}
