import { CalcGrade, CalcReview, CalcReviewPatch, applyReviewPatch, emptyReview, normalizeGrade } from './calc-review.util';

/**
 * Geräte-lokaler Speicher des Kalkulations-Modus für NICHT ANGEMELDETE Nutzer.
 *
 * Ein öffentlicher Kalkulations-Kurs (`/{slug}`) ist ohne Konto benutzbar: Baum, Festlegung,
 * Rechenzeit und Bewertung entstehen genauso — sie gehen nur nicht an den Server, sondern
 * hierher. **Das ist die sichere Variante**: die Kalkulations-Endpoints müssen für SCHREIBzugriffe
 * gar nicht geöffnet werden (keine nullable UserId, keine anonyme Sitzungs-Id, keine neue
 * Schreib-Angriffsfläche). Serverseitige Persistenz bleibt angemeldeten Nutzern vorbehalten.
 *
 * Gleiches Muster wie die übrigen Offline-Speicher (`core/offline.service.ts`,
 * `features/puzzles/book-offline.util.ts`): ein localStorage-Schlüssel je Buch, alles in
 * try/catch — ein voller oder gesperrter Speicher (Privatmodus, Quota) darf NIE werfen, sondern
 * kostet höchstens die Persistenz.
 */

/** localStorage-Präfix; ein Schlüssel je Buch (`…_<bookId>`). */
export const CALC_LOCAL_PREFIX = 'rookhub_calc_local_';

/**
 * Deckel: so viele Stellungen hält ein Buch lokal fest. Ein Kurs kann hunderte Stellungen haben,
 * und ein Baum darf groß werden — ohne Obergrenze frisst ein einziger Kurs das ganze
 * localStorage-Kontingent (und nimmt damit Offline-Büchern/Repertoires den Platz weg).
 * Verdrängt wird die am längsten nicht angefasste Stellung.
 */
export const CALC_LOCAL_MAX_POSITIONS = 150;

/** Deckel je Baum (Zeichen). Der Server erlaubt 256 KB; lokal ist der Platz knapper. */
export const CALC_LOCAL_MAX_TREE_CHARS = 64 * 1024;

/** Was zu EINER Stellung lokal liegt: der Baum plus die drei Trainings-Werte. */
export interface CalcLocalEntry {
  /** Serialisierter Analysebaum; `null` = keiner (die Zeile trägt dann nur Trainings-Werte). */
  tree: string | null;
  /** Zeitpunkt der letzten Baum-Speicherung (ISO) — Gegenstück zu `treeUpdatedAt` vom Server. */
  updatedAt: string | null;
  chosenSan: string | null;
  chosenUci: string | null;
  secondsSpent: number;
  grade: CalcGrade | null;
  /** Letzte Berührung (ms) — nur für die Verdrängung, nie angezeigt. */
  touchedAt: number;
}

export type CalcLocalEntries = Record<string, CalcLocalEntry>;

interface CalcLocalStore {
  v: number;
  entries: CalcLocalEntries;
}

function storageKey(bookId: number): string {
  return `${CALC_LOCAL_PREFIX}${bookId}`;
}

function emptyEntry(): CalcLocalEntry {
  return { tree: null, updatedAt: null, chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null, touchedAt: 0 };
}

/** Fremden/alten Inhalt auf die erwartete Form bringen — kaputte Einträge fliegen still raus. */
function sanitize(raw: unknown): CalcLocalEntries {
  const out: CalcLocalEntries = {};
  const entries = (raw as CalcLocalStore | null)?.entries;
  if (!entries || typeof entries !== 'object') return out;
  for (const [id, value] of Object.entries(entries as Record<string, unknown>)) {
    if (!/^\d+$/.test(id) || !value || typeof value !== 'object') continue;
    const v = value as Partial<CalcLocalEntry>;
    const tree = typeof v.tree === 'string' && v.tree.length > 0 ? v.tree : null;
    out[id] = {
      tree,
      updatedAt: typeof v.updatedAt === 'string' ? v.updatedAt : null,
      chosenSan: typeof v.chosenSan === 'string' ? v.chosenSan : null,
      chosenUci: typeof v.chosenUci === 'string' ? v.chosenUci : null,
      secondsSpent: Math.max(0, Math.floor(Number(v.secondsSpent)) || 0),
      grade: normalizeGrade(v.grade),
      touchedAt: Math.max(0, Math.floor(Number(v.touchedAt)) || 0),
    };
  }
  return out;
}

/** Alle lokal gespeicherten Stellungen eines Buchs (leer, wenn nichts/kaputt/gesperrt). */
export function readCalcLocal(bookId: number): CalcLocalEntries {
  try {
    const raw = localStorage.getItem(storageKey(bookId));
    return raw ? sanitize(JSON.parse(raw)) : {};
  } catch { return {}; }
}

/** Stand EINER Stellung; `null`, wenn es lokal nichts dazu gibt. */
export function readCalcLocalEntry(bookId: number, bookPuzzleId: number): CalcLocalEntry | null {
  return readCalcLocal(bookId)[String(bookPuzzleId)] ?? null;
}

/** Die drei Trainings-Werte einer Stellung (Standardwerte, wenn nichts gespeichert ist). */
export function readCalcLocalReview(bookId: number, bookPuzzleId: number): CalcReview {
  const entry = readCalcLocalEntry(bookId, bookPuzzleId);
  if (!entry) return emptyReview();
  return {
    chosenSan: entry.chosenSan,
    chosenUci: entry.chosenUci,
    secondsSpent: entry.secondsSpent,
    grade: entry.grade,
  };
}

/**
 * Schreiben mit Deckel: erst die überzähligen (am längsten nicht angefassten) Stellungen
 * verdrängen, dann speichern. Scheitert das Speichern trotzdem (Quota/gesperrt), wird noch
 * einmal aggressiv halbiert — und danach still aufgegeben. Geworfen wird nie.
 */
function persist(bookId: number, entries: CalcLocalEntries, keepId: string): boolean {
  const trimmed = evict(entries, keepId, CALC_LOCAL_MAX_POSITIONS);
  if (write(bookId, trimmed)) return true;
  const halved = evict(trimmed, keepId, Math.max(1, Math.floor(Object.keys(trimmed).length / 2)));
  return write(bookId, halved);
}

function write(bookId: number, entries: CalcLocalEntries): boolean {
  try {
    const store: CalcLocalStore = { v: 1, entries };
    localStorage.setItem(storageKey(bookId), JSON.stringify(store));
    return true;
  } catch { return false; }
}

/** Auf `max` Einträge eindampfen; die gerade bearbeitete Stellung bleibt immer erhalten. */
function evict(entries: CalcLocalEntries, keepId: string, max: number): CalcLocalEntries {
  const ids = Object.keys(entries);
  if (ids.length <= max) return entries;
  const order = ids
    .filter(id => id !== keepId)
    .sort((a, b) => (entries[a].touchedAt || 0) - (entries[b].touchedAt || 0));
  const out = { ...entries };
  let over = ids.length - max;
  for (const id of order) {
    if (over <= 0) break;
    delete out[id];
    over--;
  }
  return out;
}

function touch(entries: CalcLocalEntries, id: string): CalcLocalEntry {
  const entry = entries[id] ?? emptyEntry();
  entry.touchedAt = Date.now();
  entries[id] = entry;
  return entry;
}

/**
 * Baum ablegen. Antwort ist der Speicher-Zeitstempel (wie `treeUpdatedAt` beim Server) —
 * `null`, wenn der Baum den Deckel sprengt oder der Speicher nicht mitspielt; die Oberfläche
 * zeigt dann „nicht gespeichert" statt eine Lüge.
 */
export function writeCalcLocalTree(bookId: number, bookPuzzleId: number, treeJson: string): string | null {
  if (!treeJson || treeJson.length > CALC_LOCAL_MAX_TREE_CHARS) return null;
  const entries = readCalcLocal(bookId);
  const id = String(bookPuzzleId);
  const entry = touch(entries, id);
  const updatedAt = new Date().toISOString();
  entry.tree = treeJson;
  entry.updatedAt = updatedAt;
  return persist(bookId, entries, id) ? updatedAt : null;
}

/** Baum verwerfen; die Trainings-Werte bleiben stehen (wie beim Server-DELETE). */
export function deleteCalcLocalTree(bookId: number, bookPuzzleId: number): void {
  const entries = readCalcLocal(bookId);
  const id = String(bookPuzzleId);
  if (!entries[id]) return;
  const entry = touch(entries, id);
  entry.tree = null;
  entry.updatedAt = null;
  // Zeile ganz weg, wenn auch sonst nichts mehr dran hängt.
  if (!entry.chosenSan && !entry.chosenUci && !entry.secondsSpent && entry.grade === null) delete entries[id];
  persist(bookId, entries, id);
}

/**
 * Festlegung/Zeit/Stufe ändern — dieselbe Patch-Semantik wie beim Server (`secondsDelta` wird
 * ADDIERT, fehlende Felder bleiben unverändert). Antwort ist der neue Stand, oder `null`, wenn der
 * Speicher nicht mitspielte (Privatmodus, Quota) — dann steht der Wert NIRGENDS.
 *
 * Die Antwort MUSS ausgewertet werden (wie bei {@link writeCalcLocalTree}): wer den berechneten
 * Stand zurückgibt, ohne auf `persist` zu schauen, meldet der Oberfläche einen Erfolg, den es nicht
 * gab — Festlegung, Zeit und Bewertung stünden als „gespeichert" da und wären nach dem Neuladen weg.
 */
export function writeCalcLocalReview(bookId: number, bookPuzzleId: number, patch: CalcReviewPatch): CalcReview | null {
  const entries = readCalcLocal(bookId);
  const id = String(bookPuzzleId);
  const entry = touch(entries, id);
  const next = applyReviewPatch(
    { chosenSan: entry.chosenSan, chosenUci: entry.chosenUci, secondsSpent: entry.secondsSpent, grade: entry.grade },
    patch);
  entry.chosenSan = next.chosenSan;
  entry.chosenUci = next.chosenUci;
  entry.secondsSpent = next.secondsSpent;
  entry.grade = next.grade;
  return persist(bookId, entries, id) ? next : null;
}

/** Alles Lokale dieses Buchs vergessen. */
export function clearCalcLocal(bookId: number): void {
  try { localStorage.removeItem(storageKey(bookId)); } catch { /* gesperrt → egal */ }
}
