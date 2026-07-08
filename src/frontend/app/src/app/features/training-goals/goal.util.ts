/**
 * Klemmt einen Trainingsziel-Eingabewert auf [0, max], rundet auf ganze Zahlen und
 * behandelt NaN/leer/negativ als 0. Gemeinsam genutzt vom persönlichen Trainingsziel
 * (training-goals.component), der Gruppen-Vorlage (admin.component) und den manuellen
 * Offline-Aktivitäten (manual-activities-card.component) — vermeidet die 3× kopierte Logik.
 */
export function clampGoal(v: number, max: number): number {
  return Math.max(0, Math.min(max, Math.round(v || 0)));
}
