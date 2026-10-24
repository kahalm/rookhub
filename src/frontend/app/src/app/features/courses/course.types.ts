import { BookPuzzleDto } from '../puzzles/puzzle.service';

// Typen der Kurs-Schnittstelle (`/api/courses`). Die Zugriffe stehen nach Thema in course-api.ts,
// course-authoring-api.ts, course-share-api.ts und course-translation-api.ts; course.service.ts ist die Fassade.

export type CourseMode = 'sequential' | 'random';

export interface CourseListItem {
  bookId: number;
  fileName: string;
  displayName: string;
  difficulty: string | null;
  rating: number | null;
  tags: string | null;
  description: string | null;
  puzzleCount: number;
  /** UI-Sweep 2026-10-11 (r2-rp-empty-course): Info-/Partie-Linien — nur bei Kursen OHNE Quiz-Linie gefüllt. */
  infoLineCount?: number;
  solvedCount: number;
  progressPercent: number;
  lastMode: string | null;
  /** ISO-Zeitstempel der letzten Verwendung (CourseProgress.UpdatedAt); null = noch nie angefangen. */
  lastActivityAt: string | null;
  /** Showstopper: veraltete Aufbereitung, die sich hier NICHT automatisch beheben lässt — die Karte
   *  zeigt dafür ein (!). Hilft nur: den Kurs über die RepCheck-Erweiterung neu holen. */
  needsReimport?: boolean;
  /** true = eigener Kurs (Chessable-Import, selbst angelegt, PGN-Import oder aus Repertoire); false = über eine Gruppe freigegebener öffentlicher Kurs. */
  isOwned: boolean;
  /** true = vom Nutzer fürs Dashboard angepinnt (persönlich). */
  isPinned: boolean;
  /** true = dieser Kurs wurde von einem anderen Nutzer mit mir geteilt (ich bin nicht der Besitzer). */
  isShared?: boolean;
  /** Benutzername des Teilenden (nur wenn isShared) — für das „von X"-Badge. */
  sharedByUsername?: string | null;
  /** Verknüpfter Partner-Kurs (Buch↔Workbook) für den Schnellwechsel; null = keine Verknüpfung. */
  linkedBookId?: number | null;
  /** Anzeigename des verknüpften Kurses (nur wenn linkedBookId gesetzt). */
  linkedDisplayName?: string | null;
  /** Themen-Tags des Buchs (Keys opening/middlegame/endgame/tactics/other); leer/unset = ["tactics"].
   *  Steuern die Themen-Aufschlüsselung der Kurszeit im Trainingsfortschritt. */
  themes?: string[];
  /** true = Kalkulationsbuch (Stellungen ohne Lösung): Karte bietet statt sequenziell/zufällig den
   *  Kalkulations-Modus an; puzzleCount/solvedCount = alle bzw. bearbeitete Stellungen. */
  isCalculation?: boolean;
  /** Erreichte Punkte der Selbstbewertung im Kalkulations-Modus; null/fehlend = kein
   *  Kalkulationsbuch. 0 heißt „bewertet, aber keine Punkte" — deshalb nicht auf 0 zurückfallen. */
  calcPoints?: number | null;
  /** Erreichbare Punkte (4 je Stellung). Fehlt der Wert, rechnet die Karte ihn aus `puzzleCount`. */
  calcMaxPoints?: number | null;
  /** UI-Sweep 2026-10-11 (r2-rp-course-dupes): Chessable-Import (Dateiname/Tag) — Herkunft gleichnamiger Kurse. */
  isChessableImport?: boolean;
  /** Besitzer des persönlichen Buchs — nur für Admins gefüllt (Server). */
  ownerUsername?: string | null;
}

/** Pro-Linien-Bearbeitungsstatus eines Kurs-Buchs (für die „Linien durchsehen"-Ansicht):
 *  gelöste (✓) und versucht-aber-nicht-gelöste (✗) Linien-Ids des Users. */
export interface CourseLineStatus {
  solvedIds: number[];
  failedIds: number[];
}

/** Auflösung einer öffentlichen Kurz-URL `/{slug}` (siehe {@link CourseService.resolvePublicSlug}). */
export interface PublicSlugTarget {
  bookId: number;
  /** Kalkulationsbuch → Kalkulations-Modus statt Solver. Fehlt das Feld, gilt `false`. */
  isCalculation?: boolean;
}

/** Auflösung einer Kapitel-Kurz-URL `/{slug}/{kapitel}`. */
export interface PublicSlugChapterTarget extends PublicSlugTarget {
  /** Kanonischer Kapitelname, wie ihn der Server führt (die URL kann anders geschrieben sein). */
  chapter: string;
  /** Index in der SOLVER-Kapitelliste (nur Quiz-Kapitel); `null` = dort nicht startbar. */
  chapterIndex: number | null;
}

/** Der verknüpfte Partner-Kurs (leere Felder = keine Verknüpfung). */
export interface CourseLink {
  linkedBookId: number | null;
  linkedDisplayName: string | null;
}

/** Ergebnis eines Teilen-Vorgangs (Batch). */
export interface CourseShareResult {
  shared: number;
  skipped: { userId: number; reason: string }[];
}

/** Ein Nutzer, mit dem ein Kurs aktuell geteilt ist. */
export interface CourseShareRecipient {
  userId: number;
  username: string;
  displayName: string | null;
  sharedAt: string;
}

export interface CourseChapter {
  index: number;
  /** null = Sammelgruppe „ohne Kapitel". */
  name: string | null;
  puzzleCount: number;
  solvedCount: number;
  progressPercent: number;
  /** Anzahl Info-/Erklärlinien im Kapitel (nicht in puzzleCount enthalten); in der Übersicht in Klammern. */
  infoCount: number;
  /** Übersetzter Kapitelname (nur mit `?lang=`); `null` = keine. `name` bleibt der Schlüssel — angezeigt
   *  wird `label ?? name`. */
  label?: string | null;
}

/** Statistik eines Kurs-Bereichs (ganzes Buch ODER aktuelles Kapitel): Fortschritt + Zeit + Erst-Versuch-Trefferquote.
 *  Zeit/Trefferquote zählen nur Versuche seit dem letzten Reset. */
/** Wo eine Kurslinie im E-Book steht (Diagramm + Seite). */
export interface CourseLineEbookRef {
  lineId: number;
  ebookId: number;
  ebookTitle: string;
  diagramId: number;
  page: number;
  label: string | null;
}

/** Ergebnis von „Mit E-Book verknüpfen“; `ebookId` null = kein passendes Buch gefunden. */
export interface CourseEbookLinkResult {
  ebookId: number | null;
  ebookTitle: string | null;
  linked: number;
  lines: number;
}

export interface CourseScopeStats {
  solvedCount: number;
  total: number;
  progressPercent: number;
  /** Akkumulierte Zeit über alle Versuche (seit letztem Reset), Sekunden. */
  totalSeconds: number;
  /** Puzzles mit mindestens einem Versuch (seit Reset). */
  attemptedCount: number;
  /** Davon beim ERSTEN Versuch korrekt. */
  firstTryCorrect: number;
  /** 0–100: firstTryCorrect / attemptedCount. */
  accuracyPercent: number;
  /** Gelöst ohne Hilfe (erster Versuch richtig, ohne Tipp); cleanCount + helpedCount = solvedCount. */
  cleanCount?: number;
  /** Gelöst mit Tipp oder nach Fehlversuch/Neustart. */
  helpedCount?: number;
  /** Versucht, aber nicht gelöst (aufgegeben). */
  gaveUpCount?: number;
}

export interface CourseNextPuzzle {
  puzzle: BookPuzzleDto | null;
  solvedCount: number;
  total: number;
  completed: boolean;
  /** Statistik fürs ganze Buch. */
  book?: CourseScopeStats | null;
  /** Statistik fürs Kapitel des aktuellen Puzzles; null = Buch hat nur ein Kapitel / kein aktuelles Puzzle. */
  chapter?: CourseScopeStats | null;
  chapterName?: string | null;
}

export interface CourseProgress {
  bookId: number;
  solvedCount: number;
  total: number;
  progressPercent: number;
  completed: boolean;
  lastMode: string | null;
  book?: CourseScopeStats | null;
  chapter?: CourseScopeStats | null;
  chapterName?: string | null;
}

/**
 * Kapitel in der VERWALTUNGSSICHT der Detailseite — anders als {@link CourseChapter} sind hier ALLE
 * Kapitel enthalten, auch solche, die nur aus Stellungs-/Info-Linien bestehen (die Kalkulationsbücher).
 * Adressiert wird über `name`, nicht über einen Index (der verschiebt sich beim Anlegen).
 */
export interface CourseManageChapter {
  /** null = Sammelgruppe „ohne Kapitel". */
  name: string | null;
  /** Alle Linien des Kapitels. */
  lineCount: number;
  /** Davon abgefragte Quiz-Linien (mit Lösung). */
  quizCount: number;
  solvedCount: number;
  progressPercent: number;
  /** Index in der Solver-Kapitelliste; null = im Solver nicht startbar (keine Quiz-Linien). */
  solverIndex: number | null;
  /** Erste Linie des Kapitels — Einstieg für den Kalkulations-Modus. */
  firstLineId: number | null;
  /** Übersetzter Kapitelname (nur mit `?lang=`); `null` = keine. `name` bleibt der Schlüssel (Umbenennen,
   *  Löschen, Kapitel-PGN) — angezeigt wird `label ?? name`. */
  label?: string | null;
}

/** Vollbild der Kurs-Detailseite. */
export interface CourseDetail {
  bookId: number;
  fileName: string;
  displayName: string;
  description: string | null;
  difficulty: string | null;
  rating: number | null;
  minElo: number | null;
  maxElo: number | null;
  tags: string | null;
  themes: string[];
  kind: 'Puzzle' | 'Study';
  isCalculation: boolean;
  isPublic: boolean;
  publicSlug: string | null;
  isOwned: boolean;
  isShared: boolean;
  sharedByUsername: string | null;
  isPinned: boolean;
  /** Darf der Aufrufer Kapitel/Linien bearbeiten (Besitzer oder Admin)? */
  canManage: boolean;
  puzzleCount: number;
  solvedCount: number;
  progressPercent: number;
  totalLines: number;
  infoLineCount: number;
  /** Erreichte Punkte der Selbstbewertung im Kalkulations-Modus (nur bei Kalkulationsbüchern
   *  belegt; `null` = kein Kalkulationsbuch, 0 = bewertet ohne Punkte). */
  calcPoints?: number | null;
  /** Erreichbare Punkte (4 je Stellung); fehlt der Wert, wird er aus `puzzleCount` gerechnet. */
  calcMaxPoints?: number | null;
  lastMode: string | null;
  /** Persönlich: Info-Linien im sequenziellen Durchlauf überspringen. */
  skipInfoLines?: boolean;
  lastActivityAt: string | null;
  linkedBookId: number | null;
  linkedDisplayName: string | null;
  chapters: CourseManageChapter[];
  createdAt: string;
  updatedAt: string;
}

/** Eine Linie in der Verwaltungssicht (ohne Zugfolge — die Detailseite verrät keine Lösung). */
export interface CourseLine {
  id: number;
  lineId: string;
  round: string;
  title: string | null;
  chapter: string | null;
  fen: string;
  comment: string | null;
  isInfoOnly: boolean;
  /** Halbzüge der gespeicherten Linie (0 = reine Stellung). */
  moveCount: number;
}

/** Eine beim Einfügen verworfene Zeile (Grund invalid_fen/duplicate/too_many). */
export interface CourseLineIssue {
  lineNumber: number;
  text: string;
  reason: string;
}

export interface AddCourseLinesResult {
  added: number;
  chapter: string | null;
  issues: CourseLineIssue[];
  totalLines: number;
}

/** Status der Aufbereitungs-Versionierung (Kurse/Repertoires) — Basis für den „Aktualisieren (N)"-Knopf. */
export interface ReprocessStatus {
  currentVersion: number;
  total: number;
  stale: number;
  reprocessableLocally: number;
  refetchable: number;
  needsReimport: number;
}

/**
 * Ergebnis eines Reprocess-Laufs — SPIEGEL des Server-DTOs, aber KEIN Antwort-Vertrag:
 * `POST /api/courses/reprocess` startet den Lauf im Hintergrund und antwortet mit
 * `202 { started: true }`. Die Zahlen hier landen also nur im Server-Log; der Typ
 * beschreibt die Struktur für den Fall, dass der Lauf später synchron auswertbar wird.
 * Wer auf `failed` reagieren will, braucht vorher einen Endpoint, der das ausliefert.
 */
export interface ReprocessResult {
  reprocessed: number;
  updatedLines: number;
  enqueued: number;
  /** Nichts zu tun (keine Quelle, Re-Fetch-Backoff, Dedup). */
  skipped: number;
  /** Mit Fehler abgebrochen — der Datensatz bleibt veraltet (siehe Server-Log). */
  failed: number;
}

/** Eine Sprache, in der es für den Kurs Übersetzungen gibt (`GET /api/courses/{id}/translations`). */
export interface CourseTranslationLanguage {
  language: string;
  /** Linien mit einem Satz in dieser Sprache (auch wenn einzelne Stellen inzwischen veraltet sind). */
  linesTranslated: number;
  /** Linien mit übersetzbarem Text. */
  linesTotal: number;
}

export type CourseTranslationStatus = 'queued' | 'running' | 'done' | 'failed' | 'cancelled';

/** Ein Übersetzungsauftrag (offen oder kürzlich erledigt). */
export interface CourseTranslationJob {
  id: number;
  bookId: number;
  language: string;
  status: CourseTranslationStatus;
  linesTotal: number;
  linesDone: number;
  linesFailed: number;
  /** Von der Automatik angelegt (de/en für alle Kurse, Nachziehen nach dem Aktualisieren). */
  automatic: boolean;
  requestedByMe: boolean;
  /** Platz in der Warteschlange (1 = der nächste), nur bei `queued`. */
  queuePosition?: number | null;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  lastError?: string | null;
}

/** Der offene Auftrag des Aufrufers — irgendein Kurs (für den Hinweis beim Nutzer-Limit). */
export interface CourseTranslationMyJob {
  jobId: number;
  bookId: number;
  bookName: string;
  language: string;
  status: CourseTranslationStatus;
}

/** `GET /api/courses/{id}/translations` — Stufe B (0.548.0). */
export interface CourseTranslations {
  /** Quellsprache der Kommentare (`und` = nicht bestimmbar). */
  sourceLanguage: string | null;
  languages: CourseTranslationLanguage[];
  /** Offene Aufträge (laufender zuerst, dann nach Platz), dahinter die jüngsten erledigten. */
  jobs: CourseTranslationJob[];
  /** Bis wann die Spark gerade anderen gehört (`null` = frei) — Aufträge werden trotzdem angenommen. */
  quietUntil?: string | null;
  myOpenJob?: CourseTranslationMyJob | null;
  /** Es ist ein Text-Modell konfiguriert. */
  available: boolean;
  /** Der Aufrufer darf JETZT anfordern (angemeldet, Modell da, kein eigener offener Auftrag bzw. Admin). */
  canRequest: boolean;
}
