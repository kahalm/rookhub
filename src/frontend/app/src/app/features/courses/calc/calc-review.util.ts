/**
 * Selbstbewertung des Kalkulations-Modus — reine Datenlogik, kein Angular.
 *
 * Der Trainings-Zyklus ist: rechnen → sich festlegen → Lösung ANDERSWO prüfen → sich selbst
 * bewerten. Die drei Angaben je Stellung (Festlegung, Rechenzeit, Stufe) liegen BEWUSST neben
 * dem Baum-JSON (eigene Server-Spalten): im opaken `TreeJson` vergraben wären sie für immer
 * unabfragbar, so lassen sich Kapitel vergleichen und Auswertungen bauen.
 *
 * Wichtig: Der Kalkulations-Modus kennt die Lösung nach wie vor NICHT. Die Bewertung ist reine
 * Selbsteinschätzung — hier wird nichts nachgerechnet.
 */

/**
 * Die fünf Stufen der Selbstbewertung, von schlecht nach gut. Bewusst BENANNTE Stufen statt einer
 * Zahl von 0 bis 10: eine Stufe ist reproduzierbar, „7 von 10" heißt nächste Woche etwas anderes
 * als heute.
 *
 * Die Reihenfolge ist Absicht: „Hauptfolge nicht gesehen" wiegt schwerer als „Nebenfolgen nicht
 * gesehen" — die Hauptfortsetzung ist der Kern der Rechnung, Nebenvarianten sind Kür.
 */
export const CALC_GRADE_KEYS = [
  'notSolved',        // 0 — nicht gelöst
  'someIdeas',        // 1 — manche Ideen gesehen
  'moveNoMainLine',   // 2 — richtiger Zug, Hauptfolge nicht gesehen
  'moveNoSideLines',  // 3 — richtiger Zug, Nebenfolgen nicht gesehen
  'solved',           // 4 — gelöst
] as const;

export type CalcGradeKey = typeof CALC_GRADE_KEYS[number];
/** Index in {@link CALC_GRADE_KEYS} — GENAU das wird gespeichert (nicht die Punktzahl). */
export type CalcGrade = 0 | 1 | 2 | 3 | 4;

/** Höchstpunktzahl EINER Stellung (Punkte der besten Stufe). */
export const CALC_MAX_POINTS_PER_POSITION = 4;

/**
 * Punkte einer Stufe. Heute linear 0..4 — und genau deshalb wird die STUFE gespeichert und nicht
 * die Punktzahl: eine spätere Neugewichtung ändert dann nur diese eine Funktion, ohne die
 * Bedeutung bereits gespeicherter Bewertungen umzuschreiben. `null` = noch nicht bewertet und
 * zählt in Summen als 0 (die Summe steht immer neben ihrem Maximum, siehe {@link formatScore}).
 */
export function gradePoints(grade: CalcGrade | null | undefined): number {
  return grade == null ? 0 : grade;
}

/** Eine Stufe, wie die Auswahl sie anzeigt (Bedeutung + die Punkte, die sie einbringt). */
export interface CalcGradeOption {
  grade: CalcGrade;
  key: CalcGradeKey;
  /** Ausgeschriebene Bedeutung („Richtiger Zug, aber Hauptfolge nicht gesehen"). */
  labelKey: string;
  /** Kurzform für enge Stellen (Sprungliste). */
  shortKey: string;
  points: number;
}

/** Die Auswahl in Anzeige-Reihenfolge (schlecht → gut). */
export const CALC_GRADE_OPTIONS: readonly CalcGradeOption[] = CALC_GRADE_KEYS.map((key, index) => ({
  grade: index as CalcGrade,
  key,
  labelKey: `calc.review.grade.${key}`,
  shortKey: `calc.review.gradeShort.${key}`,
  points: gradePoints(index as CalcGrade),
}));

/** Festlegung + Rechenzeit + Stufe einer Stellung, so wie sie der Server führt. */
export interface CalcReview {
  /** Erster Zug, auf den sich der Nutzer festgelegt hat (SAN), null = keine Festlegung. */
  chosenSan: string | null;
  /** Derselbe Zug in UCI — daran erkennt die Anzeige den Zug im Baum wieder. */
  chosenUci: string | null;
  /** Aufsummierte AKTIVE Rechenzeit an dieser Stellung (Sekunden). */
  secondsSpent: number;
  /** Selbstbewertung als STUFE 0..4; null = noch nicht bewertet (≠ Stufe 0 „nicht gelöst"). */
  grade: CalcGrade | null;
}

/**
 * Änderungswunsch an den Server: nur die GESETZTEN Felder ändern sich. Die Zeit ist ein DELTA
 * (der Server addiert) — zwei Geräte/Tabs an derselben Stellung addieren so beide ihre Zeit,
 * statt sich gegenseitig zu überschreiben.
 */
export interface CalcReviewPatch {
  chosenSan?: string | null;
  chosenUci?: string | null;
  grade?: CalcGrade | null;
  secondsDelta?: number;
  /**
   * IDEMPOTENZ-MARKE des Zeit-Deltas (siehe {@link newSecondsToken}). Gehört fest zu
   * `secondsDelta`: der Server ADDIERT die Zeit, ein Wiederholversuch DARF sie nicht zweimal
   * buchen. Beim Wiedereinreihen nach einem Fehler bleibt die Marke deshalb dieselbe — eine neue
   * würde den Schutz aushebeln.
   */
  secondsToken?: string;
}

/**
 * Eine neue Marke für ein frisch gemessenes Zeit-Delta. Zeit + Zufall reichen: sie muss nur je
 * Stellung eindeutig sein, der Server vergleicht sie bloß mit der zuletzt verbuchten.
 */
export function newSecondsToken(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

/**
 * Was tatsächlich über die Leitung geht. `null` kann im JSON nicht zwischen „nicht mitgeschickt"
 * und „löschen" unterscheiden — dafür gibt es die beiden Schalter. Gesetzt wird die STUFE
 * (`grade`), nie die Punktzahl: die rechnet jede Seite selbst aus.
 */
export interface CalcReviewBody {
  addSeconds?: number;
  /** Marke zu `addSeconds` — der Server verbucht dieselbe Marke nur einmal. */
  secondsToken?: string;
  grade?: CalcGrade;
  clearGrade?: boolean;
  chosenSan?: string;
  chosenUci?: string;
  clearChoice?: boolean;
}

export function emptyReview(): CalcReview {
  return { chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null };
}

/**
 * Fremde Eingabe auf eine gültige Stufe bringen. Leer/unlesbar = „noch nicht bewertet" (null) —
 * ein gültiger Zustand, kein Fehler. Werte außerhalb der Skala werden auf sie geklemmt: sie können
 * nur von einem abweichenden Server kommen, und „bewertet" bleibt dann näher an der Wahrheit als
 * „unbewertet".
 */
export function normalizeGrade(value: unknown): CalcGrade | null {
  if (value === null || value === undefined || value === '') return null;
  const parsed = Math.round(Number(value));
  if (!Number.isFinite(parsed)) return null;
  return Math.min(CALC_GRADE_KEYS.length - 1, Math.max(0, parsed)) as CalcGrade;
}

/** Ändert der Patch überhaupt etwas? Ein reines `secondsDelta: 0` nicht. */
export function isNoopPatch(patch: CalcReviewPatch): boolean {
  const touchesChoice = 'chosenSan' in patch || 'chosenUci' in patch;
  const touchesGrade = 'grade' in patch;
  return !touchesChoice && !touchesGrade && !patch.secondsDelta;
}

/**
 * Zwei Änderungswünsche DERSELBEN Stellung zusammenlegen: bei Festlegung und Stufe gewinnt der
 * jüngere Stand, Zeiten ADDIEREN sich (es sind Deltas). Gebraucht wird das an zwei Stellen:
 * beim Aufstauen (solange eine Anfrage unterwegs ist) und beim Wiedereinreihen nach einem Fehler —
 * dort darf die schon gemessene Zeit nicht verloren gehen, die Festlegung aber auch nicht
 * zurückspringen.
 *
 * Die Zeit-MARKE des älteren Patches gewinnt, sobald der ältere Zeit trägt: genau der kann schon
 * beim Server angekommen sein (nur die Antwort ging verloren). Der Server erkennt die Marke wieder
 * und rechnet vom gewachsenen Delta nur die Differenz an — mit einer frischen Marke würde er alles
 * ein zweites Mal buchen.
 */
export function mergeReviewPatch(older: CalcReviewPatch, newer: CalcReviewPatch): CalcReviewPatch {
  const merged: CalcReviewPatch = { ...older };
  if ('chosenSan' in newer || 'chosenUci' in newer) {
    merged.chosenSan = newer.chosenSan ?? null;
    merged.chosenUci = newer.chosenUci ?? null;
  }
  if ('grade' in newer) merged.grade = newer.grade ?? null;
  const seconds = (older.secondsDelta ?? 0) + (newer.secondsDelta ?? 0);
  if (seconds > 0) {
    merged.secondsDelta = seconds;
    const token = (older.secondsDelta ? older.secondsToken : undefined)
      ?? newer.secondsToken ?? older.secondsToken;
    if (token) merged.secondsToken = token; else delete merged.secondsToken;
  } else {
    delete merged.secondsDelta;
    delete merged.secondsToken;
  }
  return merged;
}

/** Patch auf einen bekannten Stand anwenden (optimistische Anzeige, bevor der Server antwortet). */
export function applyReviewPatch(review: CalcReview, patch: CalcReviewPatch): CalcReview {
  const next: CalcReview = { ...review };
  if ('chosenSan' in patch || 'chosenUci' in patch) {
    next.chosenSan = patch.chosenSan ?? null;
    next.chosenUci = patch.chosenUci ?? null;
  }
  if ('grade' in patch) next.grade = normalizeGrade(patch.grade);
  next.secondsSpent = Math.max(0, review.secondsSpent + (patch.secondsDelta ?? 0));
  return next;
}

/** Patch in den Wortlaut der Anfrage übersetzen (siehe {@link CalcReviewBody}). */
export function toReviewBody(patch: CalcReviewPatch): CalcReviewBody {
  const body: CalcReviewBody = {};
  if ('chosenSan' in patch || 'chosenUci' in patch) {
    if (patch.chosenSan && patch.chosenUci) {
      body.chosenSan = patch.chosenSan;
      body.chosenUci = patch.chosenUci;
    } else {
      body.clearChoice = true;
    }
  }
  if ('grade' in patch) {
    const grade = normalizeGrade(patch.grade);
    if (grade === null) body.clearGrade = true; else body.grade = grade;
  }
  if (patch.secondsDelta) {
    body.addSeconds = patch.secondsDelta;
    // Ohne Marke wäre der addierende Zeit-Anteil nicht wiederholungsfest (siehe CalcReviewPatch).
    if (patch.secondsToken) body.secondsToken = patch.secondsToken;
  }
  return body;
}

/** Zeitangabe als m:ss bzw. h:mm:ss (eine Quelle für Kapitel-Timer, Stellungszeit und Summen). */
export function formatSeconds(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds || 0));
  const pad = (n: number) => n.toString().padStart(2, '0');
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

/** Summe der Punkte (nicht bewertete Stellungen zählen als 0, nicht als Lücke). */
export function sumPoints(items: readonly { grade: CalcGrade | null }[]): number {
  return items.reduce((sum, item) => sum + gradePoints(item.grade), 0);
}

/** Erreichbare Punkte für so viele Stellungen. */
export function maxPoints(positionCount: number): number {
  return Math.max(0, positionCount) * CALC_MAX_POINTS_PER_POSITION;
}

export function sumSeconds(items: readonly { secondsSpent: number }[]): number {
  return items.reduce((sum, item) => sum + (item.secondsSpent || 0), 0);
}

/**
 * Punktzahl IMMER mit ihrem Maximum: eine nackte Summe ist ohne die Zahl der Stellungen nicht
 * lesbar („14" sagt nichts, „14 / 24" schon).
 */
export function formatScore(points: number, max: number): string {
  return `${Math.max(0, Math.round(points))} / ${Math.max(0, Math.round(max))}`;
}
