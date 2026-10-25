import { maxPoints, normalizeGrade, sumPoints } from './calc-review.util';
import { CalcBook, CalcPositionListItem } from './calculation.service';
import {
  CalcChapterSums, CalcPositionGroup, applyChapterSums, groupByChapter, serverChapterSums,
} from './calc-chapters.util';

/**
 * Kapitelmodell des Kalkulations-Modus (Zerlegung Z20, aus `CalculationComponent`): alle Stellungen des Buchs, ihre
 * Kapitel-Gruppen, wo man gerade steht (Kapitel + Stellung darin) und die Summen. Reine Logik — Laden, URL und
 * Übersetzung bleiben in der Komponente; die reinen Gruppier-/Summenfunktionen stehen in `calc-chapters.util.ts`.
 *
 * Der Modus ist kapitelweise: Weiter/Zurück, Sprungliste, Zähler und Punkte beziehen sich alle auf EIN Kapitel. Am
 * Kapitelende wird BEWUSST nicht weitergesprungen — sonst mischte die Kapitel-Zeit still über Kapitelgrenzen hinweg.
 */
export class CalcChapterModel {
  /** ALLE Stellungen des Buchs (Quelle der Kapitel-Gruppen) — gearbeitet wird nie auf dieser Liste. */
  positions: CalcPositionListItem[] = [];
  /** Die Kapitel in Lesereihenfolge; „ohne Kapitel" ist eines davon. */
  groups: CalcPositionGroup[] = [];
  /** Welches KAPITEL bearbeitet wird (Index in {@link groups}); -1 = noch keins gewählt. */
  chapterIndex = -1;
  /** Stellung INNERHALB des Kapitels (Index in {@link chapterPositions}). */
  index = 0;

  /** Erreichte/erreichbare Punkte des AKTUELLEN KAPITELS (das ist die Arbeitseinheit). */
  totalPoints = 0;
  totalMaxPoints = 0;
  /** Dieselben Zahlen fürs ganze Buch — dürfen vorkommen, aber nur ausdrücklich beschriftet. */
  bookPoints = 0;
  bookMaxPoints = 0;

  /**
   * Ist der Nutzer am Kapitelende ANGEKOMMEN (durchnavigiert), statt dort einzusteigen? Nur dann
   * darf „Kapitel durchgearbeitet" behauptet werden (siehe {@link atChapterEnd}). Wird beim
   * Betreten eines Kapitels zurückgesetzt und bei jedem Sprung innerhalb des Kapitels nachgeführt.
   */
  private arrivedAtChapterEnd = false;

  /**
   * Angezeigte Nummer je Stellung: ihre Position IM KAPITEL, beginnend bei 1 (BookPuzzleId →
   * Nummer). Bewusst nicht `round`: das ist die buchweit fortlaufende Nummer und hat Lücken,
   * sobald Linien gelöscht wurden („#7" bis „#12" in einem Kapitel mit sechs Stellungen).
   * Reine Anzeige — in der Datenbank wird NICHTS umnummeriert.
   */
  private chapterNumbers = new Map<number, number>();

  /** Server-Summen je Kapitel; nach der ersten eigenen Änderung rechnet die Ansicht selbst. */
  private serverSums = new Map<string, CalcChapterSums>();
  private serverTotals: { points: number; maxPoints: number } | null = null;

  /** Buch übernehmen: Zeilen auffüllen, Kapitel bilden, Server-Summen merken. */
  setBook(book: CalcBook): void {
    this.positions = book.positions.map(p => CalcChapterModel.normalizeItem(p));
    this.groups = this.groupPositions(this.positions);
    this.takeServerSums(book);
  }

  /** Kapitel bilden ({@link groupByChapter}) und die Anzeige-Nummern je Stellung übernehmen. */
  groupPositions(positions: CalcPositionListItem[]): CalcPositionGroup[] {
    const { groups, numbers } = groupByChapter(positions);
    this.chapterNumbers = numbers;
    return groups;
  }

  /** Das gerade bearbeitete Kapitel; `null`, solange nichts geladen ist. */
  get chapter(): CalcPositionGroup | null {
    return this.groups[this.chapterIndex] ?? null;
  }

  /** Nur die Stellungen dieses Kapitels — die einzige Liste, auf der navigiert wird. */
  get chapterPositions(): CalcPositionListItem[] {
    return this.chapter?.items ?? [];
  }

  get hasNextChapter(): boolean { return this.chapterIndex >= 0 && this.chapterIndex < this.groups.length - 1; }

  get nextChapter(): CalcPositionGroup | null { return this.groups[this.chapterIndex + 1] ?? null; }

  /**
   * Reine ORTSANGABE: die angezeigte Stellung ist die letzte des Kapitels. Daran hängt der WEG ins
   * nächste Kapitel — der darf immer offen stehen, auch wenn hier gerade erst eingestiegen wurde.
   */
  get atLastPosition(): boolean {
    const items = this.chapterPositions;
    return items.length > 0 && this.index >= items.length - 1;
  }

  /**
   * „Kapitel durchgearbeitet" — das ist eine BEHAUPTUNG, und die muss stimmen. Sie gilt erst, wenn
   * man am Ende ANGEKOMMEN ist (siehe {@link arrivedAtChapterEnd}), nicht wenn man dort startet:
   * am Index allein hängend meldete ein Kapitel mit genau EINER Stellung ab der ersten Sekunde
   * „durch", ebenso ein Deep-Link (`?pos=`) auf die letzte Stellung.
   */
  get atChapterEnd(): boolean {
    return this.atLastPosition && this.arrivedAtChapterEnd;
  }

  hasPrev(): boolean { return this.index > 0; }
  hasNext(): boolean { return this.index < this.chapterPositions.length - 1; }

  /** Bearbeitete Stellungen des KAPITELS (alle Zahlen dieser Ansicht meinen das Kapitel). */
  get doneCount(): number { return this.chapterPositions.filter(p => p.hasTree).length; }

  /**
   * Kapitel betreten und die Einstiegsstellung bestimmen (Deep-Link, sonst erste unbearbeitete). Liefert deren
   * BookPuzzleId, oder null bei einem leeren Kapitel.
   */
  enter(chapterIndex: number, requestedPositionId: number | null): number | null {
    this.chapterIndex = chapterIndex;
    // Einsteigen ist kein Durcharbeiten — auch nicht, wenn der Einstieg auf der letzten Stellung
    // liegt (Deep-Link, Kapitel mit einer einzigen Stellung).
    this.arrivedAtChapterEnd = false;
    this.refreshSums();
    const items = this.chapterPositions;
    if (items.length === 0) return null;
    const wanted = requestedPositionId != null ? items.findIndex(p => p.id === requestedPositionId) : -1;
    const firstOpen = items.findIndex(p => !p.hasTree);
    this.index = wanted >= 0 ? wanted : (firstOpen >= 0 ? firstOpen : 0);
    return items[this.index].id;
  }

  /** Innerhalb des Kapitels navigieren — nur so entsteht „am Ende angekommen" (und geht beim Zurückgehen wieder
   *  verloren). Liefert die BookPuzzleId der Stellung. */
  moveTo(index: number): number {
    this.index = index;
    this.arrivedAtChapterEnd = this.atLastPosition;
    return this.chapterPositions[index].id;
  }

  /** In welchem Kapitel steht diese Stellung? (-1 = in keinem) */
  chapterOf(bookPuzzleId: number): number {
    return this.groups.findIndex(g => g.items.some(p => p.id === bookPuzzleId));
  }

  /**
   * Die ANGEZEIGTE Nummer einer Stellung: ihre Position im Kapitel (1, 2, 3 …). Fällt auf
   * `round` zurück, solange die Gruppen noch nicht stehen — `round`/`id` selbst bleiben
   * unverändert, sie sind die Identität der Linie.
   */
  chapterNumberOf(item: CalcPositionListItem): string {
    const number = this.chapterNumbers.get(item.id);
    return number ? String(number) : item.round;
  }

  /** Sprunglisten-Zeile als bearbeitet/unbearbeitet markieren. */
  markDone(bookPuzzleId: number, done: boolean): void {
    const item = this.positions.find(p => p.id === bookPuzzleId);
    if (item) item.hasTree = done;
  }

  /**
   * Kapitel-Summen + Buchsumme neu setzen (Server-Werte, solange vorhanden).
   *
   * Die angezeigte Gesamtsumme ist die des KAPITELS: der Nutzer steht in einem Kapitel, eine Zahl
   * über das ganze Buch gehörte zu nichts Sichtbarem („19 / 48" unter sechs Stellungen). Sie kommt
   * fertig aus `chapters[]` — dieselbe serverseitige Wahrheit im richtigen Zuschnitt, KEIN zweiter
   * Rechenweg im Client. Die Buchsumme steht daneben, aber ausdrücklich als solche beschriftet.
   */
  refreshSums(): void {
    applyChapterSums(this.groups, this.serverSums);
    const chapter = this.chapter;
    this.totalPoints = chapter ? chapter.points : 0;
    this.totalMaxPoints = chapter ? chapter.maxPoints : 0;
    this.bookPoints = this.serverTotals ? this.serverTotals.points : sumPoints(this.positions);
    this.bookMaxPoints = this.serverTotals?.maxPoints || maxPoints(this.positions.length);
  }

  /** Nach einer eigenen Änderung sind die Server-Summen überholt — ab jetzt selbst rechnen. */
  dropServerSums(): void {
    this.serverSums.clear();
    this.serverTotals = null;
    this.refreshSums();
  }

  /**
   * Summen: solange der Nutzer nichts geändert hat, gelten die FERTIGEN Server-Summen. Sobald er
   * bewertet, rechnet die Ansicht aus den Zeilen nach — sonst stünden Kapitel- und Kurssumme bis
   * zum nächsten Laden auf dem alten Stand.
   */
  private takeServerSums(book: CalcBook): void {
    this.serverSums = serverChapterSums(book.chapters);
    // Buchsumme, falls der Server eine mitliefert (sonst rechnet refreshSums sie selbst).
    this.serverTotals = typeof book.points === 'number'
      ? { points: book.points, maxPoints: book.maxPoints ?? maxPoints(this.positions.length) }
      : null;
    this.refreshSums();
  }

  /** Fehlende Felder eines älteren/knapperen Server-Standes auffüllen (nie `undefined` anzeigen). */
  private static normalizeItem(item: CalcPositionListItem): CalcPositionListItem {
    item.chosenSan = item.chosenSan ?? null;
    item.chosenUci = item.chosenUci ?? null;
    item.secondsSpent = item.secondsSpent ?? 0;
    item.grade = normalizeGrade(item.grade);
    return item;
  }
}
