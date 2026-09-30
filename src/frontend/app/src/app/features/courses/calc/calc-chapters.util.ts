import { labelOr } from '../course-language.util';
import { maxPoints, sumPoints, sumSeconds } from './calc-review.util';
import type { CalcChapterSummary, CalcPositionListItem } from './calculation.service';

/**
 * Kapitelmodell des Kalkulations-Modus als reine Funktionen: Schlüssel, Gruppen, Einstiegskapitel
 * und Summen. Aus der `CalculationComponent` herausgelöst (Codereview 2026-09-29, F3-001),
 * damit die Regeln ohne Komponente testbar sind — die Komponente hält nur noch den Zustand.
 */

/** Stellungen EINES Kapitels — die Arbeitseinheit dieses Modus, samt der Kapitel-Summen. */
export interface CalcPositionGroup {
  /** Kapitel-SCHLÜSSEL (Original); `null` = ohne Kapitel. */
  chapter: string | null;
  /** Angezeigter Name (Kurs-Übersetzung, sonst das Original); `null` = ohne Kapitel. */
  label?: string | null;
  /**
   * Schlüssel des Kapitels — EXAKT der des Servers ({@link chapterKey}): ordinal über den ROHEN
   * Namen. Gruppenbildung und das Nachschlagen der Server-Summen benutzen ihn gemeinsam, sonst
   * zeigt die Ansicht die Zeilen des einen und die Summe eines anderen Kapitels.
   */
  key: string;
  items: CalcPositionListItem[];
  /** Erreichte Punkte des Kapitels. */
  points: number;
  /** Erreichbare Punkte des Kapitels — jede Summe wird MIT ihrem Maximum genannt. */
  maxPoints: number;
  /** Summe der Rechenzeit des Kapitels (Sekunden). */
  seconds: number;
}

/** Fertige Summen eines Kapitels, wie der Server sie in `chapters[]` liefert. */
export interface CalcChapterSums {
  points: number;
  maxPoints: number;
  seconds: number;
}

/** Eigener Schlüssel für „ohne Kapitel" — ein Name, den es als Kapitelname nicht geben kann
 *  (Spiegel von `CalculationService.SummarizeChapters`). */
export const NO_CHAPTER_KEY = '\u0000';

/**
 * Schlüssel eines Kapitels — GENAU wie serverseitig: ordinal über den ROHEN Namen, nur
 * leer/whitespace zählt als „ohne Kapitel".
 *
 * Die Strenge ist Absicht. Der Server gruppiert in `CalculationService.SummarizeChapters` mit
 * `StringComparer.Ordinal` und liefert die Kapitel-SUMMEN fertig aus (`chapters[]`); die Ansicht
 * schlägt sie hier nach. Faßte der Client zwei Kapitel zusammen, die sich nur in Groß-/Klein-
 * schreibung oder Leerzeichen unterscheiden (bei `PUT /chapters/rename` erlaubt, die Duplikat-
 * Prüfung ist ebenfalls ordinal), zeigte er die Zeilen BEIDER mit der Summe EINER — die Map-
 * Kollision überschreibt still die erste. Der Server ist die Wahrheit für die Summen, der Client
 * richtet sich danach.
 *
 * Nachsichtig verglichen wird bewusst NUR beim Auflösen von `?chapter=` (siehe {@link normChapter}).
 */
export function chapterKey(chapter: string | null | undefined): string {
  return chapter?.trim() ? chapter : NO_CHAPTER_KEY;
}

/**
 * Kapitelnamen nachsichtig vergleichen (getrimmt, ohne Groß-/Kleinschreibung) — AUSSCHLIESSLICH
 * für den Kapitel-Wunsch aus der URL (`?chapter=`, Kurz-URL `/{slug}/{kapitel}`): der Name kann
 * abgetippt sein, ein Link soll trotzdem treffen. Für Gruppen und Summen gilt {@link chapterKey}.
 */
export function normChapter(value: string | null | undefined): string {
  return (value ?? '').trim().toLocaleLowerCase();
}

/** Beschriftung eines Kapitels: das Label der ersten Stellung, die eins trägt, sonst der Name. */
export function chapterGroupLabel(items: readonly CalcPositionListItem[], chapter: string | null): string | null {
  return labelOr(items.find(i => i.chapterLabel)?.chapterLabel, chapter);
}

/**
 * Kapitel EINDEUTIG je Name gruppieren (nicht bloß aufeinanderfolgende Läufe): stünden zwei
 * Blöcke desselben Kapitels in der Liste, gäbe es zwei Kapitel gleichen Namens — mit derselben
 * Server-Summe an beiden. Stellungen ohne Kapitel bilden ihre eigene Gruppe.
 *
 * `numbers` ist die angezeigte Nummer je Stellung (BookPuzzleId → Position IM KAPITEL, ab 1).
 */
export function groupByChapter(positions: readonly CalcPositionListItem[]): {
  groups: CalcPositionGroup[];
  numbers: Map<number, number>;
} {
  const byKey = new Map<string, CalcPositionGroup>();
  const groups: CalcPositionGroup[] = [];
  const numbers = new Map<number, number>();
  for (const p of positions) {
    const chapter = p.chapter?.trim() ? p.chapter : null;
    const key = chapterKey(chapter);
    let group = byKey.get(key);
    if (!group) {
      group = { chapter, label: chapter, key, items: [], points: 0, maxPoints: 0, seconds: 0 };
      byKey.set(key, group);
      groups.push(group);
    }
    group.items.push(p);
    // Nummerierung ist reine ANZEIGE: sie zählt die Stellung IM KAPITEL. `round`/`id` bleiben
    // unangetastet — an ihnen hängen Fortschritt und gespeicherte Bäume.
    numbers.set(p.id, group.items.length);
  }
  // Beschriftung: die Kurs-Übersetzung, wo eine Stellung des Kapitels sie trägt — gruppiert wird
  // weiter über den Original-Schlüssel.
  for (const g of groups) g.label = chapterGroupLabel(g.items, g.chapter);
  return { groups, numbers };
}

/**
 * Welches Kapitel wird beim Öffnen bearbeitet? Reihenfolge: `?chapter=` aus der Kurz-URL
 * (nachsichtig verglichen — der Name kommt aus einer URL, die jemand abgetippt haben kann),
 * sonst das Kapitel der per `?pos=` verlangten Stellung, sonst das erste mit offener Arbeit.
 *
 * Trifft `?chapter=` nichts (Kapitel umbenannt, Tippfehler), wird der Wunsch nicht behauptet:
 * es geht normal weiter, statt eine leere Seite mit fremdem Kapitelnamen zu zeigen.
 */
export function pickChapterIndex(
  groups: readonly CalcPositionGroup[],
  requestedChapter: string | null | undefined,
  requestedPositionId: number | null,
): number {
  const wanted = normChapter(requestedChapter);
  if (wanted) {
    // NACHSICHTIG und nur hier: verglichen wird der Anzeigename, nicht der (strenge)
    // Gruppen-Schlüssel — ein abgetippter Link soll auch bei abweichender Schreibweise treffen.
    // Passen mehrere (etwa „Taktik" neben „taktik"), gewinnt das erste Kapitel des Buchs.
    const hit = groups.findIndex(g => normChapter(g.chapter) === wanted);
    if (hit >= 0) return hit;
  }
  if (requestedPositionId != null) {
    const hit = groups.findIndex(g => g.items.some(p => p.id === requestedPositionId));
    if (hit >= 0) return hit;
  }
  const open = groups.findIndex(g => g.items.some(p => !p.hasTree));
  return open >= 0 ? open : 0;
}

/**
 * Die fertigen Server-Summen je Kapitel-Schlüssel. Schlüssel wie bei den Gruppen (siehe
 * {@link CalcPositionGroup.key}) — sonst findet das Kapitel seine eigene Summe nicht wieder und die
 * Ansicht rechnet still selbst. Und wie beim Server: zwei Kapitel, die sich nur in Schreibweise/
 * Leerzeichen unterscheiden, haben ZWEI Summen — ein nachsichtiger Schlüssel ließe die eine die
 * andere überschreiben.
 */
export function serverChapterSums(chapters: readonly CalcChapterSummary[] | null | undefined): Map<string, CalcChapterSums> {
  const sums = new Map<string, CalcChapterSums>();
  for (const c of chapters ?? []) {
    sums.set(chapterKey(c.chapter), {
      points: c.points ?? 0,
      maxPoints: c.maxPoints ?? 0,
      // `secondsSum`, nicht `secondsSpent`: der Server liefert eine SUMME (siehe
      // CalcChapterSummary). Ein Tippfehler hier fällt nicht auf — die Zeit stünde still auf 0.
      seconds: c.secondsSum ?? 0,
    });
  }
  return sums;
}

/**
 * Kapitel-Summen in die Gruppen schreiben: die Server-Summe, solange es eine gibt, sonst aus den
 * Zeilen gerechnet. Das Maximum hängt nur an der Zahl der Stellungen — der Server darf es liefern,
 * die Ansicht kann es aber jederzeit selbst ausrechnen.
 */
export function applyChapterSums(groups: readonly CalcPositionGroup[], serverSums: ReadonlyMap<string, CalcChapterSums>): void {
  for (const group of groups) {
    const fromServer = serverSums.get(group.key);
    group.points = fromServer ? fromServer.points : sumPoints(group.items);
    group.maxPoints = fromServer?.maxPoints || maxPoints(group.items.length);
    group.seconds = fromServer ? fromServer.seconds : sumSeconds(group.items);
  }
}
