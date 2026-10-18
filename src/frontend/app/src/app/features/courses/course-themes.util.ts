/**
 * Thema-Abzeichen eines Kurses (UI-Sweep 2026-10-11, r2-rp-taktik-chip; d3-rk-kurs-thema-taktik-vorgabe): nur für
 * GESETZTE Themen. Der Server liefert ohne gesetzte Themen die Vorgabe ["tactics"] — als Abzeichen unterschied „Taktik“
 * damit nichts (auch Eröffnungs- und Endspielkurse trugen es). EINE Regel für Kurskarte und Kursdetail.
 *
 * @returns i18n-Schlüssel `trainingGoals.theme.<thema>` der gesetzten Themen, sonst leer.
 */
export function setThemeKeys(themes: readonly string[] | null | undefined): string[] {
  const list = themes ?? [];
  if (list.length === 0 || (list.length === 1 && list[0] === 'tactics')) return [];
  return list.map(t => 'trainingGoals.theme.' + t);
}
