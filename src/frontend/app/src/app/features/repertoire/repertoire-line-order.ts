import { localStore, readRaw } from '../../core/local-json-store';

/**
 * Reihenfolge der Linien im Repertoire-Trainer (0.996.0) — je Nutzer UND Repertoire einstellbar, am Server gespeichert
 * (`GET …/training/config` → `lineOrder`, `PUT …/training/line-order`), damit sie über Geräte hinweg gilt.
 *
 * <p><b>Handgespiegelt</b>: dieselbe Liste steht im Backend als `RepertoireLineOrders.All`
 * (`Services/Repertoires/RepertoireLineOrders.cs`). Beide Seiten nageln sie mit LITERALEN Werten fest
 * (`repertoire-line-order.spec.ts` ↔ `RepertoireTrainingServiceTests.LineOrders_MatchTheFrontendMirror`).
 * Die Reihenfolge HIER ist die Anzeige-Reihenfolge der Auswahl.</p>
 */
export const LINE_ORDERS = ['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency', 'opponent'] as const;

/**
 * `leastTrained` — wenig trainierte zuerst: kürzeste Wiederkehr bei richtiger Lösung zuerst (Stufe 0 vor 1 vor 2 …);
 * `mostTrained` — die Umkehrung; `random` — gemischt; `repertoire` — wie im PGN; `frequency` — „Häufigste zuerst"
 * (Lichess-Explorer, der frühere Geräte-Schalter); `opponent` — „Nach dem Gegner" (0.999.3): was der gespeicherte
 * Gegner eines „Prep: …"-Repertoires am wahrscheinlichsten spielt, zuerst. Nur mit gespeichertem Gegner wählbar
 * ({@link availableLineOrders}) — der Server lehnt den Wert sonst ab.
 */
export type LineOrder = typeof LINE_ORDERS[number];

/** Gilt, solange für das Repertoire nichts gewählt ist (und der alte Geräte-Schalter aus ist). */
export const DEFAULT_LINE_ORDER: LineOrder = 'leastTrained';

/** Ein Wert vom Server bzw. aus der Offline-Kopie; alles außerhalb der Liste (auch fehlend) → `null`. */
export function parseLineOrder(value: unknown): LineOrder | null {
  return typeof value === 'string' && (LINE_ORDERS as readonly string[]).includes(value) ? value as LineOrder : null;
}

/** Die Reihenfolgen, die für ein Repertoire zur Wahl stehen: `opponent` nur mit gespeichertem Gegner. */
export function availableLineOrders(hasOpponent: boolean): LineOrder[] {
  return LINE_ORDERS.filter(o => o !== 'opponent' || hasOpponent);
}

/**
 * Die geltende Reihenfolge: der gespeicherte Wert; ohne einen (nichts gewählt, unbekannt) `frequency`, wenn der alte
 * Geräte-Schalter „Häufigste zuerst" an ist — niemand verliert seine bisherige Reihenfolge —, sonst die Vorgabe.
 *
 * <p>`hasOpponent` = das Repertoire wird gegen seinen GESPEICHERTEN Gegner trainiert: nur dann gilt ein gewähltes
 * `opponent` (ohne Gegner fällt es auf die Vorgabe), und der alte Schalter zählt dort nicht — er hatte in der
 * Gegner-Sitzung nie Wirkung (dort reihte der Gegner) und soll sie nicht nachträglich bekommen.</p>
 */
export function effectiveLineOrder(stored: unknown, legacyFrequencyOn: boolean, hasOpponent = false): LineOrder {
  const chosen = parseLineOrder(stored);
  if (chosen === 'opponent') return hasOpponent ? chosen : DEFAULT_LINE_ORDER;
  return chosen ?? (legacyFrequencyOn && !hasOpponent ? 'frequency' : DEFAULT_LINE_ORDER);
}

/** Der frühere Geräte-Schalter „Häufigste zuerst" (bis 0.995.0). Wird nur noch GELESEN: gewählt wird je Repertoire. */
const LEGACY_FREQ_ORDER_KEY = 'rookhub_rep_train_freq_order';

export function readLegacyFrequencyOrder(): boolean {
  return readRaw(localStore(), LEGACY_FREQ_ORDER_KEY) === '1';
}

/** i18n-Schlüssel des Namens einer Reihenfolge („Wenig trainierte zuerst" …). */
export function lineOrderLabelKey(order: LineOrder): string {
  return 'repertoire.trainOrder.option.' + order;
}

/** i18n-Schlüssel des Erklärsatzes einer Reihenfolge. */
export function lineOrderHintKey(order: LineOrder): string {
  return 'repertoire.trainOrder.hint.' + order;
}
