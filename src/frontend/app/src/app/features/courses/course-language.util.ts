/**
 * Kurs-Kommentare mehrsprachig (Stufe C, 0.549.0): die REINEN Regeln der Sprachwahl — ohne Angular,
 * ohne Speicher, mit literalen Vektoren getestet (`course-language.util.spec.ts`).
 *
 * Grundbegriffe:
 * - **Sprachliste eines Kurses**: die QUELLE zuerst (die Oberfläche nennt sie „Original"), danach die
 *   Sprachen, in die es Übersetzungen gibt, alphabetisch. Genau so liefert der Server
 *   `BookPuzzleDto.commentLanguages` je Linie; für den Kurs setzt die Seite sie aus der
 *   Übersetzungs-Übersicht (`GET /api/courses/{id}/translations`) und den gesehenen Linien zusammen.
 * - **Gewünschte Sprache** (`requested`): die gemerkte Wahl des Geräts, sonst die Oberflächensprache.
 *   Sie geht als `?lang=` an den Server — der liefert je Stelle die Übersetzung, wo es eine aktuelle
 *   gibt, sonst das Original.
 * - **Wirksame Sprache**: die gewünschte, WENN der Kurs sie hat, sonst die Quelle. Genau das ist die
 *   Regel „Vorgabe = Oberflächensprache, wenn der Kurs sie hat, sonst das Original".
 * - „Original" wird als KÜRZEL der Quelle gemerkt (nicht als eigenes Wort): `?lang=<Quelle>` liefert
 *   laut Server immer das Original, und die Antwort trägt trotzdem `commentLanguages`.
 */

/** Ein Sprachkürzel klein und getrimmt; leer/fehlend → `null`. */
export function normLang(lang: string | null | undefined): string | null {
  const l = (lang ?? '').trim().toLowerCase();
  return l ? l : null;
}

/**
 * Zwei Sprachlisten zusammenführen. `incoming[0]` ist die Quelle (so liefert es der Server) und
 * GEWINNT gegen die bisher bekannte — eine korrigierte Quellsprache soll sich durchsetzen. Die alte
 * Quelle fällt dabei heraus; Übersetzungen beider Listen bleiben, alphabetisch, ohne Doppelte.
 */
export function mergeLanguages(
  known: readonly string[],
  incoming: readonly (string | null | undefined)[] | null | undefined,
): string[] {
  const inc = (incoming ?? []).map(normLang).filter((l): l is string => !!l);
  if (inc.length === 0) return [...known];
  const source = inc[0];
  const rest = new Set<string>([...known.slice(1), ...inc.slice(1)]);
  rest.delete(source);
  return [source, ...[...rest].sort()];
}

/** Sprachen eines Kurses aus seinen Linien (je Linie `commentLanguages`, Quelle zuerst). */
export function languagesFromLines(
  lines: readonly { commentLanguages?: readonly string[] | null }[] | null | undefined,
): string[] {
  let out: string[] = [];
  for (const l of lines ?? []) out = mergeLanguages(out, l?.commentLanguages ?? null);
  return out;
}

/** Was die Übersetzungs-Übersicht über die Sprachen des Kurses sagt (nur die Felder, die es braucht). */
export interface LanguageOverview {
  sourceLanguage?: string | null;
  languages?: readonly { language: string; linesTranslated: number }[] | null;
}

/** Sprachen eines Kurses aus der Übersetzungs-Übersicht: Quelle + jede Sprache mit mindestens einer
 *  übersetzten Linie (auch eine, die gerade erst entsteht — der Server füllt Lücken mit dem Original). */
export function languagesFromOverview(o: LanguageOverview | null | undefined): string[] {
  const source = normLang(o?.sourceLanguage);
  if (!source) return [];
  const langs = (o?.languages ?? [])
    .filter(l => (l?.linesTranslated ?? 0) > 0)
    .map(l => normLang(l.language))
    .filter((l): l is string => !!l);
  return mergeLanguages([], [source, ...langs]);
}

/**
 * Die WIRKSAME Sprache: die gewünschte, wenn der Kurs sie hat, sonst die Quelle. `null`, solange
 * die Sprachliste unbekannt ist (dann lässt sich weder „Original" noch eine Übersetzung benennen).
 */
export function effectiveLanguage(requested: string | null | undefined, languages: readonly string[]): string | null {
  if (languages.length === 0) return null;
  const r = normLang(requested);
  return r && languages.includes(r) ? r : languages[0];
}

/**
 * Übersetzt oder Original? Die Übersetzungssprache, wenn die wirksame Sprache NICHT die Quelle ist,
 * sonst `''` (= Original). Der Vergleichsschlüssel der Offline-Kopie.
 */
export function translationKey(requested: string | null | undefined, languages: readonly string[]): string {
  const eff = effectiveLanguage(requested, languages);
  return eff && eff !== languages[0] ? eff : '';
}

/** Was eine gespeicherte Offline-Kopie über ihre Sprache weiß (`book-offline.util`). */
export interface OfflineLanguageMeta {
  /** Mit welchem `?lang=` sie geholt wurde; `null` = ohne (alte Kopie oder Original). */
  lang: string | null;
  /** Sprachen, die die Linien der Kopie nannten (Quelle zuerst); leer bei alten Kopien. */
  langs: string[];
}

/**
 * Passt die Offline-Kopie nicht mehr zur Wahl? Verglichen wird „übersetzt in X" gegen „Original"
 * — nicht das nackte Kürzel: eine Kopie, die mit `lang=fr` geholt wurde, obwohl der Kurs kein
 * Französisch hat, IST das Original. Die aktuelle Seite rechnet mit den Sprachen, die sie jetzt kennt
 * (sonst mit denen der Kopie): kam eine Übersetzung nach dem Herunterladen dazu, veraltet die Kopie.
 * Eine alte Kopie ohne Angabe gilt als Original.
 */
export function offlineLanguageStale(
  meta: OfflineLanguageMeta | null | undefined,
  requested: string | null | undefined,
  currentLanguages: readonly string[],
): boolean {
  if (!meta) return false;
  const copyKey = translationKey(meta.lang, meta.langs);
  const known = currentLanguages.length ? currentLanguages : meta.langs;
  return copyKey !== translationKey(requested, known);
}

/**
 * Angezeigter Name: die Übersetzung, wenn es eine gibt, sonst das Original. Kapitel und Titel bleiben
 * überall SCHLÜSSEL (Filter, Routen, Umbenennen, Kapitel-PGN) — nur die ANZEIGE nimmt das Label.
 */
export function labelOr(label: string | null | undefined, original: string | null | undefined): string | null {
  return label && label.trim() ? label : (original ?? null);
}
